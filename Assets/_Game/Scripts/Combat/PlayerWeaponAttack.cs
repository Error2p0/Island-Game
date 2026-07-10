using IslandGame.Data.Items;
using IslandGame.Held;
using IslandGame.Inventory;
using IslandGame.Player;
using IslandGame.Terrain;
using UnityEngine;

namespace IslandGame.Combat
{
    /// <summary>
    /// Use-button orchestration for the held item, split cleanly across the
    /// existing systems instead of duplicating them:
    ///
    ///   - SWING VISUALS: while the use button is held with any holdable item
    ///     equipped, the generic Use animation fires at the item's cadence
    ///     (AttacksPerSecond; tools without a rate swing at 1/s) — this also
    ///     covers the mining loop's visuals.
    ///   - MINING: entirely owned by Phase 6's PlayerBlockInteraction, which
    ///     already enforces the Phase 3 tier/efficiency data — nothing here
    ///     touches blocks. When the crosshair is on voxel terrain within the
    ///     weapon's reach, damage is skipped so mining is the one consumer.
    ///   - DAMAGE: for Weapon-flagged items aiming at non-terrain, a sphere
    ///     cast from the camera (radius for swing forgiveness, range =
    ///     AttackRange) applies WeaponDamage/DamageType to the first
    ///     IDamageable hit, at the same cadence.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class PlayerWeaponAttack : MonoBehaviour
    {
        private const float FallbackAttacksPerSecond = 1f;

        [Tooltip("Sphere-cast radius for hit detection — forgiveness for melee swings.")]
        [SerializeField] private float hitRadius = 0.25f;

        private PlayerReferences references;
        private ItemHoldController holdController;
        private HotbarSelector selector;

        private float nextSwingTime;
        private float nextHitTime;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            holdController = GetComponent<ItemHoldController>();
            selector = GetComponent<HotbarSelector>();
        }

        private void Update()
        {
            bool useHeld = references.InputHandler.MineHeld;
            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;

            if (!useHeld || equipped == null || equipped.HoldType == HoldType.None)
            {
                // Released: the next press swings/hits immediately.
                nextSwingTime = 0f;
                nextHitTime = 0f;
                return;
            }

            float interval = 1f / Mathf.Max(0.25f,
                equipped.AttacksPerSecond > 0f ? equipped.AttacksPerSecond : FallbackAttacksPerSecond);

            if (Time.time >= nextSwingTime)
            {
                nextSwingTime = Time.time + interval;
                if (holdController != null)
                    holdController.PlayUse();
            }

            if (equipped.IsWeapon && Time.time >= nextHitTime)
            {
                nextHitTime = Time.time + interval;
                TryHit(equipped);
            }
        }

        private readonly RaycastHit[] castBuffer = new RaycastHit[16];

        private void TryHit(ItemDefinition weapon)
        {
            Transform origin = references.CameraPivot;
            float range = Mathf.Max(0.5f, weapon.AttackRange);

            int hitCount = Physics.SphereCastNonAlloc(
                origin.position, hitRadius, origin.forward, castBuffer, range,
                ~0, QueryTriggerInteraction.Ignore);

            // Nearest hit that is NOT the player: the swing sweep clips the
            // rig's own visual colliders when aiming downward and must ignore them.
            int best = -1;
            for (int i = 0; i < hitCount; i++)
            {
                if (castBuffer[i].collider.transform.IsChildOf(transform))
                    continue;
                if (best < 0 || castBuffer[i].distance < castBuffer[best].distance)
                    best = i;
            }

            if (best < 0)
                return;

            RaycastHit hit = castBuffer[best];

            // Voxel terrain in front of us: mining owns that interaction.
            if (hit.collider.GetComponentInParent<ChunkView>() != null)
                return;

            var damageable = hit.collider.GetComponentInParent<IDamageable>();
            if (damageable == null)
                return;

            var info = new DamageInfo(
                weapon.WeaponDamage, weapon.DamageType, hit.point, origin.forward, gameObject);
            damageable.ApplyDamage(in info);
        }
    }
}
