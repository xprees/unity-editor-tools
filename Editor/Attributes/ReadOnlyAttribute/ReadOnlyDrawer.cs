﻿// This Code is used from Unity Open Source Project #1 (UOP1) Chop Chop
// https://github.com/UnityTechnologies/open-project-1

using UnityEditor;
using UnityEngine;

namespace Xprees.EditorTools.Editor.Attributes.ReadOnlyAttribute
{
#if UNITY_EDITOR

    /// <summary>
    /// Custom drawer for the ReadOnly attribute
    /// </summary>
    [CustomPropertyDrawer(typeof(EditorTools.Attributes.ReadOnlyAttribute.ReadOnlyAttribute))]
    public class ReadOnlyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var previousGUIState = GUI.enabled;

            GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label);
            GUI.enabled = previousGUIState;
        }
    }
#endif
}