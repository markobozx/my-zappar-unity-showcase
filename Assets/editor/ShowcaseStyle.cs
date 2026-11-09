using UnityEditor;
using UnityEngine;

// One place to art-direct every material converted to Flat Kit's Stylized Surface.
// Edit the asset (Assets/Showcase/Style/ShowcaseStyle.asset) and press "Apply to all materials".
// Each material keeps its own base colour/texture; the style controls how it is shaded.
[CreateAssetMenu(fileName = "ShowcaseStyle", menuName = "Showcase/Style")]
public class ShowcaseStyle : ScriptableObject
{
    public enum CelMode { None = 0, Single = 1, Steps = 2, Curve = 3 }

    [Header("Cel shading")]
    public CelMode Mode = CelMode.Steps;
    [Range(2, 8)] public int Steps = 3;
    [Tooltip("Shaded side = the material's own colour multiplied by this.")]
    public Color ShadeTint = new Color(0.62f, 0.6f, 0.78f);
    [Range(0, 1)] public float SelfShadingSize = 0.45f;
    [Range(0, 1)] public float ShadowEdgeSize = 0.05f;
    [Range(0, 1)] public float Flatness = 0.85f;

    [Header("Rim light")]
    public bool Rim = true;
    public Color RimColor = new Color(1f, 1f, 1f, 0.35f);
    [Range(0, 1)] public float RimSize = 0.25f;

    [Header("Specular")]
    public bool Specular = false;
    public Color SpecularColor = new Color(1f, 1f, 1f, 0.6f);
    [Range(0, 1)] public float SpecularSize = 0.1f;

    [Header("Height gradient")]
    public bool HeightGradient = false;
    public Color GradientColor = new Color(0.75f, 0.85f, 1f);
    public float GradientSize = 1f;

    [Header("Cast shadows (Unity)")]
    public bool ColoredShadows = true;
    public Color ShadowColor = new Color(0.32f, 0.3f, 0.5f);
    [Range(0, 1)] public float ShadowSharpness = 0.6f;

    [Header("Per-object outline (vertex extrusion)")]
    public bool Outline = false;
    public Color OutlineColor = new Color(0.12f, 0.1f, 0.18f);
    public float OutlineWidth = 1.2f;
}

[CustomEditor(typeof(ShowcaseStyle))]
public class ShowcaseStyleEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();
        if (GUILayout.Button("Apply to all materials", GUILayout.Height(30)))
            FlatKitConversion.ApplyStyleToAll();
        EditorGUILayout.HelpBox("Applies to every material converted by Zappar > Showcase > Convert Materials To Flat Kit.", MessageType.None);
    }
}
