using System;
using System.Collections.Generic;
using UnityEngine;

namespace Modz
{
    // Flat dark look shared with the app and the website.
    public static class Theme
    {
        public static readonly Color Bg = Hex(0x18181b), Row = Hex(0x222226), Line = Hex(0x303036), TextColor = Hex(0xececef),
            Dim = Hex(0x9a9aa3), Accent = Hex(0xf2b33d), Bad = Hex(0xef5350);

        static Font sans, mono;
        static readonly Dictionary<string, GUIStyle> textStyles = new Dictionary<string, GUIStyle>();
        static readonly Dictionary<Color, Texture2D> solids = new Dictionary<Color, Texture2D>();

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        // the game renders in linear space; colours here are sRGB like the app and website
        public static Color C(Color c) => QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        public static Font Sans => sans ? sans : sans = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Segoe UI", "Arial" }, 14);
        public static Font Mono => mono ? mono : mono = Font.CreateDynamicFontFromOSFont(new[] { "Menlo", "Consolas", "DejaVu Sans Mono", "Courier New" }, 12);

        public static Texture2D Solid(Color c)
        {
            if (solids.TryGetValue(c, out var t) && t) return t;
            t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, C(c));
            t.Apply();
            return solids[c] = t;
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

        public static void Rect(Rect r, Color c, float angle = 0f) => Texture(Texture2D.whiteTexture, r, c, angle);

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

        public static void Panel(Rect r, float alpha = 0.96f)
        {
            Rect(r, new Color(Line.r, Line.g, Line.b, alpha));
            Rect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(Bg.r, Bg.g, Bg.b, alpha));
        }

        static GUIStyle button, accentButton, field, toggle, slider, thumb;

        static GUIStyle Flat(Color normal, Color hover, Color text, int size = 12)
        {
            return new GUIStyle
            {
                normal = { background = Solid(normal), textColor = C(text) },
                hover = { background = Solid(hover), textColor = C(text) },
                active = { background = Solid(Line), textColor = C(text) },
                focused = { background = Solid(normal), textColor = C(text) },
                font = Sans, fontSize = size, alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(10, 10, 4, 4), margin = new RectOffset(0, 6, 2, 2), fixedHeight = 24,
            };
        }

        public static GUIStyle Button => button ?? (button = Flat(Hex(0x3c3c44), Hex(0x4c4c56), TextColor));
        public static GUIStyle AccentButton => accentButton ?? (accentButton = Flat(Accent, Hex(0xf7c766), Hex(0x18181b)));
        public static GUIStyle Field => field ?? (field = new GUIStyle(Flat(Hex(0x0f0f11), Hex(0x0f0f11), TextColor)) { font = Mono, alignment = TextAnchor.MiddleLeft, padding = new RectOffset(6, 6, 2, 2) });

        public static GUIStyle Toggle => toggle ?? (toggle = new GUIStyle
        {
            normal = { background = Solid(Hex(0x4a4a53)) }, hover = { background = Solid(Hex(0x5a5a64)) },
            onNormal = { background = Solid(Accent) }, onHover = { background = Solid(Hex(0xf7c766)) },
            fixedWidth = 16, fixedHeight = 16, margin = new RectOffset(0, 4, 4, 4), border = new RectOffset(0, 0, 0, 0),
        });

        public static GUIStyle Slider => slider ?? (slider = new GUIStyle
        {
            normal = { background = Solid(Line) }, fixedHeight = 4, margin = new RectOffset(0, 8, 10, 10),
        });

        public static GUIStyle Thumb => thumb ?? (thumb = new GUIStyle
        {
            normal = { background = Solid(Accent) }, hover = { background = Solid(Hex(0xf7c766)) }, active = { background = Solid(TextColor) },
            fixedWidth = 10, fixedHeight = 14, margin = new RectOffset(0, 0, -5, 0),
        });

        static GUIStyle bar, barThumb, none;

        // thin flat scrollbar: IMGUI finds the thumb and arrows by skin style name, so swap them in for one scroll view
        public static Vector2 Scroll(Vector2 pos, Action draw)
        {
            var sk = GUI.skin;
            GUIStyle b = sk.verticalScrollbar, t = sk.verticalScrollbarThumb, up = sk.verticalScrollbarUpButton, down = sk.verticalScrollbarDownButton;
            sk.verticalScrollbar = bar ?? (bar = new GUIStyle { normal = { background = Solid(Bg) }, fixedWidth = 6, margin = new RectOffset(0, 2, 2, 2) });
            sk.verticalScrollbarThumb = barThumb ?? (barThumb = new GUIStyle { normal = { background = Solid(Hex(0x4a4a53)) }, hover = { background = Solid(Hex(0x5a5a64)) }, fixedWidth = 6 });
            sk.verticalScrollbarUpButton = sk.verticalScrollbarDownButton = none ?? (none = new GUIStyle { fixedWidth = 0, fixedHeight = 0 });
            try
            {
                pos = GUILayout.BeginScrollView(pos, false, false, GUIStyle.none, sk.verticalScrollbar);
                draw();
                GUILayout.EndScrollView();
            }
            finally
            {
                sk.verticalScrollbar = b; sk.verticalScrollbarThumb = t; sk.verticalScrollbarUpButton = up; sk.verticalScrollbarDownButton = down;
            }
            return pos;
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
            Rect(r, new Color(0, 0, 0, 0.6f));
            Rect(new Rect(r.x + 1, r.y + 1, (r.width - 2) * Mathf.Clamp01(frac), r.height - 2), fill);
        }

        public static void Hud(string title, string hint)
        {
            var ts = TextStyle(16, TextColor, "left", true);
            var hs = TextStyle(11, Dim);
            float w = Mathf.Max(ts.CalcSize(new GUIContent(title)).x, string.IsNullOrEmpty(hint) ? 0f : hs.CalcSize(new GUIContent(hint)).x) + 28;
            float h = string.IsNullOrEmpty(hint) ? 36f : 54f;
            var r = new Rect(Screen.width - w - 16, Screen.height - h - 16, w, h);
            Panel(r, 0.85f);
            GUI.Label(new Rect(r.x + 14, r.y + 7, w - 28, 22), title, ts);
            if (!string.IsNullOrEmpty(hint)) GUI.Label(new Rect(r.x + 14, r.y + 29, w - 28, 16), hint, hs);
        }

        public static void Toast(string msg)
        {
            var st = TextStyle(13, TextColor);
            float w = Mathf.Min(Screen.width - 32, st.CalcSize(new GUIContent(msg)).x + 28);
            var r = new Rect((Screen.width - w) / 2f, 16, w, 32);
            Panel(r, 0.9f);
            Rect(new Rect(r.x + 1, r.y + 1, 2, r.height - 2), Accent);
            GUI.Label(new Rect(r.x + 14, r.y, w - 20, r.height), msg, st);
        }

        public static void Badge(Rect r, string text)
        {
            Panel(r, 0.8f);
            GUI.Label(r, text, TextStyle(11, Dim, "center"));
        }
    }
}
