using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Combat
{
    /// <summary>Everything a hit needs to know, passed by readonly reference.</summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly DamageType Type;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;
        public readonly GameObject Source;

        public DamageInfo(float amount, DamageType type, Vector3 point, Vector3 direction, GameObject source)
        {
            Amount = amount;
            Type = type;
            Point = point;
            Direction = direction;
            Source = source;
        }
    }

    /// <summary>
    /// The minimal damage hook (Phase 9). Weapon hits call this on whatever
    /// they strike; future enemies, destructible props and station objects
    /// implement it — no health/combat system lives here by design, only the
    /// clean seam those systems will plug into.
    /// </summary>
    public interface IDamageable
    {
        void ApplyDamage(in DamageInfo damage);
    }
}
