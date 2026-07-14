using IslandGame.Data.Blocks;
using IslandGame.Data.Items;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// The effective mining parameters of whatever the player holds: a real
    /// tool's authored values, or the implicit BARE HANDS profile when no
    /// Tool-flagged item is equipped (organic-terrain phase 3). One resolver
    /// and one rule set, consumed by BOTH the mining action
    /// (PlayerBlockInteraction) and the radius preview (MiningRadiusIndicator)
    /// — so what the highlight shows is, by construction, what the bite does:
    /// same tier gate, same radius, same carve-center bias. Never special-case
    /// "no tool" anywhere else; resolve a profile and ask it.
    ///
    /// BARE HANDS: tier 0 (the existing permission system already blocks
    /// RequiredToolTier ≥ 1 materials like stone/ore outright for tier 0 —
    /// unchanged policy, now explicit), a small carve radius so hands dig
    /// soft dirt/sand organically instead of popping whole blocks, and a slow
    /// base rate. Tools keep base rate 1 with their authored efficiency
    /// multiplier on top, exactly as before this phase.
    /// </summary>
    public readonly struct MiningProfile
    {
        // The implicit bare-hands "virtual tool". Constants (not serialized
        // fields) because two systems read profiles independently — a single
        // compile-time source can never drift between action and preview.
        public const int BareHandTier = 0;
        public const float BareHandRadius = 0.55f;
        public const float BareHandSpeedMultiplier = 0.45f;

        /// <summary>The equipped tool; null for bare hands.</summary>
        public readonly ItemDefinition Tool;

        /// <summary>Permission tier (RequiredToolTier gate).</summary>
        public readonly int Tier;

        /// <summary>Carve-sphere radius in meters. ≤ 0 = classic whole-block mining (radius-less authored tools).</summary>
        public readonly float Radius;

        private readonly float baseSpeed;

        public bool IsBareHands => Tool == null;

        private MiningProfile(ItemDefinition tool, int tier, float radius, float speed)
        {
            Tool = tool;
            Tier = tier;
            Radius = radius;
            baseSpeed = speed;
        }

        /// <summary>The profile for an equipped item — bare hands for null or any non-tool item.</summary>
        public static MiningProfile Resolve(ItemDefinition equipped)
        {
            if (equipped != null && equipped.IsTool)
                return new MiningProfile(equipped, equipped.ToolTier, equipped.MiningRadius, 1f);

            return new MiningProfile(null, BareHandTier, BareHandRadius, BareHandSpeedMultiplier);
        }

        /// <summary>
        /// THE permission rule (aim gate): unbreakable-flagged blocks never
        /// mine, and a RequiredToolTier above this profile blocks outright —
        /// the pre-existing policy, now in its single shared home.
        /// </summary>
        public bool CanMine(BlockDefinition block)
        {
            return block != null
                   && !block.HasBehavior(BlockBehaviorFlags.Unbreakable)
                   && block.RequiredToolTier <= Tier;
        }

        /// <summary>
        /// Per-cell carve filter for the sphere (and its preview): the aim
        /// rule plus the geometry rules — liquids and non-solid decoration
        /// are never carved by tools.
        /// </summary>
        public bool CanCarve(BlockDefinition block)
        {
            return block != null
                   && block.IsSolid
                   && !block.HasBehavior(BlockBehaviorFlags.Liquid)
                   && CanMine(block);
        }

        /// <summary>Mining rate against a block: the profile's base rate times the tool's authored efficiency when it applies.</summary>
        public float SpeedAgainst(BlockDefinition block)
        {
            float speed = baseSpeed;
            if (Tool != null && Tool.IsEfficientAgainst(block))
                speed *= Tool.MiningSpeedMultiplier;
            return speed;
        }

        /// <summary>
        /// Carve-sphere center for a surface hit: biased a quarter-radius INTO
        /// the face so bites eat material, not air. Action and preview both
        /// call this — the bias can never drift between them.
        /// </summary>
        public Vector3 CarveCenter(Vector3 hitPoint, Vector3 hitNormal)
        {
            return hitPoint - hitNormal * (Radius * 0.25f);
        }
    }
}
