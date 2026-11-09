using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Showcase
{
    // Routes image targets to experiences.
    //  - Every target has its own Zappar tracker; whichever is seen first anchors World (World moves under
    //    that tracker and the Zappar camera uses it as origin) until it is lost.
    //  - Seeing a target for the first time, or a different target, opens the experience mapped to it.
    //  - The mapping comes from, strongest first: the page URL (?exp=room for every target, or
    //    ?<targetId>=room for one), the remote JSON at m_remoteConfigUrl, then the values baked in at build.
    // Tracker events are wired to OnSeen/OnLost(index) by the scene builder.
    public class ShowcaseTargets : MonoBehaviour
    {
        public const string HubId = "hub";

        public static ShowcaseTargets Instance { get; private set; }
        public static event Action ConfigChanged;

        [SerializeField] private List<ShowcaseTarget> m_targets = new List<ShowcaseTarget>();
        [Tooltip("JSON with {defaultExperience, targets:[{id, experience}]}; empty = no remote config.")]
        [SerializeField] private string m_remoteConfigUrl = "";
        [SerializeField] private float m_remoteTimeout = 6f;
        [Tooltip("Project-relative copy of the remote JSON; the Editor reads this instead, so local edits show before they are pushed.")]
        [SerializeField] private string m_localConfigFile = "";

        private bool[] m_visible;
        private Zappar.ZapparCamera m_zapparCamera;

        public IReadOnlyList<ShowcaseTarget> Targets => m_targets;
        public ShowcaseTarget Active { get; private set; }
        public string ConfigSource { get; private set; } = "built-in";

        void Awake()
        {
            Instance = this;
            m_visible = new bool[m_targets.Count];
            gameObject.AddComponent<TargetScanScreen>();
            ApplyUrlOverrides();
        }

        IEnumerator Start()
        {
            var rig = ShowcaseRig.Instance;
            if (rig != null && rig.ArCamera != null)
                m_zapparCamera = rig.ArCamera.GetComponent<Zappar.ZapparCamera>();
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(m_localConfigFile) && System.IO.File.Exists(m_localConfigFile))
            {
                ApplyJson(System.IO.File.ReadAllText(m_localConfigFile), "local");
                yield break;
            }
#endif
            if (!string.IsNullOrEmpty(m_remoteConfigUrl))
                yield return FetchRemoteConfig();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

#if UNITY_EDITOR
        // Scene builder only.
        public void Configure(List<ShowcaseTarget> targets, string remoteConfigUrl, string localConfigFile)
        {
            m_targets = targets;
            m_remoteConfigUrl = remoteConfigUrl;
            m_localConfigFile = localConfigFile;
        }
#endif

        // ---------- tracker events ----------

        public void OnSeen(int index)
        {
            if (index < 0 || index >= m_targets.Count)
                return;
            m_visible[index] = true;
            var target = m_targets[index];
            // Keep the target we are already on while it is still in view.
            if (Active != null && Active != target && m_visible[m_targets.IndexOf(Active)])
                return;
            var rig = ShowcaseRig.Instance;
            bool firstSight = rig == null || rig.IsScanning;
            bool switched = Active != target;
            Active = target;
            if (switched)
                Anchor(target);
            rig?.SetTracking(true);
            if (switched || firstSight)
                OpenExperience(target.Experience);
        }

        public void OnLost(int index)
        {
            if (index < 0 || index >= m_targets.Count)
                return;
            m_visible[index] = false;
            if (Active != m_targets[index])
                return;
            // Hand over to another target that is still in view, otherwise fall back to the free camera.
            for (int i = 0; i < m_targets.Count; i++)
                if (m_visible[i])
                {
                    OnSeen(i);
                    return;
                }
            ShowcaseRig.Instance?.SetTracking(false);
        }

        // World (and everything in it) follows the tracker that is anchoring the camera.
        private void Anchor(ShowcaseTarget target)
        {
            var rig = ShowcaseRig.Instance;
            if (rig == null || target.Tracker == null)
                return;
            // Move World's top-level holder, keeping its local offset/scale on the target.
            Transform content = rig.World;
            while (content.parent != null && content.parent.GetComponent<Zappar.ZapparImageTrackingTarget>() == null)
                content = content.parent;
            if (content.parent != target.Tracker.transform)
                content.SetParent(target.Tracker.transform, false);
            if (m_zapparCamera != null)
                m_zapparCamera.AnchorOrigin = target.Tracker;
        }

        public static void OpenExperience(string id)
        {
            var hub = ShowcaseHub.Instance;
            var exp = FindExperience(id);
            if (exp == null)
            {
                if (!string.IsNullOrEmpty(id) && id != HubId)
                    Debug.LogWarning("[Showcase] Unknown experience '" + id + "', showing the hub");
                if (hub != null && hub.Current != null)
                    hub.ReturnToHub();
                return;
            }
            if (hub != null && hub.Current != exp)
                hub.Open(exp);
        }

        public static ShowcaseExperience FindExperience(string id)
        {
            if (string.IsNullOrEmpty(id) || id == HubId)
                return null;
            foreach (var e in FindObjectsByType<ShowcaseExperience>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        public static string TitleOf(string id)
        {
            var e = FindExperience(id);
            return e != null ? e.Title : "Showcase hub (all experiences)";
        }

        // ---------- configuration ----------

        private IEnumerator FetchRemoteConfig()
        {
            // The query string defeats browser caching; the host may still cache for a few minutes.
            string url = m_remoteConfigUrl + (m_remoteConfigUrl.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks;
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = Mathf.CeilToInt(m_remoteTimeout);
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[Showcase] Remote config not loaded (" + req.error + "), using built-in mapping");
                    yield break;
                }
                ApplyJson(req.downloadHandler.text, "remote");
            }
        }

        private void ApplyJson(string json, string source)
        {
            ShowcaseRemoteConfig config = null;
            try { config = JsonUtility.FromJson<ShowcaseRemoteConfig>(json); }
            catch (Exception e) { Debug.LogWarning("[Showcase] " + source + " config is not valid JSON: " + e.Message); }
            if (config == null)
                return;
            Apply(config);
            ConfigSource = source;
            // The URL is the most specific instruction, so it still wins over the config file.
            ApplyUrlOverrides();
            Debug.Log("[Showcase] " + source + " config applied: " + Describe());
            ConfigChanged?.Invoke();
        }

        public void Apply(ShowcaseRemoteConfig config)
        {
            foreach (var t in m_targets)
            {
                string exp = config.defaultExperience;
                if (config.targets != null)
                    foreach (var rt in config.targets)
                        if (rt != null && string.Equals(rt.id, t.Id, StringComparison.OrdinalIgnoreCase))
                            exp = rt.experience;
                if (!string.IsNullOrEmpty(exp))
                    t.Experience = exp.Trim();
            }
        }

        private void ApplyUrlOverrides()
        {
            var query = ParseQuery(Application.absoluteURL);
            if (query.Count == 0)
                return;
            bool changed = false;
            foreach (var t in m_targets)
            {
                if (query.TryGetValue(t.Id.ToLowerInvariant(), out var own)) { t.Experience = own; changed = true; }
                else if (query.TryGetValue("exp", out var all)) { t.Experience = all; changed = true; }
            }
            if (changed)
            {
                ConfigSource = "url";
                ConfigChanged?.Invoke();
            }
        }

        private static Dictionary<string, string> ParseQuery(string url)
        {
            var result = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(url))
                return result;
            int q = url.IndexOf('?');
            if (q < 0)
                return result;
            string query = url.Substring(q + 1);
            int hash = query.IndexOf('#');
            if (hash >= 0)
                query = query.Substring(0, hash);
            foreach (var pair in query.Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0)
                    continue;
                result[UnityWebRequest.UnEscapeURL(pair.Substring(0, eq)).ToLowerInvariant()] = UnityWebRequest.UnEscapeURL(pair.Substring(eq + 1)).Trim();
            }
            return result;
        }

        private string Describe()
        {
            var parts = new List<string>();
            foreach (var t in m_targets)
                parts.Add(t.Id + "→" + t.Experience);
            return string.Join(", ", parts);
        }
    }
}
