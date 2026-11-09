using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Showcase.EditorTools
{
    // Keeps the WebGL download small:
    //  - every texture the Showcase scene uses gets a WebGL override capped at MaxTextureSize
    //    (the Synty atlas is 4096 px; on a phone screen 1024 is plenty), with crunch-free compression;
    //  - models the scene uses get mesh compression;
    //  - BuildAndReport() builds WebGL and logs the biggest assets in the build.
    // Batch: -executeMethod Showcase.EditorTools.ShowcaseSizeOptimizer.Optimize
    //        -executeMethod Showcase.EditorTools.ShowcaseSizeOptimizer.BuildAndReport
    public static class ShowcaseSizeOptimizer
    {
        const int MaxTextureSize = 1024;
        const int MaxSmallTextureSize = 512;   // textures under Showcase-unrelated pack folders that are small props

        static void Log(string m) => Debug.Log("[SHOWCASE-SIZE] " + m);

        public static void Optimize()
        {
            var deps = AssetDatabase.GetDependencies(ShowcaseSceneBuilder.ScenePath, true);
            int textures = 0, models = 0;
            long before = 0;
            foreach (var path in deps)
            {
                var importer = AssetImporter.GetAtPath(path);
                if (importer is TextureImporter ti)
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture>(path);
                    int size = tex != null ? Mathf.Max(tex.width, tex.height) : 0;
                    before += tex != null ? (long)tex.width * tex.height : 0;
                    // Ramp/lookup textures must stay exact.
                    if (path.Contains("/Showcase/Style/") || ti.filterMode == FilterMode.Point && size <= 256) continue;
                    int cap = path.StartsWith("Assets/JMO Assets") ? MaxSmallTextureSize : MaxTextureSize;
                    var s = ti.GetPlatformTextureSettings("WebGL");
                    bool changed = !s.overridden || s.maxTextureSize > cap || s.crunchedCompression;
                    s.overridden = true;
                    s.maxTextureSize = Mathf.Min(s.maxTextureSize > 0 ? s.maxTextureSize : cap, cap);
                    s.format = TextureImporterFormat.Automatic;
                    s.textureCompression = TextureImporterCompression.Compressed;
                    s.crunchedCompression = false;
                    if (changed)
                    {
                        ti.SetPlatformTextureSettings(s);
                        ti.SaveAndReimport();
                        textures++;
                    }
                }
                else if (importer is ModelImporter mi && mi.meshCompression < ModelImporterMeshCompression.Medium)
                {
                    mi.meshCompression = ModelImporterMeshCompression.Medium;
                    mi.SaveAndReimport();
                    models++;
                }
            }
            Log("Scene depends on " + deps.Length + " assets; capped " + textures + " textures for WebGL, compressed " + models + " models");
        }

        public static void BuildAndReport()
        {
            var report = BuildScript.BuildWebGL("Builds/WebGL");
            var s = report.summary;
            Log("Build " + s.result + ", total " + (s.totalSize / 1048576f).ToString("0.0") + " MB (uncompressed), " + s.totalErrors + " errors");
            var biggest = new List<(string path, ulong size)>();
            foreach (var pa in report.packedAssets)
                foreach (var c in pa.contents)
                    biggest.Add((c.sourceAssetPath, c.packedSize));
            foreach (var g in biggest.GroupBy(b => b.path).Select(g => (g.Key, (ulong)g.Sum(x => (long)x.size))).OrderByDescending(x => x.Item2).Take(25))
                Log("  " + (g.Item2 / 1024f).ToString("0") + " KB  " + g.Key);
            if (s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
