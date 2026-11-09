using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Showcase
{
    // Single place that turns mouse/touch into taps, drags and pinches.
    //  - Tap on a collider whose parents have an ITappable -> ITappable.OnTap (finger-sized tolerance).
    //  - Drag starting on an IDraggable -> OnDragStart/OnDrag/OnDragEnd.
    //  - Drag/pinch on empty space while not tracking -> orbits/zooms the FreeCamera.
    //  - Presses that start on UI (screen or world-space canvases) are ignored here.
    // Every tap also raises Tapped, so experiences can react to taps on empty floor.
    public class ShowcaseInput : MonoBehaviour
    {
        public static event Action<Ray, bool, RaycastHit> Tapped;   // ray, hitSomething, hit

        [SerializeField] private float m_tapMaxMove = 14f;       // pixels
        [SerializeField] private float m_tapMaxTime = 0.6f;      // seconds
        [SerializeField] private float m_tapTolerance = 0.05f;   // World units around the finger

        private bool m_pressed, m_pressOnUi, m_dragging, m_moved;
        private Vector2 m_downPos, m_lastPos;
        private float m_downTime;
        private bool m_downHit;
        private RaycastHit m_hit;
        private IDraggable m_draggable;
        private float m_lastPinch;

        void Update()
        {
            if (Input.touchCount >= 2)
            {
                HandlePinch();
                return;
            }
            m_lastPinch = 0f;

            float wheel = Input.mouseScrollDelta.y;
            if (Mathf.Abs(wheel) > 0.01f && !OverUi() && !IsTracking())
                FreeCamera.Instance?.Zoom(1f + wheel * 0.1f);

            if (Input.GetMouseButtonDown(0))
                Down(Input.mousePosition);
            else if (m_pressed && Input.GetMouseButton(0))
                Hold(Input.mousePosition);
            if (m_pressed && Input.GetMouseButtonUp(0))
                Up(Input.mousePosition);
        }

        private static bool IsTracking() => ShowcaseRig.Instance != null && ShowcaseRig.Instance.IsTracking;

        private static bool OverUi()
        {
            if (EventSystem.current == null)
                return false;
            if (Input.touchCount > 0)
                return EventSystem.current.IsPointerOverGameObject(Input.GetTouch(0).fingerId);
            return EventSystem.current.IsPointerOverGameObject();
        }

        private void Down(Vector2 pos)
        {
            m_pressed = true;
            m_pressOnUi = OverUi();
            m_moved = false;
            m_dragging = false;
            m_downPos = m_lastPos = pos;
            m_downTime = Time.unscaledTime;
            m_draggable = null;
            if (m_pressOnUi)
                return;

            m_downHit = Pick(pos, out m_hit, out _);
            if (m_downHit)
                m_draggable = m_hit.collider.GetComponentInParent<IDraggable>();
        }

        private void Hold(Vector2 pos)
        {
            if (m_pressOnUi)
                return;
            if (!m_moved && (pos - m_downPos).magnitude > m_tapMaxMove)
            {
                m_moved = true;
                if (m_draggable != null)
                {
                    m_dragging = true;
                    m_draggable.OnDragStart(m_hit);
                }
            }
            if (m_moved)
            {
                Camera cam = ShowcaseRig.ActiveCamera;
                if (m_dragging && cam != null)
                    m_draggable.OnDrag(cam.ScreenPointToRay(pos));
                else if (!IsTracking())
                    FreeCamera.Instance?.Orbit(pos - m_lastPos);
            }
            m_lastPos = pos;
        }

        private void Up(Vector2 pos)
        {
            m_pressed = false;
            if (m_dragging)
            {
                m_dragging = false;
                m_draggable?.OnDragEnd();
                return;
            }
            if (m_pressOnUi || m_moved || Time.unscaledTime - m_downTime > m_tapMaxTime)
                return;

            Camera cam = ShowcaseRig.ActiveCamera;
            if (cam == null)
                return;
            Ray ray = cam.ScreenPointToRay(pos);
            bool hitAny = Pick(pos, out RaycastHit hit, out ITappable tappable);
            tappable?.OnTap(hit);
            Tapped?.Invoke(ray, hitAny, hit);
        }

        // Nearest collider under the finger; if it isn't interactive, look a little around the
        // finger for something that is, because props on a phone screen are small.
        private bool Pick(Vector2 pos, out RaycastHit hit, out ITappable tappable)
        {
            tappable = null;
            hit = default;
            Camera cam = ShowcaseRig.ActiveCamera;
            if (cam == null)
                return false;
            Ray ray = cam.ScreenPointToRay(pos);
            bool any = Physics.Raycast(ray, out hit, 200f, ~0, QueryTriggerInteraction.Collide);
            if (any)
                tappable = hit.collider.GetComponentInParent<ITappable>();
            if (tappable == null)
            {
                float radius = m_tapTolerance * (ShowcaseRig.Instance != null && ShowcaseRig.Instance.World != null ? ShowcaseRig.Instance.World.lossyScale.x : 1f);
                float best = float.MaxValue;
                foreach (RaycastHit h in Physics.SphereCastAll(ray, radius, 200f, ~0, QueryTriggerInteraction.Collide))
                {
                    ITappable t = h.collider.GetComponentInParent<ITappable>();
                    if (t != null && h.distance < best && (!any || h.distance <= hit.distance + radius * 4f))
                    {
                        best = h.distance;
                        tappable = t;
                        hit = h;
                        any = true;
                    }
                }
            }
            return any;
        }

        private void HandlePinch()
        {
            m_pressed = false;
            if (m_dragging)
            {
                m_dragging = false;
                m_draggable?.OnDragEnd();
            }
            float d = (Input.GetTouch(0).position - Input.GetTouch(1).position).magnitude;
            if (m_lastPinch > 0f && !IsTracking())
                FreeCamera.Instance?.Zoom(d / m_lastPinch);
            m_lastPinch = d;
        }
    }
}
