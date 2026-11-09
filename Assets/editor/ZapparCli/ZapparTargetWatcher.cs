using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZapparCliTools
{
    // Watches the target folders. A new image in targetImagesFolder is trained automatically
    // (interactive Editor, autoTrainNewImages on); any change to images or .zpt files lights up
    // "Rebuild scene" in the Zappar CLI window and logs what changed.
    public class ZapparTargetWatcher : AssetPostprocessor
    {
        static bool s_scheduled;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            var s = ZapparCliSettings.Instance;
            bool Relevant(string p) =>
                (p.StartsWith(s.targetImagesFolder + "/") && ZapparTargets.IsImage(p)) ||
                (p.StartsWith(s.targetsFolder + "/", System.StringComparison.OrdinalIgnoreCase) && p.EndsWith(".zpt"));
            if (!imported.Concat(deleted).Concat(moved).Concat(movedFrom).Any(Relevant))
                return;
            if (s_scheduled)
                return;
            s_scheduled = true;
            // Never block inside an import callback; train after the import finishes.
            EditorApplication.delayCall += Process;
        }

        static void Process()
        {
            s_scheduled = false;
            var s = ZapparCliSettings.Instance;
            if (s.autoTrainNewImages && !Application.isBatchMode)
            {
                var untrained = ZapparTargets.Scan().Where(t => !t.Trained && !ZapparTargets.TrainingNow.Contains(t.ImagePath)).ToList();
                if (untrained.Count > 0)
                    foreach (var line in ZapparTargets.TrainMissing())
                        Debug.Log("[Zappar] Auto-train: " + line);
            }
            if (ZapparTargets.RebuildNeeded(out string reason))
                Debug.Log("[Zappar] Image targets changed (" + reason + "). Rebuild the scene: Zappar > Rebuild Scene For Targets, or the button in Zappar CLI > Image Targets.");
            ZapparTargets.NotifyChanged();
        }

        [MenuItem("Zappar/Rebuild Scene For Targets", false, 20)]
        static void RebuildMenu() => Debug.Log("[Zappar] " + ZapparTargets.RebuildScene());

        [MenuItem("Zappar/Train Untrained Target Images", false, 21)]
        static void TrainMenu()
        {
            var report = ZapparTargets.TrainMissing();
            Debug.Log("[Zappar] " + (report.Count == 0 ? "Every target image is trained" : string.Join("\n", report)));
        }
    }
}
