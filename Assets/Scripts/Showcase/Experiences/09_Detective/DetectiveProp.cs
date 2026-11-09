using UnityEngine;

namespace Showcase.Experiences.Detective
{
    // A kitchen prop that is not a clue: tapping it gives a little wobble and a "Nothing here" quip.
    public class DetectiveProp : MonoBehaviour, ITappable
    {
        public string Quip = "Just an ordinary kitchen thing.";

        public void OnTap(RaycastHit hit) => GetComponentInParent<DetectiveExperience>(true)?.Nothing(this);
    }
}
