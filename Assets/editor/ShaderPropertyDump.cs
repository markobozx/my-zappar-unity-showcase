using System.Linq;
using UnityEditor;
using UnityEngine;

// Logs the properties of shaders used by imported packs, to plan material conversions.
// Batch: -executeMethod ShaderPropertyDump.Run    Lines start with [SHADERDUMP].
public static class ShaderPropertyDump
{
    public static void Run()
    {
        var names = new[] { "FlatKit/Stylized Surface", "Synty/Generic_Basic", "Synty/Generic_Standard", "Synty/SyntyStudios_Building", "Synty/PolygonShader", "Synty/Generic_Basic_Specular" };
        foreach (var n in names)
        {
            var s = Shader.Find(n);
            if (s == null) { Debug.Log("[SHADERDUMP] MISSING " + n); continue; }
            var props = Enumerable.Range(0, s.GetPropertyCount()).Select(i => s.GetPropertyName(i) + ":" + s.GetPropertyType(i));
            Debug.Log("[SHADERDUMP] " + n + " => " + string.Join(", ", props));
        }
        foreach (var s in AssetDatabase.FindAssets("t:Shader", new[] { "Assets/FlatKit" }).Select(g => AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(g))).Where(s => s != null))
            Debug.Log("[SHADERDUMP] FLATKIT SHADER " + s.name);
        // One sample Synty material: which texture/colour slots are actually filled.
        var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(AssetDatabase.FindAssets("t:Material PolygonNightclubs_Mat_01", new[] { "Assets/Synty" }).FirstOrDefault() ?? ""));
        if (mat != null)
            Debug.Log("[SHADERDUMP] SAMPLE " + mat.name + " (" + mat.shader.name + ") textures: " + string.Join(", ", mat.GetTexturePropertyNames().Where(t => mat.GetTexture(t) != null).Select(t => t + "=" + mat.GetTexture(t).name)));
    }
}
