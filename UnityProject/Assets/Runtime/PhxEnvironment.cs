using System;
using System.Collections.Generic;
using UnityEngine;
using LibSWBF2;
using LibSWBF2.Enums;
using LibSWBF2.Wrappers;
using UnityEditor;


/*
 * Phases of PhxEnvironment:
 *     
 * 1. Init                          Environment is created, with base LVLs scheduled and ready to load. Base LVLs are:
 *                                      - core.lvl
 *                                      - shell.lvl
 *                                      - common.lvl
 *                                      - mission.lvl
 * 2. Loading Base                  Loading scheduled base LVLs
 * 3. Executing Main                There's always a LUA script responsible for environment setup that will be executed in this phase.
 *                                  Usually, there's an optional main function within the main script that will called aswell, if specified.
 *                                  The main LUA script is expected to call ReadDataFile on multiple LVL files. These LVL files will be scheduled.
 * 4. Loading World                 All LVLs that have been scheduled during phase 3 will be loaded in this phase
 * 5. Create Scene                  In this phase, scene conversion of the imported world LVL takes place. 
 * 6. Loaded                        Final state. Everything is done.
 * 
 */


public class PhxEnvironment
{
    public struct LVL
    {
        public SWBF2Handle Handle;
        public Level Level;

        // For Debug
        public PhxPath DisplayPath;
        public bool bIsAddon;
    }

    public enum EnvStage
    {
        Init,        
        LoadingBase, 
        ExecuteMain, 
        LoadingWorld,
        CreateScene, 
        Loaded       
    }


    public List<LVL> Loading = new List<LVL>();
    public List<LVL> Loaded  = new List<LVL>();

    public bool              IsLoaded => Stage == EnvStage.Loaded;
    public Action<Texture2D> OnLoadscreenLoaded;
    public Action            OnExecuteMain;
    public Action            OnLoaded;          // Same frame load is done
    public Action            OnPostLoad;        // one frame AFTER load is done

    public PhxPath GameDataPath { get; private set; }
    public PhxPath AddonDataPath { get; private set; }
    public EnvStage Stage { get; private set; }

    bool CanSchedule => Stage == EnvStage.Init || Stage == EnvStage.ExecuteMain;
    bool CanExecute  => Stage == EnvStage.ExecuteMain || Stage == EnvStage.CreateScene || Stage == EnvStage.Loaded;

    PhxLuaRuntime  LuaRT;
    SWBF2Handle LoadscreenHandle;

    Container EnvCon;
    Level     WorldLevel;     // points to level inside 'Loaded'
    Level     LoadscreenLVL;  // points to level inside 'Loaded'

    string InitScriptName;
    string InitFunctionName;
    string PostLoadFunctionName;

    PhxScene RTScene;
    PhxMatch Match;
    PhxTimerDB Timers;

    List<Localization> Localizations = new List<Localization>();
    Dictionary<string, List<Localization>> LocalizationLookup = new Dictionary<string, List<Localization>>();

    // To prevent loading an lvl more than once.
    // The path here always describes the relative 2-leaf lvl path
    Dictionary<PhxPath, SWBF2Handle> PathToHandle = new Dictionary<PhxPath, SWBF2Handle>();

    bool FirePostLoadEvent;


    PhxEnvironment(PhxPath dataPath, PhxPath addonPath)
    {
        GameDataPath = dataPath;
        AddonDataPath = addonPath;
        Stage = EnvStage.Init;
        WorldLevel = null;

        LuaRT = new PhxLuaRuntime();
        EnvCon = new Container();

        Loader.SetGlobalContainer(EnvCon);
    }

    ~PhxEnvironment()
    {
        Destroy();
    }

    public void Destroy()
    {
        WorldLevel = null;
        Match.Destroy();
        Match = null;
        Timers = null;
        RTScene.Destroy();
        RTScene = null;
        OnLoadscreenLoaded = null;
        OnExecuteMain = null;
        OnLoaded = null;
        LuaRT?.Close();
        EnvCon?.Delete();
        EnvCon = null;
        PathToHandle.Clear();
        Debug.Log("PhxEnvironment destroyed");
    }

    public PhxScene GetScene()
    {
        return RTScene;
    }

    public PhxMatch GetMatch()
    {
        return Match;
    }

    public PhxTimerDB GetTimerDB()
    {
        return Timers;
    }

    public static PhxEnvironment Create(PhxPath lvlGameDataPath, PhxPath lvlAddonDataPath = null, bool initMatch=true)
    {
        if (!lvlGameDataPath.Exists())
        {
            Debug.LogError($"Given environment path '{lvlGameDataPath}' doesn't exist!");
            return null;
        }

        bool bIsAddon = lvlAddonDataPath != null;
        if (bIsAddon && !lvlAddonDataPath.Exists())
        {
            Debug.LogError($"Given environment path '{lvlGameDataPath}' doesn't exist!");
            return null;
        }

        PhxAnimationLoader.ClearDB();
        PhxLuaEvents.Clear();

        PhxEnvironment rt = new PhxEnvironment(lvlGameDataPath, lvlAddonDataPath);
        rt.ScheduleRelFallback("core.lvl");
        rt.ScheduleRelFallback("shell.lvl");
        rt.ScheduleRelFallback("common.lvl");
        rt.ScheduleRelFallback("mission.lvl");
        rt.ScheduleRel("sound/common.bnk");

        // TODO: Remove
        //rt.ScheduleAbs(rt.AddonDataPath / "ingame.lvl");

        rt.RTScene = new PhxScene(rt, rt.EnvCon);
        rt.Match = initMatch ? new PhxMatch() : null;
        rt.Timers = new PhxTimerDB();

        PhxAnimationLoader.Con = rt.EnvCon;

        return rt;
    }

    public PhxLuaRuntime GetLuaRuntime()
    {
        return LuaRT;
    }

    public T Find<T>(string name) where T : NativeWrapper, new()
    {
        return EnvCon.Get<T>(name);
    }

    public bool Execute(string scriptName)
    {
        Debug.Assert(CanExecute);
        
        Script script = EnvCon.Get<Script>(scriptName);
        if (script == null)
        {
            script = EnvCon.Get<Script>(scriptName.ToLower());
        }

        if (script == null)
        {
            Debug.LogError($"Couldn't find script '{scriptName}'!");
            return false;
        }

        if (!script.IsValid())
        {
            Debug.LogError($"Script '{scriptName}' found but invalid!");
            return false;
        }

        return Execute(script);
    }

    public bool Execute(Script script)
    {
        Debug.Assert(CanExecute);
        if (script == null || !script.IsValid())
        {
            Debug.LogError($"Given script '{script.Name}' is NULl or invalid!");
            return false;
        }

        if (!script.GetData(out IntPtr luaBin, out uint size))
        {
            Debug.LogError($"Couldn't grab lua binary code from script '{script.Name}'!");
            return false;
        }

        // Still have no idea why missionlist fails on Linux/Mac.  I'll have to dig into the compilation
        // warnings produced when compiling the Lua lib.
        if (script.Name == "missionlist" && PhxGame.Instance.MissionListPath != "")
        {
            return LuaRT.ExecuteFile(PhxGame.Instance.MissionListPath);
        }
        else 
        {
            return LuaRT.Execute(luaBin, size, script.Name);
        }
    }

    public void Run(string initScript, string initFn = null, string postLoadFn = null)
    {
        Debug.Assert(Stage == EnvStage.Init);
        Loader.ResetAllLoaders();

        InitScriptName = initScript;
        InitFunctionName = initFn;
        PostLoadFunctionName = postLoadFn;

        LoadscreenHandle = ScheduleRel(GetLoadscreenPath());
        EnvCon.LoadLevels();

        Stage = EnvStage.LoadingBase;
    }

    public float GetLoadingProgress()
    {
        float stageContribution = 1.0f / 2.0f;
        float stageProgress = Stage >= EnvStage.LoadingWorld ? stageContribution : 0.0f;

        return stageProgress + stageContribution * EnvCon.GetOverallProgress();
    }

    public Level GetWorldLevel()
    {
        return WorldLevel;
    }

    public SWBF2Handle ScheduleAbs(PhxPath absPath, string[] subLVLs = null)
    {
        if (!Schedule(absPath, out SWBF2Handle handle, subLVLs))
        {
            Debug.LogErrorFormat("Couldn't schedule '{0}'! File not found!", absPath);
        }
        return handle;
    }

    public SWBF2Handle ScheduleRel(PhxPath relPath, string[] subLVLs = null, bool bAddon = false)
    {
        if (bAddon && !AddonDataPath.Exists())
        {
            Debug.LogError($"ScheduleRel '{relPath}' from Addon, but AddonDataPath is NULL!");
            return new SWBF2Handle(ushort.MaxValue);
        }

        // Relative paths are always lower case in consideration of Unix file systems.
        // Also see PhxGame::Awake()
        relPath = relPath.ToString().ToLower();
        PhxPath dataPath = bAddon ? AddonDataPath : GameDataPath;

        SWBF2Handle handle;
        if (!Schedule(dataPath / relPath, out handle, subLVLs))
        {
            Debug.LogError($"Couldn't schedule '{relPath}'! File not found!");
        }
        return handle;
    }

    public SWBF2Handle ScheduleRelFallback(PhxPath relPath, string[] subLVLs = null)
    {
        relPath = relPath.ToString().ToLower();
        if (AddonDataPath != null)
        {
            PhxPath path = (AddonDataPath / relPath);
            if (path.Exists() && path.IsFile())
            {
                return ScheduleRel(relPath, subLVLs, true);
            }
        }
        return ScheduleRel(relPath, subLVLs, false);
    }

    public float GetProgress(SWBF2Handle handle)
    {
        return EnvCon.GetProgress(handle);
    }

    public void Tick(float deltaTime)
    {
        // This needs to come first
        if (FirePostLoadEvent)
        {
            OnPostLoad?.Invoke();
            FirePostLoadEvent = false;
        }

        for (int i = 0; i < Loading.Count; ++i)
        {
            ELoadStatus status = EnvCon.GetStatus(Loading[i].Handle);

            if (status == ELoadStatus.Loaded)
            {
                LVL scheduled = Loading[i];
                
                var lvl = EnvCon.GetLevel(Loading[i].Handle);
                Debug.Assert(lvl != null);
                scheduled.Level = lvl;

                if (lvl.IsWorldLevel)
                {
                    if (WorldLevel != null)
                    {
                        Debug.LogErrorFormat("Encounterred another world lvl '{0}' in environment! Previously found world lvl: '{1}'", lvl.Name, WorldLevel.Name);
                    }
                    else
                    {
                        WorldLevel = lvl;
                    }
                }

                // grab lvl localizations, if any
                Localizations.AddRange(lvl.Get<Localization>());
                

                SoundLoader.Instance.InitializeSoundProperties(lvl);


                Loaded.Add(scheduled);
                Loading.RemoveAt(i);
                break; // do not further iterate altered list
            }
            else if (status == ELoadStatus.Failed)
            {
                Debug.LogErrorFormat("Loading '{0}' failed!", Loading[i].DisplayPath);

                Loading.RemoveAt(i);
                break; // do not further iterate altered list
            }    
        }

        if (Stage == EnvStage.LoadingBase)
        {
            if (LoadscreenLVL == null)
            {
                LoadscreenLVL = EnvCon.GetLevel(LoadscreenHandle);
                if (LoadscreenLVL != null)
                {
                    var textures = LoadscreenLVL.Get<LibSWBF2.Wrappers.Texture>();
                    int texIdx = UnityEngine.Random.Range(0, textures.Length - 1);
                    OnLoadscreenLoaded?.Invoke(TextureLoader.Instance.ImportUITexture(textures[texIdx].Name));
                }
            }

            if (EnvCon.IsDone() && Loading.Count == 0)
            {
                Stage = EnvStage.ExecuteMain;
                RunMain();
            }
        }

        if (Stage == EnvStage.LoadingWorld && EnvCon.IsDone() && Loading.Count == 0)
        {
            for (int i = 0; i < Localizations.Count; ++i)
            {
                string locName = Localizations[i].Name;
                if (LocalizationLookup.TryGetValue(locName, out var loc))
                {
                    loc.Add(Localizations[i]);
                }
                else
                {
                    LocalizationLookup.Add(locName, new List<LibSWBF2.Wrappers.Localization> { Localizations[i] });
                }
            }

            // apply queued Lua calls like "AddUnitClass"
            Match?.ApplySchedule();

            Stage = EnvStage.CreateScene;
            CreateScene();
        }

        Timers?.Tick(deltaTime);
        Match?.Tick(deltaTime);
        RTScene.Tick(deltaTime);
    }

    public void TickPhysics(float deltaTime)
    {
        RTScene.TickPhysics(deltaTime);
    }

    public string GetLocalized(string localizedPath, bool bReturnNullIfNotFound=false)
    {
        if (GetLocalized(PhxGame.Instance.Settings.Language, localizedPath, out string localizedUnicode))
        {
            return localizedUnicode;
        }
        if (GetLocalized("english", localizedPath, out localizedUnicode))
        {
            return localizedUnicode;
        }
        return bReturnNullIfNotFound ? null : localizedPath;
    }

    bool GetLocalized(string language, string localizedPath, out string localizedUnicode)
    {
        localizedUnicode = localizedPath;

        List<LibSWBF2.Wrappers.Localization> locs;
        if (!LocalizationLookup.TryGetValue(language, out locs))
        {
            return false;
        }

        for (int i = 0; i < locs.Count; ++i)
        {
            if (locs[i].GetLocalizedWideString(localizedPath, out localizedUnicode))
            {
                return true;
            }
        }
        return false;
    }

    public string GetLocalizedMapName(string mapluafile)
    {
        object[] res = LuaRT.CallLuaFunction("missionlist_GetLocalizedMapName", 2, false, true, mapluafile);
        string mapName = res[0] as string;
        return mapName;
    }

    bool Schedule(PhxPath absPath, out SWBF2Handle handle, string[] subLVLs = null)
    {
        Debug.Assert(CanSchedule);

        if (PathToHandle.TryGetValue(absPath, out handle))
        {
            return true;
        }

        if (absPath.Exists() && absPath.IsFile())
        {
            handle = EnvCon.AddLevel(absPath, subLVLs);

            Loading.Add(new LVL
            {
                Handle = handle,
                DisplayPath = absPath.GetLeaf(2),
                bIsAddon = absPath.Contains("/addon/")
            });

            PathToHandle.Add(absPath, handle);
            return true;
        }

        handle = new SWBF2Handle(ushort.MaxValue);
        return false;
    }

    PhxPath GetLoadscreenPath()
    {
        // First, try grab loadscreen for standard maps
        if (!string.IsNullOrEmpty(InitScriptName))
        {
            PhxPath loadscreenLVL = new PhxPath("load") / (InitScriptName.Substring(0, 4) + ".lvl");
            if ((GameDataPath / loadscreenLVL).Exists() || (AddonDataPath != null && (AddonDataPath / loadscreenLVL).Exists()))
            {
                return loadscreenLVL;
            } 
        }
        return "load/common.lvl";
    }

    void RunMain()
    {
        Debug.Assert(Stage == EnvStage.ExecuteMain);

        // 1 - execute the main script
        if (!string.IsNullOrEmpty(InitScriptName) && !Execute(InitScriptName))
        {
            Debug.LogErrorFormat("Executing lua main script '{0}' failed! Closing application.", InitScriptName);
            #if UNITY_EDITOR
            EditorApplication.ExitPlaymode();
            #endif
            Application.Quit(1);
            return;
        }
        OnExecuteMain?.Invoke();

        // 2 - execute the main function -> will call ReadDataFile multiple times
        if (!string.IsNullOrEmpty(InitFunctionName) && LuaRT.CallLuaFunction(InitFunctionName, 0) == null)
        {
            Debug.LogErrorFormat("Executing lua main function '{0}' failed!", InitFunctionName);
            return;
        }

        // 3 - load the (via ReadDataFile) scheduled lvl files
        EnvCon.LoadLevels();
        Stage = EnvStage.LoadingWorld;
    }

    void CreateScene()
    {
        Debug.Assert(Stage == EnvStage.CreateScene);

        // WorldLevel will be null for Main Menu
        if (WorldLevel != null)
        {
            RTScene.Import(WorldLevel.Get<LibSWBF2.Wrappers.World>());
        }

        // 4 - execute post load function AFTER scene has been created
        if (!string.IsNullOrEmpty(PostLoadFunctionName) && LuaRT.CallLuaFunction(PostLoadFunctionName, 0) == null)
        {
            Debug.LogErrorFormat("Executing lua post load function '{0}' failed!", PostLoadFunctionName);
        }

        Stage = EnvStage.Loaded;
        OnLoaded?.Invoke();

        FirePostLoadEvent = true;
    }
}