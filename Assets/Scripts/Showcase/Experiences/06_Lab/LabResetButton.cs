using UnityEngine;

namespace Showcase.Experiences.Lab
{
    // The big "Reset beaker" push button on the bench. Pulses when the beaker needs emptying.
    public class LabResetButton : MonoBehaviour, ITappable
    {
        [SerializeField] private LabExperience m_owner;
        [SerializeField] private Transform m_cap;

        public bool Pulsing { get; set; }

        private Vector3 m_capScale;

        void Awake()
        {
            if (m_cap != null)
                m_capScale = m_cap.localScale;
        }

        void Update()
        {
            if (m_cap == null)
                return;
            float k = Pulsing ? 1f + 0.08f * Mathf.Sin(Time.time * 7f) : 1f;
            m_cap.localScale = new Vector3(m_capScale.x * k, m_capScale.y, m_capScale.z * k);
        }

        public void OnTap(RaycastHit hit)
        {
            if (m_owner != null)
                m_owner.ResetBeaker(true);
        }
    }
}
