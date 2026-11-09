using System;
using UnityEngine;

namespace Showcase
{
    // One trained image target and what it opens. Filled in by the scene builder from
    // StreamingAssets/<Id>.zpt + Assets/Showcase/Targets/<Id>.png; Experience can be changed remotely.
    [Serializable]
    public class ShowcaseTarget
    {
        public string Id;                       // the .zpt name without extension; remote config and URLs use it
        public string Experience = ShowcaseTargets.HubId;
        public Texture2D Image;                 // shown on the scan screen (WebGL can't read the preview out of the .zpt)
        public Zappar.ZapparImageTrackingTarget Tracker;
    }

    // Shape of RemoteConfig/showcase-config.json (JsonUtility has no dictionaries, hence the arrays).
    [Serializable]
    public class ShowcaseRemoteConfig
    {
        public string defaultExperience;
        public ShowcaseRemoteTarget[] targets;
    }

    [Serializable]
    public class ShowcaseRemoteTarget
    {
        public string id;
        public string experience;
    }
}
