using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase
{
    // Screen-space bottom sheet for choosing things, big enough for thumbs on a phone:
    //   ┌ Title ─────────────────────── × ┐
    //   │ [ live 3D preview ]  Name        │   preview spins, drag to turn it
    //   │                      €649        │
    //   │                      detail…     │
    //   │ [tile][tile][tile][tile] ›       │   rendered model thumbnails / swatches, scrolls
    //   │ [  Action  ]  [  Action  ]       │
    //   └──────────────────────────────────┘
    // The 3D object in the world should change together with the selection (onSelect), so the tray
    // and the scene always show the same thing. While open, the free camera lifts the stage above it.
    //   Tray.Open("Sofa", options, slot.Index, i => slot.Show(i), ("♥ Save", Save), ("Done", Tray.Close));
    public class ShowcaseTray : MonoBehaviour
    {
        public static ShowcaseTray Instance { get; private set; }

        const float Pad = 30f, HeaderH = 96f, InfoH = 300f, TilesH = 236f, ActionsH = 116f, Gap = 18f;
        const float TileW = 196f;

        private RectTransform m_sheet, m_info, m_tilesViewport, m_tilesContent, m_actions;
        private ScrollRect m_scroll;
        private Text m_title, m_name, m_price, m_detail;
        private RawImage m_preview;
        private RectTransform m_previewFrame;
        private readonly List<Button> m_tiles = new List<Button>();
        private readonly List<Button> m_actionButtons = new List<Button>();
        private IList<TrayOption> m_options = Array.Empty<TrayOption>();
        private Action<int> m_onSelect;
        private Coroutine m_slide;
        private float m_height;

        public bool IsOpen { get; private set; }
        public int Selected { get; private set; } = -1;
        public IList<TrayOption> Options => m_options;
        public event Action Closed;
        public static event Action<bool> OpenChanged;

        // Fraction of the screen height the sheet covers (0 when closed).
        public float Cover => IsOpen && m_sheet != null ? m_height / Mathf.Max(1f, ((RectTransform)m_sheet.parent).rect.height) : 0f;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Built by ShowcaseHud into its screen canvas.
        public void Build(Transform canvas)
        {
            var bg = Ui.Panel(canvas, Ui.Paper, "Tray");
            bg.raycastTarget = true;   // taps on the sheet never reach the scene
            Ui.Round(bg, 44f);
            m_sheet = bg.rectTransform;
            m_sheet.anchorMin = new Vector2(0f, 0f);
            m_sheet.anchorMax = new Vector2(1f, 0f);
            m_sheet.pivot = new Vector2(0.5f, 0f);
            bg.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0f, 6f);

            var handle = Ui.Round(Ui.Panel(bg.transform, new Color(0f, 0f, 0f, 0.15f), "Handle"), 5f);
            Ui.Place(handle.rectTransform, new Vector2(0.5f, 1f), new Vector2(120, 10), new Vector2(0, -14));

            m_title = Ui.Label(bg.transform, "", 44, Ui.Muted, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
            Anchor(m_title.rectTransform, Pad, Pad + 110, -24f, HeaderH - 24f);

            var close = Ui.Button(bg.transform, "×", new Color(0.13f, 0.12f, 0.2f, 0.12f), Close, 60, "Close");
            close.GetComponentInChildren<Text>().color = Ui.Ink;
            Ui.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(96, 96), new Vector2(-Pad + 10, -18));

            m_info = Ui.Rect("Info", bg.transform);
            m_previewFrame = Ui.Rect("Preview", m_info);
            var frameImg = Ui.Round(Ui.Panel(m_previewFrame, Color.white, "Mask"), 30f);
            Ui.Stretch(frameImg.rectTransform);
            frameImg.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var raw = Ui.Rect("Live", frameImg.transform).gameObject.AddComponent<RawImage>();
            Ui.Stretch(raw.rectTransform);
            raw.raycastTarget = true;
            raw.gameObject.AddComponent<PreviewDragArea>();
            m_preview = raw;
            var turn = Ui.Label(frameImg.transform, "↔ drag to turn", 24, new Color(0.13f, 0.12f, 0.2f, 0.45f), TextAnchor.LowerCenter, FontStyle.Normal, "Turn Hint");
            Ui.Stretch(turn.rectTransform, 0, 0, 0, 10);

            m_name = Ui.Label(m_info, "", 52, Ui.Ink, TextAnchor.UpperLeft, FontStyle.Bold, "Name");
            m_price = Ui.Label(m_info, "", 44, Ui.Accent, TextAnchor.UpperLeft, FontStyle.Bold, "Price");
            m_detail = Ui.Label(m_info, "", 34, Ui.Muted, TextAnchor.UpperLeft, FontStyle.Normal, "Detail");
            m_detail.verticalOverflow = VerticalWrapMode.Truncate;
            m_detail.resizeTextForBestFit = true;
            m_detail.resizeTextMinSize = 24;
            m_detail.resizeTextMaxSize = 34;
            m_name.horizontalOverflow = HorizontalWrapMode.Wrap;
            m_name.resizeTextForBestFit = true;
            m_name.resizeTextMinSize = 34;
            m_name.resizeTextMaxSize = 52;

            var viewport = Ui.Panel(bg.transform, new Color(1, 1, 1, 0.003f), "Tiles");
            viewport.raycastTarget = true;   // lets the scroll view catch drags between tiles
            m_tilesViewport = viewport.rectTransform;
            viewport.gameObject.AddComponent<RectMask2D>();
            m_tilesContent = Ui.Rect("Content", m_tilesViewport);
            m_tilesContent.anchorMin = new Vector2(0f, 0f);
            m_tilesContent.anchorMax = new Vector2(0f, 1f);
            m_tilesContent.pivot = new Vector2(0f, 0.5f);
            m_scroll = viewport.gameObject.AddComponent<ScrollRect>();
            m_scroll.horizontal = true;
            m_scroll.vertical = false;
            m_scroll.viewport = m_tilesViewport;
            m_scroll.content = m_tilesContent;
            m_scroll.movementType = ScrollRect.MovementType.Elastic;
            m_scroll.scrollSensitivity = 30f;

            m_actions = Ui.Rect("Actions", bg.transform);

            bg.gameObject.SetActive(false);
        }

        private static void Anchor(RectTransform rt, float left, float right, float top, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, top - height);
            rt.offsetMax = new Vector2(-right, top);
        }

        // ---- API ----

        public void Open(string title, IList<TrayOption> options, int selected, Action<int> onSelect, params (string label, Action action)[] actions)
        {
            m_options = options ?? Array.Empty<TrayOption>();
            m_onSelect = onSelect;
            m_title.text = title ?? "";
            Layout(actions.Length);
            BuildTiles();
            BuildActions(actions);
            Selected = -1;
            Select(Mathf.Clamp(selected, 0, Mathf.Max(0, m_options.Count - 1)), false);
            ScrollTo(Selected);

            bool wasOpen = IsOpen;
            IsOpen = true;
            m_sheet.gameObject.SetActive(true);
            if (!wasOpen)
            {
                if (m_slide != null) StopCoroutine(m_slide);
                m_slide = StartCoroutine(Slide(-m_height - 80f, -40f));
                OpenChanged?.Invoke(true);
            }
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            IsOpen = false;
            ModelPreview.Instance.ClearLive();
            if (m_slide != null) StopCoroutine(m_slide);
            if (isActiveAndEnabled)
                m_slide = StartCoroutine(Slide(m_sheet.anchoredPosition.y, -m_height - 80f, true));
            else
                m_sheet.gameObject.SetActive(false);
            OpenChanged?.Invoke(false);
            Closed?.Invoke();
        }

        // Highlights a tile, updates the info and preview, and (if notify) tells the owner.
        public void Select(int index, bool notify = true)
        {
            if (index < 0 || index >= m_options.Count)
                return;
            Selected = index;
            RefreshInfo();
            for (int i = 0; i < m_tiles.Count; i++)
                StyleTile(i);
            if (notify)
            {
                if (m_tiles.Count > index) StartCoroutine(Tween.Punch(m_tiles[index].transform, 0.12f, 0.2f));
                m_onSelect?.Invoke(index);
            }
        }

        // Re-read the option texts/badges after the owner changed them (e.g. saved, price changed).
        public void Refresh()
        {
            if (!IsOpen) return;
            for (int i = 0; i < m_tiles.Count; i++)
            {
                m_tiles[i].transform.Find("Caption").GetComponent<Text>().text = m_options[i].Label;
                var swatch = m_tiles[i].transform.Find("Picture/Swatch");
                if (swatch != null && m_options[i].Swatch.a > 0f) swatch.GetComponent<Image>().color = m_options[i].Swatch;
                var pic = m_tiles[i].transform.Find("Picture").GetComponent<RawImage>();
                if (pic != null && m_options[i].Icon != null) pic.texture = m_options[i].Icon;
                StyleTile(i);
            }
            RefreshInfo();
        }

        public void SetTitle(string title) => m_title.text = title ?? "";

        public void SetAction(int index, string label, Color? color = null, bool interactable = true)
        {
            if (index < 0 || index >= m_actionButtons.Count) return;
            var b = m_actionButtons[index];
            if (label != null) Ui.SetButtonLabel(b, label);
            if (color.HasValue) b.GetComponent<Image>().color = color.Value;
            b.interactable = interactable;
        }

        public Button Action(int index) => index >= 0 && index < m_actionButtons.Count ? m_actionButtons[index] : null;

        // Automated tests: press an action button like a player would.
        public void Press(int index) => Action(index)?.onClick.Invoke();

        // ---- Build ----

        private bool HasPreview()
        {
            foreach (var o in m_options)
                if (o.Model != null) return true;
            return false;
        }

        private void Layout(int actionCount)
        {
            bool preview = HasPreview();
            float infoH = preview ? InfoH : 190f;
            float y = -HeaderH;
            Anchor(m_info, Pad, Pad, y, infoH);
            y -= infoH + Gap;
            Anchor(m_tilesViewport, 0f, 0f, y, TilesH);
            y -= TilesH + Gap;
            if (actionCount > 0)
            {
                Anchor(m_actions, Pad, Pad, y, ActionsH);
                y -= ActionsH;
            }
            m_actions.gameObject.SetActive(actionCount > 0);
            m_height = -y + Pad + 10f;
            m_sheet.sizeDelta = new Vector2(0f, m_height + 40f);   // extra hides the bottom corners off-screen

            m_previewFrame.gameObject.SetActive(preview);
            float textLeft = preview ? InfoH + 34f : 0f;
            Ui.Place(m_previewFrame, new Vector2(0f, 1f), new Vector2(InfoH, InfoH), Vector2.zero);
            Anchor(m_name.rectTransform, textLeft, 0f, -6f, 66f);
            Anchor(m_price.rectTransform, textLeft, 0f, -76f, 56f);
            Anchor(m_detail.rectTransform, textLeft, 0f, -138f, infoH - 138f);
            m_preview.texture = ModelPreview.Instance.Live;
        }

        private void BuildTiles()
        {
            for (int i = m_tilesContent.childCount - 1; i >= 0; i--)
                Destroy(m_tilesContent.GetChild(i).gameObject);
            m_tiles.Clear();
            for (int i = 0; i < m_options.Count; i++)
            {
                int index = i;
                var o = m_options[i];
                var b = Ui.Button(m_tilesContent, "", Color.white, () => Select(index), 26, "Tile " + i);
                Destroy(b.transform.Find("Label").gameObject);
                var rt = (RectTransform)b.transform;
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(TileW - 14f, TilesH - 16f);
                rt.anchoredPosition = new Vector2(Pad + i * TileW, 0f);

                var pic = Ui.Rect("Picture", b.transform);
                Ui.Place(pic, new Vector2(0.5f, 1f), new Vector2(TileW - 34f, TilesH - 82f), new Vector2(0f, -10f));
                if (o.Model != null || o.Icon != null)
                {
                    var raw = pic.gameObject.AddComponent<RawImage>();
                    raw.texture = o.Icon != null ? o.Icon : ModelPreview.Instance.Thumbnail(o.Model);
                    raw.raycastTarget = false;
                }
                else
                {
                    var sw = Ui.Round(Ui.Panel(pic, o.Swatch.a > 0f ? o.Swatch : Ui.Muted, "Swatch"), 60f);
                    Ui.Place(sw.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(110, 110), Vector2.zero);
                    if (o.Swatch.a <= 0f)
                    {
                        var initial = Ui.Label(sw.transform, string.IsNullOrEmpty(o.Label) ? "?" : o.Label.Substring(0, 1), 56, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                        Ui.Stretch(initial.rectTransform);
                    }
                }

                var label = Ui.Label(b.transform, o.Label, 26, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, "Caption");
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 18;
                label.resizeTextMaxSize = 26;
                Ui.Place(label.rectTransform, new Vector2(0.5f, 0f), new Vector2(TileW - 26f, 64f), new Vector2(0f, 4f));

                var badge = Ui.Round(Ui.Panel(b.transform, Ui.Accent, "Badge"), 26f);
                Ui.Place(badge.rectTransform, new Vector2(1f, 1f), new Vector2(52, 52), new Vector2(8, 8));
                var badgeText = Ui.Label(badge.transform, "", 30, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                Ui.Stretch(badgeText.rectTransform);

                m_tiles.Add(b);
            }
            m_tilesContent.sizeDelta = new Vector2(Pad * 2f + m_options.Count * TileW - 14f, 0f);
            m_tilesContent.anchoredPosition = Vector2.zero;
        }

        private void StyleTile(int i)
        {
            bool on = i == Selected;
            var b = m_tiles[i];
            var img = b.GetComponent<Image>();
            img.color = on ? Ui.Accent2 : new Color(0.13f, 0.12f, 0.2f, 0.07f);
            b.transform.Find("Caption").GetComponent<Text>().color = on ? Color.white : Ui.Ink;
            var badge = b.transform.Find("Badge");
            string text = m_options[i].Badge;
            badge.gameObject.SetActive(!string.IsNullOrEmpty(text));
            badge.GetComponentInChildren<Text>().text = text ?? "";
        }

        private void BuildActions((string label, Action action)[] actions)
        {
            for (int i = m_actions.childCount - 1; i >= 0; i--)
                Destroy(m_actions.GetChild(i).gameObject);
            m_actionButtons.Clear();
            for (int i = 0; i < actions.Length; i++)
            {
                var (label, action) = actions[i];
                var b = Ui.Button(m_actions, label, i == 0 ? Ui.Accent : Ui.Accent2, () => action?.Invoke(), 40, "Action " + i);
                var rt = (RectTransform)b.transform;
                float a0 = (float)i / actions.Length, a1 = (float)(i + 1) / actions.Length;
                rt.anchorMin = new Vector2(a0, 0f);
                rt.anchorMax = new Vector2(a1, 1f);
                rt.offsetMin = new Vector2(i == 0 ? 0f : 9f, 0f);
                rt.offsetMax = new Vector2(i == actions.Length - 1 ? 0f : -9f, 0f);
                m_actionButtons.Add(b);
            }
        }

        private void RefreshInfo()
        {
            if (Selected < 0 || Selected >= m_options.Count)
                return;
            var o = m_options[Selected];
            m_name.text = o.Label ?? "";
            m_price.text = o.Price ?? "";
            m_detail.text = o.Detail ?? "";
            if (o.Model != null) ModelPreview.Instance.ShowLive(o.Model);
            else ModelPreview.Instance.ClearLive();
        }

        // Scroll the tile row so tile `index` is centred (as far as the row allows).
        public void ScrollTo(int index)
        {
            if (m_options.Count <= 1 || index < 0) return;
            Canvas.ForceUpdateCanvases();
            float viewW = m_tilesViewport.rect.width, contentW = m_tilesContent.sizeDelta.x;
            if (contentW <= viewW) { m_scroll.horizontalNormalizedPosition = 0f; return; }
            float centre = Pad + index * TileW + TileW * 0.5f;
            m_scroll.horizontalNormalizedPosition = Mathf.Clamp01((centre - viewW * 0.5f) / (contentW - viewW));
        }

        private IEnumerator Slide(float from, float to, bool hideAtEnd = false)
        {
            for (float a = 0f; a < 1f; a += Time.unscaledDeltaTime / 0.24f)
            {
                float k = 1f - Mathf.Pow(1f - a, 3f);
                m_sheet.anchoredPosition = new Vector2(0f, Mathf.Lerp(from, to, k));
                yield return null;
            }
            m_sheet.anchoredPosition = new Vector2(0f, to);
            if (hideAtEnd) m_sheet.gameObject.SetActive(false);
            m_slide = null;
        }
    }
}
