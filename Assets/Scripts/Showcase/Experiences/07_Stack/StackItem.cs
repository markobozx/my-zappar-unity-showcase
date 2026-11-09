using System;
using UnityEngine;

namespace Showcase.Experiences.Stack
{
    public enum StackKind { Crate, Box, Barrel, Basket, AppleCrate, NovaBox }

    public enum StackState { Pooled, Hover, Falling, Settled, Lost }

    // One droppable delivery item (pooled by StackDeliveryExperience). The template lives inactive in
    // the scene with a fitted BoxCollider on its root and the mesh under `Visual`, centred on the root.
    public class StackItem : MonoBehaviour
    {
        public StackKind Kind;
        public float Mass = 1f;
        public Transform Visual;

        [NonSerialized] public StackDeliveryExperience Owner;
        [NonSerialized] public Rigidbody Body;
        [NonSerialized] public BoxCollider Box;
        [NonSerialized] public StackState State;
        [NonSerialized] public float DropTime;
        [NonSerialized] public float StillTime;
        [NonSerialized] public bool Landed;
        [NonSerialized] public bool Frozen;
        [NonSerialized] public Vector3 VisualScale = Vector3.one;
        [NonSerialized] public Coroutine Anim;

        void OnCollisionEnter(Collision c)
        {
            if (Owner != null)
                Owner.OnItemCollision(this, c);
        }
    }
}
