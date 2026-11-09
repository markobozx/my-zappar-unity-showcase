using System;
using UnityEngine;

namespace Showcase
{
    // Owns the two ways of looking at World:
    //  - AR: the Zappar cameras render while the image target is tracked.
    //  - Free: when the target is not visible (and at startup), a normal camera orbits World,
    //    so every experience stays playable without the target.
    // World itself is always active; the tracker's seen/not-seen events call SetTracking.
    public class ShowcaseRig : MonoBehaviour
    {
        public static ShowcaseRig Instance { get; private set; }

        // The camera that currently renders the scene; use it for every raycast and billboard.
        public static Camera ActiveCamera => Instance != null ? Instance.Current : Camera.main;

        public static event Action<bool> TrackingChanged;

        [SerializeField] private Camera m_arCamera;
        [SerializeField] private Camera m_arBackgroundCamera;
        [SerializeField] private Camera m_freeCamera;
        [SerializeField] private Transform m_world;
        [SerializeField] private ShowcaseFxLibrary m_fx;

        public Transform World => m_world;
        public ShowcaseFxLibrary Fx => m_fx;
        public bool IsTracking { get; private set; }
        public Camera Current => IsTracking ? m_arCamera : m_freeCamera;

        void Awake()
        {
            Instance = this;
            // Model previews render on their own layer far below the stage; keep them out of the scene cameras.
            foreach (var cam in new[] { m_arCamera, m_arBackgroundCamera, m_freeCamera })
                if (cam != null) cam.cullingMask &= ~(1 << ModelPreview.Layer);
            Apply(false);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // Wired to ZapparImageTrackingTarget.OnSeenEvent / OnNotSeenEvent.
        public void OnTargetSeen() => SetTracking(true);
        public void OnTargetLost() => SetTracking(false);

        public void SetTracking(bool tracking)
        {
            if (tracking == IsTracking)
                return;
            Apply(tracking);
            TrackingChanged?.Invoke(tracking);
        }

        private void Apply(bool tracking)
        {
            IsTracking = tracking;
            // Only the Camera components are toggled; Zappar's own scripts keep running so tracking resumes.
            if (m_arCamera != null) m_arCamera.enabled = tracking;
            if (m_arBackgroundCamera != null) m_arBackgroundCamera.enabled = tracking;
            if (m_freeCamera != null) m_freeCamera.gameObject.SetActive(!tracking);
        }
    }
}
