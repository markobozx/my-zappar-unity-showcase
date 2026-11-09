using UnityEngine;

namespace Showcase.Experiences.Room
{
    // The "Finish room" plinth on the floor edge. Taps on the 3D plinth land here; taps on its
    // label go through the UGUI button that RoomExperience puts on it.
    public class RoomFinishButton : MonoBehaviour, ITappable
    {
        public void OnTap(RaycastHit hit) => GetComponentInParent<RoomExperience>(true)?.Finish();
    }
}
