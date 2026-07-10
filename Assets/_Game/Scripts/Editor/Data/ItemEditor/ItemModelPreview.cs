using System.Collections.Generic;
using IslandGame.Data.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Live 3D preview of an item's world-model prefab for the Item Editor —
    /// same PreviewRenderUtility approach and drag/auto-rotate feel as the
    /// Block Editor's cube preview. Renders every MeshFilter/MeshRenderer and
    /// SkinnedMeshRenderer in the prefab with its own shared materials (no
    /// instantiation, no scene or play-mode state).
    ///
    /// Hold-offset mode is the numeric hold preview made visual: RGB axis bars
    /// mark the hold socket frame at the origin and the model is drawn at
    /// HoldLocalPosition/HoldLocalRotation under it — exactly the transform the
    /// held-item phase will apply (see HoldSocketConvention).
    /// </summary>
    internal sealed class ItemModelPreview : System.IDisposable
    {
        private const float DragDegreesPerPixel = 0.7f;
        private const float AutoRotateDegreesPerSecond = 25f;
        private const float AxisLength = 0.3f;
        private const float AxisThickness = 0.008f;

        private struct PreviewMesh
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Matrix4x4 Matrix;
        }

        private PreviewRenderUtility utility;
        private Material fallbackMaterial;
        private Material[] axisMaterials;
        private Mesh axisMesh;

        private GameObject cachedPrefab;
        private bool cacheBuilt;
        private readonly List<PreviewMesh> meshes = new List<PreviewMesh>();
        private Vector3 boundsCenter;
        private float boundsRadius = 0.5f;

        private float yaw = 32f;
        private float pitch = 18f;
        private double lastFrameTime;

        public bool AutoRotate { get; set; } = true;

        public void ResetView()
        {
            yaw = 32f;
            pitch = 18f;
        }

        /// <summary>
        /// Renders the prefab into rect. With showHoldOffset the model is drawn
        /// under the socket frame using the given local offset (both from the
        /// edit session, so unsaved values preview live).
        /// </summary>
        public void Draw(Rect rect, GameObject prefab, bool showHoldOffset, Vector3 holdLocalPosition, Vector3 holdLocalRotationEuler)
        {
            EnsureResources();
            CacheModel(prefab);
            HandleInput(rect);

            double now = EditorApplication.timeSinceStartup;
            if (AutoRotate)
                yaw = Mathf.Repeat(yaw + (float)(now - lastFrameTime) * AutoRotateDegreesPerSecond, 360f);
            lastFrameTime = now;

            if (Event.current.type != EventType.Repaint)
                return;

            if (meshes.Count == 0)
            {
                EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
                GUI.Label(rect,
                    prefab == null
                        ? "No world model — assign a prefab to preview it."
                        : "Prefab has no renderable meshes.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            Quaternion viewRotation = Quaternion.Euler(pitch, yaw, 0f);
            Matrix4x4 view = Matrix4x4.Rotate(viewRotation);

            Matrix4x4 modelRoot;
            float frameRadius;
            if (showHoldOffset)
            {
                // Socket frame at the origin, model offset under it — the exact
                // parent/child transform the held-item phase applies.
                modelRoot = view * Matrix4x4.TRS(holdLocalPosition, Quaternion.Euler(holdLocalRotationEuler), Vector3.one);
                frameRadius = boundsRadius + holdLocalPosition.magnitude + AxisLength;
            }
            else
            {
                modelRoot = view * Matrix4x4.Translate(-boundsCenter);
                frameRadius = boundsRadius;
            }

            float distance = frameRadius / Mathf.Sin(utility.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
            utility.camera.transform.position = new Vector3(0f, 0f, -distance);
            utility.camera.farClipPlane = distance + frameRadius * 4f;

            utility.BeginPreview(rect, "PreBackground");

            foreach (PreviewMesh entry in meshes)
            {
                Matrix4x4 matrix = modelRoot * entry.Matrix;
                for (int submesh = 0; submesh < entry.Mesh.subMeshCount; submesh++)
                {
                    Material material = submesh < entry.Materials.Length ? entry.Materials[submesh] : null;
                    if (material == null)
                        material = fallbackMaterial;
                    utility.DrawMesh(entry.Mesh, matrix, material, submesh);
                }
            }

            if (showHoldOffset)
                DrawSocketAxes(view);

            utility.camera.Render();
            utility.EndAndDrawPreview(rect);
        }

        public void Dispose()
        {
            if (utility != null)
            {
                utility.Cleanup();
                utility = null;
            }

            if (fallbackMaterial != null)
            {
                Object.DestroyImmediate(fallbackMaterial);
                fallbackMaterial = null;
            }

            if (axisMaterials != null)
            {
                foreach (Material material in axisMaterials)
                {
                    if (material != null)
                        Object.DestroyImmediate(material);
                }

                axisMaterials = null;
            }

            // axisMesh is Unity's built-in cube — never destroy it.
            axisMesh = null;
        }

        // ------------------------------------------------------------------
        // Internals
        // ------------------------------------------------------------------

        private void DrawSocketAxes(Matrix4x4 socketFrame)
        {
            // One thin bar per axis, extending along +X (red), +Y (green), +Z (blue).
            Vector3[] directions = { Vector3.right, Vector3.up, Vector3.forward };
            for (int i = 0; i < 3; i++)
            {
                Vector3 scale = new Vector3(AxisThickness, AxisThickness, AxisThickness);
                scale[i] = AxisLength;
                Matrix4x4 bar = socketFrame * Matrix4x4.TRS(directions[i] * (AxisLength * 0.5f), Quaternion.identity, scale);
                utility.DrawMesh(axisMesh, bar, axisMaterials[i], 0);
            }
        }

        private void CacheModel(GameObject prefab)
        {
            if (cacheBuilt && prefab == cachedPrefab)
                return;

            cachedPrefab = prefab;
            cacheBuilt = true;
            meshes.Clear();
            boundsCenter = Vector3.zero;
            boundsRadius = 0.5f;

            if (prefab == null)
                return;

            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;
            bool hasBounds = false;
            var combined = new Bounds();

            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null)
                    continue;

                Matrix4x4 matrix = rootInverse * filter.transform.localToWorldMatrix;
                meshes.Add(new PreviewMesh { Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials, Matrix = matrix });
                Encapsulate(ref combined, ref hasBounds, filter.sharedMesh.bounds, matrix);
            }

            foreach (SkinnedMeshRenderer skinned in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null)
                    continue;

                Matrix4x4 matrix = rootInverse * skinned.transform.localToWorldMatrix;
                meshes.Add(new PreviewMesh { Mesh = skinned.sharedMesh, Materials = skinned.sharedMaterials, Matrix = matrix });
                Encapsulate(ref combined, ref hasBounds, skinned.sharedMesh.bounds, matrix);
            }

            if (hasBounds)
            {
                boundsCenter = combined.center;
                boundsRadius = Mathf.Max(combined.extents.magnitude, 0.05f);
            }
        }

        private static void Encapsulate(ref Bounds combined, ref bool hasBounds, Bounds meshBounds, Matrix4x4 matrix)
        {
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;

            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? min.x : max.x,
                    (corner & 2) == 0 ? min.y : max.y,
                    (corner & 4) == 0 ? min.z : max.z);
                Vector3 transformed = matrix.MultiplyPoint3x4(point);

                if (!hasBounds)
                {
                    combined = new Bounds(transformed, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    combined.Encapsulate(transformed);
                }
            }
        }

        private void HandleInput(Rect rect)
        {
            int controlId = GUIUtility.GetControlID("IslandGameItemModelPreview".GetHashCode(), FocusType.Passive, rect);
            Event current = Event.current;

            switch (current.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (current.button == 0 && rect.Contains(current.mousePosition))
                    {
                        GUIUtility.hotControl = controlId;
                        current.Use();
                    }

                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId)
                    {
                        AutoRotate = false;
                        yaw = Mathf.Repeat(yaw + current.delta.x * DragDegreesPerPixel, 360f);
                        pitch = Mathf.Clamp(pitch + current.delta.y * DragDegreesPerPixel, -89f, 89f);
                        current.Use();
                    }

                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        current.Use();
                    }

                    break;
            }
        }

        private void EnsureResources()
        {
            if (utility != null)
                return;

            utility = new PreviewRenderUtility();
            utility.camera.transform.rotation = Quaternion.identity;
            utility.camera.fieldOfView = 30f;
            utility.camera.nearClipPlane = 0.01f;

            utility.lights[0].intensity = 1.1f;
            utility.lights[0].transform.rotation = Quaternion.Euler(45f, 35f, 0f);
            utility.lights[1].intensity = 0.45f;
            utility.lights[1].transform.rotation = Quaternion.Euler(340f, 218f, 177f);
            utility.ambientColor = new Color(0.32f, 0.32f, 0.34f, 1f);

            Shader litShader = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultShader
                : Shader.Find("Standard");
            fallbackMaterial = new Material(litShader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                color = new Color(0.7f, 0.7f, 0.7f),
            };

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
                unlitShader = Shader.Find("Unlit/Color");

            var axisColors = new[] { new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.85f, 0.2f), new Color(0.25f, 0.45f, 0.95f) };
            axisMaterials = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                axisMaterials[i] = new Material(unlitShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    color = axisColors[i],
                };
            }

            axisMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            lastFrameTime = EditorApplication.timeSinceStartup;

            cacheBuilt = false; // camera recreated → re-derive framing next Draw
        }
    }
}
