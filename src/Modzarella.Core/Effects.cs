using System.Linq;
using System.Collections.Generic;
using MoonSharp.Interpreter;
using UnityEngine;

namespace Modz
{
    public static class Gibs
    {
        public static GameObject Create(ActiveRagdoll r, RagdollPart root, List<RagdollPart> parts)
        {
            var go = new GameObject("Gib") { layer = root.gameObject.layer };
            if (!SkinnedCopy(r, root, go)) { Object.Destroy(go); return null; }

            float mass = 0f;
            foreach (var p in parts)
            {
                if (p.rigidBody) mass += p.rigidBody.mass;
                var src = p.GetComponent<Collider>();
                if (!src) continue;
                var child = new GameObject("col") { layer = go.layer };
                child.transform.SetParent(go.transform, false);
                child.transform.SetPositionAndRotation(p.transform.position, p.transform.rotation);
                var s = p.transform.lossyScale;
                switch (src)
                {
                    case CapsuleCollider c:
                        var cc = child.AddComponent<CapsuleCollider>();
                        cc.center = Vector3.Scale(c.center, s); cc.radius = c.radius * Mathf.Max(s.x, s.z); cc.height = c.height * s.y; cc.direction = c.direction;
                        break;
                    case SphereCollider sp:
                        var sc = child.AddComponent<SphereCollider>();
                        sc.center = Vector3.Scale(sp.center, s); sc.radius = sp.radius * s.x;
                        break;
                    case BoxCollider b:
                        var bc = child.AddComponent<BoxCollider>();
                        bc.center = Vector3.Scale(b.center, s); bc.size = Vector3.Scale(b.size, s);
                        break;
                    default:
                        child.AddComponent<BoxCollider>().size = src.bounds.size;
                        break;
                }
                child.GetComponent<Collider>().sharedMaterial = src.sharedMaterial;
            }

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = Mathf.Max(0.1f, mass);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.drag = 0.05f;
            rb.angularDrag = 0.3f;
            foreach (var gc in go.GetComponentsInChildren<Collider>())
            foreach (var p in r.GetRagdollParts())
            foreach (var pc in p.GetComponents<Collider>())
                Physics.IgnoreCollision(gc, pc);
            return go;
        }

        static bool SkinnedCopy(ActiveRagdoll r, RagdollPart root, GameObject go)
        {
            SkinnedMeshRenderer smr = null;
            foreach (var x in r.GetComponentsInChildren<SkinnedMeshRenderer>(false))
                if (x.enabled && x.gameObject.activeInHierarchy && x.sharedMesh) { smr = x; break; }
            if (!smr || smr.bones == null || smr.bones.Length == 0) return false;

            var bones = smr.bones;
            go.transform.SetPositionAndRotation(root.transform.position, root.transform.rotation);
            var copy = new Transform[bones.Length];
            bool any = false;
            for (int i = 0; i < bones.Length; i++)
            {
                var src = bones[i];
                var t = new GameObject("bone").transform;
                t.SetParent(go.transform, false);
                if (src && (src == root.transform || src.IsChildOf(root.transform)))
                {
                    t.SetPositionAndRotation(src.position, src.rotation);
                    t.localScale = src.lossyScale;
                    any = true;
                }
                else
                {
                    t.SetPositionAndRotation(root.transform.position, src ? src.rotation : Quaternion.identity);
                    t.localScale = Vector3.one * 0.0001f;
                }
                copy[i] = t;
            }
            if (!any) return false;

            var sk = new GameObject("limb").AddComponent<SkinnedMeshRenderer>();
            sk.transform.SetParent(go.transform, false);
            sk.sharedMesh = smr.sharedMesh;
            sk.sharedMaterials = smr.sharedMaterials;
            sk.bones = copy;
            int rootIdx = System.Array.IndexOf(bones, smr.rootBone);
            sk.rootBone = rootIdx >= 0 ? copy[rootIdx] : go.transform;
            sk.updateWhenOffscreen = true;
            sk.quality = SkinQuality.Bone4;
            return true;
        }
    }

    public static class Particles
    {
        static ParticleSystem.MinMaxCurve Curve(DynValue v, float fallback)
        {
            if (v.Type == DataType.Number) return new ParticleSystem.MinMaxCurve((float)v.Number);
            if (v.Type == DataType.Table) return new ParticleSystem.MinMaxCurve((float)v.Table.Get(1).Number, (float)v.Table.Get(2).Number);
            return new ParticleSystem.MinMaxCurve(fallback);
        }

        static Color Col(DynValue v) => v.ToObject<Color>();

        public static ParticleSystem Create(GameObject go, Table o)
        {
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = o.Get("loop").CastToBool();
            main.duration = o.Get("duration").IsNil() ? 1f : (float)o.Get("duration").Number;
            main.startLifetime = Curve(o.Get("lifetime"), 1f);
            main.startSpeed = Curve(o.Get("speed"), 1f);
            main.startSize = Curve(o.Get("size"), 1f);
            var color = o.Get("color");
            if (color.Type == DataType.Table) main.startColor = new ParticleSystem.MinMaxGradient(Col(color.Table.Get(1)), Col(color.Table.Get(2)));
            else if (!color.IsNil()) main.startColor = Col(color);
            main.gravityModifier = (float)(o.Get("gravity").CastToNumber() ?? 0);
            main.simulationSpace = o.Get("local").CastToBool() ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.maxParticles = (int)(o.Get("max").CastToNumber() ?? 500);

            var em = ps.emission;
            em.rateOverTime = (float)(o.Get("rate").CastToNumber() ?? 0);
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = (float)(o.Get("angle").CastToNumber() ?? 25);
            sh.radius = (float)(o.Get("radius").CastToNumber() ?? 0.01);

            if (o.Get("collide").CastToBool())
            {
                var col = ps.collision;
                col.enabled = true;
                col.type = ParticleSystemCollisionType.World;
                col.mode = ParticleSystemCollisionMode.Collision3D;
                col.collidesWith = ModCommon.GroundMask;
                col.bounce = 0.05f; col.dampen = 0.9f; col.lifetimeLoss = 0.6f;
                col.quality = ParticleSystemCollisionQuality.Low;
                col.maxCollisionShapes = 16;
            }
            var grow = o.Get("grow");
            if (grow.Type == DataType.Table)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, (float)grow.Table.Get(1).Number, 1f, (float)grow.Table.Get(2).Number));
            }
            if (o.Get("fade").CastToBool())
            {
                var c = ps.colorOverLifetime;
                c.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                c.color = g;
            }
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.sharedMaterial = o.Get("material").ToObject<Material>() ?? ModCommon.UnlitMaterial(ModCommon.BlobTexture(32));
            var stretch = o.Get("stretch");
            if (!stretch.IsNil())
            {
                rend.renderMode = ParticleSystemRenderMode.Stretch;
                rend.velocityScale = (float)stretch.Number;
                rend.lengthScale = (float)(o.Get("length").CastToNumber() ?? 1.5);
            }
            ps.Play();
            return ps;
        }

        public static void Emit(ParticleSystem ps, Vector3 position, Vector3 velocity, float size, float lifetime, Color color)
        {
            var ep = new ParticleSystem.EmitParams
            {
                position = position, velocity = velocity, startSize = size, startLifetime = lifetime, startColor = color,
                applyShapeToPosition = false,
            };
            ps.Emit(ep, 1);
        }

        public static void Rate(ParticleSystem ps, float rate) { var em = ps.emission; em.rateOverTime = rate; }
        public static void Speed(ParticleSystem ps, float min, float max) { var m = ps.main; m.startSpeed = new ParticleSystem.MinMaxCurve(min, max); }
        public static void Tint(ParticleSystem ps, Color c) { var m = ps.main; m.startColor = c; }
    }

    public static class Decals
    {
        static readonly Dictionary<string, Queue<GameObject>> pools = new Dictionary<string, Queue<GameObject>>();
        static Mesh quad;

        static Mesh QuadMesh
        {
            get
            {
                if (quad) return quad;
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
                return quad;
            }
        }

        public static GameObject Quad(string pool, int max, Vector3 point, Vector3 normal, Vector3 along, float w, float h, Material mat, Transform parent)
        {
            var q = new GameObject(pool);
            q.AddComponent<MeshFilter>().sharedMesh = QuadMesh;
            q.AddComponent<MeshRenderer>();
            if (!pools.TryGetValue(pool, out var queue)) pools[pool] = queue = new Queue<GameObject>();
            if (parent) q.transform.SetParent(parent, true);
            q.transform.position = point + normal * (0.02f + queue.Count * 0.00005f);
            Vector3 f = Vector3.ProjectOnPlane(along, normal);
            q.transform.rotation = f.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(-normal, f) : Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            Vector3 ls = parent ? parent.lossyScale : Vector3.one;
            q.transform.localScale = new Vector3(w / ls.x, h / ls.y, 1f);
            if (!parent) q.AddComponent<SurfaceDecal>();
            var rend = q.GetComponent<Renderer>();
            rend.sharedMaterial = mat;
            rend.receiveShadows = false;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            queue.Enqueue(q);
            while (queue.Count > max) { var old = queue.Dequeue(); if (old) Object.Destroy(old); }
            return q;
        }

        internal static void Prune()
        {
            foreach (var q in pools.Values)
            {
                var alive = q.Where(g => g).ToList();
                q.Clear();
                foreach (var g in alive) q.Enqueue(g);
            }
        }

        public static IEnumerable<GameObject> All(string pool) => pools.TryGetValue(pool, out var q) ? q : (IEnumerable<GameObject>)new GameObject[0];
    }

    // a world decal draped over the ground under it, refitted when it grows
    public class SurfaceDecal : MonoBehaviour
    {
        const int N = 4;
        Mesh mesh;
        Vector3 fitted;

        void Start() => Fit();

        void LateUpdate()
        {
            var sc = transform.localScale;
            if (Mathf.Abs(sc.x - fitted.x) > fitted.x * 0.1f || Mathf.Abs(sc.y - fitted.y) > fitted.y * 0.1f) Fit();
        }

        void OnDestroy() { if (mesh) Destroy(mesh); }

        void Fit()
        {
            var t = transform;
            fitted = t.localScale;
            float reach = Mathf.Max(fitted.x, fitted.y) * 0.5f + 0.2f;
            var verts = new Vector3[(N + 1) * (N + 1)];
            var uv = new Vector2[verts.Length];
            for (int y = 0, i = 0; y <= N; y++)
            for (int x = 0; x <= N; x++, i++)
            {
                var local = new Vector3(x / (float)N - 0.5f, y / (float)N - 0.5f, 0f);
                uv[i] = new Vector2(x / (float)N, y / (float)N);
                var world = t.TransformPoint(local);
                if (Physics.Raycast(world - t.forward * reach, t.forward, out var hit, reach * 2f, ModCommon.GroundMask, QueryTriggerInteraction.Ignore))
                    local.z = t.InverseTransformPoint(hit.point - t.forward * 0.02f).z;
                verts[i] = local;
            }
            var tris = new int[N * N * 6];
            for (int y = 0, k = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int a = y * (N + 1) + x, b = a + 1, c = a + N + 1, d = c + 1;
                tris[k++] = a; tris[k++] = c; tris[k++] = b;
                tris[k++] = b; tris[k++] = c; tris[k++] = d;
            }
            if (!mesh) { mesh = new Mesh { name = "SurfaceDecal" }; GetComponent<MeshFilter>().sharedMesh = mesh; }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
