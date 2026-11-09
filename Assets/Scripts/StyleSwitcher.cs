using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Cycles whole-screen looks by toggling renderer features (Flat Kit outline/pixelation and
// the 4-colour palette pass) on the active URP renderer. Hook Next() to a UI button.
public class StyleSwitcher : MonoBehaviour
{
    [System.Serializable]
    public class Style
    {
        public string Name;
        public string[] ActiveFeatures;
    }

    [SerializeField]
    private ScriptableRendererData m_renderer;
    [SerializeField]
    private Style[] m_styles;
    [SerializeField]
    private Text m_label;

    private int m_index;

    void Start()
    {
        Apply();
    }

    public void Next()
    {
        if (m_styles == null || m_styles.Length == 0)
            return;
        m_index = (m_index + 1) % m_styles.Length;
        Apply();
    }

    private void Apply()
    {
        if (m_renderer == null || m_styles == null || m_styles.Length == 0)
            return;

        Style style = m_styles[m_index];
        foreach (ScriptableRendererFeature feature in m_renderer.rendererFeatures)
        {
            if (feature == null || !IsManaged(feature.name))
                continue;
            feature.SetActive(System.Array.IndexOf(style.ActiveFeatures, feature.name) >= 0);
        }
        if (m_label != null)
            m_label.text = "Look: " + style.Name;
    }

    // Only touch features that some style mentions; anything else on the renderer is left alone.
    private bool IsManaged(string featureName)
    {
        foreach (Style s in m_styles)
            if (System.Array.IndexOf(s.ActiveFeatures, featureName) >= 0)
                return true;
        return false;
    }

#if UNITY_EDITOR
    // Feature toggles write to the renderer asset in the editor; don't leave Play-mode looks behind.
    void OnDestroy()
    {
        if (m_renderer == null || m_styles == null)
            return;
        foreach (ScriptableRendererFeature feature in m_renderer.rendererFeatures)
            if (feature != null && IsManaged(feature.name))
                feature.SetActive(false);
    }
#endif
}
