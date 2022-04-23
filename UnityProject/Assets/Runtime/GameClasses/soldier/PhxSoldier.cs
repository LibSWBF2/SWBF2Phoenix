using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Animations;
using LibSWBF2.Utils;
using System.Runtime.ExceptionServices;

public class PhxSoldier : PhxControlableInstance<PhxSoldier.ClassProperties>, ICraAnimated, IPhxTickable
{
    static PhxGame Game => PhxGame.Instance;
    static PhxMatch Match => PhxGame.GetMatch();
    static PhxScene Scene => PhxGame.GetScene();
    static PhxCamera Camera => PhxGame.GetCamera();


    public class ClassProperties : PhxClass
    {
        public PhxProp<Texture2D> MapTexture = new PhxProp<Texture2D>(null);
        public PhxProp<float> MapScale = new PhxProp<float>(1.0f);
        public PhxProp<float> MapViewMin = new PhxProp<float>(1.0f);
        public PhxProp<float> MapViewMax = new PhxProp<float>(1.0f);
        public PhxProp<float> MapSpeedMin = new PhxProp<float>(1.0f);
        public PhxProp<float> MapSpeedMax = new PhxProp<float>(1.0f);

        public PhxProp<string> HealthType = new PhxProp<string>("person");
        public PhxProp<float>  MaxHealth = new PhxProp<float>(100.0f);

        // Are these two the same? If so, which one has precedence?
        public PhxProp<string> AnimationName = new PhxProp<string>("human");
        public PhxProp<string> SkeletonName = new PhxProp<string>("human");

        public PhxProp<float> MaxSpeed = new PhxProp<float>(1.0f);
        public PhxProp<float> MaxStrafeSpeed = new PhxProp<float>(1.0f);
        public PhxProp<float> MaxTurnSpeed = new PhxProp<float>(1.0f);
        public PhxProp<float> JumpHeight = new PhxProp<float>(1.0f);
        public PhxProp<float> JumpForwardSpeedFactor = new PhxProp<float>(1.0f);
        public PhxProp<float> JumpStrafeSpeedFactor = new PhxProp<float>(1.0f);
        public PhxProp<float> RollSpeedFactor = new PhxProp<float>(1.0f);
        public PhxProp<float> Acceleration = new PhxProp<float>(1.0f);
        public PhxProp<float> SprintAccelerateTime = new PhxProp<float>(1.0f);

        public PhxMultiProp ControlSpeed = new PhxMultiProp(typeof(string), typeof(float), typeof(float), typeof(float));

        public PhxProp<float> EnergyBar = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyRestore = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyRestoreIdle = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyDrainSprint = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyMinSprint = new PhxProp<float>(1.0f);
        public PhxProp<float> EnergyCostJump = new PhxProp<float>(0.0f);
        public PhxProp<float> EnergyCostRoll = new PhxProp<float>(1.0f);

        public PhxProp<float> AimValue = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorPostureSpecial = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorPostureStand = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorPostureCrouch = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorPostureProne = new PhxProp<float>(1.0f);
        public PhxProp<float> AimFactorStrafe = new PhxProp<float>(0.0f);
        public PhxProp<float> AimFactorMove = new PhxProp<float>(1.0f);

        public PhxPropertySection Weapons = new PhxPropertySection(
            "WEAPONSECTION",
            ("WeaponName",    new PhxProp<string>(null)),
            ("WeaponAmmo",    new PhxProp<int>(0)),
            ("WeaponChannel", new PhxProp<int>(0))
        );

        public PhxProp<string> AISizeType = new PhxProp<string>("SOLDIER");
    }

    // See: com_inf_default.odf
    Dictionary<string, PhxAnimPosture> ControlToPosture = new Dictionary<string, PhxAnimPosture>()
    {
        { "stand", PhxAnimPosture.Stand },
        { "crouch", PhxAnimPosture.Crouch },
        { "prone", PhxAnimPosture.Prone },
        { "sprint", PhxAnimPosture.Sprint },
        { "jet", PhxAnimPosture.Jet },
        { "jump", PhxAnimPosture.Jump },
        { "roll", PhxAnimPosture.Roll },
        { "tumble", PhxAnimPosture.Tumble },
    };

    enum PhxSoldierContext
    {
        Free,
        Pilot,
    }

    public PhxProp<float> CurHealth = new PhxProp<float>(100.0f);


    PhxSoldierContext Context = PhxSoldierContext.Free;
    PhxAnimPosture PreviousPosture = PhxAnimPosture.Stand;

    // Vehicle related fields
    PhxSeat CurrentSeat;

    PhxPoser Poser;

    PhxAnimHuman Animator;
    Rigidbody Body;
    CapsuleCollider MovementColl;

    // Important skeleton bones
    Transform DummyRoot;
    Transform HpWeapons;
    Transform Spine;
    Transform Neck;

    // Physical raycast downwards
    bool Grounded;
    int GroundedLayerMask;

    // How long to still be alerted after the last fire / hit
    const float AlertTime = 3f;
    float AlertTimer;

    // Minimum time not grounded, after which we're considered falling
    const float FallTime = 0.2f;

    // How long we're already falling
    float FallTimer;

    // Minimum time we're considered falling when jumping
    const float JumpTime = 0.2f;
    float JumpTimer;

    // When > 0, we're currently landing
    float LandTimer;

    // Time it takes to turn left/right when idle (not walking)
    const float TurnTime = 0.2f;
    float TurnTimer;
    Quaternion TurnStart;

    Quaternion BodyTargetRotation; 
    Vector3 CurrrentVelocity;

    // Settings for stairs/steps/slopes
    const float MaxStepHeight = 0.31f;
    static readonly Vector3 StepCheckOffset = new Vector3(0f, MaxStepHeight + 0.1f,  0.4f);
    const float StepUpForceMulti = 20.0f;

    bool IsFixated => Body == null;
    bool HasCombo = false;
    int PreviousSwingSound = 0;

    // <stance>, <thrustfactor> <strafefactor> <turnfactor>
    Dictionary<PhxAnimPosture, float[]> ControlValues = new Dictionary<PhxAnimPosture, float[]>();

    // First array index is whether:
    // - 0 : Primary Weapon
    // - 1 : Secondary Weapon
    IPhxWeapon[][] Weapons = new IPhxWeapon[2][];
    int[] WeaponIdx = new int[2] { -1, -1 };


    public override void Init()
    {
        gameObject.layer = LayerMask.NameToLayer("SoldierAll");

        AimConstraint.x = 45f;

        // TODO: base turn speed in degreees/sec really 45?
        MaxTurnSpeed.y = 45f * C.MaxTurnSpeed;

        foreach (var cs in ControlToPosture)
        {
            ControlValues.Add(cs.Value, GetControlSpeed(cs.Key));
        }

        DummyRoot = transform.GetChild(0);
        Debug.Assert(DummyRoot != null);
        
        HpWeapons = PhxUtils.FindTransformRecursive(transform, "hp_weapons");
        Neck = PhxUtils.FindTransformRecursive(transform, "bone_neck");
        Spine = PhxUtils.FindTransformRecursive(transform, "bone_b_spine");
        Debug.Assert(HpWeapons != null);
        Debug.Assert(Spine != null);

        Body = gameObject.AddComponent<Rigidbody>();
        Body.mass = 80f;
        Body.drag = 0f;
        Body.angularDrag = 1000f;
        Body.interpolation = RigidbodyInterpolation.None;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;

        MovementColl = gameObject.AddComponent<CapsuleCollider>();
        MovementColl.height = 1.8f;
        MovementColl.radius = 0.3f;
        MovementColl.center = new Vector3(0f, 0.9f, 0f);

        // Idk whether there's a better method for this, but haven't found any
        GroundedLayerMask = 0;
        for (int i = 0; i < 32; ++i)
        {
            GroundedLayerMask |= Physics.GetIgnoreLayerCollision(i, gameObject.layer) ? 0 : 1 << i;
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Weapons
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        var weapons = new List<IPhxWeapon>[2]
        {
            new List<IPhxWeapon>(),
            new List<IPhxWeapon>()
        };

        HashSet<PhxAnimWeapon> weaponAnimBanks = new HashSet<PhxAnimWeapon>();

        foreach (Dictionary<string, IPhxPropRef> section in C.Weapons)
        {
            int channel = 0;
            if (section.TryGetValue("WeaponChannel", out IPhxPropRef chVal))
            {
                PhxProp<int> weapCh = (PhxProp<int>)chVal;
                channel = weapCh;
            }
            Debug.Assert(channel >= 0 && channel < 2);

            if (section.TryGetValue("WeaponName", out IPhxPropRef nameVal))
            {
                PhxProp<string> weapCh = (PhxProp<string>)nameVal;
                PhxClass weapClass = Scene.GetClass(weapCh);
                if (weapClass != null)
                {
                    PhxProp<int> medalProp = weapClass.P.Get<PhxProp<int>>("MedalsTypeToUnlock");
                    if (medalProp != null && medalProp != 0)
                    {
                        // Skip medal/award weapons for now
                        continue;
                    }

                    IPhxWeapon weap = Scene.CreateInstance(weapClass, false, HpWeapons) as IPhxWeapon;
                    if (weap != null)
                    {
                        weap.SetIgnoredColliders(new List<Collider>() {gameObject.GetComponent<CapsuleCollider>()});

                        PhxAnimWeapon weapAnim = weap.GetAnimInfo();
                        if (!string.IsNullOrEmpty(weapAnim.AnimationBank) && !weaponAnimBanks.Contains(weapAnim))
                        {
                            if (!string.IsNullOrEmpty(weapAnim.Combo))
                            {
                                HasCombo = true;
                            }
                            weaponAnimBanks.Add(weapAnim);
                        }

                        weapons[channel].Add(weap);

                        // init weapon as inactive
                        weap.GetInstance().gameObject.SetActive(false);
                        weap.OnShot(() => FireAnimation(channel == 0));
                        weap.OnReload(Reload);
                    }
                    else
                    {
                        Debug.LogWarning($"Instantiation of weapon class '{weapCh}' failed!");
                    }
                }
                else
                {
                    Debug.LogWarning($"Cannot find weapon class '{weapCh}'!");
                }
            }

            // TODO: weapon ammo
        }

        Weapons[0] = weapons[0].Count == 0 ? new IPhxWeapon[1] { null } : weapons[0].ToArray();
        Weapons[1] = weapons[1].Count == 0 ? new IPhxWeapon[1] { null } : weapons[1].ToArray();


        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        // Animation
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        PhxAnimWeapon[] weapAnimBanks = new PhxAnimWeapon[weaponAnimBanks.Count];
        weaponAnimBanks.CopyTo(weapAnimBanks);

        string characterAnim = C.AnimationName;
        if (characterAnim.ToLower() == "human" && C.SkeletonName.Get().ToLower() != "human")
        {
            characterAnim = C.SkeletonName;
        }

        //LookRotation = Body.rotation;
        Animator = new PhxAnimHuman(Scene.AnimResolver, transform, characterAnim, weapAnimBanks);

        // Assume we're grounded on spawn
        Grounded = true;
        Animator.InGrounded.SetBool(true);

        // this needs to happen after the Animator is initialized, since swicthing
        // will weapons will most likely cause an animation bank change aswell
        NextWeapon(0);
        //NextWeapon(1);
    }

    public override void Destroy()
    {
        
    }

    public override void Fixate()
    {
        Destroy(Body);
        Body = null;
        Grounded = true;
        Animator.InGrounded.SetBool(true);

        Destroy(GetComponent<CapsuleCollider>());
    }

    public override IPhxWeapon GetPrimaryWeapon()
    {
        return Weapons[0][WeaponIdx[0]];
    }

    public void AddHealth(float amount)
    {
        if (amount < 0)
        {
            // we got hit! alert!
            AlertTimer = AlertTime;
        }

        float health = CurHealth + amount;
        if (health <= 0f)
        {
            health = 0;
            // TODO: dead!
        }
        CurHealth.Set(Mathf.Min(health, C.MaxHealth));
    }

    public void AddAmmo(float amount)
    {
        // TODO
    }

    public void NextWeapon(int channel)
    {
        Debug.Assert(channel >= 0 && channel < 2);

        if (WeaponIdx[channel] >= 0 && Weapons[channel][WeaponIdx[channel]] != null)
        {
            Weapons[channel][WeaponIdx[channel]].GetInstance().gameObject.SetActive(false);
        }
        if (++WeaponIdx[channel] >= Weapons[channel].Length)
        {
            WeaponIdx[channel] = 0;
        }
        if (Weapons[channel][WeaponIdx[channel]] != null)
        {
            Weapons[channel][WeaponIdx[channel]].GetInstance().gameObject.SetActive(true);
            PhxAnimWeapon info = Weapons[channel][WeaponIdx[channel]].GetAnimInfo();
            Animator.SetActiveWeaponBank(info.AnimationBank);
        }
        else
        {
            Debug.LogWarning($"Encountered NULL weapon at channel {channel} and weapon index {WeaponIdx[channel]}!");
        }
    }

    public override void PlayIntroAnim()
    {
        Animator.InPressedEvents.SetInt(Animator.InPressedEvents.GetInt() | (int)PhxInput.Soldier_Reload);
    }

    void FireAnimation(bool primary)
    {
        //Animator.InShootPrimary.SetBool(true);
    }

    void Reload()
    {
        //IPhxWeapon weap = Weapons[0][WeaponIdx[0]];
        //if (weap != null)
        //{
        //    Animator.Anim.SetState(1, Animator.StandReload);
        //    Animator.Anim.RestartState(1);
        //    float animTime = Animator.Anim.GetCurrentState(1).GetDuration();
        //    Animator.Anim.SetPlaybackSpeed(1, Animator.StandReload, 1f / (weap.GetReloadTime() / animTime));
        //}
    }

    // Undoes SetPilot; called by vehicles/turrets when ejecting soldiers
    public void SetFree(Vector3 position)
    {
        Context = PhxSoldierContext.Free;
        CurrentSeat = null;

        Body = gameObject.AddComponent<Rigidbody>();
        Body.mass = 80f;
        Body.drag = 0f;
        Body.angularDrag = 1000f;
        Body.interpolation = RigidbodyInterpolation.None;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        GetComponent<SkinnedMeshRenderer>().enabled = true;
        GetComponent<CapsuleCollider>().enabled = true;

        transform.parent = null;
        transform.position = position;

        Poser = null;

        if (WeaponIdx[0] >= 0 && Weapons[0][WeaponIdx[0]] != null)
        {
            Weapons[0][WeaponIdx[0]].GetInstance().gameObject.SetActive(true);
        }
    }


    public void SetPilot(PhxSeat section)
    {
        Context = PhxSoldierContext.Pilot;
        CurrentSeat = section;

        if (Body != null)
        {
            Destroy(Body);
            Body = null;
        }

        GetComponent<CapsuleCollider>().enabled = false;


        if (section.PilotPosition == null)
        {
            GetComponent<SkinnedMeshRenderer>().enabled = false;
        }
        else
        {
            GetComponent<SkinnedMeshRenderer>().enabled = true;

            transform.parent = section.PilotPosition;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            if (CurrentSeat.PilotAnimationType != PilotAnimationType.None)
            {
                bool isStatic = CurrentSeat.PilotAnimationType == PilotAnimationType.StaticPose;
                string animName = isStatic ? CurrentSeat.PilotAnimation : CurrentSeat.Pilot9Pose;
                
                Poser = new PhxPoser("human_4", "human_" + animName, transform, isStatic);   
            }    
        }

        if (WeaponIdx[0] >= 0 && Weapons[0][WeaponIdx[0]] != null)
        {
            Weapons[0][WeaponIdx[0]].GetInstance().gameObject.SetActive(false);
        }
    }


    // see: com_inf_default
    float[] GetControlSpeed(string controlName)
    {
        foreach (object[] values in C.ControlSpeed.Values)
        {
            if (!string.IsNullOrEmpty(controlName) && controlName == (string)values[0])
            {
                return new float[3]
                {
                    (float)values[1],
                    (float)values[2],
                    (float)values[3],
                };
            }
        }
        Debug.LogError($"Cannot find control state '{controlName}'!");
        return null;
    }

    public void Tick(float deltaTime)
    {
        Profiler.BeginSample("Tick Soldier");
        UpdateState(deltaTime);
        Profiler.EndSample();
    }


    void UpdatePose(float deltaTime)
    {
        var data = Controller.GetControlData();

        // TODO: fill the last two parameters with sensible values again
        Vector4 Input = new Vector4(data.Move.x, data.Move.y, 0f, 0f);

        if (Poser != null && CurrentSeat != null)
        {
            float blend = 2f * deltaTime;

            if (CurrentSeat.PilotAnimationType == PilotAnimationType.NinePose)
            {
                if (Vector4.Magnitude(Input) < .001f)
                {
                    Poser.SetState(PhxNinePoseState.Idle, blend);
                    return;
                }

                if (Input.x > .01f)
                {
                    Poser.SetState(PhxNinePoseState.StrafeRight, blend);           
                }

                if (Input.x < -.01f)
                {
                    Poser.SetState(PhxNinePoseState.StrafeLeft, blend);            
                }

                if (Input.y < 0f) 
                {
                    if (Input.z > .01f)
                    {
                        Poser.SetState(PhxNinePoseState.BackwardsTurnLeft, blend);            
                    }
                    else if (Input.z < -.01f)
                    {
                        Poser.SetState(PhxNinePoseState.BackwardsTurnRight, blend);            
                    }
                    else
                    {
                        Poser.SetState(PhxNinePoseState.Backwards, blend);            
                    }
                }
                else
                {
                    if (Input.z > .01f)
                    {
                        Poser.SetState(PhxNinePoseState.ForwardTurnLeft, blend);            
                    }
                    else if (Input.z < -.01f)
                    {
                        Poser.SetState(PhxNinePoseState.ForwardTurnRight, blend);            
                    }
                    else
                    {
                        Poser.SetState(PhxNinePoseState.Forward, blend);            
                    }
                }
            }
            else if (CurrentSeat.PilotAnimationType == PilotAnimationType.FivePose)
            {
                if (Mathf.Abs(Input.z) + Mathf.Abs(Input.w) < .001f)
                {
                    Poser.SetState(PhxFivePoseState.Idle, blend);
                    return;
                }

                if (Input.z > .01f)
                {
                Poser.SetState(PhxFivePoseState.TurnRight, blend);            
                }
                else
                {
                    Poser.SetState(PhxFivePoseState.TurnLeft, blend);            
                }

                if (Input.w > .01f)
                {
                    Poser.SetState(PhxFivePoseState.TurnDown, blend);            
                }
                else
                {
                    Poser.SetState(PhxFivePoseState.TurnUp, blend);            
                }
            }
            else if (CurrentSeat.PilotAnimationType == PilotAnimationType.StaticPose)
            {
                Poser.SetState();
            }
            else 
            {
                // Not sure what happens if PilotPosition is defined but PilotAnimation/Pilot9Pose are missing...
            }
        }
    }

    void UpdateState(float deltaTime)
    {
        if (Context == PhxSoldierContext.Pilot && Controller != null)
        {
            //Animator.SetActive(false);
            UpdatePose(deltaTime);
            return;
        }

        //AnimationCorrection();

        AlertTimer = Mathf.Max(AlertTimer - deltaTime, 0f);

        if (Controller == null)
        {
            return;
        }

        var data = Controller.GetControlData();

        // Will work into specific control schema.  Not sure when you can't enter vehicles...
        if (data.Events.IsPressed(PhxInput.Soldier_Enter) && Context == PhxSoldierContext.Free)
        {
            PhxVehicle ClosestVehicle = null;
            float ClosestDist = float.MaxValue;
            
            Collider[] PossibleVehicles = Physics.OverlapSphere(transform.position, 5.0f, ~0, QueryTriggerInteraction.Collide);
            foreach (Collider PossibleVehicle in PossibleVehicles)
            {
                GameObject CollidedObj = PossibleVehicle.gameObject;
                if (PossibleVehicle.attachedRigidbody != null)
                {
                    CollidedObj = PossibleVehicle.attachedRigidbody.gameObject;
                }

                PhxVehicle Vehicle = CollidedObj.GetComponent<PhxVehicle>();
                if (Vehicle != null && Vehicle.HasAvailableSeat())
                {
                    float Dist = Vector3.Magnitude(transform.position - Vehicle.transform.position);
                    if (Dist < ClosestDist)
                    {
                        ClosestDist = Dist;
                        ClosestVehicle = Vehicle;
                    }
                }
            }

            if (ClosestVehicle != null)
            {
                CurrentSeat = ClosestVehicle.TryEnterVehicle(this);
                if (CurrentSeat != null)
                {
                    SetPilot(CurrentSeat);
                    return;
                }
            }
        }

        CraState  stateLower  = Animator.LayerLower.GetActiveState();
        CraState  stateUpper  = Animator.LayerLower.GetActiveState();
        CraPlayer playerLower = stateLower.GetPlayer();
        CraPlayer playerUpper = stateUpper.GetPlayer();
        CraClip   clipLower   = playerLower.GetClip();
        CraClip   clipUpper   = playerUpper.GetClip();

        PhxAnimPosture posture = (PhxAnimPosture)Animator.OutPosture.GetInt();
        PhxAnimAction  action  = (PhxAnimAction)Animator.OutAction.GetInt();
        PhxInput       locked  = (PhxInput)Animator.OutInputLocks.GetInt();
        PhxAimType     aimType = (PhxAimType)Animator.OutAimType.GetInt();

        // Button events can be queried directly, without being affected by locks
        Animator.InDownEvents.SetInt((int)data.Events.Down);
        Animator.InChangedEvents.SetInt((int)data.Events.Changed);
        Animator.InPressedEvents.SetInt((int)data.Events.Pressed);
        Animator.InReleasedEvents.SetInt((int)data.Events.Released);
        Animator.InTabEvents.SetInt((int)data.Events.Tab);
        Animator.InHoldEvents.SetInt((int)data.Events.Hold);

        float lockDuration = Animator.OutInputLockDuration.GetFloat();
        if (lockDuration <= 0f || (playerUpper.GetTime() < lockDuration))
        {
            data.Events.Down &= ~(locked);
            data.Events.Changed &= ~(locked);
            data.Events.Pressed &= ~(locked);
            data.Events.Released &= ~(locked);
            data.Events.Tab &= ~(locked);
            data.Events.Hold &= ~(locked);
        }

        // Determine Action AFTER applying input locks!
        PhxAnimAction inAction = PhxAnimAction.None;
        if ((data.Events.Down & PhxInput.Soldier_FirePrimary) != 0)
        {
            inAction = PhxAnimAction.ShootPrimary;
        }
        else if ((data.Events.Pressed & PhxInput.Soldier_Reload) != 0)
        {
            inAction = PhxAnimAction.Reload;
        }
        else if ((data.Events.Down & PhxInput.Soldier_Crouch) != 0)
        {
            inAction = PhxAnimAction.Crouch;
        }
        else if ((data.Events.Pressed & PhxInput.Soldier_Roll) != 0)
        {
            inAction = PhxAnimAction.Roll;
        }
        else if ((data.Events.Pressed & PhxInput.Soldier_Jump) != 0)
        {
            inAction = PhxAnimAction.Jump;
        }
        else if ((data.Events.Down & PhxInput.Soldier_Sprint) != 0)
        {
            inAction = PhxAnimAction.Sprint;

            Debug.Log($"lockDuration: {lockDuration}   playerUpper: {playerUpper.GetTime()}");
        }

        float moveX = (locked & PhxInput.Soldier_Thrust) != 0 ? 0f : data.Move.x;
        float moveY = (locked & PhxInput.Soldier_Thrust) != 0 ? 0f : data.Move.y;

        Animator.InThrustX.SetFloat(moveX);
        Animator.InThrustY.SetFloat(moveY);
        Animator.InAction.SetInt((int)inAction);

        Animator.InEnergy.SetFloat(100.0f);
        Animator.InGrounded.SetBool(Grounded);


        if (IsFixated)
        {
            //transform.rotation = lookRot;
            return;
        }

        Grounded = posture != PhxAnimPosture.Jump ? Physics.OverlapSphere(Body.position, 0.2f, GroundedLayerMask).Length > 1 : false;

        if (data.Events.IsPressed(PhxInput.Soldier_NextPrimaryWeapon))
        {
            NextWeapon(0);
        }
        //if (Controller.NextSecondaryWeapon)
        //{
        //    NextWeapon(1);
        //}

        if (HasCombo)
        {
            int swingSound = Animator.OutSound.GetInt();
            if (PreviousSwingSound != Animator.OutSound.GetInt())
            {
                PhxMelee melee = Weapons[0][WeaponIdx[0]] as PhxMelee;
                if (melee != null)
                {
                    melee.PlaySwingSound((uint)swingSound);
                }
                PreviousSwingSound = swingSound;
            }

            CraPlayer player = Animator.LayerUpper.GetActiveState().GetPlayer();
            if (player.IsValid())
            {
                float time = player.GetTime();
                for (int i = 0; i < Animator.OutAttacks.Length; ++i)
                {
                    var output = Animator.OutAttacks[i];
                    if (output.AttackID.GetInt() >= 0)
                    {
                        float timeStart = output.AttackDamageTimeStart.GetFloat();
                        float timeEnd = output.AttackDamageTimeEnd.GetFloat();
                        PhxAnimTimeMode mode = (PhxAnimTimeMode)output.AttackDamageTimeMode.GetInt();
                        if (mode == PhxAnimTimeMode.Frames)
                        {
                            // Battlefront Frames (30 FPS) to time
                            timeStart /= 30f;
                            timeEnd /= 30f;
                        }
                        else if (mode == PhxAnimTimeMode.FromAnim)
                        {
                            timeStart *= player.GetClip().GetDuration();
                            timeEnd *= player.GetClip().GetDuration();
                        }

                        if (time >= timeStart && time <= timeEnd)
                        {
                            PhxMelee melee = Weapons[0][WeaponIdx[0]] as PhxMelee;
                            if (melee == null)
                            {
                                Debug.LogError("Tried to perform melee attack with no melee weapon in hand!");
                                continue;
                            }

                            int edge = output.AttackEdge.GetInt();
                            if (edge >= melee.C.LightSabers.Sections.Length)
                            {
                                Debug.LogError($"Tried to perform melee attack on edge {edge} with just {melee.C.LightSabers.Sections.Length} edges present!");
                                continue;
                            }

                            var section = melee.C.LightSabers.Sections[edge];
                            PhxProp<float> lengthProp = section["LightSaberLength"] as PhxProp<float>;
                            PhxProp<float> widthProp = section["LightSaberWidth"] as PhxProp<float>;
                            Debug.Assert(lengthProp != null);
                            Debug.Assert(widthProp != null);

                            float length = output.AttackDamageLength.GetFloat();
                            if (output.AttackDamageLengthFromEdge.GetBool())
                            {
                                length *= lengthProp;
                            }

                            float width = output.AttackDamageWidth.GetFloat();
                            if (output.AttackDamageLengthFromEdge.GetBool())
                            {
                                width *= widthProp;
                            }

                            Transform edgeTransform = melee.GetEdge(edge);
                            Debug.Assert(edgeTransform != null);

                            Vector3 edgeFrom = edgeTransform.position;
                            Vector3 edgeTo = edgeFrom + edgeTransform.forward * length;

                            int mask = 0;
                            mask |= 1 << LayerMask.NameToLayer("SoldierAll");
                            mask |= 1 << LayerMask.NameToLayer("VehicleAll");
                            mask |= 1 << LayerMask.NameToLayer("BuildingAll");
                            Collider[] hits = Physics.OverlapCapsule(edgeFrom, edgeTo, width, mask, QueryTriggerInteraction.Ignore);
                            for (int hi = 0; hi < hits.Length; ++hi)
                            {
                                if (hits[hi] == MovementColl)
                                {
                                    continue;
                                }

                                float damage = output.AttackDamage.GetFloat();
                                Debug.Log($"Deal {damage} Damage to {hits[hi].gameObject.name}!");
                            }
                        }
                    }
                }
            }
        }

        // ODF doesn't know about fall and land, it's all jump
        PhxAnimPosture minPosture = posture;
        switch(minPosture)
        {
            case PhxAnimPosture.Fall:
            case PhxAnimPosture.Land:
                minPosture = PhxAnimPosture.Jump;
                break;
        }

        float thrustVelocity = C.MaxSpeed;
        float strafeVelocity = C.MaxStrafeSpeed;

        float accStep = C.Acceleration * deltaTime;
        float thrustFactor = ControlValues[minPosture][0];
        float strafeFactor = ControlValues[minPosture][1];
        float turnFactor = ControlValues[minPosture][2];

        // I hate this special case of Roll...
        if (posture != PhxAnimPosture.Roll)
        {
            PreviousPosture = posture;
        }
        else
        {
            Debug.Assert(PreviousPosture != PhxAnimPosture.Roll);
            Debug.Assert(PreviousPosture != PhxAnimPosture.Jump);
            Debug.Assert(PreviousPosture != PhxAnimPosture.Fall);
            Debug.Assert(PreviousPosture != PhxAnimPosture.Land);
            thrustFactor = ControlValues[PreviousPosture][0];
            strafeFactor = ControlValues[PreviousPosture][1];
            turnFactor = ControlValues[PreviousPosture][2];
        }

        if (Animator.IsComboState(stateLower) && Animator.IsComboState(stateUpper) && Animator.OutAnimatedMove.GetBool())
        {
            thrustVelocity = Animator.OutVelocityFromThrust.GetFloat();
            strafeVelocity = Animator.OutVelocityFromStrafe.GetFloat();
        }

        {
            float maxDegrees = 45f * C.MaxTurnSpeed;

            Vector3 viewRotationEuler = AimRotation.eulerAngles;
            viewRotationEuler.x += Mathf.Min(data.ViewDelta.y * turnFactor, maxDegrees);
            viewRotationEuler.y += Mathf.Min(data.ViewDelta.x * turnFactor, maxDegrees);

            PhxUtils.SanitizeEuler180(ref viewRotationEuler);
            viewRotationEuler.x = Mathf.Clamp(viewRotationEuler.x, -AimConstraint.x, AimConstraint.x);
            viewRotationEuler.y = Mathf.Clamp(viewRotationEuler.y, -AimConstraint.y, AimConstraint.y);

            AimRotation = Quaternion.Euler(viewRotationEuler);
        }

        Vector3 moveDirLocal = new Vector3(moveX, 0f, moveY);
        Vector3 moveDirWorld = AimRotation * moveDirLocal;
        bool bInvertDirection = Animator.OutStrafeBackwards.GetBool() || moveY < 0f;

        float t = Mathf.Clamp01(moveDirLocal.z);
        float forwardFactor = Mathf.Lerp(strafeFactor, thrustFactor, t);
        float maxSpeed = Mathf.Lerp(strafeVelocity, thrustVelocity, t);
        float walk = Mathf.Clamp01(moveDirLocal.magnitude);
        if (walk < 0.01f)
        {
            walk = 0f;
        }

        float signedWalk = walk;
        if (moveY < 0f)
        {
            signedWalk = -signedWalk;
        }

        Quaternion bodyRotation = Body.rotation;
        if (aimType == PhxAimType.FullBody)
        {
            bodyRotation = AimRotation;
        }
        else if (walk > 0f)
        {
            bodyRotation = AimRotation * Quaternion.LookRotation(moveDirLocal);
        }

        if (Animator.OutAnimatedMove.GetBool())
        {
            Vector3 overrideVelocity = Quaternion.Inverse(BodyTargetRotation) * Body.velocity;
            if (minPosture != PhxAnimPosture.Jump)
            {
                overrideVelocity = new Vector3(Animator.OutVelocityX.GetFloat(), 0f, Animator.OutVelocityZ.GetFloat());
                if (Animator.OutVelocityXFromAnim.GetBool() || Animator.OutVelocityZFromAnim.GetBool())
                {
                    PhxTransform rootMotionDelta = PhxAnimLoader.GetRootMotionDelta(clipLower, 0f, clipLower.GetDuration());
                    if (Animator.OutVelocityXFromAnim.GetBool())
                    {
                        overrideVelocity.x = rootMotionDelta.Position.x * Animator.OutVelocityX.GetFloat();
                    }
                    if (Animator.OutVelocityZFromAnim.GetBool())
                    {
                        overrideVelocity.z = rootMotionDelta.Position.z * Animator.OutVelocityZ.GetFloat();
                    }
                }
            }

            // I hate this special case of Roll...
            Vector3 thrustAdd = Vector3.forward * maxSpeed * forwardFactor * signedWalk;
            if (posture == PhxAnimPosture.Roll && bInvertDirection)
            {
                overrideVelocity -= thrustAdd;
            }
            else
            {
                overrideVelocity += thrustAdd;
            }

            CurrrentVelocity = BodyTargetRotation * overrideVelocity;
            //Debug.Log($"ANIMATED MOVE    {CurrrentVelocity}");
        }
        else if (posture == PhxAnimPosture.Land)
        {
            CurrrentVelocity = Vector3.zero;
            //Debug.Log($"LAND    {CurrrentVelocity}");
        }
        else if (minPosture == PhxAnimPosture.Jump)
        {
            Vector3 addVelocity = Vector3.forward * maxSpeed * forwardFactor * signedWalk;
            CurrrentVelocity += Body.rotation * addVelocity * deltaTime;
            //Debug.Log($"JUMP/FALL    {CurrrentVelocity}");
        }
        else if (walk > 0f)
        {
            CurrrentVelocity += moveDirWorld * accStep;
            CurrrentVelocity = Vector3.ClampMagnitude(CurrrentVelocity, maxSpeed * forwardFactor * walk);
            //Debug.Log($"WALK    {CurrrentVelocity}");
        }
        else if (accStep > 0f)
        {
            CurrrentVelocity -= CurrrentVelocity / accStep;
            //Debug.Log($"STAND    {CurrrentVelocity}");
        }


        Animator.InThrustMagnitude.SetFloat(walk);

        float thrustAngle = Mathf.Atan2(-moveX, moveY) * Mathf.Rad2Deg;
        thrustAngle = PhxUtils.SanitizeEuler360(thrustAngle);
        Animator.InThrustAngle.SetFloat(thrustAngle);

        if (aimType != PhxAimType.FullBody && posture != PhxAnimPosture.Roll)
        {
            Vector3 bodyRotationEuler = bodyRotation.eulerAngles;
            bodyRotationEuler.x = 0f;
            bodyRotationEuler.z = 0f;
            if (bInvertDirection)
            {
                // invert look direction when strafing left/right backwards
                bodyRotationEuler.y += 180f;
            }
            BodyTargetRotation = Quaternion.Euler(bodyRotationEuler);
        }

        Vector3 planeVelocity = Body.velocity;
        planeVelocity.y = 0f;
        if (planeVelocity.magnitude < 0.01f)
        {
            planeVelocity = Vector3.zero;
        }

        // Handle Animation playback speed for movement states
        if (posture == PhxAnimPosture.Stand || posture == PhxAnimPosture.Crouch || posture == PhxAnimPosture.Prone || posture == PhxAnimPosture.Sprint)
        {
            PhxTransform rootMotionDelta = PhxAnimLoader.GetRootMotionDelta(clipLower, 0f, clipLower.GetDuration());      
            if (rootMotionDelta.Position.magnitude > 0f)
            {
                float rootMotionVelocity = rootMotionDelta.Position.magnitude / clipLower.GetDuration();
                float playSpeed = planeVelocity.magnitude / rootMotionVelocity;

                // TODO: use Cra's PlaybackSpeedInput?
                playerLower.SetPlaybackSpeed(Animator.IsMovementState(stateLower) ? playSpeed : 1f);
                playerUpper.SetPlaybackSpeed(Animator.IsMovementState(stateUpper) ? playSpeed : 1f);

                //Debug.Log($"Root Motion Delta: {rootMotionDelta.Position}");
                //Debug.Log($"Root Motion Anim Speed: {playSpeed}");
            }
            else
            {
                playerLower.SetPlaybackSpeed(1f);
                playerUpper.SetPlaybackSpeed(1f);
                //Debug.Log("No Root Motion");
            }
        }

        if (inAction == PhxAnimAction.Roll)
        {
            Debug.Log($"Roll Invert: {bInvertDirection}");
        }

        if (inAction == PhxAnimAction.Roll && bInvertDirection)
        {
            BodyTargetRotation *= Quaternion.Euler(new Vector3(0f, 180f, 0f));
        }
        Body.MoveRotation(Quaternion.Slerp(Body.rotation, BodyTargetRotation, deltaTime * 5f));


        Animator.InLandHardness.SetInt(FallTimer < 1.5f ? 1 : 2);
        CurrrentVelocity.y = Body.velocity.y;
        Body.velocity = CurrrentVelocity;

         Animator.InWorldVelocity.SetFloat(Body.velocity.magnitude);   
        Animator.InMoveVelocity.SetFloat(planeVelocity.magnitude);

        if (inAction == PhxAnimAction.Jump)
        {
            if (Grounded)
            {
                FallTimer = 0f;
            }
            Body.AddForce(Vector3.up * Mathf.Sqrt(C.JumpHeight * -2f * Physics.gravity.y), ForceMode.VelocityChange);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (Body != null)
        {
            Gizmos.DrawSphere(Body.position, 0.2f);
        }
    }

    public Vector3 RotAlt1 = new Vector3(7f, -78f, -130f);
    public Vector3 RotAlt2 = new Vector3(0f, -50f, -75f);
    public Vector3 RotAlt3 = new Vector3(0f, -68f, -81f);
    public Vector3 RotAlt4 = new Vector3(0f, -53f, -77f);

    void AnimationCorrection()
    {
        if (Context == PhxSoldierContext.Pilot) return;

        if (Controller == null/* || FallTimer > 0f || TurnTimer > 0f*/)
        {
            return;
        }

        PhxAnimPosture posture = (PhxAnimPosture)Animator.OutPosture.GetInt();
        if (posture == PhxAnimPosture.Stand || posture == PhxAnimPosture.Crouch)
        {
            //if (Animator.Anim.GetCurrentStateIdx(1) == Animator.StandShootPrimary)
            //{
            //    Spine.rotation = Quaternion.LookRotation(Controller.ViewDirection) * Quaternion.Euler(RotAlt4);
            //}
            //else if (AlertTimer > 0f)
            //{
            //    if (Controller.MoveDirection.magnitude > 0.1f)
            //    {
            //        Spine.rotation = Quaternion.LookRotation(Controller.ViewDirection) * Quaternion.Euler(RotAlt3);
            //    }
            //    else
            //    {
            //        Spine.rotation = Quaternion.LookRotation(Controller.ViewDirection) * Quaternion.Euler(RotAlt2);
            //    }
            //}
            //else if (Neck != null)
            //{
            //    Neck.rotation = Quaternion.LookRotation(Controller.ViewDirection) * Quaternion.Euler(RotAlt1);
            //}
        }
    }

    public CraStateMachine GetStateMachine()
    {
        return Animator.Machine;
    }
}
