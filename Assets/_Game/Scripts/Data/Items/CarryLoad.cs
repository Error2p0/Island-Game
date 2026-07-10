using UnityEngine;

namespace IslandGame.Data.Items
{
    /// <summary>
    /// Bridge between authored item weights and the movement system's carry
    /// model. PlayerLocomotion already owns the load concept as a normalized
    /// 0–1 value (SetCarryWeight / CarryWeight01) that scales speed and
    /// acceleration and gates sprint/prone — that stays the single source of
    /// truth for how load FEELS.
    ///
    /// Items author real kilograms (ItemDefinition.WeightKg). The inventory
    /// phase sums kilograms across all slots and converts here:
    ///
    ///     locomotion.SetCarryWeight(CarryLoad.ToNormalized(totalKg, capacityKg));
    ///
    /// capacityKg (the "1.0 load" point) is a player-progression stat and will
    /// be authored on the inventory settings asset in the inventory phase.
    /// </summary>
    public static class CarryLoad
    {
        /// <summary>Total kilograms of one inventory stack.</summary>
        public static float GetStackWeightKg(ItemDefinition item, int count)
        {
            return item == null ? 0f : item.WeightKg * Mathf.Max(0, count);
        }

        /// <summary>
        /// Kilograms → the 0–1 load PlayerLocomotion.SetCarryWeight expects.
        /// Clamped: loads past capacity read as 1 (max penalty), they don't
        /// scale further — over-encumbrance rules, if any, come later.
        /// </summary>
        public static float ToNormalized(float totalWeightKg, float capacityKg)
        {
            if (capacityKg <= 0f)
                return 0f;

            return Mathf.Clamp01(totalWeightKg / capacityKg);
        }
    }
}
