using System;
using UnityEngine;

namespace Showcase.Experiences.Detective
{
    // One of the three suspects in the line-up. Built from primitives by the builder; `Body` is the
    // child that bobs and wiggles, `Glow` the floor ring that lights up once all clues are found.
    public class DetectiveSuspect : MonoBehaviour, ITappable
    {
        public string Name;
        [TextArea] public string Alibi;
        [Tooltip("What they say when wrongly accused.")]
        public string Denial;
        [Tooltip("Hint given after a wrong accusation.")]
        public string Hint;
        public bool IsCulprit;
        [Tooltip("Head height above the root, in stage units; name tag and cards float above it.")]
        public float CardHeight = 0.45f;
        public Transform Body;
        public GameObject Glow;

        [NonSerialized] public bool Lit;

        public void OnTap(RaycastHit hit) => GetComponentInParent<DetectiveExperience>(true)?.Question(this);

        public void SetLit(bool lit)
        {
            Lit = lit;
            if (Glow != null)
                Glow.SetActive(lit);
        }
    }
}
