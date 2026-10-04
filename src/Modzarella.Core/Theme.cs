using System.Collections.Generic;
using UnityEngine;

namespace Modz
{
    public static class Theme
    {
        public static readonly Color Bg = Hex(0x16161a), Card = Hex(0x202026), Raised = Hex(0x2c2c34), Line = Hex(0x2e2e36),
            TextColor = Hex(0xececf0), Dim = Hex(0x9a9aa6), Accent = Hex(0xf5b830), Ok = Hex(0x5fd17a), Bad = Hex(0xf06a5a);

        static Font font;
        static readonly Dictionary<long, GUIStyle> textStyles = new Dictionary<long, GUIStyle>();
        static readonly Dictionary<long, Texture2D> rounded = new Dictionary<long, Texture2D>();

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        // the game renders in linear space; colors here are written in sRGB like the app and website
        public static Color C(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        public static Font Font
        {
            get
            {
                if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Segoe UI", "Arial" }, 14);
                return font;
            }
        }

        public static Texture2D Round(Color c, int radius)
        {
            long key = ((long)c.GetHashCode() << 8) ^ radius;
            if (rounded.TryGetValue(key, out var t) && t) return t;
            int n = radius * 2 + 2;
            t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float qx = Mathf.Max(radius - px, 0f, px - (n - radius));
                float qy = Mathf.Max(radius - py, 0f, py - (n - radius));
                float d = Mathf.Sqrt(qx * qx + qy * qy);
                var lc = C(c);
                t.SetPixel(x, y, new Color(lc.r, lc.g, lc.b, c.a * Mathf.Clamp01(radius + 0.5f - d)));
            }
            t.Apply();
            return rounded[key] = t;
        }

        public static GUIStyle Box(Color c, int radius = 8, int pad = 0)
        {
            var tex = Round(c, radius);
            return new GUIStyle
            {
                normal = { background = tex },
                border = new RectOffset(radius + 1, radius + 1, radius + 1, radius + 1),
                padding = new RectOffset(pad, pad, pad, pad),
            };
        }

        public static GUIStyle TextStyle(int size, Color color, string align = "left", bool bold = false, bool wrap = false)
        {
            long key = ((long)size << 40) ^ ((long)color.GetHashCode() << 8) ^ (align.GetHashCode() & 0xff) ^ (bold ? 1 << 30 : 0) ^ (wrap ? 1 << 29 : 0);
            if (textStyles.TryGetValue(key, out var st)) return st;
            st = new GUIStyle
            {
                font = Font, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, wordWrap = wrap, richText = true,
                normal = { textColor = C(color) },
                alignment = align == "center" ? TextAnchor.MiddleCenter : align == "right" ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
            };
            return textStyles[key] = st;
        }

        public static void Text(string text, Rect r, int size, Color color, string align = "left", bool bold = false) =>
            GUI.Label(r, text, TextStyle(size, color, align, bold));

        public static void Rect(Rect r, Color c, float angle = 0f)
        {
            var m = GUI.matrix;
            if (angle != 0f) GUIUtility.RotateAroundPivot(angle, r.center);
            var prev = GUI.color;
            GUI.color = C(c);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.matrix = m;
        }

        public static void Texture(Texture tex, Rect r, Color c, float angle = 0f)
        {
            if (!tex) return;
            var m = GUI.matrix;
            if (angle != 0f) GUIUtility.RotateAroundPivot(angle, r.center);
            var prev = GUI.color;
            GUI.color = C(c);
            GUI.DrawTexture(r, tex);
            GUI.color = prev;
            GUI.matrix = m;
        }

        static Texture2D vignette;

        public static Texture2D Vignette()
        {
            if (vignette) return vignette;
            const int n = 128;
            vignette = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x / (n - 1f) * 2f - 1f, dy = y / (n - 1f) * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                vignette.SetPixel(x, y, new Color(1, 1, 1, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 1f, d))));
            }
            vignette.Apply();
            return vignette;
        }

        public static void Bar(Rect r, float frac, Color fill)
        {
            GUI.Box(r, GUIContent.none, Box(new Color(0f, 0f, 0f, 0.55f), 4));
            var inner = new Rect(r.x + 2, r.y + 2, Mathf.Max(0f, (r.width - 4) * Mathf.Clamp01(frac)), r.height - 4);
            if (inner.width > 0f) GUI.Box(inner, GUIContent.none, Box(fill, 3));
        }

        public static void Hud(string title, string hint)
        {
            float w = 460f, h = string.IsNullOrEmpty(hint) ? 52f : 72f;
            var r = new Rect(Screen.width - w - 24, Screen.height - h - 24, w, h);
            GUI.Box(r, GUIContent.none, Box(new Color(Bg.r, Bg.g, Bg.b, 0.88f), 10));
            Text(title, new Rect(r.x + 18, r.y + 10, w - 36, 30), 20, TextColor, "left", true);
            if (!string.IsNullOrEmpty(hint)) Text(hint, new Rect(r.x + 18, r.y + 40, w - 36, 22), 12, Dim);
        }

        public static void Toast(string msg)
        {
            var st = TextStyle(15, TextColor, "center", true);
            float w = Mathf.Min(Screen.width - 40, st.CalcSize(new GUIContent(msg)).x + 48);
            var r = new Rect((Screen.width - w) / 2f, 28, w, 40);
            GUI.Box(r, GUIContent.none, Box(new Color(Bg.r, Bg.g, Bg.b, 0.92f), 20));
            GUI.Box(new Rect(r.x + 16, r.y + 17, 6, 6), GUIContent.none, Box(Accent, 3));
            GUI.Label(new Rect(r.x + 14, r.y, w - 20, r.height), msg, st);
        }
    }
}
