using IslandGame.Building;
using IslandGame.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// Wires the building runtime into the current scene: adds
    /// BuildingPlacementController + BuildingDeconstructor to the player (found
    /// via PlayerReferences, same discovery every builder uses) and ensures a
    /// PlacedPieceRegistry object exists so placed pieces have their parent/
    /// bookkeeping home from the first frame. Idempotent — existing components
    /// and their inspector tweaks are left untouched. Runs standalone from the
    /// menu and as a Build Everything step (after the player rig exists).
    /// </summary>
    public static class BuildingSystemBuilder
    {
        [MenuItem("Island Game/World/Add Building System To Player")]
        public static void Create()
        {
            var player = Object.FindFirstObjectByType<PlayerReferences>();
            if (player == null)
            {
                Debug.LogError(
                    "Building System: no player in the scene (PlayerReferences not found) — " +
                    "run Tools/Island Game/Build Player Rig first.");
                return;
            }

            bool changed = false;

            if (player.GetComponent<BuildingPlacementController>() == null)
            {
                Undo.AddComponent<BuildingPlacementController>(player.gameObject);
                changed = true;
                Debug.Log("Building System: added BuildingPlacementController to the player.");
            }

            if (player.GetComponent<BuildingDeconstructor>() == null)
            {
                Undo.AddComponent<BuildingDeconstructor>(player.gameObject);
                changed = true;
                Debug.Log("Building System: added BuildingDeconstructor to the player.");
            }

            if (player.GetComponent<PlayerInteraction>() == null)
            {
                Undo.AddComponent<PlayerInteraction>(player.gameObject);
                changed = true;
                Debug.Log("Building System: added PlayerInteraction (E — functional placeables) to the player.");
            }

            if (player.GetComponent<PlayerStats>() == null)
            {
                Undo.AddComponent<PlayerStats>(player.gameObject);
                changed = true;
                Debug.Log("Building System: added PlayerStats (hunger + eat-on-use) to the player.");
            }

            if (Object.FindFirstObjectByType<PlacedPieceRegistry>() == null)
            {
                var registryObject = new GameObject("PlacedPieces");
                registryObject.AddComponent<PlacedPieceRegistry>();
                Undo.RegisterCreatedObjectUndo(registryObject, "Create PlacedPieces registry");
                changed = true;
                Debug.Log("Building System: created the PlacedPieces registry object.");
            }

            if (changed)
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            else
                Debug.Log("Building System: already set up — nothing to do.");
        }
    }
}
