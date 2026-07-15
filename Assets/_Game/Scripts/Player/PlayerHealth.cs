using System;
using IslandGame.Combat;
using IslandGame.Data.Items;
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
    /// DEATH: OnPlayerDeath fires exactly once per death. Since the respawn
    /// phase, PlayerRespawnController subscribes and owns the real flow
    /// (penalty, death screen, bed spawn points) — it sets
    /// ExternalRespawnHandling so the legacy placeholder respawn below stays
    /// dormant, and finishes through ExternalRespawnAt. Scenes without the
    /// respawn system keep the old instant-respawn placeholder behavior.
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

        /// <summary>
        /// Set true by the respawn system when it takes ownership of the death
        /// flow — the placeholder respawn then never runs. The OnPlayerDeath
        /// event itself is unchanged either way.
        /// </summary>
        public bool ExternalRespawnHandling { get; set; }

        /// <summary>Name of whatever last hurt the player through ApplyDamage ("Boar"), for cause-of-death text. Null before any hit.</summary>
        public string LastDamageSourceName { get; private set; }

        /// <summary>Damage type of the last ApplyDamage hit.</summary>
        public DamageType LastDamageType { get; private set; }

        /// <summary>Time.time of the last ApplyDamage hit; -inf before any. Stat-driven deaths (freezing) don't update it — check recency.</summary>
        public float LastDamageTime { get; private set; } = float.NegativeInfinity;

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

            // Recorded before the stat write so a lethal hit's cause is
            // already in place when OnPlayerDeath fires.
            LastDamageSourceName = damage.Source != null ? damage.Source.name : null;
            LastDamageType = damage.Type;
            LastDamageTime = Time.time;

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

            if (!ExternalRespawnHandling)
                Respawn();
        }

        /// <summary>
        /// Respawn-system API: safe teleport (CharacterController disabled
        /// across the write or it overrides the transform with cached state),
        /// clears the dead flag, fires OnPlayerRespawned. Post-respawn STAT
        /// policy is deliberately the caller's job — this only moves and
        /// revives.
        /// </summary>
        public void ExternalRespawnAt(Vector3 position, Quaternion rotation)
        {
            CharacterController controller = references.Controller;
            bool hadController = controller != null && controller.enabled;
            if (hadController)
                controller.enabled = false;

            transform.SetPositionAndRotation(position, rotation);

            if (hadController)
                controller.enabled = true;

            if (references.Locomotion != null)
                references.Locomotion.SetVerticalVelocity(0f);

            isDead = false;
            OnPlayerRespawned?.Invoke();
        }

        /// <summary>Placeholder respawn (scenes without the respawn system): spawn point + every Resource stat refilled.</summary>
        private void Respawn()
        {
            ExternalRespawnAt(spawnPosition, spawnRotation);

            var statInstances = statContainer.Instances;
            for (int i = 0; i < statInstances.Count; i++)
            {
                if (statInstances[i].Definition.Kind == StatKind.Resource)
                    statContainer.RefillToMax(statInstances[i].Definition.Id);
            }

            Debug.Log("[PlayerHealth] Placeholder respawn: spawn point, stats refilled.", this);
        }
    }
}
