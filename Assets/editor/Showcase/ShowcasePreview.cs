using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Showcase.EditorTools
{
    // Renders the hub or one experience of the Showcase scene to PNGs (3D content only; runtime UI
    // such as the HUD and info cards is created in Play mode and does not appear).
    // Batch (needs graphics, so no -nographics):
    //   -executeMethod Showcase.EditorTools.ShowcasePreview.Render -showcaseExperience <id|hub>
    // Output: Builds/Preview/<id>/{front,top,left,close}.png
    public static class ShowcasePreview
    {
        public static void Render()
        {
            string id = ShowcaseSceneBuilder.ArgValue("-showcaseExperience") ?? "hub";
            EditorSceneManager.OpenScene(ShowcaseSceneBuilder.ScenePath, OpenSceneMode.Single);
            var world = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(t => t.name == "World");
            world.gameObject.SetActive(true);
            var hub = world.Find("Hub");
            var experiences = Object.FindObjectsByType<ShowcaseExperience>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var e in experiences) e.gameObject.SetActive(false);

            Vector3 focus = new Vector3(0f, 0.15f, 0f);
            float distance = 4.4f, pitch = 50f;
            if (id != "hub")
            {
                var exp = experiences.FirstOrDefault(e => e.Id == id);
                if (exp == null) { Debug.Log("[SHOWCASE-PREVIEW] ERROR: no experience '" + id + "'"); return; }
                if (hub != null) hub.gameObject.SetActive(false);
                exp.gameObject.SetActive(true);
                focus = exp.FocusPoint; distance = exp.FocusDistance; pitch = exp.FocusPitch;
            }
            foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None)) c.enabled = false;

            string dir = "Builds/Preview/" + id;
            Directory.CreateDirectory(dir);
            var go = new GameObject("Preview Camera");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.8f, 0.85f, 0.96f);
            cam.fieldOfView = 45f;
            cam.nearClipPlane = 0.02f;

            Shot(cam, world, focus, distance, pitch, 0f, dir + "/front.png");
            Shot(cam, world, focus, distance * 1.1f, 85f, 0f, dir + "/top.png");
            Shot(cam, world, focus, distance, 35f, -40f, dir + "/left.png");
            Shot(cam, world, focus, distance * 0.55f, 40f, 15f, dir + "/close.png");
            Object.DestroyImmediate(go);
            Debug.Log("[SHOWCASE-PREVIEW] wrote " + dir);
        }

        static void Shot(Camera cam, Transform world, Vector3 focus, float distance, float pitch, float yaw, string path)
        {
            var rot = world.rotation * Quaternion.Euler(pitch, yaw, 0f);
            cam.transform.rotation = rot;
            cam.transform.position = world.TransformPoint(focus) - rot * Vector3.forward * distance;
            var rt = new RenderTexture(1280, 800, 24) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            cam.Render();   // first render can run before textures finish uploading
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
