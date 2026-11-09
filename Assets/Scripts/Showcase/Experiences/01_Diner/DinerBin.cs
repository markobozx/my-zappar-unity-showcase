using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Diner
{
    // One ingredient crate. Tapping it sends its ingredient to the plate (same as its tile in the
    // ingredient tray, which also shows the menu facts).
    public class DinerBin : MonoBehaviour, ITappable
    {
        public Ingredient Ingredient;
        [Tooltip("The ingredient model sitting on the crate; pieces fly from here.")]
        public Transform Display;
        [Tooltip("Width of the crate's front face, in stage units, for the label.")]
        public float FrontWidth = 0.3f;
        public float FrontZ = -0.15f;
        public float LabelY = 0.08f;

        private DinerRushExperience m_game;
        private Vector3 m_baseScale;
        private Image m_labelBg;

        public Vector3 BaseScale => m_baseScale;
        public Vector3 SpawnPoint => Display != null ? DinerUtil.TopOf(Display) : transform.position + transform.up * 0.2f;

        void Awake()
        {
            m_game = GetComponentInParent<DinerRushExperience>(true);
            m_baseScale = transform.localScale;
            BuildLabel();
        }

        public void ResetVisual()
        {
            transform.localScale = m_baseScale;
        }

        public void OnTap(RaycastHit hit)
        {
            if (m_game != null)
                m_game.TapCrate(this);
        }

        // A sticker on the crate's front face with the ingredient name.
        private void BuildLabel()
        {
            var holder = new GameObject("Label").transform;
            holder.SetParent(transform, false);
            holder.localPosition = new Vector3(0f, LabelY, FrontZ);
            holder.localRotation = Quaternion.Euler(15f, 0f, 0f);
            var canvas = Ui.WorldCanvas("Canvas", holder, new Vector2(360, 96), FrontWidth);
            m_labelBg = Ui.Panel(canvas.transform, Ui.Paper, "Background");
            Ui.Stretch(m_labelBg.rectTransform);
            // Display only; otherwise taps on the crate's front are eaten as UI.
            m_labelBg.raycastTarget = false;
            var stripe = Ui.Panel(m_labelBg.transform, Ui.Accent, "Stripe");
            stripe.raycastTarget = false;
            Ui.Place(stripe.rectTransform, new Vector2(0f, 0.5f), new Vector2(10, 96), Vector2.zero);
            var name = Ui.Label(m_labelBg.transform, DinerMenu.Info(Ingredient).label, 56, Ui.Ink, TextAnchor.MiddleCenter, FontStyle.Bold, "Name");
            Ui.Stretch(name.rectTransform, 16, 6, 0, 0);
        }

        public void Flash(Color c)
        {
            if (m_labelBg != null)
                m_labelBg.color = c;
        }
    }
}
