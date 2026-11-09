using System.Collections;
using UnityEngine;

// Tap/click an object to make it hop, spin and change colour. Works the same in AR and
// in the editor; touches arrive as mouse clicks on WebGL with the legacy Input Manager.
[RequireComponent(typeof(Collider))]
public class TapBounce : MonoBehaviour
{
    [SerializeField]
    private Color[] m_colors = { new Color(1f, 0.45f, 0.35f), new Color(0.35f, 0.75f, 1f), new Color(1f, 0.85f, 0.3f), new Color(0.55f, 0.9f, 0.5f) };
    [SerializeField]
    private float m_hopHeight = 0.5f;
    [SerializeField]
    private float m_hopTime = 0.45f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private Renderer m_renderer;
    private MaterialPropertyBlock m_block;
    private Collider m_collider;
    private int m_colorIndex;
    private bool m_hopping;

    void Awake()
    {
        m_renderer = GetComponent<Renderer>();
        m_collider = GetComponent<Collider>();
        m_block = new MaterialPropertyBlock();
    }

    void Update()
    {
        if (!Input.GetMouseButtonDown(0) || m_hopping)
            return;

        Camera cam = ActiveCamera();
        if (cam == null)
            return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (m_collider.Raycast(ray, out _, 1000f))
            StartCoroutine(Hop());
    }

    // The Zappar rig has a background camera plus the AR camera; tap rays must come from
    // the AR one, which is the camera with the highest depth.
    private static Camera ActiveCamera()
    {
        Camera best = null;
        foreach (Camera c in Camera.allCameras)
            if (best == null || c.depth > best.depth)
                best = c;
        return best;
    }

    private IEnumerator Hop()
    {
        m_hopping = true;
        if (m_renderer != null && m_colors.Length > 0)
        {
            m_colorIndex = (m_colorIndex + 1) % m_colors.Length;
            m_renderer.GetPropertyBlock(m_block);
            m_block.SetColor(BaseColorId, m_colors[m_colorIndex]);
            m_renderer.SetPropertyBlock(m_block);
        }

        Vector3 start = transform.localPosition;
        Quaternion startRot = transform.localRotation;
        float height = m_hopHeight * transform.localScale.y;
        for (float t = 0f; t < 1f; t += Time.deltaTime / m_hopTime)
        {
            transform.localPosition = start + Vector3.up * (4f * height * t * (1f - t));
            transform.localRotation = startRot * Quaternion.Euler(0f, 360f * t, 0f);
            yield return null;
        }
        transform.localPosition = start;
        transform.localRotation = startRot;
        m_hopping = false;
    }
}
