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
            if (window.width < 10f) window = new Rect((Screen.width - 820) / 2f, (Screen.height - 580) / 2f, 820, 580);
            window = GUI.Window(0xC4EE5E, window, DrawWindow, GUIContent.none, Theme.Box(Theme.Bg, 14));
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

        static GUIStyle Btn(Color bg, Color hover, int size = 13, Color? text = null)
        {
            var st = Theme.Box(bg, 8, 0);
            st.hover.background = Theme.Round(hover, 8);
            st.active.background = Theme.Round(Theme.Line, 8);
            st.font = Theme.Font;
            st.fontSize = size;
            st.alignment = TextAnchor.MiddleCenter;
            st.normal.textColor = st.hover.textColor = st.active.textColor = Theme.C(text ?? Theme.TextColor);
            st.padding = new RectOffset(14, 14, 8, 8);
            st.margin = new RectOffset(0, 8, 0, 8);
            return st;
        }

        void DrawWindow(int id)
        {
            float w = window.width, h = window.height;
            Theme.Text("Modzarella", new Rect(22, 14, 300, 30), 20, Theme.Accent, "left", true);
            Theme.Text(ModCommon.Active ? "Play Offline · mods are on" : "Mods run in Play Offline", new Rect(160, 14, 300, 30), 12, Theme.Dim);
            if (GUI.Button(new Rect(w - 92, 14, 72, 30), "Close", Btn(Theme.Card, Theme.Raised, 12))) SetMenu(false);
            Theme.Rect(new Rect(0, 58, w, 1), Theme.Line);

            var pages = Pages();
            page = Mathf.Clamp(page, 0, pages.Count - 1);
            DrawSidebar(pages, new Rect(12, 70, 200, h - 82));
            Theme.Rect(new Rect(224, 59, 1, h - 59), Theme.Line);
            DrawPage(pages[page], new Rect(244, 70, w - 264, h - 82));
            GUI.DragWindow(new Rect(0, 0, w, 58));
        }

        void DrawSidebar(List<Page> pages, Rect area)
        {
            GUILayout.BeginArea(area);
            sideScroll = GUILayout.BeginScrollView(sideScroll, false, false, GUIStyle.none, GUIStyle.none);
            for (int i = 0; i < pages.Count; i++)
            {
                var p = pages[i];
                var st = Btn(i == page ? Theme.Raised : new Color(0, 0, 0, 0), i == page ? Theme.Raised : Theme.Card, 14);
                st.alignment = TextAnchor.MiddleLeft;
                st.padding = new RectOffset(32, 10, 9, 9);
                st.margin = new RectOffset(0, 0, 0, 4);
                var r = GUILayoutUtility.GetRect(new GUIContent(p.Name), st, GUILayout.ExpandWidth(true));
                if (GUI.Button(r, p.Name, st)) { page = i; scroll = Vector2.zero; showMore = false; }
                Color dot = p.Error != null ? Theme.Bad : p.Enabled == null || p.Enabled.Value ? Theme.Ok : Theme.Dim;
                GUI.Box(new Rect(r.x + 13, r.center.y - 4, 8, 8), GUIContent.none, Theme.Box(dot, 4));
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawPage(Page p, Rect area)
        {
            GUILayout.BeginArea(area);
            scroll = GUILayout.BeginScrollView(scroll, false, false, GUIStyle.none, GUIStyle.none);

            GUILayout.BeginHorizontal();
            GUILayout.Label(p.Name, Theme.TextStyle(22, Theme.TextColor, "left", true), GUILayout.Height(32));
            GUILayout.Space(10);
            GUILayout.Label("v" + p.Version, Theme.TextStyle(12, Theme.Dim), GUILayout.Height(32));
            GUILayout.FlexibleSpace();
            if (p.Lua != null && GUILayout.Button("Reload", Btn(Theme.Card, Theme.Raised, 11, Theme.Dim), GUILayout.Height(26))) LuaEngine.Reload(p.Lua);
            if (p.Enabled != null) { GUILayout.Space(8); p.Enabled.Value = Switch(p.Enabled.Value); }
            GUILayout.EndHorizontal();
            GUILayout.Label(p.Description, Theme.TextStyle(13, Theme.Dim, "left", false, true));
            GUILayout.Space(10);

            if (p.Error != null)
            {
                GUILayout.BeginVertical(Theme.Box(new Color(Theme.Bad.r, Theme.Bad.g, Theme.Bad.b, 0.15f), 8, 12));
                GUILayout.Label("This mod stopped because of an error:", Theme.TextStyle(13, Theme.Bad, "left", true));
                GUILayout.Label(p.Error, Theme.TextStyle(12, Theme.TextColor, "left", false, true));
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            if (p.Buttons.Count > 0)
            {
                GUI.enabled = ModCommon.InRound && (p.Enabled == null || p.Enabled.Value) && p.Error == null;
                GUILayout.BeginHorizontal();
                foreach (var b in p.Buttons)
                    if (GUILayout.Button(b.Key, Btn(Theme.Raised, Theme.Line))) { try { b.Value(); } catch (Exception e) { Log.LogError(e); } }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUI.enabled = true;
                if (!ModCommon.InRound) GUILayout.Label("Buttons work during a round.", Theme.TextStyle(11, Theme.Dim));
                GUILayout.Space(6);
            }

            var cfg = p.Config;
            var entries = cfg.Where(kv => kv.Key.Section != "General" && kv.Key.Section != "Keys").ToList();
            var basic = entries.Where(kv => !IsAdvanced(kv.Value)).ToList();
            var more = entries.Where(kv => IsAdvanced(kv.Value)).ToList();
            var keys = cfg.Where(kv => kv.Value is ConfigEntry<KeyboardShortcut>).ToList();

            if (basic.Count > 0) { Heading("Settings"); foreach (var kv in basic) Row(kv.Key, kv.Value); }
            if (keys.Count > 0) { Heading("Keys"); foreach (var kv in keys) Row(kv.Key, kv.Value); }
            if (more.Count > 0)
            {
                GUILayout.Space(8);
                if (GUILayout.Button(showMore ? "Hide extra settings" : $"More settings ({more.Count})", Btn(Theme.Card, Theme.Raised, 12, Theme.Dim))) showMore = !showMore;
                if (showMore) foreach (var kv in more) Row(kv.Key, kv.Value);
            }

            GUILayout.Space(14);
            GUILayout.BeginHorizontal();
            GUILayout.Label(string.IsNullOrEmpty(GUI.tooltip) ? "Changes save automatically." : GUI.tooltip, Theme.TextStyle(12, Theme.Dim, "left", false, true));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset to defaults", Btn(Theme.Card, Theme.Raised, 11, Theme.Dim)))
                foreach (var kv in cfg) if (kv.Key.Section != "General") kv.Value.BoxedValue = kv.Value.DefaultValue;
            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static bool IsAdvanced(ConfigEntryBase e) => e.Description.Tags != null && e.Description.Tags.Contains("Advanced");

        static void Heading(string text)
        {
            GUILayout.Space(12);
            GUILayout.Label(text.ToUpperInvariant(), Theme.TextStyle(11, Theme.Dim, "left", true), GUILayout.Height(22));
            Theme.Rect(GUILayoutUtility.GetRect(1, 1, GUILayout.ExpandWidth(true)), Theme.Line);
            GUILayout.Space(4);
        }

        static bool Switch(bool on)
        {
            var r = GUILayoutUtility.GetRect(42, 24, GUILayout.Width(42), GUILayout.Height(24));
            r.y += 4;
            GUI.Box(r, GUIContent.none, Theme.Box(on ? Theme.Ok : Theme.Line, 11));
            GUI.Box(new Rect(on ? r.xMax - 21 : r.x + 3, r.y + 3, 18, 18), GUIContent.none, Theme.Box(Color.white, 9));
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { e.Use(); return !on; }
            return on;
        }

        void Row(ConfigDefinition def, ConfigEntryBase e)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(34));
            GUILayout.Label(new GUIContent(def.Key, e.Description.Description), Theme.TextStyle(14, Theme.TextColor), GUILayout.Width(230), GUILayout.Height(30));
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
                    if (GUILayout.Button("<", Btn(Theme.Raised, Theme.Line), GUILayout.Width(34))) str.Value = opts[(idx + opts.Length - 1) % opts.Length];
                    GUILayout.Label(str.Value, Theme.TextStyle(13, Theme.TextColor, "center"), GUILayout.Width(150), GUILayout.Height(30));
                    if (GUILayout.Button(">", Btn(Theme.Raised, Theme.Line), GUILayout.Width(34))) str.Value = opts[(idx + 1) % opts.Length];
                    break;
                }
                case ConfigEntry<KeyboardShortcut> key:
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(rebinding == e ? "Press a key…" : ModCommon.Key(key.Value), Btn(rebinding == e ? Theme.Accent : Theme.Raised, Theme.Line, 13, rebinding == e ? Theme.Bg : Theme.TextColor), GUILayout.MinWidth(110)))
                        rebinding = rebinding == e ? null : e;
                    break;
                case ConfigEntry<string> str:
                    str.Value = GUILayout.TextField(str.Value, Field(), GUILayout.ExpandWidth(true));
                    break;
            }
            GUILayout.EndHorizontal();
        }

        static float Slider(float value, float min, float max)
        {
            var track = Theme.Box(Theme.Line, 3);
            track.fixedHeight = 6;
            track.margin = new RectOffset(0, 12, 13, 0);
            var thumb = Theme.Box(Theme.Accent, 8);
            thumb.fixedWidth = thumb.fixedHeight = 16;
            thumb.margin = new RectOffset(0, 0, -5, 0);
            return GUILayout.HorizontalSlider(value, min, max, track, thumb, GUILayout.ExpandWidth(true));
        }

        static GUIStyle Field()
        {
            var st = Theme.Box(Theme.Card, 6, 6);
            st.font = Theme.Font; st.fontSize = 13;
            st.normal.textColor = Theme.C(Theme.TextColor);
            st.alignment = TextAnchor.MiddleCenter;
            st.margin = new RectOffset(0, 0, 3, 0);
            return st;
        }

        void NumberField(ConfigEntryBase e, string current, Action<string> commit)
        {
            if (!numText.TryGetValue(e, out var t)) t = current;
            string nt = GUILayout.TextField(t, Field(), GUILayout.Width(60), GUILayout.Height(26));
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
