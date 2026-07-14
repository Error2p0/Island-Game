using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IslandGame.Terrain
{
    /// <summary>
    /// Minimal on-screen performance readout for terrain streaming (organic-
    /// terrain phase): chunk/view counts, detailed-vs-simplified LOD meshes,
    /// promoted-cell totals with approximate grid memory, and generation/
    /// meshing times — exactly the numbers that say whether Detail Mesh
    /// Radius and the frame budgets on VoxelWorld need tuning. Pure debug UI:
    /// reads VoxelWorld's public stats at 2 Hz, changes nothing, allocates
    /// only when the text refreshes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TerrainPerfOverlay : MonoBehaviour
    {
        // ~150 B per promoted cell: 64 B bitset + object header + dictionary
        // entry (see SubVoxelGrid's storage note). Estimate, for the readout.
        private const float BytesPerPromotedCell = 150f;

        [Tooltip("Keyboard key that shows/hides the overlay.")]
        [SerializeField] private Key toggleKey = Key.F3;

        [Tooltip("Show immediately on Play (toggle off with the key).")]
        [SerializeField] private bool startVisible = true;

        private readonly StringBuilder builder = new StringBuilder(256);
        private VoxelWorld world;
        private bool visible;
        private float nextRefreshTime;
        private string text = string.Empty;
        private GUIStyle style;

        private void Start()
        {
            world = FindFirstObjectByType<VoxelWorld>();
            if (world == null)
            {
                Debug.LogWarning("TerrainPerfOverlay: no VoxelWorld in the scene — overlay disabled.", this);
                enabled = false;
                return;
            }

            visible = startVisible;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
                visible = !visible;

            if (!visible || Time.unscaledTime < nextRefreshTime)
                return;

            nextRefreshTime = Time.unscaledTime + 0.5f;

            int meshed = world.MeshedViewCount;
            int detailed = world.CountDetailedViews();
            long promoted = world.CountPromotedCells();
            float promotedMb = promoted * BytesPerPromotedCell / (1024f * 1024f);

            builder.Length = 0;
            builder.Append("TERRAIN  [").Append(toggleKey).Append("]  detail radius ")
                   .Append(world.DetailMeshRadius).AppendLine(" chunks");
            builder.Append("Chunks: ").Append(world.LoadedChunkCount).Append(" data | ")
                   .Append(meshed).Append(" meshed (").Append(detailed).Append(" detailed / ")
                   .Append(meshed - detailed).AppendLine(" simplified)");
            builder.Append("Promoted cells: ").Append(promoted.ToString("N0"))
                   .Append("  (~").Append(promotedMb.ToString("0.0")).AppendLine(" MB grids)");
            builder.Append("Generate: ").Append(world.LastGenerateMs.ToString("0.00"))
                   .Append(" ms last / ").Append(world.AverageGenerateMs.ToString("0.00")).AppendLine(" ms avg");
            builder.Append("Mesh: ").Append(world.LastMeshMs.ToString("0.00"))
                   .Append(" ms last / ").Append(world.AverageMeshMs.ToString("0.00")).Append(" ms avg");
            text = builder.ToString();
        }

        private void OnGUI()
        {
            if (!visible)
                return;

            style ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 12,
                padding = new RectOffset(8, 8, 6, 6),
            };

            GUI.Box(new Rect(10f, 10f, 360f, 104f), text, style);
        }
    }
}
