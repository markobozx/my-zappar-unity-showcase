using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Showcase.Experiences.ModernArt
{
    // Modern Art: opened by the "Arte moderno" book cover. Everything sits in Target Space, a transform the
    // builder lines up with the image tracker, so the three layers (cover, portrait, blank page) are exactly
    // the size of the real book (1.47 x 2 tracker units, flat on it) and Theo van Doesburg stands on top.
    // The tray switches between three views of the layers; Theo walks, spins and explains himself.
    public class ModernArtExperience : ShowcaseExperience
    {
        public enum View { Book, PopUp, Layers }

        // One flat image layer; its pivot is the layer's near (bottom) edge so it can stand up like a pop-up page.
        [System.Serializable]
        public class Layer
        {
            public Transform Pivot;
            public CanvasGroup Group;
        }

        [Header("Modern Art")]
        public Layer Cover, Portrait, Blank;
        public TheoCharacter Theo;
        public Texture CoverTexture, PortraitTexture, BlankTexture;
        public float CoverHeight = 2f;
        public Transform TargetSpace;

        private View m_view;
        private Coroutine m_viewRoutine;
        private WorldTag m_theoTag;
        private bool m_theoMet;

        public View CurrentView => m_view;

        protected override void OnEnter()
        {
            StopAllCoroutines();
            m_theoMet = false;
            ApplyView(View.Book, true);
            Theo.ResetTo(new Vector3(0f, 0f, -0.45f));
            if (m_theoTag == null)
                m_theoTag = WorldTag.Create(Theo.transform, new Vector3(0f, Theo.Height + 0.12f, 0f), "Theo van Doesburg · tap me", WorldUnits(0.1f));
            m_theoTag.Set("Theo van Doesburg · tap me", new Color(0.13f, 0.12f, 0.2f, 0.88f));
            m_theoTag.Show(true);
            m_theoTag.Pulse(true);
            OpenTray();
        }

        protected override void OnExit()
        {
            ShowcaseTray.Instance?.Close();
            Theo.Stop();
        }

        private void OpenTray()
        {
            var tray = ShowcaseTray.Instance;
            if (tray == null)
                return;
            var options = new List<TrayOption>
            {
                new TrayOption("Book", "The cover", "The printed cover, lined up exactly with your book.") { Icon = CoverTexture },
                new TrayOption("Pop-up", "Page stands up", "The page goes blank and the portrait rises like a pop-up book.") { Icon = PortraitTexture },
                new TrayOption("Layers", "Exploded view", "Blank page, portrait and cover float apart as a stack of layers.") { Icon = BlankTexture },
            };
            tray.Open("Arte moderno", options, (int)m_view, i => SetView((View)i),
                ("Walk", ToggleWalk),
                ("Spin", Spin),
                ("About Theo", ShowTheoCard));
        }

        // ---------- views ----------

        public void SetView(View view)
        {
            if (view == m_view && m_viewRoutine == null)
                return;
            ApplyView(view, false);
            Fx.Play(FxKind.Sparkle, Portrait.Pivot.position + Portrait.Pivot.up * 0.3f, 0.8f);
        }

        private void ApplyView(View view, bool instant)
        {
            m_view = view;
            if (m_viewRoutine != null)
                StopCoroutine(m_viewRoutine);
            m_viewRoutine = null;
            // Target poses per layer: local position of the near-edge pivot, tilt, scale, opacity.
            // Gaps of a few millimetres keep coplanar layers from flickering.
            float h = CoverHeight * 0.5f;
            var poses = new List<(Layer layer, Vector3 pos, float tilt, float scale, float alpha)>();
            switch (view)
            {
                case View.Book:
                    poses.Add((Blank, new Vector3(0f, 0.002f, -h), 0f, 1f, 1f));
                    poses.Add((Portrait, new Vector3(0f, 0.004f, -h), 0f, 1f, 0f));
                    poses.Add((Cover, new Vector3(0f, 0.006f, -h), 0f, 1f, 1f));
                    break;
                case View.PopUp:
                    poses.Add((Blank, new Vector3(0f, 0.002f, -h), 0f, 1f, 1f));
                    poses.Add((Portrait, new Vector3(0f, 0.004f, 0.05f), -78f, 0.62f, 1f));
                    poses.Add((Cover, new Vector3(0f, 0.006f, -h), 0f, 1f, 0f));
                    break;
                case View.Layers:
                    poses.Add((Blank, new Vector3(0f, 0.002f, -h), 0f, 1f, 1f));
                    poses.Add((Portrait, new Vector3(0f, Theo.Height + 0.25f, -h), 0f, 1f, 0.72f));
                    poses.Add((Cover, new Vector3(0f, Theo.Height + 0.75f, -h), 0f, 1f, 0.55f));
                    break;
            }
            if (instant)
            {
                foreach (var p in poses)
                    Pose(p.layer, p.pos, p.tilt, p.scale, p.alpha, 1f, p.layer.Pivot.localPosition, p.layer.Pivot.localEulerAngles.x, p.layer.Pivot.localScale.x, p.layer.Group.alpha);
            }
            else
            {
                m_viewRoutine = StartCoroutine(AnimateView(poses));
            }
            // Theo stays on the page: in the pop-up view he steps in front of the standing portrait.
            Theo.SetHome(view == View.PopUp ? new Vector3(0f, 0f, -0.5f) : new Vector3(0f, 0f, -0.45f));
            ShowcaseTray.Instance?.Select((int)view, false);
            if (Hud != null)
                Hud.SetHint(view == View.Book ? "Pick a view below · tap Theo" : view == View.PopUp ? "Pop-up: the page stands up" : "Layers: look from the side");
        }

        private IEnumerator AnimateView(List<(Layer layer, Vector3 pos, float tilt, float scale, float alpha)> poses)
        {
            var from = new List<(Vector3 pos, float tilt, float scale, float alpha)>();
            foreach (var p in poses)
                from.Add((p.layer.Pivot.localPosition, NormalizedTilt(p.layer.Pivot.localEulerAngles.x), p.layer.Pivot.localScale.x, p.layer.Group.alpha));
            const float time = 0.9f;
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / time);
                for (int i = 0; i < poses.Count; i++)
                    Pose(poses[i].layer, poses[i].pos, poses[i].tilt, poses[i].scale, poses[i].alpha, k, from[i].pos, from[i].tilt, from[i].scale, from[i].alpha);
                yield return null;
            }
            for (int i = 0; i < poses.Count; i++)
                Pose(poses[i].layer, poses[i].pos, poses[i].tilt, poses[i].scale, poses[i].alpha, 1f, from[i].pos, from[i].tilt, from[i].scale, from[i].alpha);
            m_viewRoutine = null;
        }

        private static float NormalizedTilt(float x) => x > 180f ? x - 360f : x;

        private static void Pose(Layer l, Vector3 pos, float tilt, float scale, float alpha, float k, Vector3 fromPos, float fromTilt, float fromScale, float fromAlpha)
        {
            l.Pivot.localPosition = Vector3.Lerp(fromPos, pos, k);
            l.Pivot.localRotation = Quaternion.Euler(Mathf.Lerp(NormalizedTilt(fromTilt), tilt, k), 0f, 0f);
            l.Pivot.localScale = Vector3.one * Mathf.Lerp(fromScale, scale, k);
            l.Group.alpha = Mathf.Lerp(fromAlpha, alpha, k);
            l.Group.gameObject.SetActive(l.Group.alpha > 0.01f);
        }

        void Update()
        {
            // The exploded stack breathes a little so it reads as floating.
            if (m_view == View.Layers && m_viewRoutine == null)
            {
                float bob = Mathf.Sin(Time.time * 1.4f) * 0.03f;
                Portrait.Pivot.localPosition = new Vector3(0f, Theo.Height + 0.25f + bob, -CoverHeight * 0.5f);
                Cover.Pivot.localPosition = new Vector3(0f, Theo.Height + 0.75f - bob, -CoverHeight * 0.5f);
            }
        }

        // ---------- Theo ----------

        public void ToggleWalk()
        {
            bool walking = Theo.ToggleWalk(CoverHeight);
            ShowcaseTray.Instance?.SetAction(0, walking ? "Stop" : "Walk");
            MeetTheo();
        }

        public void Spin()
        {
            Theo.Spin();
            ShowcaseTray.Instance?.SetAction(0, "Walk");
            MeetTheo();
        }

        public void OnTheoTapped()
        {
            Spin();
            ShowTheoCard();
        }

        public void ShowTheoCard()
        {
            MeetTheo();
            WorldCard.Show(Theo.transform, new Vector3(0f, Theo.Height + 0.45f, 0f), "Theo van Doesburg",
                "Dutch artist, 1883-1931.\nFounded the De Stijl movement with Piet Mondrian: straight lines, flat primary colours, pure geometry.",
                WorldUnits(1.1f));
        }

        // Shared UI (WorldTag, WorldCard) is sized in World units; this scene is laid out in target units.
        private float WorldUnits(float targetUnits)
        {
            var world = ShowcaseRig.Instance != null ? ShowcaseRig.Instance.World : null;
            return world == null || TargetSpace == null ? targetUnits : targetUnits * TargetSpace.lossyScale.x / world.lossyScale.x;
        }

        private void MeetTheo()
        {
            if (m_theoMet)
                return;
            m_theoMet = true;
            m_theoTag.Set("Theo van Doesburg", Ui.Good);
            m_theoTag.Pulse(false);
        }

        // ---------- tests ----------

        public override void DebugComplete()
        {
            SetView(View.Layers);
            ShowTheoCard();
        }

        public override IEnumerator SelfTest()
        {
            var tray = ShowcaseTray.Instance;
            if (tray == null || !tray.IsOpen) Debug.LogError("[SELFTEST] modernart: tray not open on enter");
            if (!Cover.Group.gameObject.activeSelf || Cover.Group.alpha < 0.99f) Debug.LogError("[SELFTEST] modernart: cover not showing in Book view");
            Snapshot("book");

            tray.Select((int)View.PopUp);
            yield return new WaitForSeconds(1.2f);
            if (m_view != View.PopUp) Debug.LogError("[SELFTEST] modernart: tray did not switch to Pop-up");
            if (Cover.Group.gameObject.activeSelf) Debug.LogError("[SELFTEST] modernart: cover still visible in Pop-up");
            if (Mathf.Abs(NormalizedTilt(Portrait.Pivot.localEulerAngles.x) + 78f) > 1f) Debug.LogError("[SELFTEST] modernart: portrait did not stand up (" + Portrait.Pivot.localEulerAngles.x + ")");
            Snapshot("popup");

            tray.Press(0);
            yield return new WaitForSeconds(1.5f);
            if (!Theo.IsWalking) Debug.LogError("[SELFTEST] modernart: Walk did not start");
            tray.Press(1);
            yield return new WaitForSeconds(0.3f);
            if (!Theo.IsSpinning) Debug.LogError("[SELFTEST] modernart: Spin did not start");
            yield return new WaitForSeconds(Theo.SpinSeconds + 0.3f);
            if (Theo.IsSpinning) Debug.LogError("[SELFTEST] modernart: Spin never finished");

            tray.Select((int)View.Layers);
            yield return new WaitForSeconds(1.2f);
            if (Portrait.Pivot.localPosition.y < Theo.Height) Debug.LogError("[SELFTEST] modernart: layers did not lift");
            Theo.OnTap(new RaycastHit());
            yield return new WaitForSeconds(0.5f);
            Snapshot("layers");
            Debug.Log("[SELFTEST] modernart: views Book/Pop-up/Layers, walk and spin ok");
        }
    }
}
