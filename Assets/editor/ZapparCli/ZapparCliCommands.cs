using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Pipeline.Commands;

namespace ZapparCliTools
{
    // The Zappar plugin on the Unity CLI (needs the com.unity.pipeline package).
    //   Editor open:   unity command zappar_status
    //   Editor closed: unity run . --command zappar_status
    // Arguments go after the name (open) or after "--" (closed): unity run . --command zappar_add_target -- --image ~/poster.png
    // Failures throw, so the CLI exits non-zero. `unity command` lists them all with their arguments.
    static class ZapparCliCommands
    {
        static ZapparCliSettings Settings => ZapparCliSettings.Instance;

        [CliCommand("zappar_status", "Zappar project status: product, version, ZapWorks project, targets (trained / in scene), rebuild needed, last zip")]
        static string Status()
        {
            var sb = new StringBuilder();
            sb.AppendLine("product: " + UnityEditor.PlayerSettings.productName);
            sb.AppendLine("next version: " + Settings.version + (Settings.autoBumpVersion ? " (auto-bump on)" : ""));
            sb.AppendLine("zapworks project: " + (string.IsNullOrEmpty(Settings.projectId) ? "(not set)" : Settings.projectId));
            sb.AppendLine("target images: " + Settings.targetImagesFolder + ", zpt: " + Settings.targetsFolder + ", auto-train: " + Settings.autoTrainNewImages);
            foreach (var t in ZapparTargets.Scan())
                sb.AppendLine("  target " + t.Id + ": " + (t.Trained ? "trained" : "NOT TRAINED") + ", " + (t.Built ? "in scene" : "not in scene"));
            sb.AppendLine("rebuild needed: " + (ZapparTargets.RebuildNeeded(out string reason) ? "yes (" + reason + ")" : "no"));
            string zip = ZapparRelease.ZipPathFor(Settings.buildPath, Previous(Settings.version));
            sb.AppendLine("last release zip: " + (File.Exists(zip) ? zip + " (" + (new FileInfo(zip).Length / 1048576f).ToString("0.0") + " MB)" : "(none for v" + Previous(Settings.version) + ")"));
            return sb.ToString().TrimEnd();
        }

        [CliCommand("zappar_add_target", "Copy an image into the target images folder, train it with zapworks, and (by default) rebuild the scene")]
        static string AddTarget(
            [CliArg("image", "Path to a PNG/JPG", Required = true)] string image,
            [CliArg("dpi", "Print DPI of the image (0 = auto)")] int dpi = 0,
            [CliArg("rebuild", "Rebuild the scene afterwards")] bool rebuild = true)
        {
            image = ExpandHome(image);
            string dest = ZapparTargets.ImportImage(image);
            UnityEditor.AssetDatabase.ImportAsset(dest);
            if (!ZapparTargets.TrainSync(dest, new ZapparTargets.TrainOptions { Dpi = dpi }, out string log))
                throw new InvalidOperationException("Training failed for " + dest + ":\n" + log);
            string result = "Added target " + Path.GetFileNameWithoutExtension(dest) + " (" + dest + ", " + ZapparTargets.ZptPathFor(Path.GetFileNameWithoutExtension(dest)) + ")";
            if (rebuild) result += "\n" + ZapparTargets.RebuildScene();
            return result;
        }

        [CliCommand("zappar_remove_target", "Delete a target's image and .zpt, and (by default) rebuild the scene")]
        static string RemoveTarget(
            [CliArg("id", "Target id (see zappar_status)", Required = true)] string id,
            [CliArg("rebuild", "Rebuild the scene afterwards")] bool rebuild = true)
        {
            var t = ZapparTargets.Scan().FirstOrDefault(x => x.Id == id);
            string zpt = ZapparTargets.ZptPathFor(id);
            if (t == null && !File.Exists(zpt)) throw new ArgumentException("No target '" + id + "'. Targets: " + string.Join(", ", ZapparTargets.Scan().Select(x => x.Id)));
            if (t != null) UnityEditor.AssetDatabase.DeleteAsset(t.ImagePath);
            if (File.Exists(zpt)) UnityEditor.AssetDatabase.DeleteAsset(zpt);
            string result = "Removed target " + id;
            if (rebuild) result += "\n" + ZapparTargets.RebuildScene();
            return result;
        }

        [CliCommand("zappar_train_missing", "Train every image in the target images folder that has no .zpt yet")]
        static string TrainMissing()
        {
            var report = ZapparTargets.TrainMissing();
            if (report.Any(r => r.StartsWith("FAILED"))) throw new InvalidOperationException(string.Join("\n", report));
            return report.Count == 0 ? "Every target image is trained" : string.Join("\n", report);
        }

        [CliCommand("zappar_rebuild_scene", "Rebuild the scene so every trained target has a tracker and shows on the start screen")]
        static string RebuildScene()
        {
            string result = ZapparTargets.RebuildScene();
            if (ZapparTargets.RebuildNeeded(out string reason)) throw new InvalidOperationException(result + "\nStill out of date: " + reason);
            return result;
        }

        [CliCommand("zappar_build_release", "Build WebGL and zip it as ZappAR-Unity-WebGL-<Product>-v<version>.zip (warns over 20 MB, fails over 22 MB)")]
        static string BuildRelease([CliArg("version", "Version to build (default: the next version from the settings)")] string version = "")
        {
            string zip = ZapparRelease.Release(version, out string message);
            if (zip == null) throw new InvalidOperationException(message);
            return zip + "\n" + message + "\nnext version: " + Settings.version;
        }

        [CliCommand("zappar_set_version", "Set the next release version, e.g. 0.1.0 for a milestone")]
        static string SetVersion([CliArg("version", "e.g. 0.0.7", Required = true)] string version)
        {
            if (version.Split('.').Length != 3 || version.Split('.').Any(p => !int.TryParse(p, out _)))
                throw new ArgumentException("Use major.minor.patch, e.g. 0.0.7");
            Settings.version = version;
            Settings.Save();
            return "next version: " + version;
        }

        [CliCommand("zappar_set_project", "Set the ZapWorks project ID used for uploads")]
        static string SetProject([CliArg("id", "ZapWorks project ID", Required = true)] string id)
        {
            Settings.projectId = id.Trim();
            Settings.Save();
            return "zapworks project: " + Settings.projectId;
        }

        static string Previous(string version)
        {
            var p = version.Split('.');
            return p.Length == 3 && int.TryParse(p[2], out int patch) && patch > 0 ? p[0] + "." + p[1] + "." + (patch - 1) : version;
        }

        static string ExpandHome(string path) =>
            path.StartsWith("~/") ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2)) : path;
    }
}
