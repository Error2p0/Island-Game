using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.Building
{
    /// <summary>
    /// The semi-transparent placement preview: a stripped clone of a piece
    /// prefab that only renders. All colliders are disabled and destroyed (it
    /// must never block the aim ray or its own overlap test), all behaviours
    /// (BuildingPiece included) are removed so nothing initializes or
    /// registers, shadows are off, and every renderer swaps to one shared
    /// unlit-ish transparent material tinted green (placeable) or red
    /// (blocked). Owned and driven by BuildingPlacementController; plain class
    /// so no scene component is needed beyond the controller itself.
    ///
    /// Materials use the same runtime URP transparent-surface recipe as the
    /// terrain's water material (see VoxelWorld.CreateMaterials) with the
    /// Standard-pipeline fallback for portability.
    /// </summary>
    public sealed class BuildingGhost
    {
        private readonly Material validMaterial;
        private readonly Material invalidMaterial;

        private readonly List<Renderer> renderers = new List<Renderer>();

        private GameObject root;
        private GameObject sourcePrefab;
        private bool valid = true;
        private bool visible;

        public BuildingGhost(Color validColor, Color invalidColor)
        {
            validMaterial = CreateGhostMaterial(validColor);
            invalidMaterial = CreateGhostMaterial(invalidColor);
        }

        public bool Visible => visible && root != null;

        /// <summary>
        /// Ensures the ghost clone mirrors the given prefab, rebuilding only
        /// when the prefab actually changed (hotbar swap).
        /// </summary>
        public void SetPrefab(GameObject prefab)
        {
            if (prefab == sourcePrefab && root != null)
                return;

            DestroyRoot();
            sourcePrefab = prefab;

            if (prefab == null)
                return;

            root = Object.Instantiate(prefab);
            root.name = $"BuildingGhost ({prefab.name})";

            // Disable immediately (queries this frame must not see them), then
            // destroy — the ghost must never collide, be hit by rays, or shove
            // the CharacterController.
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
                Object.Destroy(collider);
            }

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
                Object.Destroy(body);

            // No behaviour may run on a preview: BuildingPiece would register
            // itself, functional placeables would Init. Destroy is deferred to
            // end of frame, which is fine — Start callbacks can no longer run.
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                Object.Destroy(behaviour);

            renderers.Clear();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderers.Add(renderer);
            }

            ApplyMaterial(valid ? validMaterial : invalidMaterial);
            root.SetActive(visible);
        }

        public void SetPose(Vector3 position, Quaternion rotation)
        {
            if (root == null)
                return;

            root.transform.SetPositionAndRotation(position, rotation);
        }

        public void SetValid(bool isValid)
        {
            if (valid == isValid)
                return;

            valid = isValid;
            ApplyMaterial(valid ? validMaterial : invalidMaterial);
        }

        public void SetVisible(bool isVisible)
        {
            visible = isVisible;
            if (root != null && root.activeSelf != isVisible)
                root.SetActive(isVisible);
        }

        /// <summary>Full teardown — call from the owning component's OnDestroy.</summary>
        public void Destroy()
        {
            DestroyRoot();
            sourcePrefab = null;

            if (validMaterial != null)
                Object.Destroy(validMaterial);
            if (invalidMaterial != null)
                Object.Destroy(invalidMaterial);
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private void DestroyRoot()
        {
            renderers.Clear();
            if (root != null)
            {
                Object.Destroy(root);
                root = null;
            }
        }

        private void ApplyMaterial(Material material)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null)
                    continue;

                // Every submesh slot gets the ghost material — piece prefabs
                // may mix materials, the preview deliberately flattens them.
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int slot = 0; slot < materials.Length; slot++)
                    materials[slot] = material;
                renderer.sharedMaterials = materials;
            }
        }

        private static Material CreateGhostMaterial(Color color)
        {
            Shader lit = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");

            var material = new Material(lit) { name = "BuildingGhost", color = color };

            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0f);
            if (material.HasProperty("_Glossiness"))
                material.SetFloat("_Glossiness", 0f);

            if (material.HasProperty("_Surface"))
            {
                // URP Lit transparent surface — same recipe as the terrain water.
                material.SetFloat("_Surface", 1f); // 1 = Transparent
                material.SetOverrideTag("RenderType", "Transparent");
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (material.HasProperty("_Mode"))
            {
                // Built-in Standard shader transparent mode.
                material.SetFloat("_Mode", 3f);
                material.SetOverrideTag("RenderType", "Transparent");
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_ZWrite", 0);
                material.DisableKeyword("_ALPHATEST_ON");
                material.EnableKeyword("_ALPHABLEND_ON");
                material.renderQueue = (int)RenderQueue.Transparent;
            }

            return material;
        }
    }
}
