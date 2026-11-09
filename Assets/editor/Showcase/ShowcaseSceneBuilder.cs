using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Showcase.EditorTools
{
    // Builds Assets/Scenes/Showcase.unity: the Zappar rig with the AR/free camera switch, the hub
    // of 10 pedestals and every experience found via [ShowcaseExperienceBuilder].
    //
    // Batch:
    //   -executeMethod Showcase.EditorTools.ShowcaseSceneBuilder.BuildAll
    //   -executeMethod Showcase.EditorTools.ShowcaseSceneBuilder.BuildOne -showcaseExperience <id>
    // Log lines start with [SHOWCASE-BUILD].
    public static class ShowcaseSceneBuilder
    {
        public const string SourceScene = "Assets/Scenes/URP Basic.unity";
        public const string ScenePath = "Assets/Scenes/Showcase.unity";
        const string FxLibraryPath = "Assets/Showcase/ShowcaseFxLibrary.asset";
        // A target = <targetsFolder>/<id>.zpt + <targetImagesFolder>/<id>.png (folders from the Zappar CLI
        // settings; ZapparTargets trains and places them), shown on the scan screen.
        static string TargetImagesFolder => ZapparCliTools.ZapparCliSettings.Instance.targetImagesFolder;
        static string ZptFolder => ZapparCliTools.ZapparCliSettings.Instance.targetsFolder;
        // Committed in the repo; the built app re-reads it from GitHub at startup, so editing it on
        // github.com changes which experience each target opens without a new build.
        public const string RemoteConfigFile = "RemoteConfig/showcase-config.json";
        public const string RemoteConfigUrl = "https://raw.githubusercontent.com/markobozx/my-zappar-unity-showcase/urp-migration/" + RemoteConfigFile;

        // Pedestal defaults, used until an experience's builder exists.
        static readonly (string id, string title, string blurb, string prop)[] Slots =
        {
            ("diner", "Pop-Up Diner Rush", "Build burgers and pizzas\nbefore the timer runs out.", "food_burger"),
            ("room", "Room Makeover", "Swap furniture and colours.\nSee prices and details.", "Armchair_01"),
            ("nightclub", "Hidden-Object Nightclub", "Find 8 hidden items\nwith your flashlight.", "SM_Prop_DJ_Turntable_02"),
            ("station", "Space Station Escape", "Solve 4 puzzles\nto open the airlock.", "CR_Radio"),
            ("treasure", "Treasure Rain", "Catch gold and gems\nin 45 seconds.", "Gold_Bar"),
            ("lab", "Little Lab", "Mix 3 ingredients to find\nthe hero formula.", "CR_FireExtinguisher"),
            ("stack", "Stack the Delivery", "Build the tallest tower\nbefore the truck leaves.", "Containers_Crate_Medium_Wood"),
            ("looks", "Look Switcher Gallery", "Restyle the world and\nfind hidden objects.", "Plants_01"),
            ("detective", "Kitchen Detective", "Who ate the last ice cream?\nFind 4 clues.", "food_icecream_cone_vanilla"),
            ("barcart", "Build Your Bar Cart", "Mix a drink and get\nthe recipe card.", "SM_Prop_Bar_Bottle_02"),
        };

        static readonly Color[] PedestalColors =
        {
            new Color(1f, 0.55f, 0.45f), new Color(1f, 0.78f, 0.4f), new Color(0.62f, 0.5f, 1f), new Color(0.4f, 0.8f, 0.85f), new Color(1f, 0.85f, 0.35f),
            new Color(0.55f, 0.85f, 0.55f), new Color(0.85f, 0.62f, 0.45f), new Color(0.95f, 0.55f, 0.8f), new Color(0.5f, 0.7f, 1f), new Color(0.75f, 0.55f, 0.9f),
        };

        static void Log(string msg) => ShowcaseBuildContext.Log(msg);

        [MenuItem("Zappar/Showcase/Build Showcase Scene (all)")]
        public static void BuildAll() => Build(null);

        // Rebuilds one experience (id from -showcaseExperience) and re-links the hub.
        public static void BuildOne()
        {
            string id = ArgValue("-showcaseExperience");
            if (string.IsNullOrEmpty(id)) { Log("ERROR: pass -showcaseExperience <id>"); return; }
            Build(id);
        }

        public static string ArgValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static List<(ShowcaseExperienceBuilderAttribute attr, MethodInfo build)> FindBuilders()
        {
            var list = new List<(ShowcaseExperienceBuilderAttribute, MethodInfo)>();
            foreach (var type in TypeCache.GetTypesWithAttribute<ShowcaseExperienceBuilderAttribute>())
            {
                var attr = type.GetCustomAttribute<ShowcaseExperienceBuilderAttribute>();
                var m = type.GetMethod("Build", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ShowcaseBuildContext), typeof(Transform) }, null);
                if (m == null) { Log("ERROR: " + type.Name + " has no public static Build(ShowcaseBuildContext, Transform)"); continue; }
                list.Add((attr, m));
            }
            return list.OrderBy(b => b.Item1.Order).ToList();
        }

        static void Build(string onlyId)
        {
            bool fresh = !File.Exists(ScenePath);
            if (fresh) AssetDatabase.CopyAsset(SourceScene, ScenePath);
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var ctx = new ShowcaseBuildContext();

            var world = FindAll<Transform>().FirstOrDefault(t => t.name == "World");
            if (world == null) { Log("ERROR: no World in " + ScenePath); return; }
            world.gameObject.SetActive(true);

            var experiencesRoot = world.Find("Experiences") ?? new GameObject("Experiences").transform;
            experiencesRoot.SetParent(world, false);

            if (onlyId == null)
            {
                SetupRig(world);
                foreach (Transform child in world.Cast<Transform>().ToList())
                    if (child.name != "Experiences") UnityEngine.Object.DestroyImmediate(child.gameObject);
            }

            var builders = FindBuilders();
            var experiences = new Dictionary<string, ShowcaseExperience>();
            foreach (var (attr, build) in builders)
            {
                string rootName = attr.Order.ToString("00") + " " + attr.Title;
                var existing = experiencesRoot.Cast<Transform>().FirstOrDefault(t => t.name == rootName);
                if (onlyId != null && attr.Id != onlyId)
                {
                    if (existing != null) experiences[attr.Id] = existing.GetComponent<ShowcaseExperience>();
                    continue;
                }
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);
                var root = new GameObject(rootName).transform;
                root.SetParent(experiencesRoot, false);
                try
                {
                    build.Invoke(null, new object[] { ctx, root });
                    var exp = root.GetComponent<ShowcaseExperience>();
                    if (exp == null) Log("ERROR: builder for '" + attr.Id + "' did not add a ShowcaseExperience component");
                    else
                    {
                        exp.Id = attr.Id;
                        if (string.IsNullOrEmpty(exp.Title)) exp.Title = attr.Title;
                        experiences[attr.Id] = exp;
                    }
                    Log("Built experience '" + attr.Id + "' (" + root.GetComponentsInChildren<Renderer>(true).Length + " renderers)");
                }
                catch (Exception e)
                {
                    Log("ERROR building '" + attr.Id + "': " + (e.InnerException ?? e));
                }
                root.gameObject.SetActive(false);
            }
            // Keep experience roots in hub order.
            foreach (var t in experiencesRoot.Cast<Transform>().OrderBy(t => t.name).ToList()) t.SetAsLastSibling();

            BuildHub(ctx, world, builders, experiences);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Log("Saved " + ScenePath + (onlyId != null ? " (rebuilt '" + onlyId + "')" : " (full build)"));
        }

        static IEnumerable<T> FindAll<T>() where T : UnityEngine.Object =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // ---------- rig ----------

        static void SetupRig(Transform world)
        {
            var arCam = FindAll<Zappar.ZapparCamera>().FirstOrDefault()?.GetComponent<Camera>();
            var bgCam = FindAll<Zappar.ZapparCameraBackground>().FirstOrDefault()?.GetComponent<Camera>();

            var old = FindAll<ShowcaseRig>().FirstOrDefault();
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var oldFree = FindAll<FreeCamera>().FirstOrDefault();
            if (oldFree != null) UnityEngine.Object.DestroyImmediate(oldFree.gameObject);

            var freeGo = new GameObject("Free Camera");
            var free = freeGo.AddComponent<Camera>();
            free.clearFlags = CameraClearFlags.SolidColor;
            free.backgroundColor = new Color(0.8f, 0.85f, 0.96f);
            free.fieldOfView = 45f;
            free.nearClipPlane = 0.02f;
            free.farClipPlane = 60f;
            free.depth = 5;
            var data = free.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.renderPostProcessing = false;
            var fc = freeGo.AddComponent<FreeCamera>();
            ShowcaseBuildContext.Set(fc, "m_pivotSpace", world);

            var rigGo = new GameObject("Showcase Rig");
            var rig = rigGo.AddComponent<ShowcaseRig>();
            rigGo.AddComponent<ShowcaseInput>();
            rigGo.AddComponent<ShowcaseHud>();
            ShowcaseBuildContext.Set(rig, "m_arCamera", arCam);
            ShowcaseBuildContext.Set(rig, "m_arBackgroundCamera", bgCam);
            ShowcaseBuildContext.Set(rig, "m_freeCamera", free);
            ShowcaseBuildContext.Set(rig, "m_world", world);
            ShowcaseBuildContext.Set(rig, "m_fx", FxLibrary());

            SetupTargets(rigGo, world, arCam);

            // The old example canvas: keep only the Look button, small, bottom-right.
            var canvas = FindAll<Canvas>().FirstOrDefault(c => c.name == "Canvas");
            if (canvas != null)
            {
                foreach (Transform child in canvas.transform.Cast<Transform>().ToList())
                    if (child.name != "Look Button") UnityEngine.Object.DestroyImmediate(child.gameObject);
                var look = canvas.transform.Find("Look Button") as RectTransform;
                if (look != null)
                {
                    look.anchorMin = look.anchorMax = look.pivot = new Vector2(1f, 0f);
                    look.anchoredPosition = new Vector2(-30f, 40f);
                    look.sizeDelta = new Vector2(380f, 90f);
                    var label = look.GetComponentInChildren<UnityEngine.UI.Text>();
                    if (label != null) label.fontSize = 32;
                }
                var scaler = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(1080, 1920);
                    scaler.matchWidthOrHeight = 0.5f;
                }
            }
            // UI clicks (HUD, world cards, keypads) need exactly one EventSystem; the example scene kept
            // it under the old canvas, which is cleared above, so make a root-level one.
            foreach (var es in FindAll<UnityEngine.EventSystems.EventSystem>().ToList())
                UnityEngine.Object.DestroyImmediate(es.gameObject);
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));

            Log("Rig set up: AR camera " + (arCam != null) + ", background " + (bgCam != null));
        }

        // One Zappar tracker per target image, all routed through ShowcaseTargets.
        static void SetupTargets(GameObject rigGo, Transform world, Camera arCam)
        {
            var trackers = FindAll<Zappar.ZapparImageTrackingTarget>().ToList();
            var original = trackers.FirstOrDefault();
            var defaults = File.Exists(RemoteConfigFile) ? JsonUtility.FromJson<ShowcaseRemoteConfig>(File.ReadAllText(RemoteConfigFile)) : null;

            var targets = new List<ShowcaseTarget>();
            var images = Directory.Exists(TargetImagesFolder)
                ? Directory.GetFiles(TargetImagesFolder).Where(f => f.EndsWith(".png") || f.EndsWith(".jpg") || f.EndsWith(".jpeg")).OrderBy(f => f).ToList()
                : new List<string>();
            foreach (var imagePath in images)
            {
                string id = Path.GetFileNameWithoutExtension(imagePath);
                string zpt = id + ".zpt";
                if (!File.Exists(Path.Combine(ZptFolder, zpt))) { Log("WARNING: target image " + imagePath + " has no " + zpt + " in " + ZptFolder + "; skipped"); continue; }

                var importer = (TextureImporter)AssetImporter.GetAtPath(imagePath.Replace('\\', '/'));
                if (importer != null && (importer.maxTextureSize != 512 || importer.mipmapEnabled || importer.npotScale != TextureImporterNPOTScale.None))
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.maxTextureSize = 512;
                    importer.mipmapEnabled = false;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }

                var tracker = trackers.FirstOrDefault(t => t.Target == zpt);
                if (tracker == null)
                {
                    var go = new GameObject("Image Target - " + id);
                    if (original != null) go.transform.SetParent(original.transform.parent, false);
                    tracker = go.AddComponent<Zappar.ZapparImageTrackingTarget>();
                    tracker.Target = zpt;
                    trackers.Add(tracker);
                }
                string exp = ShowcaseTargets.HubId;
                if (defaults != null)
                {
                    if (!string.IsNullOrEmpty(defaults.defaultExperience)) exp = defaults.defaultExperience;
                    var own = defaults.targets?.FirstOrDefault(t => t != null && t.id == id);
                    if (own != null && !string.IsNullOrEmpty(own.experience)) exp = own.experience;
                }
                targets.Add(new ShowcaseTarget { Id = id, Experience = exp, Image = AssetDatabase.LoadAssetAtPath<Texture2D>(imagePath), Tracker = tracker });
            }

            if (targets.Count == 0) { Log("ERROR: no image targets (need " + TargetImagesFolder + "/<name>.png + " + ZptFolder + "/<name>.zpt)"); return; }
            if (targets.Count > 4) Log("WARNING: " + targets.Count + " targets: every tracker runs each frame, so older phones may slow down");

            var router = rigGo.AddComponent<ShowcaseTargets>();
            router.Configure(targets, RemoteConfigUrl, RemoteConfigFile);
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i].Tracker;
                ClearPersistent(t, "OnSeenEvent");
                ClearPersistent(t, "OnNotSeenEvent");
                UnityEventTools.AddIntPersistentListener(t.OnSeenEvent, router.OnSeen, i);
                UnityEventTools.AddIntPersistentListener(t.OnNotSeenEvent, router.OnLost, i);
                EditorUtility.SetDirty(t);
            }
            EditorUtility.SetDirty(router);

            // Start anchored on the first target; ShowcaseTargets moves World when another one is seen.
            Transform content = world;
            while (content.parent != null && content.parent.GetComponent<Zappar.ZapparImageTrackingTarget>() == null) content = content.parent;
            if (content.parent != targets[0].Tracker.transform) content.SetParent(targets[0].Tracker.transform, false);
            // Trackers left over from removed targets would still cost tracking time.
            foreach (var t in trackers.Where(t => targets.All(x => x.Tracker != t)).ToList())
            {
                Log("Removed tracker " + t.name + " (no image in " + TargetImagesFolder + ")");
                UnityEngine.Object.DestroyImmediate(t.gameObject);
            }
            var zcam = arCam != null ? arCam.GetComponent<Zappar.ZapparCamera>() : null;
            if (zcam != null) { zcam.AnchorOrigin = targets[0].Tracker; EditorUtility.SetDirty(zcam); }
            ZapparCliTools.ZapparTargets.RecordBuilt(targets.Select(t => t.Id));
            Log("Targets: " + string.Join(", ", targets.Select(t => t.Id + "→" + t.Experience)));
        }

        static void ClearPersistent(UnityEngine.Object target, string eventField)
        {
            var so = new SerializedObject(target);
            var calls = so.FindProperty(eventField + ".m_PersistentCalls.m_Calls");
            if (calls != null) calls.arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static ShowcaseFxLibrary FxLibrary()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ShowcaseFxLibrary>(FxLibraryPath);
            if (lib == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FxLibraryPath));
                lib = ScriptableObject.CreateInstance<ShowcaseFxLibrary>();
                AssetDatabase.CreateAsset(lib, FxLibraryPath);
            }
            var ctx = new ShowcaseBuildContext();
            lib.Hit = ctx.Prefab("CFXR Hit A (Red)");
            lib.Poof = ctx.Prefab("CFXR Magic Poof");
            lib.Pickup = ctx.Prefab("CFXR2 Pickup Diamond (+ Stars)");
            lib.Sparkle = ctx.Prefab("CFXR2 Magic Stars");
            lib.Confetti = ctx.Prefab("CFXR Firework 1");
            lib.Smoke = ctx.Prefab("CFXR Smoke Poof");
            lib.Explosion = ctx.Prefab("CFXR Explosion 1");
            lib.Heart = ctx.Prefab("CFXR2 Pickup Heart");
            lib.Stars = ctx.Prefab("CFXR2 Pickup Magic Stars");
            lib.Magic = ctx.Prefab("CFXR2 Magic Explosion (Lit, Blue)");
            EditorUtility.SetDirty(lib);
            return lib;
        }

        // ---------- hub ----------

        static void BuildHub(ShowcaseBuildContext ctx, Transform world, List<(ShowcaseExperienceBuilderAttribute attr, MethodInfo build)> builders, Dictionary<string, ShowcaseExperience> experiences)
        {
            var old = world.Find("Hub");
            if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var hub = new GameObject("Hub").transform;
            hub.SetParent(world, false);
            hub.SetAsFirstSibling();
            hub.gameObject.AddComponent<ShowcaseHub>();

            ctx.Floor(hub, new Vector2(3.3f, 2.3f), new Color(0.95f, 0.94f, 0.97f));
            var rim = new Color(0.55f, 0.6f, 0.78f);
            ctx.Primitive(PrimitiveType.Cube, "Rim Back", hub, new Vector3(0f, 0.03f, 1.16f), new Vector3(3.3f, 0.06f, 0.04f), rim);
            ctx.Primitive(PrimitiveType.Cube, "Rim Front", hub, new Vector3(0f, 0.03f, -1.16f), new Vector3(3.3f, 0.06f, 0.04f), rim);
            ctx.Primitive(PrimitiveType.Cube, "Rim Left", hub, new Vector3(-1.66f, 0.03f, 0f), new Vector3(0.04f, 0.06f, 2.36f), rim);
            ctx.Primitive(PrimitiveType.Cube, "Rim Right", hub, new Vector3(1.66f, 0.03f, 0f), new Vector3(0.04f, 0.06f, 2.36f), rim);

            // Title sign at the back of the stage.
            var sign = Ui.WorldCanvas("Title Sign", hub, new Vector2(1400, 220), 2.2f);
            sign.transform.localPosition = new Vector3(0f, 0.45f, 1.05f);
            sign.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            var signBg = Ui.Panel(sign.transform, new Color(0.13f, 0.12f, 0.2f, 0.92f), "Background");
            Ui.Stretch(signBg.rectTransform);
            var signText = Ui.Label(signBg.transform, "AR SHOWCASE  ·  tap a pedestal", 84, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Stretch(signText.rectTransform, 30, 30, 10, 10);

            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Slots[i];
                var builder = builders.FirstOrDefault(b => b.attr.Order == i + 1);
                string title = builder.attr != null ? builder.attr.Title : slot.title;
                string blurb = builder.attr != null ? builder.attr.Blurb.Replace("\\n", "\n") : slot.blurb;
                string prop = builder.attr != null && !string.IsNullOrEmpty(builder.attr.PreviewPrefab) ? builder.attr.PreviewPrefab : slot.prop;
                string id = builder.attr != null ? builder.attr.Id : slot.id;

                int row = i / 5, col = i % 5;
                var pos = new Vector3(-1.24f + col * 0.62f, 0f, row == 0 ? 0.35f : -0.45f);
                var ped = new GameObject((i + 1).ToString("00") + " Pedestal " + title).transform;
                ped.SetParent(hub, false);
                ped.localPosition = pos;

                ctx.Primitive(PrimitiveType.Cylinder, "Base", ped, new Vector3(0f, 0.06f, 0f), new Vector3(0.42f, 0.06f, 0.42f), PedestalColors[i], false);
                ctx.Primitive(PrimitiveType.Cylinder, "Top", ped, new Vector3(0f, 0.125f, 0f), new Vector3(0.36f, 0.005f, 0.36f), Color.white, false);

                var display = new GameObject("Display").transform;
                display.SetParent(ped, false);
                display.localPosition = new Vector3(0f, 0.13f, 0f);
                ctx.Place(prop, display, Vector3.zero, 0.24f, 0f, true);

                var box = ped.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.22f, 0f);
                box.size = new Vector3(0.48f, 0.44f, 0.48f);

                var label = Ui.WorldCanvas("Label", ped, new Vector2(560, 150), 0.56f);
                label.transform.localPosition = new Vector3(0f, 0.02f, -0.3f);
                label.transform.localRotation = Quaternion.Euler(60f, 0f, 0f);
                var lbg = Ui.Panel(label.transform, new Color(1f, 1f, 1f, 0.9f), "Background");
                Ui.Stretch(lbg.rectTransform);
                lbg.raycastTarget = false;
                var num = Ui.Label(lbg.transform, (i + 1).ToString(), 70, PedestalColors[i] * 0.8f, TextAnchor.MiddleLeft, FontStyle.Bold, "Number");
                Ui.Stretch(num.rectTransform, 20, 440, 0, 0);
                var lt = Ui.Label(lbg.transform, title, 44, Ui.Ink, TextAnchor.MiddleLeft, FontStyle.Bold, "Title");
                Ui.Stretch(lt.rectTransform, 110, 16, 0, 0);

                var p = ped.gameObject.AddComponent<HubPedestal>();
                p.Title = title;
                p.Blurb = blurb;
                p.Display = display;
                experiences.TryGetValue(id, out p.Experience);
            }
            Log("Hub built: " + experiences.Count + " of " + Slots.Length + " experiences linked");
        }
    }
}
