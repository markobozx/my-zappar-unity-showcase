using System.Collections;
using UnityEngine;

namespace Showcase.Experiences.Nightclub
{
    // The glowing "Hint" pad at the front of the stage.
    public class NightclubHintButton : MonoBehaviour, ITappable
    {
        public NightclubExperience Game;
        private bool m_punching;

        public void OnTap(RaycastHit hit)
        {
            if (!m_punching && isActiveAndEnabled)
                StartCoroutine(PunchRoutine());
            if (Game != null)
                Game.ShowHint();
        }

        private IEnumerator PunchRoutine()
        {
            m_punching = true;
            yield return Tween.Punch(transform, 0.25f, 0.25f);
            m_punching = false;
        }

        void OnDisable() => m_punching = false;
    }
}
