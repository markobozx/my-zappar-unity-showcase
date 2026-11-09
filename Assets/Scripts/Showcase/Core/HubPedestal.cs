using UnityEngine;

namespace Showcase
{
    // One hub pedestal. Tap once: info card with "Play"; the card's Play opens the experience.
    // A pedestal without an experience shows "Coming soon".
    public class HubPedestal : MonoBehaviour, ITappable
    {
        public ShowcaseExperience Experience;
        public string Title;
        [TextArea] public string Blurb;
        public Transform Display;   // the spinning preview prop

        void Update()
        {
            if (Display != null)
                Display.Rotate(0f, 25f * Time.deltaTime, 0f, Space.Self);
        }

        public void OnTap(RaycastHit hit)
        {
            StartCoroutine(Tween.Punch(transform, 0.12f));
            Fx.Play(FxKind.Sparkle, transform.position + transform.up * 0.2f, 0.6f);
            if (Experience == null)
            {
                WorldCard.Show(transform, new Vector3(0f, 0.55f, 0f), Title, Blurb + "\n<i>Coming soon</i>", 0.8f);
                return;
            }
            WorldCard.Show(transform, new Vector3(0f, 0.55f, 0f), Title, Blurb, 0.8f,
                ("Play", () => ShowcaseHub.Instance?.Open(Experience)));
        }
    }
}
