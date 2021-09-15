#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
    #define UNIX
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

using LibLog = LibSWBF2.Logging.Logger;
using LibLogEntry = LibSWBF2.Logging.LoggerEntry;
using ELibLogType = LibSWBF2.Logging.ELogType;

#if UNIX
using System.IO;
#endif


public class PhxGameRuntime : MonoBehaviour
{
    public static PhxGameRuntime Instance { get; private set; } = null;


    public PhxPath GamePath 
    { 
        get { return new PhxPath(GamePathString); } 
        private set { GamePathString = value.ToString(); }
    }


    public enum PhxStartupBehaviour
    {
        MainMenu,
        SWBF2Map,
        UnityScene
    }


    [Header("Settings")]
    public string GamePathString = "";

    public string Language = "english";
    public PhxStartupBehaviour StartupBehaviour;
    public string StartupSWBF2Map;
    public string StartupUnityScene;
    public bool   InfiniteAmmo;

    [Header("References")]
    public PhxLoadscreen      InitScreenPrefab;
    public PhxLoadscreen      LoadScreenPrefab;
    public PhxMainMenu        MainMenuPrefab;
    public PhxPauseMenu       PauseMenuPrefab;
    public PhxCharacterSelect CharacterSelectPrefab;
    public Transform          CharSelectTransform;
    public Volume             CharSelectPPVolume;
    public AudioMixerGroup    UIAudioMixer;
    public PhxCamera          Camera;
    public PhysicMaterial     GroundPhyMat;
    public PhxHUD             HUDPrefab;
    public PhxBolt            BoltPrefab;
    public PhxBeam            BeamPrefab;

    [Header("For non-Windows Users")]
    public string MissionListPath = Application.platform == RuntimePlatform.WindowsEditor ? "" : "path/to/missionlist.lua";

    // This will only fire for maps, NOT for the main menu!
    public Action OnMapLoaded;
    public Action<Type> OnRemoveMenu;

    public PhxPath AddonPath { get; private set; }
    public PhxPath StdLVLPC { get; private set; }

    // Used only when registering addons
    PhxPath CurrentAddonFolder;

    PhxLoadscreen    CurrentLS;
    PhxMenuInterface CurrentMenu;

    // ring buffer
    AudioSource[] UIAudio = new AudioSource[5];
    byte UIAudioHead = 0;

    PhxRuntimeEnvironment Env;
    Dictionary<string, string> RegisteredAddons = new Dictionary<string, string>();
    
    // Maps addons to their root folders
    Dictionary<string, PhxPath> AddonRoots = new Dictionary<string, PhxPath>();

    bool bInitMainMenu;
    string UnitySceneName = null;

    // contains mapluafile strings, e.g. cor1c_con
    List<string> MapRotation = new List<string>();
    int MapRotationIdx = -1;



    // Do not call Destroy on destruction in Editor!
    // For some reason, when switching between Edit and Play mode,
    // Unity will create multiple instances of this, without calling
    // 'Awake' and then destroy those instances again immediately...
#if !UNITY_EDITOR
    ~PhxGameRuntime()
    {
        Debug.Log("PhxGameRuntime destructor called");
        Destroy();
    }
#endif

    public static PhxLuaRuntime GetLuaRuntime()
    {
        PhxRuntimeEnvironment env = GetEnvironment();
        return env == null ? null : env.GetLuaRuntime();
    }

    public static PhxRuntimeEnvironment GetEnvironment()
    {
        return Instance == null ? null : Instance.Env;
    }

    public static PhxCamera GetCamera()
    {
        return Instance == null ? null : Instance.Camera;
    }

    public static PhxRuntimeScene GetScene()
    {
        PhxRuntimeEnvironment env = GetEnvironment();
        return env == null ? null : env.GetScene();
    }

    public static PhxRuntimeMatch GetMatch()
    {
        PhxRuntimeEnvironment env = GetEnvironment();
        return env == null ? null : env.GetMatch();
    }

    public static PhxTimerDB GetTimerDB()
    {
        PhxRuntimeEnvironment env = GetEnvironment();
        return env == null ? null : env.GetTimerDB();
    }

    public void Destroy()
    {
        Env?.Destroy();
        Env = null;
        Instance = null;
        Debug.Log("PhxGameRuntime destroyed");
    }

    public void AddToMapRotation(List<string> mapScripts)
    {
        MapRotation.AddRange(mapScripts);
    }

    public void AddToMapRotation(string mapScript)
    {
        MapRotation.Add(mapScript);
    }

    public void NextMap()
    {
        if (MapRotation.Count == 0)
        {
            return;
        }

        if (++MapRotationIdx >= MapRotation.Count)
        {
            MapRotationIdx = 0;
        }

        EnterSWBF2Map(MapRotation[MapRotationIdx]);
    }

    public void RegisterAddonScript(string scriptName, string addonName)
    {
        if (RegisteredAddons.TryGetValue(scriptName.ToLower(), out string addNm))
        {
            Debug.LogWarningFormat("Addon script '{0}' already registered to '{1}'!", scriptName, addNm);
        }
        else 
        {
            // CurrentAddonFolder is set in OnMainMenuExecution
            AddonRoots[addonName] = CurrentAddonFolder;
            RegisteredAddons.Add(scriptName.ToLower(), addonName);
        }
    }

    public void EnterMainMenu(bool bInit = false)
    {
        Debug.Assert(Env == null || Env.IsLoaded);

        MapRotation.Clear();
        MapRotationIdx = -1;

        bInitMainMenu = bInit;
        ShowLoadscreen(bInit);
        RemoveMenu(false);

        if (UnitySceneName != null)
        {
            SceneManager.UnloadSceneAsync(UnitySceneName);
            UnitySceneName = null;
        }

        Env?.Destroy();
        Env = PhxRuntimeEnvironment.Create(StdLVLPC);

        if (!bInit)
        {
            Env.ScheduleRel("load/gal_con.lvl");
        }

        RegisteredAddons.Clear();
        ExploreAddons();

        Env.OnExecuteMain += OnMainMenuExecution;
        Env.OnLoaded += OnMainMenuLoaded;
        Env.Run("missionlist");
    }

    public void EnterSWBF2Map(string mapScript)
    {
        Debug.Assert(Env == null || Env.IsLoaded);

        ShowLoadscreen();
        RemoveMenu(false);

        PhxPath envPath = StdLVLPC;
        if (RegisteredAddons.TryGetValue(mapScript.ToLower(), out string addonName))
        {
            envPath = AddonPath / AddonRoots[addonName] / "data/_lvl_pc";
        }
        

        if (UnitySceneName != null)
        {
            SceneManager.UnloadSceneAsync(UnitySceneName);
            UnitySceneName = null;
        }

        Env?.Destroy();
        Env = PhxRuntimeEnvironment.Create(envPath, StdLVLPC);
        Env.ScheduleRel("load/common.lvl");
        Env.OnLoadscreenLoaded += OnLoadscreenTextureLoaded;
        Env.OnLoaded += OnEnvLoaded;
        Env.Run(mapScript, "ScriptInit", "ScriptPostLoad");
    }

    public void EnterUnityScene(string sceneName)
    {
        Debug.Assert(Env == null || Env.IsLoaded);

        ShowLoadscreen();
        RemoveMenu(false);

        if (UnitySceneName != null)
        {
            SceneManager.UnloadSceneAsync(UnitySceneName);
            UnitySceneName = null;
        }

        SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        UnitySceneName = sceneName;

        SceneManager.sceneLoaded += (Scene scene, LoadSceneMode mode) =>
        {
            PhxUnityScript sceneInit = FindObjectOfType<PhxUnityScript>();

            // for some reason, this event fires twice...
            // and only the second time, the script is found
            if (sceneInit == null)
            {
                return;
            }

            Env?.Destroy();
            Env = PhxRuntimeEnvironment.Create(StdLVLPC);
            Env.ScheduleRel("load/common.lvl");

            Env.OnLoadscreenLoaded += OnLoadscreenTextureLoaded;
            Env.OnLoaded += OnEnvLoaded;

            Env.OnExecuteMain += sceneInit.ScriptInit;
            Env.OnLoaded += sceneInit.ScriptPostLoad;

            Env.Run(null);
        };
    }

    public T ShowMenu<T>(T prefab) where T : PhxMenuInterface
    {
        if (CurrentMenu != null)
        {
            RemoveMenu(false);
        }
        CurrentMenu = Instantiate(prefab.gameObject).GetComponent<PhxMenuInterface>();
        return (T)CurrentMenu;
    }

    public void RemoveMenu()
    {
        RemoveMenu(true);
    }

    public bool IsMenuActive(PhxMenuInterface prefab)
    {
        Debug.Assert(prefab != null);
        return CurrentMenu == null ? false : CurrentMenu.GetType() == prefab.GetType();
    }

    void RemoveMenu(bool bInvokeEvent)
    {
        if (CurrentMenu != null)
        {
            Type menuType = CurrentMenu.GetType();
            CurrentMenu.Clear();
            Destroy(CurrentMenu.gameObject);
            CurrentMenu = null;
            if (bInvokeEvent)
            {
                OnRemoveMenu?.Invoke(menuType);
            }
        }
    }

    public void PlayUISound(AudioClip sound, float pitch = 1.0f)
    {
        UIAudio[UIAudioHead].clip = sound;
        UIAudio[UIAudioHead].pitch = pitch;
        UIAudio[UIAudioHead].Play();

        UIAudioHead++;
        if (UIAudioHead >= UIAudio.Length)
        {
            UIAudioHead = 0;
        }
    }

    void ShowLoadscreen(bool bInitScreen = false)
    {
        Debug.Assert(CurrentLS == null);
        CurrentLS = Instantiate(bInitScreen ? InitScreenPrefab : LoadScreenPrefab);
    }

    void OnLoadscreenTextureLoaded(Texture2D loadscreenTexture)
    {
        CurrentLS.SetLoadImage(loadscreenTexture);
    }

    void RemoveLoadscreen()
    {
        Debug.Assert(CurrentLS != null);
        CurrentLS.FadeOut();
        CurrentLS = null;
    }

    void Init()
    {
        Debug.Log("PhxGameRuntime Init");
        Debug.Assert(Instance == null);

        Instance = this;
        WorldLoader.UseHDRP = true;
        MaterialLoader.UseHDRP = true;
        EffectsLoader.UseHDRP = true;

        AddonPath = GamePath / "GameData/addon";
        StdLVLPC = GamePath / "GameData/data/_lvl_pc";

        if (GamePath.IsFile()              || 
            !GamePath.Exists()             || 
            !CheckExistence("common.lvl")  ||
            !CheckExistence("core.lvl")    ||
            !CheckExistence("ingame.lvl")  ||
            !CheckExistence("inshell.lvl") ||
            !CheckExistence("mission.lvl") ||
            !CheckExistence("shell.lvl"))
        {
            Debug.LogErrorFormat("Invalid game path '{0}!'", GamePath);
            return;
        }

        if (StartupBehaviour == PhxStartupBehaviour.MainMenu)
        {
            EnterMainMenu(true);
        }
        else if (StartupBehaviour == PhxStartupBehaviour.SWBF2Map)
        {
            Debug.Assert(!string.IsNullOrEmpty(StartupSWBF2Map));
            EnterSWBF2Map(StartupSWBF2Map);
        }
        else if (StartupBehaviour == PhxStartupBehaviour.UnityScene)
        {
            Debug.Assert(!string.IsNullOrEmpty(StartupUnityScene));
            EnterUnityScene(StartupUnityScene);
        }
    }

    void ExploreAddons()
    {
        string[] addons = System.IO.Directory.GetDirectories(AddonPath);
        
        foreach (PhxPath addon in addons)
        {
            PhxPath addme = addon / "addme.script";
            if (addme.Exists() && addme.IsFile())
            {
                Env.ScheduleAbs(addme);
            }
        }
    }

    void OnMainMenuExecution()
    {
        Debug.Assert(CurrentLS != null);

        if (bInitMainMenu)
        {
            CurrentLS.SetLoadImage(TextureLoader.Instance.ImportUITexture("_LOCALIZE_english_bootlegal"));
        }
        else
        {
            CurrentLS.SetLoadImage(TextureLoader.Instance.ImportUITexture("gal_con"));
        }

        foreach (var lvl in Env.Loaded)
        {
            if (lvl.DisplayPath.GetLeaf() == "addme.script")
            {
                var addme = lvl.Level.Get<LibSWBF2.Wrappers.Script>("addme");
                if (addme == null)
                {
                    Debug.LogWarningFormat("Seems like '{0}' has no 'addme' script chunk!", lvl.DisplayPath);
                    continue;
                }

                CurrentAddonFolder = lvl.DisplayPath - "addme.script";
                Env.Execute(addme);
            }
        }
    }

    void OnMainMenuLoaded()
    {
        RemoveLoadscreen();
        ShowMenu(MainMenuPrefab);
    }

    void OnEnvLoaded()
    {
        RemoveLoadscreen();
        Env.GetMatch().StartMatch();
        OnMapLoaded?.Invoke();
    }

    bool CheckExistence(string lvlName)
    {
        PhxPath p = StdLVLPC / lvlName;
        bool bExists = p.Exists();
        if (!bExists)
        {
            Debug.LogErrorFormat("Could not find '{0}'!", p);
        }
        return bExists;
    }

    void Awake()
    {
        LibLog.SetLogLevel(ELibLogType.Warning);

        Debug.Assert(InitScreenPrefab     != null);
        Debug.Assert(LoadScreenPrefab     != null);
        Debug.Assert(MainMenuPrefab       != null);
        Debug.Assert(PauseMenuPrefab      != null);
        Debug.Assert(CharSelectTransform  != null);
        Debug.Assert(CharSelectPPVolume   != null);
        Debug.Assert(UIAudioMixer         != null);
        Debug.Assert(Camera               != null);
        Debug.Assert(GroundPhyMat         != null);
        Debug.Assert(HUDPrefab            != null);
        Debug.Assert(BoltPrefab           != null);
        Debug.Assert(BeamPrefab           != null);

        for (int i = 0; i < UIAudio.Length; ++i)
        {
            GameObject audioObj = new GameObject(string.Format("UIAudio{0}", i));
            audioObj.transform.SetParent(transform);
            UIAudio[i] = audioObj.AddComponent<AudioSource>();
            UIAudio[i].outputAudioMixerGroup = UIAudioMixer;
        }

        
#if UNIX
        if (PlayerPrefs.GetInt("PhoenixUnixRename") == 0)
        {
            // For Unix, ensure all folder- and file names are all lower case, starting from 'GameData'
            void LowerCaseRecursive(PhxPath p)
            {
                string[] files = Directory.GetFiles(p);
                for (int i = 0; i < files.Length; ++i)
                {
                    string fileName = new PhxPath(files[i]).GetLeaf();
                    if (fileName.ToLower() != fileName)
                    {
                        if (!File.Exists(p / fileName.ToLower()))
                        {
                            File.Move(p / fileName, p / fileName.ToLower());
                            Debug.Log($"Renamed '{p / fileName}' to '{p / fileName.ToLower()}'");
                        }
                    }
                }

                string[] subDirs = Directory.GetDirectories(p);
                for (int i = 0; i < subDirs.Length; ++i)
                {
                    string dirName = new PhxPath(subDirs[i]).GetLeaf();
                    if (dirName.ToLower() != dirName)
                    {
                        if (!Directory.Exists(p / dirName.ToLower()))
                        {
                            Directory.Move(p / dirName, p / dirName.ToLower());
                            Debug.Log($"Renamed '{p / dirName}' to '{p / dirName.ToLower()}'");
                        }
                    }

                    LowerCaseRecursive(p / dirName.ToLower());
                }
            }
            LowerCaseRecursive(GamePath / "GameData");
            PlayerPrefs.SetInt("PhoenixUnixRename", 1);
        }

#endif

        Init();
    }

    void Start()
    {
        //PhxProjectiles.Instance.InitProjectileMeshes();
    }

    void Update()
    {
        Env?.Tick(Time.deltaTime);

        while (LibLog.GetNextLog(out LibLogEntry entry))
        {
            switch (entry.Level)
            {
                case ELibLogType.Info:
                    Debug.Log($"[LibSWBF2] {entry}");
                    break;
                case ELibLogType.Warning:
                    Debug.LogWarning($"[LibSWBF2] {entry}");
                    break;

                case ELibLogType.Error:
                    Debug.LogError($"[LibSWBF2] {entry}");
                    break;
            }
        }    
    }

    void FixedUpdate()
    {
        Env?.TickPhysics(Time.fixedDeltaTime);
    }
}