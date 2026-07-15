using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// The swappable "what does dying cost" strategy — a ScriptableObject so
    /// the choice is an ASSET assignment on PlayerRespawnController, tunable
    /// in the inspector and replaceable without touching the respawn flow
    /// (this is exactly the knob that gets redesigned during balancing:
    /// drop-everything, drop-backpack, durability-loss and skill-drain
    /// variants all fit behind this one call).
    ///
    /// CONTRACT: called exactly once per death, at the death location, BEFORE
    /// the death screen shows. Whatever the policy drops must be real,
    /// persistent world state (a registered placed piece, world items) — the
    /// save system only knows how to walk existing registries. Returns the
    /// world object holding recoverable loot (the death marker points at it),
    /// or null when nothing was dropped.
    /// </summary>
    public abstract class DeathPenaltyPolicy : ScriptableObject
    {
        public abstract Transform Apply(GameObject player, Vector3 deathPosition);
    }
}
