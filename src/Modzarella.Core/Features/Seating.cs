using System.Collections.Generic;
using UnityEngine;

namespace Modz
{
    public enum SeatPose { Upright, Reclined }

    public static class SeatUtil
    {
        public static void PlaceOnSeat(ActiveRagdoll r, Transform seat, Vector3 vehicleVelocity)
        {
            Vector3 pelvis = r.spine1.transform.position;
            Vector3 spineUp = (r.spine2.transform.position - pelvis).normalized;
            Vector3 face = Vector3.Cross(r.upperLegRight.transform.position - r.upperLegLeft.transform.position, spineUp);
            if (face.sqrMagnitude < 1e-4f) face = seat.forward;
            Quaternion q = Quaternion.LookRotation(seat.forward, seat.up) * Quaternion.Inverse(Quaternion.LookRotation(face.normalized, spineUp));
            Vector3 delta = seat.position - pelvis;
            var targets = new List<(RagdollPart p, Vector3 pos, Quaternion rot)>();
            foreach (var p in r.GetRagdollParts())
            {
                if (!p || !p.rigidBody || p.rigidBody.isKinematic) continue;
                targets.Add((p, pelvis + q * (p.transform.position - pelvis) + delta, q * p.transform.rotation));
            }
            for (int pass = 0; pass < 2; pass++)
                foreach (var t in targets) t.p.transform.SetPositionAndRotation(t.pos, t.rot);
            foreach (var t in targets)
            {
                t.p.rigidBody.position = t.pos;
                t.p.rigidBody.rotation = t.rot;
                t.p.rigidBody.velocity = vehicleVelocity;
                t.p.rigidBody.angularVelocity = Vector3.zero;
            }
        }

        public static void IgnoreCollisions(ActiveRagdoll r, IEnumerable<Collider> cols, bool ignore)
        {
            foreach (var p in r.GetRagdollParts())
            {
                if (!p) continue;
                foreach (var pc in p.GetComponents<Collider>())
                foreach (var cc in cols)
                    if (pc && cc) Physics.IgnoreCollision(pc, cc, ignore);
            }
        }
    }
}

namespace Modz
{
    public class KinematicSeat : MonoBehaviour
    {
        public ActiveRagdoll Rider { get; private set; }
        public Transform Seat;
        public Transform HandTarget;
        public SeatPose Pose;
        private readonly List<Rigidbody> madeKinematic = new List<Rigidbody>();
        private readonly Dictionary<Rigidbody, RigidbodyInterpolation> interp = new Dictionary<Rigidbody, RigidbodyInterpolation>();
        private Quaternion pelvisInSeat;
        private readonly Dictionary<RagdollPart, (Vector3 pos, Quaternion rot)> bind = new Dictionary<RagdollPart, (Vector3, Quaternion)>();

        public void Sit(ActiveRagdoll r, Vector3 vehicleVel)
        {
            Stand(Vector3.zero);
            if (!r || !Seat) return;
            SeatUtil.PlaceOnSeat(r, Seat, vehicleVel);
            Rider = r;
            CheeseApi.SetSeated(r, true);
            madeKinematic.Clear();
            foreach (var p in r.GetRagdollParts())
            {
                if (!p || !p.rigidBody || p.rigidBody.isKinematic) continue;
                p.rigidBody.velocity = Vector3.zero;
                p.rigidBody.angularVelocity = Vector3.zero;
                p.rigidBody.isKinematic = true;
                interp[p.rigidBody] = p.rigidBody.interpolation;
                p.rigidBody.interpolation = RigidbodyInterpolation.None; // stale interpolation would undo the pose
                madeKinematic.Add(p.rigidBody);
            }
            foreach (var h in new[] { r.handLeft, r.handRight }) if (h) h.BreakHold();
            pelvisInSeat = Quaternion.Inverse(Seat.rotation) * r.spine1.transform.rotation;
            bind.Clear();
            foreach (var (parent, child) in Chain(r))
            {
                if (!parent || !child) continue;
                var pr = Quaternion.Inverse(parent.transform.rotation);
                bind[child] = (pr * (child.transform.position - parent.transform.position), pr * child.transform.rotation);
            }
            ApplyPose();
        }

        private static IEnumerable<(RagdollPart parent, RagdollPart child)> Chain(ActiveRagdoll r)
        {
            yield return (r.spine1, r.spine2);
            yield return (r.spine2, r.head);
            yield return (r.spine2, r.upperArmLeft);
            yield return (r.upperArmLeft, r.lowerArmLeft);
            yield return (r.lowerArmLeft, r.handLeft);
            yield return (r.spine2, r.upperArmRight);
            yield return (r.upperArmRight, r.lowerArmRight);
            yield return (r.lowerArmRight, r.handRight);
            yield return (r.spine1, r.upperLegLeft);
            yield return (r.upperLegLeft, r.lowerLegLeft);
            yield return (r.lowerLegLeft, r.footLeft);
            yield return (r.spine1, r.upperLegRight);
            yield return (r.upperLegRight, r.lowerLegRight);
            yield return (r.lowerLegRight, r.footRight);
        }

        public ActiveRagdoll Stand(Vector3 velocity)
        {
            var r = Rider;
            Rider = null;
            foreach (var rb in madeKinematic)
            {
                if (!rb) continue;
                rb.isKinematic = false;
                if (interp.TryGetValue(rb, out var mode)) rb.interpolation = mode;
                rb.velocity = velocity;
            }
            madeKinematic.Clear();
            interp.Clear();
            bind.Clear();
            if (r) CheeseApi.SetSeated(r, false);
            return r;
        }

        private void OnDestroy() { if (Rider) Stand(Vector3.zero); }

        private void LateUpdate() { if (Rider) ApplyPose(); }
        private void FixedUpdate() { if (Rider) ApplyPose(); }

        private void FK(RagdollPart parent, RagdollPart child)
        {
            if (!parent || !child || !bind.TryGetValue(child, out var b)) return;
            child.transform.SetPositionAndRotation(parent.transform.position + parent.transform.rotation * b.pos, parent.transform.rotation * b.rot);
            if (child.rigidBody) { child.rigidBody.position = child.transform.position; child.rigidBody.rotation = child.transform.rotation; }
        }

        private void Aim(RagdollPart part, RagdollPart child, Vector3 dir)
        {
            if (!part || !child || !bind.TryGetValue(child, out var b) || dir.sqrMagnitude < 1e-6f) { FK(part, child); return; }
            Vector3 cur = part.transform.rotation * b.pos;
            if (cur.sqrMagnitude > 1e-6f) part.transform.rotation = Quaternion.FromToRotation(cur, dir) * part.transform.rotation;
            FK(part, child);
        }

        private void ApplyPose()
        {
            var r = Rider;
            if (!r || !r.spine1 || !Seat) { Rider = null; return; }
            Vector3 F = Seat.forward, U = Seat.up, Rt = Seat.right;
            float sc = ModCommon.BodyScale(r);
            bool reclined = Pose == SeatPose.Reclined;

            r.spine1.transform.SetPositionAndRotation(Seat.position, Seat.rotation * pelvisInSeat);
            if (r.spine1.rigidBody) { r.spine1.rigidBody.position = Seat.position; r.spine1.rigidBody.rotation = r.spine1.transform.rotation; }
            FK(r.spine1, r.spine2);
            Aim(r.spine2, r.head, reclined ? (U * 0.95f - F * 0.3f) : (U * 0.9f + F * 0.42f));

            FK(r.spine1, r.upperLegLeft);
            FK(r.spine1, r.upperLegRight);
            Vector3 thigh = reclined ? (F * 0.97f - U * 0.1f) : (F * 0.98f + U * 0.02f);
            Vector3 shin = reclined ? (-U * 0.55f + F * 0.83f) : (-U * 0.96f + F * 0.25f);
            float spread = reclined ? 0.12f : 0.22f;
            Aim(r.upperLegLeft, r.lowerLegLeft, thigh - Rt * spread);
            Aim(r.upperLegRight, r.lowerLegRight, thigh + Rt * spread);
            Aim(r.lowerLegLeft, r.footLeft, shin);
            Aim(r.lowerLegRight, r.footRight, shin);

            FK(r.spine2, r.upperArmLeft);
            FK(r.spine2, r.upperArmRight);
            Vector3 handL, handR;
            if (HandTarget)
            {
                handL = HandTarget.position - Rt * sc * 0.14f;
                handR = HandTarget.position + Rt * sc * 0.14f;
            }
            else
            {
                Vector3 knee = ((r.lowerLegLeft ? r.lowerLegLeft.transform.position : Seat.position) + (r.lowerLegRight ? r.lowerLegRight.transform.position : Seat.position)) * 0.5f;
                handL = knee - Rt * sc * 0.12f + U * sc * 0.05f;
                handR = knee + Rt * sc * 0.12f + U * sc * 0.05f;
            }
            Reach(r.upperArmLeft, r.lowerArmLeft, r.handLeft, handL, -Rt, U);
            Reach(r.upperArmRight, r.lowerArmRight, r.handRight, handR, Rt, U);
        }

        private void Reach(RagdollPart upper, RagdollPart lower, RagdollPart hand, Vector3 target, Vector3 outward, Vector3 up)
        {
            if (!upper || !lower || !bind.TryGetValue(lower, out var bl)) return;
            float a = bl.pos.magnitude;
            float b = hand && bind.TryGetValue(hand, out var bh) ? bh.pos.magnitude : a;
            Vector3 sh = upper.transform.position;
            Vector3 toT = target - sh;
            float c = Mathf.Clamp(toT.magnitude, 0.01f, (a + b) * 0.999f);
            Vector3 dir = toT.normalized;
            float cosA = Mathf.Clamp((a * a + c * c - b * b) / (2f * a * c), -1f, 1f);
            Vector3 bendAxis = Vector3.Cross(dir, (outward * 0.6f - up * 0.8f).normalized);
            if (bendAxis.sqrMagnitude < 1e-4f) bendAxis = Vector3.Cross(dir, up);
            Vector3 elbowDir = Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, bendAxis.normalized) * dir;
            Aim(upper, lower, elbowDir);
            if (hand) Aim(lower, hand, target - lower.transform.position);
        }
    }
}
