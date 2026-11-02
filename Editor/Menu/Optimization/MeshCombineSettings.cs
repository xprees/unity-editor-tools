using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Project settings of the Mesh Combine tool (ProjectSettings/XpreesMeshCombineSettings.asset, edited in Project Settings > Xprees > Mesh Combine).
    [FilePath("ProjectSettings/XpreesMeshCombineSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class MeshCombineSettings : ScriptableSingleton<MeshCombineSettings>
    {
        [Tooltip("Only objects with the opt-in static flag are combined. Interactables and anything else without it stay separate objects.")]
        public bool requireStaticFlag = true;

        [Tooltip("The static flag that opts an object in. Batching Static is free to use: static batching is off in this project.")]
        public StaticEditorFlags optInFlag = StaticEditorFlags.BatchingStatic;

        [Min(1)] public int defaultMinGroupSize = 2;

        [Tooltip(
            "Rebake the stale combine roots of a scene or prefab when it is saved. A root in 'Edit originals' mode is always baked on save.")]
        public bool autoRebakeOnSave = true;

        [Tooltip(
            "Before a build, rebake the stale combine roots in all prefabs and scenes (the assets are saved) and fail the build if one fails.")]
        public bool rebakeOnBuild;

        [Tooltip("Components that may stay on a combinable object. Objects with different settings of them are combined separately, and the " +
                 "combined object (and its collider container) gets a copy. Assembly-qualified type names.")]
        public List<string> groupedComponentTypes = new() { "Unity.AI.Navigation.NavMeshModifier, Unity.AI.Navigation" };

        [Tooltip("Components that may stay on a combinable object and are copied to a child at the object's pose in the collider container. " +
                 "Assembly-qualified type names.")]
        public List<string> copiedComponentTypes = new() { "UnityEngine.AI.NavMeshObstacle, UnityEngine.AIModule" };

        public IEnumerable<Type> GroupedTypes => Resolve(groupedComponentTypes);
        public IEnumerable<Type> CopiedTypes => Resolve(copiedComponentTypes);

        /// A component that does not keep an object from being combined.
        public bool IsMovable(Component component) =>
            component && (GroupedTypes.Any(type => type.IsInstanceOfType(component))
                          || CopiedTypes.Any(type => type.IsInstanceOfType(component)));

        public bool HasOptIn(GameObject gameObject) =>
            !requireStaticFlag || (GameObjectUtility.GetStaticEditorFlags(gameObject) & optInFlag) != 0;

        public void SaveSettings() => Save(true);

        public static IEnumerable<Type> Resolve(IEnumerable<string> typeNames) =>
            typeNames.Select(name => Type.GetType(name)).Where(type => type != null && typeof(Component).IsAssignableFrom(type));
    }

    internal static class MeshCombineSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create() => new("Project/Xprees/Mesh Combine", SettingsScope.Project)
        {
            label = "Mesh Combine",
            keywords = new[] { "mesh", "combine", "static", "batching", "navmesh", "collider" },
            guiHandler = _ =>
            {
                var so = new SerializedObject(MeshCombineSettings.instance);
                EditorGUILayout.PropertyField(so.FindProperty(nameof(MeshCombineSettings.requireStaticFlag)));
                using (new EditorGUI.DisabledScope(!MeshCombineSettings.instance.requireStaticFlag))
                {
                    EditorGUILayout.PropertyField(so.FindProperty(nameof(MeshCombineSettings.optInFlag)));
                }

                EditorGUILayout.PropertyField(so.FindProperty(nameof(MeshCombineSettings.defaultMinGroupSize)));
                EditorGUILayout.PropertyField(so.FindProperty(nameof(MeshCombineSettings.autoRebakeOnSave)));
                EditorGUILayout.PropertyField(so.FindProperty(nameof(MeshCombineSettings.rebakeOnBuild)));
                DrawTypeList(so.FindProperty(nameof(MeshCombineSettings.groupedComponentTypes)));
                DrawTypeList(so.FindProperty(nameof(MeshCombineSettings.copiedComponentTypes)));
                if (so.ApplyModifiedProperties()) MeshCombineSettings.instance.SaveSettings();
            },
        };

        private static void DrawTypeList(SerializedProperty list)
        {
            EditorGUILayout.PropertyField(list, true);
            for (var i = 0; i < list.arraySize; i++)
            {
                var name = list.GetArrayElementAtIndex(i).stringValue;
                if (!string.IsNullOrWhiteSpace(name) && !MeshCombineSettings.Resolve(new[] { name }).Any())
                    EditorGUILayout.HelpBox($"Type not found (or not a Component): {name}", MessageType.Warning);
            }
        }
    }
}