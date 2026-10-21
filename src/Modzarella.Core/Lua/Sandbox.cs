using System;
using System.Collections.Generic;
using MoonSharp.Interpreter.Interop.RegistrationPolicies;

namespace Modz
{
    // Lua only ever sees game and Unity objects: reflection, files, processes, networking and loaders stay out of reach
    public class Sandbox : AutomaticRegistrationPolicy
    {
        static readonly string[] blockedNamespaces =
        {
            "System", "Microsoft", "Mono", "MonoMod", "HarmonyLib", "BepInEx", "Steamworks", "MoonSharp",
            "UnityEngine.Networking", "UnityEngine.Windows", "UnityEngine.WSA", "UnityEngine.Diagnostics", "UnityEngineInternal", "UnityEditor",
        };
        static readonly HashSet<string> blockedTypes = new HashSet<string>
        {
            "UnityEngine.Application", "UnityEngine.ScreenCapture", "UnityEngine.PlayerPrefs", "UnityEngine.AssetBundle", "UnityEngine.WWW",
            "UnityEngine.WebCamTexture", "UnityEngine.Microphone", "UnityEngine.GUIUtility",
            "Modz.CorePlugin", "Modz.LuaEngine", "Modz.LuaMod", "Modz.LuaApi", "Modz.Net", "Modz.Sandbox",
        };
        static readonly Dictionary<Type, bool> cache = new Dictionary<Type, bool>();

        public override bool AllowTypeAutoRegistration(Type type) => Allowed(type);

        public static bool Allowed(Type t)
        {
            if (t == null) return false;
            lock (cache)
                if (cache.TryGetValue(t, out var known)) return known;
            bool ok = Check(t);
            lock (cache) cache[t] = ok;
            return ok;
        }

        static bool Check(Type t)
        {
            if (t.IsArray || t.IsPointer || t.IsByRef) return !t.IsPointer && Allowed(t.GetElementType());
            if (t.IsGenericType)
            {
                foreach (var a in t.GetGenericArguments())
                    if (!Allowed(a)) return false;
                if (t.Namespace == "System.Collections.Generic") return true;
            }
            if (typeof(Type).IsAssignableFrom(t) || typeof(Delegate).IsAssignableFrom(t)) return false;
            if (t.Namespace == "System" && (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(DateTime) || t == typeof(TimeSpan))) return true;
            var name = t.FullName ?? t.Name;
            if (blockedTypes.Contains(name)) return false;
            var ns = t.Namespace ?? "";
            foreach (var b in blockedNamespaces)
                if (ns == b || ns.StartsWith(b + ".", StringComparison.Ordinal)) return false;
            return t.DeclaringType == null || Allowed(t.DeclaringType);
        }
    }
}
