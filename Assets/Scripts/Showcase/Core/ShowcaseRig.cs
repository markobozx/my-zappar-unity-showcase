using System;
using UnityEngine;

namespace Showcase
{
    public enum ViewMode
    {
        Scanning,   // startup: camera feed + "find this image" screen, World hidden
        Tracking,   // AR: the Zappar cameras render World on the image target
        Free,       // the target was lost (or the visitor chose to explore without it): orbit camera
    }

    // Owns the three ways of looking at World. The app starts in Scanning and only enters AR once a
    // target is seen; the free camera takes over only after the target is lost again, so every
    // experience stays playable without the target. World itself is always active;
    // ShowcaseTargets turns tracker seen/not-seen events into SetTracking calls.
    public class ShowcaseRig : MonoBehaviour
    {
        public static ShowcaseRig Instance { get; private set; }

        // The camera that currently renders the scene; use it for every raycast and billboard.
        public static Camera ActiveCamera => Instance != null ? Instance.Current : Camera.main;

        public static event Action<bool> TrackingChanged;
        public static event Action<ViewMode> ModeChanged;

        [SerializeField] private Camera m_arCamera;
        [SerializeField] private Camera m_arBackgroundCamera;
        [SerializeField] private Camera m_freeCamera;
        [SerializeField] private Transform m_world;
        [SerializeField] private ShowcaseFxLibrary m_fx;

        public Transform World => m_world;
        public ShowcaseFxLibrary Fx => m_fx;
        public ViewMode Mode { get; private set; } = ViewMode.Scanning;
        public bool IsTracking => Mode == ViewMode.Tracking;
        public bool IsScanning => Mode == ViewMode.Scanning;
        public Camera Current => IsTracking ? m_arCamera : m_freeCamera;
        public Camera ArCamera => m_arCamera;

        private int m_arMask;

        void Awake()
        {
            Instance = this;
            // Model previews render on their own layer far below the stage; keep them out of the scene cameras.
            foreach (var cam in new[] { m_arCamera, m_arBackgroundCamera, m_freeCamera })
                if (cam != null) cam.cullingMask &= ~(1 << ModelPreview.Layer);
            if (m_arCamera != null) m_arMask = m_arCamera.cullingMask;
            Apply(ViewMode.Scanning);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // Target seen -> AR; target lost -> free camera (never back to Scanning).
        public void SetTracking(bool tracking) => SetMode(tracking ? ViewMode.Tracking : ViewMode.Free);

        // "Explore without the image" on the scan screen.
        public void StartFree() => SetMode(ViewMode.Free);

        public void SetMode(ViewMode mode)
        {
            if (mode == Mode)
                return;
            bool wasTracking = IsTracking;
            Apply(mode);
            ModeChanged?.Invoke(mode);
            if (wasTracking != IsTracking)
                TrackingChanged?.Invoke(IsTracking);
        }

        private void Apply(ViewMode mode)
        {
            Mode = mode;
            bool ar = mode != ViewMode.Free;
            // Only the Camera components are toggled; Zappar's own scripts keep running so tracking resumes.
            // While scanning the feed shows but World is culled: until a target is found its pose is meaningless.
            if (m_arCamera != null)
            {
                m_arCamera.enabled = ar;
                m_arCamera.cullingMask = mode == ViewMode.Scanning ? 0 : m_arMask;
            }
            if (m_arBackgroundCamera != null) m_arBackgroundCamera.enabled = ar;
            if (m_freeCamera != null)
            {
                // Stays active while scanning (only its Camera is off) so experiences can still Frame() it.
                m_freeCamera.gameObject.SetActive(mode != ViewMode.Tracking);
                m_freeCamera.enabled = mode == ViewMode.Free;
            }
        }
    }
}
