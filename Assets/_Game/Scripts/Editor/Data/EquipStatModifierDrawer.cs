using System.Collections.Generic;
using IslandGame.Data.Stats;
using UnityEditor;
using UnityEngine;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Drawer for the EquipStatModifier entries on ItemDefinition: the stat is
    /// picked from a dropdown of every StatDefinition in the StatDatabase
    /// (stored as the stable ID string), so designers can't typo an ID. Falls
    /// back to a plain text field when the database is missing/empty. Target,
    /// type and value render on one compact second line.
    /// </summary>
    [CustomPropertyDrawer(typeof(EquipStatModifier))]
    internal sealed class EquipStatModifierDrawer : PropertyDrawer
    {
        private const float LineSpacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2f + LineSpacing * 3f;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            SerializedProperty statId = property.FindPropertyRelative("statId");
            SerializedProperty target = property.FindPropertyRelative("target");
            SerializedProperty type = property.FindPropertyRelative("type");
            SerializedProperty value = property.FindPropertyRelative("value");

            float line = EditorGUIUtility.singleLineHeight;
            var statRect = new Rect(position.x, position.y + LineSpacing, position.width, line);
            var detailRect = new Rect(position.x, statRect.yMax + LineSpacing, position.width, line);

            DrawStatIdField(statRect, statId);

            // Second line: target | type | value, sharing the row.
            float third = (detailRect.width - 8f) / 3f;
            var targetRect = new Rect(detailRect.x, detailRect.y, third, line);
            var typeRect = new Rect(targetRect.xMax + 4f, detailRect.y, third, line);
            var valueRect = new Rect(typeRect.xMax + 4f, detailRect.y, third, line);

            EditorGUI.PropertyField(targetRect, target, GUIContent.none);
            EditorGUI.PropertyField(typeRect, type, GUIContent.none);
            EditorGUI.PropertyField(valueRect, value, GUIContent.none);

            EditorGUI.EndProperty();
        }

        private static void DrawStatIdField(Rect rect, SerializedProperty statId)
        {
            IReadOnlyList<StatDefinition> all = LoadStats();
            if (all == null || all.Count == 0)
            {
                EditorGUI.PropertyField(rect, statId, new GUIContent("Stat Id",
                    "StatDatabase not found or empty — run Island Game/Data/Sync Databases for the dropdown."));
                return;
            }

            // Build the option list: index 0 = none, then every known stat.
            var labels = new string[all.Count + 1];
            labels[0] = "(none)";
            int currentIndex = 0;
            for (int i = 0; i < all.Count; i++)
            {
                StatDefinition stat = all[i];
                labels[i + 1] = stat != null ? $"{stat.DisplayName} ({stat.Id})" : "(missing)";
                if (stat != null && stat.Id == statId.stringValue)
                    currentIndex = i + 1;
            }

            // An ID that no longer resolves still shows, flagged, so data is never silently lost.
            if (currentIndex == 0 && !string.IsNullOrEmpty(statId.stringValue))
                labels[0] = $"(unknown: {statId.stringValue})";

            int picked = EditorGUI.Popup(rect, "Stat", currentIndex, labels);
            if (picked != currentIndex)
                statId.stringValue = picked <= 0 ? string.Empty : all[picked - 1].Id;
        }

        private static IReadOnlyList<StatDefinition> LoadStats()
        {
            var database = AssetDatabase.LoadAssetAtPath<StatDatabase>(
                $"{DefinitionDatabaseSync.DatabaseFolder}/StatDatabase.asset");
            return database != null ? database.All : null;
        }
    }
}
