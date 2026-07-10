using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// The single authority on "what's built where": every BuildingPiece in
    /// the world registers here when it initializes (placed by the player OR
    /// hand-authored in a scene) and unregisters when destroyed, so placement
    /// snapping, and later durability/decay, saving and deconstruction UI, all
    /// query one list. Placed pieces are parented under this object to keep
    /// the hierarchy tidy.
    ///
    /// Lives as a scene object (created by the building-system builder, or
    /// auto-created on first access). PERFORMANCE: queries are a linear scan
    /// with squared-distance early-out — O(n) over placed pieces, no
    /// per-frame allocation. That is comfortably fine into the thousands of
    /// pieces (one float4 compare each); if profiling ever disagrees, this
    /// class is the one place to add a spatial hash, and no caller changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlacedPieceRegistry : MonoBehaviour
    {
        private static PlacedPieceRegistry instance;
        private static bool applicationQuitting;

        private readonly List<BuildingPiece> pieces = new List<BuildingPiece>();

        /// <summary>
        /// Scene instance, auto-created on demand. Null only during shutdown
        /// (so OnDestroy-time unregistration can never resurrect the object).
        /// </summary>
        public static PlacedPieceRegistry Instance
        {
            get
            {
                if (applicationQuitting)
                    return null;

                if (instance == null)
                {
                    instance = FindFirstObjectByType<PlacedPieceRegistry>();
                    if (instance == null)
                        instance = new GameObject("PlacedPieces").AddComponent<PlacedPieceRegistry>();
                }

                return instance;
            }
        }

        /// <summary>Every live piece in the world (registration order).</summary>
        public IReadOnlyList<BuildingPiece> All => pieces;

        public int Count => pieces.Count;

        public void Register(BuildingPiece piece)
        {
            if (piece == null || pieces.Contains(piece))
                return;

            pieces.Add(piece);
        }

        public void Unregister(BuildingPiece piece)
        {
            pieces.Remove(piece);
        }

        /// <summary>
        /// Teardown-safe unregister for BuildingPiece.OnDestroy: touches only
        /// an already-existing instance, never creates one mid-shutdown.
        /// </summary>
        public static void UnregisterIfAlive(BuildingPiece piece)
        {
            if (instance != null)
                instance.Unregister(piece);
        }

        /// <summary>
        /// Fills results with every registered piece whose ROOT position lies
        /// within radius of point (callers add their own margin for piece
        /// extents — see BuildingPlacementController). Returns the count.
        /// </summary>
        public int CollectNear(Vector3 point, float radius, List<BuildingPiece> results)
        {
            results.Clear();
            float radiusSqr = radius * radius;

            for (int i = 0; i < pieces.Count; i++)
            {
                BuildingPiece piece = pieces[i];
                if (piece == null)
                    continue;

                if ((piece.transform.position - point).sqrMagnitude <= radiusSqr)
                    results.Add(piece);
            }

            return results.Count;
        }

        private void OnApplicationQuit()
        {
            applicationQuitting = true;
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
