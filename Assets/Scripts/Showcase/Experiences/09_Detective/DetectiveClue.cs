using System;
using UnityEngine;

namespace Showcase.Experiences.Detective
{
    // One hidden clue in the kitchen. Its root is an unscaled holder at floor level whose
    // children are the visuals; the box collider on the root is the (generous) tap target.
    public class DetectiveClue : MonoBehaviour, ITappable
    {
        public string Title;
        [TextArea] public string Body;
        [Tooltip("Short line written into the case notebook.")]
        public string NoteLine;
        [Tooltip("Height of the top of this clue above its root, in stage units (card and magnifier float above).")]
        public float CardHeight = 0.1f;

        [Tooltip("Optional part that swings open when the clue is found (e.g. the freezer door).")]
        public Transform Animated;
        public Quaternion ClosedRotation = Quaternion.identity;
        public Quaternion OpenRotation = Quaternion.identity;

        [NonSerialized] public bool Found;

        public void OnTap(RaycastHit hit) => GetComponentInParent<DetectiveExperience>(true)?.Inspect(this);

        public void ResetClue()
        {
            Found = false;
            if (Animated != null)
                Animated.localRotation = ClosedRotation;
        }
    }
}
