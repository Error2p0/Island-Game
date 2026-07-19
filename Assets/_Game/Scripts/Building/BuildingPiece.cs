using System;
using System.Collections;
using IslandGame.Combat;
using IslandGame.Data.Building;
using IslandGame.Terrain;
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

        // Damage feedback tuning. The flash tint REPLACES the material color
        // for its duration (property-block override), so it must read as a
        // hit on any material color the piece uses.
        private static readonly Color FlashColor = new Color(1f, 0.38f, 0.32f);
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private const float FlashSeconds = 0.12f;
        private const int HitDebrisCount = 8;
        private const int DestroyDebrisCount = 24;

        private BuildingPieceDefinition definition;
        private float currentHealth;
        private bool initialized;

        private MeshRenderer[] flashRenderers;
        private MaterialPropertyBlock flashBlock;
        private Coroutine flashRoutine;
        private Color debrisColor;
        private bool debrisColorCached;

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

        /// <summary>Load phase: restores saved health after Initialize filled it to max. Clamped to (0, MaxHealth].</summary>
        public void RestoreHealth(float health)
        {
            if (!initialized)
                return;

            currentHealth = Mathf.Clamp(health, 1f, MaxHealth);
        }

        public void ApplyDamage(in DamageInfo damage)
        {
            if (!initialized || currentHealth <= 0f)
                return;

            currentHealth -= damage.Amount;
            PlayHitFeedback(damage.Point);
            if (currentHealth > 0f)
                return;

            currentHealth = 0f;

            // Smashed pieces drop part of their build cost on the ground —
            // the same cost math deconstruction uses, without an inventory
            // (the attacker may be a creature, not the player).
            BuildingRefund.Refund(this, null, BuildingRefund.DestroyedRatio);
            MiningDebrisEffect.GetOrCreate().EmitBurst(
                transform.position + Vector3.up * 0.5f, DebrisColor(), DestroyDebrisCount);

            Destroyed?.Invoke(this);
            Destroy(gameObject);
        }

        // ------------------------------------------------------------------
        // Damage feedback: a chip burst at the hit point (the mining debris
        // system, tinted with this piece's material color) plus a brief
        // red flash over every renderer.
        // ------------------------------------------------------------------

        private void PlayHitFeedback(Vector3 hitPoint)
        {
            MiningDebrisEffect.GetOrCreate().EmitBurst(hitPoint, DebrisColor(), HitDebrisCount);

            if (flashRenderers == null)
                flashRenderers = GetComponentsInChildren<MeshRenderer>();
            if (flashRenderers.Length == 0)
                return;

            if (flashBlock == null)
            {
                flashBlock = new MaterialPropertyBlock();
                // Built-in Standard reads _Color, URP Lit reads _BaseColor —
                // setting both covers whichever pipeline built the materials.
                flashBlock.SetColor(ColorId, FlashColor);
                flashBlock.SetColor(BaseColorId, FlashColor);
            }

            if (flashRoutine != null)
                StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashRoutine());
        }

        private IEnumerator FlashRoutine()
        {
            for (int i = 0; i < flashRenderers.Length; i++)
            {
                if (flashRenderers[i] != null)
                    flashRenderers[i].SetPropertyBlock(flashBlock);
            }

            yield return new WaitForSeconds(FlashSeconds);

            for (int i = 0; i < flashRenderers.Length; i++)
            {
                if (flashRenderers[i] != null)
                    flashRenderers[i].SetPropertyBlock(null);
            }

            flashRoutine = null;
        }

        private Color DebrisColor()
        {
            if (debrisColorCached)
                return debrisColor;

            debrisColorCached = true;
            debrisColor = new Color(0.55f, 0.45f, 0.35f); // splinter fallback

            if (flashRenderers == null)
                flashRenderers = GetComponentsInChildren<MeshRenderer>();

            for (int i = 0; i < flashRenderers.Length; i++)
            {
                Material material = flashRenderers[i] != null ? flashRenderers[i].sharedMaterial : null;
                if (material == null)
                    continue;

                if (material.HasProperty(ColorId))
                {
                    debrisColor = material.GetColor(ColorId);
                    break;
                }

                if (material.HasProperty(BaseColorId))
                {
                    debrisColor = material.GetColor(BaseColorId);
                    break;
                }
            }

            return debrisColor;
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
