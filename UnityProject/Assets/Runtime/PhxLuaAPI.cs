using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class PhxLuaAPI
{
	public class Unicode : Attribute { }

	static PhxGame GAME => PhxGame.Instance;
	static PhxEnvironment ENV => PhxGame.GetEnvironment();
	static PhxScene RTS => PhxGame.GetScene();
	static PhxMatch MT => PhxGame.GetMatch();
	static PhxLuaRuntime RT => PhxGame.GetLuaRuntime();
	static PhxTimerDB TDB => PhxGame.GetTimerDB();
	static Lua L => RT.GetLua();


	public static string ScriptCB_GetPlatform()
	{
		// "PC", "XBox", "PS2"
		return "PC";
	}

	public static (float, float, float, float) ScriptCB_GetScreenInfo()
	{
		return (Screen.width, Screen.height, 0.0f, Screen.width / Screen.height);
	}

	public static string ScriptCB_GetOnlineService()
	{
		return "GameSpy";
	}

	public static (string, int) ScriptCB_GetLanguage()
	{
		// TODO: second parameter '0' needs verification
		return ("english", 0);
	}

	public static int ScriptCB_GetCONMaxTimeLimit()
	{
		return 60 * 60;
	}

	public static int ScriptCB_GetCONNumBots()
	{
		return 0;
	}

	public static void ScriptCB_SetNumBots(int numBots)
	{

	}

	public static int ScriptCB_GetCTFNumBots()
	{
		return 0;
	}

	public static float ScriptCB_GetCTFMaxTimeLimit()
	{
		return 0.0f;
	}

	public static int ScriptCB_GetCTFCaptureLimit()
	{
		return 0;
	}

	public static void ScriptCB_SetAssaultScoreLimit()
    {

    }	

	public static int ScriptCB_GetHuntMaxTimeLimit()
    {
		return 0;
    }

	public static void ScriptCB_ShowHuntScoreLimit(int unkwn1)
    {

    }

	public static int ScriptCB_GetHuntScoreLimit()
    {
		return 0;
    }

	public static void ScriptCB_SetUberScoreLimit(int limit)
    {

    }

	public static int ScriptCB_GetUberScoreLimit()
    {
		return 0;
    }

	public static bool ScriptCB_IsMissionSetupSaved()
	{
		// I think this is the "galactic conquest" special items setup (e.g. sabotage, extra health, ...)
		// see setup_teams.lua:12
		return false;
	}

	public static int ScriptCB_LoadMissionSetup()
	{
		// I think this is the "galactic conquest" special items setup (e.g. sabotage, extra health, ...)
		// see setup_teams.lua:13
		return 0;
	}

	public static void ScriptCB_SetDopplerFactor(float factor)
	{

	}

	public static int ScriptCB_GetNumCameras()
	{
		// Apparently used to determine number of view ports in split screen
		// 1 = no split screen
		return 1;
	}

	[return: Unicode]
	public static string ScriptCB_getlocalizestr(string localizePath)
	{
		return ENV.GetLocalized(localizePath, false);
	}

	[return: Unicode]
	public static string ScriptCB_getlocalizestr(string localizePath, bool bReturnNullIfNotFound)
	{
		return ENV.GetLocalized(localizePath, bReturnNullIfNotFound);
	}

	[return: Unicode]
	public static string ScriptCB_tounicode(string ansiString)
	{
		string res = Encoding.Unicode.GetString(Encoding.Convert(Encoding.ASCII, Encoding.Unicode, Encoding.ASCII.GetBytes(ansiString)));
		return res;
	}

	public static string ScriptCB_ununicode([Unicode] string unicodeString)
	{
		string res = Encoding.ASCII.GetString(Encoding.Convert(Encoding.Unicode, Encoding.ASCII, Encoding.Unicode.GetBytes(unicodeString)));
		return res;
	}

	[return: Unicode]
	public static string ScriptCB_usprintf([Unicode] object[] args)
	{
		Debug.Assert(args.Length > 1);

		// first argument is the localize path, the rest are format placements
		string localizePath = args[0] as string;
		object[] placements = new object[args.Length - 1];
		Array.Copy(args, 1, placements, 0, placements.Length);

		string localized = ENV.GetLocalized(localizePath);
		string res = PhxHelpers.Format(localized, placements);
		return res;
	}

	public static bool ScriptCB_IsFileExist(string path)
	{
		PhxPath p =  GAME.StdLVLPC / path;
		return p.IsFile() && p.Exists();
	}

	public static void ScriptCB_OpenMovie(string mvsPath, string unkwn1)
	{

	}

	public static void ScriptCB_CloseMovie()
	{

	}

	public static void ScriptCB_PlayInGameMovie(string mvsName, string movieName)
	{

	}

	public static void ScriptCB_SetGameRules(string ruleName)
	{
		// Known values are:
		// - "campaign"
		// - "instantaction"
		// - "mp"
	}

	public static void ScriptCB_DoFile(string scriptName)
	{
		ENV.Execute(scriptName);
	}

	public static bool ScriptCB_AutoNetJoin()
	{
		return false;
	}

	public static void ScriptCB_SetAmHost(bool value)
	{
		// We are a networking Host
		// Create socket here?
	}

	public static bool ScriptCB_GetAmHost()
	{
		// Are we the host?
		return false;
	}

	public static void ScriptCB_SetInNetGame(bool value)
	{
		// Guess: Enabled or Disables multiplayer?
	}

	public static bool ScriptCB_InNetGame()
	{
		// Guess: Are we in a multiplayer game?
		return false;
	}

	public static bool ScriptCB_InNetSession()
	{
		// Guess: Are we connected to someone?
		return false;
	}

	public static void ScriptCB_SetDedicated(bool value)
	{
		// We are a dedicated networking Host ?
	}

	public static bool ScriptCB_IsDedicated()
	{
		return false;
	}

	public static bool ScriptCB_InMultiplayer()
	{
		return false;
	}

	public static bool ScriptCB_NetWasHost()
	{
		return false;
	}

	public static bool ScriptCB_NetWasDedicated()
	{
		return false;
	}

	public static bool ScriptCB_NetWasDedicatedQuit()
	{
		return false;
	}

	public static bool ScriptCB_NetWasClient()
	{
		return false;
	}

	public static bool ScriptCB_IsAutoNet()
	{
		return false;
	}

	// Unknown parameters and return value
	public static void ScriptCB_EndAutoNet()
	{

	}

	public static string ScriptCB_GetAutoNetMode()
	{
		// Unknown return values, appears only once in a print statement in ifs_mp_autonet.lua
		return "";
	}

	public static void ScriptCB_SetGameName(string name)
	{

	}

	public static string ScriptCB_GetGameName()
	{
		return "GAME";
	}

	public static bool ScriptCB_IsInShell()
	{
		return false;
	}

	public static void ScriptCB_OpenNetShell(bool value)
	{
		// false - login access only?
	}

	public static void ScriptCB_CloseNetShell(bool close)
	{
		// seems to be always called with 'true'
	}

	public static bool ScriptCB_IsNetworkOn()
	{
		return false;
	}

	public static bool ScriptCB_IsBootInvitePending()
    {
		return false;
    }

	public static bool ScriptCB_GetAutoAssignTeams()
    {
		// Whether auto assign teams is enabled or not
		return false;
    }

	public static void ScriptCB_SetConnectType(string type)
    {
		// type can be:
		//    - "lan"
		//    - "wan"
		//    - "direct"
    }

	public static string ScriptCB_GetConnectType()
    {
		return "lan";
    }

	public static void ScriptCB_EnableJournal()
    {

    }

	public static void ScriptCB_EnablePlayback()
    {

    }

	public static (int, string) ScriptCB_GetLatestError()
    {
		// return values are:
		//    - Error Level
		//        - Starting at 0, with 0 being 'info' or no error?
		//    - Error Message
		return (0, "");
    }

	public static (int, string) ScriptCB_GetError()
	{
		// return values are:
		//    - Error Level
		//        - Starting at 0, with 0 being 'info' or no error?
		//        - == 6  -->  'in session error'?
		//        - >= 8  -->  login error?
		//    - Error Message
		return (0, "");
	}

	public static void ScriptCB_ClearError()
    {

    }

	public static void SetPS2ModelMemory(int PS2Mem)
	{
		
	}

	public static void StealArtistHeap(int numBytes)
	{
		
	}

	public static void SetTeamAggressiveness(int teamIdx, float aggr)
	{
		
	}

	public static void SetMemoryPoolSize(string poolName, int size)
	{
		
	}

	public static void ClearWalkers()
	{
		
	}

	public static void AddWalkerType(int pairOfLegs, int unkwn1)
	{
		
	}
	public static int? AddAIGoal(int teamIdx, string goalName, float goalWeight)
	{
		return null;
	}

	public static int? AddAIGoal(int teamIdx, string goalName, float goalWeight, string captureRegion)
	{
		return null;
	}

	public static int? AddAIGoal(int teamIdx, string goalName, float goalWeight, string captureRegion, int flagPtr)
	{
		return null;
	}

	public static void DeleteAIGoal(int? goalPtr)
    {

    }

	public static void ClearAIGoals(int teamIdx)
    {

    }

	public static void SetAIVehicleNotifyRadius(float radius)
    {

    }

	public static void SetAIDifficulty(int teamIdx, int unkwn1, string difficulty)
    {
		// values for the difficulty string:
		// - "medium"
		// - "hard"
	}

	public static void AllowAISpawn(int teamIdx, bool allow)
    {

    }

	public static void SetSpawnDelay(float unkwn1, float unkwn2)
	{
		
	}

	public static void SetStayInTurrets(int toggle)
    {

    }

	public static void SetHeroClass(int teamIdx, string className)
	{
		MT.SetHeroClass(teamIdx, className);
	}

	public static void EnableSPScriptedHeroes()
    {

    }

    public static void EnableAIAutoBalance()
    {

    }

	public static void EnableFlyerPath(string pathName, bool enable)
    {
		// Examples:
		//   EnableFlyerPath('pickup', 0)
		//   EnableFlyerPath('capture', 0)
	}

	public static void SetTeamAsEnemy(int teamIdx1, int teamIdx2)
	{
		MT.SetTeamAsEnemy(teamIdx1, teamIdx2);
	}

	public static void SetTeamAsFriend(int teamIdx1, int teamIdx2)
	{
		MT.SetTeamAsFriend(teamIdx1, teamIdx2);
	}

	public static void SetTeamName(int teamIdx, string name)
	{
		MT.SetTeamName(teamIdx, name);
	}

	public static void SetTeamPoints(int teamIdx, int points)
    {

    }

	public static void SetUnitCount(int teamIdx, int numUnits)
	{
		MT.SetUnitCount(teamIdx, numUnits);
	}

	public static void AddUnitClass(int teamIdx, string className, int unitCountMin)
	{
		MT.AddUnitClass(teamIdx, className, unitCountMin);
	}

	public static void AddUnitClass(int teamIdx, string className, int unitCountMin, int unitCountMax)
	{
		MT.AddUnitClass(teamIdx, className, unitCountMin, unitCountMax);
	}

	public static void SetDenseEnvironment(string isDense)
	{
		// seems to be always called with string "false"
	}

	public static void SetMinFlyHeight(float height)
	{
		
	}

	public static void SetMaxFlyHeight(float height)
	{
		
	}

	public static void SetMinPlayerFlyHeight(float height)
    {

    }

	public static void SetMaxPlayerFlyHeight(float height)
	{
		
	}

	public static void SetAttackingTeam(int teamIdx)
	{
		
	}

	public static void AddCameraShot(float quatW, float quatX, float quatY, float quatZ, float posX, float posY, float posZ)
	{
		RTS.AddCameraShot(
			UnityUtils.Vec3FromLibWorld( new LibSWBF2.Types.Vector3 { X = posX,  Y = posY,  Z = posZ } ),
			UnityUtils.QuatFromLibSkel( new LibSWBF2.Types.Vector4 { X = quatX, Y = quatY, Z = quatZ, W = quatW } )
		);
	}

	public static void SetTeamIcon(int teamIdx, string iconName, string hudIconName, string flagIconName)
	{
		
	}

	public static void SetTeamIcon(int teamIdx, string iconName)
	{
		MT.SetTeamIcon(teamIdx, iconName);
	}

	public static void SetBleedRate(int teamIdx, float rate)
	{
		// a.k.a. reinforcement losses per second (e.g. 0.33)
	}

	public static int GetReinforcementCount(int teamIdx)
	{
		return MT.GetReinforcementCount(teamIdx);
	}

	public static void SetReinforcementCount(int teamIdx, int count)
	{
		MT.SetReinforcementCount(teamIdx, count);
	}

	public static void AddReinforcements(int teamIdx, int count)
	{
		MT.AddReinforcements(teamIdx, count);
	}

	public static int OpenAudioStream(string lvlPath, string streamName)
	{
		// TODO: what exactly does this function return? An audio stream object?
		return 0;
	}

	public static void AudioStreamAppendSegments(string lvlPath, string voiceOverName, int audioStream)
	{
		
	}

	public static void SetBleedingVoiceOver(int teamIdx1, int teamIdx2, string voiceOverName, int unkwn1)
	{
		
	}

	public static void SetLowReinforcementsVoiceOver(int teamIdx1, int teamIdx2, string voiceOverName, float unkwn1, int unkwn2)
	{
		
	}

	public static void SetOutOfBoundsVoiceOver(int teamIdx, string soundName)
	{
		
	}

	public static void BroadcastVoiceOver(string voName, int teamIdx)
    {

    }

	public static void SetAmbientMusic(int teamIdx, float unkwn1, string musicName, int unkwn2, int unkwn3)
	{
		
	}

	public static void SetVictoryMusic(int teamIdx, string soundName)
	{
		
	}

	public static void SetDefeatMusic(int teamIdx, string soundName)
	{
		
	}

	public static void SetSoundEffect(string eventName, string soundName)
	{
		
	}

	public static void ScaleSoundParameter(string soundName, string paramName, float scale)
    {

    }

	public static void SetMapNorthAngle(int unkwn1)
	{

	}

	public static void SetMapNorthAngle(float angle, int unkwn1)
	{
		
	}

	public static void AISnipeSuitabilityDist(float distance)
	{
		
	}

	public static void FillAsteroidRegion(string regionName, string asteroidClass, int numAsteroids, 
										float u0, float u1, float u2, 
										float v0, float v1, float v2)
	{

	}

	public static void EnableSPHeroRules()
	{
		
	}

	public static void AddDeathRegion(string regionName)
	{
		
	}

	public static void AddLandingRegion(string regionName)
    {

    }

	public static void SetProperty(string instName, string propName, object propValue)
	{
		PhxScene scene = PhxGame.GetScene();
		// Debug.LogFormat("Setting property: {0} of instance: {1} to value: {2}", propName, instName, propValue);
		scene?.SetProperty(instName, propName, propValue);
	}

	public static void SetClassProperty(string className, string propName, object propValue)
	{
		PhxScene scene = PhxGame.GetScene();
		scene?.SetClassProperty(className, propName, propValue);
	}

	public static void SetObjectTeam(string instName, int teamIdx)
    {
		SetProperty(instName, "Team", teamIdx);
	}

	public static float GetObjectHealth(string instName)
    {
		return 0f;
    }

	public static void DisableBarriers(string barrierName)
	{
		
	}

	public static void EnableBarriers(string barrierName)
	{
		
	}

	public static void PlayAnimation(string animName)
	{
		RTS.Animator.PlayAnimation(animName.ToLower());
	}

	public static void PlayAnimationFromTo(string animName, float start, float end)
	{
		
	}

	public static void PauseAnimation(string animName)
    {
		RTS.Animator.PauseAnimation(animName.ToLower());
    }

	public static void RewindAnimation(string animName)
	{
		RTS.Animator.RewindAnimation(animName.ToLower());
	}

	public static void BlockPlanningGraphArcs(string planNodeName)
	{

	}

	public static void BlockPlanningGraphArcs(int planNode)
	{

	}

	public static void UnblockPlanningGraphArcs(string planNodeName)
    {

    }

	public static void UnblockPlanningGraphArcs(int planNode)
	{

	}

	public static void SetUberMode(bool enable)
	{
		Debug.Log(enable ? "Enabled" : "Disabled" + " Uber mode");
	}

	public static void SetGroundFlyerMap(bool enable)
    {

    }

	public static void SetDefenderSnipeRange(float range)
    {

    }

	public static int GetCommandPostTeam(int? cpPtr)
    {
		if (!cpPtr.HasValue)
        {
			return 0;
        }			
		PhxCommandpost cp = RTS.GetInstance<PhxCommandpost>(cpPtr.Value);
		if (cp != null)
        {
			return cp.Team;
        }
		Debug.LogWarning($"Illegal CommandPost Lua pointer '{cpPtr.Value}'!");
		return 0;
    }

	public static float GetCommandPostBleedValue(string cpName, int teamIdx)
	{
		return 0.0f;
	}

	public static void AICanCaptureCP(string cpName, int teamIdx, bool canCapture)
    {

    }

	public static void MapHideCommandPosts()
    {
		MapHideCommandPosts(true);
	}

	public static void MapHideCommandPosts(bool hide)
	{

	}

	public static void SetFlagGameplayType(string typeName)
    {

    }

	public static void SetAIViewMultiplier(float multiplier)
    {

    }

	public static void AddMissionObjective(int teamIdx, string localizePath)
	{

	}

	public static void AddMissionObjective(int teamIdx, string colorName, string localizePath)
	{

	}

	public static void ActivateObjective(string objectiveName)
    {

    }

	public static void CompleteObjective(string objectiveName)
	{

	}

	public static void MissionVictory(object teams)
    {
		// teams can either be one int (1), or a table of ints {1,2}
		Debug.Log($"Team '{teams}' wins!");
	}

	public static int? CreateTimer(string timerName)
    {
		return TDB.CreateTimer(timerName);
    }

	public static void DestroyTimer(int? timer)
	{
		if (MT.ShowTimer == timer)
        {
			MT.ShowTimer = null;
		}
		TDB.DestroyTimer(timer);
	}

	public static void StartTimer(int? timer)
	{
		TDB.StartTimer(timer);
	}

	public static void StopTimer(int? timer)
    {
		TDB.StopTimer(timer);
	}

	public static void SetTimerRate(int? timer, float rate)
	{
		TDB.SetTimerRate(timer, rate);
	}

	public static void SetTimerValue(int? timer, float value)
    {
		TDB.SetTimerValue(timer, value);
	}

	public static void ReleaseTimerElapse(int? timerCallback)
    {
		// Some scripts call this to notify their timer callback
		// to be obsolete, so that it can be deleted.
		// Example:

		//eventPtr = OnTimerElapse(
		//	function()
		//		ReleaseTimerElapse(eventPtr)
		//		eventPtr = nil
		//	end,
		//	some_timer
		//)
	}

	public static void ShowTimer(int? timer)
    {
		// only one neutral timer can be shown at a time.
		// when called with 'nil', hide the timer
		MT.ShowTimer = timer;
	}

	public static void SetDefeatTimer(int? timer, int teamIdx)
    {
		// only one defeat/victory timer can be shown at a time.
		// when called with 'nil', hide the timer
		MT.SetDefeatTimer(timer, teamIdx);
	}

	public static void SetVictoryTimer(int? timer, int teamIdx)
	{
		// only one victory/defeat timer can be shown at a time.
		// when called with 'nil', hide the timer
		MT.SetVictoryTimer(timer, teamIdx);
	}

	public static int? FindTimer(string timerName)
    {
		return TDB.FindTimer(timerName);
    }

	public static int GetObjectTeam(string objName)
    {
		PhxInstance inst = RTS.GetInstance<PhxInstance>(objName);
		return inst != null ? inst.Team : 0;
    }

	public static int GetObjectTeam(int objPtr)
	{
		return RTS.GetInstance<PhxInstance>(objPtr).Team;
	}

	public static bool IsObjectAlive(string objName)
	{
		return RTS.IsObjectAlive(objName);
	}

	public static void KillObject(string objName)
    {

    }

	public static void SetNumBirdTypes(int num)
    {

    }

	public static void SetBirdType(int birdIdx, float unkwn1, string typeName)
    {

    }

	public static void SetNumFishTypes(int num)
    {

    }

	public static void SetFishType(int fishIdx, float unkwn1, string typeName)
	{

	}

	public static void SetAllowBlindJetJumps(int num)
	{

	}

	public static void SetAIDamageThreshold(string objName, float threshold)
    {

    }

	public static void SetParticleLODBias(int bias)
    {

    }

	public static void SetMaxCollisionDistance(float distance)
	{

	}

	public static void SetWorldExtents(float distance)
	{

	}

	public static int GetRegion(string regionName)
    {
		return 0;
    }

	public static string GetRegionName(int region)
	{
		return "";
	}

	public static void ActivateRegion(string regionName)
	{

    }

	public static void DeactivateRegion(string regionName)
	{

	}

	public static void ReleaseEnterRegion(int regionEventRef)
    {

    }

	public static void ShowTeamPoints(int teamIdx, bool show)
    {

    }

	public static int? GetObjectPtr(string objName)
    {
		return RTS.GetInstanceIndex(objName);
	}

	public static string GetEntityName(int? objPtr)
    {
		if (objPtr.HasValue)
        {
			return RTS.GetInstance<PhxInstance>(objPtr.Value).name;
        }
		return null;
    }

	public static void MapAddEntityMarker(string objName, string iconName, float size, int teamIdx, string color, bool unkwn1)
    {

    }

	public static void MapAddEntityMarker(string objName, string iconName, float size, int teamIdx, string color, bool unkwn1, bool unkwn2, bool unkwn3)
	{

	}

	public static void MapAddEntityMarker(string objName, string iconName, float size, int teamIdx, string color, bool unkwn1, bool unkwn2, bool unkwn3, bool unkwn4)
	{

	}

	public static void MapRemoveEntityMarker(object objectPtr, int teamIdx)
    {

    }

	public static void MapRemoveRegionMarker(int region)
	{

	}

	public static void MapRemoveRegionMarker(string regionName)
    {

    }

	public static int GetFlagCarrier(string flagName)
    {
		return 0;
    }

	public static void SpaceAssaultEnable(bool enable)
    {

    }

	public static void SpaceAssaultSetupBitmaps(
		object shipBitmapATT, object shipBitmapDEF,
		object shieldBitmapATT, object shieldBitmapDEF,
		object criticalSystemBitmapATT, object criticalSystemBitmapDEF)
	{

	}

	public static void SpaceAssaultAddCriticalSystem(string name, float pointValue, float hudPosX, float hudPosY)
    {
		SpaceAssaultAddCriticalSystem(name, pointValue, hudPosX, hudPosY, true);
	}

	public static void SpaceAssaultAddCriticalSystem(string name, float pointValue, float hudPosX, float hudPosY, bool displayHudMarker)
	{

	}

	public static void SpaceAssaultLinkCriticalSystems(object obj)
    {

    }

	public static void AddSpaceAssaultDestroyPoints(object killer, string instName)
    {

    }

	public static void EnableBuildingLockOn(string instName, bool lockOn)
    {

    }

	public static void DisableSmallMapMiniMap()
    {

    }

	public static void ReadDataFile(params object[] args)
	{
		// NOTE: ReadDataFile has dynamic parameters and can be either called like:
		// - ReadDataFile("file.lvl", "subLVL1", "subLVL2", ...)
		// - ReadDataFile("file.lvl;subLVL1;subLVL2")
		// or potentially as a mixture. Though I personally didn't see a mixture yet.

		string path = "";

		List<string> subLVLs = new List<string>();
		for (int i = 0; i < args.Length; ++i)
        {
			string arg = (string)args[i];
			string[] splits = arg.Split(';');
			subLVLs.AddRange(splits);

			if (i == 0)
            {
				path = splits[0];
				subLVLs.RemoveAt(0);
			}
		}

		bool bLoadFromAddon = path.StartsWith("dc:", StringComparison.InvariantCultureIgnoreCase);
		if (bLoadFromAddon)
        {
			path = path.Remove(0, 3);
		}

		/*
		string subLVLsStr = "";
		foreach (string subLVL in subLVLs)
		{
			subLVLsStr += (subLVL + ", ");
		}
		Debug.LogFormat("Called ReadDataFile from path: '{0}' with subLVLs: {1}", path, subLVLsStr);
		*/

		ENV.ScheduleRel(path, subLVLs.ToArray(), bLoadFromAddon);
	}

	public static void AddDownloadableContent(string threeLetterName, string scriptName, int levelMemoryModifier)
	{
		GAME.RegisterAddonScript(scriptName, threeLetterName);
	}




	// ===============================================================================================================
	// Event Callbacks
	// ===============================================================================================================

	public static void OnCharacterDeath(PhxLuaRuntime.LFunction callback)
    {
		
    }
	public static void OnCharacterDeathTeam(PhxLuaRuntime.LFunction callback, int teamIdx)
	{
		
	}
	public static void OnTicketCountChange(PhxLuaRuntime.LFunction callback)
	{
		// TicketCount seems to be the reinforcement count, see Objective.lua:192
	}
	public static void OnTimerElapse(PhxLuaRuntime.LFunction callback, int timer)
	{
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnTimerElapse, callback, timer);
	}
	public static void OnEnterRegion(PhxLuaRuntime.LFunction callback, string regionName)
    {
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnEnterRegion, callback, regionName);
	}
	public static void OnEnterRegionTeam(PhxLuaRuntime.LFunction callback, string regionName, int teamIdx)
	{
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnEnterRegionTeam, callback, (regionName, teamIdx));
		// callback parameters:
		// - region    (string?)
		// - carrier   (ptr?)
	}
	public static void OnLeaveRegion(PhxLuaRuntime.LFunction callback, string regionName)
    {
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnLeaveRegion, callback, regionName);

	}
	public static void OnFinishCapture(PhxLuaRuntime.LFunction callback)
	{
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnFinishCapture, callback);
		// callback paramters:
		// - postPtr
	}
	public static void OnFinishCaptureName(PhxLuaRuntime.LFunction callback, string cpName)
	{
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnFinishCaptureName, callback, cpName);
		// callback paramters:
		// - postPtr
	}
	public static void OnFinishCaptureTeam(PhxLuaRuntime.LFunction callback, int teamIdx)
	{
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnFinishCaptureTeam, callback, teamIdx);
		// callback paramters:
		// - postPtr
	}
	public static void OnFinishNeutralize(PhxLuaRuntime.LFunction callback)
	{
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnFinishNeutralize, callback);
		// callback paramters:
		// - postPtr
	}
	public static void OnCommandPostRespawn(PhxLuaRuntime.LFunction callback)
	{
		// callback paramters:
		// - postPtr
	}
	public static void OnCommandPostKill(PhxLuaRuntime.LFunction callback)
	{
		// callback paramters:
		// - postPtr
	}
	public static void OnObjectKill(PhxLuaRuntime.LFunction callback)
    {

    }
	public static void OnObjectKillName(PhxLuaRuntime.LFunction callback, string objName)
	{
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnObjectKillName, callback, objName.ToLower());
	}
	public static void OnObjectKillTeam(PhxLuaRuntime.LFunction callback, int teamIdx)
	{
		
	}
	public static void OnObjectKillClass(PhxLuaRuntime.LFunction callback, string className)
	{
		
	}
	public static void OnObjectRespawnName(PhxLuaRuntime.LFunction callback, string objName)
	{
		PhxLuaEvents.Register(PhxLuaEvents.Event.OnObjectRespawnName, callback, objName.ToLower());		
	}

	public static void OnObjectRepair(PhxLuaRuntime.LFunction callback)
    {

    }

	public static void OnObjectRepairName(PhxLuaRuntime.LFunction callback, string objName)
	{
		// callback paramters:
		// - objPtr
		// - characterId
	}

	public static void OnObjectDamageName(PhxLuaRuntime.LFunction callback, string objName)
    {

    }

	public static void OnTeamPointsChange(PhxLuaRuntime.LFunction callback)
    {

    }

	public static void OnTeamPointsChangeTeam(PhxLuaRuntime.LFunction callback, int teamIdx)
	{

	}
}

public static class PhxLuaEvents
{
	static PhxEnvironment ENV { get { return PhxGame.GetEnvironment(); } }
	static PhxLuaRuntime RT { get { return PhxGame.GetLuaRuntime(); } }
	static Lua L { get { return RT.GetLua(); } }

	public enum Event
	{
		OnEnterRegion,
		OnEnterRegionTeam,
		OnLeaveRegion,
		OnTimerElapse,
		OnFinishCapture,
		OnFinishCaptureName,
		OnFinishCaptureTeam,
		OnFinishNeutralize,
		OnObjectKillName,
		OnObjectRespawnName,
	}

	/// <summary>
	/// Maps parameterized callbacks. For example, Someone in Lua could call something like 'OnEnterRegion', 
	/// providing a callback and the name of the region this event should react to.
	/// Hence, we need to store who wants to listen to what exactly.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	class CallbackDict<T>
    {
		Dictionary<T, List<PhxLuaRuntime.LFunction>> Callbacks = new Dictionary<T, List<PhxLuaRuntime.LFunction>>();

		public int AddCallback(T key, PhxLuaRuntime.LFunction callback)
		{
			if (Callbacks.TryGetValue(key, out List<PhxLuaRuntime.LFunction> callbacks))
			{
				callbacks.Add(callback);
				return callbacks.Count - 1;
			}
			callbacks = new List<PhxLuaRuntime.LFunction>() { callback };
			Callbacks.Add(key, callbacks);
			return callbacks.Count - 1;
		}

		public void RemoveCallback(T key, int idx)
        {
			if (Callbacks.TryGetValue(key, out List<PhxLuaRuntime.LFunction> callbacks))
			{
				callbacks.RemoveAt(idx);
			}
		}

		public void Invoke(T key, object[] args)
        {
			if (Callbacks.TryGetValue(key, out List<PhxLuaRuntime.LFunction> callbacks))
			{
				for (int i = 0; i < callbacks.Count; ++i)
                {
					callbacks[i].Invoke(args);
				}
			}
		}
	}

	static Dictionary<Event, CallbackDict<object>> ParameterizedCallbacks = new Dictionary<Event, CallbackDict<object>>();
	static Dictionary<Event, List<PhxLuaRuntime.LFunction>> Callbacks = new Dictionary<Event, List<PhxLuaRuntime.LFunction>>();


	static CallbackDict<object> Get(Event ev)
	{
		if (ParameterizedCallbacks.TryGetValue(ev, out CallbackDict<object> inner))
		{
			return inner;
		}

		inner = new CallbackDict<object>();
		ParameterizedCallbacks.Add(ev, inner);
		return inner;
	}

	public static void Clear()
    {
		ParameterizedCallbacks.Clear();
		Callbacks.Clear();
	}

	public static void Register(Event ev, PhxLuaRuntime.LFunction callback)
	{
		if (Callbacks.TryGetValue(ev, out var callbacks))
        {
			callbacks.Add(callback);
			return;
		}
		Callbacks.Add(ev, new List<PhxLuaRuntime.LFunction>() { callback });
	}

	public static void Register(Event ev, PhxLuaRuntime.LFunction callback, object key)
	{
		Get(ev).AddCallback(key, callback);
	}

	// To be called by environment
	public static void Invoke(Event ev, params object[] eventArgs)
	{
		if (Callbacks.TryGetValue(ev, out List<PhxLuaRuntime.LFunction> callbacks))
		{
			for (int i = 0; i < callbacks.Count; ++i)
			{
				callbacks[i].Invoke(eventArgs);
				Debug.Log($"Invoked Lua callback for '{ev}'");
			}
		}
		else 
		{
			Debug.Log($"Failed to find callback for '{ev}'");
		}
	}

	// To be called by environment
	public static void InvokeParameterized(Event ev, object key, params object[] eventArgs)
    {
		Get(ev).Invoke(key, eventArgs);
	}
}