using UnityEngine;

namespace Showcase.Experiences.Detective
{
    // The case-notes board on the wall: tapping it opens the case file in the tray.
    public class DetectiveNotebook : MonoBehaviour, ITappable
    {
        public void OnTap(RaycastHit hit) => GetComponentInParent<DetectiveExperience>(true)?.OpenCaseFile(-1);
    }
}
