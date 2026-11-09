using UnityEngine;

namespace Showcase
{
    // Orbit camera used while the image target is not tracked. ShowcaseInput feeds it drags and
    // pinches that did not land on anything interactive. Experiences call Frame() to show their area.
    [RequireComponent(typeof(Camera))]
    public class FreeCamera : MonoBehaviour
    {
        public static FreeCamera Instance { get; private set; }

        [SerializeField] private Transform m_pivotSpace;   // World
        [SerializeField] private Vector3 m_focus = Vector3.zero;
        [SerializeField] private float m_distance = 4.2f;
        [SerializeField] private float m_yaw = 0f;
        [SerializeField] private float m_pitch = 48f;
        [SerializeField] private float m_minDistance = 1.2f;
        [SerializeField] private float m_maxDistance = 8f;
        [SerializeField] private float m_smoothing = 8f;
        [Tooltip("Stage width (World units) kept visible at the default 4.2 distance; scales with closer framings.")]
        [SerializeField] private float m_fitWidth = 3.2f;

        private Vector3 m_targetFocus;
        private float m_targetDistance, m_targetYaw, m_targetPitch;
        private float m_lift;

        void Awake()
        {
            Instance = this;
            m_targetFocus = m_focus;
            m_targetDistance = m_distance;
            m_targetYaw = m_yaw;
            m_targetPitch = m_pitch;
            Place(1f);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        // Frame a point (in World-local space) at a given distance, keeping the current angle.
        public void Frame(Vector3 localFocus, float distance, float pitch = -1f)
        {
            m_targetFocus = localFocus;
            m_targetDistance = Mathf.Clamp(distance, m_minDistance, m_maxDistance);
            if (pitch > 0f)
                m_targetPitch = pitch;
            m_targetYaw = 0f;
        }

        // Jump straight to the target framing (no smoothing), e.g. after the render target changed aspect.
        public void SnapNow()
        {
            Place(1f);
        }

        public void Orbit(Vector2 screenDelta)
        {
            float k = 180f / Mathf.Max(Screen.height, 1);
            m_targetYaw += screenDelta.x * k;
            m_targetPitch = Mathf.Clamp(m_targetPitch - screenDelta.y * k, 12f, 85f);
        }

        public void Zoom(float factor)
        {
            m_targetDistance = Mathf.Clamp(m_targetDistance / Mathf.Max(factor, 0.01f), m_minDistance, m_maxDistance);
        }

        void LateUpdate()
        {
            Place(1f - Mathf.Exp(-m_smoothing * Time.unscaledDeltaTime));
        }

        private void Place(float t)
        {
            m_focus = Vector3.Lerp(m_focus, m_targetFocus, t);
            m_distance = Mathf.Lerp(m_distance, m_targetDistance, t);
            m_yaw = Mathf.LerpAngle(m_yaw, m_targetYaw, t);
            m_pitch = Mathf.Lerp(m_pitch, m_targetPitch, t);

            // Portrait phones see a narrow horizontal slice; back off until the stage width fits.
            Camera cam = GetComponent<Camera>();
            float distance = m_distance;
            if (cam != null)
            {
                float halfWidthTan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(cam.aspect, 0.1f);
                // A multiplier (not a minimum) so pinch-zoom still moves closer in portrait.
                float neededAtDefault = m_fitWidth * 0.5f / Mathf.Max(halfWidthTan, 0.01f);
                distance = m_distance * Mathf.Max(1f, neededAtDefault / 4.2f);
            }

            // Lift the stage above the bottom tray: moving the camera down along its up axis moves the picture up.
            float cover = ShowcaseTray.Instance != null ? ShowcaseTray.Instance.Cover : 0f;
            m_lift = Mathf.Lerp(m_lift, cover * 0.5f, t);

            Transform space = m_pivotSpace;
            Vector3 focusWorld = space != null ? space.TransformPoint(m_focus) : m_focus;
            Quaternion spaceRot = space != null ? space.rotation : Quaternion.identity;
            // Yaw 0 looks from the front of the stage (-Z) toward +Z, like holding a phone over the target.
            Quaternion rot = spaceRot * Quaternion.Euler(m_pitch, m_yaw, 0f);
            float scale = space != null ? space.lossyScale.x : 1f;
            transform.position = focusWorld - rot * Vector3.forward * (distance * scale);
            if (cam != null && m_lift > 0.001f)
                transform.position -= rot * Vector3.up * (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * distance * scale * m_lift);
            transform.rotation = rot;
        }
    }
}
