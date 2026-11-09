using UnityEngine;

namespace Showcase
{
    // Implement on any component whose GameObject (or a parent) has a collider, to receive taps.
    public interface ITappable
    {
        void OnTap(RaycastHit hit);
    }

    // Implement to be dragged with one finger. Dragging an IDraggable never orbits the free camera.
    public interface IDraggable
    {
        void OnDragStart(RaycastHit hit);
        // ray: the current pointer ray from the active camera.
        void OnDrag(Ray ray);
        void OnDragEnd();
    }
}
