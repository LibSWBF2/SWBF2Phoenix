using System;
using System.Collections.Generic;
using UnityEngine;
using LibSWBF2.Wrappers;
using LibSWBF2.Enums;

#if UNITY_EDITOR
using UnityEditor;
using System.Reflection;
#endif


public struct PhxTransform
{
    public Vector3 Position;
    public Quaternion Rotation;
}

public class PhxScene
{
    public Texture2D MapTexture { get; private set; }

    List<PhxInstance>         Instances                = new List<PhxInstance>();

    // Subsets of 'Instances'
    List<IPhxTickable>        TickableInstances        = new List<IPhxTickable>();
    List<IPhxTickablePhysics> TickablePhysicsInstances = new List<IPhxTickablePhysics>();
    List<PhxCommandpost>      CommandPosts             = new List<PhxCommandpost>();
    GameObject                Vehicles                 = new GameObject("Vehicles");

    Dictionary<string, PhxClass> Classes     = new Dictionary<string, PhxClass>();
    Dictionary<string, int>      InstanceMap = new Dictionary<string, int>();

    PhxEnvironment ENV;
    Container EnvCon;
    bool bTerrainImported = false;

    Dictionary<string, GameObject> LoadedSkydomes = new Dictionary<string, GameObject>();
    Dictionary<string, PhxRegion>  Regions  = new Dictionary<string, PhxRegion>();

    List<GameObject>  WorldRoots = new List<GameObject>();

    // Camera positions
    List<PhxTransform> CameraShots = new List<PhxTransform>();
    int CurrCamIdx;
    const float CameraMaxDistance = 15.0f;
    Dictionary<PhxCommandpost, PhxTransform> CPCamPositions = new Dictionary<PhxCommandpost, PhxTransform>();

    PhxProjectiles Projectiles = new PhxProjectiles();
    public readonly PhxEffectsManager EffectsManager = new PhxEffectsManager();

    CraMain Cra;
    int InstanceCounter;


    public PhxSceneAnimator Animator { get; private set; }


    public PhxScene(PhxEnvironment env, Container c)
    {
        ENV = env;
        EnvCon = c;
        Cra = new CraMain();

        ModelLoader.Instance.PhyMat = PhxGame.Instance.GroundPhyMat;
        ENV.OnPostLoad += CalcCPCamPositions;

        Animator = new PhxSceneAnimator();
    }

    public void SetProperty(string instName, string propName, object propValue)
    {
        int instIdx;
        if (InstanceMap.TryGetValue(instName, out instIdx))
        {
            Instances[instIdx].P.SetProperty(propName, propValue);
            return;
        }
        else if (InstanceMap.TryGetValue(instName.ToLower(), out instIdx))
        {
            Instances[instIdx].P.SetProperty(propName, propValue);
            return;
        }
        Debug.LogWarningFormat("Could not find instance '{0}' to set property '{1}'!", instName, propName);
    }

    public void SetClassProperty(string className, string propName, object propValue)
    {
        if (Classes.TryGetValue(className, out PhxClass cl))
        {
            cl.P.SetProperty(propName, propValue);
            return;
        }
        Debug.LogWarningFormat("Coukd not find odf class '{0}' to set class property '{1}'!", className, propName);
    }

    public bool IsObjectAlive(string instName)
    {
        if (InstanceMap.TryGetValue(instName, out int instIdx))
        {
            return Instances[instIdx].gameObject.activeSelf;
        }
        return false;
    }

    public PhxRegion GetRegion(string name)
    {
        if (Regions.TryGetValue(name.ToLower(), out PhxRegion region))
        {
            return region;
        }
        return null;
    }

    public int? GetInstanceIndex(PhxInstance inst)
    {
        int idx = Instances.IndexOf(inst);
        return idx < 0 ? null : new int?(idx);
    }

    public int? GetInstanceIndex(string instName)
    {
        if (InstanceMap.TryGetValue(instName, out int instIdx))
        {
            return instIdx;
        }
        return null;
    }

    public T GetInstance<T>(string instName) where T : PhxInstance
    {
        if (InstanceMap.TryGetValue(instName, out int instIdx))
        {
            return GetInstance<T>(instIdx);
        }

        Debug.LogWarning($"Cannot inf Instance '{instName}'!");
        return null;
    }

    public T GetInstance<T>(int instIdx) where T : PhxInstance
    {
        if (instIdx >= 0 && instIdx < Instances.Count)
        {
            return Instances[instIdx] as T;
        }

        Debug.LogWarning($"Instance index '{instIdx}' is out of bounds ({Instances.Count})!");
        return null;
    }

    public void AddCameraShot(Vector3 position, Quaternion rotation)
    {
        CameraShots.Add(new PhxTransform
        {
            Position = position,
            Rotation = rotation
        });
    }

    public PhxTransform GetNextCameraShot()
    {
        if (CameraShots.Count == 0)
        {
            return new PhxTransform
            {
                Position = Vector3.zero,
                Rotation = Quaternion.identity
            };
        }

        PhxTransform camShot = CameraShots[CurrCamIdx++];
        if (CurrCamIdx >= CameraShots.Count)
        {
            CurrCamIdx = 0;
        }
        return camShot;
    }


    public void FireProjectile(IPhxWeapon WeaponOfOrigin,
                                PhxOrdnanceClass OrdnanceClass,
                                Vector3 Pos, Quaternion Rot)
    {
        Projectiles.FireProjectile(WeaponOfOrigin, OrdnanceClass, Pos, Rot);
    }


    public void Import(World[] worldLayers)
    {
        if (WorldRoots.Count > 0)
        {
            Debug.LogError("Create a new RuntimeScene instance!");
            return;
        }

        Loader.ResetAllLoaders();

        WorldLoader.Instance.TerrainAsMesh = true;

        foreach (World world in worldLayers)
        {
            if (MapTexture == null)
            {
                MapTexture = TextureLoader.Instance.ImportUITexture(world.Name + "_map", false);
            }

            GameObject worldRoot = new GameObject(world.Name);

            //Regions
            //Import before instances, since instances will reference regions
            var regionsRoot = WorldLoader.Instance.ImportRegions(world.GetRegions());
            regionsRoot.transform.parent = worldRoot.transform;
            foreach (var region in WorldLoader.Instance.LoadedRegions)
            {
                string regName = region.Key.ToLower();
                if (Regions.ContainsKey(regName))
                {
                    Debug.LogWarning($"Region '{regName}' already registered!");
                    continue;
                }

                PhxRegion reg = region.Value.gameObject.AddComponent<PhxRegion>();

                // invoke Lua events
                reg.OnEnter += (IPhxControlableInstance obj) => PhxLuaEvents.InvokeParameterized(PhxLuaEvents.Event.OnEnterRegion, regName, regName, obj.GetInstance().name);
                reg.OnLeave += (IPhxControlableInstance obj) => PhxLuaEvents.InvokeParameterized(PhxLuaEvents.Event.OnLeaveRegion, regName, regName, obj.GetInstance().name);

                Regions.Add(regName, reg);
            }

            //Instances
            GameObject instancesRoot = new GameObject("Instances");
            instancesRoot.transform.parent = worldRoot.transform;

            List<GameObject> instances = ImportInstances(world.GetInstances());
            foreach (GameObject instanceObject in instances)
            {
                instanceObject.transform.SetParent(instancesRoot.transform);
            }

            //Terrain
            var terrain = world.GetTerrain();
            if (terrain != null && !bTerrainImported)
            {
                GameObject terrainGameObject;
                terrainGameObject = WorldLoader.Instance.ImportTerrainAsMeshHDRP(terrain);

                terrainGameObject.transform.parent = worldRoot.transform;
                terrainGameObject.layer = LayerMask.NameToLayer("TerrainAll");
                bTerrainImported = true;
            }


            //Lighting
            var lightingRoots = WorldLoader.Instance.ImportLights(EnvCon.FindConfig(EConfigType.Lighting, world.Name));
            foreach (var lightingRoot in lightingRoots)
            {
                lightingRoot.transform.parent = worldRoot.transform;
            }


            //Skydome, check if already loaded first
            if (!LoadedSkydomes.ContainsKey(world.SkydomeName))
            {
                var skyRoot = WorldLoader.Instance.ImportSkydome(EnvCon.FindConfig(EConfigType.Skydome, world.SkydomeName));
                if (skyRoot != null)
                {
                    skyRoot.transform.parent = worldRoot.transform;
                }

                LoadedSkydomes[world.SkydomeName] = skyRoot;
            }

            WorldRoots.Add(worldRoot);
        }

        Animator.InitializeWorldAnimations(worldLayers);
    }

    public SWBFPath GetPath(string pathName)
    {
        Level level = ENV.GetWorldLevel();
        if (level != null)
        {
            SWBFPath path = WorldLoader.Instance.ImportPath(level, pathName);
            return path;
        }
        return null;
    }

    public void Destroy()
    {
        Cra.Destroy();
        Cra = null;
        Projectiles.Destroy();
        Instances.Clear();
        Classes.Clear();
        UnityEngine.Object.Destroy(Vehicles);
        for (int i = 0; i < WorldRoots.Count; ++i)
        {
            UnityEngine.Object.Destroy(WorldRoots[i]);
        }
        WorldRoots.Clear();
    }

    // TODO: implement object pooling
    public PhxInstance CreateInstance(PhxClass cl, string instName, Vector3 position, Quaternion rotation, bool withCollision = true, Transform parent = null)
    {
        return CreateInstance(cl.EntityClass, instName, position, rotation, withCollision, parent).GetComponent<PhxInstance>();
    }

    public PhxInstance CreateInstance(PhxClass cl, bool withCollision=true, Transform parent=null)
    {
        return CreateInstance(cl.EntityClass, cl.Name + InstanceCounter++, Vector3.zero, Quaternion.identity, withCollision, parent).GetComponent<PhxInstance>();
    }

    public void DestroyInstance(PhxInstance instance)
    {
        int idx = Instances.IndexOf(instance);
        Debug.Assert(idx >= 0);

        UnityEngine.Object.Destroy(instance.gameObject);
        Instances.RemoveAt(idx);

        if (instance is IPhxTickable)
        {
            TickableInstances.Remove((IPhxTickable)instance);
        }
        if (instance is IPhxTickablePhysics)
        {
            TickablePhysicsInstances.Remove((IPhxTickablePhysics)instance);
        }
    }

    public PhxClass GetClass(string odfClassName)
    {
        if (Classes.TryGetValue(odfClassName, out PhxClass odf))
        {
            return odf;
        }
        EntityClass ec = ENV.Find<EntityClass>(odfClassName);
        if (ec != null)
        {
            return GetClass(ec);
        }
        return null;
    }

    public PhxCommandpost[] GetCommandPosts()
    {
        return CommandPosts.ToArray();
    }

    public bool GetCPCameraTransform(PhxCommandpost cp, out PhxTransform camTransform)
    {
        return CPCamPositions.TryGetValue(cp, out camTransform);
    }

    public void Tick(float deltaTime)
    {
        Projectiles.Tick(deltaTime);
        Cra?.Tick();

        // Update instances AFTER animation update!
        // Instances might adapt some bone transformations (e.g. PhxSoldier)
        for (int i = 0; i < TickableInstances.Count; ++i)
        {
            TickableInstances[i].Tick(deltaTime);
        }
    }

    public void TickPhysics(float deltaTime)
    {
        Projectiles.TickPhysics(deltaTime);
        for (int i = 0; i < TickablePhysicsInstances.Count; ++i)
        {
            TickablePhysicsInstances[i].TickPhysics(deltaTime);
        }
    }

    PhxClass GetClass(EntityClass ec)
    {
        PhxClass odf = null;
        if (!Classes.TryGetValue(ec.Name, out odf))
        {
            EntityClass rootClass = ClassLoader.GetRootClass(ec);
            Type classType = PhxClassRegister.GetPhxClassType(rootClass.BaseClassName);
            if (classType != null)
            {
                odf = (PhxClass)Activator.CreateInstance(classType);
                odf.InitClass(ec);
                Classes.Add(ec.Name, odf);
            }
        }
        return odf;
    }

    GameObject CreateInstance(ISWBFProperties instOrClass, string instName, Vector3 position, Quaternion rotation, bool withCollision=true, Transform parent =null)
    {
        if (InstanceMap.ContainsKey(instName))
        {
            Debug.LogWarningFormat("Instance with name: {0} already created!", instName);
            return null;
        }

        if (instOrClass == null)
        {
            Debug.LogWarning("Called 'CreateInstance' with NULL!");
            return null;
        }

        EntityClass ec = instOrClass.GetType() == typeof(Instance) ? ((Instance)instOrClass).EntityClass : ((EntityClass)instOrClass);
        if (ec == null)
        {
            // this can only happen if 'instOrClass' is an instance!
            Instance inst = (Instance)instOrClass;
            Debug.LogWarning($"Cannot find EnityClass '{inst.EntityClassName}' of given Instance '{inst.Name}'!");
            return null;
        }

        EntityClass rootClass = ClassLoader.GetRootClass(ec);
        if (rootClass == null)
        {
            Debug.LogWarning($"Could not find root class of '{ec.Name}'!");
            return null;
        }

        GameObject instanceObject = ClassLoader.Instance.Instantiate(instOrClass, instName);
        instanceObject.transform.SetParent(parent);
        instanceObject.transform.localRotation = rotation;
        instanceObject.transform.localPosition = position;
        instanceObject.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);

        Type instType = PhxClassRegister.GetPhxInstanceType(rootClass.BaseClassName);

        if (instType != null)
        {
            PhxClass odf = GetClass(ec);
            PhxInstance script = (PhxInstance)instanceObject.AddComponent(instType);
            script.InitInstance(instOrClass, odf);

            Instances.Add(script);

            if (!string.IsNullOrEmpty(instanceObject.name))
            {
                if (!InstanceMap.ContainsKey(instanceObject.name))
                {
                    InstanceMap.Add(instanceObject.name, Instances.Count - 1);
                }
                else
                {
                    Debug.LogError($"Instance with name '{instanceObject.name}' is already registered in scene!");
                }
            }
            else
            {
                Debug.LogWarning($"Encountered instance of type '{odf.Name}' with no Name!");
            }

            if (script is IPhxTickable)
            {
                TickableInstances.Add((IPhxTickable)script);
            }
            if (script is IPhxTickablePhysics)
            {
                TickablePhysicsInstances.Add((IPhxTickablePhysics)script);
            }
            if (script is PhxCommandpost)
            {
                CommandPosts.Add((PhxCommandpost)script);
            }
            if(script is PhxVehicle)
            {
                instanceObject.transform.parent = Vehicles.transform;
            }
        }

        return instanceObject;
    }

    List<GameObject> ImportInstances(Instance[] instances)
    {
        List<GameObject> instanceObjects = new List<GameObject>();
        foreach (Instance inst in instances)
        {
            try
            {
                GameObject instGO = CreateInstance(
                    inst,
                    inst.Name,
                    UnityUtils.Vec3FromLibWorld(inst.Position),
                    UnityUtils.QuatFromLibWorld(inst.Rotation)
                );
                if (instGO != null)
                {
                    instanceObjects.Add(instGO);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to create {inst.Name}: " + e);
            }
        }
        return instanceObjects;
    }

    void CalcCPCamPositions()
    {
        for (int i = 0; i < CommandPosts.Count; ++i)
        {
            Transform t = CommandPosts[i].transform;
            Vector3 direction = (2.0f * -t.forward + t.up).normalized;
            Vector3 right = -t.right;

            float closestDistance = CameraMaxDistance;
            if (Physics.Raycast(t.position, direction, out RaycastHit info, CameraMaxDistance))
            {
                closestDistance = info.distance;
            }

            CPCamPositions.Add(CommandPosts[i], new PhxTransform
            {
                Position = t.position + direction * closestDistance,
                Rotation = Quaternion.LookRotation(-direction, Vector3.Cross(right, -direction))
            });
        }
    }

#if UNITY_EDITOR
    void DrawIcon(GameObject gameObject, int idx)
    {
        var largeIcons = GetTextures("sv_label_", string.Empty, 0, 8);
        var icon = largeIcons[idx];
        var egu = typeof(EditorGUIUtility);
        var flags = BindingFlags.InvokeMethod | BindingFlags.Static | BindingFlags.NonPublic;
        var args = new object[] { gameObject, icon.image };
        var setIcon = egu.GetMethod("SetIconForObject", flags, null, new Type[] { typeof(UnityEngine.Object), typeof(Texture2D) }, null);
        setIcon.Invoke(null, args);
    }
    GUIContent[] GetTextures(string baseName, string postFix, int startIndex, int count)
    {
        GUIContent[] array = new GUIContent[count];
        for (int i = 0; i < count; i++)
        {
            array[i] = EditorGUIUtility.IconContent(baseName + (startIndex + i) + postFix);
        }
        return array;
    }

    public int GetInstanceCount()
    {
        return Instances.Count;
    }
    public int GetActiveProjectileCount()
    {
        return Projectiles.GetActiveCount();
    }
    public int GetTotalProjectileCount()
    {
        return Projectiles.GetTotalCount();
    }
    public int GetTickableCount()
    {
        return TickableInstances.Count;
    }
    public int GetTickablePhysicsCount()
    {
        return TickablePhysicsInstances.Count;
    }
#endif
}
