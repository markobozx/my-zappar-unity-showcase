using UnityEngine;

namespace Showcase
{
    // World-space canvases need the camera that is actually rendering to receive clicks.
    public class WorldCanvasCamera : MonoBehaviour
    {
        private Canvas m_canvas;

        void Awake() { m_canvas = GetComponent<Canvas>(); }

        void LateUpdate()
        {
            Camera cam = ShowcaseRig.ActiveCamera;
            if (m_canvas != null && m_canvas.worldCamera != cam)
                m_canvas.worldCamera = cam;
        }
    }
}
