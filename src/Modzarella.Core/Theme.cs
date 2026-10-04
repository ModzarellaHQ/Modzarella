using System.Collections.Generic;
using UnityEngine;

namespace Modz
{
    // Workbench look, shared with the app and the website: walnut panels, wax red, brass, keycaps.
    public static class Theme
    {
        public static readonly Color Wood = Hex(0x201b16), Panel = Hex(0x2b241d), Inset = Hex(0x120e0b), Edge = Hex(0x0d0a08),
            TextColor = Hex(0xf0e6d2), Dim = Hex(0xa39581), Wax = Hex(0xc8202f), WaxLight = Hex(0xd8323f), WaxDark = Hex(0x8f1420),
            Brass = Hex(0xd9a441), BrassLight = Hex(0xe8c56e), Cap = Hex(0xefe7d8), CapLow = Hex(0xcbbfa9), CapEdge = Hex(0x7d6f5b),
            Bad = Hex(0xe0533f);
        public static readonly Color Accent = Brass;

        static Font sans, mono;
        static readonly Dictionary<string, GUIStyle> textStyles = new Dictionary<string, GUIStyle>();
        static readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        // the game renders in linear space; colours here are sRGB like the app and website
        public static Color C(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        public static Font Sans => sans ? sans : sans = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Segoe UI", "Arial" }, 14);
        public static Font Mono => mono ? mono : mono = Font.CreateDynamicFontFromOSFont(new[] { "Menlo", "Consolas", "DejaVu Sans Mono", "Courier New" }, 12);

        static float Cover(float px, float py, float w, float h, float r, float inset = 0f)
        {
            float qx = Mathf.Max(r + inset - px, 0f, px - (w - r - inset));
            float qy = Mathf.Max(r + inset - py, 0f, py - (h - r - inset));
            return Mathf.Clamp01(r + 0.5f - Mathf.Sqrt(qx * qx + qy * qy));
        }

        // A control texture meant to be drawn at its real height: only sliced horizontally, so the vertical gradient survives.
        public static Texture2D Shape(int height, int radius, Color top, Color bottom, Color border, Color highlight, int drop = 0, Color dropColor = default)
        {
            string key = $"{height}|{radius}|{top}|{bottom}|{border}|{highlight}|{drop}|{dropColor}";
            if (textures.TryGetValue(key, out var t) && t) return t;
            int w = radius * 2 + 2, h = height + drop;
            t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int row = 0; row < h; row++)
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = row + 0.5f;
                float main = row < height ? Cover(px, py, w, height, radius) : 0f;
                float shadow = drop > 0 ? Cover(px, py - drop, w, height, radius) : 0f;
                float inner = Cover(px, py, w, height, Mathf.Max(0.5f, radius - 1), 1f);
                Color fill = Color.Lerp(top, bottom, row / (float)Mathf.Max(1, height - 1));
                if (row == 1 && highlight.a > 0f) fill = Color.Lerp(fill, new Color(highlight.r, highlight.g, highlight.b, 1f), highlight.a);
                Color face = Color.Lerp(border, fill, inner);
                Color c = main >= 1f || shadow <= 0f ? face : Color.Lerp(dropColor, face, main);
                float a = Mathf.Max(main * face.a, shadow * dropColor.a);
                var lc = C(c);
                t.SetPixel(x, h - 1 - row, new Color(lc.r, lc.g, lc.b, a));
            }
            t.Apply();
            return textures[key] = t;
        }

        public static GUIStyle Style(Texture2D tex, int radius)
        {
            return new GUIStyle
            {
                normal = { background = tex },
                border = new RectOffset(radius + 1, radius + 1, tex.height / 2, tex.height - tex.height / 2),
            };
        }

        static void Draw(Rect r, Texture2D tex, int radius) => GUI.Box(r, GUIContent.none, Style(tex, radius));

        public static GUIStyle Box(Color fill, int radius = 8, int pad = 0, Color? border = null)
        {
            var tex = Shape(radius * 2 + 4, radius, fill, fill, border ?? fill, Color.clear);
            return new GUIStyle
            {
                normal = { background = tex },
                border = new RectOffset(radius + 2, radius + 2, radius + 2, radius + 2),
                padding = new RectOffset(pad, pad, pad, pad),
            };
        }

        public static GUIStyle TextStyle(int size, Color color, string align = "left", bool bold = false, bool wrap = false, bool monospace = false)
        {
            string key = $"{size}|{color}|{align}|{bold}|{wrap}|{monospace}";
            if (textStyles.TryGetValue(key, out var st)) return st;
            st = new GUIStyle
            {
                font = monospace ? Mono : Sans, fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, wordWrap = wrap, richText = true,
                normal = { textColor = C(color) },
                alignment = align == "center" ? TextAnchor.MiddleCenter : align == "right" ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft,
            };
            return textStyles[key] = st;
        }

        public static void Text(string text, Rect r, int size, Color color, string align = "left", bool bold = false, bool monospace = false) =>
            GUI.Label(r, text, TextStyle(size, color, align, bold, false, monospace));

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

        // raised walnut panel with a lit top edge: the window, HUDs, toasts
        public static void PanelRect(Rect r, float alpha = 1f)
        {
            GUI.Box(r, GUIContent.none, Box(new Color(Wood.r, Wood.g, Wood.b, alpha), 10, 0, new Color(Edge.r, Edge.g, Edge.b, alpha)));
            Rect(new Rect(r.x + 10, r.y + 1, r.width - 20, 1), new Color(1f, 1f, 1f, 0.07f * alpha));
        }

        public static void Keycap(Rect r, string label, bool active = false)
        {
            int h = (int)r.height - 3;
            Draw(r, Shape(h, 5, active ? BrassLight : Cap, active ? Brass : CapLow, Edge, new Color(1, 1, 1, 0.6f), 3, CapEdge), 5);
            GUI.Label(new Rect(r.x, r.y, r.width, h), label, TextStyle(12, Hex(0x241d16), "center", true, false, true));
        }

        public static float KeycapWidth(string label) =>
            Mathf.Max(34f, TextStyle(12, Color.black, "center", true, false, true).CalcSize(new GUIContent(label)).x + 20f);

        public static void Switch(Rect r, bool on)
        {
            int h = (int)r.height, rad = h / 2;
            Draw(r, on ? Shape(h, rad, BrassLight, Brass, Edge, new Color(1, 1, 1, 0.3f)) : Shape(h, rad, Hex(0x0b0907), Hex(0x1a1511), Edge, Color.clear), rad);
            int k = h - 6;
            Draw(new Rect(on ? r.xMax - k - 3 : r.x + 3, r.y + 3, k, k), Shape(k, k / 2, Color.white, Hex(0xcfc6b8), Hex(0x3a3027), Color.clear), k / 2);
        }

        public static void Slider(Rect track, float t)
        {
            int h = (int)track.height, rad = h / 2;
            Draw(track, Shape(h, rad, Hex(0x0b0907), Inset, Edge, Color.clear), rad);
            float fill = Mathf.Max(track.height, track.width * Mathf.Clamp01(t));
            Draw(new Rect(track.x, track.y, fill, track.height), Shape(h, rad, BrassLight, Brass, Edge, Color.clear), rad);
            const int k = 16;
            Draw(new Rect(track.x + fill - k / 2f, track.center.y - k / 2f, k, k), Shape(k, k / 2, Cap, CapLow, Edge, new Color(1, 1, 1, 0.7f)), k / 2);
        }

        public static GUIStyle Button(bool primary, int height = 32, int size = 13)
        {
            var normal = primary ? Shape(height, 7, WaxLight, Wax, WaxDark, new Color(1, 1, 1, 0.35f), 2, Edge)
                                 : Shape(height, 7, Hex(0x382f26), Panel, Edge, new Color(1, 1, 1, 0.09f), 2, Edge);
            var hover = primary ? Shape(height, 7, Hex(0xe5404c), WaxLight, WaxDark, new Color(1, 1, 1, 0.4f), 2, Edge)
                                : Shape(height, 7, Hex(0x43392e), Hex(0x332a21), Edge, new Color(1, 1, 1, 0.12f), 2, Edge);
            var st = Style(normal, 7);
            st.hover.background = hover;
            st.active.background = normal;
            st.font = Sans;
            st.fontSize = size;
            st.fontStyle = FontStyle.Bold;
            st.alignment = TextAnchor.MiddleCenter;
            st.fixedHeight = height + 2;
            st.normal.textColor = st.hover.textColor = st.active.textColor = C(primary ? Color.white : TextColor);
            st.padding = new RectOffset(14, 14, 0, 2);
            st.margin = new RectOffset(0, 8, 0, 8);
            return st;
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
            int h = (int)r.height, rad = h / 2;
            Draw(r, Shape(h, rad, Hex(0x0b0907), Inset, Edge, Color.clear), rad);
            float w = (r.width - 2) * Mathf.Clamp01(frac);
            if (w >= h - 2) Draw(new Rect(r.x + 1, r.y + 1, w, h - 2), Shape(h - 2, (h - 2) / 2, Color.Lerp(fill, Color.white, 0.25f), fill, Color.clear, Color.clear), (h - 2) / 2);
        }

        public static void Hud(string title, string hint)
        {
            float need = Mathf.Max(TextStyle(20, TextColor, "left", true).CalcSize(new GUIContent(title)).x,
                string.IsNullOrEmpty(hint) ? 0f : TextStyle(11, Dim, "left", false, false, true).CalcSize(new GUIContent(hint)).x);
            float w = Mathf.Min(Screen.width - 48f, Mathf.Max(320f, need + 48f)), h = string.IsNullOrEmpty(hint) ? 54f : 78f;
            var r = new Rect(Screen.width - w - 24, Screen.height - h - 24, w, h);
            PanelRect(r, 0.95f);
            Rect(new Rect(r.x + 16, r.y + 17, 3, 20), Wax);
            Text(title, new Rect(r.x + 28, r.y + 12, w - 44, 30), 20, TextColor, "left", true);
            if (!string.IsNullOrEmpty(hint)) Text(hint, new Rect(r.x + 28, r.y + 46, w - 44, 20), 11, Dim, "left", false, true);
        }

        public static void Toast(string msg)
        {
            var st = TextStyle(14, TextColor, "left", true);
            float w = Mathf.Min(Screen.width - 40, st.CalcSize(new GUIContent(msg)).x + 54);
            var r = new Rect((Screen.width - w) / 2f, 26, w, 42);
            PanelRect(r, 0.97f);
            GUI.Box(new Rect(r.x + 17, r.y + 17, 8, 8), GUIContent.none, Box(Wax, 4));
            GUI.Label(new Rect(r.x + 35, r.y, w - 40, r.height), msg, st);
        }

        public static void Badge(Rect r, string text)
        {
            PanelRect(r, 0.93f);
            GUI.Label(r, text, TextStyle(11, Brass, "center", true, false, true));
        }
    }
}
