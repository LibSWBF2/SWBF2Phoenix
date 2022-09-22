using System;
using System.Text;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;


using lua_State_ptr = System.IntPtr;
using lua_Debug_ptr = System.IntPtr;
using char_ptr = System.IntPtr;
using void_ptr = System.IntPtr;
using size_t = System.UInt64;
using luaL_reg_ptr = System.IntPtr;
using luaL_Buffer_ptr = System.IntPtr;


[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
public struct LuaDebug
{
	int Event;
	string Name;
	string Namewhat;
	string What;
	string Source;
	int CurrentLine;
	int Nups;
	int LineDefined;

	[MarshalAs(UnmanagedType.ByValArray, SizeConst = Lua.LUA_IDSIZE)]
	byte[] Short_src;

	int I_ci;
}

// Don't permanently store references of these! 
public sealed class Lua
{
	public Action<string> OnError;

	public int UsedCallbackCount { get; private set; }

	lua_State_ptr L;
	static Dictionary<lua_State_ptr, WeakReference<Lua>> LuaInstances = new Dictionary<lua_State_ptr, WeakReference<Lua>>();


	// This is an utterly disgusting solution...
	// Since IL2CPP does NOT support marshalling non-static functions, we cannot create lambda callbacks 
	// (which would've made this 100 times easier and is in fact how this was handled before I tried to 
	// actually build this project...). It also does NOT support creating methods at runtime using 
	// DynamicMethod and the ILGenerator. Since we only can marshal static functions, we cannot provide 
	// just one single static function, since that'll be called for ALL callbacks ever registered and the 
	// only way to differentiate is the actual function pointer, which is always the same in that scenario.
	// As a stupid workaround, we provide an arbitrary pool of predefined static functions, with every one 
	// of them only executing the associated function provided in this map below.
	public const int CallbackMapSize = 250;
	static readonly CFunction[] CallbackMap = new CFunction[CallbackMapSize];


	public Lua()
	{
		L = LuaWrapper.lua_open();
		LuaInstances.Add(L, new WeakReference<Lua>(this));
	}

	public void Close()
    {
		if (L != lua_State_ptr.Zero)
        {
			LuaWrapper.lua_close(L);
			LuaInstances.Remove(L);
			L = lua_State_ptr.Zero;
			Array.Clear(CallbackMap, 0, CallbackMap.Length);
        }
	}

	Lua(lua_State_ptr l)
	{
		L = l;
		LuaInstances.Add(L, new WeakReference<Lua>(this));
	}

	~Lua()
	{
		Close();
	}

	static Lua GetLuaInstance(lua_State_ptr l)
	{
		if (LuaInstances.TryGetValue(l, out WeakReference<Lua> luaRef))
		{
			if (luaRef.TryGetTarget(out Lua lua))
			{
				return lua;
			}
			LuaInstances.Remove(l);
		}
		return new Lua(l);
	}
	LuaWrapper.lua_CFunction CB_Function(CFunction fn)
	{
		// TODO: linear search is baaad. 
		// But does it really matter here?
		int i = 0;
		while (i < CallbackMapSize)
		{
			if (CallbackMap[i] == null) break;
			i++;
		}
		if (i >= CallbackMapSize)
		{
			OnError?.Invoke($"Exceeded static callbacks of {CallbackMapSize}!");
			return null;
		}
		UsedCallbackCount++;
		CallbackMap[i] = fn;
		return ProvidedCallbacks[i];
	}

	CFunction CB_Function(LuaWrapper.lua_CFunction fn)
	{
		return (Lua L) =>
		{
			return fn(L.L);
		};
	}

	//LuaWrapper.lua_Chunkreader CB_Chunkreader(Chunkreader fn)
	//{
	//	return (lua_State_ptr L, void_ptr ud, out size_t sz) =>
	//	{
	//		byte[] chunk = fn(LuaInstances[L], ud, out sz);
	//		return fn(LuaInstances[L], ud, out sz);
	//	};
	//}


	/* 
	** ===============================================================
	** lua.h
	** ===============================================================
	*/

	public delegate int CFunction(Lua L);
	//public delegate byte[] Chunkreader(Lua L, void_ptr ud, out size_t sz);
	//public delegate int Chunkwriter(Lua L, void_ptr p, size_t sz, void_ptr ud);
	public delegate void Hook(Lua L, LuaDebug ar);

	public enum ValueType : int
	{
		NONE = -1,
		NIL = 0,
		BOOLEAN = 1,
		LIGHTUSERDATA = 2,
		NUMBER = 3,
		STRING = 4,
		TABLE = 5,
		FUNCTION = 6,
		USERDATA = 7,
		THREAD = 8
	}

	public enum ErrorCode : int
	{
		NONE = 0,
		RUN = 1,
		FILE = 2,
		SYNTAX = 3,
		MEM = 4,
		ERR = 5
	}

	public enum EventCode : int
	{
		CALL = 0,
		RET = 1,
		LINE = 2,
		COUNT = 3,
		TAILRET = 4
	}

	[System.Flags]
	public enum HookMask : int
	{
		DISABLED = 0,
		CALL = 1 << EventCode.CALL,
		RET = 1 << EventCode.RET,
		LINE = 1 << EventCode.LINE,
		COUNT = 1 << EventCode.COUNT,
	}

	public const int LUA_IDSIZE = 60;
	public const int LUA_REGISTRYINDEX = -10000;
	public const int LUA_GLOBALSINDEX = -10001;

	static Hook HookFn = null;
	static void CB_Hook(lua_State_ptr l, lua_Debug_ptr ar)
	{
		HookFn?.Invoke(GetLuaInstance(l), Marshal.PtrToStructure<LuaDebug>(ar));
	}

	//Hook CB_Hook(LuaWrapper.lua_Hook fn)
	//{
	//	return (Lua L, lua_Debug_ptr ar) =>
	//	{
	//		fn(L.L, ar);
	//	};
	//}

	public void AtPanic(CFunction panicf)
	{
		LuaWrapper.lua_atpanic(L, CB_Function(panicf));
	}

	/*
    ** basic stack manipulation
    */

	public int GetTop()
	{
		return LuaWrapper.lua_gettop(L);
	}
	public void SetTop(int idx)
	{
		LuaWrapper.lua_settop(L, idx);
	}
	public void PushValue(int idx)
	{
		LuaWrapper.lua_pushvalue(L, idx);
	}
	public void Remove(int idx)
	{
		LuaWrapper.lua_remove(L, idx);
	}
	public void Insert(int idx)
	{
		LuaWrapper.lua_insert(L, idx);
	}
	public void Replace(int idx)
	{
		LuaWrapper.lua_replace(L, idx);
	}
	public bool CheckStack(int sz)
	{
		return LuaWrapper.lua_checkstack(L, sz) != 0;
	}

	public void XMove(Lua other, int n)
	{
		LuaWrapper.lua_xmove(L, other.L, n);
	}

	/*
    ** access functions (stack -> C)
    */


	public bool IsNumber(int idx)
	{
		return LuaWrapper.lua_isnumber(L, idx) != 0;
	}
	public bool IsString(int idx)
	{
		return LuaWrapper.lua_isstring(L, idx) != 0;
	}
	public bool IsCFunction(int idx)
	{
		return LuaWrapper.lua_iscfunction(L, idx) != 0;
	}
	public bool IsUserData(int idx)
	{
		return LuaWrapper.lua_isuserdata(L, idx) != 0;
	}
	public ValueType Type(int idx)
	{
		return (ValueType)LuaWrapper.lua_type(L, idx);
	}
	public string TypeName(int tp)
	{
		return LuaWrapper.lua_typename(L, tp);
	}

	public bool Equal(int idx1, int idx2)
	{
		return LuaWrapper.lua_equal(L, idx1, idx2) != 0;
	}
	public bool RawEqual(int idx1, int idx2)
	{
		return LuaWrapper.lua_rawequal(L, idx1, idx2) != 0;
	}
	public bool LessThan(int idx1, int idx2)
	{
		return LuaWrapper.lua_lessthan(L, idx1, idx2) != 0;
	}

	public float ToNumber(int idx)
	{
		return LuaWrapper.lua_tonumber(L, idx);
	}
	public bool ToBoolean(int idx)
	{
		return LuaWrapper.lua_toboolean(L, idx) != 0;
	}
	public string ToString(int idx)
	{
		return Marshal.PtrToStringAnsi(LuaWrapper.lua_tostring(L, idx));
	}
	public string ToStringUnicode(int idx)
	{
		char_ptr uniPtr = LuaWrapper.lua_tostring(L, idx);
		int len = (int)LuaWrapper.lua_strlen(L, idx);
		byte[] uniChars = new byte[len];
		Marshal.Copy(uniPtr, uniChars, 0, len);

		// HACK: For some reason, lua puts ascii strings in here,
		// while in the Unity Editor it's unicode as expected...
		// More debugging needed!
		bool IsAscii(byte[] chars)
        {
			for (int i = 0; i < chars.Length; ++i)
            {
				if (chars[i] < 32 || chars[i] > 126)
                {
					return false;
                }
            }
			return true;
        }

		string res;
		if (IsAscii(uniChars))
        {
			res = Encoding.ASCII.GetString(uniChars);
        }
        else
        {
			res = Encoding.Unicode.GetString(uniChars);
		}
		return res;
	}
	public ulong StrLen(int idx)
	{
		return LuaWrapper.lua_strlen(L, idx);
	}
	public CFunction ToCFunction(int idx)
	{
		return CB_Function(LuaWrapper.lua_tocfunction(L, idx));
	}
	public void_ptr ToUserData(int idx)
	{
		return LuaWrapper.lua_touserdata(L, idx);
	}
	public lua_State_ptr ToThread(int idx)
	{
		return LuaWrapper.lua_tothread(L, idx);
	}
	public void_ptr ToPointer(int idx)
	{
		return LuaWrapper.lua_topointer(L, idx);
	}

	/*
    ** push functions (C -> stack)
    */

	public void PushNil()
	{
		LuaWrapper.lua_pushnil(L);
	}
	public void PushNumber(float n)
	{
		LuaWrapper.lua_pushnumber(L, n);
	}
	public void PushString(string s)
	{
		LuaWrapper.lua_pushstring(L, s);
	}
	public void PushStringUnicode(string s)
	{
		byte[] uniChars = Encoding.Unicode.GetBytes(s);
		char_ptr str = Marshal.AllocHGlobal(uniChars.Length);
		Marshal.Copy(uniChars, 0, str, uniChars.Length);
		LuaWrapper.lua_pushlstring(L, str, (ulong)uniChars.Length);
		Marshal.FreeHGlobal(str);
	}
	public void PushCClosure(CFunction fn, int n)
	{
		LuaWrapper.lua_pushcclosure(L, CB_Function(fn), n);
	}
	public void PushBoolean(bool b)
	{
		LuaWrapper.lua_pushboolean(L, b ? 1 : 0);
	}
    public void PushLightUserData(void_ptr ptr)
    {
        LuaWrapper.lua_pushlightuserdata(L, ptr);
    }

    /*
    ** get functions (Lua -> stack)
    */

    public void GetTable(int idx)
	{
		LuaWrapper.lua_gettable(L, idx);
	}
	public void RawGet(int idx)
	{
		LuaWrapper.lua_rawget(L, idx);
	}
	public void RawGetI(int idx, int n)
	{
		LuaWrapper.lua_rawgeti(L, idx, n);
	}
	public void NewTable()
	{
		LuaWrapper.lua_newtable(L);
	}
	public void_ptr NewUserData(size_t sz)
	{
		return LuaWrapper.lua_newuserdata(L, sz);
	}
	public bool GetMetaTable(int objindex)
	{
		return LuaWrapper.lua_getmetatable(L, objindex) != 0;
	}
	public void GetFenv(int idx)
	{
		LuaWrapper.lua_getfenv(L, idx);
	}

	/*
    ** set functions (stack -> Lua)
    */

	public void SetTable(int idx)
	{
		LuaWrapper.lua_settable(L, idx);
	}
	public void RawSet(int idx)
	{
		LuaWrapper.lua_rawset(L, idx);
	}
	public void RawSetI(int idx, int n)
	{
		LuaWrapper.lua_rawseti(L, idx, n);
	}
	public bool SetMetaTable(int objindex)
	{
		return LuaWrapper.lua_setmetatable(L, objindex) != 0;
	}
	public bool SetFenv(int idx)
	{
		return LuaWrapper.lua_setfenv(L, idx) != 0;
	}

	/*
    ** `load' and `call' functions (load and run Lua code)
    */

	public void Call(int nargs, int nresults)
	{
		LuaWrapper.lua_call(L, nargs, nresults);
	}
	public ErrorCode PCall(int nargs, int nresults, int errfunc)
	{
		return (ErrorCode)LuaWrapper.lua_pcall(L, nargs, nresults, errfunc);
	}
	public ErrorCode CPCall(CFunction func, void_ptr ud)
	{
		return (ErrorCode)LuaWrapper.lua_cpcall(L, CB_Function(func), ud);
	}
	//public ErrorCode Load(Chunkreader reader, void_ptr dt, string chunkname)
	//{
	//	char_ptr str = Marshal.StringToHGlobalAnsi(chunkname);
	//	int res = LuaWrapper.lua_load(L, reader, dt, str);
	//	Marshal.FreeHGlobal(str);
	//	return (ErrorCode)res;
	//}

	//public int Dump(Chunkwriter writer, void_ptr data)
	//{
	//	return LuaWrapper.lua_dump(L, writer, data);
	//}

	/*
    ** coroutine functions
    */

	public ErrorCode Yield(int nresults)
	{
		return (ErrorCode)LuaWrapper.lua_yield(L, nresults);
	}
	public ErrorCode Resume(int narg)
	{
		return (ErrorCode)LuaWrapper.lua_resume(L, narg);
	}
	/*
    ** garbage-collection functions
    */

	public int GetGCThreshold()
	{
		return LuaWrapper.lua_getgcthreshold(L);
	}
	public int GetGCCount()
	{
		return LuaWrapper.lua_getgccount(L);
	}
	public void SetGCThreshold(int newthreshold)
	{
		LuaWrapper.lua_setgcthreshold(L, newthreshold);
	}
	/*
    ** miscellaneous functions
    */


	public static string Version()
	{
		return LuaWrapper.lua_version();
	}

	public int Error()
	{
		return LuaWrapper.lua_error(L);
	}

	public bool Next(int idx)
	{
		return LuaWrapper.lua_next(L, idx) != 0;
	}

	public void Concat(int n)
	{
		LuaWrapper.lua_concat(L, n);
	}

	public int PushUpValues()
	{
		return LuaWrapper.lua_pushupvalues(L);
	}

	public bool GetStack(int level, lua_Debug_ptr ar)
	{
		return LuaWrapper.lua_getstack(L, level, ar) != 0;
	}
	public bool GetInfo(string what, lua_Debug_ptr ar)
	{
		return LuaWrapper.lua_getinfo(L, what, ar) != 0;
	}
	public string GetLocal(lua_Debug_ptr ar, int n)
	{
		return LuaWrapper.lua_getlocal(L, ar, n);
	}
	public string SetLocal(lua_Debug_ptr ar, int n)
	{
		return LuaWrapper.lua_setlocal(L, ar, n);
	}
	public string GetUpValue(int funcindex, int n)
	{
		return LuaWrapper.lua_getupvalue(L, funcindex, n);
	}
	public string SetUpValue(int funcindex, int n)
	{
		return LuaWrapper.lua_setupvalue(L, funcindex, n);
	}

	public int SetHook(Hook func, HookMask mask, int count)
	{
		HookFn = func;
		return LuaWrapper.lua_sethook(L, CB_Hook, (int)mask, count);
	}
	//public Hook GetHook()
	//{
	//	return CB_Hook(LuaWrapper.lua_gethook(L));
	//}
	public HookMask GetHookMask()
	{
		return (HookMask)LuaWrapper.lua_gethookmask(L);
	}
	public int GetHookCount()
	{
		return LuaWrapper.lua_gethookcount(L);
	}

	/* 
	** ===============================================================
	** some useful macros
	** ===============================================================
	*/

	public void Pop(int n) => SetTop(-(n) - 1);
	public void Register(string n, CFunction f)
	{
		PushString(n);
		PushFunction(f);
		SetTable(LUA_GLOBALSINDEX);
	}
	public void PushFunction(CFunction f) => PushCClosure(f, 0);
	public bool IsFunction(int n) => Type(n) == ValueType.FUNCTION;
	public bool IsTable(int n) => Type(n) == ValueType.TABLE;
	public bool IsLightUserData(int n) => Type(n) == ValueType.LIGHTUSERDATA;
	public bool IsNil(int n) => Type(n) == ValueType.NIL;
	public bool IsBoolean(int n) => Type(n) == ValueType.BOOLEAN;
	public bool IsNone(int n) => Type(n) == ValueType.NONE;
	public bool IsNoneOrNil(int n) => Type(n) <= 0;

	/*
	** compatibility macros and functions
	*/

	public void GetRegistry() => PushValue(LUA_REGISTRYINDEX);
	public void SetGlobal(string s)
	{
		PushString(s);
		Insert(-2);
		SetTable(LUA_GLOBALSINDEX);
	}

	public void GetGlobal(string s)
	{
		PushString(s);
		GetTable(LUA_GLOBALSINDEX);
	}


	/* 
	** ===============================================================
	** lualib.h
	** ===============================================================
	*/

	public int OpenBase()
	{
		return LuaWrapper.luaopen_base(L);
	}
	public int OpenTable()
	{
		return LuaWrapper.luaopen_table(L);
	}
	public int OpenIO()
	{
		return LuaWrapper.luaopen_io(L);
	}
	public int OpenString()
	{
		return LuaWrapper.luaopen_string(L);
	}
	public int OpenMath()
	{
		return LuaWrapper.luaopen_math(L);
	}
	public int OpenDebug()
	{
		return LuaWrapper.luaopen_debug(L);
	}
	public int OpenLoadLib()
	{
		return LuaWrapper.luaopen_loadlib(L);
	}


	/* 
	** ===============================================================
	** lauxlib.h
	** ===============================================================
	*/

	public void OpenLib(string libname, luaL_reg_ptr l, int nup)
	{
		LuaWrapper.luaL_openlib(L, libname, l, nup);
	}
	public int GetMetaField(int obj, string e)
	{
		return LuaWrapper.luaL_getmetafield(L, obj, e);
	}
	public int CallMeta(int obj, string e)
	{
		return LuaWrapper.luaL_callmeta(L, obj, e);
	}
	public int TypError(int narg, string tname)
	{
		return LuaWrapper.luaL_typerror(L, narg, tname);
	}
	public int ArgError(int numarg, string extramsg)
	{
		return LuaWrapper.luaL_argerror(L, numarg, extramsg);
	}
	public string CheckLString(int numArg, out size_t l)
	{
		return LuaWrapper.luaL_checklstring(L, numArg, out l);
	}
	public string OptLString(int numArg, string def, out size_t l)
	{
		return LuaWrapper.luaL_optlstring(L, numArg, def, out l);
	}
	public float CheckNumber(int numArg)
	{
		return LuaWrapper.luaL_checknumber(L, numArg);
	}
	public float OptNumber(int nArg, float def)
	{
		return LuaWrapper.luaL_optnumber(L, nArg, def);
	}

	public void CheckStack(int sz, string msg)
	{
		LuaWrapper.luaL_checkstack(L, sz, msg);
	}
	public void CheckType(int narg, int t)
	{
		LuaWrapper.luaL_checktype(L, narg, t);
	}
	public void CheckAny(int narg)
	{
		LuaWrapper.luaL_checkany(L, narg);
	}

	public int NewMetaTable(string tname)
	{
		return LuaWrapper.luaL_newmetatable(L, tname);
	}
	public void GetMetaTable(string tname)
	{
		LuaWrapper.luaL_getmetatable(L, tname);
	}
	public void_ptr CheckUData(int ud, string tname)
	{
		return LuaWrapper.luaL_checkudata(L, ud, tname);
	}

	public void Where(int lvl)
	{
		LuaWrapper.luaL_where(L, lvl);
	}

	public int Ref(int t)
	{
		return LuaWrapper.luaL_ref(L, t);
	}
	public void Unref(int t, int reference)
	{
		LuaWrapper.luaL_unref(L, t, reference);
	}

	public int GetN(int t)
	{
		return LuaWrapper.luaL_getn(L, t);
	}
	public void SetN(int t, int n)
	{
		LuaWrapper.luaL_setn(L, t, n);
	}

	public int LoadFile(string filename)
	{
		return LuaWrapper.luaL_loadfile(L, filename);
	}
	public int LoadBuffer(void_ptr buff, size_t sz, string name)
	{
		return LuaWrapper.luaL_loadbuffer(L, buff, sz, name);
	}

	public void BuffInit(luaL_Buffer_ptr B)
	{
		LuaWrapper.luaL_buffinit(L, B);
	}
	public static string PrepBuffer(luaL_Buffer_ptr B)
	{
		return Marshal.PtrToStringAnsi(LuaWrapper.luaL_prepbuffer(B));
	}
	//public static void luaL_addlstring(luaL_Buffer_ptr B, char_ptr s, size_t l)
	//{
	//	LuaWrapper.luaL_addlstring(B, s, l);
	//}
	public static void AddString(luaL_Buffer_ptr B, string s)
	{
		LuaWrapper.luaL_addstring(B, s);
	}
	public static void AddValue(luaL_Buffer_ptr B)
	{
		LuaWrapper.luaL_addvalue(B);
	}
	public static void PushResult(luaL_Buffer_ptr B)
	{
		LuaWrapper.luaL_pushresult(B);
	}

	public ErrorCode DoFile(string filename)
	{
		return (ErrorCode)LuaWrapper.lua_dofile(L, filename);
	}
	public ErrorCode DoString(string str)
	{
		return (ErrorCode)LuaWrapper.lua_dostring(L, str);
	}
	public ErrorCode DoBuffer(void_ptr buff, ulong sz, string n)
	{
		return (ErrorCode)LuaWrapper.lua_dobuffer(L, buff, sz, n);
	}

	/*
	** ===============================================================
	** some useful macros
	** ===============================================================
	*/

	public void ArgCheck(bool cond, int numarg, string extramsg) 
	{
		if (!cond)
		{
			ArgError(numarg, extramsg);
		}
	}
	public string CheckString(int n) => CheckLString(n, out ulong _);
	public string OptString(int n, string d) => OptLString(n, d, out _);
	public int CheckInt(int n) => (int)CheckNumber(n);
	public long CheckLong(int n) => (long)CheckNumber(n);
	public int OptInt(int n, float d) => (int)OptNumber(n, d);
	public long OptLong(int n, float d) => (long)OptNumber(n, d);


    static readonly LuaWrapper.lua_CFunction[] ProvidedCallbacks = new LuaWrapper.lua_CFunction[]
    {
        CBFunc0,   CBFunc1,   CBFunc2,   CBFunc3,   CBFunc4,   CBFunc5,   CBFunc6,   CBFunc7,   CBFunc8,   CBFunc9,
        CBFunc10,  CBFunc11,  CBFunc12,  CBFunc13,  CBFunc14,  CBFunc15,  CBFunc16,  CBFunc17,  CBFunc18,  CBFunc19,
        CBFunc20,  CBFunc21,  CBFunc22,  CBFunc23,  CBFunc24,  CBFunc25,  CBFunc26,  CBFunc27,  CBFunc28,  CBFunc29,
		CBFunc30,  CBFunc31,  CBFunc32,  CBFunc33,  CBFunc34,  CBFunc35,  CBFunc36,  CBFunc37,  CBFunc38,  CBFunc39,
        CBFunc40,  CBFunc41,  CBFunc42,  CBFunc43,  CBFunc44,  CBFunc45,  CBFunc46,  CBFunc47,  CBFunc48,  CBFunc49,
        CBFunc50,  CBFunc51,  CBFunc52,  CBFunc53,  CBFunc54,  CBFunc55,  CBFunc56,  CBFunc57,  CBFunc58,  CBFunc59,
        CBFunc60,  CBFunc61,  CBFunc62,  CBFunc63,  CBFunc64,  CBFunc65,  CBFunc66,  CBFunc67,  CBFunc68,  CBFunc69,
        CBFunc70,  CBFunc71,  CBFunc72,  CBFunc73,  CBFunc74,  CBFunc75,  CBFunc76,  CBFunc77,  CBFunc78,  CBFunc79,
        CBFunc80,  CBFunc81,  CBFunc82,  CBFunc83,  CBFunc84,  CBFunc85,  CBFunc86,  CBFunc87,  CBFunc88,  CBFunc89,
        CBFunc90,  CBFunc91,  CBFunc92,  CBFunc93,  CBFunc94,  CBFunc95,  CBFunc96,  CBFunc97,  CBFunc98,  CBFunc99,
		CBFunc100, CBFunc101, CBFunc102, CBFunc103, CBFunc104, CBFunc105, CBFunc106, CBFunc107, CBFunc108, CBFunc109,
		CBFunc110, CBFunc111, CBFunc112, CBFunc113, CBFunc114, CBFunc115, CBFunc116, CBFunc117, CBFunc118, CBFunc119,
		CBFunc120, CBFunc121, CBFunc122, CBFunc123, CBFunc124, CBFunc125, CBFunc126, CBFunc127, CBFunc128, CBFunc129,
		CBFunc130, CBFunc131, CBFunc132, CBFunc133, CBFunc134, CBFunc135, CBFunc136, CBFunc137, CBFunc138, CBFunc139,
		CBFunc140, CBFunc141, CBFunc142, CBFunc143, CBFunc144, CBFunc145, CBFunc146, CBFunc147, CBFunc148, CBFunc149,
		CBFunc150, CBFunc151, CBFunc152, CBFunc153, CBFunc154, CBFunc155, CBFunc156, CBFunc157, CBFunc158, CBFunc159,
		CBFunc160, CBFunc161, CBFunc162, CBFunc163, CBFunc164, CBFunc165, CBFunc166, CBFunc167, CBFunc168, CBFunc169,
		CBFunc170, CBFunc171, CBFunc172, CBFunc173, CBFunc174, CBFunc175, CBFunc176, CBFunc177, CBFunc178, CBFunc179,
		CBFunc180, CBFunc181, CBFunc182, CBFunc183, CBFunc184, CBFunc185, CBFunc186, CBFunc187, CBFunc188, CBFunc189,
		CBFunc190, CBFunc191, CBFunc192, CBFunc193, CBFunc194, CBFunc195, CBFunc196, CBFunc197, CBFunc198, CBFunc199,
		CBFunc200, CBFunc201, CBFunc202, CBFunc203, CBFunc204, CBFunc205, CBFunc206, CBFunc207, CBFunc208, CBFunc209,
		CBFunc210, CBFunc211, CBFunc212, CBFunc213, CBFunc214, CBFunc215, CBFunc216, CBFunc217, CBFunc218, CBFunc219,
		CBFunc220, CBFunc221, CBFunc222, CBFunc223, CBFunc224, CBFunc225, CBFunc226, CBFunc227, CBFunc228, CBFunc229,
		CBFunc230, CBFunc231, CBFunc232, CBFunc233, CBFunc234, CBFunc235, CBFunc236, CBFunc237, CBFunc238, CBFunc239,
		CBFunc240, CBFunc241, CBFunc242, CBFunc243, CBFunc244, CBFunc245, CBFunc246, CBFunc247, CBFunc248, CBFunc249			
	};

	delegate int AOTCallbackDelegate(lua_State_ptr l);

	[MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc0(lua_State_ptr l) => CallbackMap[0].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc1(lua_State_ptr l) => CallbackMap[1].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc2(lua_State_ptr l) => CallbackMap[2].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc3(lua_State_ptr l) => CallbackMap[3].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc4(lua_State_ptr l) => CallbackMap[4].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc5(lua_State_ptr l) => CallbackMap[5].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc6(lua_State_ptr l) => CallbackMap[6].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc7(lua_State_ptr l) => CallbackMap[7].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc8(lua_State_ptr l) => CallbackMap[8].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc9(lua_State_ptr l) => CallbackMap[9].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc10(lua_State_ptr l) => CallbackMap[10].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc11(lua_State_ptr l) => CallbackMap[11].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc12(lua_State_ptr l) => CallbackMap[12].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc13(lua_State_ptr l) => CallbackMap[13].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc14(lua_State_ptr l) => CallbackMap[14].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc15(lua_State_ptr l) => CallbackMap[15].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc16(lua_State_ptr l) => CallbackMap[16].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc17(lua_State_ptr l) => CallbackMap[17].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc18(lua_State_ptr l) => CallbackMap[18].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc19(lua_State_ptr l) => CallbackMap[19].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc20(lua_State_ptr l) => CallbackMap[20].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc21(lua_State_ptr l) => CallbackMap[21].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc22(lua_State_ptr l) => CallbackMap[22].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc23(lua_State_ptr l) => CallbackMap[23].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc24(lua_State_ptr l) => CallbackMap[24].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc25(lua_State_ptr l) => CallbackMap[25].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc26(lua_State_ptr l) => CallbackMap[26].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc27(lua_State_ptr l) => CallbackMap[27].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc28(lua_State_ptr l) => CallbackMap[28].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc29(lua_State_ptr l) => CallbackMap[29].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc30(lua_State_ptr l) => CallbackMap[30].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc31(lua_State_ptr l) => CallbackMap[31].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc32(lua_State_ptr l) => CallbackMap[32].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc33(lua_State_ptr l) => CallbackMap[33].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc34(lua_State_ptr l) => CallbackMap[34].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc35(lua_State_ptr l) => CallbackMap[35].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc36(lua_State_ptr l) => CallbackMap[36].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc37(lua_State_ptr l) => CallbackMap[37].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc38(lua_State_ptr l) => CallbackMap[38].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc39(lua_State_ptr l) => CallbackMap[39].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc40(lua_State_ptr l) => CallbackMap[40].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc41(lua_State_ptr l) => CallbackMap[41].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc42(lua_State_ptr l) => CallbackMap[42].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc43(lua_State_ptr l) => CallbackMap[43].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc44(lua_State_ptr l) => CallbackMap[44].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc45(lua_State_ptr l) => CallbackMap[45].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc46(lua_State_ptr l) => CallbackMap[46].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc47(lua_State_ptr l) => CallbackMap[47].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc48(lua_State_ptr l) => CallbackMap[48].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc49(lua_State_ptr l) => CallbackMap[49].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc50(lua_State_ptr l) => CallbackMap[50].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc51(lua_State_ptr l) => CallbackMap[51].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc52(lua_State_ptr l) => CallbackMap[52].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc53(lua_State_ptr l) => CallbackMap[53].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc54(lua_State_ptr l) => CallbackMap[54].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc55(lua_State_ptr l) => CallbackMap[55].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc56(lua_State_ptr l) => CallbackMap[56].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc57(lua_State_ptr l) => CallbackMap[57].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc58(lua_State_ptr l) => CallbackMap[58].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc59(lua_State_ptr l) => CallbackMap[59].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc60(lua_State_ptr l) => CallbackMap[60].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc61(lua_State_ptr l) => CallbackMap[61].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc62(lua_State_ptr l) => CallbackMap[62].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc63(lua_State_ptr l) => CallbackMap[63].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc64(lua_State_ptr l) => CallbackMap[64].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc65(lua_State_ptr l) => CallbackMap[65].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc66(lua_State_ptr l) => CallbackMap[66].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc67(lua_State_ptr l) => CallbackMap[67].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc68(lua_State_ptr l) => CallbackMap[68].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc69(lua_State_ptr l) => CallbackMap[69].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc70(lua_State_ptr l) => CallbackMap[70].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc71(lua_State_ptr l) => CallbackMap[71].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc72(lua_State_ptr l) => CallbackMap[72].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc73(lua_State_ptr l) => CallbackMap[73].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc74(lua_State_ptr l) => CallbackMap[74].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc75(lua_State_ptr l) => CallbackMap[75].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc76(lua_State_ptr l) => CallbackMap[76].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc77(lua_State_ptr l) => CallbackMap[77].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc78(lua_State_ptr l) => CallbackMap[78].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc79(lua_State_ptr l) => CallbackMap[79].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc80(lua_State_ptr l) => CallbackMap[80].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc81(lua_State_ptr l) => CallbackMap[81].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc82(lua_State_ptr l) => CallbackMap[82].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc83(lua_State_ptr l) => CallbackMap[83].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc84(lua_State_ptr l) => CallbackMap[84].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc85(lua_State_ptr l) => CallbackMap[85].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc86(lua_State_ptr l) => CallbackMap[86].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc87(lua_State_ptr l) => CallbackMap[87].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc88(lua_State_ptr l) => CallbackMap[88].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc89(lua_State_ptr l) => CallbackMap[89].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc90(lua_State_ptr l) => CallbackMap[90].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc91(lua_State_ptr l) => CallbackMap[91].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc92(lua_State_ptr l) => CallbackMap[92].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc93(lua_State_ptr l) => CallbackMap[93].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc94(lua_State_ptr l) => CallbackMap[94].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc95(lua_State_ptr l) => CallbackMap[95].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc96(lua_State_ptr l) => CallbackMap[96].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc97(lua_State_ptr l) => CallbackMap[97].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc98(lua_State_ptr l) => CallbackMap[98].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc99(lua_State_ptr l) => CallbackMap[99].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc100(lua_State_ptr l) => CallbackMap[100].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc101(lua_State_ptr l) => CallbackMap[101].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc102(lua_State_ptr l) => CallbackMap[102].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc103(lua_State_ptr l) => CallbackMap[103].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc104(lua_State_ptr l) => CallbackMap[104].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc105(lua_State_ptr l) => CallbackMap[105].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc106(lua_State_ptr l) => CallbackMap[106].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc107(lua_State_ptr l) => CallbackMap[107].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc108(lua_State_ptr l) => CallbackMap[108].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc109(lua_State_ptr l) => CallbackMap[109].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc110(lua_State_ptr l) => CallbackMap[110].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc111(lua_State_ptr l) => CallbackMap[111].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc112(lua_State_ptr l) => CallbackMap[112].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc113(lua_State_ptr l) => CallbackMap[113].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc114(lua_State_ptr l) => CallbackMap[114].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc115(lua_State_ptr l) => CallbackMap[115].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc116(lua_State_ptr l) => CallbackMap[116].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc117(lua_State_ptr l) => CallbackMap[117].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc118(lua_State_ptr l) => CallbackMap[118].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc119(lua_State_ptr l) => CallbackMap[119].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc120(lua_State_ptr l) => CallbackMap[120].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc121(lua_State_ptr l) => CallbackMap[121].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc122(lua_State_ptr l) => CallbackMap[122].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc123(lua_State_ptr l) => CallbackMap[123].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc124(lua_State_ptr l) => CallbackMap[124].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc125(lua_State_ptr l) => CallbackMap[125].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc126(lua_State_ptr l) => CallbackMap[126].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc127(lua_State_ptr l) => CallbackMap[127].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc128(lua_State_ptr l) => CallbackMap[128].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc129(lua_State_ptr l) => CallbackMap[129].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc130(lua_State_ptr l) => CallbackMap[130].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc131(lua_State_ptr l) => CallbackMap[131].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc132(lua_State_ptr l) => CallbackMap[132].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc133(lua_State_ptr l) => CallbackMap[133].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc134(lua_State_ptr l) => CallbackMap[134].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc135(lua_State_ptr l) => CallbackMap[135].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc136(lua_State_ptr l) => CallbackMap[136].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc137(lua_State_ptr l) => CallbackMap[137].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc138(lua_State_ptr l) => CallbackMap[138].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc139(lua_State_ptr l) => CallbackMap[139].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc140(lua_State_ptr l) => CallbackMap[140].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc141(lua_State_ptr l) => CallbackMap[141].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc142(lua_State_ptr l) => CallbackMap[142].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc143(lua_State_ptr l) => CallbackMap[143].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc144(lua_State_ptr l) => CallbackMap[144].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc145(lua_State_ptr l) => CallbackMap[145].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc146(lua_State_ptr l) => CallbackMap[146].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc147(lua_State_ptr l) => CallbackMap[147].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc148(lua_State_ptr l) => CallbackMap[148].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc149(lua_State_ptr l) => CallbackMap[149].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc150(lua_State_ptr l) => CallbackMap[150].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc151(lua_State_ptr l) => CallbackMap[151].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc152(lua_State_ptr l) => CallbackMap[152].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc153(lua_State_ptr l) => CallbackMap[153].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc154(lua_State_ptr l) => CallbackMap[154].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc155(lua_State_ptr l) => CallbackMap[155].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc156(lua_State_ptr l) => CallbackMap[156].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc157(lua_State_ptr l) => CallbackMap[157].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc158(lua_State_ptr l) => CallbackMap[158].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc159(lua_State_ptr l) => CallbackMap[159].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc160(lua_State_ptr l) => CallbackMap[160].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc161(lua_State_ptr l) => CallbackMap[161].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc162(lua_State_ptr l) => CallbackMap[162].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc163(lua_State_ptr l) => CallbackMap[163].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc164(lua_State_ptr l) => CallbackMap[164].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc165(lua_State_ptr l) => CallbackMap[165].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc166(lua_State_ptr l) => CallbackMap[166].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc167(lua_State_ptr l) => CallbackMap[167].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc168(lua_State_ptr l) => CallbackMap[168].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc169(lua_State_ptr l) => CallbackMap[169].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc170(lua_State_ptr l) => CallbackMap[170].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc171(lua_State_ptr l) => CallbackMap[171].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc172(lua_State_ptr l) => CallbackMap[172].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc173(lua_State_ptr l) => CallbackMap[173].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc174(lua_State_ptr l) => CallbackMap[174].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc175(lua_State_ptr l) => CallbackMap[175].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc176(lua_State_ptr l) => CallbackMap[176].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc177(lua_State_ptr l) => CallbackMap[177].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc178(lua_State_ptr l) => CallbackMap[178].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc179(lua_State_ptr l) => CallbackMap[179].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc180(lua_State_ptr l) => CallbackMap[180].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc181(lua_State_ptr l) => CallbackMap[181].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc182(lua_State_ptr l) => CallbackMap[182].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc183(lua_State_ptr l) => CallbackMap[183].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc184(lua_State_ptr l) => CallbackMap[184].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc185(lua_State_ptr l) => CallbackMap[185].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc186(lua_State_ptr l) => CallbackMap[186].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc187(lua_State_ptr l) => CallbackMap[187].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc188(lua_State_ptr l) => CallbackMap[188].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc189(lua_State_ptr l) => CallbackMap[189].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc190(lua_State_ptr l) => CallbackMap[190].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc191(lua_State_ptr l) => CallbackMap[191].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc192(lua_State_ptr l) => CallbackMap[192].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc193(lua_State_ptr l) => CallbackMap[193].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc194(lua_State_ptr l) => CallbackMap[194].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc195(lua_State_ptr l) => CallbackMap[195].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc196(lua_State_ptr l) => CallbackMap[196].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc197(lua_State_ptr l) => CallbackMap[197].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc198(lua_State_ptr l) => CallbackMap[198].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc199(lua_State_ptr l) => CallbackMap[199].Invoke(GetLuaInstance(l));	
	[MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc200(lua_State_ptr l) => CallbackMap[200].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc201(lua_State_ptr l) => CallbackMap[201].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc202(lua_State_ptr l) => CallbackMap[202].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc203(lua_State_ptr l) => CallbackMap[203].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc204(lua_State_ptr l) => CallbackMap[204].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc205(lua_State_ptr l) => CallbackMap[205].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc206(lua_State_ptr l) => CallbackMap[206].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc207(lua_State_ptr l) => CallbackMap[207].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc208(lua_State_ptr l) => CallbackMap[208].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc209(lua_State_ptr l) => CallbackMap[209].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc210(lua_State_ptr l) => CallbackMap[210].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc211(lua_State_ptr l) => CallbackMap[211].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc212(lua_State_ptr l) => CallbackMap[212].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc213(lua_State_ptr l) => CallbackMap[213].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc214(lua_State_ptr l) => CallbackMap[214].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc215(lua_State_ptr l) => CallbackMap[215].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc216(lua_State_ptr l) => CallbackMap[216].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc217(lua_State_ptr l) => CallbackMap[217].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc218(lua_State_ptr l) => CallbackMap[218].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc219(lua_State_ptr l) => CallbackMap[219].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc220(lua_State_ptr l) => CallbackMap[220].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc221(lua_State_ptr l) => CallbackMap[221].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc222(lua_State_ptr l) => CallbackMap[222].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc223(lua_State_ptr l) => CallbackMap[223].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc224(lua_State_ptr l) => CallbackMap[224].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc225(lua_State_ptr l) => CallbackMap[225].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc226(lua_State_ptr l) => CallbackMap[226].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc227(lua_State_ptr l) => CallbackMap[227].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc228(lua_State_ptr l) => CallbackMap[228].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc229(lua_State_ptr l) => CallbackMap[229].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc230(lua_State_ptr l) => CallbackMap[230].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc231(lua_State_ptr l) => CallbackMap[231].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc232(lua_State_ptr l) => CallbackMap[232].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc233(lua_State_ptr l) => CallbackMap[233].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc234(lua_State_ptr l) => CallbackMap[234].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc235(lua_State_ptr l) => CallbackMap[235].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc236(lua_State_ptr l) => CallbackMap[236].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc237(lua_State_ptr l) => CallbackMap[237].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc238(lua_State_ptr l) => CallbackMap[238].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc239(lua_State_ptr l) => CallbackMap[239].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc240(lua_State_ptr l) => CallbackMap[240].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc241(lua_State_ptr l) => CallbackMap[241].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc242(lua_State_ptr l) => CallbackMap[242].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc243(lua_State_ptr l) => CallbackMap[243].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc244(lua_State_ptr l) => CallbackMap[244].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc245(lua_State_ptr l) => CallbackMap[245].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc246(lua_State_ptr l) => CallbackMap[246].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc247(lua_State_ptr l) => CallbackMap[247].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc248(lua_State_ptr l) => CallbackMap[248].Invoke(GetLuaInstance(l));
    [MonoPInvokeCallback(typeof(AOTCallbackDelegate))]
	static int CBFunc249(lua_State_ptr l) => CallbackMap[249].Invoke(GetLuaInstance(l));    
}