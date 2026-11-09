using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Showcase.EditorTools
{
    // Batch play test of the Showcase scene. Enters Play mode, then for each experience
    // (or one, via -showcaseExperience <id>):
    //   open it from the hub -> screenshot -> tap every ITappable -> run SelfTest() ->
    //   DebugComplete() -> screenshot -> back to hub; also toggles AR tracking on/off.
    // Any exception or error log (other than known Zappar editor noise) fails the run.
    // Run with tools/unity-batch.sh ... --graphics --no-quit (the harness exits Unity itself):
    //   tools/unity-batch.sh Showcase.EditorTools.ShowcasePlayTest.Run playtest-<id> --graphics --no-quit -showcaseExperience <id|all>
    // Output: [SHOWCASE-PLAYTEST] lines, screenshots in Builds/Preview/<id>/play-*.png
    public static class ShowcasePlayTest
    {
        static string s_target;
        static List<ShowcaseExperience> s_queue;
        static int s_index;
        static double s_waitUntil;
        static int s_step;
        static readonly List<string> s_errors = new List<string>();
        static Coroutine s_selfTest;
        static bool s_selfTestDone;
        static ShowcaseExperience s_current;
        static double s_deadline;
        static bool s_prevOptionsEnabled;
        static EnterPlayModeOptions s_prevOptions;

        public static void Run()
        {
            s_target = ShowcaseSceneBuilder.ArgValue("-showcaseExperience") ?? "all";
            s_prevOptionsEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            s_prevOptions = EditorSettings.enterPlayModeOptions;
            // Skipping the domain reload keeps this driver's static state alive into Play mode.
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
            EditorSceneManager.OpenScene(ShowcaseSceneBuilder.ScenePath, OpenSceneMode.Single);
            Application.logMessageReceived += OnLog;
            s_step = 0;
            s_waitUntil = EditorApplication.timeSinceStartup + 2.0;
            s_deadline = EditorApplication.timeSinceStartup + 600;
            EditorApplication.update += Drive;
            EditorApplication.isPlaying = true;
            Log("starting play test for '" + s_target + "'");
        }

        static void Log(string msg) => Debug.Log("[SHOWCASE-PLAYTEST] " + msg);

        static void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
            // Known noise from Zappar's editor plugins in batch mode (no webcam / image trainer).
            if (message.Contains("ImageTraining") || message.Contains("zappar") || message.Contains("Zappar") || message.Contains("camera") && message.Contains("permission")) return;
            if (message.StartsWith("[SHOWCASE-PLAYTEST]")) return;
            s_errors.Add(type + ": " + message.Split('\n')[0] + (string.IsNullOrEmpty(stack) ? "" : " @ " + stack.Split('\n').FirstOrDefault(l => l.Contains("Assets/"))));
        }

        static void Wait(double seconds) => s_waitUntil = EditorApplication.timeSinceStartup + seconds;

        static void Drive()
        {
            if (EditorApplication.timeSinceStartup > s_deadline) { Fail("timed out"); return; }
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < s_waitUntil) return;
            try { Step(); }
            catch (Exception e) { s_errors.Add("Harness: " + e); Next(); }
        }

        static void Step()
        {
            var hub = ShowcaseHub.Instance;
            switch (s_step)
            {
                case 0:
                    if (ShowcaseRig.Instance == null || hub == null) { Fail("rig or hub missing in play mode"); return; }
                    Log("hub ok; free camera active: " + (ShowcaseRig.ActiveCamera != null && !ShowcaseRig.Instance.IsTracking));
                    Shot("hub", "play-hub");
                    if (s_target == "hub") { ShowcaseRig.Instance.SetTracking(true); ShowcaseRig.Instance.SetTracking(false); s_step = 6; Wait(0.5); return; }
                    s_queue = UnityEngine.Object.FindObjectsByType<ShowcaseExperience>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                        .Where(e => s_target == "all" || e.Id == s_target).OrderBy(e => e.transform.GetSiblingIndex()).ToList();
                    if (s_queue.Count == 0) { Fail("no experience matching '" + s_target + "'"); return; }
                    // AR toggle round trip: the free camera must come back.
                    ShowcaseRig.Instance.SetTracking(true);
                    ShowcaseRig.Instance.SetTracking(false);
                    s_index = 0; s_step = 1; Wait(0.3);
                    break;

                case 1: // open
                    s_current = s_queue[s_index];
                    Log("opening '" + s_current.Id + "'");
                    hub.Open(s_current);
                    s_step = 2; Wait(1.2);
                    break;

                case 2: // screenshot + tap everything
                    Shot(s_current.Id, "play-enter");
                    int taps = 0;
                    foreach (var t in s_current.GetComponentsInChildren<MonoBehaviour>(false).OfType<ITappable>().ToList())
                    {
                        var mb = (MonoBehaviour)t;
                        if (mb == null || !mb.isActiveAndEnabled) continue;
                        var col = mb.GetComponentInChildren<Collider>();
                        var hit = new RaycastHit { point = col != null ? col.bounds.center : mb.transform.position };
                        try { t.OnTap(hit); taps++; } catch (Exception e) { s_errors.Add("OnTap " + mb.name + ": " + e.Message); }
                    }
                    Log("tapped " + taps + " ITappables in '" + s_current.Id + "'");
                    WorldCard.CloseOpen();
                    if (ShowcaseHud.Instance != null) ShowcaseHud.Instance.HideResult();
                    s_step = 3; Wait(1.0);
                    break;

                case 3: // self test
                    s_selfTestDone = false;
                    var routine = Wrap(s_current.SelfTest());
                    // Run on the rig, not the experience: an experience may StopAllCoroutines() while resetting.
                    s_selfTest = ShowcaseRig.Instance.StartCoroutine(routine);
                    s_step = 4; Wait(0.2);
                    break;

                case 4:
                    if (!s_selfTestDone) return;
                    Shot(s_current.Id, "play-selftest");
                    try { s_current.DebugComplete(); } catch (Exception e) { s_errors.Add("DebugComplete: " + e.Message); }
                    s_step = 5; Wait(1.5);
                    break;

                case 5:
                    Shot(s_current.Id, "play-complete");
                    hub.ReturnToHub();
                    Log("closed '" + s_current.Id + "'; errors so far: " + s_errors.Count);
                    s_index++;
                    if (s_index < s_queue.Count) { s_step = 1; Wait(0.5); }
                    else { s_step = 6; Wait(0.5); }
                    break;

                case 6:
                    Finish();
                    break;
            }
        }

        static System.Collections.IEnumerator Wrap(System.Collections.IEnumerator inner)
        {
            var started = EditorApplication.timeSinceStartup;
            while (true)
            {
                object current;
                try
                {
                    if (!inner.MoveNext()) break;
                    current = inner.Current;
                }
                catch (Exception e) { s_errors.Add("SelfTest exception: " + e); break; }
                if (EditorApplication.timeSinceStartup - started > 120) { s_errors.Add("SelfTest took > 120 s"); break; }
                yield return current;
            }
            s_selfTestDone = true;
        }

        static void Next() { s_step = 5; Wait(0.5); }

        static void Shot(string id, string name)
        {
            var cam = ShowcaseRig.ActiveCamera;
            if (cam == null) return;
            string dir = "Builds/Preview/" + id;
            Directory.CreateDirectory(dir);
            var rt = new RenderTexture(720, 1280, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            // Frame for the portrait phone-sized target rather than the batch window.
            var free = cam.GetComponent<FreeCamera>();
            if (free != null) { free.SnapNow(); free.SnapNow(); }
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(dir + "/" + name + ".png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = prev;
            UnityEngine.Object.DestroyImmediate(rt);
            UnityEngine.Object.DestroyImmediate(tex);
        }

        static void Fail(string why)
        {
            s_errors.Add(why);
            Finish();
        }

        static void Finish()
        {
            EditorApplication.update -= Drive;
            Application.logMessageReceived -= OnLog;
            foreach (var e in s_errors.Distinct().Take(40)) Log("ERROR " + e);
            Log(s_errors.Count == 0 ? "PASS" : "FAIL (" + s_errors.Count + " errors)");
            int code = s_errors.Count == 0 ? 0 : 1;
            EditorApplication.isPlaying = false;
            EditorSettings.enterPlayModeOptionsEnabled = s_prevOptionsEnabled;
            EditorSettings.enterPlayModeOptions = s_prevOptions;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(code);
        }
    }
}
