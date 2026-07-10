using UnityEngine;
using UnityEngine.InputSystem;

namespace IslandGame.Terrain
{
    /// <summary>
    /// TEMPORARY diagnostic for the small-voxel pipeline (this phase's proof,
    /// removed/disabled once real mining damage lands in the next phase):
    /// aims at a block via PlayerBlockInteraction and applies sub-voxel test
    /// patterns —
    ///
    ///   F6  corner bite   (sphere carved from the top corner — the "damaged
    ///                      block" look; validates UV continuity + collision)
    ///   F7  checkerboard  (worst-case culling/UV stress: every second cell)
    ///   F8  tunnel        (horizontal bore through the block along Z —
    ///                      validates walking/aiming into carved space)
    ///   F9  clear all     (every sub-cell empty → the cell must demote to
    ///                      air and the sparse entry must disappear)
    ///
    /// Reads the keyboard directly on purpose: debug-only keys don't belong
    /// in PlayerInputHandler's gameplay surface. Add this component to the
    /// Player object; it does nothing in builds you ship without it.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerBlockInteraction))]
    public sealed class SubVoxelDebugTester : MonoBehaviour
    {
        [Tooltip("Auto-resolved when empty.")]
        [SerializeField] private VoxelWorld world;

        private PlayerBlockInteraction interaction;

        private void Awake()
        {
            interaction = GetComponent<PlayerBlockInteraction>();
        }

        private void Start()
        {
            if (world == null)
                world = FindFirstObjectByType<VoxelWorld>();

            if (world == null)
            {
                Debug.LogError("SubVoxelDebugTester: no VoxelWorld in the scene.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f6Key.wasPressedThisFrame)
                ApplyPattern("corner bite", CornerBite);
            else if (keyboard.f7Key.wasPressedThisFrame)
                ApplyPattern("checkerboard", Checkerboard);
            else if (keyboard.f8Key.wasPressedThisFrame)
                ApplyPattern("tunnel", Tunnel);
            else if (keyboard.f9Key.wasPressedThisFrame)
                ApplyPattern("clear all (demotion)", ClearAll);
        }

        private delegate bool CellCleared(int sx, int sy, int sz, int res);

        private void ApplyPattern(string patternName, CellCleared shouldClear)
        {
            if (!interaction.HasAimedBlock)
            {
                Debug.Log("SubVoxel debug: aim at a terrain block first.");
                return;
            }

            Vector3Int cell = interaction.AimedCell;

            SubVoxelGrid grid = world.PromoteBlockToSubVoxels(cell);
            if (grid == null)
            {
                Debug.LogWarning($"SubVoxel debug: could not promote {cell} (air/unloaded?).");
                return;
            }

            int res = grid.Resolution;
            int cleared = 0;
            for (int sy = 0; sy < res; sy++)
            {
                for (int sz = 0; sz < res; sz++)
                {
                    for (int sx = 0; sx < res; sx++)
                    {
                        if (shouldClear(sx, sy, sz, res) && grid.Set(sx, sy, sz, false))
                            cleared++;
                    }
                }
            }

            // One notification for the whole batch: demotes if empty,
            // otherwise remeshes the chunk (and border neighbors) once.
            world.NotifySubVoxelsChanged(cell);

            bool demoted = world.GetSubVoxelGrid(cell) == null;
            Debug.Log(
                $"SubVoxel debug: '{patternName}' on {cell} (res {res}) — cleared {cleared} sub-cells, " +
                $"{grid.FilledCount} remain. " +
                (demoted
                    ? "Cell demoted — sparse entry removed."
                    : "Cell stays promoted."));
        }

        // ------------------------------------------------------------------
        // Patterns (return true = clear that sub-cell)
        // ------------------------------------------------------------------

        /// <summary>Sphere bite centered on the top +X+Z corner, radius 0.75 blocks.</summary>
        private static bool CornerBite(int sx, int sy, int sz, int res)
        {
            float radius = res * 0.75f;
            float dx = res - (sx + 0.5f);
            float dy = res - (sy + 0.5f);
            float dz = res - (sz + 0.5f);
            return dx * dx + dy * dy + dz * dz < radius * radius;
        }

        private static bool Checkerboard(int sx, int sy, int sz, int res)
        {
            return ((sx + sy + sz) & 1) == 1;
        }

        /// <summary>Horizontal bore along Z through the block's middle, radius ~0.3 blocks.</summary>
        private static bool Tunnel(int sx, int sy, int sz, int res)
        {
            float center = res * 0.5f;
            float radius = res * 0.3f;
            float dx = sx + 0.5f - center;
            float dy = sy + 0.5f - center;
            return dx * dx + dy * dy < radius * radius;
        }

        private static bool ClearAll(int sx, int sy, int sz, int res)
        {
            return true;
        }
    }
}
