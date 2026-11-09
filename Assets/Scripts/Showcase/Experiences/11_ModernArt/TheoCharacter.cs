using UnityEngine;

namespace Showcase.Experiences.ModernArt
{
    // Theo van Doesburg, the rigged glTF character standing on the book. Plays the model's legacy clips:
    // Idle when standing, Walking on a loop around the cover, the 360 spin jump on request. Tapping him
    // asks the experience to show who he is.
    public class TheoCharacter : MonoBehaviour, ITappable
    {
        public ModernArtExperience Experience;
        public Animation Anim;
        public float Height = 0.6f;
        public float WalkSpeed = 0.28f;   // tracker units per second
        public string IdleClip = "Idle_11", WalkClip = "Walking", SpinClip = "_360_Power_Spin_Jump";

        private Vector3 m_home;
        private bool m_walking;
        private float m_spinUntil;
        private Vector3[] m_path;
        private int m_next;

        public bool IsWalking => m_walking;
        public bool IsSpinning => Time.time < m_spinUntil;
        public float SpinSeconds => Anim != null && Anim[SpinClip] != null ? Anim[SpinClip].length : 1.5f;

        public void ResetTo(Vector3 localPos)
        {
            m_home = localPos;
            m_walking = false;
            m_spinUntil = 0f;
            transform.localPosition = localPos;
            transform.localRotation = Quaternion.identity;
            Play(IdleClip, 0f);
        }

        public void SetHome(Vector3 localPos)
        {
            m_home = localPos;
            if (!m_walking)
                StartCoroutine(Tween.MoveLocal(transform, localPos, 0.6f));
        }

        public void Stop()
        {
            m_walking = false;
            m_spinUntil = 0f;
            StopAllCoroutines();
            Play(IdleClip, 0f);
        }

        // Walks a loop just inside the edge of the cover. Returns true when walking.
        public bool ToggleWalk(float coverHeight)
        {
            m_walking = !m_walking;
            if (m_walking)
            {
                float hz = coverHeight * 0.5f - 0.3f, hx = coverHeight * 0.735f * 0.5f - 0.25f;
                m_path = new[] { new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz) };
                m_next = 0;
                Play(WalkClip, 0.2f);
            }
            else
            {
                Play(IdleClip, 0.25f);
                StartCoroutine(Tween.MoveLocal(transform, m_home, 0.6f));
            }
            return m_walking;
        }

        public void Spin()
        {
            m_walking = false;
            m_spinUntil = Time.time + SpinSeconds;
            Play(SpinClip, 0.1f);
        }

        public void OnTap(RaycastHit hit)
        {
            if (Experience != null) Experience.OnTheoTapped();
            else Spin();
        }

        void Update()
        {
            if (m_spinUntil > 0f && !IsSpinning)
            {
                m_spinUntil = 0f;
                Play(IdleClip, 0.25f);
                transform.localRotation = Quaternion.identity;
            }
            if (!m_walking || m_path == null || IsSpinning)
                return;
            Vector3 target = m_path[m_next];
            Vector3 pos = transform.localPosition;
            Vector3 to = target - pos;
            to.y = 0f;
            if (to.magnitude < 0.03f)
            {
                m_next = (m_next + 1) % m_path.Length;
                return;
            }
            transform.localPosition = Vector3.MoveTowards(pos, target, WalkSpeed * Time.deltaTime);
            // The model faces local -Z (toward the viewer) at rest, so turn that side into the walk direction.
            var look = Quaternion.LookRotation(-to.normalized, Vector3.up);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, look, 8f * Time.deltaTime);
        }

        private void Play(string clip, float fade)
        {
            if (Anim == null || Anim[clip] == null)
                return;
            Anim[clip].wrapMode = clip == SpinClip ? WrapMode.Once : WrapMode.Loop;
            if (fade <= 0f) Anim.Play(clip);
            else Anim.CrossFade(clip, fade);
        }
    }
}
