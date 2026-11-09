using UnityEngine;

namespace Showcase.Experiences.Lab
{
    // Small round "i" badge in front of a vial: opens that ingredient's info card.
    public class LabInfoButton : MonoBehaviour, ITappable
    {
        public int Index;
        [SerializeField] private LabExperience m_owner;

        public void OnTap(RaycastHit hit)
        {
            if (m_owner != null)
                m_owner.ShowInfo(Index);
        }
    }
}
