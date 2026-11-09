using UnityEngine;

namespace Showcase
{
    public enum FxKind { Hit, Poof, Pickup, Sparkle, Confetti, Smoke, Explosion, Heart, Stars, Magic }

    // Cartoon FX prefabs used across experiences, assigned by the scene builder.
    // Play() shrinks effects to the tabletop scale of World.
    [CreateAssetMenu(fileName = "ShowcaseFxLibrary", menuName = "Showcase/FX Library")]
    public class ShowcaseFxLibrary : ScriptableObject
    {
        public GameObject Hit, Poof, Pickup, Sparkle, Confetti, Smoke, Explosion, Heart, Stars, Magic;
        public float DefaultScale = 0.25f;

        public GameObject Get(FxKind kind)
        {
            switch (kind)
            {
                case FxKind.Hit: return Hit;
                case FxKind.Poof: return Poof;
                case FxKind.Pickup: return Pickup;
                case FxKind.Sparkle: return Sparkle;
                case FxKind.Confetti: return Confetti;
                case FxKind.Smoke: return Smoke;
                case FxKind.Explosion: return Explosion;
                case FxKind.Heart: return Heart;
                case FxKind.Stars: return Stars;
                default: return Magic;
            }
        }
    }

    public static class Fx
    {
        // worldPosition in world space; scale is relative to the library default (1 = normal).
        public static void Play(FxKind kind, Vector3 worldPosition, float scale = 1f)
        {
            ShowcaseRig rig = ShowcaseRig.Instance;
            if (rig == null || rig.Fx == null)
                return;
            GameObject prefab = rig.Fx.Get(kind);
            if (prefab == null)
                return;

            Transform parent = rig.World;
            GameObject fx = Object.Instantiate(prefab, worldPosition, Quaternion.identity, parent);
            float s = rig.Fx.DefaultScale * scale;
            fx.transform.localScale = Vector3.one * s;
            foreach (ParticleSystem ps in fx.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
            // Some Cartoon FX prefabs shake the camera or add lights; neither suits AR on a phone.
            foreach (Light l in fx.GetComponentsInChildren<Light>(true))
                l.enabled = false;
            foreach (MonoBehaviour mb in fx.GetComponentsInChildren<MonoBehaviour>(true))
                if (mb.GetType().Name == "CFXR_Effect")
                    SetField(mb, "cameraShake", null);
            Object.Destroy(fx, 4f);
        }

        private static void SetField(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null && !f.FieldType.IsValueType)
            {
                // cameraShake is an object with an "enabled" bool in CFXR; turn it off rather than null it.
                object shake = f.GetValue(target);
                var enabled = shake?.GetType().GetField("enabled");
                if (enabled != null)
                    enabled.SetValue(shake, false);
            }
        }
    }
}
