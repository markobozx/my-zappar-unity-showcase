using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

// Audits materials of imported asset packs for URP compatibility and converts plain
// Built-in "Standard" materials to URP Lit, keeping their textures and colours.
// Batch: -executeMethod ImportedAssetsCheck.Run    Lines start with [ASSETCHECK].
public static class ImportedAssetsCheck
{
    static readonly string[] Roots = { "Assets/KayKit", "Assets/Cosmic_Retro_Mega_Bundle", "Assets/ithappy", "Assets/Synty", "Assets/JMO Assets" };

    static void Log(string msg) { Debug.Log("[ASSETCHECK] " + msg); }

    [MenuItem("Zappar/Showcase/Check Imported Asset Materials")]
    public static void Run()
    {
        var roots = Roots.Where(AssetDatabase.IsValidFolder).ToArray();
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        var byShader = new Dictionary<string, int>();
        var broken = new List<string>();
        int converted = 0;

        foreach (var guid in AssetDatabase.FindAssets("t:Material", roots))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            string shader = mat.shader == null ? "(none)" : mat.shader.name;
            if (shader == "Standard" && urpLit != null)
            {
                // Snapshot before switching: _MainTex/_Color are unreadable once the shader changes.
                var tex = mat.GetTexture("_MainTex");
                var scale = mat.GetTextureScale("_MainTex");
                var offset = mat.GetTextureOffset("_MainTex");
                var color = mat.GetColor("_Color");
                var normal = mat.GetTexture("_BumpMap");
                var emissionMap = mat.GetTexture("_EmissionMap");
                var emission = mat.GetColor("_EmissionColor");
                float gloss = mat.GetFloat("_Glossiness");
                float metallic = mat.GetFloat("_Metallic");

                mat.shader = urpLit;
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", scale);
                mat.SetTextureOffset("_BaseMap", offset);
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", gloss);
                mat.SetFloat("_Metallic", metallic);
                if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
                if (emissionMap != null || emission.maxColorComponent > 0.01f)
                {
                    mat.SetTexture("_EmissionMap", emissionMap);
                    mat.SetColor("_EmissionColor", emission);
                    mat.EnableKeyword("_EMISSION");
                }
                EditorUtility.SetDirty(mat);
                if (tex != null && mat.GetTexture("_BaseMap") == null) Log("ERROR texture lost converting " + path);
                converted++;
                shader = mat.shader.name;
            }

            if ((shader.StartsWith("Legacy Shaders/Particles/") || shader == "Particles/Standard Unlit") && ConvertToUrpParticle(mat, shader))
            {
                converted++;
                shader = mat.shader.name;
            }

            byShader[shader] = byShader.TryGetValue(shader, out int n) ? n + 1 : 1;
            if (shader == "Hidden/InternalErrorShader" || shader == "(none)" || shader.StartsWith("Legacy Shaders/") || shader == "Standard" || shader.StartsWith("Particles/"))
                broken.Add(path + " [" + shader + "]");
        }

        AssetDatabase.SaveAssets();
        Log("Converted to URP: " + converted);
        foreach (var kv in byShader.OrderByDescending(kv => kv.Value))
            Log("SHADER " + kv.Value + " x " + kv.Key);
        Log("NOT URP-COMPATIBLE: " + broken.Count);
        foreach (var b in broken.Take(60)) Log("  " + b);
    }

    // Legacy additive/standard particle materials (fog, lasers, light rays) -> URP Particles/Unlit,
    // kept transparent and additive rather than forced onto opaque Lit.
    static bool ConvertToUrpParticle(Material mat, string sourceShader)
    {
        var urpParticle = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (urpParticle == null) return false;

        var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
        var scale = mat.HasProperty("_MainTex") ? mat.GetTextureScale("_MainTex") : Vector2.one;
        var offset = mat.HasProperty("_MainTex") ? mat.GetTextureOffset("_MainTex") : Vector2.zero;
        // Legacy additive particles multiply _TintColor by 2.
        var tint = mat.HasProperty("_TintColor") ? mat.GetColor("_TintColor") * 2f
                 : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
        tint.a = Mathf.Clamp01(tint.a);

        mat.shader = urpParticle;
        mat.SetTexture("_BaseMap", tex);
        mat.SetTextureScale("_BaseMap", scale);
        mat.SetTextureOffset("_BaseMap", offset);
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetFloat("_Blend", 2f);     // Additive
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        if (tex != null && mat.GetTexture("_BaseMap") == null) Log("ERROR texture lost converting " + AssetDatabase.GetAssetPath(mat));
        Log("Particle " + AssetDatabase.GetAssetPath(mat) + ": " + sourceShader + " -> " + urpParticle.name);
        return true;
    }
}
