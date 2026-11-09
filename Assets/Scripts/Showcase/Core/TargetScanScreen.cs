using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase
{
    // The first thing a visitor sees: the live camera feed with "Find this image" and a card with the
    // target's picture and what it opens. With several targets the card is a slideshow (cross-fading
    // every few seconds, dots underneath, tap to skip). Leaves for good once the rig leaves Scanning,
    // i.e. when a target is found or the visitor taps "Explore without AR".
    public class TargetScanScreen : MonoBehaviour
    {
        private const float SlideSeconds = 3.2f;
        private const float FadeSeconds = 0.45f;

        private GameObject m_root;
        private readonly List<ShowcaseTarget> m_targets = new List<ShowcaseTarget>();
        private readonly List<CanvasGroup> m_slides = new List<CanvasGroup>();
        private readonly List<Image> m_dots = new List<Image>();
        private RectTransform m_scanLine;
        private Image m_frame;
        private Text m_label, m_status;
        private int m_slide;
        private float m_slideTime;

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

            if (ShowcaseTargets.Instance != null)
                m_targets.AddRange(ShowcaseTargets.Instance.Targets);
            title.text = m_targets.Count > 1 ? "Find one of these images" : "Find this image";
            BuildCard(root);

            m_status = Ui.Label(root, "", 38, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Status");
            Ui.Place(m_status.rectTransform, new Vector2(0.5f, 0f), new Vector2(900, 70), new Vector2(0, 330));
            m_status.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.5f);

            var explore = Ui.Button(root, "No image? Explore without AR ›", new Color(1f, 1f, 1f, 0.22f), () => ShowcaseRig.Instance?.StartFree(), 36, "Explore");
            Ui.Place((RectTransform)explore.transform, new Vector2(0.5f, 0f), new Vector2(720, 110), new Vector2(0, 170));

            var version = Ui.Label(root, "v" + Application.version, 26, new Color(1f, 1f, 1f, 0.6f), TextAnchor.LowerLeft, FontStyle.Normal, "Version");
            Ui.Place(version.rectTransform, new Vector2(0f, 0f), new Vector2(300, 50), new Vector2(30, 30));

            RefreshLabels();
        }

        private void BuildCard(Transform root)
        {
            const float cardW = 900f, imageH = 560f;
            bool slideshow = m_targets.Count > 1;
            float cardH = imageH + (slideshow ? 190f : 140f);
            var card = Ui.Round(Ui.Panel(root, new Color(1f, 0.98f, 0.95f, 0.95f), "Card"), 40f);
            Ui.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(cardW, cardH), new Vector2(0, 110));

            m_frame = Ui.Round(Ui.Panel(card.transform, Ui.Accent, "Frame"), 28f);
            Ui.Place(m_frame.rectTransform, new Vector2(0.5f, 1f), new Vector2(cardW - 32f, imageH), new Vector2(0, -16));

            var holder = Ui.Rect("Images", m_frame.transform);
            Ui.Stretch(holder, 10, 10, 10, 10);
            holder.gameObject.AddComponent<RectMask2D>();
            foreach (var target in m_targets)
            {
                var slide = Ui.Rect("Slide " + target.Id, holder);
                Ui.Stretch(slide);
                var group = slide.gameObject.AddComponent<CanvasGroup>();
                group.blocksRaycasts = false;
                group.alpha = m_slides.Count == 0 ? 1f : 0f;
                m_slides.Add(group);
                if (target.Image != null)
                {
                    var img = Ui.Rect("Picture", slide).gameObject.AddComponent<RawImage>();
                    img.texture = target.Image;
                    img.raycastTarget = false;
                    var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                    fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                    fit.aspectRatio = target.Image.width / (float)Mathf.Max(1, target.Image.height);
                }
                else
                {
                    var missing = Ui.Label(slide, target.Id, 40, Color.white);
                    Ui.Stretch(missing.rectTransform);
                }
            }

            // A bright line sweeping over the picture says "looking for this".
            var line = Ui.Panel(holder, new Color(1f, 1f, 1f, 0.75f), "Scan Line");
            line.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            line.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            line.rectTransform.sizeDelta = new Vector2(0f, 8f);
            m_scanLine = line.rectTransform;

            m_label = Ui.Label(card.transform, "", 40, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, "Opens");
            Ui.Place(m_label.rectTransform, new Vector2(0.5f, 0f), new Vector2(cardW - 40f, 90f), new Vector2(0, slideshow ? 74f : 24f));
            m_label.resizeTextForBestFit = true;
            m_label.resizeTextMinSize = 24;
            m_label.resizeTextMaxSize = 40;

            if (!slideshow)
                return;
            // Dots: which picture is showing; tapping the card skips to the next one.
            var dots = Ui.Rect("Dots", card.transform);
            Ui.Place(dots, new Vector2(0.5f, 0f), new Vector2(cardW, 40f), new Vector2(0, 26f));
            for (int i = 0; i < m_targets.Count; i++)
            {
                var dot = Ui.Round(Ui.Panel(dots, Ui.Muted, "Dot " + i), 12f);
                Ui.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(24f, 24f), new Vector2((i - (m_targets.Count - 1) * 0.5f) * 44f, 0f));
                m_dots.Add(dot);
            }
            card.raycastTarget = true;
            var skip = card.gameObject.AddComponent<Button>();
            skip.transition = Selectable.Transition.None;
            skip.onClick.AddListener(() => ShowSlide(m_slide + 1));
        }

        private void ShowSlide(int index)
        {
            if (m_targets.Count == 0)
                return;
            m_slide = (index % m_targets.Count + m_targets.Count) % m_targets.Count;
            m_slideTime = 0f;
            RefreshLabels();
        }

        private void RefreshLabels()
        {
            if (m_label == null || m_targets.Count == 0)
                return;
            var target = m_targets[m_slide];
            m_label.text = (m_targets.Count > 1 ? (m_slide + 1) + "/" + m_targets.Count + "  ·  " : "") + "Opens: " + ShowcaseTargets.TitleOf(target.Experience);
            for (int i = 0; i < m_dots.Count; i++)
                m_dots[i].color = i == m_slide ? Ui.Accent : new Color(Ui.Muted.r, Ui.Muted.g, Ui.Muted.b, 0.45f);
        }

        void Update()
        {
            if (m_root == null || !m_root.activeSelf)
                return;
            float t = Time.time;
            if (m_scanLine != null)
            {
                float h = ((RectTransform)m_scanLine.parent).rect.height;
                m_scanLine.anchoredPosition = new Vector2(0f, Mathf.Sin(t * 1.6f) * h * 0.46f);
            }
            if (m_frame != null)
                m_frame.color = Color.Lerp(Ui.Accent2, Ui.Accent, 0.6f + 0.4f * Mathf.Sin(t * 4f));
            if (m_status != null)
                m_status.text = "Scanning" + new string('.', 1 + (int)(t * 2f) % 3);

            if (m_slides.Count > 1)
            {
                m_slideTime += Time.deltaTime;
                if (m_slideTime >= SlideSeconds)
                    ShowSlide(m_slide + 1);
                float step = Time.deltaTime / FadeSeconds;
                for (int i = 0; i < m_slides.Count; i++)
                    m_slides[i].alpha = Mathf.MoveTowards(m_slides[i].alpha, i == m_slide ? 1f : 0f, step);
            }
        }
    }
}
