using UnityEditor;
using UnityEngine;
using Xprees.EditorTools.MeshCombine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Right-aligned labels in the Hierarchy: the combine root with its state, the generated combined objects, the hidden originals, and for the
    /// meshes under a root that are not combined the reason (no Batching Static, script, children...).
    [InitializeOnLoad]
    internal static class MeshCombineHierarchyBadges
    {
        private static readonly Color Green = new(0.45f, 0.85f, 0.5f);
        private static readonly Color Orange = new(1f, 0.65f, 0.25f);
        private static readonly Color Blue = new(0.5f, 0.75f, 1f);
        private static readonly Color Gray = new(0.62f, 0.62f, 0.62f);
        private static GUIStyle style;

        static MeshCombineHierarchyBadges()
        {
            EditorApplication.hierarchyWindowItemOnGUI += OnItemGUI;
            MeshCombineState.Invalidated += EditorApplication.RepaintHierarchyWindow;
        }

        private static void OnItemGUI(int instanceId, Rect rect)
        {
            if (Event.current.type != EventType.Repaint || Application.isPlaying) return;
            if (EditorUtility.InstanceIDToObject(instanceId) is not GameObject go) return;

            var (text, color) = GetBadge(go);
            if (text == null) return;

            style ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            var content = new GUIContent(text);
            var width = style.CalcSize(content).x + 4;
            var badgeRect = new Rect(rect.xMax - width, rect.y, width, rect.height);
            EditorGUI.DrawRect(badgeRect,
                EditorGUIUtility.isProSkin ? new Color(0.22f, 0.22f, 0.22f, 0.85f) : new Color(0.76f, 0.76f, 0.76f, 0.85f));
            var previous = style.normal.textColor;
            style.normal.textColor = color;
            GUI.Label(badgeRect, content, style);
            style.normal.textColor = previous;
        }

        private static (string text, Color color) GetBadge(GameObject go)
        {
            if (go.TryGetComponent<MeshCombineRoot>(out var root))
            {
                return MeshCombineState.GetState(root) switch
                {
                    CombineState.Baked => ("combine: baked", Green),
                    CombineState.Stale => ("combine: STALE", Orange),
                    CombineState.Editing => ("combine: editing originals", Orange),
                    _ => ("combine: rebake to store state", Gray),
                };
            }

            if (go.name.StartsWith(MeshCombineTool.CombinedMeshPrefix) && go.TryGetComponent<MeshFilter>(out _))
            {
                var combinedRoot = go.GetComponentInParent<MeshCombineRoot>(true);
                return combinedRoot && combinedRoot.editingOriginals ? ("combined (hidden while editing)", Orange) : ("combined", Blue);
            }

            if (!go.TryGetComponent<MeshFilter>(out _)) return (null, default);

            var owner = go.GetComponentInParent<MeshCombineRoot>(true);
            if (!owner || owner.gameObject == go || MeshCombineTool.IsInsideCombinedMesh(go.transform, owner.transform)) return (null, default);

            if (!go.activeSelf) return go.CompareTag("EditorOnly") ? ("baked into combined", Gray) : (null, default);

            var reason = MeshCombineTool.GetNotCombinedReason(go);
            return reason == null ? (null, default) : (owner.editingOriginals ? "editing" : $"not combined: {reason}", Gray);
        }
    }
}