using System;
using UnityEngine;

namespace Showcase.Experiences.Treasure
{
    public enum TreasureKind { Gold, Coins, Gems, Golden, Bomb }

    // One falling treasure (pooled by TreasureRainExperience). The template lives inactive in the
    // scene; runtime copies get their colliders and Rigidbody from the experience.
    public class TreasureItem : MonoBehaviour, ITappable
    {
        public TreasureKind Kind;

        [NonSerialized] public TreasureRainExperience Owner;
        [NonSerialized] public Rigidbody Body;
        [NonSerialized] public float SpawnTime;
        [NonSerialized] public bool Live;
        [NonSerialized] public Vector3 BaseScale;

        public int Value
        {
            get
            {
                switch (Kind)
                {
                    case TreasureKind.Gold: return 1;
                    case TreasureKind.Coins: return 2;
                    case TreasureKind.Gems: return 5;
                    case TreasureKind.Golden: return 20;
                    default: return -10;
                }
            }
        }

        public void OnTap(RaycastHit hit)
        {
            if (Owner != null && Live)
                Owner.Collect(this, hit.point);
        }
    }
}
