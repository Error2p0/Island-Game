using IslandGame.EditorTools.Data;
using IslandGame.Sky;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.EditorTools
{
    /// <summary>
    /// One-click scene setup for the day/night cycle: creates the SkyGradient
    /// and Stars material assets (from the shaders in Assets/_Game/Shaders),
    /// builds the DayNight object — TimeOfDayController + DayNightVisuals on
    /// the root, Sun (soft-shadow directional), Moon (shadowless directional)
    /// and Stars (StarField) as children — wires every reference, assigns the
    /// skybox to the scene's lighting settings so edit mode looks right too,
    /// and DISABLES any other directional lights in the scene (they would
    /// double-light the world and fight the cycle; logged, not deleted).
    ///
    /// Idempotent: an existing DayNight object is left untouched. Runs
    /// standalone from the menu and as a Build Everything step.
    /// </summary>
    public static class DayNightBuilder
    {
        private const string RootName = "DayNight";
        private const string SkyContentFolder = "Assets/_Game/Content/Sky";

        [MenuItem("Island Game/World/Create Day Night Cycle")]
        public static void Create()
        {
            if (GameObject.Find(RootName) != null)
            {
                Debug.LogWarning($"'{RootName}' already exists in the scene — delete it first to rebuild.");
                Selection.activeGameObject = GameObject.Find(RootName);
                return;
            }

            // --- Material assets --------------------------------------------
            Material skyMaterial = CreateMaterialIfMissing(
                $"{SkyContentFolder}/SkyGradient.mat", "IslandGame/SkyGradient");
            Material starMaterial = CreateMaterialIfMissing(
                $"{SkyContentFolder}/Stars.mat", "IslandGame/Stars");
            if (skyMaterial == null || starMaterial == null)
                return; // CreateMaterialIfMissing logged the missing shader

            // --- Scene objects ----------------------------------------------
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Day Night Cycle");

            var controller = root.AddComponent<TimeOfDayController>();
            var visuals = root.AddComponent<DayNightVisuals>();

            Light sun = CreateDirectionalLight(root.transform, "Sun", LightShadows.Soft, 1.1f,
                new Color(1f, 0.96f, 0.88f));
            sun.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            // Moon shadows deliberately off: a second shadow map for barely
            // visible moonlight is pure cost (see DayNightVisuals summary).
            Light moon = CreateDirectionalLight(root.transform, "Moon", LightShadows.None, 0f,
                new Color(0.58f, 0.66f, 0.95f));
            moon.enabled = false;

            var starsObject = new GameObject("Stars");
            starsObject.transform.SetParent(root.transform, false);
            starsObject.AddComponent<MeshFilter>();
            starsObject.AddComponent<MeshRenderer>();
            var starField = starsObject.AddComponent<StarField>();

            // --- Wiring -------------------------------------------------------
            var serializedStars = new SerializedObject(starField);
            serializedStars.FindProperty("starMaterial").objectReferenceValue = starMaterial;
            serializedStars.ApplyModifiedPropertiesWithoutUndo();

            var serializedVisuals = new SerializedObject(visuals);
            serializedVisuals.FindProperty("timeOfDay").objectReferenceValue = controller;
            serializedVisuals.FindProperty("sunLight").objectReferenceValue = sun;
            serializedVisuals.FindProperty("moonLight").objectReferenceValue = moon;
            serializedVisuals.FindProperty("starField").objectReferenceValue = starField;
            serializedVisuals.FindProperty("skyboxMaterial").objectReferenceValue = skyMaterial;
            serializedVisuals.ApplyModifiedPropertiesWithoutUndo();

            // --- Scene lighting settings (edit-mode look + runtime defaults) ---
            RenderSettings.skybox = skyMaterial;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.56f, 0.63f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.45f, 0.50f);
            RenderSettings.ambientGroundColor = new Color(0.26f, 0.25f, 0.24f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 130f;

            DisableForeignDirectionalLights(root);

            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log(
                "Day/Night cycle built: DayNight (TimeOfDayController + DayNightVisuals), Sun, Moon, Stars; " +
                "skybox + ambient + fog assigned. Debug: F10 pause, hold F11 fast-forward. Save the scene.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Material CreateMaterialIfMissing(string assetPath, string shaderName)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null)
                return existing;

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogError($"DayNightBuilder: shader '{shaderName}' not found — is Assets/_Game/Shaders imported?");
                return null;
            }

            DefinitionDatabaseSync.EnsureFolderExists(SkyContentFolder);
            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, assetPath);
            return material;
        }

        private static Light CreateDirectionalLight(
            Transform parent, string name, LightShadows shadows, float intensity, Color color)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);

            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = shadows;
            light.intensity = intensity;
            light.color = color;
            return light;
        }

        private static void DisableForeignDirectionalLights(GameObject root)
        {
            foreach (Light light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional || light.transform.IsChildOf(root.transform))
                    continue;

                if (light.gameObject.activeSelf)
                {
                    Undo.RegisterCompleteObjectUndo(light.gameObject, "Disable foreign directional light");
                    light.gameObject.SetActive(false);
                    Debug.Log($"DayNightBuilder: disabled existing directional light '{light.name}' — the cycle owns the sun now.", light);
                }
            }
        }
    }
}
