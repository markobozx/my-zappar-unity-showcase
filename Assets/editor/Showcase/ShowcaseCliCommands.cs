using System;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEngine;

namespace Showcase.EditorTools
{
    // Showcase commands on the Unity CLI (see ZapparCliCommands for how to call them).
    static class ShowcaseCliCommands
    {
        [CliCommand("showcase_experiences", "List experience ids (use them for target mapping and ?exp= links)")]
        static string Experiences() =>
            ShowcaseTargets.HubId + " (the hub with all pedestals)\n" +
            string.Join("\n", ShowcaseSceneBuilder.FindBuilders().Select(b => b.attr.Id + " (" + b.attr.Title + ")"));

        [CliCommand("showcase_set_target_experience", "Set which experience a target opens in RemoteConfig/showcase-config.json (live after commit + push; baked in on the next scene rebuild)")]
        static string SetTargetExperience(
            [CliArg("target", "Target id (image name), or * for the default of all targets", Required = true)] string target,
            [CliArg("experience", "Experience id, or hub", Required = true)] string experience)
        {
            var ids = ShowcaseSceneBuilder.FindBuilders().Select(b => b.attr.Id).Append(ShowcaseTargets.HubId).ToList();
            if (!ids.Contains(experience)) throw new ArgumentException("Unknown experience '" + experience + "'. Known: " + string.Join(", ", ids));
            string file = ShowcaseSceneBuilder.RemoteConfigFile;
            var config = File.Exists(file) ? JsonUtility.FromJson<ShowcaseRemoteConfig>(File.ReadAllText(file)) : new ShowcaseRemoteConfig();
            if (target == "*") config.defaultExperience = experience;
            else
            {
                var list = (config.targets ?? new ShowcaseRemoteTarget[0]).ToList();
                var entry = list.FirstOrDefault(t => t.id == target);
                if (entry == null) list.Add(entry = new ShowcaseRemoteTarget { id = target });
                entry.experience = experience;
                config.targets = list.ToArray();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllText(file, JsonUtility.ToJson(config, true) + "\n");
            return file + ": " + target + " → " + experience + ". Commit and push it to change the live app (GitHub caches up to 5 min).";
        }
    }
}
