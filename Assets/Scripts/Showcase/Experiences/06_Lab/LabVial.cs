using UnityEngine;
using UnityEngine.UI;

namespace Showcase.Experiences.Lab
{
    // One ingredient vial on the bench. The vial's origin is its centre; the mouth is MouthY above it.
    public class LabVial : MonoBehaviour, ITappable
    {
        public int Index;
        public float MouthY = 0.12f;
        [SerializeField] private LabExperience m_owner;
        [SerializeField] private Transform m_cork;
        [SerializeField] private Image m_tagBackground;
        [SerializeField] private Text m_tagText;

        public Vector3 HomePosition { get; private set; }
        public Quaternion HomeRotation { get; private set; }
        public Transform Cork => m_cork;

        private Color m_tagIdle;
        private bool m_initialised;

        void Awake() => Init();

        private void Init()
        {
            if (m_initialised) return;
            m_initialised = true;
            HomePosition = transform.localPosition;
            HomeRotation = transform.localRotation;
            if (m_tagBackground != null) m_tagIdle = m_tagBackground.color;
        }

        public void ResetPose()
        {
            Init();
            transform.localPosition = HomePosition;
            transform.localRotation = HomeRotation;
            transform.localScale = Vector3.one;
            if (m_cork != null) m_cork.gameObject.SetActive(true);
            SetUsed(false);
        }

        // Highlights the floating name tag while this ingredient is in the beaker.
        public void SetUsed(bool used)
        {
            Init();
            Color c = LabData.Ingredients[Index].Color;
            if (m_tagBackground != null) m_tagBackground.color = used ? c : m_tagIdle;
            if (m_tagText != null)
            {
                m_tagText.color = used ? (c.grayscale > 0.6f ? Ui.Ink : Color.white) : (Index == LabData.Charcoal ? new Color(0.75f, 0.72f, 0.82f) : c);
                m_tagText.text = used ? "• " + LabData.Ingredients[Index].Name : LabData.Ingredients[Index].Name;
            }
        }

        public void OnTap(RaycastHit hit)
        {
            if (m_owner != null)
                m_owner.TapVial(Index);
        }
    }
}
