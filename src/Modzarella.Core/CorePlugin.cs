using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx;
using BepInEx.Bootstrap;
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
        public const string Prefix = "modz.";
        public static CorePlugin Instance;
        internal static ManualLogSource Log;

        public ConfigEntry<KeyboardShortcut> MenuKey;

        private bool menuOpen;
        private Rect window;
        private Vector2 scroll;
        private int page;
        private bool showAdvanced;
        private string search = "";
        private ConfigEntryBase rebinding;
        private readonly Dictionary<ConfigEntryBase, string> numText = new Dictionary<ConfigEntryBase, string>();
        private CursorLockMode prevLock;
        private bool prevVisible;
        private string toast;
        private float toastUntil;
        private GUIStyle h, dim, section, tabOn;

        public static bool MenuOpen => Instance && Instance.menuOpen;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            MenuKey = Config.Bind("Keys", "Mod menu", new KeyboardShortcut(KeyCode.F1), "Open or close the mod menu.");
            new Harmony(GUID).PatchAll(typeof(CorePatches));
            gameObject.AddComponent<CameraFeature>().Init(Config);
        }

        public static void Toast(string msg)
        {
            if (!Instance) { Debug.Log("[Modz] " + msg); return; }
            Instance.toast = msg;
            Instance.toastUntil = Time.unscaledTime + 3f;
            Log.LogInfo(msg);
        }

        private void Update()
        {
            if (rebinding == null && ModCommon.KeyDown(MenuKey.Value)) SetMenu(!menuOpen);
            KeepCursor();
        }

        private void LateUpdate() => KeepCursor();

        private void KeepCursor()
        {
            if (!menuOpen) return;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void SetMenu(bool open)
        {
            if (open == menuOpen) return;
            menuOpen = open;
            rebinding = null;
            if (open) { prevLock = Cursor.lockState; prevVisible = Cursor.visible; }
            else { Cursor.lockState = prevLock; Cursor.visible = prevVisible; }
        }

        private static IEnumerable<PluginInfo> Mods() =>
            Chainloader.PluginInfos.Values
                .Where(p => p.Metadata.GUID.StartsWith(Prefix) && p.Instance)
                .OrderBy(p => p.Metadata.GUID == GUID ? 0 : 1).ThenBy(p => p.Metadata.Name);

        private static ConfigEntry<bool> MasterSwitch(PluginInfo p)
        {
            foreach (var kv in p.Instance.Config)
                if (kv.Key.Section == "General" && kv.Key.Key == "Enabled" && kv.Value is ConfigEntry<bool> b) return b;
            return null;
        }

        private static string Blurb(PluginInfo p) => MasterSwitch(p)?.Description.Description ?? "Built in: mod menu, mouse camera, first person, zoom.";
        private static bool IsAdvanced(ConfigEntryBase e) => e.Description.Tags != null && e.Description.Tags.Contains("Advanced");

        private void OnGUI()
        {
            if (toast != null && Time.unscaledTime < toastUntil)
                GUI.Box(new Rect(Screen.width / 2 - 250, 30, 500, 40), toast, new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter });
            if (!menuOpen) return;
            KeepCursor();
            CaptureRebind();
            if (h == null)
            {
                h = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
                dim = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true, normal = { textColor = new Color(0.78f, 0.78f, 0.78f) } };
                section = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.75f, 0.35f) } };
                tabOn = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.3f), background = GUI.skin.button.active.background } };
            }
            if (window.width < 10f) window = new Rect(40, 40, 620, 680);
            window = GUILayout.Window(0xC4EE5E, window, DrawWindow, "Modzarella");
            window.x = Mathf.Clamp(window.x, 0, Screen.width - 100);
            window.y = Mathf.Clamp(window.y, 0, Screen.height - 60);
        }

        private void CaptureRebind()
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

        private void DrawWindow(int id)
        {
            var mods = Mods().ToList();
            GUILayout.BeginHorizontal();
            GUILayout.Label(ModCommon.Active ? "Play Offline — mods active" : "Mods only run in Main Menu → Play Offline", dim);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(60))) SetMenu(false);
            GUILayout.EndHorizontal();

            var names = new List<string> { "Home" };
            names.AddRange(mods.Select(m => m.Metadata.Name));
            names.Add("Controls");
            var pages = mods;
            page = Mathf.Clamp(page, 0, names.Count - 1);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Count; i++)
                if (GUILayout.Button(names[i], i == page ? tabOn : GUI.skin.button)) { page = i; scroll = Vector2.zero; search = ""; }
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);
            if (page == 0) DrawHome(pages);
            else if (page == names.Count - 1) DrawControls(mods);
            else DrawModPage(pages[page - 1]);
            GUILayout.EndScrollView();

            GUILayout.Label(string.IsNullOrEmpty(GUI.tooltip) ? $"{ModCommon.Key(MenuKey.Value)}: menu · settings save automatically" : GUI.tooltip, dim, GUILayout.Height(30));
            GUI.DragWindow();
        }

        private void Actions(PluginInfo m, ConfigEntry<bool> sw)
        {
            var actions = MenuRegistry.Get(m.Metadata.GUID);
            if (actions.Count == 0) return;
            GUI.enabled = ModCommon.InRound && (sw == null || sw.Value);
            GUILayout.BeginHorizontal();
            foreach (var a in actions)
                if (GUILayout.Button(a.Key)) { try { a.Value(); } catch (System.Exception e) { Log.LogError(e); } }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
        }

        private void DrawHome(List<PluginInfo> mods)
        {
            foreach (var m in mods)
            {
                var sw = MasterSwitch(m);
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (sw != null) sw.Value = GUILayout.Toggle(sw.Value, new GUIContent(" " + m.Metadata.Name, sw.Description.Description), h);
                else GUILayout.Label(m.Metadata.Name, h);
                GUILayout.FlexibleSpace();
                GUILayout.Label("v" + m.Metadata.Version, dim, GUILayout.Width(50));
                if (GUILayout.Button("Settings", GUILayout.Width(80))) { page = mods.IndexOf(m) + 1; scroll = Vector2.zero; }
                GUILayout.EndHorizontal();
                GUILayout.Label(Blurb(m), dim);
                Actions(m, sw);
                GUILayout.EndVertical();
            }
            if (MenuRegistry.Toggles.Count == 0) return;
            GUILayout.Space(6);
            GUILayout.Label("Quick toggles", h);
            GUILayout.BeginVertical(GUI.skin.box);
            foreach (var t in MenuRegistry.Toggles)
                t.Value.Value = GUILayout.Toggle(t.Value.Value, new GUIContent(" " + t.Key, t.Value.Description.Description));
            GUILayout.EndVertical();
        }

        private void DrawModPage(PluginInfo m)
        {
            var cfg = m.Instance.Config;
            var sw = MasterSwitch(m);
            if (sw != null) sw.Value = GUILayout.Toggle(sw.Value, new GUIContent($" {m.Metadata.Name} enabled", sw.Description.Description), h);
            GUILayout.Label(Blurb(m), dim);
            Actions(m, sw);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Search:", GUILayout.Width(52));
            search = GUILayout.TextField(search);
            showAdvanced = GUILayout.Toggle(showAdvanced, " Show advanced", GUILayout.Width(120));
            GUILayout.EndHorizontal();

            string q = search.Trim().ToLowerInvariant();
            foreach (var sec in cfg.Keys.Select(k => k.Section).Distinct().Where(x => x != "General" && x != "Keys"))
            {
                var entries = cfg.Where(k => k.Key.Section == sec)
                    .Where(k => showAdvanced || q.Length > 0 || !IsAdvanced(k.Value))
                    .Where(k => q.Length == 0 || k.Key.Key.ToLowerInvariant().Contains(q) || k.Value.Description.Description.ToLowerInvariant().Contains(q))
                    .ToList();
                if (entries.Count == 0) continue;
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label(sec, section);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(new GUIContent("Reset", "Back to defaults for this section."), GUILayout.Width(60)))
                    foreach (var kv in cfg.Where(k => k.Key.Section == sec)) kv.Value.BoxedValue = kv.Value.DefaultValue;
                GUILayout.EndHorizontal();
                GUILayout.BeginVertical(GUI.skin.box);
                foreach (var kv in entries) DrawEntry(kv.Key, kv.Value);
                GUILayout.EndVertical();
            }
            var keys = cfg.Where(k => k.Value is ConfigEntry<KeyboardShortcut>).ToList();
            if (keys.Count == 0 || q.Length > 0) return;
            GUILayout.Space(6);
            GUILayout.Label("Keys", section);
            GUILayout.BeginVertical(GUI.skin.box);
            foreach (var kv in keys) DrawEntry(kv.Key, kv.Value);
            GUILayout.EndVertical();
        }

        private void DrawControls(List<PluginInfo> mods)
        {
            GUILayout.Label("Click a binding, then press a key or mouse button (Esc cancels). Ctrl/Shift/Alt combos work.", dim);
            foreach (var m in mods)
            {
                var keys = m.Instance.Config.Where(kv => kv.Value is ConfigEntry<KeyboardShortcut>).ToList();
                if (keys.Count == 0) continue;
                GUILayout.Space(6);
                GUILayout.Label(m.Metadata.Name, section);
                GUILayout.BeginVertical(GUI.skin.box);
                foreach (var kv in keys) DrawEntry(kv.Key, kv.Value);
                GUILayout.EndVertical();
            }
            GUILayout.Space(8);
            if (GUILayout.Button(new GUIContent("Reset every mod setting to defaults", "Everything, keys included.")))
                foreach (var m in mods) foreach (var kv in m.Instance.Config) kv.Value.BoxedValue = kv.Value.DefaultValue;
        }

        private void DrawEntry(ConfigDefinition def, ConfigEntryBase e)
        {
            var label = new GUIContent(def.Key, e.Description.Description);
            GUILayout.BeginHorizontal();
            switch (e)
            {
                case ConfigEntry<bool> b:
                    b.Value = GUILayout.Toggle(b.Value, new GUIContent(" " + def.Key, e.Description.Description));
                    break;
                case ConfigEntry<float> f:
                {
                    var range = e.Description.AcceptableValues as AcceptableValueRange<float>;
                    float min = range?.MinValue ?? 0f, max = range?.MaxValue ?? Mathf.Max(1f, f.Value * 2f);
                    GUILayout.Label(label, GUILayout.Width(190));
                    float v = GUILayout.HorizontalSlider(f.Value, min, max, GUILayout.MinWidth(120));
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
                    GUILayout.Label(label, GUILayout.Width(190));
                    int v = Mathf.RoundToInt(GUILayout.HorizontalSlider(i.Value, min, max, GUILayout.MinWidth(120)));
                    if (v != i.Value) { i.Value = v; numText.Remove(e); }
                    NumberField(e, i.Value.ToString(CultureInfo.InvariantCulture), s => { if (int.TryParse(s, out var nv)) i.Value = Mathf.Clamp(nv, min, max); });
                    break;
                }
                case ConfigEntry<string> str when e.Description.AcceptableValues is AcceptableValueList<string> list:
                {
                    GUILayout.Label(label, GUILayout.Width(190));
                    int idx = System.Math.Max(0, System.Array.IndexOf(list.AcceptableValues, str.Value));
                    if (GUILayout.Button("◄", GUILayout.Width(26))) str.Value = list.AcceptableValues[(idx + list.AcceptableValues.Length - 1) % list.AcceptableValues.Length];
                    GUILayout.Label(str.Value, GUI.skin.box, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("►", GUILayout.Width(26))) str.Value = list.AcceptableValues[(idx + 1) % list.AcceptableValues.Length];
                    break;
                }
                case ConfigEntry<string> str:
                    GUILayout.Label(label, GUILayout.Width(190));
                    str.Value = GUILayout.TextField(str.Value);
                    break;
                case ConfigEntry<KeyboardShortcut> key:
                    GUILayout.Label(label, GUILayout.Width(190));
                    if (GUILayout.Button(rebinding == e ? "press a key…" : ModCommon.Key(key.Value))) rebinding = rebinding == e ? null : e;
                    if (GUILayout.Button(new GUIContent("×", "Unbind"), GUILayout.Width(24))) key.Value = KeyboardShortcut.Empty;
                    break;
                default:
                    GUILayout.Label($"{def.Key}: {e.BoxedValue}");
                    break;
            }
            if (!(e is ConfigEntry<KeyboardShortcut>) && !Equals(e.BoxedValue, e.DefaultValue)
                && GUILayout.Button(new GUIContent("↺", "Reset to default: " + e.DefaultValue), GUILayout.Width(24)))
            {
                e.BoxedValue = e.DefaultValue;
                numText.Remove(e);
            }
            GUILayout.EndHorizontal();
        }

        private void NumberField(ConfigEntryBase e, string current, System.Action<string> commit)
        {
            if (!numText.TryGetValue(e, out var t)) t = current;
            string nt = GUILayout.TextField(t, GUILayout.Width(56));
            if (nt != t) numText[e] = nt;
            if (numText.ContainsKey(e) && (Event.current.isKey && Event.current.keyCode == KeyCode.Return || nt != t && nt.Length > 0 && !nt.EndsWith(".")))
                commit(nt);
        }
    }

    internal static class CorePatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(InputManager), nameof(InputManager.ShowCursor))]
        private static bool KeepCursorForMenu() => !CorePlugin.MenuOpen;

        [HarmonyPrefix, HarmonyPatch(typeof(InputManager), "Look")]
        private static bool NoLookInMenu() => !CorePlugin.MenuOpen;

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), nameof(ActiveRagdoll.Input))]
        private static bool SeatedNoInput(ActiveRagdoll __instance) => !CheeseApi.IsSeated(__instance) && !CheeseApi.IsDead(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), "FixedUpdate")]
        private static bool SeatedNoBalance(ActiveRagdoll __instance) => !CheeseApi.IsSeated(__instance) && !CheeseApi.IsDead(__instance);
    }
}
