using System.Collections;
using UnityEngine;

namespace Showcase
{
    // Coroutine helpers for juicy feedback. Use with StartCoroutine(Tween.Punch(t)).
    public static class Tween
    {
        // Rest scale per transform while punches run, so overlapping punches (fast taps) never
        // capture a squashed scale as the base and leave the object deformed.
        private static readonly System.Collections.Generic.Dictionary<Transform, (Vector3 scale, int count)> s_rest =
            new System.Collections.Generic.Dictionary<Transform, (Vector3, int)>();

        public static IEnumerator Punch(Transform t, float amount = 0.18f, float time = 0.3f)
        {
            if (t == null) yield break;
            if (s_rest.TryGetValue(t, out var entry)) s_rest[t] = (entry.scale, entry.count + 1);
            else s_rest[t] = (t.localScale, 1);
            Vector3 baseScale = s_rest[t].scale;
            try
            {
                for (float a = 0f; a < 1f; a += Time.deltaTime / time)
                {
                    if (t == null) yield break;
                    float k = 1f + amount * Mathf.Sin(a * Mathf.PI) * (1f - a);
                    t.localScale = new Vector3(baseScale.x * k, baseScale.y / k, baseScale.z * k);
                    yield return null;
                }
            }
            finally
            {
                // Runs on completion and when the coroutine is stopped (e.g. the experience exits).
                if (t != null) t.localScale = baseScale;
                if (s_rest.TryGetValue(t, out var e))
                {
                    if (e.count <= 1) s_rest.Remove(t); else s_rest[t] = (e.scale, e.count - 1);
                }
            }
        }

        public static IEnumerator Hop(Transform t, float height, float time = 0.4f)
        {
            Vector3 start = t.localPosition;
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                if (t == null) yield break;
                t.localPosition = start + Vector3.up * (4f * height * a * (1f - a));
                yield return null;
            }
            if (t != null) t.localPosition = start;
        }

        public static IEnumerator MoveLocal(Transform t, Vector3 to, float time = 0.35f)
        {
            Vector3 from = t.localPosition;
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                if (t == null) yield break;
                t.localPosition = Vector3.LerpUnclamped(from, to, EaseOutBack(a));
                yield return null;
            }
            if (t != null) t.localPosition = to;
        }

        public static IEnumerator ScaleTo(Transform t, Vector3 to, float time = 0.25f)
        {
            Vector3 from = t.localScale;
            for (float a = 0f; a < 1f; a += Time.deltaTime / time)
            {
                if (t == null) yield break;
                t.localScale = Vector3.LerpUnclamped(from, to, EaseOutBack(a));
                yield return null;
            }
            if (t != null) t.localScale = to;
        }

        public static IEnumerator PopIn(Transform t, float time = 0.3f)
        {
            Vector3 to = t.localScale;
            t.localScale = to * 0.01f;
            yield return ScaleTo(t, to, time);
        }

        public static float EaseOutBack(float x)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }
    }
}
