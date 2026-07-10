using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// The one camera-aim raycast every interaction system shares (block
    /// mining/placing, building placement, deconstruction): nearest non-trigger
    /// hit along the camera forward that is NOT part of the player's own rig —
    /// looking down, the ray legally passes through the rig's visual colliders
    /// (legs, feet) and must skip them instead of letting them eat the aim.
    /// Extracted from PlayerBlockInteraction in the building phase so the rule
    /// lives in exactly one place; callers apply their own filters (chunk-only,
    /// building-piece-only, ...) on the returned hit.
    /// </summary>
    public static class PlayerAimRaycast
    {
        /// <summary>
        /// Casts from origin along its forward. buffer is the caller-owned
        /// scratch array (its length caps how many overlapping colliders can be
        /// considered; 16 is plenty for a first-person aim ray). Returns the
        /// nearest hit outside ignoreRoot's hierarchy, or false.
        /// </summary>
        public static bool Raycast(
            Transform origin, Transform ignoreRoot, float reach, RaycastHit[] buffer, out RaycastHit hit)
        {
            int hitCount = Physics.RaycastNonAlloc(
                origin.position, origin.forward, buffer, reach, ~0, QueryTriggerInteraction.Ignore);

            int best = -1;
            for (int i = 0; i < hitCount; i++)
            {
                if (buffer[i].collider.transform.IsChildOf(ignoreRoot))
                    continue;
                if (best < 0 || buffer[i].distance < buffer[best].distance)
                    best = i;
            }

            if (best < 0)
            {
                hit = default;
                return false;
            }

            hit = buffer[best];
            return true;
        }
    }
}
