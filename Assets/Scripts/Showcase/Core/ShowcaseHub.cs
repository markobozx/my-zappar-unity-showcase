using UnityEngine;

namespace Showcase
{
    // The hub: a stage of pedestals, one per experience. Tapping a pedestal opens its
    // experience; the HUD's Back button returns here.
    public class ShowcaseHub : MonoBehaviour
    {
        public static ShowcaseHub Instance { get; private set; }

        [SerializeField] private Vector3 m_focus = new Vector3(0f, 0.15f, 0f);
        [SerializeField] private float m_distance = 4.4f;
        [SerializeField] private string m_hint = "Tap a pedestal to open an experience";

        private ShowcaseExperience m_current;
        public ShowcaseExperience Current => m_current;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            if (ShowcaseHud.Instance != null)
                ShowcaseHud.Instance.BackPressed += ReturnToHub;
            ShowHub();
        }

        void OnDestroy()
        {
            if (ShowcaseHud.Instance != null)
                ShowcaseHud.Instance.BackPressed -= ReturnToHub;
            if (Instance == this)
                Instance = null;
        }

        public void Open(ShowcaseExperience experience)
        {
            if (experience == null)
                return;
            if (m_current != null)
                m_current.Exit();
            WorldCard.CloseOpen();
            gameObject.SetActive(false);
            m_current = experience;
            experience.Enter();
        }

        public void ReturnToHub()
        {
            if (m_current != null)
            {
                m_current.Exit();
                m_current = null;
            }
            ShowHub();
        }

        private void ShowHub()
        {
            gameObject.SetActive(true);
            var hud = ShowcaseHud.Instance;
            if (hud != null)
            {
                hud.Clear();
                hud.SetTitle("AR Showcase");
                hud.SetHint(m_hint);
            }
            FreeCamera.Instance?.Frame(m_focus, m_distance, 50f);
        }
    }
}
