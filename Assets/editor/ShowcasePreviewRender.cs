using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Renders the Graphics Validation playground to PNGs from a few angles without saving the
// scene, for checking layout outside AR. Batch (needs graphics, so no -nographics):
//   Unity -batchmode -projectPath . -executeMethod ShowcasePreviewRender.Render -quit
// Output: Builds/Preview/*.png
public static class ShowcasePreviewRender
{
    [MenuItem("Zappar/Showcase/Render Preview Images")]
    public static void Render()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/URP Basic.unity", OpenSceneMode.Single);
        var world = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == "World");
        world.gameObject.SetActive(true);
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) cam.enabled = false;

        System.IO.Directory.CreateDirectory("Builds/Preview");
        var go = new GameObject("Preview Camera");
        var c = go.AddComponent<Camera>();
        c.clearFlags = CameraClearFlags.SolidColor;
        c.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
        c.fieldOfView = 40f;
        c.nearClipPlane = 0.01f;

        Shot(c, world, "front", new Vector3(0f, 2.4f, -3.4f));
        Shot(c, world, "top", new Vector3(0f, 4.2f, -0.6f));
        Shot(c, world, "left", new Vector3(-2.6f, 1.6f, -2.2f));
        Shot(c, world, "closeup-back", new Vector3(0f, 1.0f, -1.2f));
        Object.DestroyImmediate(go);
        Debug.Log("[PREVIEW] done");
    }

    static void Shot(Camera c, Transform world, string name, Vector3 localPos)
    {
        c.transform.position = world.TransformPoint(localPos);
        c.transform.LookAt(world.TransformPoint(new Vector3(0f, 0.1f, name.StartsWith("closeup") ? 0.5f : 0f)));
        var rt = new RenderTexture(1600, 1000, 24) { antiAliasing = 4 };
        c.targetTexture = rt;
        // The first render can run before textures finish uploading and comes out white.
        c.Render();
        c.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        System.IO.File.WriteAllBytes("Builds/Preview/" + name + ".png", tex.EncodeToPNG());
        RenderTexture.active = null;
        c.targetTexture = null;
        Object.DestroyImmediate(rt);
        Debug.Log("[PREVIEW] " + name);
    }
}
