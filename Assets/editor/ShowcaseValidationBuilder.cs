using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the "Graphics Validation" playground under World in the URP Basic scene:
// a row of props per imported pack (one scale factor per pack, so sizes inside a pack stay
// true to each other), a physics crate pyramid, tap-to-drop food and a collectible rain.
// Re-running replaces the previous playground. Batch: -executeMethod ShowcaseValidationBuilder.Build
public static class ShowcaseValidationBuilder
{
    const string ScenePath = "Assets/Scenes/URP Basic.unity";
    const string RootName = "Graphics Validation";
    const string MaterialsDir = "Assets/Showcase/Materials";

    class Pack
    {
        public string Label;
        public string Folder;
        public string[] Props;
        public float TargetHeight;   // median prop height after scaling, in World units
        public Vector3 Center;
        public float Width;
        public Color PadColor;
        public float Scale = 1f;     // computed
    }

    static readonly Pack[] Packs =
    {
        new Pack { Label = "ithappy · Cute Furniture", Folder = "Assets/ithappy", Props = new[] { "Couch_01", "Armchair_01", "Coffee_Table_01", "Plants_01", "Light_01", "Toy_01" },
                   TargetHeight = 0.3f, Center = new Vector3(-0.78f, 0f, 0.55f), Width = 1.35f, PadColor = new Color(1f, 0.86f, 0.86f) },
        new Pack { Label = "Synty · Nightclub", Folder = "Assets/Synty/PolygonNightclubs", Props = new[] { "SM_Prop_DJ_Booth_01", "SM_Prop_DJ_Turntable_02", "SM_Prop_Stool_01", "SM_Prop_Sign_Flamingo_01", "SM_Prop_Bar_Bottle_02", "SM_Prop_Light_Stage_05" },
                   TargetHeight = 0.3f, Center = new Vector3(0.78f, 0f, 0.55f), Width = 1.35f, PadColor = new Color(0.82f, 0.8f, 1f) },
        new Pack { Label = "Cosmic Retro · Sci-Fi", Folder = "Assets/Cosmic_Retro_Mega_Bundle", Props = new[] { "CR_Sofa_TwoSeat", "CR_Computer_WithMonitor", "CR_Locker_Emergency_Red", "CR_Radio", "CR_StorageCrate_Large_1", "CR_FireExtinguisher" },
                   TargetHeight = 0.3f, Center = new Vector3(-1.05f, 0f, -0.5f), Width = 0.95f, PadColor = new Color(0.8f, 0.95f, 0.92f) },
        new Pack { Label = "KayKit · Restaurant", Folder = "Assets/KayKit", Props = new[] { "kitchentable_B", "stove_single_countertop", "crate_ham", "food_burger", "food_pizza_pepperoni_slice", "food_icecream_cone_vanilla" },
                   TargetHeight = 0.2f, Center = new Vector3(0f, 0f, -0.5f), Width = 0.95f, PadColor = new Color(1f, 0.94f, 0.8f) },
        new Pack { Label = "KayKit · Resources", Folder = "Assets/KayKit", Props = new[] { "Containers_Crate_Medium_Wood", "Fuel_A_Barrels", "Wood_Log_A", "Gems_Pile_Large", "Gold_Bar", "Food_Basket_A_Berries" },
                   TargetHeight = 0.18f, Center = new Vector3(1.05f, 0f, -0.5f), Width = 0.95f, PadColor = new Color(0.86f, 0.95f, 0.8f) },
    };

    static readonly string[] DropProps = { "food_burger", "food_pizza_pepperoni_slice", "food_icecream_cone_vanilla", "food_dinner", "food_stew" };
    static readonly string[] CollectibleProps = { "Gold_Bar", "Money_Coins_Stack_Single", "Gems_Pile_Small", "Silver_Bars", "Money_Bill" };

    static void Log(string msg) { Debug.Log("[SHOWCASE] " + msg); }

    [MenuItem("Zappar/Showcase/Rebuild Graphics Validation Scene")]
    public static void Build()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var world = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "World");
        if (world == null) { Log("ERROR: no 'World' object in " + ScenePath); return; }

        var old = world.Find(RootName);
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var urpTest = world.Find("URP Test");
        if (urpTest != null) urpTest.gameObject.SetActive(false);

        var root = new GameObject(RootName).transform;
        root.SetParent(world, false);

        BuildStage(root);
        foreach (var pack in Packs) BuildPack(root, pack);
        var pyramid = BuildPyramid(root);
        var score = BuildScoreText();
        ConfigureController(root.gameObject, pyramid, score);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log("Saved " + ScenePath);
    }

    // ---------- helpers ----------

    static readonly Dictionary<string, Dictionary<string, string>> s_prefabIndex = new Dictionary<string, Dictionary<string, string>>();

    // Match on exact file name; search strings with brackets or '+' (FX names) confuse FindAssets.
    static GameObject FindPrefab(string name, string folder)
    {
        if (!s_prefabIndex.TryGetValue(folder, out var index))
        {
            index = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!index.ContainsKey(file)) index[file] = path;
            }
            s_prefabIndex[folder] = index;
        }
        if (index.TryGetValue(name, out var found))
            return AssetDatabase.LoadAssetAtPath<GameObject>(found);
        Log("MISSING prefab '" + name + "' in " + folder);
        return null;
    }

    static GameObject FindFx(string name) => FindPrefab(name, "Assets/JMO Assets");

    static Bounds RendererBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Material MakeMaterial(string name, Color color, float smoothness)
    {
        System.IO.Directory.CreateDirectory(MaterialsDir);
        string path = MaterialsDir + "/" + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject Block(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat, PrimitiveType type = PrimitiveType.Cube)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // ---------- stage ----------

    static void BuildStage(Transform root)
    {
        // Thick box so fast-falling items can't tunnel through; its top sits at y = 0.
        Block("Stage", root, new Vector3(0f, -0.06f, 0f), new Vector3(3.3f, 0.12f, 2.3f), MakeMaterial("Stage", new Color(0.93f, 0.93f, 0.95f), 0.25f));
        // Low rim so rolling items mostly stay on the stage.
        var rim = MakeMaterial("Stage Rim", new Color(0.55f, 0.6f, 0.75f), 0.4f);
        Block("Rim Back", root, new Vector3(0f, 0.03f, 1.16f), new Vector3(3.3f, 0.06f, 0.04f), rim);
        Block("Rim Front", root, new Vector3(0f, 0.03f, -1.16f), new Vector3(3.3f, 0.06f, 0.04f), rim);
        Block("Rim Left", root, new Vector3(-1.66f, 0.03f, 0f), new Vector3(0.04f, 0.06f, 2.36f), rim);
        Block("Rim Right", root, new Vector3(1.66f, 0.03f, 0f), new Vector3(0.04f, 0.06f, 2.36f), rim);
    }

    // ---------- packs ----------

    static void BuildPack(Transform root, Pack pack)
    {
        var zone = new GameObject("Zone - " + pack.Label).transform;
        zone.SetParent(root, false);
        zone.localPosition = pack.Center;

        var pad = Block("Pad", zone, new Vector3(0f, 0.004f, 0f), new Vector3(pack.Width, 0.008f, 0.62f), MakeMaterial("Pad " + pack.Label.Split('·')[1].Trim(), pack.PadColor, 0.15f));
        Object.DestroyImmediate(pad.GetComponent<Collider>());

        // Measure every prop at prefab scale (facing the viewer) to choose one scale factor for the whole pack.
        var prefabs = pack.Props.Select(p => FindPrefab(p, pack.Folder)).Where(p => p != null).ToList();
        var sizes = new List<Vector3>();
        foreach (var prefab in prefabs)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            probe.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            var s = RendererBounds(probe).size;
            sizes.Add(new Vector3(Mathf.Max(s.x, 0.001f), Mathf.Max(s.y, 0.001f), Mathf.Max(s.z, 0.001f)));
            Object.DestroyImmediate(probe);
        }
        if (sizes.Count == 0) return;
        var heights = sizes.Select(v => v.y).OrderBy(h => h).ToList();
        pack.Scale = pack.TargetHeight / heights[heights.Count / 2];
        // Shrink the whole pack (never individual props) until it fits its pad and the tallest prop is capped.
        const float gap = 0.04f, padDepthForProps = 0.46f, maxHeight = 0.5f;
        pack.Scale = Mathf.Min(pack.Scale, maxHeight / heights[heights.Count - 1]);
        while (pack.Scale > 0.0001f && !Fits(sizes, pack.Scale, pack.Width - 0.08f, padDepthForProps, gap))
            pack.Scale *= 0.92f;

        // Lay the props out from the back of the pad toward the viewer, wrapping rows as needed.
        float left = -pack.Width / 2f + 0.04f;
        float x = left, z = 0.27f, rowDepth = 0f;
        for (int i = 0; i < prefabs.Count; i++)
        {
            var prefab = prefabs[i];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, zone);
            go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // face the viewer (-Z)
            go.transform.localScale = prefab.transform.localScale * pack.Scale;
            go.transform.localPosition = Vector3.zero;
            var b = RendererBounds(go);
            float w = b.size.x, d = b.size.z;
            if (x + w > pack.Width / 2f - 0.04f && x > left)
            {
                x = left;
                z -= rowDepth + gap;
                rowDepth = 0f;
            }
            // Sit the prop on the pad with its back edge on the current row line.
            Vector3 worldOffset = zone.TransformPoint(new Vector3(x + w / 2f, 0.008f, z - d / 2f)) - new Vector3(b.center.x, b.min.y, b.center.z);
            go.transform.position += worldOffset;
            x += w + gap;
            rowDepth = Mathf.Max(rowDepth, d);

            ShowcaseController.FitBoxCollider(go);
            go.AddComponent<ShowcaseProp>();
        }

        var label = new GameObject("Label");
        label.transform.SetParent(zone, false);
        label.transform.localPosition = new Vector3(0f, 0.02f, -0.36f);
        label.transform.localRotation = Quaternion.Euler(70f, 0f, 0f);
        var text = label.AddComponent<TextMesh>();
        text.text = pack.Label + "  ×" + pack.Scale.ToString("0.###");
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.GetComponent<MeshRenderer>().sharedMaterial = text.font.material;
        text.fontSize = 64;
        text.characterSize = 0.006f;
        text.anchor = TextAnchor.MiddleCenter;
        text.color = new Color(0.2f, 0.22f, 0.3f);

        Log(pack.Label + ": " + prefabs.Count + " props, scale x" + pack.Scale.ToString("0.####") + " (median prefab height " + heights[heights.Count / 2].ToString("0.###") + ")");
    }

    // Same row-wrapping as the layout above, without placing anything.
    static bool Fits(List<Vector3> sizes, float scale, float width, float depth, float gap)
    {
        float x = 0f, used = 0f, rowDepth = 0f;
        foreach (var s in sizes)
        {
            float w = s.x * scale, d = s.z * scale;
            if (w > width) return false;
            if (x > 0f && x + w > width)
            {
                used += rowDepth + gap;
                x = 0f;
                rowDepth = 0f;
            }
            x += w + gap;
            rowDepth = Mathf.Max(rowDepth, d);
        }
        return used + rowDepth <= depth;
    }

    // ---------- physics ----------

    static Rigidbody[] BuildPyramid(Transform root)
    {
        var resources = Packs.First(p => p.Label.EndsWith("Resources"));
        var prefab = FindPrefab("Containers_Box_Small", "Assets/KayKit");
        var bodies = new List<Rigidbody>();
        if (prefab == null) return bodies.ToArray();

        var group = new GameObject("Crate Pyramid (tap me)").transform;
        group.SetParent(root, false);
        group.localPosition = new Vector3(0f, 0f, 0.02f);

        // Crates get a fixed size so the pyramid stays a comfortable tap target on a phone.
        var probe = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        float crateScale = 0.09f / Mathf.Max(RendererBounds(probe).size.y, 0.001f);
        probe.transform.localScale = prefab.transform.localScale * crateScale;
        var size = RendererBounds(probe).size;
        Object.DestroyImmediate(probe);

        float gap = 0.004f;
        int[] layers = { 4, 3, 2, 1 };
        for (int layer = 0; layer < layers.Length; layer++)
        {
            for (int i = 0; i < layers[layer]; i++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group);
                go.transform.localScale = prefab.transform.localScale * crateScale;
                go.transform.localPosition = Vector3.zero;
                var b = RendererBounds(go);
                float px = (i - (layers[layer] - 1) / 2f) * (size.x + gap);
                float py = layer * (size.y + gap);
                Vector3 target = group.TransformPoint(new Vector3(px, py, 0f));
                go.transform.position += target - new Vector3(b.center.x, b.min.y, b.center.z);
                ShowcaseController.FitBoxCollider(go);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                bodies.Add(rb);
            }
        }
        Log("Pyramid: " + bodies.Count + " crates");
        return bodies.ToArray();
    }

    // ---------- UI ----------

    static Text BuildScoreText()
    {
        var canvas = Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (canvas == null) { Log("No Canvas, score text skipped"); return null; }
        var old = canvas.transform.Find("Score");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var go = new GameObject("Score", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
        go.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -40f);
        rt.sizeDelta = new Vector2(700f, 90f);
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 56;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = "Collected: 0";
        go.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
        return text;
    }

    static void ConfigureController(GameObject root, Rigidbody[] pyramid, Text score)
    {
        var restaurant = Packs.First(p => p.Label.EndsWith("Restaurant"));
        var resources = Packs.First(p => p.Label.EndsWith("Resources"));
        var c = root.AddComponent<ShowcaseController>();
        var so = new SerializedObject(c);
        so.FindProperty("m_hitFx").objectReferenceValue = FindFx("CFXR Hit A (Red)");
        so.FindProperty("m_poofFx").objectReferenceValue = FindFx("CFXR Magic Poof");
        so.FindProperty("m_pickupFx").objectReferenceValue = FindFx("CFXR2 Pickup Diamond (+ Stars)");
        so.FindProperty("m_sparkleFx").objectReferenceValue = FindFx("CFXR2 Magic Stars");
        so.FindProperty("m_fxScale").floatValue = 0.25f;

        SetArray(so.FindProperty("m_dropPrefabs"), DropProps.Select(p => FindPrefab(p, "Assets/KayKit")).Where(p => p != null).ToArray());
        so.FindProperty("m_dropScale").floatValue = restaurant.Scale * 2f;
        SetArray(so.FindProperty("m_collectiblePrefabs"), CollectibleProps.Select(p => FindPrefab(p, "Assets/KayKit")).Where(p => p != null).ToArray());
        so.FindProperty("m_collectibleScale").floatValue = resources.Scale * 2.5f;

        var py = so.FindProperty("m_pyramid");
        py.arraySize = pyramid.Length;
        for (int i = 0; i < pyramid.Length; i++) py.GetArrayElementAtIndex(i).objectReferenceValue = pyramid[i];
        so.FindProperty("m_scoreText").objectReferenceValue = score;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetArray(SerializedProperty prop, Object[] values)
    {
        prop.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
