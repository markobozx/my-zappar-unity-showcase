using UnityEngine;

namespace Showcase.Experiences.Stack
{
    // The Nova Logistics label stuck to the pallet: tap it for the brand info card.
    // Its trigger collider only catches taps; dropped items pass through it.
    public class StackInfoTag : MonoBehaviour, ITappable
    {
        [SerializeField] private string m_title = "Nova Logistics";
        [SerializeField, TextArea] private string m_body = "Our boxes survive anything.\nNext-day delivery, 120 cities.";
        [SerializeField] private Vector3 m_cardOffset = new Vector3(0f, 0.55f, -0.1f);

        private Vector3 m_baseScale;
        private Coroutine m_punch;

        void Awake() => m_baseScale = transform.localScale;

        public void OnTap(RaycastHit hit)
        {
            WorldCard.Show(transform, m_cardOffset, m_title, m_body, 1.0f);
            Fx.Play(FxKind.Sparkle, transform.position, 0.6f);
            var owner = GetComponentInParent<StackDeliveryExperience>();
            if (owner == null || !owner.isActiveAndEnabled)
                return;
            if (m_punch != null) owner.StopCoroutine(m_punch);
            transform.localScale = m_baseScale;
            m_punch = owner.StartCoroutine(Tween.Punch(transform, 0.2f, 0.3f));
        }
    }
}
