using UnityEditor;
using UnityEngine;
using Xprees.EditorTools.Attributes.Expandable;

namespace Xprees.EditorTools.Editor.Attributes.Expandable
{
    // TODO make it work for the list of objects and custom editors
    [CustomPropertyDrawer(typeof(ExpandableAttribute))]
    public class ExpandableDrawer : PropertyDrawer
    {
        private UnityEditor.Editor _cachedEditor;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.PropertyField(position, property, label, true);

            if (property.objectReferenceValue == null) return;

            property.isExpanded = EditorGUI.Foldout(position, property.isExpanded, GUIContent.none);
            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                var rect = EditorGUILayout.BeginVertical(GUI.skin.box);

                if (_cachedEditor == null)
                {
                    UnityEditor.Editor.CreateCachedEditor(property.objectReferenceValue, null, ref _cachedEditor);
                }

                // Draw the item editor
                _cachedEditor.OnInspectorGUI();

                EditorGUILayout.EndVertical();
                DrawOutline(rect, Color.gray);
                EditorGUI.indentLevel--;
            }
        }

        private static void DrawOutline(Rect rect, Color color, int thickness = 1)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color); // Top
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color); // Left
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color); // Bottom
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color); // Right
        }
    }
}