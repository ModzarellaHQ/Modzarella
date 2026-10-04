using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Modz
{
    public static class GlbLoader
    {
        public class CarModel
        {
            public GameObject Body;              // inactive template
            public GameObject[] Wheels;          // FR, FL, RR, RL pivoted at their centres, or null
            public Vector3[] WheelCenters;
            public float WheelRadius;
            public Bounds BodyBounds;
            public int Triangles, DrawCalls;
        }

        private class Prim
        {
            public List<Vector3> V = new List<Vector3>();
            public List<Vector3> N = new List<Vector3>();
            public List<Vector2> UV = new List<Vector2>();
            public List<int> I = new List<int>();
        }

        private static readonly string[] WheelWords = { "wheel", "tire", "tyre", "rim", "rad", "reifen", "felge", "roda", "rueda" };

        public static CarModel Load(string path, float targetLength, float yawDegrees, Transform holder)
        {
            byte[] file = File.ReadAllBytes(path);
            if (BitConverter.ToUInt32(file, 0) != 0x46546C67) throw new Exception("not a .glb (binary glTF) file");
            int jsonLen = BitConverter.ToInt32(file, 12);
            var json = (Dictionary<string, object>)MiniJson.Parse(Encoding.UTF8.GetString(file, 20, jsonLen));
            int binStart = 20 + jsonLen + 8;
            byte[] bin = null;
            if (file.Length > binStart)
            {
                int binLen = BitConverter.ToInt32(file, 20 + jsonLen);
                bin = new byte[binLen];
                Buffer.BlockCopy(file, binStart, bin, 0, binLen);
            }
            if (json.TryGetValue("extensionsRequired", out var req) && ((List<object>)req).Count > 0)
                throw new Exception("model needs glTF extensions " + string.Join(",", ((List<object>)req).Select(x => x.ToString())) + " (re-export without compression)");

            var g = new Gltf(json, bin, Path.GetDirectoryName(path));

            var bodyPrims = new Dictionary<int, Prim>();
            var wheelPrims = new List<(Vector3 center, Dictionary<int, Prim> byMat)>();
            var scenes = g.List("scenes");
            int sceneIdx = json.ContainsKey("scene") ? g.Int(json["scene"]) : 0;
            var roots = scenes != null && scenes.Count > 0 ? ((Dictionary<string, object>)scenes[sceneIdx])["nodes"] as List<object> : null;
            var wheelGeo = new List<(Matrix4x4 m, int mesh)>();
            if (roots != null)
                foreach (var r in roots) Walk(g, g.Int(r), Matrix4x4.identity, false, bodyPrims, wheelGeo);

            var flip = Matrix4x4.Scale(new Vector3(-1, 1, 1));

            var wheelRaw = new Dictionary<int, Prim>();
            foreach (var (m, mesh) in wheelGeo) AddMesh(g, mesh, m, wheelRaw);

            var all = bodyPrims.Values.Concat(wheelRaw.Values).ToList();
            ApplyAll(all, v => flip.MultiplyPoint3x4(v), n => flip.MultiplyVector(n).normalized, flipWinding: true);
            var b = BoundsOf(all);
            Quaternion orient = Quaternion.identity;
            if (b.size.x > b.size.z) orient = Quaternion.Euler(0, 90, 0);
            orient = Quaternion.Euler(0, yawDegrees, 0) * orient;
            ApplyAll(all, v => orient * v, n => orient * n, false);
            b = BoundsOf(all);
            float scale = targetLength / Mathf.Max(0.001f, b.size.z);
            Vector3 offset = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            ApplyAll(all, v => (v + offset) * scale, n => n, false);
            b = BoundsOf(all);

            var model = new CarModel { BodyBounds = b };

            if (wheelRaw.Count > 0)
            {
                var quad = new Dictionary<int, Prim>[4];
                for (int q = 0; q < 4; q++) quad[q] = new Dictionary<int, Prim>();
                foreach (var kv in wheelRaw)
                {
                    var p = kv.Value;
                    for (int t = 0; t < p.I.Count; t += 3)
                    {
                        Vector3 c = (p.V[p.I[t]] + p.V[p.I[t + 1]] + p.V[p.I[t + 2]]) / 3f;
                        int q = (c.z >= 0 ? 0 : 2) + (c.x >= 0 ? 0 : 1);
                        if (!quad[q].TryGetValue(kv.Key, out var dst)) quad[q][kv.Key] = dst = new Prim();
                        CopyTri(p, t, dst);
                    }
                }
                if (quad.All(q => q.Count > 0))
                {
                    model.Wheels = new GameObject[4];
                    model.WheelCenters = new Vector3[4];
                    float rSum = 0f;
                    for (int q = 0; q < 4; q++)
                    {
                        var wb = BoundsOf(quad[q].Values.ToList());
                        model.WheelCenters[q] = wb.center;
                        rSum += Mathf.Max(wb.size.y, wb.size.z) * 0.5f;
                        var c = wb.center;
                        ApplyAll(quad[q].Values.ToList(), v => v - c, n => n, false);
                        model.Wheels[q] = Build("wheel_" + q, quad[q], g, holder, ref model.Triangles, ref model.DrawCalls);
                    }
                    model.WheelRadius = rSum / 4f;
                }
                else
                {
                    foreach (var kv in wheelRaw) Merge(bodyPrims, kv.Key, kv.Value);
                }
            }

            model.Body = Build("body", bodyPrims, g, holder, ref model.Triangles, ref model.DrawCalls);
            return model;
        }

        private static void Walk(Gltf g, int nodeIdx, Matrix4x4 parent, bool inWheel, Dictionary<int, Prim> body, List<(Matrix4x4, int)> wheels)
        {
            var node = (Dictionary<string, object>)g.List("nodes")[nodeIdx];
            var m = parent * LocalMatrix(g, node);
            string name = node.TryGetValue("name", out var nm) ? nm.ToString().ToLowerInvariant() : "";
            bool wheel = inWheel || WheelWords.Any(w => name.Contains(w)) && !name.Contains("steering") && !name.Contains("lenkrad") && !name.Contains("spare");
            if (node.TryGetValue("mesh", out var meshIdx))
            {
                int mi = g.Int(meshIdx);
                var mesh = (Dictionary<string, object>)g.List("meshes")[mi];
                string meshName = mesh.TryGetValue("name", out var mn) ? mn.ToString().ToLowerInvariant() : "";
                bool caliper = name.Contains("callip") || name.Contains("calip") || meshName.Contains("calip");
                bool w = !caliper && (wheel || WheelWords.Any(x => meshName.Contains(x)) && !meshName.Contains("steering"));
                if (w) wheels.Add((m, mi));
                else AddMesh(g, mi, m, body);
            }
            if (node.TryGetValue("children", out var ch))
                foreach (var c in (List<object>)ch) Walk(g, g.Int(c), m, wheel, body, wheels);
        }

        private static Matrix4x4 LocalMatrix(Gltf g, Dictionary<string, object> node)
        {
            if (node.TryGetValue("matrix", out var mo))
            {
                var a = ((List<object>)mo).Select(x => g.F(x)).ToArray();
                var m = new Matrix4x4();
                for (int i = 0; i < 16; i++) m[i % 4, i / 4] = a[i]; // column-major
                return m;
            }
            Vector3 t = node.TryGetValue("translation", out var to) ? g.V3(to) : Vector3.zero;
            Quaternion r = Quaternion.identity;
            if (node.TryGetValue("rotation", out var ro))
            {
                var a = ((List<object>)ro).Select(x => g.F(x)).ToArray();
                r = new Quaternion(a[0], a[1], a[2], a[3]);
            }
            Vector3 s = node.TryGetValue("scale", out var so) ? g.V3(so) : Vector3.one;
            return Matrix4x4.TRS(t, r, s);
        }

        private static void AddMesh(Gltf g, int meshIdx, Matrix4x4 m, Dictionary<int, Prim> byMat)
        {
            var mesh = (Dictionary<string, object>)g.List("meshes")[meshIdx];
            var nm = m.inverse.transpose;
            foreach (Dictionary<string, object> prim in (List<object>)mesh["primitives"])
            {
                int mode = prim.TryGetValue("mode", out var mo) ? g.Int(mo) : 4;
                if (mode != 4) continue; // triangles only
                var attrs = (Dictionary<string, object>)prim["attributes"];
                if (!attrs.ContainsKey("POSITION")) continue;
                var pos = g.ReadVec(g.Int(attrs["POSITION"]), 3);
                var nor = attrs.ContainsKey("NORMAL") ? g.ReadVec(g.Int(attrs["NORMAL"]), 3) : null;
                var uv = attrs.ContainsKey("TEXCOORD_0") ? g.ReadVec(g.Int(attrs["TEXCOORD_0"]), 2) : null;
                int[] idx = prim.ContainsKey("indices") ? g.ReadIndices(g.Int(prim["indices"])) : Enumerable.Range(0, pos.Length / 3).ToArray();
                int mat = prim.TryGetValue("material", out var ma) ? g.Int(ma) : -1;
                if (!byMat.TryGetValue(mat, out var dst)) byMat[mat] = dst = new Prim();
                int baseV = dst.V.Count;
                int count = pos.Length / 3;
                for (int i = 0; i < count; i++)
                {
                    dst.V.Add(m.MultiplyPoint3x4(new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2])));
                    dst.N.Add(nor != null ? nm.MultiplyVector(new Vector3(nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2])).normalized : Vector3.zero);
                    dst.UV.Add(uv != null ? new Vector2(uv[i * 2], 1f - uv[i * 2 + 1]) : Vector2.zero);
                }
                bool mirrored = m.determinant < 0;
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    if (mirrored) { dst.I.Add(baseV + idx[i]); dst.I.Add(baseV + idx[i + 2]); dst.I.Add(baseV + idx[i + 1]); }
                    else { dst.I.Add(baseV + idx[i]); dst.I.Add(baseV + idx[i + 1]); dst.I.Add(baseV + idx[i + 2]); }
                }
            }
        }

        private static void Merge(Dictionary<int, Prim> into, int mat, Prim p)
        {
            if (!into.TryGetValue(mat, out var dst)) into[mat] = dst = new Prim();
            int b = dst.V.Count;
            dst.V.AddRange(p.V); dst.N.AddRange(p.N); dst.UV.AddRange(p.UV);
            foreach (var i in p.I) dst.I.Add(b + i);
        }

        private static void CopyTri(Prim src, int t, Prim dst)
        {
            for (int k = 0; k < 3; k++)
            {
                int i = src.I[t + k];
                dst.I.Add(dst.V.Count);
                dst.V.Add(src.V[i]); dst.N.Add(src.N[i]); dst.UV.Add(src.UV[i]);
            }
        }

        private static void ApplyAll(List<Prim> prims, Func<Vector3, Vector3> pos, Func<Vector3, Vector3> nor, bool flipWinding)
        {
            foreach (var p in prims)
            {
                for (int i = 0; i < p.V.Count; i++) { p.V[i] = pos(p.V[i]); p.N[i] = nor(p.N[i]); }
                if (flipWinding)
                    for (int i = 0; i < p.I.Count; i += 3) { int t = p.I[i + 1]; p.I[i + 1] = p.I[i + 2]; p.I[i + 2] = t; }
            }
        }

        private static Bounds BoundsOf(List<Prim> prims)
        {
            bool first = true;
            var b = new Bounds();
            foreach (var p in prims)
            foreach (var i in p.I)
            {
                if (first) { b = new Bounds(p.V[i], Vector3.zero); first = false; }
                else b.Encapsulate(p.V[i]);
            }
            return b;
        }

        private static GameObject Build(string name, Dictionary<int, Prim> byMat, Gltf g, Transform holder, ref int tris, ref int draws)
        {
            var go = new GameObject(name);
            go.transform.SetParent(holder, false);
            var mesh = new Mesh { name = name };
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>();
            var subs = new List<List<int>>(); var mats = new List<Material>();
            bool needNormals = false;
            foreach (var kv in byMat.OrderBy(k => g.IsBlend(k.Key) ? 1 : 0)) // opaque first
            {
                var p = kv.Value;
                if (p.I.Count == 0) continue;
                int b = v.Count;
                v.AddRange(p.V); n.AddRange(p.N); uv.AddRange(p.UV);
                if (p.N.Any(x => x == Vector3.zero)) needNormals = true;
                subs.Add(p.I.Select(i => b + i).ToList());
                mats.Add(g.Material(kv.Key));
                tris += p.I.Count / 3;
            }
            if (v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetUVs(0, uv);
            mesh.subMeshCount = subs.Count;
            for (int i = 0; i < subs.Count; i++) mesh.SetTriangles(subs[i], i, false);
            if (needNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats.ToArray();
            draws += mats.Count;
            return go;
        }

        private class Gltf
        {
            private readonly Dictionary<string, object> j;
            private readonly byte[] bin;
            private readonly string dir;
            private readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();
            private readonly Dictionary<int, Texture2D> texs = new Dictionary<int, Texture2D>();

            public Gltf(Dictionary<string, object> json, byte[] bin, string dir) { j = json; this.bin = bin; this.dir = dir; }

            public List<object> List(string key) => j.TryGetValue(key, out var o) ? (List<object>)o : null;
            public int Int(object o) => Convert.ToInt32(o, CultureInfo.InvariantCulture);
            public float F(object o) => Convert.ToSingle(o, CultureInfo.InvariantCulture);
            public Vector3 V3(object o) { var l = (List<object>)o; return new Vector3(F(l[0]), F(l[1]), F(l[2])); }

            private byte[] BufferView(int idx, out int offset, out int length, out int stride)
            {
                var bv = (Dictionary<string, object>)List("bufferViews")[idx];
                offset = bv.TryGetValue("byteOffset", out var o) ? Int(o) : 0;
                length = Int(bv["byteLength"]);
                stride = bv.TryGetValue("byteStride", out var s) ? Int(s) : 0;
                int buf = Int(bv["buffer"]);
                if (buf == 0 && bin != null) return bin;
                var b = (Dictionary<string, object>)List("buffers")[buf];
                return ReadUri(b["uri"].ToString());
            }

            private byte[] ReadUri(string uri)
            {
                if (uri.StartsWith("data:")) return Convert.FromBase64String(uri.Substring(uri.IndexOf(',') + 1));
                return File.ReadAllBytes(Path.Combine(dir, Uri.UnescapeDataString(uri)));
            }

            public float[] ReadVec(int accessor, int comps)
            {
                var a = (Dictionary<string, object>)List("accessors")[accessor];
                int count = Int(a["count"]);
                int ctype = Int(a["componentType"]);
                bool norm = a.TryGetValue("normalized", out var no) && (bool)no;
                var outp = new float[count * comps];
                if (!a.ContainsKey("bufferView")) return outp;
                var data = BufferView(Int(a["bufferView"]), out int off, out _, out int stride);
                off += a.TryGetValue("byteOffset", out var ao) ? Int(ao) : 0;
                int csize = ctype == 5126 ? 4 : ctype == 5123 || ctype == 5122 ? 2 : 1;
                if (stride == 0) stride = csize * comps;
                for (int i = 0; i < count; i++)
                for (int c = 0; c < comps; c++)
                {
                    int p = off + i * stride + c * csize;
                    float v;
                    switch (ctype)
                    {
                        case 5126: v = BitConverter.ToSingle(data, p); break;
                        case 5123: v = BitConverter.ToUInt16(data, p); if (norm) v /= 65535f; break;
                        case 5122: v = BitConverter.ToInt16(data, p); if (norm) v = Mathf.Max(v / 32767f, -1f); break;
                        case 5121: v = data[p]; if (norm) v /= 255f; break;
                        default: v = (sbyte)data[p]; if (norm) v = Mathf.Max(v / 127f, -1f); break;
                    }
                    outp[i * comps + c] = v;
                }
                return outp;
            }

            public int[] ReadIndices(int accessor)
            {
                var a = (Dictionary<string, object>)List("accessors")[accessor];
                int count = Int(a["count"]);
                int ctype = Int(a["componentType"]);
                var data = BufferView(Int(a["bufferView"]), out int off, out _, out _);
                off += a.TryGetValue("byteOffset", out var ao) ? Int(ao) : 0;
                var r = new int[count];
                for (int i = 0; i < count; i++)
                    r[i] = ctype == 5125 ? (int)BitConverter.ToUInt32(data, off + i * 4) : ctype == 5123 ? BitConverter.ToUInt16(data, off + i * 2) : data[off + i];
                return r;
            }

            public bool IsBlend(int mat)
            {
                if (mat < 0) return false;
                var m = (Dictionary<string, object>)List("materials")[mat];
                return m.TryGetValue("alphaMode", out var am) && am.ToString() == "BLEND" || IsGlass(mat);
            }

            public bool IsGlass(int mat)
            {
                if (mat < 0) return false;
                var m = (Dictionary<string, object>)List("materials")[mat];
                if (m.TryGetValue("extensions", out var ex) && ((Dictionary<string, object>)ex).ContainsKey("KHR_materials_transmission")) return true;
                string n = m.TryGetValue("name", out var nm) ? nm.ToString().ToLowerInvariant() : "";
                return n.Contains("glass") || n.Contains("window");
            }

            public Material Material(int idx)
            {
                if (mats.TryGetValue(idx, out var cached)) return cached;
                var mat = new Material(IsGlass(idx) || IsBlend(idx) ? Shaders.Transparent : Shaders.Lit);
                if (idx >= 0)
                {
                    var m = (Dictionary<string, object>)List("materials")[idx];
                    mat.name = m.TryGetValue("name", out var n) ? n.ToString() : "mat" + idx;
                    float metal = 1f, rough = 1f;
                    if (m.TryGetValue("pbrMetallicRoughness", out var pbrO))
                    {
                        var pbr = (Dictionary<string, object>)pbrO;
                        if (pbr.TryGetValue("baseColorFactor", out var bc))
                        {
                            var l = ((List<object>)bc).Select(F).ToArray();
                            var lin = new Color(l[0], l[1], l[2], l[3]);
                            mat.color = QualitySettings.activeColorSpace == ColorSpace.Linear ? lin : lin.gamma;
                        }
                        if (pbr.TryGetValue("baseColorTexture", out var bt))
                        {
                            var btd = (Dictionary<string, object>)bt;
                            var tex = Texture(Int(btd["index"]));
                            if (tex) mat.mainTexture = tex;
                            if (btd.TryGetValue("extensions", out var tex2) && ((Dictionary<string, object>)tex2).TryGetValue("KHR_texture_transform", out var tt))
                            {
                                var t = (Dictionary<string, object>)tt;
                                if (t.TryGetValue("scale", out var sc)) { var l = (List<object>)sc; mat.mainTextureScale = new Vector2(F(l[0]), F(l[1])); }
                                if (t.TryGetValue("offset", out var of)) { var l = (List<object>)of; mat.mainTextureOffset = new Vector2(F(l[0]), -F(l[1])); }
                            }
                        }
                        metal = pbr.TryGetValue("metallicFactor", out var mf) ? F(mf) : 1f;
                        rough = pbr.TryGetValue("roughnessFactor", out var rf) ? F(rf) : 1f;
                        if (pbr.ContainsKey("metallicRoughnessTexture")) { metal = Mathf.Min(metal, 0.3f); rough = Mathf.Max(rough, 0.5f); }
                    }
                    if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", Mathf.Clamp01(metal));
                    if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", Mathf.Clamp01(1f - rough));
                    if (mat.HasProperty("_Shininess")) mat.SetFloat("_Shininess", Mathf.Lerp(0.03f, 0.6f, 1f - rough));
                    if (mat.HasProperty("_SpecColor")) mat.SetColor("_SpecColor", Color.Lerp(new Color(0.1f, 0.1f, 0.1f), Color.white, (1f - rough) * 0.6f));
                    if (m.TryGetValue("emissiveFactor", out var ef))
                    {
                        var l = ((List<object>)ef).Select(F).ToArray();
                        var e = new Color(l[0], l[1], l[2]);
                        if (e.maxColorComponent > 0.01f) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", e); }
                    }
                    if (IsGlass(idx))
                    {
                        var col = mat.color;
                        mat.color = new Color(col.r * 0.4f, col.g * 0.4f, col.b * 0.45f, 0.35f);
                        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.95f);
                        MakeTransparent(mat);
                    }
                    else if (IsBlend(idx)) MakeTransparent(mat);
                    if (m.TryGetValue("doubleSided", out var ds) && (bool)ds && mat.HasProperty("_Cull")) mat.SetInt("_Cull", 0);
                }
                mat.enableInstancing = true;
                mats[idx] = mat;
                return mat;
            }

            private static void MakeTransparent(Material m)
            {
                if (m.shader.name != "Standard") { m.renderQueue = 3000; return; }
                m.SetFloat("_Mode", 3f);
                m.SetInt("_SrcBlend", (int)BlendMode.One);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.DisableKeyword("_ALPHATEST_ON");
                m.DisableKeyword("_ALPHABLEND_ON");
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                m.renderQueue = 3000;
            }

            private Texture2D Texture(int idx)
            {
                if (texs.TryGetValue(idx, out var t)) return t;
                try
                {
                    var tex = (Dictionary<string, object>)List("textures")[idx];
                    if (!tex.ContainsKey("source")) return null;
                    var img = (Dictionary<string, object>)List("images")[Int(tex["source"])];
                    byte[] bytes;
                    if (img.ContainsKey("bufferView"))
                    {
                        var data = BufferView(Int(img["bufferView"]), out int off, out int len, out _);
                        bytes = new byte[len];
                        Buffer.BlockCopy(data, off, bytes, 0, len);
                    }
                    else bytes = ReadUri(img["uri"].ToString());
                    t = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                    if (!t.LoadImage(bytes, false)) return null;
                    if (t.width > 1024 || t.height > 1024) t = Downscale(t, 1024);
                    if (t.width % 4 == 0 && t.height % 4 == 0) t.Compress(true);
                    t.Apply(false, true);
                    t.wrapMode = TextureWrapMode.Repeat;
                    t.anisoLevel = 4;
                }
                catch (Exception e) { Debug.LogWarning("[GlbLoader] texture " + idx + ": " + e.Message); t = null; }
                texs[idx] = t;
                return t;
            }

            private static Texture2D Downscale(Texture2D src, int max)
            {
                float k = (float)max / Mathf.Max(src.width, src.height);
                int w = Mathf.Max(4, Mathf.RoundToInt(src.width * k)), h = Mathf.Max(4, Mathf.RoundToInt(src.height * k));
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(src, rt);
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var dst = new Texture2D(w, h, TextureFormat.RGBA32, true);
                dst.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                dst.Apply(true);
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(src);
                return dst;
            }
        }
    }

    public static class Shaders
    {
        private static Shader lit, transparent;
        private static bool logged;

        public static Shader Lit => lit ? lit : (lit = Pick(false));
        public static Shader Transparent => transparent ? transparent : (transparent = Pick(true));

        private static Shader Pick(bool trans)
        {
            if (!logged)
            {
                logged = true;
                var names = Resources.FindObjectsOfTypeAll<Shader>().Select(x => x.name).Where(n => !n.StartsWith("Hidden/")).Distinct().OrderBy(n => n);
                Debug.Log("[GlbLoader] shaders in build: " + string.Join(", ", names));
            }
            string[] want = trans
                ? new[] { "Standard", "Legacy Shaders/Transparent/Specular", "Legacy Shaders/Transparent/Diffuse", "Mobile/Particles/Alpha Blended", "Sprites/Default" }
                : new[] { "Standard", "Legacy Shaders/Specular", "Legacy Shaders/Diffuse", "Mobile/Diffuse", "Legacy Shaders/VertexLit", "Unlit/Texture" };
            foreach (var n in want) { var sh = Shader.Find(n); if (sh) return sh; }
            var any = Resources.FindObjectsOfTypeAll<Shader>().FirstOrDefault(x => x.name.Contains("Lit") || x.name.Contains("Diffuse") || x.name.Contains("Toon"));
            return any ? any : Shader.Find("Sprites/Default");
        }
    }

    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return Value(s, ref i); }

        private static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        private static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++;
                    d[k] = Value(s, ref i); Ws(s, ref i);
                    if (s[i++] == '}') return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i)); Ws(s, ref i);
                    if (s[i++] == ']') return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Substring(i).StartsWith("true")) { i += 4; return true; }
            if (s.Substring(i, Math.Min(5, s.Length - i)) == "false") { i += 5; return false; }
            if (s.Substring(i, Math.Min(4, s.Length - i)) == "null") { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        private static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'u') { sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e == 'r' ? '\r' : e == 'b' ? '\b' : e == 'f' ? '\f' : e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }
    }
}
