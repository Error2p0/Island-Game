using IslandGame.Data.Blocks;
using IslandGame.Player;
using UnityEngine;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Connects the voxel ocean to the movement system's water model — the
    /// "check voxel data directly" option from Phase 7 (chosen over generating
    /// trigger volumes: water bodies span hundreds of chunks and change with
    /// every mined/placed block, so keeping colliders in sync would be a
    /// permanent source of bugs; a per-frame column probe is trivially cheap
    /// and always current).
    ///
    /// Every frame: if the block at the player's feet carries the Liquid
    /// behavior flag, scan up that column to the first non-liquid block — its
    /// base Y is the water surface — and report both through
    /// PlayerLocomotion.SetExternalWater. From there the UNCHANGED Phase 5
    /// movement logic takes over: depth-based wade slowdown, swim enter/exit
    /// thresholds, buoyancy, eye-underwater — trigger-volume pools keep
    /// working in parallel (highest surface wins).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    public sealed class PlayerVoxelWaterSensor : MonoBehaviour
    {
        [Tooltip("Wired by the Voxel World builder; auto-resolved when empty.")]
        [SerializeField] private VoxelWorld world;

        private PlayerReferences references;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
        }

        private void Start()
        {
            if (world == null)
                world = FindFirstObjectByType<VoxelWorld>();
        }

        private void Update()
        {
            PlayerLocomotion locomotion = references.Locomotion;
            if (locomotion == null)
                return;

            if (world == null)
            {
                locomotion.SetExternalWater(false, 0f);
                return;
            }

            // The root sits at the feet (the movement system's convention);
            // sample a hair above so standing exactly on a block top doesn't
            // read the block below.
            Vector3Int cell = Vector3Int.FloorToInt(transform.position + Vector3.up * 0.05f);

            if (!IsLiquid(cell))
            {
                locomotion.SetExternalWater(false, 0f);
                return;
            }

            int surfaceY = cell.y + 1;
            while (surfaceY < Chunk.SizeY && IsLiquid(new Vector3Int(cell.x, surfaceY, cell.z)))
                surfaceY++;

            locomotion.SetExternalWater(true, surfaceY);
        }

        private bool IsLiquid(Vector3Int cell)
        {
            BlockDefinition block = world.GetBlock(cell);
            return block != null && block.HasBehavior(BlockBehaviorFlags.Liquid);
        }
    }
}
