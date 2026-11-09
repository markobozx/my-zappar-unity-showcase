using UnityEngine;
using UnityEngine.EventSystems;

namespace Showcase
{
    // Put on the tray's live preview image: horizontal drags turn the previewed model.
    public class PreviewDragArea : MonoBehaviour, IDragHandler
    {
        public void OnDrag(PointerEventData e) => ModelPreview.Instance.Spin(-e.delta.x);
    }
}
