using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Drives the graphics-validation playground: tap handling, falling collectibles, score,
// and resetting the physics crate pyramid. Lives on the root of the playground under World,
// so everything it spawns hides and shows with the tracked target.
public class ShowcaseController : MonoBehaviour
{
    [Header("Effects (Cartoon FX)")]
    [SerializeField] private GameObject m_hitFx;
    [SerializeField] private GameObject m_poofFx;
    [SerializeField] private GameObject m_pickupFx;
    [SerializeField] private GameObject m_sparkleFx;
    [SerializeField] private float m_fxScale = 0.3f;

    [Header("Tap the floor to drop")]
    [SerializeField] private GameObject[] m_dropPrefabs;
    [SerializeField] private float m_dropScale = 1f;

    [Header("Collectible rain")]
    [SerializeField] private GameObject[] m_collectiblePrefabs;
    [SerializeField] private float m_collectibleScale = 1f;
    [SerializeField] private Vector2 m_stageHalfSize = new Vector2(1.3f, 0.8f);
    [SerializeField] private float m_rainHeight = 1.6f;
    [SerializeField] private float m_rainInterval = 1.1f;
    [SerializeField] private int m_maxCollectibles = 10;
    [SerializeField] private float m_collectRadius = 0.06f;
    [SerializeField] private Text m_scoreText;

    [Header("Physics pyramid")]
    [SerializeField] private Rigidbody[] m_pyramid;
    [SerializeField] private float m_pyramidResetDelay = 8f;
    [SerializeField] private float m_tapForce = 1.2f;

    private readonly List<Collectible> m_collectibles = new List<Collectible>();
    private Vector3[] m_pyramidPositions;
    private Quaternion[] m_pyramidRotations;
    private float m_rainTimer;
    private float m_lastPyramidHit = -1f;
    private int m_score;

    void Awake()
    {
        m_pyramidPositions = new Vector3[m_pyramid.Length];
        m_pyramidRotations = new Quaternion[m_pyramid.Length];
        for (int i = 0; i < m_pyramid.Length; i++)
        {
            m_pyramidPositions[i] = m_pyramid[i].transform.localPosition;
            m_pyramidRotations[i] = m_pyramid[i].transform.localRotation;
        }
        UpdateScore();
    }

    void OnEnable()
    {
        foreach (Rigidbody rb in m_pyramid)
            rb.Sleep();
    }

    void Update()
    {
        HandleTap();
        RainCollectibles();
        ResetPyramidWhenIdle();
    }

    private void HandleTap()
    {
        if (!Input.GetMouseButtonDown(0))
            return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Camera cam = ActiveCamera();
        if (cam == null)
            return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        // Falling items are small and moving, so give collecting a finger-sized tolerance.
        foreach (RaycastHit near in Physics.SphereCastAll(ray, m_collectRadius, 100f))
        {
            Collectible c = near.collider.GetComponentInParent<Collectible>();
            if (c != null)
            {
                Collect(c, c.transform.position);
                return;
            }
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, 100f))
            return;

        Collectible collectible = hit.collider.GetComponentInParent<Collectible>();
        if (collectible != null)
        {
            Collect(collectible, hit.point);
            return;
        }

        Rigidbody body = hit.rigidbody;
        if (body != null && !body.isKinematic)
        {
            body.AddForceAtPosition(ray.direction * m_tapForce, hit.point, ForceMode.Impulse);
            ShowcaseFx.Spawn(m_hitFx, hit.point, transform, m_fxScale);
            if (System.Array.IndexOf(m_pyramid, body) >= 0)
                m_lastPyramidHit = Time.time;
            return;
        }

        ShowcaseProp prop = hit.collider.GetComponentInParent<ShowcaseProp>();
        if (prop != null)
        {
            prop.Poke();
            ShowcaseFx.Spawn(m_sparkleFx, hit.point, transform, m_fxScale);
            return;
        }

        // Anything else is the stage floor: drop a random item onto the tapped spot.
        if (m_dropPrefabs != null && m_dropPrefabs.Length > 0)
        {
            Vector3 local = transform.InverseTransformPoint(hit.point);
            Vector3 spawn = transform.TransformPoint(new Vector3(local.x, local.y + 0.9f, local.z));
            SpawnPhysicsItem(m_dropPrefabs[Random.Range(0, m_dropPrefabs.Length)], spawn, m_dropScale);
            ShowcaseFx.Spawn(m_poofFx, spawn, transform, m_fxScale);
        }
    }

    private void Collect(Collectible collectible, Vector3 point)
    {
        ShowcaseFx.Spawn(m_pickupFx, point, transform, m_fxScale);
        m_collectibles.Remove(collectible);
        Destroy(collectible.gameObject);
        m_score++;
        UpdateScore();
    }

    private void RainCollectibles()
    {
        m_collectibles.RemoveAll(c => c == null);
        if (m_collectiblePrefabs == null || m_collectiblePrefabs.Length == 0 || m_collectibles.Count >= m_maxCollectibles)
            return;

        m_rainTimer += Time.deltaTime;
        if (m_rainTimer < m_rainInterval)
            return;
        m_rainTimer = 0f;

        Vector3 local = new Vector3(Random.Range(-m_stageHalfSize.x, m_stageHalfSize.x), m_rainHeight, Random.Range(-m_stageHalfSize.y, m_stageHalfSize.y));
        GameObject item = SpawnPhysicsItem(m_collectiblePrefabs[Random.Range(0, m_collectiblePrefabs.Length)], transform.TransformPoint(local), m_collectibleScale);
        item.GetComponent<Rigidbody>().angularVelocity = Random.insideUnitSphere * 6f;
        m_collectibles.Add(item.AddComponent<Collectible>());
    }

    private void ResetPyramidWhenIdle()
    {
        if (m_lastPyramidHit < 0f || Time.time - m_lastPyramidHit < m_pyramidResetDelay)
            return;
        m_lastPyramidHit = -1f;
        for (int i = 0; i < m_pyramid.Length; i++)
        {
            Rigidbody rb = m_pyramid[i];
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.transform.localPosition = m_pyramidPositions[i];
            rb.transform.localRotation = m_pyramidRotations[i];
            rb.Sleep();
            ShowcaseFx.Spawn(m_poofFx, rb.position, transform, m_fxScale * 0.6f);
        }
    }

    private GameObject SpawnPhysicsItem(GameObject prefab, Vector3 worldPosition, float scale)
    {
        GameObject go = Instantiate(prefab, worldPosition, transform.rotation * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
        go.transform.localScale = Vector3.one * scale;
        FitBoxCollider(go);
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.3f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        return go;
    }

    // Pack prefabs ship without colliders; wrap the meshes in one box in the root's local space.
    public static void FitBoxCollider(GameObject go)
    {
        Bounds local = new Bounds();
        bool any = false;
        Matrix4x4 toLocal = go.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null)
                continue;
            Bounds b = mf.sharedMesh.bounds;
            Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                else local.Encapsulate(p);
            }
        }
        BoxCollider box = go.AddComponent<BoxCollider>();
        if (any)
        {
            box.center = local.center;
            box.size = local.size;
        }
    }

    private void UpdateScore()
    {
        if (m_scoreText != null)
            m_scoreText.text = "Collected: " + m_score;
    }

    // The AR camera is the one with the highest depth (the Zappar background camera is -1).
    private static Camera ActiveCamera()
    {
        Camera best = null;
        foreach (Camera c in Camera.allCameras)
            if (best == null || c.depth > best.depth)
                best = c;
        return best;
    }
}
