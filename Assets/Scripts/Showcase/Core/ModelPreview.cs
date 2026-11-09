using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Showcase
{
    // Renders 3D models into UI textures (no asset package needed, works in WebGL):
    //  - Live: one spinning, drag-rotatable copy of a model in a 512 px RenderTexture (ShowcaseTray's big preview).
    //  - Thumbnail(model): a 192 px still, rendered once and cached (tray tiles).
    // Copies live on their own layer far below the stage, so the AR/free cameras never see them.
    // Copies are stripped of scripts, colliders and physics, so previews can't trigger gameplay.
    public class ModelPreview : MonoBehaviour
    {
        public const int Layer = 31;
        const int LiveSize = 512, ThumbSize = 192;
        static readonly Vector3 LiveSpot = new Vector3(0f, -500f, 0f);
        static readonly Vector3 ThumbSpot = new Vector3(40f, -500f, 0f);
        static readonly Color Backdrop = new Color(0.93f, 0.91f, 0.88f, 1f);

        private static ModelPreview s_instance;
        public static ModelPreview Instance
        {
            get
            {
                if (s_instance == null)
                    s_instance = new GameObject("Model Preview Stage").AddComponent<ModelPreview>();
                return s_instance;
            }
        }

        private Camera m_liveCam, m_thumbCam;
        private RenderTexture m_live;
        private Transform m_liveTurntable;
        private GameObject m_liveCopy, m_liveSource;
        private float m_yaw, m_spinVelocity;
        private bool m_userSpun;

        private readonly Dictionary<GameObject, RenderTexture> m_thumbs = new Dictionary<GameObject, RenderTexture>();
        private readonly Queue<GameObject> m_thumbQueue = new Queue<GameObject>();
        private GameObject m_thumbCopy;

        public Texture Live => m_live;

        void Awake()
        {
            s_instance = this;
            m_live = new RenderTexture(LiveSize, LiveSize, 16, RenderTextureFormat.ARGB32) { name = "Preview Live" };
            m_liveCam = MakeCamera("Live Camera", LiveSpot, m_live);
            m_liveCam.enabled = false;
            m_liveTurntable = new GameObject("Live Turntable").transform;
            m_liveTurntable.SetParent(transform, false);
            m_liveTurntable.position = LiveSpot;
            m_thumbCam = MakeCamera("Thumb Camera", ThumbSpot, null);
            m_thumbCam.enabled = false;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            if (m_live != null) m_live.Release();
            foreach (var rt in m_thumbs.Values)
                if (rt != null) rt.Release();
        }

        private Camera MakeCamera(string name, Vector3 spot, RenderTexture target)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Backdrop;
            cam.cullingMask = 1 << Layer;
            cam.fieldOfView = 26f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 20f;
            cam.targetTexture = target;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            // Looking slightly down at a unit-sized subject centred on the spot.
            Quaternion rot = Quaternion.Euler(18f, 0f, 0f);
            go.transform.rotation = rot;
            go.transform.position = spot - rot * Vector3.forward * 5.2f;
            return cam;
        }

        // ---- Live preview ----

        public void ShowLive(GameObject source)
        {
            if (source == m_liveSource && m_liveCopy != null)
                return;
            ClearLive();
            m_liveSource = source;
            if (source == null)
                return;
            m_liveCopy = MakeCopy(source, m_liveTurntable);
            m_liveCam.enabled = true;
            m_yaw = -25f;
            m_userSpun = false;
            m_spinVelocity = 0f;
        }

        public void ClearLive()
        {
            if (m_liveCopy != null) Destroy(m_liveCopy);
            m_liveCopy = null;
            m_liveSource = null;
            if (m_liveCam != null) m_liveCam.enabled = false;
        }

        // Called by the preview image's drag handler (pixels).
        public void Spin(float deltaPixels)
        {
            m_userSpun = true;
            m_spinVelocity = deltaPixels * 0.5f / Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            m_yaw += deltaPixels * 0.5f;
        }

        // ---- Thumbnails ----

        public Texture Thumbnail(GameObject source)
        {
            if (source == null)
                return null;
            if (m_thumbs.TryGetValue(source, out var rt) && rt != null)
                return rt;
            rt = new RenderTexture(ThumbSize, ThumbSize, 16, RenderTextureFormat.ARGB32) { name = "Thumb " + source.name };
            m_thumbs[source] = rt;
            m_thumbQueue.Enqueue(source);
            return rt;
        }

        void LateUpdate()
        {
            if (m_liveCopy != null)
            {
                if (!m_userSpun) m_yaw += 30f * Time.unscaledDeltaTime;
                else
                {
                    // Let a flick coast, then fall back to the slow idle spin.
                    m_spinVelocity = Mathf.Lerp(m_spinVelocity, 0f, 1f - Mathf.Exp(-3f * Time.unscaledDeltaTime));
                    m_yaw += m_spinVelocity * Time.unscaledDeltaTime;
                    if (Mathf.Abs(m_spinVelocity) < 5f) m_userSpun = false;
                }
                m_liveTurntable.rotation = Quaternion.Euler(0f, m_yaw, 0f);
            }

            // One thumbnail per frame: the copy set up now is rendered at the end of this frame.
            if (m_thumbCopy != null)
            {
                m_thumbCopy.SetActive(false);
                Destroy(m_thumbCopy);
                m_thumbCopy = null;
            }
            while (m_thumbQueue.Count > 0)
            {
                var source = m_thumbQueue.Dequeue();
                if (source == null || !m_thumbs.TryGetValue(source, out var rt) || rt == null)
                    continue;
                var holder = new GameObject("Thumb Turntable").transform;
                holder.SetParent(transform, false);
                holder.position = ThumbSpot;
                holder.rotation = Quaternion.Euler(0f, -28f, 0f);
                MakeCopy(source, holder);
                m_thumbCopy = holder.gameObject;
                m_thumbCam.targetTexture = rt;
                m_thumbCam.enabled = true;
                return;
            }
            m_thumbCam.enabled = false;
        }

        // ---- Copies ----

        private static GameObject MakeCopy(GameObject source, Transform parent)
        {
            // Instantiate under an inactive holder so no script on the copy ever wakes up.
            var holder = new GameObject("Copy");
            holder.SetActive(false);
            holder.transform.SetParent(parent, false);
            var copy = Instantiate(source, holder.transform);
            copy.SetActive(true);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            Strip(copy);
            SetLayer(copy.transform);

            // Fit the copy into a unit box centred on the spot.
            Vector3 lossy = source.transform.lossyScale;
            copy.transform.localScale = new Vector3(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z));
            holder.transform.localScale = Vector3.one;
            holder.transform.localPosition = Vector3.zero;
            Bounds b = RendererBounds(copy, holder.transform);
            float size = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.001f);
            float k = 1.55f / size;
            holder.transform.localScale = Vector3.one * k;
            holder.transform.localPosition = -b.center * k;
            holder.SetActive(true);
            return holder;
        }

        // Bounds in `space`-local coordinates, valid while the holder is inactive (uses mesh bounds).
        private static Bounds RendererBounds(GameObject go, Transform space)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                Bounds local;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) local = smr.sharedMesh.bounds;
                else if (r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh != null) local = mf.sharedMesh.bounds;
                else continue;
                Matrix4x4 m = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static void Strip(GameObject go)
        {
            // Canvases go first and whole: UI components depend on each other and can't be removed one by one.
            foreach (var c in go.GetComponentsInChildren<Canvas>(true))
                if (c != null && c.gameObject != go) DestroyImmediate(c.gameObject);
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Joint>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Light>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<ParticleSystem>(true)) c.gameObject.SetActive(false);
        }

        private static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            foreach (Transform c in t) SetLayer(c);
        }
    }
}
