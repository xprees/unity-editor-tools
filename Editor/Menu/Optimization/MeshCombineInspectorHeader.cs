using System.Linq;
using UnityEditor;
using UnityEngine;
using Xprees.EditorTools.MeshCombine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// The combine controls (state, Edit originals / Bake / Rebake) in the Inspector header of the generated combined meshes and of the originals
    /// under a combine root, so a designer who selects one of them does not have to find the root first.
    [InitializeOnLoad]
    internal static class MeshCombineInspectorHeader
    {
        static MeshCombineInspectorHeader()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
            MeshCombineState.Invalidated += EditorApplication.RepaintHierarchyWindow;
        }

        private static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1 || editor.target is not GameObject go || Application.isPlaying) return;

            var root = go.GetComponentInParent<MeshCombineRoot>(true);
            if (!root || root.gameObject == go || !go.TryGetComponent<MeshFilter>(out _)) return;

            var isCombined = go.name.StartsWith(MeshCombineTool.CombinedMeshPrefix);
            string title;
            if (isCombined) title = $"Combined mesh of '{root.name}'";
            else if (MeshCombineTool.IsInsideCombinedMesh(go.transform, root.transform)) return;
            else if (!go.activeSelf && go.CompareTag("EditorOnly")) title = $"Original, baked into the combined meshes of '{root.name}'";
            else if (root.editingOriginals && go.activeSelf) title = $"Original being edited ('{root.name}' is in Edit originals mode)";
            else return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
                MeshCombineRootEditor.DrawControls(root, true);
                if (isCombined) DrawSourceButtons(go, root);
                else if (GUILayout.Button("Select the combine root")) Selection.activeGameObject = root.gameObject;
            }
        }

        private static void DrawSourceButtons(GameObject combined, MeshCombineRoot root)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select the originals"))
                {
                    var sources = MeshCombineTool.GetSources(combined, root);
                    if (sources.Count > 0) Selection.objects = sources.Cast<Object>().ToArray();
                }

                if (GUILayout.Button("Select the combine root")) Selection.activeGameObject = root.gameObject;
            }
        }
    }
}