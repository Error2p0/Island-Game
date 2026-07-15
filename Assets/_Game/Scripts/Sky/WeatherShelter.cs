using UnityEngine;

namespace IslandGame.Sky
{
    /// <summary>
    /// The one "am I under a roof" answer, shared by the player's warmth
    /// rules and the storm campfire hazard: a single upward ray against every
    /// solid collider. Placed building pieces AND terrain both carry real
    /// colliders, so player roofs, ruin ceilings, cave roofs and organic
    /// overhangs all count as shelter with zero special cases — matching
    /// what the rain particles' world collision shows the player.
    ///
    /// Deliberately one ray, not a cone: a roof tile directly overhead is
    /// what stops rain, and survival ticks call this 4×/s — precision isn't
    /// worth a multi-ray fan here. A creature standing exactly overhead
    /// technically counts for a tick; harmless and self-correcting.
    /// </summary>
    public static class WeatherShelter
    {
        private const float ProbeHeight = 60f;

        private static readonly RaycastHit[] Buffer = new RaycastHit[16];

        /// <summary>
        /// True when any solid collider sits above the position. ignoreRoot
        /// excludes the asker's own hierarchy (the player's rig colliders,
        /// a campfire's own prefab) from counting as its roof.
        /// </summary>
        public static bool IsSheltered(Vector3 position, Transform ignoreRoot = null)
        {
            Vector3 origin = position + Vector3.up * 0.4f;
            int hitCount = Physics.RaycastNonAlloc(
                origin, Vector3.up, Buffer, ProbeHeight, ~0, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                if (ignoreRoot != null && Buffer[i].collider.transform.IsChildOf(ignoreRoot))
                    continue;

                return true;
            }

            return false;
        }
    }
}
