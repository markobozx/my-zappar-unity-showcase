using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// One-off Built-in -> URP switch for this project, run in batch mode:
//   -executeMethod UrpSetup.Run
// Re-running is safe: existing assets are reused. Every result line starts with [URPSETUP].
public static class UrpSetup
{
    const string SettingsDir = "Assets/Settings";
    const string AssetPath = SettingsDir + "/URP-Showcase.asset";
    const string RendererPath = SettingsDir + "/URP-Showcase_Renderer.asset";
    const string SourceScene = "Assets/Scenes/Image Tracking 3D Animation.unity";
    const string BasicScene = "Assets/Scenes/URP Basic.unity";
    const string MaterialsDir = "Assets/Showcase/Materials";

    static void Log(string msg) { Debug.Log("[URPSETUP] " + msg); }

    [MenuItem("Zappar/Showcase/Run URP Setup")]
    public static void Run()
    {
        var asset = CreateOrLoadPipelineAsset();
        AssignPipeline(asset);
        AddZapparSrpDefine();
        ReimportModels();
        SetupCameras(SourceScene);
        CreateBasicScene();
        SetBuildScenes();
        AssetDatabase.SaveAssets();
        Verify();
    }

    static UniversalRenderPipelineAsset CreateOrLoadPipelineAsset()
    {
        Directory.CreateDirectory(SettingsDir);
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(AssetPath);
        if (asset != null) { Log("Reusing " + AssetPath); return asset; }

        // CreateRendererAsset is internal; it is what the "URP Asset (with Universal Renderer)"
        // menu uses, and it fills in the default post-process data and shader resources.
        var create = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var renderer = (ScriptableRendererData)create.Invoke(null, new object[] { RendererPath, RendererType.UniversalRenderer, false, "Renderer" });

        asset = UniversalRenderPipelineAsset.Create(renderer);
        // Phone-friendly defaults for web AR.
        asset.supportsHDR = false;
        asset.msaaSampleCount = 2;
        asset.shadowDistance = 10f;
        asset.supportsCameraDepthTexture = false;
        asset.supportsCameraOpaqueTexture = false;
        AssetDatabase.CreateAsset(asset, AssetPath);
        Log("Created " + AssetPath + " and " + RendererPath);
        return asset;
    }

    static void AssignPipeline(UniversalRenderPipelineAsset asset)
    {
        GraphicsSettings.defaultRenderPipeline = asset;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        QualitySettings.SetQualityLevel(current, false);
        Log("Assigned URP asset to Graphics settings and " + QualitySettings.names.Length + " quality levels");
    }

    static void AddZapparSrpDefine()
    {
        foreach (var target in new[] { NamedBuildTarget.WebGL, NamedBuildTarget.Standalone, NamedBuildTarget.iOS, NamedBuildTarget.Android })
        {
            var symbols = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(s => s.Length > 0).ToList();
            if (!symbols.Contains("ZAPPAR_SRP")) symbols.Add("ZAPPAR_SRP");
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
        }
        Log("ZAPPAR_SRP define set for WebGL, Standalone, iOS, Android");
    }

    // Embedded FBX materials are generated at import time with the active pipeline's
    // default shader, so a re-import turns Standard into URP Lit.
    static void ReimportModels()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets" }))
            AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
        Log("Re-imported models");
    }

    // Zappar's SRP mode draws the camera feed after the camera with depth -1 renders, so the
    // background camera becomes the URP Base camera and the AR camera an Overlay on its stack.
    // This mirrors Zappar's own "Update Zappar Scene For SRP" menu item.
    static void SetupCameras(string scenePath)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        var ar = Object.FindFirstObjectByType<Zappar.ZapparCamera>();
        var bg = Object.FindFirstObjectByType<Zappar.ZapparCameraBackground>();
        if (ar == null || bg == null) { Log("ERROR: Zappar cameras not found in " + scenePath); return; }

        var arCam = ar.GetComponent<Camera>();
        var bgCam = bg.GetComponent<Camera>();
        var arData = arCam.GetUniversalAdditionalCameraData();
        var bgData = bgCam.GetUniversalAdditionalCameraData();
        arData.renderType = CameraRenderType.Overlay;
        bgData.renderType = CameraRenderType.Base;
        bgData.renderShadows = false;
        bgCam.depth = -1;
        bgCam.useOcclusionCulling = false;
        bgData.cameraStack.Clear();
        bgData.cameraStack.Add(arCam);
        EditorUtility.SetDirty(arCam.gameObject);
        EditorUtility.SetDirty(bgCam.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log("Camera stack set in " + scenePath + ": base=" + bgCam.name + " overlay=" + arCam.name);
    }

    static Material MakeMaterial(string name, Color color, float smoothness)
    {
        Directory.CreateDirectory(MaterialsDir);
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

    static void CreateBasicScene()
    {
        AssetDatabase.CopyAsset(SourceScene, BasicScene);
        var scene = EditorSceneManager.OpenScene(BasicScene, OpenSceneMode.Single);

        // Content under "Model" follows the existing seen/not-seen behaviour of the example.
        var model = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Model");
        if (model == null) { Log("ERROR: 'Model' not found in basic scene"); return; }

        var old = model.Find("URP Test");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        // Size the props relative to the character so they read well whatever its import scale.
        float height = 1f;
        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (smr != null && smr.sharedMesh != null)
            height = smr.sharedMesh.bounds.size.y * smr.transform.lossyScale.y / Mathf.Max(model.lossyScale.y, 0.0001f);
        float s = height * 0.18f;

        var root = new GameObject("URP Test").transform;
        root.SetParent(model, false);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "Floor";
        floor.transform.SetParent(root, false);
        floor.transform.localScale = new Vector3(height * 1.6f, s * 0.05f, height * 1.6f);
        floor.transform.localPosition = new Vector3(0f, -s * 0.05f, 0f);
        floor.GetComponent<Renderer>().sharedMaterial = MakeMaterial("Floor", new Color(0.92f, 0.9f, 0.86f), 0.2f);
        Object.DestroyImmediate(floor.GetComponent<Collider>());

        var props = new[]
        {
            (PrimitiveType.Cube, new Vector3(-0.55f, 0f, 0.25f), MakeMaterial("Coral", new Color(1f, 0.45f, 0.35f), 0.4f)),
            (PrimitiveType.Sphere, new Vector3(0.55f, 0f, 0.2f), MakeMaterial("Sky", new Color(0.35f, 0.75f, 1f), 0.8f)),
            (PrimitiveType.Capsule, new Vector3(0f, 0f, -0.5f), MakeMaterial("Sun", new Color(1f, 0.85f, 0.3f), 0.5f)),
        };
        foreach (var (type, pos, mat) in props)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = "Tap " + mat.name;
            go.transform.SetParent(root, false);
            go.transform.localScale = Vector3.one * s;
            float halfHeight = type == PrimitiveType.Capsule ? s : s * 0.5f;
            go.transform.localPosition = new Vector3(pos.x * height, halfHeight, pos.z * height);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            go.AddComponent<TapBounce>();
        }

        var light = Object.FindFirstObjectByType<Light>();
        if (light != null && light.type == LightType.Directional)
        {
            light.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(light);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log("Basic scene saved: " + BasicScene + " (prop size " + s.ToString("0.000") + ", character height " + height.ToString("0.000") + ")");
    }

    static void SetBuildScenes()
    {
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(BasicScene, true),
            new EditorBuildSettingsScene(SourceScene, true),
        };
        Log("Build scenes: " + BasicScene + ", " + SourceScene);
    }

    static void Verify()
    {
        var rp = GraphicsSettings.defaultRenderPipeline;
        Log("VERIFY graphics pipeline = " + (rp == null ? "NULL" : rp.GetType().Name + ":" + rp.name));
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            var a = QualitySettings.GetRenderPipelineAssetAt(i);
            Log("VERIFY quality " + i + " " + QualitySettings.names[i] + " = " + (a == null ? "inherits" : a.name));
        }
        var urp = rp as UniversalRenderPipelineAsset;
        if (urp != null)
            Log("VERIFY renderer count = " + urp.rendererDataList.Length + ", default renderer = " + (urp.rendererDataList.Length > 0 && urp.rendererDataList[0] != null ? urp.rendererDataList[0].name : "MISSING"));
        Log("VERIFY WebGL defines = " + PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.WebGL));

        foreach (var scenePath in new[] { SourceScene, BasicScene })
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var shaders = new HashSet<string>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                foreach (var m in r.sharedMaterials)
                    shaders.Add(m == null ? "(null material)" : m.name + " -> " + (m.shader == null ? "(null shader)" : m.shader.name));
            Log("VERIFY " + Path.GetFileName(scenePath) + " materials: " + string.Join(" | ", shaders));
            var bg = Object.FindFirstObjectByType<Zappar.ZapparCameraBackground>();
            if (bg != null)
            {
                var d = bg.GetComponent<Camera>().GetUniversalAdditionalCameraData();
                Log("VERIFY " + Path.GetFileName(scenePath) + " background camera: " + d.renderType + ", depth " + bg.GetComponent<Camera>().depth + ", stack " + string.Join(",", d.cameraStack.Select(c => c.name)));
            }
        }
    }
}
