using IslandGame.Data.Creatures;
using UnityEditor;

namespace IslandGame.EditorTools.Data
{
    /// <summary>
    /// Inspector for CreatureDefinition with the conditional-fields pattern
    /// the Item Editor uses for its weapon/tool sections: the Taming block
    /// renders only while Tameable is checked, so non-tameable species keep
    /// a clean inspector and nobody tunes fields that can't matter.
    /// </summary>
    [CustomEditor(typeof(CreatureDefinition))]
    [CanEditMultipleObjects]
    internal sealed class CreatureDefinitionEditor : Editor
    {
        private static readonly string[] HiddenInDefaultPass =
        {
            "m_Script",
            "tameable", "favoriteFoods", "feedingsToTame", "feedCooldownSeconds",
            "canAssistInCombat", "tamedStatModifiers", "followDistance",
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, HiddenInDefaultPass);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Taming", EditorStyles.boldLabel);

            SerializedProperty tameable = serializedObject.FindProperty("tameable");
            EditorGUILayout.PropertyField(tameable);

            if (tameable.boolValue || tameable.hasMultipleDifferentValues)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(serializedObject.FindProperty("favoriteFoods"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("feedingsToTame"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("feedCooldownSeconds"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("canAssistInCombat"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("tamedStatModifiers"), true);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("followDistance"));
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
