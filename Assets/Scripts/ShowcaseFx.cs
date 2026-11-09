using UnityEngine;

// Spawns Cartoon FX prefabs scaled down to the tabletop size of the AR scene.
public static class ShowcaseFx
{
    public static void Spawn(GameObject prefab, Vector3 position, Transform parent, float scale)
    {
        if (prefab == null)
            return;

        GameObject fx = Object.Instantiate(prefab, position, Quaternion.identity, parent);
        fx.transform.localScale = Vector3.one * scale;
        // CFXR prefabs default to local scaling; Hierarchy makes the whole effect shrink with the transform.
        foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        }
        Object.Destroy(fx, 4f);
    }
}
