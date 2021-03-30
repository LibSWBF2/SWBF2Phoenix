using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using LibSWBF2.Utils;

public class GC_soldier : ISWBFInstance<GC_soldier.ClassProperties>, ISWBFSelectableCharacter
{
    static GameRuntime GAME => GameRuntime.Instance;
    static GameMatch MTC => GameRuntime.GetMatch();

    static readonly string ANIM_CONTROLLER_NAME = "AnimController_soldier";

    public class ClassProperties : ISWBFClass
    {
        public Prop<Texture2D> MapTexture = new Prop<Texture2D>(null);
        public Prop<float> MapScale = new Prop<float>(1.0f);
        public Prop<float> MapViewMin = new Prop<float>(1.0f);
        public Prop<float> MapViewMax = new Prop<float>(1.0f);
        public Prop<float> MapSpeedMin = new Prop<float>(1.0f);
        public Prop<float> MapSpeedMax = new Prop<float>(1.0f);

        public Prop<string> HealthType = new Prop<string>("person");
        public Prop<float>  MaxHealth = new Prop<float>(100.0f);

        // Default animation for soldier classes seems to be hardcoded to "human".
        // For example, there's no "AnimationName" anywhere in the odf hierarchy:
        //   rep_inf_ep3_rifleman -> rep_inf_default_rifleman -> rep_inf_default -> com_inf_default
        public Prop<string> AnimationName = new Prop<string>("human");
        public Prop<string> SkeletonName = new Prop<string>("human");

        public Prop<float> MaxSpeed = new Prop<float>(1.0f);
        public Prop<float> MaxStrafeSpeed = new Prop<float>(1.0f);
        public Prop<float> MaxTurnSpeed = new Prop<float>(1.0f);
        public Prop<float> JumpHeight = new Prop<float>(1.0f);
        public Prop<float> JumpForwardSpeedFactor = new Prop<float>(1.0f);
        public Prop<float> JumpStrafeSpeedFactor = new Prop<float>(1.0f);
        public Prop<float> RollSpeedFactor = new Prop<float>(1.0f);
        public Prop<float> Acceleration = new Prop<float>(1.0f);
        public Prop<float> SprintAccelerateTime = new Prop<float>(1.0f);

        public MultiProp ControlSpeed = new MultiProp(typeof(string), typeof(float), typeof(float), typeof(float));

        public Prop<float> EnergyBar = new Prop<float>(1.0f);
        public Prop<float> EnergyRestore = new Prop<float>(1.0f);
        public Prop<float> EnergyRestoreIdle = new Prop<float>(1.0f);
        public Prop<float> EnergyDrainSprint = new Prop<float>(1.0f);
        public Prop<float> EnergyMinSprint = new Prop<float>(1.0f);
        public Prop<float> EnergyCostJump = new Prop<float>(0.0f);
        public Prop<float> EnergyCostRoll = new Prop<float>(1.0f);

        public Prop<float> AimValue = new Prop<float>(1.0f);
        public Prop<float> AimFactorPostureSpecial = new Prop<float>(1.0f);
        public Prop<float> AimFactorPostureStand = new Prop<float>(1.0f);
        public Prop<float> AimFactorPostureCrouch = new Prop<float>(1.0f);
        public Prop<float> AimFactorPostureProne = new Prop<float>(1.0f);
        public Prop<float> AimFactorStrafe = new Prop<float>(0.0f);
        public Prop<float> AimFactorMove = new Prop<float>(1.0f);

        public Prop<string> AISizeType = new Prop<string>("SOLDIER");

        public MultiProp WeaponName = new MultiProp(typeof(string));
    }

    public Prop<float> CurHealth = new Prop<float>(100.0f);

    public InstanceController Controller;
    Animator Anim;
    bool bHasLookaroundIdleAnim = false;
    bool bHasCheckweaponIdleAnim = false;
    const float IdleTime = 10f;

    string[] IdleNames = new string[]
    {
        "IdleLookaround",
        "IdleCheckweapon"
    };


    public override void Init()
    {
        Anim = gameObject.AddComponent<Animator>();
        Anim.applyRootMotion = false;
        RuntimeAnimatorController runtimeAnimController = Resources.Load<RuntimeAnimatorController>(ANIM_CONTROLLER_NAME);
        if (runtimeAnimController == null)
        {
            Debug.LogError($"Could not get runtime animator controller '{ANIM_CONTROLLER_NAME}'!");
            return;
        }

        AnimatorOverrideController animController = new AnimatorOverrideController(runtimeAnimController);
        Anim.runtimeAnimatorController = animController;
        Anim.cullingMode = AnimatorCullingMode.CullCompletely;

        int numAnims = animController.animationClips.Length;
        var overrides = new KeyValuePair<AnimationClip, AnimationClip>[numAnims];

        for (int i = 0; i < numAnims; ++i)
        {
            AnimationClip src = animController.animationClips[i];
            if (src == null)
            {
                Debug.LogError($"Found unassigned AnimationClip in AnimationController '{ANIM_CONTROLLER_NAME}'!");
                continue;
            }

            string weaponAnimBankName = "rifle"; // TODO
            string animName = $"{C.SkeletonName}_{weaponAnimBankName}_{src.name}";

            // TODO: There's AnimationName and SkeletonName. Sometimes they mean the same thing !?
            // For example: The super battle droid just has SkeletonName property, but no AnimationName property...
            AnimationClip dst = GetClip(C.SkeletonName, animName, false);
            if (dst == null)
            {
                dst = GetClip(C.AnimationName, animName, true);
            }
            if (dst == null)
            {
                if (src.name != "stand_idle_lookaround" && src.name != "stand_idle_checkweapon")
                {
                    Debug.LogError($"Cannot find Animation '{animName}' in AnimationBanks '{C.SkeletonName}' / '{C.AnimationName}'!");
                }
                continue;
            }

            bHasLookaroundIdleAnim  = bHasLookaroundIdleAnim  || src.name == "stand_idle_lookaround";
            bHasCheckweaponIdleAnim = bHasCheckweaponIdleAnim || src.name == "stand_idle_checkweapon";

            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(src, dst);
        }
        
        animController.ApplyOverrides(overrides);
    }

    public override void BindEvents()
    {
        
    }

    public void AddHealth(float amount)
    {
        CurHealth.Set(Mathf.Clamp(CurHealth + amount, 0f, C.MaxHealth));
    }

    public void AddAmmo(float amount)
    {
        // TODO
    }

    public void PlayIntroAnim()
    {
        if (bHasLookaroundIdleAnim)
        {
            Anim.SetTrigger("IdleLookaround");
        }
        else
        {
            Anim.SetTrigger("Reload");
        }
    }

    AnimationClip GetClip(string bankName, string animName, bool bFallback)
    {
        uint animCRC = HashUtils.GetCRC(animName);
        AnimationClip clip = AnimationLoader.Instance.LoadAnimationClip(bankName, animCRC, transform, false);
        if (clip == null)
        {
            animCRC = HashUtils.GetCRC(animName + "_full");
            clip = AnimationLoader.Instance.LoadAnimationClip(bankName, animCRC, transform, false);
            if (clip != null) return clip;

            if (bFallback)
            {
                clip = GetClip("human_0", animName, false);
                if (clip != null) return clip;

                clip = GetClip("human_1", animName, false);
                if (clip != null) return clip;

                clip = GetClip("human_2", animName, false);
                if (clip != null) return clip;

                clip = GetClip("human_3", animName, false);
                if (clip != null) return clip;

                clip = GetClip("human_4", animName, false);
                if (clip != null) return clip;

                clip = GetClip("human_sabre", animName, false);
                if (clip != null) return clip;
            }
        }
        return clip;
    }

    void Update()
    {
        if (Controller != null)
        {
            Anim.SetFloat("LeftRight", Controller.ControlState.WalkDirection.x);
            Anim.SetFloat("Forward", Controller.ControlState.WalkDirection.y);

            if (Controller.IdleTime >= IdleTime)
            {
                if (bHasLookaroundIdleAnim && !bHasCheckweaponIdleAnim)
                {
                    Anim.SetTrigger(IdleNames[0]);
                }
                else if (!bHasLookaroundIdleAnim && bHasCheckweaponIdleAnim)
                {
                    Anim.SetTrigger(IdleNames[1]);
                }
                else if (bHasLookaroundIdleAnim && bHasCheckweaponIdleAnim)
                {
                    Anim.SetTrigger(IdleNames[UnityEngine.Random.Range(0, 1)]);
                }
                Controller.ResetIdleTime();
            }
        }
    }
}