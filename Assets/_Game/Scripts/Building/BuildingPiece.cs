using System;
using IslandGame.Combat;
using IslandGame.Data.Building;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// Runtime identity of one placed building piece — the component every
    /// piece prefab carries on its ROOT (weapon hits resolve IDamageable via
    /// GetComponentInParent, so children only need colliders). Owns current
    /// health (initialized from the definition's MaxHealth) and runs the
    /// IFunctionalPlaceable Init pass.
    ///
    /// Two ways into the world, both fully supported:
    ///  • Placed by the Phase 2 placement system, which instantiates the
    ///    definition's prefab and calls Initialize(definition) immediately.
    ///  • Hand-authored into a scene for testing — Start() resolves the
    ///    serialized piece ID against the BuildingPieceDatabase instead.
    /// Save data stores PieceId (the stable string), never a reference.
    ///
    /// Both paths end in PlacedPieceRegistry registration (and OnDestroy
    /// unregisters), so "what's built where" is always answerable from one
    /// place regardless of how the piece entered the world.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BuildingPiece : MonoBehaviour, IDamageable
    {
        [Tooltip("Stable BuildingPieceDefinition ID this prefab belongs to. Stamped by the Building Piece Editor / example creator; used by scene-authored instances to self-resolve.")]
        [SerializeField] private string pieceId;

        private BuildingPieceDefinition definition;
        private float currentHealth;
        private bool initialized;

        /// <summary>Raised right before the piece's GameObject is destroyed by damage. Phase 2's placement bookkeeping subscribes.</summary>
        public event Action<BuildingPiece> Destroyed;

        public string PieceId => pieceId;

        /// <summary>Null only before initialization or when the ID failed to resolve (logged).</summary>
        public BuildingPieceDefinition Definition => definition;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => definition != null ? definition.MaxHealth : 0f;
        public bool IsInitialized => initialized;

        /// <summary>
        /// Entry point for the placement system: binds the definition, fills
        /// health and runs Init on every IFunctionalPlaceable in the prefab.
        /// Call exactly once, immediately after instantiating the prefab.
        /// </summary>
        public void Initialize(BuildingPieceDefinition pieceDefinition)
        {
            if (pieceDefinition == null)
            {
                Debug.LogError($"BuildingPiece on '{name}': Initialize called with a null definition.", this);
                return;
            }

            if (initialized)
            {
                Debug.LogWarning($"BuildingPiece on '{name}': already initialized as '{pieceId}' — ignoring repeat call.", this);
                return;
            }

            definition = pieceDefinition;
            pieceId = pieceDefinition.Id;
            currentHealth = pieceDefinition.MaxHealth;
            initialized = true;

            RegisterInWorld();
            InitFunctionalPlaceables();
        }

        private void Start()
        {
            // Scene-authored instances (manual test setups) were never handed
            // a definition — resolve the stamped ID so they behave identically.
            if (initialized)
                return;

            if (string.IsNullOrEmpty(pieceId))
            {
                Debug.LogError(
                    $"BuildingPiece on '{name}' has no Piece Id and was not initialized by placement. " +
                    "Assign the ID (the Building Piece Editor stamps it onto the prefab) or place it through the build system.", this);
                return;
            }

            BuildingPieceDatabase database = BuildingPieceDatabase.Instance;
            if (database == null)
                return; // Instance already logged the missing-database error.

            BuildingPieceDefinition resolved = database.Get(pieceId);
            if (resolved == null)
                return; // Get already logged the unknown-ID error.

            definition = resolved;
            currentHealth = resolved.MaxHealth;
            initialized = true;

            RegisterInWorld();
            InitFunctionalPlaceables();
        }

        /// <summary>
        /// World pose of one of this piece's snap sockets — what the placement
        /// system mates ghost sockets against. Assumes uniform piece scale
        /// (building prefabs are authored unscaled at the root).
        /// </summary>
        public void GetSocketWorldPose(SnapSocket socket, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = transform.TransformPoint(socket.LocalPosition);
            worldRotation = transform.rotation * socket.LocalRotation;
        }

        public void ApplyDamage(in DamageInfo damage)
        {
            if (!initialized || currentHealth <= 0f)
                return;

            currentHealth -= damage.Amount;
            if (currentHealth > 0f)
                return;

            currentHealth = 0f;
            Destroyed?.Invoke(this);
            Destroy(gameObject);
        }

        private void InitFunctionalPlaceables()
        {
            // Includes inactive children: a functional prefab may keep parts
            // (fire glow, UI anchor) disabled until its behavior enables them.
            foreach (IFunctionalPlaceable placeable in GetComponentsInChildren<IFunctionalPlaceable>(true))
                placeable.Init(this);
        }

        private void RegisterInWorld()
        {
            PlacedPieceRegistry registry = PlacedPieceRegistry.Instance;
            if (registry != null)
                registry.Register(this);
        }

        private void OnDestroy()
        {
            // Teardown-safe: never creates a registry during scene unload/quit.
            PlacedPieceRegistry.UnregisterIfAlive(this);
        }
    }
}
