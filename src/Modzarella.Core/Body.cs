using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Modz
{
    public static class Body
    {
        static readonly Dictionary<ActiveRagdoll, float> armStrength = new Dictionary<ActiveRagdoll, float>();
        static readonly Dictionary<ActiveRagdoll, Vector3> heading = new Dictionary<ActiveRagdoll, Vector3>();
        static readonly HashSet<ActiveRagdoll> noSwing = new HashSet<ActiveRagdoll>();
        static readonly HashSet<ActiveRagdoll> noReach = new HashSet<ActiveRagdoll>();
        static readonly HashSet<ActiveRagdoll> dead = new HashSet<ActiveRagdoll>();
        static readonly HashSet<ActiveRagdoll> handsBusy = new HashSet<ActiveRagdoll>();

        public class Hit { public RagdollPart part; public Vector3 dir; public float time; public Vector3 point; }

        static readonly Dictionary<ActiveRagdoll, Hit> hits = new Dictionary<ActiveRagdoll, Hit>();
        static readonly Dictionary<RagdollPart, Vector3> lastVel = new Dictionary<RagdollPart, Vector3>();
        static readonly Dictionary<ActiveRagdoll, Vector3> wounds = new Dictionary<ActiveRagdoll, Vector3>();
        static readonly Dictionary<ActiveRagdoll, float> woundTime = new Dictionary<ActiveRagdoll, float>();

        internal static void Sense()
        {
            ApplyHolds();
            foreach (var r in ModCommon.AllRagdolls())
                foreach (var p in r.GetRagdollParts())
                {
                    if (!Live(p)) continue;
                    Vector3 v = p.rigidBody.velocity;
                    if (lastVel.TryGetValue(p, out var lv) && p != r.spine1)
                    {
                        Vector3 dv = v - lv;
                        if (dv.sqrMagnitude > 45f * 45f && !(handsBusy.Contains(r) && IsArm(r, p)) && (!hits.TryGetValue(r, out var h) || Time.time - h.time > 0.3f))
                            hits[r] = new Hit { part = p, dir = dv.normalized, time = Time.time, point = p.transform.position };
                    }
                    lastVel[p] = v;
                }
        }

        static bool IsArm(ActiveRagdoll r, RagdollPart p) =>
            p == r.handLeft || p == r.handRight || p == r.lowerArmLeft || p == r.lowerArmRight || p == r.upperArmLeft || p == r.upperArmRight;

        public static Hit LastHit(ActiveRagdoll r) => r && hits.TryGetValue(r, out var h) && h.part ? h : null;

        public static bool Touching(ActiveRagdoll r)
        {
            foreach (var p in r.GetRagdollParts()) if (p && p.touchingGround) return true;
            return false;
        }

        public static void MarkWound(ActiveRagdoll r, Vector3 at) { wounds[r] = at; woundTime[r] = Time.time; }
        public static object Wound(ActiveRagdoll r, float maxAge) => r && woundTime.TryGetValue(r, out var t) && Time.time - t < maxAge ? (object)wounds[r] : null;

        public static bool Live(RagdollPart p) => p && p.rigidBody && !p.rigidBody.isKinematic && p.transform.localScale.x > 0.01f;

        struct Hold { public RagdollPart a, b, c; public Vector3 target; public float k, d, until; public bool reach; }
        static readonly Dictionary<long, Hold> holds = new Dictionary<long, Hold>();
        static readonly List<long> expired = new List<long>();

        static long Key(Object a, Object b) => ((long)a.GetInstanceID() << 32) ^ (uint)b.GetInstanceID();

        public static void Align(RagdollPart part, RagdollPart tip, Vector3 target, float k, float d, float hold)
        {
            if (hold > 0f && part && tip) holds[Key(part, tip)] = new Hold { a = part, b = tip, target = target, k = k, d = d, until = Time.time + hold };
            else Align(part, tip, target, k, d);
        }

        public static void Reach(RagdollPart hand, RagdollPart lower, RagdollPart upper, Vector3 target, float k, float d, float hold)
        {
            if (hold > 0f && upper && lower) holds[Key(upper, lower) ^ 1] = new Hold { a = hand, b = lower, c = upper, target = target, k = k, d = d, until = Time.time + hold, reach = true };
            else Reach(hand, lower, upper, target, k, d);
        }

        internal static void ApplyHolds()
        {
            expired.Clear();
            foreach (var kv in holds)
            {
                var h = kv.Value;
                if (Time.time > h.until || !h.b) { expired.Add(kv.Key); continue; }
                if (h.reach) Reach(h.a, h.b, h.c, h.target, h.k, h.d);
                else Align(h.a, h.b, h.target, h.k, h.d);
            }
            foreach (var k in expired) holds.Remove(k);
        }

        public static void Align(RagdollPart part, RagdollPart tip, Vector3 target, float k, float d)
        {
            if (!Live(part) || !tip || target.sqrMagnitude < 1e-6f) return;
            Vector3 cur = tip.transform.position - part.transform.position;
            if (cur.sqrMagnitude < 1e-6f) return;
            cur.Normalize();
            target.Normalize();
            Vector3 axis = Vector3.Cross(cur, target);
            float ang = Mathf.Atan2(axis.magnitude, Vector3.Dot(cur, target));
            if (axis.sqrMagnitude > 1e-8f) axis.Normalize();
            part.rigidBody.AddTorque(axis * (ang * k) - part.rigidBody.angularVelocity * d, ForceMode.Acceleration);
        }

        public static void Reach(RagdollPart hand, RagdollPart lower, RagdollPart upper, Vector3 target, float k, float d)
        {
            if (!Live(upper) || !Live(lower)) return;
            Align(upper, lower, ((target - upper.transform.position).normalized + Vector3.down * 0.15f).normalized, k, d);
            if (Live(hand)) Align(lower, hand, target - lower.transform.position, k * 0.8f, d);
        }

        public static Vector3 Facing(ActiveRagdoll r)
        {
            if (!r || !r.upperArmLeft || !r.upperArmRight) return Vector3.forward;
            Vector3 right = Vector3.ProjectOnPlane(r.upperArmRight.transform.position - r.upperArmLeft.transform.position, Vector3.up);
            return right.sqrMagnitude > 1e-4f ? Vector3.Cross(right, Vector3.up).normalized : Vector3.forward;
        }

        public static string Name(ActiveRagdoll r, RagdollPart p)
        {
            if (p == r.head) return "head";
            if (p == r.spine2) return "chest";
            if (p == r.spine1) return "pelvis";
            if (p == r.upperArmLeft) return "left upper arm";
            if (p == r.lowerArmLeft) return "left forearm";
            if (p == r.handLeft) return "left hand";
            if (p == r.upperArmRight) return "right upper arm";
            if (p == r.lowerArmRight) return "right forearm";
            if (p == r.handRight) return "right hand";
            if (p == r.upperLegLeft) return "left thigh";
            if (p == r.lowerLegLeft) return "left shin";
            if (p == r.footLeft) return "left foot";
            if (p == r.upperLegRight) return "right thigh";
            if (p == r.lowerLegRight) return "right shin";
            if (p == r.footRight) return "right foot";
            return "part";
        }

        public static RagdollPart Parent(RagdollPart p)
        {
            for (var t = p ? p.transform.parent : null; t; t = t.parent)
            {
                var rp = t.GetComponent<RagdollPart>();
                if (rp) return rp;
            }
            return null;
        }

        public static List<RagdollPart> Below(RagdollPart p)
        {
            var list = p.GetComponentsInChildren<RagdollPart>(true).ToList();
            if (!list.Contains(p)) list.Add(p);
            return list;
        }

        public static ConfigurableJoint Grip(RagdollPart hand, Rigidbody item, Vector3 localAnchor)
        {
            if (!Live(hand) || !item) return null;
            if (hand is RagdollHand rh) rh.BreakHold();
            var j = hand.gameObject.AddComponent<ConfigurableJoint>();
            j.connectedBody = item;
            j.autoConfigureConnectedAnchor = false;
            j.anchor = Vector3.zero;
            j.connectedAnchor = localAnchor;
            float m = hand.rigidBody.mass;
            var drive = new JointDrive { positionSpring = m * 3000f, positionDamper = m * 90f, maximumForce = m * 900f };
            j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Free;
            j.xDrive = j.yDrive = j.zDrive = drive;
            j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
            j.enableCollision = false;
            j.enablePreprocessing = false;
            return j;
        }

        public static void SetArmStrength(ActiveRagdoll r, float? mult) { if (mult.HasValue) armStrength[r] = mult.Value; else armStrength.Remove(r); }
        public static void SetHeading(ActiveRagdoll r, Vector3? dir) { if (dir.HasValue) heading[r] = dir.Value; else heading.Remove(r); }
        public static void SetArmSwing(ActiveRagdoll r, bool on) { if (on) noSwing.Remove(r); else noSwing.Add(r); }
        public static void SetReach(ActiveRagdoll r, bool on) { if (on) noReach.Remove(r); else noReach.Add(r); }
        public static void SetHandsBusy(ActiveRagdoll r, bool on) { if (on) handsBusy.Add(r); else handsBusy.Remove(r); }
        public static bool HandsBusy(ActiveRagdoll r) => r && handsBusy.Contains(r);
        public static bool IsDead(ActiveRagdoll r) => r && dead.Contains(r);

        public static void SetDead(ActiveRagdoll r, bool on)
        {
            if (!r) return;
            if (!on) { dead.Remove(r); return; }
            if (!dead.Add(r)) return;
            ModCommon.Unground(r, true);
            r.moveDirection = Vector3.zero;
            var limp = new JointDrive { positionSpring = 0f, positionDamper = 2f, maximumForce = float.PositiveInfinity };
            foreach (var p in r.GetRagdollParts())
            {
                if (p && p.joint) p.joint.slerpDrive = limp;
                if (p is RagdollHand h) h.BreakHold();
            }
        }

        internal static void Prune()
        {
            CheeseApi.Vehicles.Keys.Where(k => !k).ToList().ForEach(k => CheeseApi.Vehicles.Remove(k));
            hits.Keys.Where(k => !k).ToList().ForEach(k => hits.Remove(k));
            lastVel.Keys.Where(k => !k).ToList().ForEach(k => lastVel.Remove(k));
            wounds.Keys.Where(k => !k).ToList().ForEach(k => { wounds.Remove(k); woundTime.Remove(k); });
            armStrength.Keys.Where(k => !k).ToList().ForEach(k => armStrength.Remove(k));
            heading.Keys.Where(k => !k).ToList().ForEach(k => heading.Remove(k));
            noSwing.RemoveWhere(k => !k);
            noReach.RemoveWhere(k => !k);
            dead.RemoveWhere(k => !k);
            handsBusy.RemoveWhere(k => !k);
        }

        internal static float ArmStrength(ActiveRagdoll r) => armStrength.TryGetValue(r, out var m) ? m : 1f;
        internal static bool Heading(ActiveRagdoll r, out Vector3 dir) => heading.TryGetValue(r, out dir);
        internal static bool Swings(ActiveRagdoll r) => !noSwing.Contains(r);
        internal static bool Reaches(ActiveRagdoll r) => !noReach.Contains(r);
    }

    internal static class BodyPatches
    {
        static void Drive(float drive, float damper, params RagdollPart[] parts)
        {
            var jd = new JointDrive { positionSpring = drive, positionDamper = damper, maximumForce = float.PositiveInfinity };
            foreach (var p in parts) if (p && p.joint) p.joint.slerpDrive = jd;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), "SetBodyDrive")]
        static bool BodyDrive(ActiveRagdoll __instance, float drive, float damper)
        { Drive(drive, damper, __instance.spine1, __instance.spine2, __instance.head); return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), "SetFeetDrive")]
        static bool FeetDrive(ActiveRagdoll __instance, float drive, float damper)
        { Drive(drive, damper, __instance.footLeft, __instance.footRight); return false; }

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), "SetArmDrive")]
        static bool ArmDrive(ActiveRagdoll __instance, float drive, float damper)
        {
            float m = Body.ArmStrength(__instance);
            Drive(drive * m, damper * Mathf.Lerp(0.3f, 1f, m), __instance.lowerArmLeft, __instance.lowerArmRight, __instance.handLeft, __instance.handRight);
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), "SwingArms")]
        static bool Swing(ActiveRagdoll __instance) => Body.Swings(__instance);

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), "SetRootTargetRotation")]
        static void Face(ActiveRagdoll __instance, ref Vector3 heading)
        {
            if (CameraFeature.Flying && ModCommon.IsLocal(__instance)) heading = CameraFeature.flyHeading;
            else if (Body.Heading(__instance, out var h) && h.sqrMagnitude > 1e-4f) heading = h.normalized;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), nameof(ActiveRagdoll.Input))]
        static bool Input(ActiveRagdoll __instance, ref RagdollInput input)
        {
            if (CheeseApi.IsSeated(__instance) || Body.IsDead(__instance)) return false;
            if (CameraFeature.Flying && ModCommon.IsLocal(__instance)) input = new RagdollInput(false, false, false, Vector3.zero, input.InputNumber);
            if (!Body.Reaches(__instance) && (input.TargetCheese || input.TargetRagdolls))
                input = new RagdollInput(false, false, input.Jump, input.Movement, input.InputNumber);
            return true;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(ActiveRagdoll), "FixedUpdate")]
        static bool Balance(ActiveRagdoll __instance) => !CheeseApi.IsSeated(__instance) && !Body.IsDead(__instance);

        [HarmonyPostfix, HarmonyPatch(typeof(RagdollPart), "OnCollisionEnter")]
        static void PartHit(RagdollPart __instance, Collision collision) => LuaEngine.PartHit(__instance, collision);

        [HarmonyPostfix, HarmonyPatch(typeof(RagdollHand), "OnCollisionEnter")]
        static void HandHit(RagdollHand __instance, Collision collision) => LuaEngine.PartHit(__instance, collision);
    }
}
