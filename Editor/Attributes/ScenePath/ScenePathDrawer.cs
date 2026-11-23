using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Xprees.EditorTools.Attributes.ScenePath;

namespace Xprees.EditorTools.Editor.Attributes.ScenePath
{
    /// Drawer for ScenePathAttribute: a SceneAsset picker bound to a string path, with a warning for scenes that
    /// are missing or not in the build profile's scene list. Implements UI Toolkit and IMGUI.
    [CustomPropertyDrawer(typeof(ScenePathAttribute))]
    public class ScenePathDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            if (property.propertyType != SerializedPropertyType.String) return new HelpBox($"[ScenePath] needs a string field ({property.name}).", HelpBoxMessageType.Error);

            var root = new VisualElement();
            var field = new ObjectField(property.displayName) { objectType = typeof(SceneAsset), allowSceneObjects = false };
            var status = new HelpBox("", HelpBoxMessageType.Warning);
            root.Add(field);
            root.Add(status);

            void Refresh()
            {
                field.SetValueWithoutNotify(Load(property.stringValue));
                var message = Status(property.stringValue);
                status.text = message;
                status.style.display = string.IsNullOrEmpty(message) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            field.RegisterValueChangedCallback(evt =>
            {
                property.stringValue = evt.newValue != null ? AssetDatabase.GetAssetPath(evt.newValue) : "";
                property.serializedObject.ApplyModifiedProperties();
                Refresh();
            });
            root.TrackPropertyValue(property, _ => Refresh());
            Refresh();
            return root;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "[ScenePath] needs a string field");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            var scene = EditorGUI.ObjectField(position, label, Load(property.stringValue), typeof(SceneAsset), false);
            if (EditorGUI.EndChangeCheck()) property.stringValue = scene != null ? AssetDatabase.GetAssetPath(scene) : "";
            EditorGUI.EndProperty();
        }

        private static SceneAsset Load(string path) => string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

        private static string Status(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (Load(path) == null) return $"No scene at '{path}'.";
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.path == path) return scene.enabled ? "" : "Scene is disabled in the build scene list.";
            }
            return "Scene is not in the build scene list: it loads in the Editor only.";
        }
    }
}
