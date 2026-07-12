using System;
using System.Collections.Generic;
using UnityEngine;

namespace IslandGame.Stats.UI
{
    /// <summary>
    /// Binds HUD stat bars to a StatContainer: subscribes to OnStatChanged and
    /// pushes normalized values into the matching StatBarView — the bars are
    /// NEVER polled per frame; a bar redraws only when its stat actually
    /// changed (plus one initial refresh on enable, since values may have
    /// moved while the HUD was inactive). Bindings are authored by the HUD
    /// builder; a binding whose stat the container lacks simply stays put.
    /// </summary>
    public sealed class StatsHudView : MonoBehaviour
    {
        [Serializable]
        private struct Binding
        {
            [Tooltip("Stable stat ID this bar displays.")]
            public string statId;

            public StatBarView bar;
        }

        [Tooltip("Wired by the HUD builder: the player's StatContainer.")]
        [SerializeField] private StatContainer statContainer;

        [Tooltip("Wired by the HUD builder: one entry per HUD bar.")]
        [SerializeField] private List<Binding> bindings = new List<Binding>();

        private void OnEnable()
        {
            if (statContainer == null)
            {
                Debug.LogError("[StatsHudView] No StatContainer wired — rebuild the stats HUD.", this);
                enabled = false;
                return;
            }

            statContainer.OnStatChanged += OnStatChanged;
            RefreshAll();
        }

        private void OnDisable()
        {
            if (statContainer != null)
                statContainer.OnStatChanged -= OnStatChanged;
        }

        private void OnStatChanged(string statId, float oldValue, float newValue)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].bar != null && bindings[i].statId == statId)
                {
                    bindings[i].bar.SetNormalized(statContainer.GetNormalized(statId));
                    return;
                }
            }
        }

        private void RefreshAll()
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].bar != null && statContainer.Has(bindings[i].statId))
                    bindings[i].bar.SetNormalized(statContainer.GetNormalized(bindings[i].statId));
            }
        }
    }
}
