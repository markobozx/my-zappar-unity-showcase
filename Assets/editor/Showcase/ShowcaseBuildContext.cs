using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Showcase.EditorTools
{
    // Marks a static class as the builder of one hub experience. The class must have
    //   public static void Build(ShowcaseBuildContext ctx, Transform root)
    // which fills `root` (a fresh GameObject under World/Experiences) and adds the experience's
    // ShowcaseExperience component. Order = pedestal position in the hub (1..10).
    [AttributeUsage(AttributeTargets.Class)]
    public class ShowcaseExperienceBuilderAttribute : Attribute
    {
        public readonly int Order;
        public readonly string Id;
        public readonly string Title;
        public readonly string Blurb;
        public readonly string PreviewPrefab;   // prop shown spinning on the hub pedestal

        public ShowcaseExperienceBuilderAttribute(int order, string id, string title, string blurb, string previewPrefab)
        {
            Order = order; Id = id; Title = title; Blurb = blurb; PreviewPrefab = previewPrefab;
        }
    }

    // Helpers shared by every experience builder.
    // World units: the stage is ~3.0 x 2.0 (x -1.5..1.5, z -1..1), floor top at y = 0, the viewer is at -Z.
    public class ShowcaseBuildContext
    {
        public static readonly string[] PackFolders =
        {
            "Assets/KayKit", "Assets/ithappy", "Assets/Synty/PolygonNightclubs", "Assets/Synty/PolygonGeneric",
            "Assets/Cosmic_Retro_Mega_Bundle", "Assets/JMO Assets"
        };
        public const string MaterialsDir = "Assets/Showcase/Materials";

        private readonly Dictionary<string, string> m_prefabIndex = new Dictionary<string, string>();

        public ShowcaseBuildContext()
        {
            foreach (var folder in PackFolders.Where(AssetDatabase.IsValidFolder))
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    string file = Path.GetFileNameWithoutExtension(path);
                    if (!m_prefabIndex.ContainsKey(file)) m_prefabIndex[file] = path;
                }
        }

        public static void Log(string msg) => Debug.Log("[SHOWCASE-BUILD] " + msg);

        // Exact prefab file name (without .prefab) from any pack folder. Logs and returns null if missing.
        public GameObject Prefab(string name)
        {
            if (m_prefabIndex.TryGetValue(name, out var path))
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Log("MISSING prefab '" + name + "'");
            return null;
        }

        // All prefab names containing `fragment` (case-insensitive), for discovering props.
        public IEnumerable<string> FindPrefabNames(string fragment) =>
            m_prefabIndex.Keys.Where(k => k.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0).OrderBy(k => k);

        public static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        // Instantiates a prop (prefab link kept) under `parent`, scaled so its height is `height`
        // World units (or its largest side if fitLargestSide), standing on the parent's y = localPos.y,
        // centred at localPos.x/z, rotated yawDegrees (0 = facing the viewer).
        public GameObject Place(string prefabName, Transform parent, Vector3 localPos, float height, float yawDegrees = 0f, bool fitLargestSide = false)
        {
            var prefab = Prefab(prefabName);
            if (prefab == null) return null;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.name = prefabName;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0f, 180f + yawDegrees, 0f);
            go.transform.localScale = prefab.transform.localScale;
            var b = RendererBounds(go);
            float measure = fitLargestSide ? Mathf.Max(b.size.x, b.size.y, b.size.z) : b.size.y;
            float worldScale = parent.lossyScale.y;
            float k = height * worldScale / Mathf.Max(measure, 0.0001f);
            go.transform.localScale = prefab.transform.localScale * k;
            b = RendererBounds(go);
            Vector3 target = parent.TransformPoint(localPos);
            go.transform.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
            return go;
        }

        // Box collider around all meshes, in the object's local space.
        public static BoxCollider FitBoxCollider(GameObject go, float padding = 1f)
        {
            Bounds local = new Bounds();
            bool any = false;
            Matrix4x4 toLocal = go.transform.worldToLocalMatrix;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = m.MultiplyPoint3x4(c);
                    if (!any) { local = new Bounds(p, Vector3.zero); any = true; } else local.Encapsulate(p);
                }
            }
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var wb = smr.bounds;
                var p = toLocal.MultiplyPoint3x4(wb.center);
                if (!any) { local = new Bounds(p, Vector3.zero); any = true; }
                local.Encapsulate(toLocal.MultiplyPoint3x4(wb.min));
                local.Encapsulate(toLocal.MultiplyPoint3x4(wb.max));
            }
            var box = go.GetComponent<BoxCollider>() ?? go.AddComponent<BoxCollider>();
            if (any) { box.center = local.center; box.size = local.size * padding; }
            return box;
        }

        // A Flat Kit toon material (shared, saved under Assets/Showcase/Materials), styled like the packs.
        public Material ToonMaterial(string name, Color color)
        {
            Directory.CreateDirectory(MaterialsDir);
            string path = MaterialsDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("FlatKit/Stylized Surface") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            FlatKitConversion.Track(path, "Universal Render Pipeline/Lit");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // Primitive with a toon material. Keeps its collider unless keepCollider is false.
        public GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Color color, bool keepCollider = true)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            go.GetComponent<Renderer>().sharedMaterial = ToonMaterial("Toon " + ColorUtility.ToHtmlStringRGB(color), color);
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        // A standard stage floor (box, top at y = 0) for an experience; size in World units.
        public GameObject Floor(Transform root, Vector2 size, Color color)
        {
            return Primitive(PrimitiveType.Cube, "Floor", root, new Vector3(0f, -0.06f, 0f), new Vector3(size.x, 0.12f, size.y), color);
        }

        public GameObject FxPrefab(string name) => Prefab(name);

        // Sets private [SerializeField] fields on a component, e.g. Set(c, "m_items", array).
        public static void Set(UnityEngine.Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Log("No field '" + field + "' on " + target.GetType().Name); return; }
            switch (value)
            {
                case UnityEngine.Object o: p.objectReferenceValue = o; break;
                case float f: p.floatValue = f; break;
                case int i: p.intValue = i; break;
                case bool b: p.boolValue = b; break;
                case string s: p.stringValue = s; break;
                case Color c: p.colorValue = c; break;
                case Vector3 v: p.vector3Value = v; break;
                case UnityEngine.Object[] arr:
                    p.arraySize = arr.Length;
                    for (int k = 0; k < arr.Length; k++) p.GetArrayElementAtIndex(k).objectReferenceValue = arr[k];
                    break;
                default: Log("Unsupported value for '" + field + "'"); break;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
