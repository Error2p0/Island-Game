using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Live 3D preview for the Building Piece Editor — same
    /// PreviewRenderUtility approach and drag/auto-rotate feel as the
    /// Block/Item Editor previews. Renders the assigned prefab's meshes with
    /// their own shared materials plus one gizmo per snap socket: a marker
    /// cube at the socket position, a long bar along the socket's +Z
    /// (forward, must point out of the piece) and a short bar along its +Y
    /// (up), all colored by tag via SnapTagColors so gizmos match the
    /// inspector swatches. The selected socket is drawn larger and brightened.
    /// RGB axis bars mark the prefab pivot so socket numbers can be verified
    /// against the piece's local frame. Everything renders from the edit
    /// session's buffer, so unsaved socket edits preview live.
    /// </summary>
    internal sealed class BuildingPiecePreview : System.IDisposable
    {
        private const float DragDegreesPerPixel = 0.7f;
        private const float AutoRotateDegreesPerSecond = 25f;

        private const float PivotAxisLength = 0.4f;
        private const float PivotAxisThickness = 0.01f;

        private const float SocketMarkerSize = 0.08f;
        private const float SocketSelectedMarkerSize = 0.14f;
        private const float SocketForwardLength = 0.35f;
        private const float SocketForwardThickness = 0.02f;
        private const float SocketUpLength = 0.16f;
        private const float SocketUpThickness = 0.012f;

        private struct PreviewMesh
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Matrix4x4 Matrix;
        }

        private PreviewRenderUtility utility;
        private Material fallbackMaterial;
        private Mesh cubeMesh;
        private readonly Dictionary<Color, Material> gizmoMaterials = new Dictionary<Color, Material>();

        private GameObject cachedPrefab;
        private bool cacheBuilt;
        private readonly List<PreviewMesh> meshes = new List<PreviewMesh>();
        private Bounds meshBounds;
        private bool hasMeshBounds;

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
        /// Renders the prefab plus the socket gizmos into rect. selectedSocket
        /// is an index into sockets (-1 = none); showSockets off leaves just
        /// the model and pivot axes.
        /// </summary>
        public void Draw(
            Rect rect, GameObject prefab,
            IReadOnlyList<SocketPreviewData> sockets, int selectedSocket, bool showSockets)
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

            if (meshes.Count == 0 && (sockets == null || sockets.Count == 0))
            {
                EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.13f));
                GUI.Label(rect,
                    prefab == null
                        ? "No prefab — assign one to preview it and its sockets."
                        : "Prefab has no renderable meshes.",
                    EditorStyles.centeredGreyMiniLabel);
                return;
            }

            // Frame the meshes AND the sockets — socket frames often sit on or
            // outside the hull, and clipping them would defeat the preview.
            Bounds frame = hasMeshBounds ? meshBounds : new Bounds(Vector3.zero, Vector3.one * 0.5f);
            if (showSockets && sockets != null)
            {
                for (int i = 0; i < sockets.Count; i++)
                    frame.Encapsulate(sockets[i].LocalPosition);
            }

            frame.Encapsulate(Vector3.zero); // pivot axes stay visible
            float frameRadius = Mathf.Max(frame.extents.magnitude + SocketForwardLength, 0.25f);

            Quaternion viewRotation = Quaternion.Euler(pitch, yaw, 0f);
            Matrix4x4 modelRoot = Matrix4x4.Rotate(viewRotation) * Matrix4x4.Translate(-frame.center);

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

            DrawPivotAxes(modelRoot);

            if (showSockets && sockets != null)
            {
                for (int i = 0; i < sockets.Count; i++)
                    DrawSocketGizmo(modelRoot, sockets[i], i == selectedSocket);
            }

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

            foreach (Material material in gizmoMaterials.Values)
            {
                if (material != null)
                    Object.DestroyImmediate(material);
            }

            gizmoMaterials.Clear();

            // cubeMesh is Unity's built-in cube — never destroy it.
            cubeMesh = null;
        }

        // ------------------------------------------------------------------
        // Gizmos
        // ------------------------------------------------------------------

        private void DrawSocketGizmo(Matrix4x4 modelRoot, SocketPreviewData socket, bool selected)
        {
            Color color = SnapTagColors.Get(socket.Tag);
            if (selected)
                color = Color.Lerp(color, Color.white, 0.45f);

            Matrix4x4 socketFrame = modelRoot * Matrix4x4.TRS(
                socket.LocalPosition, Quaternion.Euler(socket.LocalRotationEuler), Vector3.one);

            float markerSize = selected ? SocketSelectedMarkerSize : SocketMarkerSize;
            DrawBar(socketFrame, Vector3.zero, new Vector3(markerSize, markerSize, markerSize), color);

            // Forward (+Z) — the "a neighbor attaches from here" direction.
            DrawBar(
                socketFrame,
                new Vector3(0f, 0f, SocketForwardLength * 0.5f),
                new Vector3(SocketForwardThickness, SocketForwardThickness, SocketForwardLength),
                color);

            // Up (+Y) — shorter, disambiguates roll.
            DrawBar(
                socketFrame,
                new Vector3(0f, SocketUpLength * 0.5f, 0f),
                new Vector3(SocketUpThickness, SocketUpLength, SocketUpThickness),
                color);
        }

        private void DrawPivotAxes(Matrix4x4 modelRoot)
        {
            DrawBar(modelRoot, new Vector3(PivotAxisLength * 0.5f, 0f, 0f),
                new Vector3(PivotAxisLength, PivotAxisThickness, PivotAxisThickness), new Color(0.9f, 0.2f, 0.2f));
            DrawBar(modelRoot, new Vector3(0f, PivotAxisLength * 0.5f, 0f),
                new Vector3(PivotAxisThickness, PivotAxisLength, PivotAxisThickness), new Color(0.2f, 0.85f, 0.2f));
            DrawBar(modelRoot, new Vector3(0f, 0f, PivotAxisLength * 0.5f),
                new Vector3(PivotAxisThickness, PivotAxisThickness, PivotAxisLength), new Color(0.25f, 0.45f, 0.95f));
        }

        private void DrawBar(Matrix4x4 frame, Vector3 localCenter, Vector3 scale, Color color)
        {
            Matrix4x4 matrix = frame * Matrix4x4.TRS(localCenter, Quaternion.identity, scale);
            utility.DrawMesh(cubeMesh, matrix, GetGizmoMaterial(color), 0);
        }

        private Material GetGizmoMaterial(Color color)
        {
            if (gizmoMaterials.TryGetValue(color, out Material existing) && existing != null)
                return existing;

            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null)
                unlitShader = Shader.Find("Unlit/Color");

            var material = new Material(unlitShader)
            {
                hideFlags = HideFlags.HideAndDontSave,
                color = color,
            };
            gizmoMaterials[color] = material;
            return material;
        }

        // ------------------------------------------------------------------
        // Model cache / input / resources (same scheme as ItemModelPreview)
        // ------------------------------------------------------------------

        private void CacheModel(GameObject prefab)
        {
            if (cacheBuilt && prefab == cachedPrefab)
                return;

            cachedPrefab = prefab;
            cacheBuilt = true;
            meshes.Clear();
            meshBounds = new Bounds();
            hasMeshBounds = false;

            if (prefab == null)
                return;

            Matrix4x4 rootInverse = prefab.transform.worldToLocalMatrix;

            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer == null || filter.sharedMesh == null)
                    continue;

                Matrix4x4 matrix = rootInverse * filter.transform.localToWorldMatrix;
                meshes.Add(new PreviewMesh { Mesh = filter.sharedMesh, Materials = renderer.sharedMaterials, Matrix = matrix });
                Encapsulate(ref meshBounds, ref hasMeshBounds, filter.sharedMesh.bounds, matrix);
            }

            foreach (SkinnedMeshRenderer skinned in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (skinned.sharedMesh == null)
                    continue;

                Matrix4x4 matrix = rootInverse * skinned.transform.localToWorldMatrix;
                meshes.Add(new PreviewMesh { Mesh = skinned.sharedMesh, Materials = skinned.sharedMaterials, Matrix = matrix });
                Encapsulate(ref meshBounds, ref hasMeshBounds, skinned.sharedMesh.bounds, matrix);
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
            int controlId = GUIUtility.GetControlID("IslandGameBuildingPiecePreview".GetHashCode(), FocusType.Passive, rect);
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

            cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            lastFrameTime = EditorApplication.timeSinceStartup;

            cacheBuilt = false; // camera recreated → re-derive framing next Draw
        }
    }
}
