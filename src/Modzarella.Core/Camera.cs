using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Modz
{
    public class CameraFeature : MonoBehaviour
    {
        internal static CameraFeature Instance;

        public ConfigEntry<bool> Enabled, FreeCam, FirstPerson, InvertY, AutoCenter, ScrollZoom;
        public ConfigEntry<float> MouseSens, Zoom, SpeedFov;
        public ConfigEntry<KeyboardShortcut> FreeCamKey, FirstPersonKey;

        internal Transform eye;
        private bool lockedCursor;
        private float lastMouseMove;

        public void Init(ConfigFile Config)
        {
            Instance = this;
            Enabled = Config.Bind("Camera", "Camera features", true, "Mouse camera, first person, zoom and speed FOV.");
            FreeCam = Config.Bind("Camera", "Free mouse camera", true, "The mouse turns the camera instead of it auto-turning towards the cheese.");
            FirstPerson = Config.Bind("Camera", "First person", false, "See through your character's eyes.");
            MouseSens = Config.Bind("Camera", "Mouse sensitivity", 0.15f, ModCommon.Desc("Degrees per pixel of mouse movement.", new AcceptableValueRange<float>(0.02f, 1f)));
            Zoom = Config.Bind("Camera", "Zoom", 1f, ModCommon.Desc("Camera distance multiplier.", new AcceptableValueRange<float>(0.4f, 4f)));
            InvertY = Config.Bind("Camera", "Invert mouse Y", false, ModCommon.Desc("Invert vertical mouse look.", advanced: true));
            AutoCenter = Config.Bind("Camera", "Recentre behind vehicle", true, ModCommon.Desc("While driving, swing back behind the car when the mouse is idle.", advanced: true));
            ScrollZoom = Config.Bind("Camera", "Scroll wheel zoom", true, ModCommon.Desc("Mouse wheel zooms.", advanced: true));
            SpeedFov = Config.Bind("Camera", "Speed FOV boost", 14f, ModCommon.Desc("Extra field of view at high speed.", new AcceptableValueRange<float>(0f, 40f), true));
            FreeCamKey = Config.Bind("Keys", "Free camera", new KeyboardShortcut(KeyCode.F3), "Toggle the free mouse camera.");
            FirstPersonKey = Config.Bind("Keys", "First person", new KeyboardShortcut(KeyCode.V), "Toggle first person.");
            FreeCam.SettingChanged += (_, __) => ModCommon.Toast(FreeCam.Value ? "Free camera" : "Auto camera");
            FirstPerson.SettingChanged += (_, __) => ModCommon.Toast(FirstPerson.Value ? "First person" : "Third person");

            CheeseApi.FirstPersonCheck = () => FPActive;
            MenuRegistry.QuickToggle($"Free mouse camera ({ModCommon.Key(FreeCamKey.Value)})", FreeCam);
            MenuRegistry.QuickToggle($"First person ({ModCommon.Key(FirstPersonKey.Value)})", FirstPerson);
            new Harmony(CorePlugin.GUID + ".camera").PatchAll(typeof(CameraPatches));
        }

        private readonly List<KeyValuePair<Transform, Vector3>> hidden = new List<KeyValuePair<Transform, Vector3>>();

        private void OnEnable() { Camera.onPreCull += HideOwnHead; Camera.onPostRender += ShowOwnHead; }
        private void OnDisable() { Camera.onPreCull -= HideOwnHead; Camera.onPostRender -= ShowOwnHead; }

        private void HideOwnHead(Camera cam)
        {
            if (!FPActive || !StageManager.Instance || cam != StageManager.Instance.cameraRig.mainCamera) return;
            var me = ModCommon.LocalRagdoll();
            foreach (var part in new[] { me.head, me.spine2 })
            {
                if (!part || part.transform.localScale.x < 0.01f) continue;
                hidden.Add(new KeyValuePair<Transform, Vector3>(part.transform, part.transform.localScale));
                part.transform.localScale = Vector3.one * 0.001f;
            }
        }

        private void ShowOwnHead(Camera cam)
        {
            foreach (var kv in hidden) if (kv.Key) kv.Key.localScale = kv.Value;
            hidden.Clear();
        }

        internal bool On => Enabled.Value && ModCommon.Active;
        internal bool FPActive => FirstPerson.Value && On && ModCommon.LocalRagdoll();

        private void Update()
        {
            if (!ModCommon.InRound || CheeseApi.MenuOpen) { ReleaseCursor(); return; }
            if (ModCommon.KeyDown(FreeCamKey.Value)) FreeCam.Value = !FreeCam.Value;
            if (ModCommon.KeyDown(FirstPersonKey.Value)) FirstPerson.Value = !FirstPerson.Value;
            if (ScrollZoom.Value && Mouse.current != null)
            {
                float sc = Mouse.current.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f) Zoom.Value = Mathf.Clamp(Zoom.Value * (sc > 0 ? 0.9f : 1.1f), 0.4f, 4f);
            }
            MouseLook();
        }

        private void ReleaseCursor()
        {
            if (!lockedCursor || CheeseApi.MenuOpen) return;
            Cursor.lockState = CursorLockMode.None;
            lockedCursor = false;
        }

        private void MouseLook()
        {
            var rig = StageManager.Instance ? StageManager.Instance.cameraRig : null;
            bool want = Enabled.Value && (FreeCam.Value || FPActive) && !ModCommon.Paused && !GameManager.IAmCheese() && Application.isFocused && rig;
            if (!want) { ReleaseCursor(); return; }
            if (!lockedCursor) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; lockedCursor = true; }
            if (Mouse.current != null)
            {
                Vector2 d = Mouse.current.delta.ReadValue() * MouseSens.Value;
                if (d.sqrMagnitude > 0.0001f) lastMouseMove = Time.unscaledTime;
                rig.LookBy(d.x, InvertY.Value ? -d.y : d.y);
            }
            var vehicle = CheeseApi.DriverVehicle;
            if (AutoCenter.Value && vehicle && ModCommon.IsLocal(CheeseApi.Driver) && Time.unscaledTime - lastMouseMove > 1.2f && vehicle.velocity.magnitude > 12f)
            {
                Vector3 f = Vector3.ProjectOnPlane(vehicle.velocity, Vector3.up);
                if (Vector3.Dot(f, vehicle.transform.forward) < 0f) f = Vector3.ProjectOnPlane(vehicle.transform.forward, Vector3.up);
                if (f.sqrMagnitude > 1f)
                {
                    float k = 1f - Mathf.Exp(-Time.deltaTime * 2.2f);
                    rig.yaw = Mathf.LerpAngle(rig.yaw, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg, k);
                    rig.pitch = Mathf.Lerp(rig.pitch, 14f, k);
                }
            }
        }
    }

    internal static class CameraPatches
    {
        private static readonly AccessTools.FieldRef<CameraRig, Vector3> Offset = AccessTools.FieldRefAccess<CameraRig, Vector3>("cameraOffset");
        private static readonly AccessTools.FieldRef<CameraRig, Transform> Target = AccessTools.FieldRefAccess<CameraRig, Transform>("targetTransform");
        private static readonly Dictionary<CameraRig, Vector3> baseOffset = new Dictionary<CameraRig, Vector3>();
        private static Transform thirdPersonTarget;
        private static bool fpWasOn;
        private static float baseNear = -1f, fovBoost;

        private static CameraFeature C => CameraFeature.Instance;

        [HarmonyPrefix, HarmonyPatch(typeof(CameraRig), "Autolook")]
        private static bool NoAutolook() => !(C && C.On && (C.FreeCam.Value || C.FPActive));

        [HarmonyPrefix, HarmonyPatch(typeof(CameraRig), nameof(CameraRig.SetTarget))]
        private static bool RememberTarget(Transform target)
        {
            if (C && C.FPActive && C.eye && target != C.eye) { thirdPersonTarget = target; return false; }
            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CameraRig), "LateUpdate")]
        private static void Before(CameraRig __instance)
        {
            UpdateFirstPerson(__instance);
            if (!baseOffset.TryGetValue(__instance, out var b))
            {
                if (baseOffset.Count > 8) baseOffset.Clear();
                baseOffset[__instance] = b = Offset(__instance);
            }
            float z = 1f;
            if (C && C.On)
            {
                z = C.Zoom.Value;
                if (CheeseApi.DriverVehicle && ModCommon.IsLocal(CheeseApi.Driver)) z *= 1.5f;
                if (C.FPActive) z = 0.0001f;
            }
            Offset(__instance) = b * z;
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CameraRig), "LateUpdate")]
        private static void After(CameraRig __instance)
        {
            if (!C || !C.On || !__instance.mainCamera) return;
            float speed = 0f;
            if (CheeseApi.DriverVehicle && ModCommon.IsLocal(CheeseApi.Driver)) speed = CheeseApi.DriverVehicle.velocity.magnitude;
            else { var me = ModCommon.LocalRagdoll(); if (me) speed = me.velocity.magnitude; }
            fovBoost = Mathf.Lerp(fovBoost, Mathf.Clamp01((speed - 40f) / 220f) * C.SpeedFov.Value, 1f - Mathf.Exp(-Time.deltaTime * 3f));
            __instance.mainCamera.fieldOfView = CameraRig.FOV + fovBoost;
        }

        private static void UpdateFirstPerson(CameraRig rig)
        {
            bool on = C && C.FPActive;
            if (on)
            {
                var me = ModCommon.LocalRagdoll();
                if (!C.eye) C.eye = new GameObject("FirstPersonEye").transform;
                if (!fpWasOn) { thirdPersonTarget = Target(rig); if (baseNear < 0f) baseNear = rig.mainCamera.nearClipPlane; }
                var head = me.head && me.head.transform.localScale.x > 0.01f ? me.head.transform : me.spine2.transform;
                Vector3 look = Quaternion.Euler(0f, rig.yaw, 0f) * Vector3.forward;
                float sc = ModCommon.BodyScale(me);
                C.eye.position = head.position + Vector3.up * sc * 0.05f + look * sc * 0.16f;
                Target(rig) = C.eye;
                rig.mainCamera.nearClipPlane = sc * 0.06f;
            }
            else if (fpWasOn)
            {
                var me = ModCommon.LocalRagdoll();
                Target(rig) = thirdPersonTarget ? thirdPersonTarget : me ? me.spine1.transform : Target(rig);
                if (baseNear > 0f) rig.mainCamera.nearClipPlane = baseNear;
            }
            fpWasOn = on;
        }
    }
}
