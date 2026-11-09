using System.Collections;
using UnityEngine;

namespace Showcase.Experiences.Nightclub
{
    // A club prop that is not one of the hidden items: tapping it gives a small hop.
    public class NightclubDecoy : MonoBehaviour, ITappable
    {
        public NightclubExperience Game;
        private bool m_busy;
        private Vector3 m_restPosition, m_restScale;

        public void OnTap(RaycastHit hit)
        {
            if (Game != null)
                Game.OnDecoyTapped(this, hit);
        }

        public void Hop()
        {
            if (m_busy || !isActiveAndEnabled)
                return;
            StartCoroutine(HopRoutine());
        }

        public void ResetDecoy()
        {
            if (!m_busy)
                return;
            StopAllCoroutines();
            transform.localPosition = m_restPosition;
            transform.localScale = m_restScale;
            m_busy = false;
        }

        private IEnumerator HopRoutine()
        {
            // Guarded so overlapping taps cannot make the prop drift upwards or shrink.
            m_busy = true;
            m_restPosition = transform.localPosition;
            m_restScale = transform.localScale;
            StartCoroutine(Tween.Punch(transform, 0.12f, 0.3f));
            yield return Tween.Hop(transform, 0.05f, 0.3f);
            transform.localPosition = m_restPosition;
            transform.localScale = m_restScale;
            m_busy = false;
        }

        void OnDisable()
        {
            if (m_busy)
            {
                transform.localPosition = m_restPosition;
                transform.localScale = m_restScale;
                m_busy = false;
            }
        }
    }
}
