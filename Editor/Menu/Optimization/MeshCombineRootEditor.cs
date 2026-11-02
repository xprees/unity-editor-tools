using System;
using UnityEditor;
using UnityEngine;
using Xprees.EditorTools.MeshCombine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    [CustomEditor(typeof(MeshCombineRoot))]
    internal class MeshCombineRootEditor : UnityEditor.Editor
    {
        private void OnEnable() => MeshCombineState.Invalidated += Repaint;

        private void OnDisable() => MeshCombineState.Invalidated -= Repaint;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            DrawControls((MeshCombineRoot) target, false);
        }

        /// The state of the root and its buttons (Edit originals, Bake, Rebake; Rebake in prefab asset on a prefab instance). Compact: one line of
        /// state instead of the explaining boxes, for the header of the combined meshes and the originals.
        internal static void DrawControls(MeshCombineRoot root, bool compact)
        {
            var isInstance = PrefabUtility.IsPartOfPrefabInstance(root.gameObject);
            var state = MeshCombineState.GetState(root);
            DrawState(state, compact);

            if (isInstance) DrawInstanceBake(root, compact);

            using (new EditorGUI.DisabledScope(isInstance))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (state == CombineState.Editing)
                {
                    if (GUILayout.Button("Bake")) Run(() => MeshCombineTool.Rebake(root.gameObject));
                }
                else
                {
                    if (GUILayout.Button("Edit originals")) Run(() => MeshCombineTool.EnterEditMode(root));
                    if (GUILayout.Button("Rebake")) Run(() => MeshCombineTool.Rebake(root.gameObject));
                }
            }
        }

        private static void DrawState(CombineState state, bool compact)
        {
            var (text, type) = state switch
            {
                CombineState.Baked => ("The combined meshes are up to date.", MessageType.None),
                CombineState.Stale => (
                    "The sources changed since the last bake (moved, added, removed or changed props, or the tool settings). " +
                    "Rebake, or it is baked when you save (Project Settings > Xprees > Mesh Combine).", MessageType.Warning),
                CombineState.Editing => (
                    "Editing originals: the combined objects are hidden. Move, add or remove props freely, then Bake. Saving bakes too, " +
                    "except in Prefab Mode with Auto Save on (use Bake).", MessageType.Warning),
                _ => ("Baked by an older version of the tool, no stored state to compare. Rebake once to store it.", MessageType.Info),
            };
            if (!compact) EditorGUILayout.HelpBox(text, type);
            else EditorGUILayout.LabelField(state == CombineState.Editing ? "Editing originals" : state.ToString(), EditorStyles.miniBoldLabel);
        }

        /// A root inside a prefab instance is baked in the prefab asset in the background, so the prefab does not have to be opened.
        private static void DrawInstanceBake(MeshCombineRoot root, bool compact)
        {
            var sourcePath = MeshCombineInstanceBake.GetSourcePath(root, out var error);
            if (!compact)
            {
                EditorGUILayout.HelpBox(sourcePath != null
                    ? $"This is part of a prefab instance. 'Rebake in prefab asset' applies the overrides under this object to '{sourcePath}', "
                      +
                      "rebakes it in the background and saves it, so you do not have to open the prefab. Edit originals needs the prefab open."
                    : $"This is part of a prefab instance and cannot be baked from here. {error}", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(sourcePath == null))
            {
                if (GUILayout.Button("Rebake in prefab asset"))
                    Run(() => Debug.Log($"Rebaked '{sourcePath}' ({MeshCombineInstanceBake.Rebake(root)} override(s) applied)."));
            }
        }

        internal static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError(e.Message);
            }

            EditorApplication.RepaintHierarchyWindow();
        }
    }
}