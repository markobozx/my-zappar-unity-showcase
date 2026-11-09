using UnityEngine;

namespace Showcase
{
    // Base class for one interactive experience. Its GameObject is a child of World/Experiences,
    // covers the whole stage (about 3 x 2 World units, floor at y = 0, viewer at -Z) and is only
    // active while the experience is open. The hub calls Enter/Exit.
    public abstract class ShowcaseExperience : MonoBehaviour
    {
        [Header("Experience")]
        public string Id;
        public string Title;
        [TextArea] public string Hint;
        [Tooltip("Free-camera framing while this experience is open (World-local).")]
        public Vector3 FocusPoint = new Vector3(0f, 0.1f, 0f);
        public float FocusDistance = 3.8f;
        public float FocusPitch = 48f;

        protected ShowcaseHud Hud => ShowcaseHud.Instance;

        public void Enter()
        {
            gameObject.SetActive(true);
            if (Hud != null)
            {
                Hud.Clear();
                Hud.SetTitle(Title);
                Hud.SetHint(Hint);
                Hud.ShowBack(true);
            }
            FreeCamera.Instance?.Frame(FocusPoint, FocusDistance, FocusPitch);
            OnEnter();
        }

        public void Exit()
        {
            WorldCard.CloseOpen();
            // Stop tweens first so they can't undo the cleanup done in OnExit (stopped Tween.Punch
            // coroutines restore their rest scale on the way out).
            StopAllCoroutines();
            OnExit();
            gameObject.SetActive(false);
        }

        // Start (or restart) the experience from a clean state.
        protected abstract void OnEnter();

        // Undo anything spawned at runtime so the next Enter starts fresh.
        protected virtual void OnExit() { }

        // Used by automated tests: put the experience in its "won/completed" state if possible.
        public virtual void DebugComplete() { }

        // Automated play test (run by the batch play-test harness after Enter()). Drive the experience
        // like a player would, e.g. call your ITappable.OnTap / game methods in order, yield between
        // steps, and report problems with Debug.LogError("[SELFTEST] ...") (any error fails the test).
        public virtual System.Collections.IEnumerator SelfTest() { yield break; }
    }
}
