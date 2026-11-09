using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Showcase
{
    // Small code-only UGUI toolkit so experiences can build world-space and screen-space UI at
    // runtime without prefabs. All sizes are in canvas pixels.
    public static class Ui
    {
        public static readonly Color Ink = new Color(0.13f, 0.12f, 0.2f);
        public static readonly Color Paper = new Color(1f, 0.98f, 0.95f, 0.96f);
        public static readonly Color Accent = new Color(1f, 0.45f, 0.38f);
        public static readonly Color Accent2 = new Color(0.36f, 0.62f, 1f);
        public static readonly Color Good = new Color(0.36f, 0.78f, 0.45f);
        public static readonly Color Muted = new Color(0.45f, 0.44f, 0.52f);

        private static Font s_font;
        public static Font Font => s_font != null ? s_font : (s_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        // A world-space canvas `worldWidth` units wide, facing -Z of `parent` (toward the viewer).
        public static Canvas WorldCanvas(string name, Transform parent, Vector2 sizePx, float worldWidth)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(WorldCanvasCamera));
            go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = ShowcaseRig.ActiveCamera;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            float s = worldWidth / sizePx.x;
            rt.localScale = new Vector3(s, s, s);
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static void Stretch(RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        // anchor/pivot in 0..1 of the parent; position is the offset from that anchor.
        public static void Place(RectTransform rt, Vector2 anchor, Vector2 size, Vector2 position)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }

        public static Image Panel(Transform parent, Color color, string name = "Panel")
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            // Decorative by default: a raycast-target image on a world canvas swallows taps meant for
            // 3D props drawn in front of it. Buttons and blocking panels opt back in.
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, FontStyle style = FontStyle.Normal, string name = "Label")
        {
            var rt = Rect(name, parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            return t;
        }

        public static Button Button(Transform parent, string label, Color color, UnityAction onClick, int fontSize = 34, string name = "Button")
        {
            var img = Panel(parent, color, name);
            img.raycastTarget = true;
            Round(img, 22f);
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
            b.colors = colors;
            if (onClick != null)
                b.onClick.AddListener(onClick);
            var t = Label(img.transform, label, fontSize, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(t.rectTransform, 8, 8, 4, 4);
            return b;
        }

        private static Sprite s_rounded;

        // Give an Image rounded corners of `radius` canvas pixels (a shared 9-sliced sprite made in code).
        public static Image Round(Image img, float radius = 24f)
        {
            if (s_rounded == null)
            {
                const int size = 64, r = 30;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Rounded", wrapMode = TextureWrapMode.Clamp };
                var px = new Color32[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float cx = Mathf.Clamp(x + 0.5f, r, size - r), cy = Mathf.Clamp(y + 0.5f, r, size - r);
                        float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                        px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01(r - d + 0.5f)));
                    }
                tex.SetPixels32(px);
                tex.Apply(false, true);
                s_rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(r + 1, r + 1, r + 1, r + 1));
            }
            img.sprite = s_rounded;
            img.type = Image.Type.Sliced;
            img.pixelsPerUnitMultiplier = s_rounded.border.x / Mathf.Max(radius, 1f);
            return img;
        }

        public static void SetButtonLabel(Button b, string label)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }
    }
}
