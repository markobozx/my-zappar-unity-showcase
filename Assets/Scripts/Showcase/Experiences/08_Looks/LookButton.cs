using UnityEngine;

namespace Showcase.Experiences.Looks
{
    // One of the four big style-dial buttons. The selected one sits pressed down; the others breathe.
    public class LookButton : MonoBehaviour, ITappable
    {
        public int Look;
        [SerializeField] private LooksExperience m_owner;
        [SerializeField] private Transform m_cap;
        [SerializeField] private GameObject m_selectedRing;

        private Vector3 m_capPos;
        private Vector3 m_capScale;
        private bool m_selected;

        void Awake()
        {
            if (m_cap != null)
            {
                m_capPos = m_cap.localPosition;
                m_capScale = m_cap.localScale;
            }
        }

        public void SetSelected(bool selected)
        {
            m_selected = selected;
            if (m_selectedRing != null) m_selectedRing.SetActive(selected);
            if (m_cap != null)
            {
                m_cap.localScale = m_capScale;
                m_cap.localPosition = m_capPos + (selected ? Vector3.down * m_capScale.y * 0.9f : Vector3.zero);
            }
        }

        void Update()
        {
            if (m_cap == null || m_selected)
                return;
            float k = 1f + 0.04f * Mathf.Sin(Time.time * 4f + Look * 1.3f);
            m_cap.localScale = new Vector3(m_capScale.x * k, m_capScale.y, m_capScale.z * k);
        }

        public void OnTap(RaycastHit hit)
        {
            if (m_owner != null)
                m_owner.SelectLook(Look);
        }
    }
}
