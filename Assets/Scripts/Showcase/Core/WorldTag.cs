using UnityEngine;
using UnityEngine.UI;

namespace Showcase
{
    // A small always-facing label pinned above an object in the world (name, price, status, "tap me").
    // It's the world-space half of the UI: the scene says what things are, the tray lets you change them.
    //   var tag = WorldTag.Create(slot.transform, new Vector3(0, 0.5f, 0), "Sofa · €649");
    //   tag.Set("♥ Sofa · €649", Ui.Good);  tag.Pulse(true);
    public class WorldTag : MonoBehaviour
    {
        private Text m_text;
        private Image m_bg;
        private RectTransform m_canvas;
        private bool m_pulse;
        private float m_baseScale;

        public Text Text => m_text;

        public static WorldTag Create(Transform anchor, Vector3 localOffset, string text, float worldHeight = 0.13f, Color? color = null)
        {
            var root = new GameObject("World Tag");
            root.transform.SetParent(anchor, false);
            root.transform.localPosition = localOffset;
            // Keep a world-constant size under scaled parents.
            Vector3 ls = anchor.lossyScale;
            Transform world = ShowcaseRig.Instance != null ? ShowcaseRig.Instance.World : null;
            float worldScale = world != null ? world.lossyScale.x : 1f;
            root.transform.localScale = new Vector3(worldScale / Mathf.Max(ls.x, 1e-4f), worldScale / Mathf.Max(ls.y, 1e-4f), worldScale / Mathf.Max(ls.z, 1e-4f));
            root.AddComponent<Billboard>();
            var tag = root.AddComponent<WorldTag>();

            const float h = 96f;
            var canvas = Ui.WorldCanvas("Canvas", root.transform, new Vector2(400, h), worldHeight * 400f / h);
            canvas.sortingOrder = 5;
            tag.m_canvas = (RectTransform)canvas.transform;
            tag.m_baseScale = worldHeight / h;
            tag.m_bg = Ui.Round(Ui.Panel(canvas.transform, color ?? new Color(0.13f, 0.12f, 0.2f, 0.88f), "Background"), 48f);
            Ui.Stretch(tag.m_bg.rectTransform);
            tag.m_text = Ui.Label(tag.m_bg.transform, "", 52, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            tag.m_text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Stretch(tag.m_text.rectTransform, 26, 26, 4, 4);
            tag.Set(text);
            return tag;
        }

        public void Set(string text, Color? color = null)
        {
            m_text.text = text ?? "";
            if (color.HasValue) m_bg.color = color.Value;
            // Hug the text.
            float w = Mathf.Max(96f, m_text.preferredWidth + 56f);
            m_canvas.sizeDelta = new Vector2(w, m_canvas.sizeDelta.y);
        }

        public void Show(bool show) => gameObject.SetActive(show);

        // Gentle breathing to say "tap me".
        public void Pulse(bool on)
        {
            m_pulse = on;
            if (!on && m_canvas != null) m_canvas.localScale = Vector3.one * m_baseScale;
        }

        void Update()
        {
            if (m_pulse)
                m_canvas.localScale = Vector3.one * m_baseScale * (1f + 0.08f * Mathf.Sin(Time.time * 5f));
        }
    }
}
