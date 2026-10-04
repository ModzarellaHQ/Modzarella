using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx.Configuration;
using MoonSharp.Interpreter;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Modz
{
    public class LuaSetting
    {
        readonly ConfigEntryBase entry;
        internal LuaSetting(ConfigEntryBase entry) { this.entry = entry; }

        public object value
        {
            get => entry.BoxedValue is KeyboardShortcut k ? ModCommon.Key(k) : entry.BoxedValue;
            set => entry.BoxedValue = entry.SettingType == typeof(float) ? Convert.ToSingle(value)
                : entry.SettingType == typeof(int) ? Convert.ToInt32(value)
                : entry.SettingType == typeof(KeyboardShortcut) ? KeyboardShortcut.Deserialize(value.ToString())
                : value;
        }

        public bool down => entry.BoxedValue is KeyboardShortcut k && LuaApi.InputAllowed && k.IsDown();
        public bool held => entry.BoxedValue is KeyboardShortcut k && LuaApi.InputAllowed && k.IsPressed();
        public string label => entry.BoxedValue is KeyboardShortcut k ? ModCommon.Key(k) : entry.BoxedValue.ToString();
    }

    public static class LuaApi
    {
        public static bool InputAllowed => Application.isFocused && !CorePlugin.MenuOpen && !ModCommon.Paused && !CameraFeature.Flying;

        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static readonly Dictionary<string, GlbLoader.Model> models = new Dictionary<string, GlbLoader.Model>();
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();

        static readonly Type[] statics =
        {
            typeof(Vector2), typeof(Vector3), typeof(Quaternion), typeof(Color), typeof(Mathf), typeof(Time), typeof(Physics),
            typeof(GameObject), typeof(PrimitiveType), typeof(ForceMode), typeof(LightType), typeof(LightShadows),
            typeof(RigidbodyInterpolation), typeof(CollisionDetectionMode), typeof(PhysicMaterialCombine), typeof(AudioRolloffMode),
            typeof(Space), typeof(LayerMask), typeof(Ray), typeof(Keyboard), typeof(Mouse), typeof(Gamepad), typeof(GameManager), typeof(StageManager),
        };

        static Table List(Script s, System.Collections.IEnumerable items)
        {
            var t = new Table(s);
            foreach (var x in items) t.Append(DynValue.FromObject(s, x));
            return t;
        }

        static RagdollPart P(DynValue d) => d.IsNil() ? null : d.UserData.Object as RagdollPart;
        static Vector3 V(DynValue d) => (Vector3)d.UserData.Object;

        static T Arg<T>(CallbackArguments a, int i, T fallback = default) => a.Count > i && !a[i].IsNil() ? a[i].ToObject<T>() : fallback;

        static DynValue Fn(Script s, Func<CallbackArguments, object> f) =>
            DynValue.NewCallback((ctx, a) => DynValue.FromObject(s, f(a)));

        static Type FindType(string name)
        {
            if (types.TryGetValue(name, out var t)) return t;
            t = AppDomain.CurrentDomain.GetAssemblies()
                .Select(asm => { try { return asm.GetTypes(); } catch { return new Type[0]; } })
                .SelectMany(x => x)
                .FirstOrDefault(x => x.Name == name && typeof(Component).IsAssignableFrom(x));
            return types[name] = t ?? throw new ScriptRuntimeException($"unknown component type '{name}'");
        }

        static string ModPath(LuaMod mod, string rel)
        {
            var full = Path.GetFullPath(Path.Combine(mod.Dir, rel));
            if (!full.StartsWith(Path.GetFullPath(mod.Dir))) throw new ScriptRuntimeException("paths must stay inside the mod folder");
            return full;
        }

        static ConfigDescription Desc(Table o, AcceptableValueBase range) =>
            ModCommon.Desc(o.Get("desc").CastToString() ?? "", range, o.Get("advanced").CastToBool());

        static string Section(LuaMod mod, Table o) => o.Get("section").CastToString() ?? mod.Name;

        public static void Install(Script s, LuaMod mod)
        {
            var g = s.Globals;
            foreach (var type in statics) g[type.Name] = UserData.CreateStatic(type);
            g["Object"] = UserData.CreateStatic(typeof(UnityEngine.Object));
            g["Random"] = UserData.CreateStatic(typeof(UnityEngine.Random));

            var info = new Table(s);
            info["id"] = mod.Id; info["name"] = mod.Name; info["version"] = mod.Version; info["dir"] = mod.Dir;
            g["mod"] = info;

            g["vec"] = (Func<float, float, float, Vector3>)((x, y, z) => new Vector3(x, y, z));
            g["euler"] = (Func<float, float, float, Quaternion>)Quaternion.Euler;
            g["rgb"] = Fn(s, a => new Color((float)a[0].Number, (float)a[1].Number, (float)a[2].Number, a.Count > 3 && !a[3].IsNil() ? (float)a[3].Number : 1f));
            g["rand"] = (Func<float, float, float>)UnityEngine.Random.Range;
            g["randi"] = (Func<int, int, int>)UnityEngine.Random.Range;
            g["alive"] = Fn(s, a => a[0].ToObject() is UnityEngine.Object o ? (bool)o : !a[0].IsNil());
            g["log"] = Fn(s, a => { CorePlugin.Log.LogInfo($"[{mod.Name}] {string.Join(" ", a.GetArray().Select(x => x.ToPrintString()).ToArray())}"); return null; });
            g["toast"] = (Action<string>)ModCommon.Toast;
            g["after"] = Fn(s, a => { mod.Timers.Add((Time.time + (float)a[0].Number, a[1])); return null; });
            g["new_object"] = Fn(s, a =>
            {
                var go = new GameObject(a[0].CastToString() ?? "Object");
                var parent = Arg<Transform>(a, 1);
                if (parent) go.transform.SetParent(parent, false);
                return go;
            });
            g["primitive"] = Fn(s, a =>
            {
                var go = GameObject.CreatePrimitive((PrimitiveType)Enum.Parse(typeof(PrimitiveType), a[0].String, true));
                if (!(a.Count > 2 && a[2].CastToBool())) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
                var parent = Arg<Transform>(a, 1);
                if (parent) go.transform.SetParent(parent, false);
                return go;
            });
            g["destroy"] = Fn(s, a => { if (a[0].ToObject() is UnityEngine.Object o && o) UnityEngine.Object.Destroy(o, Arg(a, 1, 0f)); return null; });
            g["destroy_now"] = Fn(s, a => { if (a[0].ToObject() is UnityEngine.Object o && o) UnityEngine.Object.DestroyImmediate(o); return null; });
            g["get"] = Fn(s, a => Target(a[0]).GetComponent(FindType(a[1].String)));
            g["add"] = Fn(s, a => Target(a[0]).AddComponent(FindType(a[1].String)));
            g["find"] = (Func<string, GameObject>)GameObject.Find;
            g["list"] = Fn(s, a => List(s, (System.Collections.IEnumerable)a[0].ToObject()));
            g["components"] = Fn(s, a => List(s, Target(a[0]).GetComponents(FindType(a[1].String))));
            g["children"] = Fn(s, a => List(s, Target(a[0]).GetComponentsInChildren(FindType(a[1].String), Arg(a, 2, false))));

            var setting = new Table(s);
            setting["number"] = Fn(s, a =>
            {
                var o = a[0].Table;
                float def = (float)o.Get("default").Number;
                var range = o.Get("min").IsNil() ? null : new AcceptableValueRange<float>((float)o.Get("min").Number, (float)o.Get("max").Number);
                return new LuaSetting(mod.Config.Bind(Section(mod, o), o.Get("name").String, def, Desc(o, range)));
            });
            setting["toggle"] = Fn(s, a =>
            {
                var o = a[0].Table;
                return new LuaSetting(mod.Config.Bind(Section(mod, o), o.Get("name").String, o.Get("default").CastToBool(), Desc(o, null)));
            });
            setting["choice"] = Fn(s, a =>
            {
                var o = a[0].Table;
                var options = o.Get("options").Table.Values.Select(v => v.String).ToArray();
                return new LuaSetting(mod.Config.Bind(Section(mod, o), o.Get("name").String, o.Get("default").String, Desc(o, new AcceptableValueList<string>(options))));
            });
            setting["key"] = Fn(s, a =>
            {
                var o = a[0].Table;
                var key = KeyboardShortcut.Deserialize(o.Get("default").String);
                return new LuaSetting(mod.Config.Bind("Keys", o.Get("name").String, key, o.Get("desc").CastToString() ?? ""));
            });
            g["setting"] = setting;

            var menu = new Table(s);
            menu["button"] = Fn(s, a => { mod.Buttons.Add(new KeyValuePair<string, DynValue>(a[0].String, a[1])); return null; });
            g["menu"] = menu;

            var events = new Table(s);
            events["on"] = Fn(s, a => { LuaEngine.On(mod, a[0].String, a[1]); return null; });
            events["emit"] = Fn(s, a => LuaEngine.Emit(a[0].String, a.GetArray(1).Select(x => x.ToObject()).ToArray()));
            g["events"] = events;

            var input = new Table(s);
            input["key"] = Fn(s, a => InputAllowed && Keyboard.current != null && Keyboard.current[(Key)Enum.Parse(typeof(Key), a[0].String, true)].isPressed);
            input["key_down"] = Fn(s, a => InputAllowed && Keyboard.current != null && Keyboard.current[(Key)Enum.Parse(typeof(Key), a[0].String, true)].wasPressedThisFrame);
            input["mouse"] = Fn(s, a => InputAllowed && Mouse.current != null && MouseButton(Arg(a, 0, 0)).isPressed);
            input["mouse_down"] = Fn(s, a => InputAllowed && Mouse.current != null && MouseButton(Arg(a, 0, 0)).wasPressedThisFrame);
            input["allowed"] = Fn(s, a => InputAllowed);
            g["input"] = input;

            var game = new Table(s);
            game["player"] = (Func<ActiveRagdoll>)ModCommon.LocalRagdoll;
            game["ragdolls"] = Fn(s, a => List(s, ModCommon.AllRagdolls()));
            game["is_local"] = (Func<ActiveRagdoll, bool>)ModCommon.IsLocal;
            game["scale"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(ModCommon.BodyScale(a[0].IsNil() ? null : a[0].UserData.Object as ActiveRagdoll)));
            game["unground"] = Fn(s, a => { ModCommon.Unground(a[0].ToObject<ActiveRagdoll>(), Arg(a, 1, false)); return null; });
            game["grounded"] = Fn(s, a => ModCommon.Grounded(a[0].ToObject<ActiveRagdoll>()));
            game["counting_down"] = Fn(s, a => ModCommon.CountingDown);
            game["paused"] = Fn(s, a => ModCommon.Paused);
            game["menu_open"] = Fn(s, a => CorePlugin.MenuOpen);
            game["round_age"] = Fn(s, a => GameManager.Instance.roundTime - GameManager.Instance.countdownLength);
            game["next_round"] = Fn(s, a => { GameManager.Instance.LoadRandomMap(); return null; });
            game["sfx"] = Fn(s, a => ModCommon.GameSfxVolume);
            game["set_seated"] = Fn(s, a => { CheeseApi.SetSeated(a[0].ToObject<ActiveRagdoll>(), a[1].CastToBool()); return null; });
            game["seated"] = (Func<ActiveRagdoll, bool>)CheeseApi.IsSeated;
            game["set_driver"] = Fn(s, a => { CheeseApi.Driver = Arg<ActiveRagdoll>(a, 0); CheeseApi.DriverVehicle = Arg<Rigidbody>(a, 1); return null; });
            game["driver"] = Fn(s, a => CheeseApi.Driver);
            game["add_vehicle"] = Fn(s, a =>
            {
                var wheels = a.Count > 1 && a[1].Type == DataType.Table ? a[1].Table.Values.Select(v => v.ToObject<Transform>()).ToArray() : new Transform[0];
                CheeseApi.Vehicles[a[0].ToObject<Rigidbody>()] = wheels;
                return null;
            });
            game["vehicles"] = Fn(s, a =>
            {
                var t = new Table(s);
                foreach (var kv in CheeseApi.Vehicles.Where(kv => kv.Key).ToList())
                {
                    var v = new Table(s);
                    v["body"] = kv.Key;
                    v["wheels"] = List(s, kv.Value.Where(w => w));
                    t.Append(DynValue.NewTable(v));
                }
                return t;
            });
            game["is_vehicle"] = Fn(s, a => CheeseApi.IsVehicle(Arg<Rigidbody>(a, 0)));
            g["game"] = game;

            var body = new Table(s);
            body["align"] = DynValue.NewCallback((c, a) => { Body.Align(P(a[0]), P(a[1]), V(a[2]), (float)a[3].Number, (float)a[4].Number, a.Count > 5 && !a[5].IsNil() ? (float)a[5].Number : 0f); return DynValue.Nil; });
            body["reach"] = DynValue.NewCallback((c, a) => { Body.Reach(P(a[0]), P(a[1]), P(a[2]), V(a[3]), (float)a[4].Number, (float)a[5].Number, a.Count > 6 && !a[6].IsNil() ? (float)a[6].Number : 0f); return DynValue.Nil; });
            body["live"] = DynValue.NewCallback((c, a) => DynValue.NewBoolean(Body.Live(P(a[0]))));
            body["last_hit"] = (Func<ActiveRagdoll, Body.Hit>)Body.LastHit;
            body["touching"] = (Func<ActiveRagdoll, bool>)Body.Touching;
            body["mark_wound"] = (Action<ActiveRagdoll, Vector3>)Body.MarkWound;
            body["wound"] = (Func<ActiveRagdoll, float, object>)Body.Wound;
            body["facing"] = (Func<ActiveRagdoll, Vector3>)Body.Facing;
            body["name"] = (Func<ActiveRagdoll, RagdollPart, string>)Body.Name;
            body["parent"] = (Func<RagdollPart, RagdollPart>)Body.Parent;
            body["below"] = Fn(s, a => List(s, Body.Below(a[0].ToObject<RagdollPart>())));
            body["parts"] = Fn(s, a => List(s, a[0].ToObject<ActiveRagdoll>().GetRagdollParts().Where(p => p)));
            body["dead"] = (Func<ActiveRagdoll, bool>)Body.IsDead;
            body["set_dead"] = (Action<ActiveRagdoll, bool>)Body.SetDead;
            body["hands_busy"] = (Func<ActiveRagdoll, bool>)Body.HandsBusy;
            body["set_hands_busy"] = (Action<ActiveRagdoll, bool>)Body.SetHandsBusy;
            body["arm_strength"] = Fn(s, a => { Body.SetArmStrength(a[0].ToObject<ActiveRagdoll>(), a.Count > 1 && !a[1].IsNil() ? (float?)a[1].Number : null); return null; });
            body["heading"] = Fn(s, a => { Body.SetHeading(a[0].ToObject<ActiveRagdoll>(), a.Count > 1 && !a[1].IsNil() ? (Vector3?)a[1].ToObject<Vector3>() : null); return null; });
            body["arm_swing"] = (Action<ActiveRagdoll, bool>)Body.SetArmSwing;
            body["reach_items"] = (Action<ActiveRagdoll, bool>)Body.SetReach;
            body["gib"] = Fn(s, a => Gibs.Create(a[0].ToObject<ActiveRagdoll>(), a[1].ToObject<RagdollPart>(), a[2].Table.Values.Select(v => v.ToObject<RagdollPart>()).ToList()));
            body["grip"] = (Func<RagdollPart, Rigidbody, Vector3, ConfigurableJoint>)Body.Grip;
            body["seat"] = Fn(s, a =>
            {
                var seat = a[0].ToObject<GameObject>().AddComponent<KinematicSeat>();
                seat.Seat = a[1].ToObject<Transform>();
                seat.Pose = a[2].String == "reclined" ? SeatPose.Reclined : SeatPose.Upright;
                seat.HandTarget = Arg<Transform>(a, 3);
                return seat;
            });
            body["ignore"] = Fn(s, a => { SeatUtil.IgnoreCollisions(a[0].ToObject<ActiveRagdoll>(), a[1].Table.Values.Select(v => v.ToObject<Collider>()).ToList(), a[2].CastToBool()); return null; });
            g["body"] = body;

            var cam = new Table(s);
            cam["rig"] = Fn(s, a => StageManager.Instance ? StageManager.Instance.cameraRig : null);
            cam["main"] = Fn(s, a => StageManager.Instance ? StageManager.Instance.cameraRig.mainCamera : null);
            cam["target"] = Fn(s, a =>
            {
                var rig = StageManager.Instance.cameraRig;
                var t = Arg<Transform>(a, 0);
                var me = ModCommon.LocalRagdoll();
                rig.SetTarget(t ? t : me ? me.spine1.transform : null);
                return null;
            });
            cam["shake"] = Fn(s, a => { StageManager.Instance.cameraRig.SetScreenShake((float)a[0].Number, Arg(a, 1, 4f)); return null; });
            cam["first_person"] = Fn(s, a => CheeseApi.FirstPerson);
            cam["shift"] = Fn(s, a => { CameraFeature.Shift = Arg(a, 0, Vector3.zero); return null; });
            cam["fov"] = Fn(s, a => { CameraFeature.FovScale = Arg(a, 0, 1f); return null; });
            cam["hide_arms"] = Fn(s, a => { CameraFeature.HideArms = Arg(a, 0, false); return null; });
            cam["flying"] = Fn(s, a => CameraFeature.Flying);
            cam["to_screen"] = Fn(s, a =>
            {
                var c = StageManager.Instance.cameraRig.mainCamera;
                Vector3 vp = c.WorldToViewportPoint(a[0].ToObject<Vector3>());
                return vp.z > 0f ? (object)new Vector2(vp.x * Screen.width, (1f - vp.y) * Screen.height) : null;
            });
            g["camera"] = cam;

            var model = new Table(s);
            model["load"] = Fn(s, a =>
            {
                var path = ModPath(mod, a[0].String);
                if (models.TryGetValue(path, out var m) && m.Body) return m;
                var holder = new GameObject("Model " + Path.GetFileName(path));
                holder.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(holder);
                return models[path] = GlbLoader.Load(path, Arg(a, 1, 1f), 0f, holder.transform);
            });
            model["clone"] = Fn(s, a =>
            {
                var go = UnityEngine.Object.Instantiate(a[0].ToObject<GameObject>(), Arg<Transform>(a, 1), false);
                go.SetActive(true);
                return go;
            });
            g["model"] = model;

            var audio = new Table(s);
            audio["load"] = Fn(s, a =>
            {
                var path = ModPath(mod, a[0].String);
                return clips.TryGetValue(path, out var c) && c ? c : clips[path] = Wav.Load(path);
            });
            audio["folder"] = Fn(s, a =>
            {
                var t = new Table(s);
                foreach (var kv in Wav.LoadFolder(ModPath(mod, a[0].String))) t[kv.Key] = kv.Value;
                return t;
            });
            audio["source"] = Fn(s, a =>
            {
                var src = a[0].ToObject<GameObject>().AddComponent<AudioSource>();
                var o = a.Count > 1 && a[1].Type == DataType.Table ? a[1].Table : new Table(s);
                src.clip = o.Get("clip").ToObject<AudioClip>();
                src.loop = o.Get("loop").CastToBool();
                src.playOnAwake = false;
                src.spatialBlend = (float)(o.Get("spatial").CastToNumber() ?? 0.7);
                src.minDistance = (float)(o.Get("min").CastToNumber() ?? 25);
                src.maxDistance = (float)(o.Get("max").CastToNumber() ?? 600);
                src.rolloffMode = AudioRolloffMode.Linear;
                src.volume = (float)(o.Get("volume").CastToNumber() ?? 1);
                if (src.loop && src.clip) src.Play();
                return src;
            });
            audio["play"] = Fn(s, a =>
            {
                var src = a[0].ToObject<AudioSource>();
                var clip = Arg<AudioClip>(a, 1);
                if (src && clip) { src.pitch = Arg(a, 3, 1f); src.PlayOneShot(clip, Arg(a, 2, 1f) * ModCommon.GameSfxVolume * 2f); }
                return null;
            });
            audio["at"] = Fn(s, a =>
            {
                var clip = Arg<AudioClip>(a, 0);
                float v = Arg(a, 2, 1f) * ModCommon.GameSfxVolume * 2f;
                if (!clip || v < 0.01f) return null;
                var go = new GameObject("Sound");
                go.transform.position = a[1].ToObject<Vector3>();
                var src = go.AddComponent<AudioSource>();
                src.clip = clip; src.volume = v; src.pitch = Arg(a, 3, 1f);
                src.spatialBlend = 0.8f; src.minDistance = 20f; src.maxDistance = 400f; src.rolloffMode = AudioRolloffMode.Linear;
                src.Play();
                UnityEngine.Object.Destroy(go, clip.length / Mathf.Max(0.1f, src.pitch) + 0.1f);
                return null;
            });
            audio["sfx"] = Fn(s, a => ModCommon.GameSfxVolume * 2f);
            g["audio"] = audio;

            var mat = new Table(s);
            mat["solid"] = Fn(s, a => ModCommon.Solid(a[0].ToObject<Color>(), Arg(a, 1, 0.4f), Arg(a, 2, 0f)));
            mat["unlit"] = Fn(s, a =>
            {
                var m = new Material(ModCommon.UnlitMaterial(Arg<Texture>(a, 0)));
                if (a.Count > 1 && !a[1].IsNil()) m.color = a[1].ToObject<Color>();
                return m;
            });
            mat["tint"] = Fn(s, a => new Material(a[0].ToObject<Material>()) { color = a[1].ToObject<Color>() });
            mat["emissive"] = Fn(s, a =>
            {
                var c = a[0].ToObject<Color>();
                var m = new Material(ModCommon.Solid(c, 0.95f)) { color = c };
                if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * Arg(a, 1, 1f)); }
                return m;
            });
            mat["vignette"] = Fn(s, a => Theme.Vignette());
            mat["blob"] = Fn(s, a => ModCommon.BlobTexture(Arg(a, 0, 64), Arg(a, 1, 0f), Arg(a, 2, 1)));
            g["mat"] = mat;

            var fx = new Table(s);
            fx["particles"] = Fn(s, a => Particles.Create(a[0].ToObject<GameObject>(), a[1].Table));
            fx["emit"] = Fn(s, a => { a[0].ToObject<ParticleSystem>().Emit((int)a[1].Number); return null; });
            fx["emit_one"] = (Action<ParticleSystem, Vector3, Vector3, float, float, Color>)Particles.Emit;
            fx["rate"] = (Action<ParticleSystem, float>)Particles.Rate;
            fx["speed"] = (Action<ParticleSystem, float, float>)Particles.Speed;
            fx["tint"] = (Action<ParticleSystem, Color>)Particles.Tint;
            fx["decal"] = Fn(s, a => Decals.Quad(a[0].String, (int)a[1].Number, a[2].ToObject<Vector3>(), a[3].ToObject<Vector3>(), Arg(a, 4, Vector3.zero),
                (float)a[5].Number, (float)a[6].Number, a[7].ToObject<Material>(), Arg<Transform>(a, 8)));
            fx["decals"] = Fn(s, a => List(s, Decals.All(a[0].String).Where(x => x)));
            g["fx"] = fx;

            var physics = new Table(s);
            physics["ground"] = ModCommon.GroundMask;
            physics["raycast"] = Fn(s, a =>
            {
                var hits = Physics.RaycastAll(a[0].ToObject<Vector3>(), a[1].ToObject<Vector3>(), (float)a[2].Number, Arg(a, 3, ~0), QueryTriggerInteraction.Ignore);
                var skip = Arg<Rigidbody>(a, 4);
                bool noParts = Arg(a, 5, false);
                RaycastHit? best = null;
                foreach (var h in hits)
                {
                    if (skip && h.rigidbody == skip) continue;
                    if (noParts && h.collider.GetComponent<RagdollPart>()) continue;
                    if (best == null || h.distance < best.Value.distance) best = h;
                }
                return best.HasValue ? (object)best.Value : null;
            });
            physics["raycast_all"] = Fn(s, a => List(s, Physics.RaycastAll(a[0].ToObject<Vector3>(), a[1].ToObject<Vector3>(), (float)a[2].Number, Arg(a, 3, ~0), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).Cast<object>()));
            physics["overlap"] = Fn(s, a => List(s, Physics.OverlapSphere(a[0].ToObject<Vector3>(), (float)a[1].Number, Arg(a, 2, ~0), QueryTriggerInteraction.Ignore)));
            physics["ignore"] = Fn(s, a => { Physics.IgnoreCollision(a[0].ToObject<Collider>(), a[1].ToObject<Collider>(), Arg(a, 2, true)); return null; });
            physics["on_hit"] = Fn(s, a => { Relay(mod, a[0]).OnHit = a[1]; return null; });
            physics["on_touch"] = Fn(s, a => { Relay(mod, a[0]).OnStay = a[1]; return null; });
            physics["part"] = Fn(s, a => a[0].ToObject<Component>() is Component c && c ? c.GetComponent<RagdollPart>() : null);
            g["physics"] = physics;

            var ui = new Table(s);
            ui["width"] = Fn(s, a => Screen.width);
            ui["height"] = Fn(s, a => Screen.height);
            ui["text"] = Fn(s, a =>
            {
                var o = a.Count > 5 && a[5].Type == DataType.Table ? a[5].Table : null;
                Theme.Text(a[0].ToPrintString(), new Rect((float)a[1].Number, (float)a[2].Number, (float)a[3].Number, (float)a[4].Number),
                    (int)(o?.Get("size").CastToNumber() ?? 14), o != null && !o.Get("color").IsNil() ? o.Get("color").ToObject<Color>() : Theme.TextColor,
                    o?.Get("align").CastToString() ?? "left", o != null && o.Get("bold").CastToBool(), o != null && o.Get("mono").CastToBool());
                return null;
            });
            ui["rect"] = Fn(s, a => { Theme.Rect(new Rect((float)a[0].Number, (float)a[1].Number, (float)a[2].Number, (float)a[3].Number), a[4].ToObject<Color>(), Arg(a, 5, 0f)); return null; });
            ui["texture"] = Fn(s, a => { Theme.Texture(a[0].ToObject<Texture>(), new Rect((float)a[1].Number, (float)a[2].Number, (float)a[3].Number, (float)a[4].Number), Arg(a, 5, Color.white), Arg(a, 6, 0f)); return null; });
            ui["hud"] = Fn(s, a => { Theme.Hud(a[0].ToPrintString(), Arg<string>(a, 1)); return null; });
            ui["bar"] = Fn(s, a => { Theme.Bar(new Rect((float)a[0].Number, (float)a[1].Number, (float)a[2].Number, (float)a[3].Number), (float)a[4].Number, Arg(a, 5, Theme.Accent)); return null; });
            g["ui"] = ui;
        }

        static GameObject Target(DynValue d) => d.ToObject() is Component c ? c.gameObject : d.ToObject<GameObject>();

        static CollisionRelay Relay(LuaMod mod, DynValue target)
        {
            var go = target.ToObject() is Component c ? c.gameObject : target.ToObject<GameObject>();
            var relay = go.GetComponent<CollisionRelay>() ?? go.AddComponent<CollisionRelay>();
            relay.Mod = mod;
            return relay;
        }

        static UnityEngine.InputSystem.Controls.ButtonControl MouseButton(int i) =>
            i == 1 ? Mouse.current.rightButton : i == 2 ? Mouse.current.middleButton : Mouse.current.leftButton;
    }
}
