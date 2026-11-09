using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase
{
    // The first thing a visitor sees: the live camera feed with "Find this image" and a card per
    // target (its picture + what it opens). Leaves for good once the rig leaves Scanning, i.e. when a
    // target is found or the visitor taps "Explore without the image".
    public class TargetScanScreen : MonoBehaviour
    {
        private GameObject m_root;
        private readonly List<(Text label, ShowcaseTarget target)> m_cardLabels = new List<(Text, ShowcaseTarget)>();
        private readonly List<RectTransform> m_scanLines = new List<RectTransform>();
        private readonly List<Image> m_frames = new List<Image>();
        private Text m_status;

        void Start()
        {
            Build();
            ShowcaseRig.ModeChanged += OnModeChanged;
            ShowcaseTargets.ConfigChanged += RefreshLabels;
            OnModeChanged(ShowcaseRig.Instance != null ? ShowcaseRig.Instance.Mode : ViewMode.Scanning);
        }

        void OnDestroy()
        {
            ShowcaseRig.ModeChanged -= OnModeChanged;
            ShowcaseTargets.ConfigChanged -= RefreshLabels;
        }

        private void OnModeChanged(ViewMode mode)
        {
            if (m_root != null)
                m_root.SetActive(mode == ViewMode.Scanning);
        }

        private void Build()
        {
            var go = new GameObject("Scan Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            m_root = go;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;   // above the HUD (50) and the old example canvas
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            Transform root = go.transform;

            // Light dim so the camera feed stays visible; it also swallows taps meant for the hidden scene.
            var dim = Ui.Panel(root, new Color(0.05f, 0.04f, 0.1f, 0.38f), "Dim");
            Ui.Stretch(dim.rectTransform);
            dim.raycastTarget = true;

            var header = Ui.Round(Ui.Panel(root, new Color(0.13f, 0.12f, 0.2f, 0.82f), "Header"), 40f);
            Ui.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(980, 230), new Vector2(0, -60));
            var title = Ui.Label(header.transform, "", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
            Ui.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(940, 100), new Vector2(0, -24));
            var sub = Ui.Label(header.transform, "Point your camera at it to start the AR experience", 36, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Normal, "Subtitle");
            Ui.Place(sub.rectTransform, new Vector2(0.5f, 0f), new Vector2(920, 100), new Vector2(0, 22));

            var targets = ShowcaseTargets.Instance != null ? ShowcaseTargets.Instance.Targets : new List<ShowcaseTarget>();
            title.text = targets.Count > 1 ? "Find one of these images" : "Find this image";

            // One card per target, side by side, centred a little above the middle of the screen.
            int n = Mathf.Max(1, targets.Count);
            int cols = Mathf.Min(n, 3);
            int rows = Mathf.CeilToInt(n / (float)cols);
            float gap = 30f;
            float cardW = Mathf.Min(760f, (980f - gap * (cols - 1)) / cols);
            float imageH = cardW * 0.62f;
            float cardH = imageH + (cols == 1 ? 130f : 110f);
            float gridH = rows * cardH + (rows - 1) * gap;
            for (int i = 0; i < targets.Count; i++)
            {
                int r = i / cols, c = i % cols;
                int inRow = Mathf.Min(cols, targets.Count - r * cols);
                float x = (c - (inRow - 1) * 0.5f) * (cardW + gap);
                float y = 120f + gridH * 0.5f - cardH * 0.5f - r * (cardH + gap);
                BuildCard(root, targets[i], new Vector2(x, y), new Vector2(cardW, cardH), imageH, cols == 1 ? 40 : 30);
            }

            m_status = Ui.Label(root, "", 38, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Status");
            Ui.Place(m_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(900, 70), new Vector2(0, 330));
            m_status.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.5f);

            var explore = Ui.Button(root, "No image? Explore without AR ›", new Color(1f, 1f, 1f, 0.22f), () => ShowcaseRig.Instance?.StartFree(), 36, "Explore");
            Ui.Place((RectTransform)explore.transform, new Vector2(0.5f, 0f), new Vector2(720, 110), new Vector2(0, 170));

            var version = Ui.Label(root, "v" + Application.version, 26, new Color(1f, 1f, 1f, 0.6f), TextAnchor.LowerLeft, FontStyle.Normal, "Version");
            Ui.Place(version.rectTransform, new Vector2(0f, 0f), new Vector2(300, 50), new Vector2(30, 30));

            RefreshLabels();
        }

        private void BuildCard(Transform root, ShowcaseTarget target, Vector2 pos, Vector2 size, float imageH, int fontSize)
        {
            var card = Ui.Round(Ui.Panel(root, new Color(1f, 0.98f, 0.95f, 0.95f), "Card " + target.Id), 36f);
            Ui.Place(card.rectTransform, new Vector2(0.5f, 0.5f), size, pos);

            var frame = Ui.Round(Ui.Panel(card.transform, Ui.Accent, "Frame"), 26f);
            Ui.Place(frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(size.x - 28f, imageH), new Vector2(0, -14));
            m_frames.Add(frame);

            var holder = Ui.Rect("Image", frame.transform);
            Ui.Stretch(holder, 10, 10, 10, 10);
            holder.gameObject.AddComponent<RectMask2D>();
            if (target.Image != null)
            {
                var img = Ui.Rect("Picture", holder).gameObject.AddComponent<RawImage>();
                img.texture = target.Image;
                img.raycastTarget = false;
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = target.Image.width / (float)Mathf.Max(1, target.Image.height);
            }
            else
            {
                var missing = Ui.Label(holder, target.Id, 32, Color.white);
                Ui.Stretch(missing.rectTransform);
            }

            // A bright line sweeping over the picture says "looking for this".
            var line = Ui.Panel(holder, new Color(1f, 1f, 1f, 0.75f), "Scan Line");
            line.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            line.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            line.rectTransform.sizeDelta = new Vector2(0f, 8f);
            m_scanLines.Add(line.rectTransform);

            var label = Ui.Label(card.transform, "", fontSize, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, "Opens");
            Ui.Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(size.x - 30f, size.y - imageH - 34f), new Vector2(0, 10));
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 20;
            label.resizeTextMaxSize = fontSize;
            m_cardLabels.Add((label, target));
        }

        private void RefreshLabels()
        {
            foreach (var (label, target) in m_cardLabels)
                label.text = "Opens: " + ShowcaseTargets.TitleOf(target.Experience);
        }

        void Update()
        {
            if (m_root == null || !m_root.activeSelf)
                return;
            float t = Time.time;
            foreach (var line in m_scanLines)
            {
                float h = ((RectTransform)line.parent).rect.height;
                line.anchoredPosition = new Vector2(0f, Mathf.Sin(t * 1.6f) * h * 0.46f);
            }
            float glow = 0.6f + 0.4f * Mathf.Sin(t * 4f);
            foreach (var f in m_frames)
                f.color = Color.Lerp(Ui.Accent2, Ui.Accent, glow);
            if (m_status != null)
                m_status.text = "Scanning" + new string('.', 1 + (int)(t * 2f) % 3);
        }
    }
}
