using System;
using System.Collections;
using UnityEngine;

namespace Showcase.Experiences.Room
{
    [Serializable]
    public class RoomVariant
    {
        public GameObject Model;
        public string Name;
        public int Price;
        public string Material;
        public Vector3 BaseScale;
    }

    // One furniture spot in the room. Every variant is a preloaded, hidden child; swapping only
    // toggles which one is visible, so nothing is instantiated from assets at runtime.
    public class RoomSlot : MonoBehaviour, ITappable
    {
        public string Category;
        public RoomVariant[] Variants;
        [Tooltip("Where the info card floats, in this slot's local space.")]
        public Vector3 CardOffset = new Vector3(0f, 0.6f, 0f);

        public int Index { get; private set; }
        public int SavedIndex { get; private set; } = -1;
        public RoomVariant Current => Variants[Index];
        public RoomVariant Saved => SavedIndex >= 0 ? Variants[SavedIndex] : null;
        public bool IsCurrentSaved => SavedIndex == Index;

        private RoomExperience m_owner;
        private Coroutine m_pop;

        private RoomExperience Owner => m_owner != null ? m_owner : (m_owner = GetComponentInParent<RoomExperience>(true));

        public void OnTap(RaycastHit hit) => Owner?.Select(this);

        public void ResetSlot()
        {
            SavedIndex = -1;
            Show(0, false);
        }

        public void Save() => SavedIndex = Index;

        public void Cycle(int step) => Show(((Index + step) % Variants.Length + Variants.Length) % Variants.Length, true);

        public void Show(int index, bool animate)
        {
            if (m_pop != null)
            {
                StopCoroutine(m_pop);
                m_pop = null;
            }
            Index = index;
            for (int i = 0; i < Variants.Length; i++)
            {
                var v = Variants[i];
                if (v.Model == null) continue;
                v.Model.transform.localScale = v.BaseScale;
                v.Model.SetActive(i == index);
            }
            if (animate && isActiveAndEnabled && Current.Model != null)
                m_pop = StartCoroutine(Pop(Current.Model.transform, Current.BaseScale));
        }

        private IEnumerator Pop(Transform t, Vector3 to)
        {
            for (float a = 0f; a < 1f; a += Time.deltaTime / 0.32f)
            {
                t.localScale = Vector3.LerpUnclamped(to * 0.05f, to, Tween.EaseOutBack(a));
                yield return null;
            }
            t.localScale = to;
            m_pop = null;
        }
    }
}
