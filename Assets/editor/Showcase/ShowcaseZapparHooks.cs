using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Showcase.EditorTools
{
    // Plugs this project into the Zappar tooling: "Rebuild scene for targets" (window, menu, Unity CLI,
    // batch) runs the full Showcase scene build, which gives every trained target a tracker.
    [InitializeOnLoad]
    static class ShowcaseZapparHooks
    {
        static ShowcaseZapparHooks()
        {
            ZapparCliTools.ZapparTargets.SceneRebuilder = Rebuild;
        }

        static string Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "Leave Play mode first";
            // The build opens the Showcase scene in place of the current one.
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return "Rebuild cancelled";
            ShowcaseSceneBuilder.BuildAll();
            var built = ZapparCliTools.ZapparCliSettings.Instance.builtTargets;
            return "Scene rebuilt (" + ShowcaseSceneBuilder.ScenePath + ") with " + built.Length + " target(s): " + string.Join(", ", built);
        }
    }
}
