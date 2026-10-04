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
    [BepInPlugin(GUID, "Modzarella", "1.0.0")]
    public class CorePlugin : BaseUnityPlugin
    {
        public const string GUID = "modz.core";
        public static CorePlugin Instance;
        internal static ManualLogSource Log;

        public ConfigEntry<KeyboardShortcut> MenuKey;

        bool menuOpen, showMore;
        Rect window;
        Vector2 scroll, sideScroll;
        int page;
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
            public ConfigEntry<bool> Enabled;
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
                var p = new Page { Name = m.Name, Version = m.Version, Description = m.Description, Error = m.Error, Config = m.Config, Enabled = m.Enabled, Lua = m };
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
            if (window.width < 10f) window = new Rect((Screen.width - 860) / 2f, (Screen.height - 600) / 2f, 860, 600);
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

        const float Header = 56f, Side = 210f;

        void DrawWindow(int id)
        {
            float w = window.width, h = window.height;
            var frame = new Rect(0, 0, w, h);
            Theme.PanelRect(frame);
            GUI.Box(new Rect(1, 1, w - 2, Header), GUIContent.none, Theme.Style(Theme.Shape((int)Header, 9, new Color(0.18f, 0.153f, 0.125f), new Color(0.141f, 0.118f, 0.094f), new Color(0, 0, 0, 0), new Color(1, 1, 1, 0.07f)), 9));
            Theme.Rect(new Rect(1, Header, w - 2, 1), Theme.Edge);
            Theme.Rect(new Rect(1, Header + 1, w - 2, 1), new Color(1, 1, 1, 0.04f));
            Theme.Text("Modz", new Rect(22, 13, 80, 30), 21, Theme.TextColor, "left", true);
            Theme.Text("arella", new Rect(22 + Theme.TextStyle(21, Theme.TextColor, "left", true).CalcSize(new GUIContent("Modz")).x, 13, 120, 30), 21, Theme.Wax, "left", true);
            Theme.Text(ModCommon.Active ? "play offline · mods on" : "mods run in play offline", new Rect(150, 14, 300, 30), 11, Theme.Dim, "left", false, true);
            if (GUI.Button(new Rect(w - 96, 12, 76, 32), "Close", Theme.Button(false, 30, 12))) SetMenu(false);

            var pages = Pages();
            page = Mathf.Clamp(page, 0, pages.Count - 1);
            DrawSidebar(pages, new Rect(10, Header + 12, Side - 16, h - Header - 22));
            Theme.Rect(new Rect(Side, Header + 2, 1, h - Header - 3), Theme.Edge);
            Theme.Rect(new Rect(Side + 1, Header + 2, 1, h - Header - 3), new Color(1, 1, 1, 0.03f));
            DrawPage(pages[page], new Rect(Side + 22, Header + 14, w - Side - 42, h - Header - 26));
            GUI.DragWindow(new Rect(0, 0, w, Header));
        }

        void DrawSidebar(List<Page> pages, Rect area)
        {
            GUILayout.BeginArea(area);
            sideScroll = GUILayout.BeginScrollView(sideScroll, false, false, GUIStyle.none, GUIStyle.none);
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                var r = GUILayoutUtility.GetRect(area.width, 36, GUILayout.ExpandWidth(true));
                bool on = i == page;
                if (on)
                {
                    GUI.Box(r, GUIContent.none, Theme.Style(Theme.Shape(36, 6, new Color(0.2f, 0.169f, 0.137f), new Color(0.169f, 0.141f, 0.114f), Theme.Edge, new Color(1, 1, 1, 0.07f)), 6));
                    Theme.Rect(new Rect(r.x + 1, r.y + 8, 3, 20), Theme.Wax);
                }
                else if (r.Contains(Event.current.mousePosition)) GUI.Box(r, GUIContent.none, Theme.Box(new Color(1, 1, 1, 0.03f), 6));
                Theme.Text(p.Name, new Rect(r.x + 16, r.y, r.width - 40, r.height), 14, on ? Theme.TextColor : Theme.Dim, "left", on);
                Color dot = p.Error != null ? Theme.Bad : p.Enabled == null || p.Enabled.Value ? Theme.Brass : new Color(0.3f, 0.26f, 0.21f);
                GUI.Box(new Rect(r.xMax - 18, r.center.y - 3, 6, 6), GUIContent.none, Theme.Box(dot, 3));
                if (Event.current.type == EventType.MouseDown && r.Contains(Event.current.mousePosition)) { page = i; scroll = Vector2.zero; showMore = false; Event.current.Use(); }
                GUILayout.Space(2);
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPage(Page p, Rect area)
        {
            GUILayout.BeginArea(area);
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUIStyle.none);

            GUILayout.BeginHorizontal(GUILayout.Height(34));
            GUILayout.Label(p.Name, Theme.TextStyle(22, Theme.TextColor, "left", true), GUILayout.Height(34));
            GUILayout.Space(8);
            GUILayout.Label("v" + p.Version, Theme.TextStyle(11, Theme.Dim, "left", false, false, true), GUILayout.Height(36));
            GUILayout.FlexibleSpace();
            if (p.Lua != null && GUILayout.Button("Reload", Theme.Button(false, 26, 11))) LuaEngine.Reload(p.Lua);
            if (p.Enabled != null) { GUILayout.Space(4); p.Enabled.Value = Switch(p.Enabled.Value); }
            GUILayout.EndHorizontal();
            GUILayout.Label(p.Description, Theme.TextStyle(13, Theme.Dim, "left", false, true));
            GUILayout.Space(12);

            if (p.Error != null)
            {
                GUILayout.BeginVertical(Theme.Box(new Color(0.24f, 0.07f, 0.06f), 8, 12, Theme.Edge));
                GUILayout.Label("Stopped by an error", Theme.TextStyle(13, Theme.Bad, "left", true));
                GUILayout.Label(p.Error, Theme.TextStyle(11, Theme.TextColor, "left", false, true, true));
                GUILayout.EndVertical();
                GUILayout.Space(12);
            }

            if (p.Buttons.Count > 0)
            {
                GUI.enabled = ModCommon.InRound && (p.Enabled == null || p.Enabled.Value) && p.Error == null;
                GUILayout.BeginHorizontal();
                for (int i = 0; i < p.Buttons.Count; i++)
                    if (GUILayout.Button(p.Buttons[i].Key, Theme.Button(i == 0))) { try { p.Buttons[i].Value(); } catch (Exception e) { Log.LogError(e); } }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUI.enabled = true;
                if (!ModCommon.InRound) GUILayout.Label("buttons work during a round", Theme.TextStyle(11, Theme.Dim, "left", false, false, true));
            }

            var cfg = p.Config;
            var entries = cfg.Where(kv => kv.Key.Section != "General" && kv.Key.Section != "Keys").ToList();
            var basic = entries.Where(kv => !IsAdvanced(kv.Value)).ToList();
            var more = entries.Where(kv => IsAdvanced(kv.Value)).ToList();
            var keys = cfg.Where(kv => kv.Value is ConfigEntry<KeyboardShortcut>).ToList();

            if (basic.Count > 0) { Heading("settings"); foreach (var kv in basic) Row(kv.Key, kv.Value); }
            if (keys.Count > 0) { Heading("keys"); foreach (var kv in keys) Row(kv.Key, kv.Value); }
            if (more.Count > 0)
            {
                Heading("more");
                if (!showMore)
                {
                    if (GUILayout.Button($"Show {more.Count} more settings", Theme.Button(false, 28, 12))) showMore = true;
                }
                else foreach (var kv in more) Row(kv.Key, kv.Value);
            }

            GUILayout.Space(16);
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(GUI.tooltip) ? "changes save automatically" : GUI.tooltip, Theme.TextStyle(11, Theme.Dim, "left", false, true, true));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset to defaults", Theme.Button(false, 26, 11)))
                foreach (var kv in cfg) if (kv.Key.Section != "General") kv.Value.BoxedValue = kv.Value.DefaultValue;
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static bool IsAdvanced(ConfigEntryBase e) => e.Description.Tags != null && e.Description.Tags.Contains("Advanced");

        static void Heading(string text)
        {
            GUILayout.Space(18);
            GUILayout.Label(text.ToUpperInvariant(), Theme.TextStyle(11, Theme.Brass, "left", true, false, true), GUILayout.Height(18));
            var line = GUILayoutUtility.GetRect(1, 2, GUILayout.ExpandWidth(true));
            Theme.Rect(new Rect(line.x, line.y, line.width, 1), Theme.Edge);
            Theme.Rect(new Rect(line.x, line.y + 1, line.width, 1), new Color(1, 1, 1, 0.04f));
            GUILayout.Space(4);
        }

        static bool Switch(bool on)
        {
            var r = GUILayoutUtility.GetRect(44, 24, GUILayout.Width(44), GUILayout.Height(30));
            r = new Rect(r.x, r.y + 4, 44, 22);
            var e = Event.current;
            if (e.type == EventType.Repaint) Theme.Switch(r, on);
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { e.Use(); return !on; }
            return on;
        }

        static int sliderId;

        static float Slider(float value, float min, float max)
        {
            var r = GUILayoutUtility.GetRect(140, 30, GUILayout.ExpandWidth(true), GUILayout.Height(30));
            var track = new Rect(r.x + 8, r.center.y - 3, r.width - 24, 6);
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when r.Contains(e.mousePosition) && e.button == 0:
                    GUIUtility.hotControl = id; sliderId = id;
                    value = Mathf.Lerp(min, max, Mathf.InverseLerp(track.x, track.xMax, e.mousePosition.x));
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    value = Mathf.Lerp(min, max, Mathf.InverseLerp(track.x, track.xMax, e.mousePosition.x));
                    e.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.Repaint:
                    Theme.Slider(track, Mathf.InverseLerp(min, max, value));
                    break;
            }
            return value;
        }

        void Row(ConfigDefinition def, ConfigEntryBase e)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(36));
            GUILayout.Label(new GUIContent(def.Key, e.Description.Description), Theme.TextStyle(14, Theme.TextColor), GUILayout.Width(220), GUILayout.Height(30));
            switch (e)
            {
                case ConfigEntry<bool> b:
                    GUILayout.FlexibleSpace();
                    b.Value = Switch(b.Value);
                    break;
                case ConfigEntry<float> f:
                {
                    var range = e.Description.AcceptableValues as AcceptableValueRange<float>;
                    float min = range?.MinValue ?? 0f, max = range?.MaxValue ?? Mathf.Max(1f, f.Value * 2f);
                    float v = Slider(f.Value, min, max);
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
                    int v = Mathf.RoundToInt(Slider(i.Value, min, max));
                    if (v != i.Value) { i.Value = v; numText.Remove(e); }
                    NumberField(e, i.Value.ToString(CultureInfo.InvariantCulture), s => { if (int.TryParse(s, out var nv)) i.Value = Mathf.Clamp(nv, min, max); });
                    break;
                }
                case ConfigEntry<string> str when e.Description.AcceptableValues is AcceptableValueList<string> list:
                {
                    var opts = list.AcceptableValues;
                    int idx = Math.Max(0, Array.IndexOf(opts, str.Value));
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("‹", Theme.Button(false, 28), GUILayout.Width(32))) str.Value = opts[(idx + opts.Length - 1) % opts.Length];
                    GUILayout.Label(str.Value, Theme.TextStyle(13, Theme.TextColor, "center", true), GUILayout.Width(140), GUILayout.Height(30));
                    if (GUILayout.Button("›", Theme.Button(false, 28), GUILayout.Width(32))) str.Value = opts[(idx + 1) % opts.Length];
                    break;
                }
                case ConfigEntry<KeyboardShortcut> key:
                {
                    GUILayout.FlexibleSpace();
                    string label = rebinding == e ? "press a key" : ModCommon.Key(key.Value);
                    var r = GUILayoutUtility.GetRect(Theme.KeycapWidth(label), 30, GUILayout.Width(Theme.KeycapWidth(label)), GUILayout.Height(30));
                    r = new Rect(r.x, r.y + 2, r.width, 27);
                    if (Event.current.type == EventType.Repaint) Theme.Keycap(r, label, rebinding == e);
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(Event.current.mousePosition))
                    {
                        rebinding = rebinding == e ? null : e;
                        Event.current.Use();
                    }
                    break;
                }
                case ConfigEntry<string> str:
                    str.Value = GUILayout.TextField(str.Value, Field(), GUILayout.ExpandWidth(true));
                    break;
            }
            GUILayout.EndHorizontal();
        }

        static GUIStyle Field()
        {
            var st = Theme.Style(Theme.Shape(26, 6, new Color(0.043f, 0.035f, 0.027f), Theme.Inset, Theme.Edge, Color.clear), 6);
            st.font = Theme.Mono; st.fontSize = 12;
            st.normal.textColor = Theme.C(Theme.TextColor);
            st.alignment = TextAnchor.MiddleCenter;
            st.fixedHeight = 26;
            st.margin = new RectOffset(0, 0, 2, 0);
            return st;
        }

        void NumberField(ConfigEntryBase e, string current, Action<string> commit)
        {
            if (!numText.TryGetValue(e, out var t)) t = current;
            string nt = GUILayout.TextField(t, Field(), GUILayout.Width(58));
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
        static bool NoLookInMenu() => !CorePlugin.MenuOpen;
    }
}
