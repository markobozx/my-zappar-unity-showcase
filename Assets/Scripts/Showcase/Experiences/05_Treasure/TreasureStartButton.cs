using UnityEngine;

namespace Showcase.Experiences.Treasure
{
    // The big world-space START button on the stage. Pulses while waiting for a tap.
    public class TreasureStartButton : MonoBehaviour, ITappable
    {
        [SerializeField] private TreasureRainExperience m_owner;
        [SerializeField] private Transform m_cap;

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
            float k = 1f + 0.06f * Mathf.Sin(Time.time * 5f);
            m_cap.localScale = new Vector3(m_capScale.x * k, m_capScale.y, m_capScale.z * k);
        }

        public void OnTap(RaycastHit hit)
        {
            if (m_owner != null)
                m_owner.StartRound();
        }
    }
}
