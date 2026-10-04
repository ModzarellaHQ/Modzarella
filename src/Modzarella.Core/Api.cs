using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using Steamworks;
using UnityEngine;

namespace Modz
{
    public static class CheeseApi
    {
        public static ActiveRagdoll Driver;
        public static Rigidbody DriverVehicle;
        public static Func<bool> FirstPersonCheck;
        public static readonly HashSet<Rigidbody> Vehicles = new HashSet<Rigidbody>();

        private static readonly HashSet<ActiveRagdoll> seated = new HashSet<ActiveRagdoll>();

        public static bool MenuOpen => CorePlugin.MenuOpen;
        public static bool FirstPerson => FirstPersonCheck != null && FirstPersonCheck();
        public static bool IsDead(ActiveRagdoll r) => Body.IsDead(r);
        public static bool IsDriving(ActiveRagdoll r) => Driver && r && Driver == r;
        public static bool IsSeated(ActiveRagdoll r) => r && (seated.Contains(r) || IsDriving(r));
        public static bool IsVehicle(Rigidbody rb) => rb && Vehicles.Contains(rb);

        public static void SetSeated(ActiveRagdoll r, bool on)
        {
            if (!r) return;
            if (on) seated.Add(r); else seated.Remove(r);
        }
    }

    public static class ModCommon
    {
        public static bool Active =>
            GameManager.Instance != null && GameManager.Instance.playingOffline && LobbyManager.Instance != null;

        public static bool InRound =>
            Active && StageManager.Instance != null && EntityManager.Instance != null && GameManager.Instance.round != 0;

        public static bool CountingDown => GameManager.Instance != null && GameManager.Instance.GetCountdown() > 0f;
        public static bool Paused => StageManager.Instance != null && StageManager.Instance.isPaused;
        public static int GroundMask => LayerMask.GetMask("Ground", "Obstacle");

        public static ActiveRagdoll LocalRagdoll()
        {
            if (!InRound) return null;
            try { return EntityManager.Instance.GetRagdoll(SteamUser.GetSteamID(), out var r) && r ? r : null; }
            catch { return null; }
        }

        public static bool IsLocal(ActiveRagdoll r)
        {
            try { return r && r.playerID == SteamUser.GetSteamID(); } catch { return false; }
        }

        public static List<ActiveRagdoll> AllRagdolls()
        {
            var list = new List<ActiveRagdoll>();
            if (!InRound) return list;
            foreach (var r in EntityManager.Instance.GetRagdolls())
                if (r && r.active) list.Add(r);
            return list;
        }

        public static readonly AccessTools.FieldRef<ActiveRagdoll, bool> Grounded = AccessTools.FieldRefAccess<ActiveRagdoll, bool>("grounded");
        public static readonly AccessTools.FieldRef<ActiveRagdoll, float> LastGrounded = AccessTools.FieldRefAccess<ActiveRagdoll, float>("lastGrounded");
        public static readonly AccessTools.FieldRef<ActiveRagdoll, float> UngroundCooldown = AccessTools.FieldRefAccess<ActiveRagdoll, float>("ungroundCooldown");
        public static readonly AccessTools.FieldRef<ActiveRagdoll, float> RecoveryCooldown = AccessTools.FieldRefAccess<ActiveRagdoll, float>("recoveryCooldown");
        public static readonly AccessTools.FieldRef<ActiveRagdoll, float> RecoveryDelay = AccessTools.FieldRefAccess<ActiveRagdoll, float>("recoveryDelay");
        private static readonly AccessTools.FieldRef<ActiveRagdoll, float> StandingHeight = AccessTools.FieldRefAccess<ActiveRagdoll, float>("standingHeight");

        public static void Unground(ActiveRagdoll r, bool stun = false)
        {
            Grounded(r) = false;
            LastGrounded(r) = 0f;
            UngroundCooldown(r) = 0f;
            if (stun) RecoveryCooldown(r) = RecoveryDelay(r);
        }

        public static float BodyScale(ActiveRagdoll r) => r ? Mathf.Clamp(StandingHeight(r), 4f, 20f) : 9f;

        public static float GameSfxVolume
        {
            get
            {
                try
                {
                    var st = SettingsManager.Instance.savedSettings;
                    return Mathf.Clamp01(st.MasterVolume) * Mathf.Clamp01(st.SFXVolume);
                }
                catch { return 0.5f; }
            }
        }

        public static void Toast(string msg) => CorePlugin.Toast(msg);

        public static ConfigDescription Desc(string text, AcceptableValueBase range = null, bool advanced = false) =>
            new ConfigDescription(text, range, advanced ? new object[] { "Advanced" } : new object[0]);

        public static bool KeyDown(KeyboardShortcut k) => Application.isFocused && k.IsDown();
        public static bool KeyHeld(KeyboardShortcut k) => Application.isFocused && k.IsPressed();

        public static string Key(KeyboardShortcut k)
        {
            if (k.MainKey == KeyCode.None) return "unbound";
            return string.Join("+", k.Modifiers.Concat(new[] { k.MainKey }).Select(KeyLabel).ToArray());
        }

        static string KeyLabel(KeyCode c)
        {
            switch (c)
            {
                case KeyCode.Mouse0: return "LMB";
                case KeyCode.Mouse1: return "RMB";
                case KeyCode.Mouse2: return "MMB";
                case KeyCode.LeftControl: case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftAlt: case KeyCode.RightAlt: return "Alt";
                case KeyCode.Return: return "Enter";
                case KeyCode.Escape: return "Esc";
            }
            if (c >= KeyCode.Alpha0 && c <= KeyCode.Alpha9) return ((int)(c - KeyCode.Alpha0)).ToString();
            if (c >= KeyCode.Mouse3 && c <= KeyCode.Mouse6) return "Mouse" + (int)(c - KeyCode.Mouse0 + 1);
            return c.ToString();
        }

        public static string ModDir(Type pluginType) => System.IO.Path.GetDirectoryName(pluginType.Assembly.Location);

        private static Material unlit;
        private static readonly Dictionary<int, Material> solid = new Dictionary<int, Material>();

        public static Material UnlitMaterial(Texture tex = null)
        {
            if (unlit == null)
                unlit = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("UI/Default"));
            return tex == null ? unlit : new Material(unlit) { mainTexture = tex };
        }

        public static Material Solid(Color c, float gloss = 0.4f, float metal = 0f)
        {
            int key = c.GetHashCode() ^ gloss.GetHashCode() ^ (metal.GetHashCode() << 1);
            if (solid.TryGetValue(key, out var mat) && mat) return mat;
            mat = new Material(Shaders.Lit) { color = c };
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", gloss);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metal);
            if (mat.HasProperty("_Shininess")) mat.SetFloat("_Shininess", Mathf.Lerp(0.03f, 0.5f, gloss));
            if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", new Color(gloss * 0.5f, gloss * 0.5f, gloss * 0.5f));
            solid[key] = mat;
            return mat;
        }

        public static Texture2D BlobTexture(int size = 64, float noise = 0f, int seed = 1)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 100f, oy = (float)rng.NextDouble() * 100f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                if (noise > 0f)
                {
                    float ang = Mathf.Atan2(dy, dx);
                    d -= noise * (Mathf.PerlinNoise(ox + Mathf.Cos(ang) * 2f, oy + Mathf.Sin(ang) * 2f) - 0.5f);
                }
                t.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((1f - d) * (noise > 0f ? 6f : 1.6f))));
            }
            t.Apply();
            return t;
        }

        public static void StripCollider(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c) UnityEngine.Object.Destroy(c);
        }

        public static void PlayOneShot(AudioSource src, AudioClip clip, float volume)
        {
            if (src && clip) src.PlayOneShot(clip, volume * GameSfxVolume * 2f);
        }
    }

    public static class Wav
    {
        public static AudioClip Load(string path)
        {
            var b = System.IO.File.ReadAllBytes(path);
            int pos = 12, channels = 1, rate = 44100, bits = 16;
            while (pos + 8 <= b.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(b, pos, 4);
                int size = BitConverter.ToInt32(b, pos + 4);
                if (id == "fmt ")
                {
                    channels = BitConverter.ToInt16(b, pos + 10);
                    rate = BitConverter.ToInt32(b, pos + 12);
                    bits = BitConverter.ToInt16(b, pos + 22);
                }
                else if (id == "data")
                {
                    if (bits != 16) throw new Exception("only 16-bit PCM WAV is supported: " + path);
                    int n = size / 2;
                    var d = new float[n];
                    for (int i = 0; i < n; i++) d[i] = BitConverter.ToInt16(b, pos + 8 + i * 2) / 32768f;
                    var c = AudioClip.Create(System.IO.Path.GetFileNameWithoutExtension(path), n / channels, channels, rate, false);
                    c.SetData(d, 0);
                    return c;
                }
                pos += 8 + size + (size & 1);
            }
            throw new Exception("no data chunk: " + path);
        }

        public static Dictionary<string, AudioClip> LoadFolder(string dir)
        {
            var clips = new Dictionary<string, AudioClip>();
            if (!System.IO.Directory.Exists(dir)) return clips;
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.wav"))
                try { clips[System.IO.Path.GetFileNameWithoutExtension(f)] = Load(f); }
                catch (Exception e) { Debug.LogWarning("[Modz] " + e.Message); }
            return clips;
        }
    }
}
