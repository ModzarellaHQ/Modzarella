using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Modz
{
    [BepInPlugin(GUID, "Modzarella", "1.1.3")]
    public class CorePlugin : BaseUnityPlugin
    {
        public const string GUID = "modz.core";
        public static CorePlugin Instance;
        internal static ManualLogSource Log;

        public ConfigEntry<KeyboardShortcut> MenuKey;

        bool menuOpen, advanced;
        Rect window;
        Vector2 scroll;
        string search = "";
        readonly HashSet<string> expanded = new HashSet<string> { "Modzarella" };
        ConfigEntryBase rebinding;
        readonly Dictionary<ConfigEntryBase, string> numText = new Dictionary<ConfigEntryBase, string>();
        CursorLockMode prevLock;
        bool prevVisible;
        string toast;
        float toastUntil;

        public static bool MenuOpen => Instance && Instance.menuOpen;

        class Page
        {
            public string Name, Version, Description, Error;
            public ConfigFile Config;
            public List<KeyValuePair<string, Action>> Buttons = new List<KeyValuePair<string, Action>>();
            public LuaMod Lua;
        }

        void Awake()
        {
            Instance = this;
            Log = Logger;
            MenuKey = Config.Bind("Keys", "Mod menu", new KeyboardShortcut(KeyCode.F1), "Open or close this menu.");
            new Harmony(GUID).PatchAll(typeof(CorePatches));
            new Harmony(GUID + ".body").PatchAll(typeof(BodyPatches));
            gameObject.AddComponent<CameraFeature>().Init(Config);
            gameObject.AddComponent<TweaksFeature>().Init(Config);
            gameObject.AddComponent<LuaEngine>().Init();
        }

        public static void Toast(string msg)
        {
            if (!Instance) { Debug.Log("[Modzarella] " + msg); return; }
            Instance.toast = msg;
            Instance.toastUntil = Time.unscaledTime + 3f;
            Log.LogInfo(msg);
        }

        void Update()
        {
            if (rebinding == null && ModCommon.KeyDown(MenuKey.Value)) SetMenu(!menuOpen);
            KeepCursor();
        }

        void LateUpdate() => KeepCursor();

        void KeepCursor()
        {
            if (!menuOpen) return;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        void SetMenu(bool open)
        {
            if (open == menuOpen) return;
            menuOpen = open;
            rebinding = null;
            if (open) { prevLock = Cursor.lockState; prevVisible = Cursor.visible; }
            else { Cursor.lockState = prevLock; Cursor.visible = prevVisible; }
        }

        List<Page> Pages()
        {
            var pages = new List<Page> { new Page { Name = "Modzarella", Version = Info.Metadata.Version.ToString(), Description = "Camera, game tweaks and performance. Built into Modzarella.", Config = Config } };
            foreach (var m in LuaEngine.Mods)
            {
                var p = new Page { Name = m.Name, Version = m.Version, Description = m.Description, Error = m.Error, Config = m.Config, Lua = m };
                foreach (var b in m.Buttons) { var fn = b.Value; p.Buttons.Add(new KeyValuePair<string, Action>(b.Key, () => LuaEngine.Invoke(m, fn))); }
                pages.Add(p);
            }
            return pages;
        }

        void OnGUI()
        {
            if (toast != null && Time.unscaledTime < toastUntil && Event.current.type == EventType.Repaint) Theme.Toast(toast);
            if (!menuOpen) return;
            KeepCursor();
            CaptureRebind();
            if (window.width < 10f) window = new Rect((Screen.width - 620) / 2f, Screen.height * 0.1f, 620, Mathf.Min(680, Screen.height * 0.8f));
            window = GUI.Window(0xC4EE5E, window, DrawWindow, GUIContent.none, GUIStyle.none);
            window.x = Mathf.Clamp(window.x, 0, Screen.width - 120);
            window.y = Mathf.Clamp(window.y, 0, Screen.height - 60);
        }

        void CaptureRebind()
        {
            var e = Event.current;
            if (rebinding == null) return;
            if (e.type == EventType.KeyDown && e.keyCode != KeyCode.None)
            {
                if (e.keyCode != KeyCode.Escape)
                {
                    var mods = new List<KeyCode>();
                    if (e.control && e.keyCode != KeyCode.LeftControl) mods.Add(KeyCode.LeftControl);
                    if (e.shift && e.keyCode != KeyCode.LeftShift) mods.Add(KeyCode.LeftShift);
                    if (e.alt && e.keyCode != KeyCode.LeftAlt) mods.Add(KeyCode.LeftAlt);
                    rebinding.BoxedValue = new KeyboardShortcut(e.keyCode, mods.ToArray());
                }
                rebinding = null;
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button > 0)
            {
                rebinding.BoxedValue = new KeyboardShortcut(KeyCode.Mouse0 + e.button);
                rebinding = null;
                e.Use();
            }
        }

        void DrawWindow(int id)
        {
            float w = window.width, h = window.height;
            Theme.Panel(new Rect(0, 0, w, h));

            GUILayout.BeginArea(new Rect(12, 8, w - 24, 28));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Modzarella", Theme.TextStyle(15, Theme.TextColor, "left", true), GUILayout.Height(26));
            GUILayout.Space(8);
            GUILayout.Label(ModCommon.Active ? "mods on" : "mods run in Play Offline", Theme.TextStyle(11, Theme.Dim), GUILayout.Height(26));
            GUILayout.FlexibleSpace();
            GUI.SetNextControlName("search");
            search = GUILayout.TextField(search, Theme.Field, GUILayout.Width(170));
            if (search.Length == 0 && GUI.GetNameOfFocusedControl() != "search")
                GUI.Label(GUILayoutUtility.GetLastRect(), "  Search", Theme.TextStyle(12, Theme.Dim));
            GUILayout.Space(6);
            advanced = GUILayout.Toggle(advanced, GUIContent.none, Theme.Toggle);
            GUILayout.Label("Advanced", Theme.TextStyle(12, Theme.Dim), GUILayout.Height(24));
            GUILayout.Space(6);
            if (GUILayout.Button("Close", Theme.Button)) SetMenu(false);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
            Theme.Rect(new Rect(1, 42, w - 2, 1), Theme.Line);

            GUILayout.BeginArea(new Rect(1, 43, w - 2, h - 70));
            scroll = Theme.Scroll(scroll, () => { foreach (var p in Pages()) DrawPage(p); });
            GUILayout.EndArea();

            Theme.Rect(new Rect(1, h - 27, w - 2, 1), Theme.Line);
            GUI.Label(new Rect(12, h - 26, w - 24, 24), string.IsNullOrEmpty(GUI.tooltip) ? "Changes save automatically. Hover a setting to see what it does." : GUI.tooltip, Theme.TextStyle(11, Theme.Dim));
            GUI.DragWindow(new Rect(0, 0, w, 42));
        }

        void DrawPage(Page p)
        {
            var entries = p.Config.Where(kv => kv.Key.Section != "General" && (advanced || !IsAdvanced(kv.Value) || search.Length > 0)).ToList();
            bool searching = search.Length > 0;
            if (searching)
            {
                bool nameHit = p.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!nameHit) entries = entries.Where(kv => kv.Key.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                if (!nameHit && entries.Count == 0) return;
            }
            bool open = searching || expanded.Contains(p.Name);

            var head = GUILayoutUtility.GetRect(1, 30, GUILayout.ExpandWidth(true));
            Theme.Rect(head, new Color(Theme.Surface.r, Theme.Surface.g, Theme.Surface.b, 0.9f));
            float x = head.x + 12;
            var nameStyle = Theme.TextStyle(13, p.Error != null ? Theme.Bad : Theme.TextColor, "left", true);
            float nw = nameStyle.CalcSize(new GUIContent(p.Name)).x;
            GUI.Label(new Rect(x, head.y, nw, 30), p.Name, nameStyle);
            GUI.Label(new Rect(x + nw + 8, head.y, 200, 30), (open ? "▾ " : "▸ ") + p.Version, Theme.TextStyle(11, Theme.Dim));
            if (p.Lua != null && GUI.Button(new Rect(head.xMax - 76, head.y + 4, 64, 22), "Reload", Theme.Button)) LuaEngine.Reload(p.Lua);
            var click = new Rect(x, head.y, head.width - x - 90, 30);
            if (!searching && Event.current.type == EventType.MouseDown && click.Contains(Event.current.mousePosition))
            {
                if (!expanded.Remove(p.Name)) expanded.Add(p.Name);
                Event.current.Use();
            }
            if (!open) { GUILayout.Space(1); return; }

            GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(14, 14, 6, 10) });
            if (p.Error != null) GUILayout.Label("Stopped: " + p.Error, Theme.TextStyle(11, Theme.Bad, "left", false, true, true));
            if (!string.IsNullOrEmpty(p.Description)) GUILayout.Label(p.Description, Theme.TextStyle(12, Theme.Dim, "left", false, true));

            if (p.Buttons.Count > 0)
            {
                GUILayout.Space(4);
                GUI.enabled = ModCommon.InRound && p.Error == null;
                GUILayout.BeginHorizontal();
                foreach (var b in p.Buttons)
                    if (GUILayout.Button(b.Key, Theme.Button)) { try { b.Value(); } catch (Exception e) { Log.LogError(e); } }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUI.enabled = true;
            }

            string section = null;
            bool sections = entries.Select(kv => kv.Key.Section).Distinct().Count() > 1;
            foreach (var kv in entries.OrderBy(kv => kv.Value is ConfigEntry<KeyboardShortcut> ? 1 : 0))
            {
                string sec = kv.Value is ConfigEntry<KeyboardShortcut> ? "Keys" : kv.Key.Section;
                if (sections && sec != section)
                {
                    section = sec;
                    GUILayout.Label(sec.ToUpperInvariant(), Theme.TextStyle(10, Theme.Dim, "left", true), GUILayout.Height(22));
                }
                Row(kv.Key, kv.Value);
            }
            GUILayout.EndVertical();
            GUILayout.Space(1);
        }

        static bool IsAdvanced(ConfigEntryBase e) => e.Description.Tags != null && e.Description.Tags.Contains("Advanced");

        void Row(ConfigDefinition def, ConfigEntryBase e)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(26), GUILayout.Width(window.width - 50));
            GUILayout.Label(new GUIContent(def.Key, e.Description.Description), Theme.TextStyle(12, Theme.TextColor), GUILayout.Width(200), GUILayout.Height(24));
            switch (e)
            {
                case ConfigEntry<bool> b:
                    b.Value = GUILayout.Toggle(b.Value, GUIContent.none, Theme.Toggle);
                    GUILayout.FlexibleSpace();
                    break;
                case ConfigEntry<float> f:
                {
                    var range = e.Description.AcceptableValues as AcceptableValueRange<float>;
                    float min = range?.MinValue ?? 0f, max = range?.MaxValue ?? Mathf.Max(1f, f.Value * 2f);
                    float v = GUILayout.HorizontalSlider(f.Value, min, max, Theme.Slider, Theme.Thumb, GUILayout.ExpandWidth(true));
                    if (!Mathf.Approximately(v, f.Value)) { f.Value = Mathf.Round(v * 100f) / 100f; numText.Remove(e); }
                    NumberField(e, f.Value.ToString("0.##", CultureInfo.InvariantCulture), s =>
                    {
                        if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var nv)) f.Value = Mathf.Clamp(nv, min, max);
                    });
                    break;
                }
                case ConfigEntry<int> i:
                {
                    var range = e.Description.AcceptableValues as AcceptableValueRange<int>;
                    int min = range?.MinValue ?? 0, max = range?.MaxValue ?? Mathf.Max(10, i.Value * 2);
                    int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(i.Value, min, max, Theme.Slider, Theme.Thumb, GUILayout.ExpandWidth(true)));
                    if (v != i.Value) { i.Value = v; numText.Remove(e); }
                    NumberField(e, i.Value.ToString(CultureInfo.InvariantCulture), s => { if (int.TryParse(s, out var nv)) i.Value = Mathf.Clamp(nv, min, max); });
                    break;
                }
                case ConfigEntry<string> str when e.Description.AcceptableValues is AcceptableValueList<string> list:
                {
                    var opts = list.AcceptableValues;
                    int idx = Math.Max(0, Array.IndexOf(opts, str.Value));
                    if (GUILayout.Button("‹", Theme.Button, GUILayout.Width(26))) str.Value = opts[(idx + opts.Length - 1) % opts.Length];
                    GUILayout.Label(str.Value, Theme.TextStyle(12, Theme.TextColor, "center"), GUILayout.Width(150), GUILayout.Height(24));
                    if (GUILayout.Button("›", Theme.Button, GUILayout.Width(26))) str.Value = opts[(idx + 1) % opts.Length];
                    GUILayout.FlexibleSpace();
                    break;
                }
                case ConfigEntry<KeyboardShortcut> key:
                    if (GUILayout.Button(rebinding == e ? "Press a key…" : ModCommon.Key(key.Value), rebinding == e ? Theme.AccentButton : Theme.Button, GUILayout.MinWidth(90)))
                        rebinding = rebinding == e ? null : e;
                    GUILayout.FlexibleSpace();
                    break;
                case ConfigEntry<string> str:
                    str.Value = GUILayout.TextField(str.Value, Theme.Field, GUILayout.ExpandWidth(true));
                    break;
            }
            if (Equals(e.BoxedValue, e.DefaultValue)) GUILayout.Space(58);
            else if (GUILayout.Button(new GUIContent("Reset", "Back to " + e.DefaultValue), Theme.Button, GUILayout.Width(52))) { e.BoxedValue = e.DefaultValue; numText.Remove(e); }
            GUILayout.EndHorizontal();
        }

        void NumberField(ConfigEntryBase e, string current, Action<string> commit)
        {
            if (!numText.TryGetValue(e, out var t)) t = current;
            string nt = GUILayout.TextField(t, Theme.Field, GUILayout.Width(52));
            if (nt != t) numText[e] = nt;
            if (numText.ContainsKey(e) && (Event.current.isKey && Event.current.keyCode == KeyCode.Return || nt != t && nt.Length > 0 && !nt.EndsWith(".")))
                commit(nt);
        }
    }

    internal static class CorePatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(InputManager), nameof(InputManager.ShowCursor))]
        static bool KeepCursorForMenu() => !CorePlugin.MenuOpen;

        [HarmonyPrefix, HarmonyPatch(typeof(InputManager), "Look")]
        static bool NoLookInMenu() => !CorePlugin.MenuOpen && !CameraFeature.Flying;
    }
}
