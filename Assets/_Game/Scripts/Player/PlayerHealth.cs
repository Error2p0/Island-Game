using System;
using IslandGame.Combat;
using IslandGame.Data.Stats;
using IslandGame.Stats;
using UnityEngine;

namespace IslandGame.Player
{
    /// <summary>
    /// The player's implementation of the IDamageable seam from the combat
    /// phase: incoming DamageInfo reduces the "health" stat on the
    /// StatContainer, and death is driven by the container's generic
    /// OnStatDepleted event — this class never tracks a health number itself.
    ///
    /// DEATH: OnPlayerDeath fires exactly once per death (future death-penalty
    /// and enemy-AI systems subscribe here), then the placeholder respawn
    /// runs: teleport back to the recorded spawn point and refill every
    /// Resource stat. A real respawn flow (death screen, penalties, bed
    /// spawn points) is a future phase and replaces only Respawn().
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerReferences))]
    [RequireComponent(typeof(StatContainer))]
    public sealed class PlayerHealth : MonoBehaviour, IDamageable
    {
        [Tooltip("Extra log line per hit taken — handy while there is no damage UI/vignette yet.")]
        [SerializeField] private bool logDamage = true;

        private PlayerReferences references;
        private StatContainer statContainer;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private bool isDead;

        /// <summary>True from health depletion until the respawn completes.</summary>
        public bool IsDead => isDead;

        /// <summary>Fires once per death, before the placeholder respawn runs.</summary>
        public event Action OnPlayerDeath;

        /// <summary>Fires after the placeholder respawn placed and refilled the player.</summary>
        public event Action OnPlayerRespawned;

        private void Awake()
        {
            references = GetComponent<PlayerReferences>();
            statContainer = GetComponent<StatContainer>();
        }

        private void Start()
        {
            // Recorded in Start, after Build-Everything/scene-load placement settled.
            spawnPosition = transform.position;
            spawnRotation = transform.rotation;
        }

        private void OnEnable()
        {
            statContainer.OnStatDepleted += OnStatDepleted;
        }

        private void OnDisable()
        {
            statContainer.OnStatDepleted -= OnStatDepleted;
        }

        /// <summary>Weapon hits, enemy attacks, freezing, future fall damage — every hurt path lands here or on the stat directly.</summary>
        public void ApplyDamage(in DamageInfo damage)
        {
            if (isDead || damage.Amount <= 0f)
                return;

            // Depletion (death) is handled by the OnStatDepleted subscription,
            // so damage application stays one line and one code path.
            statContainer.Modify(StatIds.Health, -damage.Amount);

            if (logDamage)
            {
                Debug.Log(
                    $"[PlayerHealth] Took {damage.Amount:0.#} {damage.Type} damage from " +
                    $"'{(damage.Source != null ? damage.Source.name : "unknown")}' — " +
                    $"{statContainer.GetValue(StatIds.Health):0.#}/{statContainer.GetModifiedValue(StatIds.Health):0.#} HP.",
                    this);
            }
        }

        private void OnStatDepleted(string statId)
        {
            if (statId != StatIds.Health || isDead)
                return;

            isDead = true;
            Debug.Log("[PlayerHealth] Player died.", this);
            OnPlayerDeath?.Invoke();
            Respawn();
        }

        /// <summary>
        /// Placeholder respawn: back to the spawn point with every Resource
        /// stat refilled. CharacterController must be disabled across the
        /// teleport or it overrides the transform write with its cached state.
        /// </summary>
        private void Respawn()
        {
            CharacterController controller = references.Controller;
            bool hadController = controller != null && controller.enabled;
            if (hadController)
                controller.enabled = false;

            transform.SetPositionAndRotation(spawnPosition, spawnRotation);

            if (hadController)
                controller.enabled = true;

            if (references.Locomotion != null)
                references.Locomotion.SetVerticalVelocity(0f);

            var statInstances = statContainer.Instances;
            for (int i = 0; i < statInstances.Count; i++)
            {
                if (statInstances[i].Definition.Kind == StatKind.Resource)
                    statContainer.RefillToMax(statInstances[i].Definition.Id);
            }

            isDead = false;
            OnPlayerRespawned?.Invoke();
            Debug.Log("[PlayerHealth] Respawned at spawn point with stats refilled.", this);
        }
    }
}
