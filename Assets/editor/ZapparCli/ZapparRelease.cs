using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ZapparCliTools
{
    // Release packaging shared by the ZapWorks CLI window and batch builds:
    //   Builds/ZappAR-Unity-WebGL-<ProductName>-v<version>.zip, index.html at the root (the layout ZapWorks expects).
    // ZapWorks rejects uploads over 25 MB; we warn above SoftLimitMB and refuse above HardLimitMB,
    // which leaves headroom for later additions.
    //
    // Batch: -executeMethod ZapparCliTools.ZapparRelease.BuildRelease [-releaseVersion 0.0.7]
    //   builds WebGL, zips, checks the limits, then bumps the "next version" in the CLI settings.
    //   Exit code 1 when the build fails or the zip is over the hard limit.
    public static class ZapparRelease
    {
        public const string ZipPrefix = "ZappAR-Unity-WebGL-";
        public const float SoftLimitMB = 20f;
        public const float HardLimitMB = 22f;

        static void Log(string m) => Debug.Log("[SHOWCASE-RELEASE] " + m);

        public static string ZipName(string version) =>
            ZipPrefix + Regex.Replace(PlayerSettings.productName, @"[^A-Za-z0-9_-]+", "-").Trim('-') + "-v" + version + ".zip";

        public static string ZipPathFor(string buildDir, string version) =>
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(buildDir)), ZipName(version));

        // The version shows on the scan screen (Application.version), so set it before every build.
        public static BuildReport Build(string buildDir, string version)
        {
            PlayerSettings.bundleVersion = version;
            return BuildScript.BuildWebGL(buildDir);
        }

        // Returns the zip path, or null (with `message` saying why) if zipping failed or the zip is over the hard limit.
        public static string Zip(string buildDir, string version, out string message)
        {
            string dir = Path.GetFullPath(buildDir);
            if (!File.Exists(Path.Combine(dir, "index.html"))) { message = "No build in " + buildDir; return null; }
            string zipPath = ZipPathFor(buildDir, version);
            try
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        string rel = file.Substring(dir.Length).TrimStart('/', '\\').Replace('\\', '/');
                        if (rel.Split('/').Any(p => p.StartsWith("."))) continue;
                        zip.CreateEntryFromFile(file, rel);
                    }
                }
            }
            catch (Exception e)
            {
                message = "Zip failed: " + e.Message;
                return null;
            }

            float mb = new FileInfo(zipPath).Length / (1024f * 1024f);
            string size = mb.ToString("0.0") + " MB";
            if (mb > HardLimitMB)
            {
                // Rename rather than delete, so the oversized build can still be inspected but never uploaded by mistake.
                string rejected = Path.ChangeExtension(zipPath, null) + "-OVER-LIMIT.zip";
                if (File.Exists(rejected)) File.Delete(rejected);
                File.Move(zipPath, rejected);
                message = "ERROR: zip is " + size + ", over the " + HardLimitMB + " MB hard limit. Renamed to " + Path.GetFileName(rejected) + "; do not upload.";
                return null;
            }
            message = mb > SoftLimitMB
                ? "WARNING: " + Path.GetFileName(zipPath) + " is " + size + ", over the " + SoftLimitMB + " MB soft limit (hard limit " + HardLimitMB + " MB)"
                : Path.GetFileName(zipPath) + " is " + size + " (soft limit " + SoftLimitMB + " MB, hard limit " + HardLimitMB + " MB)";
            return zipPath;
        }

        // Build + zip + limit check + version bump. Returns the zip path, or null with `message` saying why.
        public static string Release(string version, out string message)
        {
            var settings = ZapparCliSettings.Instance;
            version = string.IsNullOrEmpty(version) ? settings.version : version;
            Log("Building v" + version + " to " + settings.buildPath);
            var s = Build(settings.buildPath, version).summary;
            Log("Build " + s.result + ", " + (s.totalSize / 1048576f).ToString("0.0") + " MB uncompressed, " + s.totalErrors + " errors");
            if (s.result != BuildResult.Succeeded) { message = "Build " + s.result + " (" + s.totalErrors + " errors)"; return null; }

            string zip = Zip(settings.buildPath, version, out message);
            Log(message);
            if (zip == null) return null;
            Log("Release ready: " + zip);
            if (settings.autoBumpVersion && version == settings.version)
            {
                settings.version = ZapparCliSettings.BumpPatch(version);
                settings.Save();
                Log("Next version: " + settings.version);
            }
            return zip;
        }

        // Batch entry point; exit code 1 on failure.
        public static void BuildRelease()
        {
            if (Release(Arg("-releaseVersion"), out _) == null) EditorApplication.Exit(1);
        }

        static string Arg(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
