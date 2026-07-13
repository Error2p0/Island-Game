using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace IslandGame.Saving
{
    /// <summary>
    /// The simple save/load menu: F9 toggles an IMGUI panel listing every
    /// slot with its timestamp and Save / Load / Delete actions, plus a
    /// new-slot field. IMGUI on purpose — this is a system menu in the same
    /// pragmatic tier as the F10/F11 time-debug keys, needs zero scene/canvas
    /// wiring, and can be restyled onto the uGUI builders later without
    /// touching SaveManager. Cursor lock is released while open and restored
    /// on close.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SaveManager))]
    public sealed class SaveLoadMenu : MonoBehaviour
    {
        [Tooltip("Key that toggles the save/load panel.")]
        [SerializeField] private Key toggleKey = Key.F9;

        private SaveManager saveManager;
        private bool open;
        private string newSlotName = "slot1";
        private string statusLine = string.Empty;
        private Vector2 scroll;
        private List<(string slotName, DateTime writtenAt)> slots = new List<(string, DateTime)>();
        private CursorLockMode previousLockMode;
        private bool previousCursorVisible;

        private void Awake()
        {
            saveManager = GetComponent<SaveManager>();
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
                SetOpen(!open);
        }

        private void SetOpen(bool shouldOpen)
        {
            if (open == shouldOpen)
                return;

            open = shouldOpen;
            statusLine = string.Empty;

            if (open)
            {
                slots = SaveManager.EnumerateSlots();
                previousLockMode = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = previousLockMode;
                Cursor.visible = previousCursorVisible;
            }
        }

        private void OnGUI()
        {
            if (!open)
                return;

            const float width = 380f;
            float height = Mathf.Min(440f, 170f + slots.Count * 30f);
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);

            GUILayout.BeginArea(rect, GUI.skin.window);
            GUILayout.Label("<b>Save / Load</b>  (F9 closes)", RichLabel());
            GUILayout.Space(4f);

            // New save row.
            GUILayout.BeginHorizontal();
            newSlotName = GUILayout.TextField(newSlotName, GUILayout.Width(180f));
            if (GUILayout.Button("Save New", GUILayout.Width(90f)))
            {
                statusLine = saveManager.Save(newSlotName)
                    ? $"Saved '{SaveManager.SanitizeSlotName(newSlotName)}'."
                    : "Save failed — see console.";
                slots = SaveManager.EnumerateSlots();
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            // Existing slots.
            scroll = GUILayout.BeginScrollView(scroll);
            if (slots.Count == 0)
                GUILayout.Label("No saves yet.");

            foreach ((string slotName, DateTime writtenAt) in slots)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{slotName}  —  {writtenAt:yyyy-MM-dd HH:mm}", GUILayout.ExpandWidth(true));

                if (GUILayout.Button("Save", GUILayout.Width(50f)))
                {
                    statusLine = saveManager.Save(slotName) ? $"Overwrote '{slotName}'." : "Save failed — see console.";
                    slots = SaveManager.EnumerateSlots();
                    GUILayout.EndHorizontal();
                    break; // list changed — redraw next frame
                }

                if (GUILayout.Button("Load", GUILayout.Width(50f)))
                {
                    SetOpen(false);
                    saveManager.Load(slotName);
                    GUILayout.EndHorizontal();
                    break; // scene reload incoming
                }

                if (GUILayout.Button("Delete", GUILayout.Width(60f)))
                {
                    SaveManager.DeleteSlot(slotName);
                    statusLine = $"Deleted '{slotName}'.";
                    slots = SaveManager.EnumerateSlots();
                    GUILayout.EndHorizontal();
                    break;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(statusLine))
                GUILayout.Label(statusLine);

            GUILayout.EndArea();
        }

        private static GUIStyle RichLabel()
        {
            var style = new GUIStyle(GUI.skin.label) { richText = true };
            return style;
        }
    }
}
