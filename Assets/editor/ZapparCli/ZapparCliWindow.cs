using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ZapparCliTools
{
    // Editor UI for the ZapWorks CLI (`zapworks`): account check, image target training,
    // WebGL build, local serve, upload and publish.
    public class ZapparCliWindow : EditorWindow
    {
        const int MaxLogLines = 2000;

        [SerializeField] List<string> m_images = new List<string>();
        [SerializeField] bool m_curved;
        [SerializeField] string m_radius = "";
        [SerializeField] string m_topRadius = "";
        [SerializeField] string m_bottomRadius = "";
        [SerializeField] int m_dpi;
        [SerializeField] bool m_includePreview = true;
        [SerializeField] bool m_serveLan = true;
        [SerializeField] int m_tab;
        [SerializeField] List<string> m_log = new List<string>();
        [SerializeField] string m_cliVersion = "";
        [SerializeField] string m_signInStatus = "Not checked";
        [SerializeField] string m_lastUrl = "";

        ZapparCliRunner m_runner;
        ZapparCliRunner m_serveRunner;
        readonly Queue<Action> m_queue = new Queue<Action>();
        Vector2 m_logScroll;
        Vector2 m_mainScroll;
        bool m_logFollow = true;

        static readonly string[] Tabs = { "Account", "Project", "Image Targets", "Build & Publish" };
        static readonly Regex UrlRegex = new Regex(@"https?://\S+");

        [MenuItem("Zappar/Zappar CLI Tools", false, 0)]
        public static void Open()
        {
            var w = GetWindow<ZapparCliWindow>();
            w.titleContent = new GUIContent("Zappar CLI");
            w.minSize = new Vector2(420, 480);
            w.Show();
        }

        ZapparCliSettings Settings => ZapparCliSettings.Instance;
        bool Busy => m_runner != null && m_runner.IsRunning;

        void OnEnable()
        {
            m_runner = new ZapparCliRunner();
            m_runner.OnLine += AddLog;
            m_serveRunner = new ZapparCliRunner();
            m_serveRunner.OnLine += AddLog;
            // A domain reload would orphan child processes (serve keeps running and holds the port).
            AssemblyReloadEvents.beforeAssemblyReload += StopAll;
            EditorApplication.quitting += StopAll;
            if (string.IsNullOrEmpty(m_cliVersion)) EditorApplication.delayCall += CheckCli;
        }

        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= StopAll;
            EditorApplication.quitting -= StopAll;
            StopAll();
        }

        void StopAll()
        {
            m_queue.Clear();
            m_runner?.Stop();
            m_serveRunner?.Stop();
        }

        void AddLog(string line)
        {
            m_log.Add(line);
            if (m_log.Count > MaxLogLines) m_log.RemoveRange(0, m_log.Count - MaxLogLines);
            var url = UrlRegex.Match(line);
            if (url.Success && (line.Contains("uploaded to") || line.Contains("access from"))) m_lastUrl = url.Value.TrimEnd('.', ',');
            if (m_logFollow) m_logScroll.y = float.MaxValue;
            Repaint();
        }

        // ---------- Running commands ----------

        void Run(string label, string args, Action<int, string> onExit = null)
        {
            if (!File.Exists(ZapparCliSettings.CliPath) && ZapparCliSettings.CliPath != "zapworks")
            {
                AddLog("ZapWorks CLI not found at " + ZapparCliSettings.CliPath + ". Use Account > Install / update CLI.");
                return;
            }
            m_runner.Start(label, ZapparCliSettings.CliPath, args, (code, output) =>
            {
                onExit?.Invoke(code, output);
                RunNextQueued();
            });
        }

        void Enqueue(Action a)
        {
            m_queue.Enqueue(a);
            if (!Busy) RunNextQueued();
        }

        void RunNextQueued()
        {
            if (Busy || m_queue.Count == 0) return;
            m_queue.Dequeue()();
        }

        static string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";

        // Helper script that reuses the CLI's own API client. The CLI has no login command and
        // doesn't store a token: every upload signs in through the browser, so "sign in" here
        // means "run that browser flow once and confirm ZapWorks accepts the token".
        const string HelperJs = @"
const fs = require('fs'), path = require('path');
const root = path.resolve(path.dirname(fs.realpathSync(process.argv[3])), '..');
const mode = process.argv[2];
if (mode === 'version') { console.log('CLI_VERSION ' + require(path.join(root, 'package.json')).version); process.exit(0); }
const cliSrc = fs.readFileSync(path.join(root, 'out/src/cli.js'), 'utf8');
const clientId = (cliSrc.match(/clientId:\s*""([^""]+)""/) || [])[1];
const zw = require(path.join(root, 'node_modules/@zappar/zw-api-client/dist/node.js'));
const fetch = require(path.join(root, 'node_modules/node-fetch'));
setTimeout(() => { console.log('SIGNIN_TIMEOUT'); process.exit(2); }, 180000);
(async () => {
  const c = new zw.Client({ clientId, env: zw.Environment.Prod, debug: false });
  const opts = await c._authenticationSetup();
  const r = await fetch(c.envSettings.url + '/upload/' + zw.IFileType.UARPackage + '/params/', opts);
  console.log(r.ok ? 'SIGNIN_OK' : 'SIGNIN_FAILED ' + r.status);
  process.exit(r.ok ? 0 : 1);
})().catch(e => { console.log('SIGNIN_FAILED ' + e.message); process.exit(1); });
";

        void RunHelper(string label, string mode, Action<int, string> onExit)
        {
            string js = Path.Combine("Temp", "zappar-cli-helper.js");
            File.WriteAllText(js, HelperJs);
            m_runner.Start(label, ZapparCliSettings.NodePath, Q(Path.GetFullPath(js)) + " " + mode + " " + Q(ZapparCliSettings.CliPath), (code, output) =>
            {
                onExit?.Invoke(code, output);
                RunNextQueued();
            });
        }

        void CheckCli()
        {
            if (Busy) return;
            RunHelper("Checking CLI", "version", (code, output) =>
            {
                var m = Regex.Match(output, @"CLI_VERSION (\S+)");
                m_cliVersion = m.Success ? m.Groups[1].Value : "not found";
                Repaint();
            });
        }

        void SignIn()
        {
            m_signInStatus = "Waiting for browser sign-in…";
            RunHelper("Signing in", "signin", (code, output) =>
            {
                if (output.Contains("SIGNIN_OK")) m_signInStatus = "Signed in (ZapWorks accepted the token) at " + DateTime.Now.ToString("HH:mm");
                else if (output.Contains("SIGNIN_TIMEOUT")) m_signInStatus = "Timed out waiting for the browser sign-in";
                else m_signInStatus = "Sign-in failed, see log";
                Repaint();
            });
        }

        // ---------- GUI ----------

        void OnGUI()
        {
            DrawHeader();
            m_tab = GUILayout.Toolbar(m_tab, Tabs);
            EditorGUILayout.Space(4);

            m_mainScroll = EditorGUILayout.BeginScrollView(m_mainScroll, GUILayout.ExpandHeight(true));
            switch (m_tab)
            {
                case 0: DrawAccount(); break;
                case 1: DrawProject(); break;
                case 2: DrawTargets(); break;
                case 3: DrawBuild(); break;
            }
            EditorGUILayout.EndScrollView();

            DrawLog();
        }

        void DrawHeader()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("CLI " + (string.IsNullOrEmpty(m_cliVersion) ? "…" : m_cliVersion), EditorStyles.miniLabel);
                GUILayout.Label("Project " + (string.IsNullOrEmpty(Settings.projectId) ? "(not set)" : Settings.projectId) + "  v" + Settings.version, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (m_serveRunner.IsRunning) GUILayout.Label("● serving", EditorStyles.miniBoldLabel);
                if (Busy)
                {
                    GUILayout.Label(m_runner.Label + "…", EditorStyles.miniBoldLabel);
                    if (GUILayout.Button("Cancel", EditorStyles.toolbarButton)) { m_queue.Clear(); m_runner.Stop(); }
                }
            }
        }

        void DrawAccount()
        {
            EditorGUILayout.LabelField("ZapWorks account", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The ZapWorks CLI has no login/logout command and keeps no saved login. Every upload signs in through your default browser, using whatever zap.works session that browser has.\n\n" +
                "Sign in: runs that browser sign-in now and checks ZapWorks accepts it.\n" +
                "Sign out / switch account: sign out on zap.works in the browser. The next sign-in then asks which account to use.",
                MessageType.Info);
            EditorGUILayout.LabelField("Status", m_signInStatus);
            using (new EditorGUI.DisabledScope(Busy))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Sign in (browser)")) SignIn();
                if (GUILayout.Button("Sign out / switch account")) Application.OpenURL(Settings.zapworksUrl);
                if (GUILayout.Button("Open ZapWorks")) Application.OpenURL(Settings.zapworksUrl);
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("CLI", EditorStyles.boldLabel);
            ZapparCliSettings.CliPath = EditorGUILayout.DelayedTextField("zapworks path", ZapparCliSettings.CliPath);
            ZapparCliSettings.NodePath = EditorGUILayout.DelayedTextField("node path", ZapparCliSettings.NodePath);
            using (new EditorGUI.DisabledScope(Busy))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Check CLI")) CheckCli();
                if (GUILayout.Button("Install / update CLI"))
                {
                    string npm = Path.Combine(Path.GetDirectoryName(ZapparCliSettings.NodePath), "npm");
                    m_runner.Start("Installing CLI", npm, "install -g @zappar/zapworks-cli", (c, o) => { m_cliVersion = ""; CheckCli(); });
                }
            }
        }

        void DrawProject()
        {
            EditorGUILayout.LabelField("ZapWorks project", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            Settings.projectId = EditorGUILayout.TextField(new GUIContent("Project ID", "The number from 'zapworks publish --project=…' on your Universal AR project."), Settings.projectId).Trim();
            Settings.version = EditorGUILayout.TextField(new GUIContent("Next version", "Version number for the next upload. Each upload needs a new one."), Settings.version).Trim();
            Settings.autoBumpVersion = EditorGUILayout.Toggle(new GUIContent("Auto-bump after upload", "1.0.0 → 1.0.1 after a successful upload."), Settings.autoBumpVersion);
            Settings.crossOriginIsolation = EditorGUILayout.Toggle(new GUIContent("Cross-origin isolation", "Only needed for builds using threads / SharedArrayBuffer."), Settings.crossOriginIsolation);
            EditorGUILayout.Space(6);
            Settings.buildPath = EditorGUILayout.TextField(new GUIContent("WebGL build folder", "Relative to the project root."), Settings.buildPath).Trim();
            Settings.targetsFolder = EditorGUILayout.TextField(new GUIContent("Target (.zpt) folder", "Zappar loads targets from StreamingAssets."), Settings.targetsFolder).Trim();
            Settings.zapworksUrl = EditorGUILayout.TextField("ZapWorks URL", Settings.zapworksUrl).Trim();
            if (EditorGUI.EndChangeCheck()) Settings.Save();
            EditorGUILayout.HelpBox("Saved to ProjectSettings/ZapparCliSettings.json (commit it to share with the team).", MessageType.None);
        }

        // ---------- Image targets ----------

        void DrawTargets()
        {
            EditorGUILayout.LabelField("Train image targets", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Drag images (PNG/JPG) from the Project window or Finder into the box, or select textures and press 'Add selection'. Training writes <name>.zpt into " + Settings.targetsFolder + ".", MessageType.None);

            var drop = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
            GUI.Box(drop, "Drop images here", EditorStyles.helpBox);
            HandleDrop(drop);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add selection")) AddSelection();
                if (GUILayout.Button("Browse…"))
                {
                    var f = EditorUtility.OpenFilePanelWithFilters("Choose target image", "", new[] { "Images", "png,jpg,jpeg" });
                    if (!string.IsNullOrEmpty(f)) AddImage(f);
                }
                if (GUILayout.Button("Clear list")) m_images.Clear();
            }

            for (int i = 0; i < m_images.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string img = m_images[i];
                    bool exists = File.Exists(img);
                    string zpt = ZptPathFor(img);
                    GUILayout.Label((exists ? "" : "(missing) ") + Path.GetFileName(img), GUILayout.MinWidth(120));
                    GUILayout.Label(File.Exists(zpt) ? "trained" : "", EditorStyles.miniLabel, GUILayout.Width(50));
                    using (new EditorGUI.DisabledScope(!exists))
                        if (GUILayout.Button("Train", GUILayout.Width(60))) { var p = img; Enqueue(() => Train(p)); }
                    if (GUILayout.Button("✕", GUILayout.Width(24))) { m_images.RemoveAt(i); GUIUtility.ExitGUI(); }
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Training options", EditorStyles.miniBoldLabel);
            m_includePreview = EditorGUILayout.Toggle(new GUIContent("Include preview image", "Embeds the image so the Zappar target shows a preview in the Scene view."), m_includePreview);
            m_dpi = EditorGUILayout.IntField(new GUIContent("DPI (0 = auto)", "Print DPI of the image, if known."), m_dpi);
            m_curved = EditorGUILayout.Toggle(new GUIContent("Curved (cylinder/cone)", "For labels wrapped around bottles or cans."), m_curved);
            if (m_curved)
            {
                EditorGUI.indentLevel++;
                m_radius = EditorGUILayout.TextField("Radius", m_radius);
                m_topRadius = EditorGUILayout.TextField("Top radius (cone)", m_topRadius);
                m_bottomRadius = EditorGUILayout.TextField("Bottom radius (cone)", m_bottomRadius);
                EditorGUI.indentLevel--;
            }
            if (GUILayout.Button("Train all (" + m_images.Count + ")") && m_images.Count > 0)
                foreach (var img in m_images.Where(File.Exists).ToList()) Enqueue(() => Train(img));

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Targets in " + Settings.targetsFolder, EditorStyles.boldLabel);
            if (!Directory.Exists(Settings.targetsFolder)) { EditorGUILayout.LabelField("(folder doesn't exist yet)"); return; }
            foreach (var zpt in Directory.GetFiles(Settings.targetsFolder, "*.zpt").OrderBy(f => f))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(Path.GetFileName(zpt), GUILayout.MinWidth(120));
                    if (GUILayout.Button("Add tracking target to scene", GUILayout.Width(190))) CreateTrackingTarget(Path.GetFileName(zpt));
                    if (GUILayout.Button("Show", GUILayout.Width(50))) EditorUtility.RevealInFinder(zpt);
                }
            }
        }

        void HandleDrop(Rect area)
        {
            var e = Event.current;
            if (!area.Contains(e.mousePosition)) return;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                foreach (var p in DragAndDrop.paths) AddImage(p);
            }
            e.Use();
        }

        void AddSelection()
        {
            foreach (var o in Selection.objects.OfType<Texture2D>()) AddImage(AssetDatabase.GetAssetPath(o));
        }

        void AddImage(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") { AddLog("Skipped (not PNG/JPG): " + path); return; }
            string full = Path.GetFullPath(path);
            if (!m_images.Contains(full)) m_images.Add(full);
        }

        string ZptPathFor(string image) => Path.Combine(Settings.targetsFolder, Path.GetFileNameWithoutExtension(image) + ".zpt");

        void Train(string image)
        {
            Directory.CreateDirectory(Settings.targetsFolder);
            string outPath = Path.GetFullPath(ZptPathFor(image));
            var args = new List<string> { "train", Q(image), "--output", Q(outPath) };
            args.Add(m_includePreview ? "--include-preview" : "--exclude-preview");
            if (m_dpi > 0) args.Add("--dpi=" + m_dpi);
            if (m_curved)
            {
                args.Add("--curved");
                if (!string.IsNullOrEmpty(m_radius)) args.Add("--radius=" + m_radius);
                if (!string.IsNullOrEmpty(m_topRadius)) args.Add("--top-radius=" + m_topRadius);
                if (!string.IsNullOrEmpty(m_bottomRadius)) args.Add("--bottom-radius=" + m_bottomRadius);
            }
            Run("Training " + Path.GetFileName(image), string.Join(" ", args), (code, output) =>
            {
                AssetDatabase.Refresh();
                AddLog(File.Exists(outPath) && code == 0 ? "Trained → " + ZptPathFor(image) : "Training failed for " + Path.GetFileName(image));
            });
        }

        void CreateTrackingTarget(string zptFileName)
        {
            var go = new GameObject("Image Target - " + Path.GetFileNameWithoutExtension(zptFileName));
            var t = go.AddComponent<Zappar.ZapparImageTrackingTarget>();
            t.Target = zptFileName;
            Undo.RegisterCreatedObjectUndo(go, "Add Zappar image target");
            EditorSceneManager.MarkSceneDirty(go.scene);
            Selection.activeGameObject = go;
        }

        // ---------- Build & publish ----------

        string BuildDir => Path.GetFullPath(Settings.buildPath);
        bool HasBuild => File.Exists(Path.Combine(BuildDir, "index.html"));

        void DrawBuild()
        {
            EditorGUILayout.LabelField("Build", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Build folder", Settings.buildPath + (HasBuild ? "  (" + File.GetLastWriteTime(Path.Combine(BuildDir, "index.html")).ToString("dd MMM HH:mm") + ")" : "  (no build yet)"));
            using (new EditorGUI.DisabledScope(Busy))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build WebGL")) EditorApplication.delayCall += () => BuildWebGL(null);
                if (GUILayout.Button("Build + Upload & Publish")) EditorApplication.delayCall += () => BuildWebGL(() => Push(true));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open build folder")) OpenBuildFolder();
                using (new EditorGUI.DisabledScope(!HasBuild))
                    if (GUILayout.Button("Make zip")) { var z = MakeZip(); if (z != null) EditorUtility.RevealInFinder(z); }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Test on your phone (local)", EditorStyles.boldLabel);
            m_serveLan = EditorGUILayout.Toggle(new GUIContent("Allow phones on Wi-Fi", "Serve on your local network (--lan) so a phone on the same Wi-Fi can open it. HTTPS, self-signed: accept the warning on the phone."), m_serveLan);
            using (new EditorGUI.DisabledScope(!HasBuild))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!m_serveRunner.IsRunning)
                {
                    if (GUILayout.Button("Serve build")) m_serveRunner.Start("Serving", ZapparCliSettings.CliPath, "serve " + Q(Settings.buildPath) + (m_serveLan ? " --lan" : ""), (c, o) => Repaint());
                }
                else if (GUILayout.Button("Stop serving")) m_serveRunner.Stop();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Push to ZapWorks hosting", EditorStyles.boldLabel);
            if (string.IsNullOrEmpty(Settings.projectId)) EditorGUILayout.HelpBox("Set the Project ID in the Project tab first.", MessageType.Warning);
            EditorGUILayout.LabelField("Will upload as", "project " + Settings.projectId + ", version " + Settings.version);
            using (new EditorGUI.DisabledScope(Busy || !HasBuild || string.IsNullOrEmpty(Settings.projectId)))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Upload only")) Push(false);
                if (GUILayout.Button("Upload & Publish")) Push(true);
            }
            using (new EditorGUI.DisabledScope(Busy || string.IsNullOrEmpty(Settings.projectId)))
                if (GUILayout.Button("Publish an already-uploaded version (" + Settings.version + ")")) PublishExisting();
            EditorGUILayout.HelpBox("If the upload fails, the tool zips the build, shows it in Finder and opens ZapWorks so you can upload the zip by hand.", MessageType.None);

            if (!string.IsNullOrEmpty(m_lastUrl))
            {
                EditorGUILayout.Space(6);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.SelectableLabel(m_lastUrl, GUILayout.Height(18));
                    if (GUILayout.Button("Open", GUILayout.Width(60))) Application.OpenURL(m_lastUrl);
                }
            }
        }

        void BuildWebGL(Action onSuccess)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            {
                if (!EditorUtility.DisplayDialog("Switch to WebGL?", "The active platform is " + EditorUserBuildSettings.activeBuildTarget + ". Switch to WebGL before building? (This can take a while the first time.)", "Switch and build", "Cancel")) return;
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);
            }
            AddLog("Building WebGL to " + Settings.buildPath + "…");
            BuildReport report = BuildScript.BuildWebGL(Settings.buildPath);
            var s = report.summary;
            AddLog("Build " + s.result + " in " + s.totalTime.ToString(@"mm\:ss") + ", " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " MB, " + s.totalErrors + " errors");
            if (s.result == BuildResult.Succeeded) onSuccess?.Invoke();
        }

        void Push(bool publish)
        {
            if (!HasBuild) { AddLog("No build in " + Settings.buildPath + ". Build first."); return; }
            string version = Settings.version;
            // The CLI joins --dir onto its working directory, so it must be relative to the project root.
            string args = (publish ? "publish" : "upload") +
                          " --project=" + Settings.projectId +
                          " --version=" + version +
                          " --dir " + Q(Settings.buildPath) +
                          (Settings.crossOriginIsolation ? " --cross-origin-isolation" : "") +
                          " --yes";
            Run(publish ? "Uploading & publishing" : "Uploading", args, (code, output) =>
            {
                // The CLI prints errors but still exits 0, so judge by its output.
                bool uploaded = output.Contains("Project uploaded to") || output.Contains("already exists");
                bool published = output.Contains("Project published!");
                bool ok = code == 0 && !output.Contains("ERROR") && (publish ? published : uploaded);
                if (ok)
                {
                    AddLog(publish ? "✔ Published version " + version : "✔ Uploaded version " + version);
                    if (Settings.autoBumpVersion && output.Contains("Project uploaded to"))
                    {
                        Settings.version = ZapparCliSettings.BumpPatch(version);
                        Settings.Save();
                    }
                }
                else
                {
                    AddLog("✖ Push failed. Falling back to manual upload: zipping the build and opening ZapWorks.");
                    var zip = MakeZip();
                    if (zip != null) EditorUtility.RevealInFinder(zip);
                    Application.OpenURL(Settings.zapworksUrl);
                }
            });
        }

        void PublishExisting()
        {
            Run("Publishing", "publish --project=" + Settings.projectId + " --version=" + Settings.version + " --yes", (code, output) =>
                AddLog(output.Contains("Project published!") && !output.Contains("ERROR") ? "✔ Published version " + Settings.version : "✖ Publish failed, see log"));
        }

        void OpenBuildFolder()
        {
            if (Directory.Exists(BuildDir)) EditorUtility.RevealInFinder(Path.Combine(BuildDir, "index.html"));
            else if (Directory.Exists(Path.GetDirectoryName(BuildDir))) EditorUtility.RevealInFinder(Path.GetDirectoryName(BuildDir));
            else AddLog("No build folder yet: " + Settings.buildPath);
        }

        // Zip with index.html at the root, the layout ZapWorks expects for a manual upload.
        string MakeZip()
        {
            if (!HasBuild) { AddLog("No build to zip."); return null; }
            string safeName = Regex.Replace(PlayerSettings.productName, @"[^A-Za-z0-9_-]+", "-").Trim('-');
            string zipPath = Path.Combine(Path.GetDirectoryName(BuildDir), safeName + "-webgl-" + Settings.version + ".zip");
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.GetFiles(BuildDir, "*", SearchOption.AllDirectories))
                    {
                        string rel = file.Substring(BuildDir.Length).TrimStart('/', '\\').Replace('\\', '/');
                        if (rel.Split('/').Any(p => p.StartsWith("."))) continue;
                        zip.CreateEntryFromFile(file, rel);
                    }
                }
                AddLog("Zipped build → " + zipPath);
                return zipPath;
            }
            catch (Exception e)
            {
                AddLog("Zip failed: " + e.Message);
                return null;
            }
        }

        // ---------- Log ----------

        void DrawLog()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Output", EditorStyles.miniBoldLabel);
                GUILayout.FlexibleSpace();
                m_logFollow = GUILayout.Toggle(m_logFollow, "Follow", EditorStyles.toolbarButton);
                if (GUILayout.Button("Copy", EditorStyles.toolbarButton)) EditorGUIUtility.systemCopyBuffer = string.Join("\n", m_log);
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton)) m_log.Clear();
            }
            m_logScroll = EditorGUILayout.BeginScrollView(m_logScroll, GUILayout.Height(Mathf.Max(140, position.height * 0.33f)));
            var style = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false, fontSize = 11 };
            foreach (var line in m_log) GUILayout.Label(line, style);
            EditorGUILayout.EndScrollView();
        }
    }
}
