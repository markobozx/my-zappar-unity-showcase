using System.Collections;
using UnityEngine;

namespace Showcase.Experiences.Nightclub
{
    // One of the 8 hidden items. Its pivot sits at the bottom centre of the visual, so on the
    // inventory shelf it can simply be placed at the slot's origin.
    public class NightclubItem : MonoBehaviour, ITappable
    {
        public NightclubExperience Game;
        public string DisplayName;
        public string Brand;
        [TextArea] public string Blurb;
        public string HintText;
        public Color Color = Color.white;

        // Where the item hides; captured by the builder so every Enter can put it back.
        public Transform HomeParent;
        public Vector3 HomePosition;
        public Quaternion HomeRotation = Quaternion.identity;
        public Vector3 HomeScale = Vector3.one;
        public Quaternion ShelfRotation = Quaternion.identity;

        public bool Found { get; private set; }
        private bool m_pulsing;

        public void OnTap(RaycastHit hit)
        {
            if (Game != null)
                Game.OnItemTapped(this);
        }

        public void ResetItem()
        {
            StopAllCoroutines();
            m_pulsing = false;
            Found = false;
            transform.SetParent(HomeParent, false);
            transform.localPosition = HomePosition;
            transform.localRotation = HomeRotation;
            transform.localScale = HomeScale;
            SetCollider(true);
        }

        // Marks the item found and flies it to its shelf slot (instantly when `instant`).
        public void Collect(Transform slot, bool instant)
        {
            Found = true;
            m_pulsing = false;
            StopAllCoroutines();
            SetCollider(false);
            transform.localScale = HomeScale;
            transform.SetParent(slot, true);
            if (instant)
            {
                transform.localPosition = Vector3.zero;
                transform.localRotation = ShelfRotation;
                transform.localScale = HomeScale;
            }
            else
                StartCoroutine(FlyToShelf());
        }

        public void Pulse()
        {
            if (!Found && !m_pulsing && isActiveAndEnabled)
                StartCoroutine(PulseRoutine());
        }

        private IEnumerator FlyToShelf()
        {
            Vector3 from = transform.localPosition;
            Quaternion fromRot = transform.localRotation;
            const float time = 0.7f;
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                float e = Mathf.SmoothStep(0f, 1f, a);
                // A high arc reads as "picked up" from any camera angle.
                Vector3 p = Vector3.Lerp(from, Vector3.zero, e) + Vector3.up * (Mathf.Sin(a * Mathf.PI) * 0.45f);
                transform.localPosition = p;
                transform.localRotation = Quaternion.Slerp(fromRot, ShelfRotation, e) * Quaternion.Euler(0f, 360f * e, 0f);
                yield return null;
            }
            transform.localPosition = Vector3.zero;
            transform.localRotation = ShelfRotation;
            Fx.Play(FxKind.Sparkle, transform.position, 0.5f);
            yield return Tween.Punch(transform, 0.3f, 0.3f);
        }

        private IEnumerator PulseRoutine()
        {
            m_pulsing = true;
            for (int i = 0; i < 3 && !Found; i++)
            {
                Fx.Play(FxKind.Sparkle, transform.position + transform.up * 0.04f, 0.45f);
                yield return Tween.Punch(transform, 0.45f, 0.35f);
                yield return new WaitForSeconds(0.2f);
            }
            if (!Found)
                transform.localScale = HomeScale;
            m_pulsing = false;
        }

        private void SetCollider(bool on)
        {
            foreach (Collider c in GetComponents<Collider>())
                c.enabled = on;
        }
    }
}
