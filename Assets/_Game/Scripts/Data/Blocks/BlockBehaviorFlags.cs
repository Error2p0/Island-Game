using System;

namespace IslandGame.Data.Blocks
{
    /// <summary>
    /// Special-behavior markers a block can carry, as combinable flags so new
    /// behaviors never require new fields on BlockDefinition. Runtime systems
    /// query these (water sim reads Liquid, mesher may cross-plane Foliage,
    /// fire spread reads Flammable, ...). Append new flags with the next unused
    /// bit — never renumber existing ones, assets serialize the raw mask.
    /// </summary>
    [Flags]
    public enum BlockBehaviorFlags
    {
        None = 0,

        /// <summary>Fluid volume — no collision mesh, wading/swimming rules apply.</summary>
        Liquid = 1 << 0,

        /// <summary>Plant-like decoration (grass tufts, bushes) — typically non-solid and cross-rendered.</summary>
        Foliage = 1 << 1,

        /// <summary>Can catch and spread fire.</summary>
        Flammable = 1 << 2,

        /// <summary>Falls when unsupported (sand/gravel-style).</summary>
        FallsWithGravity = 1 << 3,

        /// <summary>Cannot be mined regardless of tool (world border / bedrock-style).</summary>
        Unbreakable = 1 << 4,

        /// <summary>
        /// Removed (converted to drops + debris) when its connected flagged
        /// region no longer touches any unflagged solid block — tree trunks
        /// and leaves. Region-based, unlike FallsWithGravity's per-block rule;
        /// see SupportCollapseSystem.
        /// </summary>
        NeedsSupport = 1 << 5,
    }
}
