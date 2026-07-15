using IslandGame.Player;
using IslandGame.Sky;
using IslandGame.Stats;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click weather setup (same builder workflow as every world system):
    /// weather is an extension of the day/night atmosphere, so its components
    /// live ON the existing DayNight object — WeatherController (state
    /// machine), WeatherVisuals (rain volume/lightning/audio, built at
    /// runtime) and StormCampfireHazard — plus WeatherSurvivalEffects on the
    /// player (the warmth rules). Idempotent per component: existing
    /// components and their tuning are left untouched, only missing pieces
    /// are added, so it is safe after a Build Everything (which rebuilds the
    /// player but keeps DayNight).
    /// </summary>
    public static class WeatherBuilder
    {
        [MenuItem("Island Game/World/Add Weather System")]
        public static void Create()
        {
            GameObject dayNight = GameObject.Find("DayNight");
            if (dayNight == null)
            {
                Debug.LogError("Weather System: no 'DayNight' object — run Island Game/World/Create Day Night Cycle first.");
                return;
            }

            bool addedAnything = false;

            var controller = dayNight.GetComponent<WeatherController>();
            if (controller == null)
            {
                controller = Undo.AddComponent<WeatherController>(dayNight);
                addedAnything = true;
            }

            if (dayNight.GetComponent<WeatherVisuals>() == null)
            {
                var visuals = Undo.AddComponent<WeatherVisuals>(dayNight);
                var serialized = new SerializedObject(visuals);
                serialized.FindProperty("weather").objectReferenceValue = controller;
                serialized.FindProperty("dayNightVisuals").objectReferenceValue =
                    dayNight.GetComponent<DayNightVisuals>();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                addedAnything = true;
            }

            if (dayNight.GetComponent<StormCampfireHazard>() == null)
            {
                var hazard = Undo.AddComponent<StormCampfireHazard>(dayNight);
                var serialized = new SerializedObject(hazard);
                serialized.FindProperty("weather").objectReferenceValue = controller;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                addedAnything = true;
            }

            // --- Player half (the warmth rules) ---------------------------
            var player = Object.FindFirstObjectByType<PlayerReferences>();
            if (player == null)
            {
                Debug.LogWarning("Weather System: no player in the scene — run the player builders, then rerun this to add WeatherSurvivalEffects.");
            }
            else if (player.GetComponent<StatContainer>() == null)
            {
                Debug.LogWarning("Weather System: player has no StatContainer — run Island Game/World/Add Stats System To Player, then rerun this.");
            }
            else if (player.GetComponent<WeatherSurvivalEffects>() == null)
            {
                Undo.AddComponent<WeatherSurvivalEffects>(player.gameObject);
                addedAnything = true;
            }

            if (!addedAnything)
            {
                Debug.Log("Weather System: already set up — nothing to do.");
                return;
            }

            EditorSceneManager.MarkSceneDirty(dayNight.scene);
            Selection.activeGameObject = dayNight;
            Debug.Log(
                "Weather System added: WeatherController + WeatherVisuals + StormCampfireHazard on DayNight, " +
                "WeatherSurvivalEffects on the player. Debug: F8 cycles Clear → Rain → Storm. Save the scene.");
        }
    }
}
