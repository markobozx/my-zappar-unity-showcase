using UnityEngine;

namespace Showcase.Experiences.Looks
{
    // A hidden object that only exists in one look. Idles with a slow spin and bob so it reads as "special".
    public class LookSecret : MonoBehaviour, ITappable
    {
        public int Look;
        public string Title;
        public float Spin = 40f;
        public float Bob = 0.02f;
        [SerializeField] private LooksExperience m_owner;
        [SerializeField] private Transform m_visual;

        [System.NonSerialized] public bool Found;
        [System.NonSerialized] public bool Collecting;

        private Vector3 m_visualPos;
        private Quaternion m_visualRot;
        private Vector3 m_scale;
        private bool m_initialised;

        public Transform Visual => m_visual != null ? m_visual : transform;

        void Awake() => Init();

        // Secrets are built inactive, so ResetGame calls ResetPose before Awake ever ran; without this
        // lazy init the pose was captured as zero and every secret stayed invisible (scale 0).
        private void Init()
        {
            if (m_initialised) return;
            m_initialised = true;
            m_scale = transform.localScale;
            if (m_visual != null)
            {
                m_visualPos = m_visual.localPosition;
                m_visualRot = m_visual.localRotation;
            }
        }

        public void ResetPose()
        {
            Init();
            transform.localScale = m_scale;
            if (m_visual != null)
            {
                m_visual.localPosition = m_visualPos;
                m_visual.localRotation = m_visualRot;
            }
        }

        void Update()
        {
            if (m_visual == null || Collecting)
                return;
            m_visual.localPosition = m_visualPos + Vector3.up * Bob * Mathf.Sin(Time.time * 2.2f + Look);
            m_visual.localRotation = Quaternion.Euler(0f, Time.time * Spin, 0f) * m_visualRot;
        }

        public void OnTap(RaycastHit hit)
        {
            if (m_owner != null)
                m_owner.Collect(this);
        }
    }
}
