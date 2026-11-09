using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZapparCliTools
{
    // Project-wide settings live in ProjectSettings/ so they are committed and shared.
    // Machine-specific paths (CLI/node location) live in EditorPrefs instead.
    [Serializable]
    public class ZapparCliSettings
    {
        const string FilePath = "ProjectSettings/ZapparCliSettings.json";
        const string CliPathPref = "ZapparCli.CliPath";
        const string NodePathPref = "ZapparCli.NodePath";

        public string projectId = "";
        public string version = "0.0.1";
        public bool autoBumpVersion = true;
        public bool crossOriginIsolation = false;
        public string buildPath = "Builds/WebGL";
        public string targetsFolder = "Assets/StreamingAssets";
        // Source images of the targets (<id>.png next to StreamingAssets/<id>.zpt); the start screen shows them.
        public string targetImagesFolder = "Assets/Showcase/Targets";
        // Images dropped into targetImagesFolder are trained automatically (interactive Editor only).
        public bool autoTrainNewImages = true;
        // Target ids the scene was last built with; differs from the trained targets on disk -> rebuild needed.
        public string[] builtTargets = new string[0];
        public string zapworksUrl = "https://my.zap.works/";

        static ZapparCliSettings s_instance;

        public static ZapparCliSettings Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                s_instance = new ZapparCliSettings();
                if (File.Exists(FilePath))
                {
                    try { JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), s_instance); }
                    catch (Exception e) { Debug.LogWarning("[Zappar CLI] Could not read " + FilePath + ": " + e.Message); }
                }
                return s_instance;
            }
        }

        public void Save()
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(this, true));
        }

        public static string CliPath
        {
            get => EditorPrefs.GetString(CliPathPref, DefaultCliPath());
            set => EditorPrefs.SetString(CliPathPref, value);
        }

        public static string NodePath
        {
            get => EditorPrefs.GetString(NodePathPref, DefaultNodePath());
            set => EditorPrefs.SetString(NodePathPref, value);
        }

        static string DefaultCliPath()
        {
            foreach (var p in new[] { "/opt/homebrew/bin/zapworks", "/usr/local/bin/zapworks" })
                if (File.Exists(p)) return p;
            return "zapworks";
        }

        static string DefaultNodePath()
        {
            foreach (var p in new[] { "/opt/homebrew/bin/node", "/usr/local/bin/node" })
                if (File.Exists(p)) return p;
            return "node";
        }

        // 0.0.98 -> 0.0.99, then stops: moving to the next minor (0.1.0) is a milestone, set by hand.
        public static string BumpPatch(string v)
        {
            var parts = (v ?? "").Split('.');
            if (parts.Length == 3 && int.TryParse(parts[2], out int patch))
            {
                if (patch >= 99)
                {
                    Debug.LogWarning("[Zappar CLI] Version " + v + " is the last patch of this series; set the next milestone version by hand.");
                    return v;
                }
                return parts[0] + "." + parts[1] + "." + (patch + 1);
            }
            return v;
        }
    }
}
