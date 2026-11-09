using UnityEngine;

namespace Showcase.Experiences.BarCart
{
    // The shaker on the cart: tap it repeatedly, or grab it and wiggle, to fill the shake ring.
    public class BarCartShaker : MonoBehaviour, ITappable, IDraggable
    {
        private const float DragDegreesForFull = 220f;   // total pointer sweep that equals a full shake

        private BarCartExperience m_owner;
        private Ray m_lastRay;
        private bool m_hasRay;
        private float m_sinceWobble;

        private BarCartExperience Owner => m_owner != null ? m_owner : (m_owner = GetComponentInParent<BarCartExperience>(true));

        public void OnTap(RaycastHit hit) => Owner?.ShakeTap();

        public void OnDragStart(RaycastHit hit)
        {
            m_hasRay = false;
            m_sinceWobble = 0f;
        }

        public void OnDrag(Ray ray)
        {
            if (m_hasRay)
            {
                float deg = Vector3.Angle(m_lastRay.direction, ray.direction);
                m_sinceWobble += deg;
                bool wobble = m_sinceWobble > 6f;
                if (wobble) m_sinceWobble = 0f;
                Owner?.AddShake(deg / DragDegreesForFull, wobble);
            }
            m_lastRay = ray;
            m_hasRay = true;
        }

        public void OnDragEnd() => m_hasRay = false;
    }
}
