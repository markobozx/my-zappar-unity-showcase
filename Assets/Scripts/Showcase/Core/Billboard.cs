using UnityEngine;

namespace Showcase
{
    // Keeps a transform turned toward the active camera around its up axis (cards, labels).
    public class Billboard : MonoBehaviour
    {
        public bool YawOnly = true;

        void LateUpdate()
        {
            Camera cam = ShowcaseRig.ActiveCamera;
            if (cam == null)
                return;
            Vector3 dir = transform.position - cam.transform.position;
            if (YawOnly)
                dir = Vector3.ProjectOnPlane(dir, transform.parent != null ? transform.parent.up : Vector3.up);
            if (dir.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(dir, transform.parent != null ? transform.parent.up : Vector3.up);
        }
    }
}
