using System;
using System.Collections.Generic;
using IslandGame.Data.Items;
using IslandGame.Inventory;
using UnityEngine;

namespace IslandGame.Building
{
    /// <summary>
    /// The first real IFunctionalPlaceable: a fueled fire.
    ///
    ///   FUEL — interacting while the EQUIPPED hotbar item is one of the
    ///   accepted fuel items feeds one unit into the fire (consumed from the
    ///   equipped stack), up to Max Fuel Seconds. Feeding an unlit fire lights
    ///   it — throwing wood on a cold fire pit and it not catching would just
    ///   be busywork.
    ///
    ///   LIGHT/EXTINGUISH — interacting with anything else equipped toggles:
    ///   lights when there is fuel, extinguishes when burning.
    ///
    ///   BURNING — fuel drains in real time; at zero the fire goes out by
    ///   itself. While lit, the point light flickers (Perlin, no allocation)
    ///   and the flame particles play.
    ///
    /// IsLit + LitChanged are the stable query surface for future systems
    /// (cooking, warmth, night visibility) — poll or subscribe, both work.
    /// The Campfire CraftingStationMarker on the same prefab is what makes
    /// Campfire-station recipes work; it is intentionally separate from this
    /// behavior (the marker convention predates buildings and stays as-is).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CampfireBehavior : MonoBehaviour, IFunctionalPlaceable
    {
        [Header("Fuel")]
        [Tooltip("Items accepted as fuel via Interact (wired by the content creator: Log, Wood Plank).")]
        [SerializeField] private List<ItemDefinition> acceptedFuel = new List<ItemDefinition>();

        [Tooltip("Burn seconds added per fuel unit fed.")]
        [Min(1f)]
        [SerializeField] private float secondsPerFuelUnit = 60f;

        [Tooltip("Fuel cap, seconds. Interacting with fuel equipped does nothing once full.")]
        [Min(1f)]
        [SerializeField] private float maxFuelSeconds = 300f;

        [Header("Visuals (wired by the content creator; auto-resolved from children when empty)")]
        [SerializeField] private Light fireLight;
        [SerializeField] private ParticleSystem flames;

        [Header("Flicker")]
        [SerializeField] private float baseLightIntensity = 2.2f;

        [Tooltip("± intensity swing of the Perlin flicker while lit.")]
        [SerializeField] private float flickerAmplitude = 0.5f;

        [SerializeField] private float flickerSpeed = 6f;

        // Live-instance registry for proximity consumers (warmth, future
        // cooking/visibility): a scan over this short list beats physics
        // queries and scene searches, and enables/disables keep it exact.
        private static readonly List<CampfireBehavior> activeCampfires = new List<CampfireBehavior>();

        /// <summary>Every enabled campfire in the world (lit or not — check IsLit). Warmth systems scan this.</summary>
        public static IReadOnlyList<CampfireBehavior> ActiveCampfires => activeCampfires;

        private BuildingPiece piece;
        private float fuelSeconds;
        private bool isLit;

        /// <summary>The owning placed piece (set by Init; null before placement).</summary>
        public BuildingPiece Piece => piece;

        /// <summary>The stable query for cooking/warmth/visibility systems.</summary>
        public bool IsLit => isLit;

        /// <summary>Raised on every lit-state change (true = ignited, false = extinguished/burned out).</summary>
        public event Action<bool> LitChanged;

        /// <summary>Remaining burn time, 0..1 of the cap — for future UI.</summary>
        public float Fuel01 => Mathf.Clamp01(fuelSeconds / maxFuelSeconds);

        public string InteractionPrompt =>
            isLit ? "Add fuel / extinguish" : fuelSeconds > 0f ? "Light campfire" : "Add fuel (hold wood)";

        private void OnEnable()
        {
            activeCampfires.Add(this);
        }

        private void OnDisable()
        {
            activeCampfires.Remove(this);
        }

        public void Init(BuildingPiece owner)
        {
            piece = owner;

            if (fireLight == null)
                fireLight = GetComponentInChildren<Light>(true);
            if (flames == null)
                flames = GetComponentInChildren<ParticleSystem>(true);

            ApplyLitVisuals(); // placed cold and dark until fed
        }

        public void Interact(GameObject interactor)
        {
            var selector = interactor.GetComponent<HotbarSelector>();
            var inventory = interactor.GetComponent<InventorySystem>();

            // Fuel path: equipped item is accepted fuel and there's room in the pit.
            ItemDefinition equipped = selector != null ? selector.EquippedItem : null;
            if (equipped != null && inventory != null && acceptedFuel.Contains(equipped))
            {
                if (fuelSeconds >= maxFuelSeconds)
                    return; // full — don't silently eat wood

                inventory.ConsumeFromSlot(selector.SelectedIndex, 1);
                fuelSeconds = Mathf.Min(maxFuelSeconds, fuelSeconds + secondsPerFuelUnit);

                if (!isLit)
                    SetLit(true);
                return;
            }

            // Toggle path: empty hand (or non-fuel item).
            if (isLit)
                SetLit(false);
            else if (fuelSeconds > 0f)
                SetLit(true);
            // else: cold and empty — the prompt already says to bring wood.
        }

        private void Update()
        {
            if (!isLit)
                return;

            fuelSeconds -= Time.deltaTime;
            if (fuelSeconds <= 0f)
            {
                fuelSeconds = 0f;
                SetLit(false);
                return;
            }

            if (fireLight != null)
            {
                float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, 0.37f) * 2f - 1f;
                fireLight.intensity = baseLightIntensity + noise * flickerAmplitude;
            }
        }

        private void SetLit(bool lit)
        {
            if (isLit == lit)
                return;

            isLit = lit;
            ApplyLitVisuals();
            LitChanged?.Invoke(isLit);
        }

        private void ApplyLitVisuals()
        {
            if (fireLight != null)
            {
                fireLight.enabled = isLit;
                fireLight.intensity = baseLightIntensity;
            }

            if (flames != null)
            {
                if (isLit)
                    flames.Play(true);
                else
                    flames.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
