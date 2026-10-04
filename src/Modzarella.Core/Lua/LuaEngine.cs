using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;
using UnityEngine;

namespace Modz
{
    public class LuaMod
    {
        public string Id, Name, Version, Description, Dir, Error;
        internal float errorWindow;
        internal int errorCount;
        public ConfigFile Config;
        public ConfigEntry<bool> Enabled;
        public Script Script;
        public readonly List<KeyValuePair<string, DynValue>> Buttons = new List<KeyValuePair<string, DynValue>>();
        internal readonly List<(float at, DynValue fn)> Timers = new List<(float, DynValue)>();

        public DynValue Fn(string name) => Script?.Globals.Get(name) ?? DynValue.Nil;
        public bool Running => Script != null && Error == null && Enabled.Value;
    }

    public class LuaEngine : MonoBehaviour
    {
        public static LuaEngine Instance;
        public static readonly List<LuaMod> Mods = new List<LuaMod>();
        static readonly List<(LuaMod mod, string name, DynValue fn)> listeners = new List<(LuaMod, string, DynValue)>();
        bool wasInRound;

        public static string ModsDir => Path.Combine(Paths.PluginPath, "Modz");

        public void Init()
        {
            Instance = this;
            UserData.RegistrationPolicy = MoonSharp.Interpreter.Interop.InteropRegistrationPolicy.Automatic;
            UserData.RegisterType(typeof(Vector3), new FastVector3());
            UserData.RegisterType(typeof(Quaternion), new FastQuaternion());
            Script.DefaultOptions.DebugPrint = s => CorePlugin.Log.LogInfo(s);
            if (!Directory.Exists(ModsDir)) return;
            foreach (var dir in Directory.GetDirectories(ModsDir).OrderBy(d => d))
                if (File.Exists(Path.Combine(dir, "main.lua"))) Load(dir);
        }

        void Load(string dir)
        {
            var mod = new LuaMod { Dir = dir, Id = Path.GetFileName(dir) };
            mod.Name = mod.Id;
            var manifest = Path.Combine(dir, "mod.json");
            if (File.Exists(manifest) && MiniJson.Parse(File.ReadAllText(manifest)) is Dictionary<string, object> m)
            {
                mod.Name = m.TryGetValue("name", out var n) ? n.ToString() : mod.Id;
                mod.Version = m.TryGetValue("version", out var v) ? v.ToString() : "";
                mod.Description = m.TryGetValue("description", out var d) ? d.ToString() : "";
            }
            mod.Config = new ConfigFile(Path.Combine(Paths.ConfigPath, $"modz.{mod.Id}.cfg"), true);
            mod.Enabled = mod.Config.Bind("General", "Enabled", true, mod.Description);
            mod.Enabled.SettingChanged += (_, __) => { if (!mod.Enabled.Value) Call(mod, "on_disable"); };
            Mods.Add(mod);
            Run(mod);
        }

        static void Run(LuaMod mod)
        {
            mod.Error = null;
            mod.Buttons.Clear();
            mod.Timers.Clear();
            listeners.RemoveAll(l => l.mod == mod);
            var script = new Script(CoreModules.Preset_SoftSandbox | CoreModules.LoadMethods);
            script.Options.ScriptLoader = new FileSystemScriptLoader { ModulePaths = new[] { Path.Combine(mod.Dir, "?.lua") } };
            mod.Script = script;
            try
            {
                LuaApi.Install(script, mod);
                script.DoFile(Path.Combine(mod.Dir, "main.lua"));
                CorePlugin.Log.LogInfo($"Lua mod {mod.Name} {mod.Version} loaded");
            }
            catch (Exception e) { Fail(mod, e, true); }
        }

        public static void Reload(LuaMod mod)
        {
            Call(mod, "on_unload");
            mod.Config.Reload();
            Run(mod);
            if (mod.Error == null) ModCommon.Toast($"Reloaded {mod.Name}");
        }

        static string Describe(Exception e)
        {
            string msg = e is InterpreterException ie ? ie.DecoratedMessage ?? ie.Message : e.Message;
            if (string.IsNullOrEmpty(msg) && e.InnerException != null) msg = e.InnerException.Message;
            return string.IsNullOrEmpty(msg) ? e.GetType().Name : msg;
        }

        // one-off errors are logged; a mod that keeps failing is stopped
        static void Fail(LuaMod mod, Exception e, bool fatal = false)
        {
            string msg = Describe(e);
            if (Time.unscaledTime > mod.errorWindow) { mod.errorWindow = Time.unscaledTime + 10f; mod.errorCount = 0; }
            if (++mod.errorCount <= 3) CorePlugin.Log.LogError($"[{mod.Name}] {msg}\n{e}");
            if (!fatal && mod.errorCount < 30) return;
            mod.Error = msg;
            ModCommon.Toast($"{mod.Name} stopped: {msg}");
        }

        public static DynValue Call(LuaMod mod, string fn, params object[] args)
        {
            if (mod.Script == null || mod.Error != null) return DynValue.Nil;
            var f = mod.Fn(fn);
            return f.Type == DataType.Function ? Invoke(mod, f, args) : DynValue.Nil;
        }

        public static DynValue Invoke(LuaMod mod, DynValue fn, params object[] args)
        {
            if (fn.Type == DataType.Function && fn.Function.OwnerScript != mod.Script) return DynValue.Nil;
            try { return mod.Script.Call(fn, args); }
            catch (Exception e) { Fail(mod, e); return DynValue.Nil; }
        }

        public static void On(LuaMod mod, string name, DynValue fn) => listeners.Add((mod, name, fn));

        public static DynValue Emit(string name, params object[] args)
        {
            DynValue result = DynValue.Nil;
            foreach (var (mod, n, fn) in listeners.ToArray())
                if (n == name && mod.Running)
                {
                    var r = Invoke(mod, fn, args);
                    if (result.IsNil()) result = r;
                }
            return result;
        }

        static void Each(string fn, params object[] args)
        {
            foreach (var mod in Mods)
                if (mod.Running) Call(mod, fn, args);
        }

        internal static void PartHit(RagdollPart part, Collision c)
        {
            if (Instance && ModCommon.InRound) Each("on_part_hit", part, c);
        }

        void Update()
        {
            bool inRound = ModCommon.InRound;
            if (inRound && !wasInRound) { Body.Prune(); Each("on_round_start"); }
            wasInRound = inRound;
            if (!inRound) return;
            foreach (var mod in Mods)
            {
                if (!mod.Running) continue;
                for (int i = mod.Timers.Count - 1; i >= 0; i--)
                    if (Time.time >= mod.Timers[i].at) { var fn = mod.Timers[i].fn; mod.Timers.RemoveAt(i); Invoke(mod, fn); }
            }
            Each("update", Time.deltaTime);
        }

        void FixedUpdate()
        {
            if (!ModCommon.InRound) return;
            Body.Sense();
            Each("fixed_update", Time.fixedDeltaTime);
        }
        void LateUpdate() { if (ModCommon.InRound) Each("late_update", Time.deltaTime); }

        void OnGUI()
        {
            if (Event.current.type == EventType.Repaint && ModCommon.InRound) Each("draw");
        }
    }
}

namespace Modz
{
    public class CollisionRelay : MonoBehaviour
    {
        internal LuaMod Mod;
        internal DynValue OnHit, OnStay;

        bool Current(DynValue fn) => fn != null && Mod.Running && fn.Function.OwnerScript == Mod.Script;
        void OnCollisionEnter(Collision c) { if (Current(OnHit)) LuaEngine.Invoke(Mod, OnHit, c); }
        void OnCollisionStay(Collision c) { if (Current(OnStay)) LuaEngine.Invoke(Mod, OnStay, c); }
    }
}
