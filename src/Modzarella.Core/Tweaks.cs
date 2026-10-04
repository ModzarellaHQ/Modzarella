using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Modz
{
    public class TweaksFeature : MonoBehaviour
    {
        public static TweaksFeature Instance;

        public ConfigEntry<bool> Enabled, Endless, FreezeBots, SlowMo, VSyncOff;
        public ConfigEntry<int> FpsCap;
        public ConfigEntry<float> SlowMoSpeed;
        public ConfigEntry<KeyboardShortcut> EndlessKey, FreezeKey, SlowMoKey, RestartKey, NextMapKey;
        private float baseFixedDt;
        private bool slowApplied;

        public void Init(ConfigFile Config)
        {
            Instance = this;
            Enabled = Config.Bind("Game", "Game tweaks", true, "Endless round, frozen bots, slow motion and frame-rate settings.");
            Endless = Config.Bind("Game", "Endless round", false, "Nobody can catch the cheese and the timer stops before the final countdown.");
            FreezeBots = Config.Bind("Game", "Freeze bots", false, "Bots stand still and do nothing.");
            SlowMo = Config.Bind("Game", "Slow motion", false, "Everything runs in slow motion.");
            SlowMoSpeed = Config.Bind("Game", "Slow motion speed", 0.3f, ModCommon.Desc("Game speed while slow motion is on.", new AcceptableValueRange<float>(0.05f, 0.9f)));
            VSyncOff = Config.Bind("Performance", "Disable VSync", true, "VSync drops to 30 fps whenever the game misses 60. Off is smoother.");
            FpsCap = Config.Bind("Performance", "FPS cap (0 means none)", 0, ModCommon.Desc("Frame-rate cap when VSync is off.", new AcceptableValueRange<int>(0, 240), true));
            EndlessKey = Config.Bind("Keys", "Endless round", new KeyboardShortcut(KeyCode.F2), "Toggle endless round.");
            FreezeKey = Config.Bind("Keys", "Freeze bots", new KeyboardShortcut(KeyCode.F4), "Toggle frozen bots.");
            SlowMoKey = Config.Bind("Keys", "Slow motion", new KeyboardShortcut(KeyCode.F5), "Toggle slow motion.");
            RestartKey = Config.Bind("Keys", "Restart map", new KeyboardShortcut(KeyCode.F6), "Start the current map again.");
            NextMapKey = Config.Bind("Keys", "Next map", new KeyboardShortcut(KeyCode.F7), "Go to the next map.");
            SlowMo.SettingChanged += (_, __) => ModCommon.Toast(SlowMo.Value ? "Slow motion" : "Normal speed");
            baseFixedDt = Time.fixedDeltaTime;
            Endless.SettingChanged += (_, __) => ModCommon.Toast(Endless.Value ? "Endless round on" : "Endless round off — 6 s left");
            FreezeBots.SettingChanged += (_, __) => ModCommon.Toast(FreezeBots.Value ? "Bots frozen" : "Bots unfrozen");
            VSyncOff.SettingChanged += (_, __) => ApplyPerf();
            FpsCap.SettingChanged += (_, __) => ApplyPerf();
            ApplyPerf();
            new Harmony(CorePlugin.GUID + ".tweaks").PatchAll(typeof(TweaksPatches));
        }

        internal bool On => Enabled.Value && ModCommon.Active;

        private void Update()
        {
            if (VSyncOff.Value && QualitySettings.vSyncCount != 0) ApplyPerf();
            ApplySlowMo();
            if (!ModCommon.Active || CheeseApi.MenuOpen) return;
            if (ModCommon.KeyDown(SlowMoKey.Value)) SlowMo.Value = !SlowMo.Value;
            if (ModCommon.KeyDown(EndlessKey.Value)) Endless.Value = !Endless.Value;
            if (ModCommon.KeyDown(FreezeKey.Value)) FreezeBots.Value = !FreezeBots.Value;
            if (!ModCommon.InRound || !GameManager.Instance) return;
            if (ModCommon.KeyDown(NextMapKey.Value)) GameManager.Instance.LoadRandomMap();
            else if (ModCommon.KeyDown(RestartKey.Value)) CheeseApi.RestartMap();
        }

        private void ApplySlowMo()
        {
            bool want = On && SlowMo.Value && ModCommon.InRound && !ModCommon.Paused;
            if (!want && !slowApplied || Time.timeScale == 0f) return;
            float scale = want ? SlowMoSpeed.Value : 1f;
            Time.timeScale = scale;
            Time.fixedDeltaTime = baseFixedDt * scale;
            slowApplied = want;
        }

        private void ApplyPerf()
        {
            QualitySettings.vSyncCount = VSyncOff.Value ? 0 : 1;
            Application.targetFrameRate = VSyncOff.Value && FpsCap.Value > 0 ? FpsCap.Value : -1;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || !On || !ModCommon.InRound) return;
            var tags = new List<string>();
            if (Endless.Value) tags.Add("Endless round");
            if (FreezeBots.Value) tags.Add("Bots frozen");
            if (SlowMo.Value) tags.Add("Slow motion");
            if (CameraFeature.Flying) tags.Add("Freecam");
            if (tags.Count == 0) return;
            string text = string.Join("  ·  ", tags.ToArray());
            float w = Theme.TextStyle(11, Theme.Dim, "center").CalcSize(new GUIContent(text)).x + 20;
            Theme.Badge(new Rect(12, 12, w, 22), text);
        }
    }

    internal static class TweaksPatches
    {
        private static TweaksFeature P => TweaksFeature.Instance;
        private static bool EndlessOn => P && P.On && P.Endless.Value;

        [HarmonyPrefix, HarmonyPatch(typeof(Cheese), nameof(Cheese.Catch))]
        private static bool NoCatch(ref bool __result)
        {
            if (!EndlessOn) return true;
            __result = false;
            return false;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GameManager), "Update")]
        private static void HoldClock(GameManager __instance)
        {
            if (!EndlessOn || __instance.round == 0) return;
            float hold = __instance.GetRoundLength() - 6f;
            if (__instance.roundTime <= hold) return;
            __instance.roundStartTime = Time.time - hold;
            __instance.roundTime = hold;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameManager), nameof(GameManager.RestartGame))]
        private static bool NoRestart() => !EndlessOn;

        [HarmonyPrefix, HarmonyPatch(typeof(BotController), nameof(BotController.CreateInput))]
        private static bool FreezeBot(BotController __instance)
        {
            if (!P || !P.On || !P.FreezeBots.Value) return true;
            var r = __instance.GetComponent<ActiveRagdoll>();
            if (r && !CheeseApi.IsSeated(r)) r.Input(new RagdollInput(false, false, false, Vector3.zero));
            return false;
        }
    }
}
