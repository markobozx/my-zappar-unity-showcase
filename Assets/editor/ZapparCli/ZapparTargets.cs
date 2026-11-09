using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace ZapparCliTools
{
    // The image-target pipeline, usable from the Zappar CLI window, the Unity CLI ([CliCommand]s in
    // ZapparCliCommands) and batch mode (ZapparBatch):
    //   source image -> <targetImagesFolder>/<id>.png -> zapworks train -> <targetsFolder>/<id>.zpt
    //   -> the scene is rebuilt so every trained target gets a tracker and shows on the start screen.
    // The rebuild itself belongs to the project (it knows its scene), so it registers SceneRebuilder.
    public static class ZapparTargets
    {
        public class TrainOptions
        {
            public bool IncludePreview = true;
            public int Dpi;
            public bool Curved;
            public string Radius, TopRadius, BottomRadius;
        }

        public class TargetInfo
        {
            public string Id, ImagePath, ZptPath;
            public bool Trained => ZptPath != null;
            public bool Built;
        }

        // Set by the project (an [InitializeOnLoad] class) to rebuild its scene; returns a short report.
        public static Func<string> SceneRebuilder;

        // Raised whenever targets on disk or the built list change (the window repaints on it).
        public static event Action Changed;

        // Images being trained right now, so the import watcher doesn't train them a second time.
        internal static readonly HashSet<string> TrainingNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static ZapparCliSettings Settings => ZapparCliSettings.Instance;
        static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg" };

        public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

        // "My Poster (v2).PNG" -> "my-poster-v2": the id doubles as a file name and a URL parameter.
        public static string IdFor(string imagePath)
        {
            string id = Regex.Replace(Path.GetFileNameWithoutExtension(imagePath).ToLowerInvariant(), @"[^a-z0-9_-]+", "-").Trim('-');
            return string.IsNullOrEmpty(id) ? "target" : id;
        }

        public static string ZptPathFor(string id) => Path.Combine(Settings.targetsFolder, id + ".zpt").Replace('\\', '/');

        public static List<TargetInfo> Scan()
        {
            var list = new List<TargetInfo>();
            if (!Directory.Exists(Settings.targetImagesFolder))
                return list;
            var built = new HashSet<string>(Settings.builtTargets ?? new string[0]);
            foreach (var img in Directory.GetFiles(Settings.targetImagesFolder).Where(IsImage).OrderBy(f => f))
            {
                string id = Path.GetFileNameWithoutExtension(img);
                string zpt = ZptPathFor(id);
                list.Add(new TargetInfo { Id = id, ImagePath = img.Replace('\\', '/'), ZptPath = File.Exists(zpt) ? zpt : null, Built = built.Contains(id) });
            }
            return list;
        }

        // True when the trained targets on disk differ from what the scene was built with.
        public static bool RebuildNeeded(out string reason)
        {
            var trained = Scan().Where(t => t.Trained).Select(t => t.Id).ToList();
            var built = (Settings.builtTargets ?? new string[0]).ToList();
            var added = trained.Except(built).ToList();
            var removed = built.Except(trained).ToList();
            var parts = new List<string>();
            if (added.Count > 0) parts.Add("new: " + string.Join(", ", added));
            if (removed.Count > 0) parts.Add("removed: " + string.Join(", ", removed));
            reason = string.Join("; ", parts);
            return parts.Count > 0;
        }

        // Called by the project's scene builder after it wired the targets.
        public static void RecordBuilt(IEnumerable<string> ids)
        {
            Settings.builtTargets = ids.ToArray();
            Settings.Save();
            Changed?.Invoke();
        }

        internal static void NotifyChanged() => Changed?.Invoke();

        // Copies an image into the target images folder under its id. Returns the asset path.
        public static string ImportImage(string sourcePath)
        {
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("Image not found", sourcePath);
            if (!IsImage(sourcePath)) throw new ArgumentException("Not a PNG/JPG: " + sourcePath);
            Directory.CreateDirectory(Settings.targetImagesFolder);
            string dest = Path.Combine(Settings.targetImagesFolder, IdFor(sourcePath) + Path.GetExtension(sourcePath).ToLowerInvariant()).Replace('\\', '/');
            if (Path.GetFullPath(dest) != Path.GetFullPath(sourcePath))
                File.Copy(sourcePath, dest, true);
            return dest;
        }

        public static string TrainArgs(string imagePath, string zptPath, TrainOptions o)
        {
            o = o ?? new TrainOptions();
            var args = new List<string> { "train", Q(Path.GetFullPath(imagePath)), "--output", Q(Path.GetFullPath(zptPath)) };
            args.Add(o.IncludePreview ? "--include-preview" : "--exclude-preview");
            if (o.Dpi > 0) args.Add("--dpi=" + o.Dpi);
            if (o.Curved)
            {
                args.Add("--curved");
                if (!string.IsNullOrEmpty(o.Radius)) args.Add("--radius=" + o.Radius);
                if (!string.IsNullOrEmpty(o.TopRadius)) args.Add("--top-radius=" + o.TopRadius);
                if (!string.IsNullOrEmpty(o.BottomRadius)) args.Add("--bottom-radius=" + o.BottomRadius);
            }
            return string.Join(" ", args);
        }

        // Blocking train (CLI commands, batch mode, the import watcher). Returns true when the .zpt exists.
        public static bool TrainSync(string imagePath, TrainOptions options, out string log, int timeoutSeconds = 300)
        {
            string id = Path.GetFileNameWithoutExtension(imagePath);
            string zpt = ZptPathFor(id);
            Directory.CreateDirectory(Settings.targetsFolder);
            var psi = new ProcessStartInfo(ZapparCliSettings.CliPath, TrainArgs(imagePath, zpt, options))
            {
                WorkingDirectory = ZapparCliRunner.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            // Same PATH fix as ZapparCliRunner: zapworks is a node script.
            psi.EnvironmentVariables["PATH"] = string.Join(":", Path.GetDirectoryName(ZapparCliSettings.NodePath), "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin", psi.EnvironmentVariables["PATH"] ?? "");
            psi.EnvironmentVariables["NO_COLOR"] = "1";
            var output = new StringBuilder();
            TrainingNow.Add(imagePath);
            try
            {
                using (var p = new Process { StartInfo = psi })
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    p.Start();
                    p.StandardInput.Close();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(timeoutSeconds * 1000))
                    {
                        try { p.Kill(); } catch { }
                        output.AppendLine("Timed out after " + timeoutSeconds + " s");
                    }
                    p.WaitForExit();
                }
            }
            catch (Exception e)
            {
                output.AppendLine("Could not run " + ZapparCliSettings.CliPath + ": " + e.Message);
            }
            finally
            {
                TrainingNow.Remove(imagePath);
            }
            log = output.ToString().Trim();
            bool ok = File.Exists(zpt);
            if (ok) AssetDatabase.ImportAsset(zpt);
            Changed?.Invoke();
            return ok;
        }

        // Every image in the folder without a .zpt gets trained. Returns a report line per image.
        public static List<string> TrainMissing(TrainOptions options = null)
        {
            var report = new List<string>();
            var missing = Scan().Where(t => !t.Trained).ToList();
            for (int i = 0; i < missing.Count; i++)
            {
                var t = missing[i];
                if (!UnityEngine.Application.isBatchMode)
                    EditorUtility.DisplayProgressBar("Zappar", "Training target " + t.Id + " (" + (i + 1) + "/" + missing.Count + ")", i / (float)missing.Count);
                try
                {
                    bool ok = TrainSync(t.ImagePath, options, out string log);
                    report.Add((ok ? "trained " : "FAILED ") + t.Id + (ok ? "" : ": " + log));
                }
                finally
                {
                    if (!UnityEngine.Application.isBatchMode) EditorUtility.ClearProgressBar();
                }
            }
            return report;
        }

        public static string RebuildScene()
        {
            if (SceneRebuilder == null)
                return "No scene rebuilder registered (set ZapparTargets.SceneRebuilder from the project)";
            string result = SceneRebuilder();
            Changed?.Invoke();
            return result;
        }

        static string Q(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
    }
}
