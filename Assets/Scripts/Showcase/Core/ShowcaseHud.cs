using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Showcase
{
    // Screen-space HUD shared by all experiences, built in code on Awake.
    //   top:    [Back]  Title                Score
    //   below:  Timer
    //   bottom: Hint (tracking help / instructions)
    //   centre: Toast banners and the Result panel (title, body, buttons)
    // Keep screen UI minimal; prefer WorldCard for object information.
    public class ShowcaseHud : MonoBehaviour
    {
        public static ShowcaseHud Instance { get; private set; }

        private Text m_title, m_score, m_timer, m_hint, m_toast;
        private Button m_back;
        private RectTransform m_result;
        private Text m_resultTitle, m_resultBody;
        private RectTransform m_resultButtons;
        private Coroutine m_toastRoutine;
        private string m_experienceHint = "";

        public event Action BackPressed;

        void Awake()
        {
            Instance = this;
            Build();
            ShowcaseRig.TrackingChanged += OnTrackingChanged;
            OnTrackingChanged(ShowcaseRig.Instance != null && ShowcaseRig.Instance.IsTracking);
        }

        void OnDestroy()
        {
            ShowcaseRig.TrackingChanged -= OnTrackingChanged;
            if (Instance == this)
                Instance = null;
        }

        private void Build()
        {
            var go = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            Transform root = go.transform;

            m_back = Ui.Button(root, "‹ Hub", new Color(0.13f, 0.12f, 0.2f, 0.8f), () => BackPressed?.Invoke(), 40, "Back");
            Ui.Place((RectTransform)m_back.transform, new Vector2(0f, 1f), new Vector2(220, 100), new Vector2(30, -50));

            m_title = Ui.Label(root, "", 52, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
            Ui.Place(m_title.rectTransform, new Vector2(0.5f, 1f), new Vector2(560, 100), new Vector2(0, -50));
            m_title.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.55f);

            m_score = Ui.Label(root, "", 48, Color.white, TextAnchor.MiddleRight, FontStyle.Bold, "Score");
            Ui.Place(m_score.rectTransform, new Vector2(1f, 1f), new Vector2(320, 100), new Vector2(-34, -50));
            m_score.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.55f);

            m_timer = Ui.Label(root, "", 64, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold, "Timer");
            Ui.Place(m_timer.rectTransform, new Vector2(0.5f, 1f), new Vector2(400, 90), new Vector2(0, -150));
            m_timer.gameObject.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.55f);

            var hintBg = Ui.Panel(root, new Color(0.13f, 0.12f, 0.2f, 0.6f), "Hint");
            Ui.Place(hintBg.rectTransform, new Vector2(0.5f, 0f), new Vector2(980, 110), new Vector2(0, 200));
            hintBg.raycastTarget = false;
            m_hint = Ui.Label(hintBg.transform, "", 36, Color.white);
            Ui.Stretch(m_hint.rectTransform, 24, 24, 8, 8);

            var toastBg = Ui.Panel(root, new Color(1f, 0.98f, 0.95f, 0.95f), "Toast");
            Ui.Place(toastBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(820, 150), new Vector2(0, 380));
            toastBg.raycastTarget = false;
            m_toast = Ui.Label(toastBg.transform, "", 46, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(m_toast.rectTransform, 20, 20, 10, 10);
            toastBg.gameObject.SetActive(false);

            var result = Ui.Panel(root, new Color(1f, 0.98f, 0.95f, 0.97f), "Result");
            result.raycastTarget = true;   // block taps to the scene while the result is up
            m_result = result.rectTransform;
            Ui.Place(m_result, new Vector2(0.5f, 0.5f), new Vector2(880, 620), Vector2.zero);
            var stripe = Ui.Panel(result.transform, Ui.Accent, "Stripe");
            Ui.Place(stripe.rectTransform, new Vector2(0.5f, 1f), new Vector2(880, 16), Vector2.zero);
            m_resultTitle = Ui.Label(result.transform, "", 64, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, "Title");
            Ui.Place(m_resultTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(800, 120), new Vector2(0, -50));
            m_resultBody = Ui.Label(result.transform, "", 40, Ui.Muted, TextAnchor.MiddleCenter, FontStyle.Normal, "Body");
            Ui.Place(m_resultBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(800, 240), new Vector2(0, 30));
            m_resultButtons = Ui.Rect("Buttons", result.transform);
            Ui.Place(m_resultButtons, new Vector2(0.5f, 0f), new Vector2(800, 110), new Vector2(0, 40));
            result.gameObject.SetActive(false);

            Clear();
        }

        // Called by the hub when entering/leaving an experience.
        public void Clear()
        {
            SetTitle("");
            SetScore("");
            SetTimer("");
            SetHint("");
            ShowBack(false);
            HideResult();
        }

        public void SetTitle(string t) => m_title.text = t;
        public void SetScore(string t) => m_score.text = t;
        public void SetTimer(string t) => m_timer.text = t;
        public void ShowBack(bool show) => m_back.gameObject.SetActive(show);

        // Instruction line for the current experience; a tracking tip is added while not tracking.
        public void SetHint(string t)
        {
            m_experienceHint = t ?? "";
            Refresh();
        }

        private void OnTrackingChanged(bool tracking) => Refresh();

        private void Refresh()
        {
            bool tracking = ShowcaseRig.Instance != null && ShowcaseRig.Instance.IsTracking;
            string tip = tracking ? "" : "Point at the target for AR · drag to look around";
            string text = string.IsNullOrEmpty(m_experienceHint) ? tip : (string.IsNullOrEmpty(tip) ? m_experienceHint : m_experienceHint + "\n<size=28>" + tip + "</size>");
            m_hint.text = text;
            var box = (RectTransform)m_hint.transform.parent;
            box.gameObject.SetActive(!string.IsNullOrEmpty(text));
            // Grow the box to the wrapped text so long hints never spill out.
            box.sizeDelta = new Vector2(box.sizeDelta.x, Mathf.Max(110f, m_hint.preferredHeight + 24f));
        }

        public void Toast(string text, float seconds = 1.6f)
        {
            if (m_toastRoutine != null)
                StopCoroutine(m_toastRoutine);
            m_toastRoutine = StartCoroutine(ToastRoutine(text, seconds));
        }

        private IEnumerator ToastRoutine(string text, float seconds)
        {
            var bg = m_toast.transform.parent.gameObject;
            m_toast.text = text;
            bg.SetActive(true);
            var rt = (RectTransform)bg.transform;
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.18f)
            {
                rt.localScale = Vector3.one * Mathf.SmoothStep(0.6f, 1f, a);
                yield return null;
            }
            rt.localScale = Vector3.one;
            yield return new WaitForSeconds(seconds);
            bg.SetActive(false);
        }

        public void ShowResult(string title, string body, params (string label, Action action)[] buttons)
        {
            m_resultTitle.text = title;
            m_resultBody.text = body;
            for (int i = m_resultButtons.childCount - 1; i >= 0; i--)
                Destroy(m_resultButtons.GetChild(i).gameObject);
            float w = (800 - 20 * (buttons.Length - 1)) / Mathf.Max(1, buttons.Length);
            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, action) = buttons[i];
                var b = Ui.Button(m_resultButtons, label, i == 0 ? Ui.Accent : Ui.Accent2, () => { HideResult(); action?.Invoke(); }, 40);
                Ui.Place((RectTransform)b.transform, new Vector2(0f, 0.5f), new Vector2(w, 110), new Vector2(i * (w + 20), 0));
            }
            m_result.gameObject.SetActive(true);
        }

        public void HideResult() => m_result.gameObject.SetActive(false);
        public bool ResultVisible => m_result.gameObject.activeSelf;
    }
}
