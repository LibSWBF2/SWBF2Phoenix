using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;
using LibSWBF2.Wrappers;

public class PhxCommandpost : PhxInstance<PhxCommandpost.ClassProperties>, IPhxTickable
{
    PhxMatch Match => PhxGame.GetMatch();
    PhxScene Scene => PhxGame.GetScene();

    public class ClassProperties : PhxClass
    {
        public PhxProp<float> NeutralizeTime = new PhxProp<float>(1.0f);
        public PhxProp<float> CaptureTime    = new PhxProp<float>(1.0f);
        public PhxProp<float> HoloTurnOnTime = new PhxProp<float>(1.0f);
        public PhxMultiProp   ChargeSound    = new PhxMultiProp(typeof(AudioClip), typeof(string));
        public PhxMultiProp   CapturedSound  = new PhxMultiProp(typeof(AudioClip), typeof(string));
        public PhxMultiProp   DischargeSound = new PhxMultiProp(typeof(AudioClip), typeof(string));
        public PhxMultiProp   LostSound      = new PhxMultiProp(typeof(AudioClip), typeof(string));

        public PhxMultiProp HoloImageGeometry = new PhxMultiProp(typeof(string), typeof(string));
        public PhxProp<PhxClass> HoloOdf      = new PhxProp<PhxClass>(null);

        public PhxProp<Texture2D> MapTexture = new PhxProp<Texture2D>(null);
        public PhxProp<float>     MapScale   = new PhxProp<float>(1.0f);
    }

    // SWBF Instance Properties
    public PhxProp<PhxRegion> CaptureRegion = new PhxProp<PhxRegion>(null);
    public PhxProp<PhxRegion> ControlRegion = new PhxProp<PhxRegion>(null);
    public PhxProp<SWBFPath>  SpawnPath     = new PhxProp<SWBFPath>(null);

    [Header("References")]
    public LineRenderer HoloRay;
    public PhxHoloIcon HoloIcon;
    public HDAdditionalLightData Light;

    [Header("Settings")]
    public Vector2 CapturePitch = new Vector2(0.5f, 1.5f);
    public float   HoloPresenceSpeed = 1.0f;


    public int   CaptureTeam { get; private set; }
    public bool  CaptureDisputed { get; private set; }
    public bool  CaptureToNeutral { get; private set; }

    public float CaptureTimer;
    int   CaptureCount;
    AudioSource AudioAction;
    AudioSource AudioAmbient;
    AudioSource AudioCapture;
    HashSet<PhxPawnController> CaptureControllers = new HashSet<PhxPawnController>();

    // cache
    bool  bInitInstance => C != null;
    float HoloWidthStart;
    float HoloWidthEnd;
    float LightIntensity;
    Color HoloColor;
    float HoloAlpha;
    float HoloPresence = 1.0f;
    float HoloPresenceDest = 1.0f;
    float HoloPresenceVel;
    float LastHoloPresence;


    public override void Init()
    {
        Transform hpHolo = transform.Find(string.Format("{0}/hp_hologram", C.Name));
        if (hpHolo != null)
        {
            GameObject holoPrefab = Resources.Load<GameObject>("cp_holo");
            GameObject holo = Instantiate(holoPrefab, hpHolo);
            HoloRay = holo.GetComponent<LineRenderer>();
            Light = holo.GetComponentInChildren<HDAdditionalLightData>();

            HoloWidthStart = HoloRay.startWidth;
            HoloWidthEnd = HoloRay.endWidth;
            HoloAlpha = HoloRay.material.GetColor("_UnlitColor").a;
            LightIntensity = Light.intensity;
        }

        AudioAmbient = gameObject.AddComponent<AudioSource>();
        AudioAmbient.spatialBlend = 1.0f;
        AudioAmbient.clip = SoundLoader.Instance.LoadSound("com_blg_commandpost2");
        AudioAmbient.pitch = 1.0f;
        AudioAmbient.volume = 0.5f;
        AudioAmbient.rolloffMode = AudioRolloffMode.Linear;
        AudioAmbient.minDistance = 2.0f;
        AudioAmbient.maxDistance = 30.0f;
        AudioAmbient.Play();

        AudioCapture = gameObject.AddComponent<AudioSource>();
        AudioCapture.spatialBlend = 1.0f;
        AudioCapture.loop = true;
        AudioCapture.pitch = 1.0f;
        AudioCapture.volume = 0.8f;
        AudioCapture.rolloffMode = AudioRolloffMode.Linear;
        AudioCapture.minDistance = 2.0f;
        AudioCapture.maxDistance = 30.0f;

        AudioAction = gameObject.AddComponent<AudioSource>();
        AudioAction.spatialBlend = 1.0f;
        AudioAction.loop = false;
        AudioAction.pitch = 1.1f;
        AudioAction.volume = 0.5f;
        AudioAction.rolloffMode = AudioRolloffMode.Linear;
        AudioAction.minDistance = 2.0f;
        AudioAction.maxDistance = 30.0f;


        Team.OnValueChanged += ApplyTeam;
        CaptureRegion.OnValueChanged += (PhxRegion oldRegion) =>
        {
            if (oldRegion != null)
            {
                oldRegion.OnEnter -= AddToCapture;
                oldRegion.OnLeave -= RemoveFromCapture;
            }

            PhxRegion newRegion = CaptureRegion.Get();
            if (newRegion != null)
            {
                newRegion.OnEnter += AddToCapture;
                newRegion.OnLeave += RemoveFromCapture;
            }
        };

        if (C.HoloOdf.Get() != null)
        {
            HoloIcon = (PhxHoloIcon)Scene.CreateInstance(C.HoloOdf, $"{name}_{C.HoloOdf.Get().Name}", new Vector3(0.0f, 4.7f, 0.0f), Quaternion.identity, false, hpHolo != null ? hpHolo : gameObject.transform);
        }
    }
    
    public override void Destroy()
    {
        
    }

    public float GetCaptureProgress()
    {
        return CaptureTimer / C.CaptureTime;
    }    

    public void RemoveFromCapture(IPhxControlableInstance other)
    {
        PhxPawnController controller = other.GetController();
        if (controller != null)
        {
            CaptureControllers.Remove(controller);
            controller.CapturePost = null;
            RefreshCapture();
        }
    }

    public void UpdateColor()
    {
        HoloColor = PhxGame.GetMatch().GetTeamColor(Team);
        HoloRay?.material.SetColor("_EmissiveColor", HoloColor);
        if (Light != null)
        {
            Light.color = HoloColor;
        }
    }

    public void Tick(float deltaTime)
    {
        if (!bInitInstance) return;

        if (CaptureCount > 0)
        {
            if (CaptureDisputed)
            {
                // TODO: play dispute sound
            }
            else
            {
                float progress;
                float captureMultiplier = Mathf.Sqrt(CaptureCount);
                CaptureTimer += deltaTime * captureMultiplier;
                if (Team == 0)
                {
                    if (C.CaptureTime - CaptureTimer <= HoloPresenceSpeed * captureMultiplier * 2f)
                    {
                        HoloPresenceDest = 0.0f;
                    }

                    progress = CaptureTimer / C.CaptureTime;
                    if (CaptureTimer >= C.CaptureTime)
                    {
                        Team.Set(CaptureTeam);
                        progress = 0.0f;
                    }

                    CaptureToNeutral = false;
                    AudioCapture.pitch = Mathf.Lerp(CapturePitch.x, CapturePitch.y, progress);
                }
                else if (CaptureTeam != Team)
                {
                    if (C.NeutralizeTime - CaptureTimer <= HoloPresenceSpeed * captureMultiplier * 2f)
                    {
                        HoloPresenceDest = 0.0f;
                    }

                    progress = CaptureTimer / C.NeutralizeTime;
                    if (CaptureTimer >= C.NeutralizeTime)
                    {
                        Team.Set(0);
                        progress = 0.0f;
                    }

                    CaptureToNeutral = true;
                    AudioCapture.pitch = Mathf.Lerp(CapturePitch.y, CapturePitch.x, progress);
                }
            }
        }
        else
        {
            HoloPresenceDest = 1.0f;
            CaptureTimer = Mathf.Max(CaptureTimer - deltaTime * 0.1f, 0.0f);
            AudioCapture.pitch = 0.0f;
        }

        HoloPresence = Mathf.SmoothDamp(HoloPresence, HoloPresenceDest, ref HoloPresenceVel, HoloPresenceSpeed);
        //HoloPresence = Mathf.Lerp(HoloPresence, HoloPresenceDest, deltaTime * HoloPresenceSpeed);
        if (HoloPresence != LastHoloPresence)
        {
            HoloColor.a = Mathf.Lerp(0.0f, HoloAlpha, HoloPresence);
            if (Light != null)
            {
                Light.intensity = Mathf.Lerp(0.0f, LightIntensity, HoloPresence);
            }
            if (HoloRay != null)
            {
                HoloRay.startWidth = Mathf.Lerp(0.0f, HoloWidthStart, HoloPresence);
                HoloRay.endWidth   = Mathf.Lerp(0.0f, HoloWidthEnd, HoloPresence);
                HoloRay.material.SetColor("_UnlitColor", HoloColor);
            }
            LastHoloPresence = HoloPresence;
        }
    }

    void AddToCapture(IPhxControlableInstance other)
    {
        PhxPawnController controller = other.GetController();
        if (controller != null)
        {
            CaptureControllers.Add(controller);
            controller.CapturePost = this;
            RefreshCapture();
        }
    }

    void RefreshCapture()
    {
        CaptureTeam = 0;
        CaptureDisputed = false;
        ushort[] teamCounts = new ushort[255];

        foreach (PhxPawnController controller in CaptureControllers)
        {
            if (++teamCounts[controller.Team] > teamCounts[CaptureTeam])
            {
                CaptureTeam = controller.Team;
            }

            if (CaptureTeam > 0)
            {
                CaptureDisputed = CaptureDisputed ||
                    (controller.Team != CaptureTeam && !Match.IsFriend(controller.Team, CaptureTeam));
            }
        }

        CaptureCount = teamCounts[CaptureTeam];
    }

    public void ChangeIcon()
    {
        if (CaptureToNeutral) {
            if (HoloIcon != null)
                HoloIcon.Hide();
            return; 
        }
        if (Match.Teams[Team].Hologram == null) { LoadIcon(ref Match.Teams[Team].Hologram); }

        HoloIcon?.LoadIcon(Match.GetTeamHologram(Team), Team);

        if (HoloIcon != null)
            HoloIcon.Show();
    }

    public void ChangeColorIcon()
    {
        if (CaptureToNeutral) {
            if(HoloIcon!=null)
                HoloIcon.Hide();
            return; 
        }
        if (Match.Teams[Team].Hologram == null) { LoadIcon(ref Match.Teams[Team].Hologram); }

        HoloIcon?.ChangeColorIcon(Team);

        if(HoloIcon!=null)
            HoloIcon.Show();
    }

    private void LoadIcon(ref GameObject icon)
    {
        string name = Match.getTeamName(Team); //Odf use full name but teams only 3 first chars
        if (name.Equals("imp")) { name = "emp"; } //Do not know how to solve better atm

        for (int i = 0; i < C.HoloImageGeometry.GetCount(); i++)
        {
            if (name.Equals(C.HoloImageGeometry.Get<string>(1, i).Substring(0, 3).ToLower()))
            {
                icon = ModelLoader.Instance.GetGameObjectFromModel(C.HoloImageGeometry.Get<string>(0, i), "");
            }
        }

        //To be destroy and be invisible
        //Vector3 scale = new Vector3(0, 0, 0);
        if (icon != null)
        {
            icon.transform.localScale = new Vector3(0, 0, 0);
            icon.transform.parent = gameObject.transform; //Maybe has to be changed now
        }
    }

    void ApplyTeam(int oldTeam)
    {
        if (Team == oldTeam)
        {
            // nothing to do
            return;
        }

        CaptureTimer = 0.0f;

        AudioCapture.clip = Team == 0 ? C.ChargeSound.Get<AudioClip>(0) : C.DischargeSound.Get<AudioClip>(0);
        AudioCapture.Play();

        AudioAmbient.loop = true;
        AudioAction.clip = Team == 0 ? C.LostSound.Get<AudioClip>(0) : C.CapturedSound.Get<AudioClip>(0);
        AudioAction.Play();

        HoloPresence = 0.0f;
        HoloPresenceDest = 1.0f;

        if (Team == 0)
        {
            PhxLuaEvents.Invoke(PhxLuaEvents.Event.OnFinishNeutralize, Scene.GetInstanceIndex(this));
        }
        else
        {
            PhxLuaEvents.Invoke(PhxLuaEvents.Event.OnFinishCapture, Scene.GetInstanceIndex(this));
            PhxLuaEvents.Invoke(PhxLuaEvents.Event.OnFinishCaptureName, name, Scene.GetInstanceIndex(this));
            PhxLuaEvents.Invoke(PhxLuaEvents.Event.OnFinishCaptureTeam, Team.Get(), Scene.GetInstanceIndex(this));
        }

        RefreshCapture();
        UpdateColor();
        ChangeIcon();
    }

    void OnDrawGizmos()
    {
        SWBFPath path = SpawnPath.Get();
        if(path != null)
        {
            Gizmos.color = Color.green;
            for(int i = 1; i < path.Nodes.Length; i++)
            {
                SWBFPath.Node nodePrevious = path.Nodes[i - 1];
                SWBFPath.Node nodeCurrent = path.Nodes[i];
                Gizmos.DrawLine(nodePrevious.Position, nodeCurrent.Position);
            }
        }
    }

}
