using UnityEngine;

namespace Showcase.Experiences.BarCart
{
    // A tappable prop on the bar cart set: a cart style, a soda bottle, a glass or a garnish.
    public class BarCartPart : MonoBehaviour, ITappable
    {
        public BarCartKind Kind;
        public int Index;

        private BarCartExperience m_owner;
        private BarCartExperience Owner => m_owner != null ? m_owner : (m_owner = GetComponentInParent<BarCartExperience>(true));

        public void OnTap(RaycastHit hit) => Owner?.Tap(this);
    }
}
