using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Converts the imported packs' opaque materials to Flat Kit's Stylized Surface (keeping each
// material's texture, colour, normal map and emission), styles them all from ShowcaseStyle,
// and sets up switchable full-screen looks (Flat Kit outline/pixelation, 4-colour palette).
// The original shader of every converted material is recorded so it can be reverted.
// Batch: -executeMethod FlatKitConversion.RunAll    Lines start with [FLATKIT].
public static class FlatKitConversion
{
    const string FlatKitShader = "FlatKit/Stylized Surface";
    const string StyleDir = "Assets/Showcase/Style";
    const string StylePath = StyleDir + "/ShowcaseStyle.asset";
    const string RampPath = StyleDir + "/CelSteps.png";
    const string RecordPath = StyleDir + "/FlatKitConversion.json";
    const string RendererPath = "Assets/Settings/URP-Showcase_Renderer.asset";
    const string ScenePath = "Assets/Scenes/URP Basic.unity";

    public const string OutlineFeature = "Outline";
    public const string PixelationFeature = "Pixelation";
    public const string PaletteFeature = "Palette 4-Colour";

    static readonly string[] Roots = { "Assets/KayKit", "Assets/Cosmic_Retro_Mega_Bundle", "Assets/ithappy", "Assets/Synty", "Assets/Showcase/Materials" };

    [Serializable] class Entry { public string path; public string originalShader; }
    [Serializable] class Record { public List<Entry> entries = new List<Entry>(); }

    static void Log(string msg) { Debug.Log("[FLATKIT] " + msg); }

    [MenuItem("Zappar/Showcase/Convert Materials To Flat Kit + Rebuild Scene")]
    public static void RunAll()
    {
        ConvertAll();
        SetupRenderEffects();
        ShowcaseValidationBuilder.Build();
        AddStyleSwitcher();
        AssetDatabase.SaveAssets();
    }

    // ---------- materials ----------

    struct Source
    {
        public Texture Tex; public Vector2 Tiling, Offset; public Color Color;
        public Texture Normal; public Texture EmissionMap; public Color Emission; public bool HasEmission;
        public bool AlphaClip; public float Cutoff;
    }

    static bool TryReadSource(Material m, out Source s)
    {
        s = new Source { Tiling = Vector2.one, Color = Color.white, Cutoff = 0.5f };
        string sh = m.shader.name;
        float F(string p, float d = 0f) => m.HasProperty(p) ? m.GetFloat(p) : d;
        Texture T(string p) => m.HasProperty(p) ? m.GetTexture(p) : null;
        Color C(string p, Color d) => m.HasProperty(p) ? m.GetColor(p) : d;

        if (F("_Surface") > 0.5f) return false;   // keep transparent materials (glass, decals) on their own shaders

        if (sh == "Universal Render Pipeline/Lit" || sh == "Universal Render Pipeline/Simple Lit")
        {
            s.Tex = T("_BaseMap"); s.Tiling = m.GetTextureScale("_BaseMap"); s.Offset = m.GetTextureOffset("_BaseMap");
            s.Color = C("_BaseColor", Color.white); s.Normal = T("_BumpMap");
            s.HasEmission = m.IsKeywordEnabled("_EMISSION"); s.EmissionMap = T("_EmissionMap"); s.Emission = C("_EmissionColor", Color.black);
            s.AlphaClip = F("_AlphaClip") > 0.5f; s.Cutoff = F("_Cutoff", 0.5f);
            return true;
        }
        if (sh == "Synty/Generic_Basic" || sh == "Synty/Generic_Standard" || sh == "Synty/Generic_Basic_Bloody")
        {
            s.Tex = T("_Albedo_Map"); s.Tiling = s.Tex != null ? m.GetTextureScale("_Albedo_Map") : Vector2.one;
            s.Color = C("_BaseColor", Color.white); s.Normal = T("_Normal_Map");
            s.HasEmission = F("_Enable_Emission") > 0.5f; s.EmissionMap = T("_Emission_Map"); s.Emission = C("_Emission_Color", Color.black);
            s.AlphaClip = F("_AlphaClip") > 0.5f; s.Cutoff = F("_Alpha_Clip_Threshold", 0.5f);
            return true;
        }
        if (sh == "Synty/Generic_Basic_Specular" || sh == "Synty/Generic_Basic_Specular_Bloody")
        {
            s.Tex = T("_Albedo_Map"); s.Color = C("_Base_Color", Color.white); s.Normal = T("_Normal_Map");
            s.HasEmission = F("_Enable_Emission") > 0.5f; s.EmissionMap = T("_Emission_Map"); s.Emission = C("_Emission_Color", Color.black);
            return true;
        }
        if (sh == "Synty/PolygonShader")
        {
            s.Tex = F("_Enable_Base_Texture") > 0.5f ? T("_Base_Texture") : null;
            if (m.HasProperty("_Base_Tiling")) { var v = m.GetVector("_Base_Tiling"); s.Tiling = new Vector2(v.x, v.y); }
            s.Color = C("_Color_Tint", Color.white);
            s.Normal = F("_Enable_Normal_Texture") > 0.5f ? T("_Normal_Texture") : null;
            s.HasEmission = F("_Enable_Emission_Texture") > 0.5f; s.EmissionMap = T("_Emission_Texture"); s.Emission = C("_Emission_Color_Tint", Color.black);
            return true;
        }
        return false;
    }

    static Record LoadRecord()
    {
        return File.Exists(RecordPath) ? JsonUtility.FromJson<Record>(File.ReadAllText(RecordPath)) : new Record();
    }

    static void SaveRecord(Record r)
    {
        Directory.CreateDirectory(StyleDir);
        File.WriteAllText(RecordPath, JsonUtility.ToJson(r, true));
    }

    [MenuItem("Zappar/Showcase/Convert Materials To Flat Kit (materials only)")]
    public static void ConvertAll()
    {
        var flat = Shader.Find(FlatKitShader);
        if (flat == null) { Log("ERROR: " + FlatKitShader + " not found. Is Flat Kit (URP) imported?"); return; }

        var record = LoadRecord();
        var known = new HashSet<string>(record.entries.Select(e => e.path));
        var skipped = new Dictionary<string, int>();
        int converted = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:Material", Roots.Where(AssetDatabase.IsValidFolder).ToArray()))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null || m.shader == null) continue;

            // Already converted: read the source values again from the original shader (Unity keeps
            // them in the material), so re-running the conversion repairs earlier results.
            var previous = record.entries.FirstOrDefault(e => e.path == path);
            if (m.shader.name == FlatKitShader)
            {
                var originalShader = previous != null ? Shader.Find(previous.originalShader) : null;
                if (originalShader == null) continue;
                m.shader = originalShader;
            }

            string original = m.shader.name;
            if (!TryReadSource(m, out var s))
            {
                skipped[original] = skipped.TryGetValue(original, out int n) ? n + 1 : 1;
                continue;
            }

            m.shader = flat;
            // Keywords and colours survive shader swaps; clear leftovers (e.g. a stale white
            // emission colour) so only what the source really uses is switched on.
            m.DisableKeyword("_EMISSION");
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_NORMALMAP");
            m.SetColor("_EmissionColor", Color.black);
            m.SetTexture("_EmissionMap", null);
            m.SetTexture("_BumpMap", null);
            m.SetFloat("_AlphaClip", 0f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
            m.SetTexture("_BaseMap", s.Tex);
            m.SetTextureScale("_BaseMap", s.Tiling);
            m.SetTextureOffset("_BaseMap", s.Offset);
            m.SetColor("_BaseColor", s.Color);
            m.SetFloat("_TextureBlendingMode", 0f);   // multiply
            m.SetFloat("_TextureImpact", 1f);
            if (s.Normal != null) { m.SetTexture("_BumpMap", s.Normal); m.EnableKeyword("_NORMALMAP"); }
            if (s.HasEmission)
            {
                m.SetTexture("_EmissionMap", s.EmissionMap);
                m.SetColor("_EmissionColor", s.Emission);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            if (s.AlphaClip)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", s.Cutoff);
                m.EnableKeyword("_ALPHATEST_ON");
            }
            if (s.Tex != null && m.GetTexture("_BaseMap") == null) Log("ERROR texture lost on " + path);

            if (!known.Contains(path)) { record.entries.Add(new Entry { path = path, originalShader = original }); known.Add(path); }
            EditorUtility.SetDirty(m);
            converted++;
        }

        SaveRecord(record);
        Log("Converted " + converted + " materials (" + record.entries.Count + " tracked in total)");
        foreach (var kv in skipped.OrderByDescending(k => k.Value)) Log("Kept on own shader: " + kv.Value + " x " + kv.Key);
        ApplyStyleToAll();
    }

    [MenuItem("Zappar/Showcase/Revert Flat Kit Materials To Original Shaders")]
    public static void RevertAll()
    {
        var record = LoadRecord();
        int n = 0;
        foreach (var e in record.entries)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(e.path);
            var sh = Shader.Find(e.originalShader);
            if (m == null || sh == null) continue;
            // Unity keeps the original shader's property values in the material, so swapping back restores them.
            m.shader = sh;
            EditorUtility.SetDirty(m);
            n++;
        }
        AssetDatabase.SaveAssets();
        File.Delete(RecordPath);
        Log("Reverted " + n + " materials");
    }

    // ---------- style ----------

    public static ShowcaseStyle LoadOrCreateStyle()
    {
        var style = AssetDatabase.LoadAssetAtPath<ShowcaseStyle>(StylePath);
        if (style != null) return style;
        Directory.CreateDirectory(StyleDir);
        style = ScriptableObject.CreateInstance<ShowcaseStyle>();
        AssetDatabase.CreateAsset(style, StylePath);
        return style;
    }

    // Flat Kit's Steps mode reads its bands from a ramp texture; generate one for the chosen step count.
    static Texture2D StepRamp(int steps)
    {
        Directory.CreateDirectory(StyleDir);
        var tex = new Texture2D(256, 4, TextureFormat.RGBA32, false);
        for (int x = 0; x < 256; x++)
        {
            float t = x / 255f;
            float v = Mathf.Min(Mathf.Floor(t * steps), steps - 1) / (steps - 1);
            for (int y = 0; y < 4; y++) tex.SetPixel(x, y, new Color(v, v, v, 1f));
        }
        File.WriteAllBytes(RampPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(RampPath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(RampPath);
        importer.sRGBTexture = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(RampPath);
    }

    // Registers a material created by a builder so ApplyStyleToAll keeps it in line, and styles it now.
    public static void Track(string path, string originalShader)
    {
        var record = LoadRecord();
        if (!record.entries.Any(e => e.path == path))
        {
            record.entries.Add(new Entry { path = path, originalShader = originalShader });
            SaveRecord(record);
        }
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null || m.shader == null || m.shader.name != FlatKitShader) return;
        var ramp = AssetDatabase.LoadAssetAtPath<Texture2D>(RampPath) ?? StepRamp(LoadOrCreateStyle().Steps);
        ApplyStyle(m, LoadOrCreateStyle(), ramp);
    }

    public static void ApplyStyleToAll()
    {
        var style = LoadOrCreateStyle();
        var ramp = StepRamp(style.Steps);
        int n = 0;
        foreach (var e in LoadRecord().entries)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(e.path);
            if (m == null || m.shader == null || m.shader.name != FlatKitShader) continue;
            ApplyStyle(m, style, ramp);
            n++;
        }
        AssetDatabase.SaveAssets();
        Log("Style applied to " + n + " materials (mode " + style.Mode + ", " + style.Steps + " steps)");
    }

    static void ApplyStyle(Material m, ShowcaseStyle st, Texture2D ramp)
    {
        Color baseColor = m.GetColor("_BaseColor");
        Color shade = new Color(baseColor.r * st.ShadeTint.r, baseColor.g * st.ShadeTint.g, baseColor.b * st.ShadeTint.b, 1f);

        m.SetFloat("_CelPrimaryMode", (float)st.Mode);
        m.SetColor("_ColorDim", shade);
        m.SetColor("_ColorDimSteps", shade);
        m.SetColor("_ColorDimCurve", shade);
        m.SetFloat("_CelNumSteps", st.Steps);
        m.SetTexture("_CelStepTexture", ramp);
        m.SetFloat("_SelfShadingSize", st.SelfShadingSize);
        m.SetFloat("_ShadowEdgeSize", st.ShadowEdgeSize);
        m.SetFloat("_Flatness", st.Flatness);

        m.SetFloat("_RimEnabled", st.Rim ? 1 : 0);
        m.SetColor("_FlatRimColor", st.RimColor);
        m.SetFloat("_FlatRimSize", st.RimSize);

        m.SetFloat("_SpecularEnabled", st.Specular ? 1 : 0);
        m.SetColor("_FlatSpecularColor", st.SpecularColor);
        m.SetFloat("_FlatSpecularSize", st.SpecularSize);

        m.SetFloat("_GradientEnabled", st.HeightGradient ? 1 : 0);
        m.SetColor("_ColorGradient", st.GradientColor);
        m.SetFloat("_GradientSize", st.GradientSize);

        m.SetFloat("_UnityShadowMode", st.ColoredShadows ? 2 : 1);   // KeywordEnum(None, Multiply, Color)
        m.SetColor("_UnityShadowColor", st.ShadowColor);
        m.SetFloat("_UnityShadowSharpness", st.ShadowSharpness);

        m.SetFloat("_OutlineEnabled", st.Outline ? 1 : 0);
        m.SetColor("_OutlineColor", st.OutlineColor);
        m.SetFloat("_OutlineWidth", st.OutlineWidth);

        // Flat Kit switches features with shader keywords driven by its KeywordEnum/Toggle
        // drawers; setting the floats alone would not change what renders.
        MaterialEditor.ApplyMaterialPropertyDrawers(m);
        EditorUtility.SetDirty(m);
    }

    // ---------- full-screen looks ----------

    public static void SetupRenderEffects()
    {
        var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        if (data == null) { Log("ERROR: renderer not found at " + RendererPath); return; }
        Directory.CreateDirectory(StyleDir);

        if (!HasFeature(data, OutlineFeature))
        {
            var settings = CreateOrLoad<FlatKit.OutlineSettings>(StyleDir + "/OutlineSettings.asset");
            settings.edgeColor = new Color(0.1f, 0.08f, 0.16f);
            settings.thickness = 2;
            settings.useDepth = true;
            settings.useColor = true;
            EditorUtility.SetDirty(settings);
            var f = ScriptableObject.CreateInstance<FlatKit.FlatKitOutline>();
            f.settings = settings;
            AddFeature(data, f, OutlineFeature);
        }
        if (!HasFeature(data, PixelationFeature))
        {
            var settings = CreateOrLoad<FlatKit.PixelationSettings>(StyleDir + "/PixelationSettings.asset");
            settings.resolution = 200;
            EditorUtility.SetDirty(settings);
            var f = ScriptableObject.CreateInstance<FlatKit.FlatKitPixelation>();
            f.settings = settings;
            AddFeature(data, f, PixelationFeature);
        }
        if (!HasFeature(data, PaletteFeature))
        {
            string matPath = StyleDir + "/Palette4.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Hidden/Showcase/Palette4"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            var f = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
            f.passMaterial = mat;
            f.injectionPoint = FullScreenPassRendererFeature.InjectionPoint.AfterRenderingPostProcessing;
            f.fetchColorBuffer = true;
            AddFeature(data, f, PaletteFeature);
        }
        AssetDatabase.SaveAssets();
        Log("Renderer features: " + string.Join(", ", data.rendererFeatures.Select(f => f.name + (f.isActive ? " (on)" : " (off)"))));
    }

    static T CreateOrLoad<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    static bool HasFeature(ScriptableRendererData data, string name) => data.rendererFeatures.Any(f => f != null && f.name == name);

    // Mirrors what the renderer's "Add Renderer Feature" button does: sub-asset, list entry and id map.
    static void AddFeature(ScriptableRendererData data, ScriptableRendererFeature feature, string name)
    {
        feature.name = name;
        feature.SetActive(false);
        AssetDatabase.AddObjectToAsset(feature, data);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

        var so = new SerializedObject(data);
        var list = so.FindProperty("m_RendererFeatures");
        var map = so.FindProperty("m_RendererFeatureMap");
        list.arraySize++;
        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = feature;
        map.arraySize++;
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        so.ApplyModifiedProperties();
        data.SetDirty();
        EditorUtility.SetDirty(data);
        Log("Added renderer feature '" + name + "' (off)");
    }

    // ---------- scene UI ----------

    static void AddStyleSwitcher()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var canvas = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (canvas == null) { Log("No Canvas; style switcher skipped"); return; }
        var old = canvas.transform.Find("Look Button");
        if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var go = new GameObject("Look Button", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        go.transform.SetParent(canvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 60f);
        rt.sizeDelta = new Vector2(560f, 110f);
        go.GetComponent<Image>().color = new Color(0.1f, 0.08f, 0.18f, 0.75f);

        var labelGo = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
        var label = labelGo.GetComponent<Text>();
        label.font = font; label.fontSize = 44; label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter; label.color = Color.white; label.text = "Look: Toon";

        var switcher = go.AddComponent<StyleSwitcher>();
        var so = new SerializedObject(switcher);
        so.FindProperty("m_renderer").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
        so.FindProperty("m_label").objectReferenceValue = label;
        var styles = new (string name, string[] features)[]
        {
            ("Toon", new string[0]),
            ("Toon + Outline", new[] { OutlineFeature }),
            ("4-Colour (Game Boy)", new[] { PaletteFeature }),
            ("Pixel Art", new[] { PixelationFeature, OutlineFeature }),
        };
        var arr = so.FindProperty("m_styles");
        arr.arraySize = styles.Length;
        for (int i = 0; i < styles.Length; i++)
        {
            var el = arr.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("Name").stringValue = styles[i].name;
            var fs = el.FindPropertyRelative("ActiveFeatures");
            fs.arraySize = styles[i].features.Length;
            for (int k = 0; k < styles[i].features.Length; k++) fs.GetArrayElementAtIndex(k).stringValue = styles[i].features[k];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        UnityEventTools.AddPersistentListener(go.GetComponent<Button>().onClick, switcher.Next);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log("Look button added (Toon / Toon + Outline / 4-Colour / Pixel Art)");
    }
}
