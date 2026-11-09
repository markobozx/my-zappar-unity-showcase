using UnityEngine;

namespace Showcase.Experiences.Looks
{
    // A normal gallery exhibit: tapping it shows the art-style credit card.
    public class LookExhibit : MonoBehaviour, ITappable
    {
        public string Title;
        [TextArea] public string Credit;
        public float CardHeight = 0.45f;
        [SerializeField] private LooksExperience m_owner;

        public void OnTap(RaycastHit hit)
        {
            if (m_owner != null)
                m_owner.ShowExhibit(this);
        }
    }
}
