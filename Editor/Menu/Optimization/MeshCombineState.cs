using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using Xprees.EditorTools.MeshCombine;
using Object = UnityEngine.Object;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    public enum CombineState
    {
        /// Baked by an older version of the tool: no stored state to compare with. A rebake stores it.
        Unknown,
        Baked,
        Stale,
        Editing,
    }

    /// Stale detection of the combine roots: the state of the sources (everything the bake reads) is hashed at the bake and compared later.
    /// States are cached per root and recomputed (lazily, only for the roots asked for) a moment after the last change in the scene.
    [InitializeOnLoad]
    internal static class MeshCombineState
    {
        private const double DebounceSeconds = 0.7;
        private static readonly Dictionary<EntityId, CombineState> Cache = new();
        private static double dirtyAt = -1;

        /// Raised when cached states were dropped, so the views repaint.
        public static event Action Invalidated;

        static MeshCombineState()
        {
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => MarkDirty();
            Undo.undoRedoPerformed += MarkDirty;
            EditorApplication.update += Tick;
        }

        private static void MarkDirty() => dirtyAt = EditorApplication.timeSinceStartup;

        private static void Tick()
        {
            if (dirtyAt < 0 || EditorApplication.timeSinceStartup - dirtyAt < DebounceSeconds) return;

            dirtyAt = -1;
            Cache.Clear();
            Invalidated?.Invoke();
        }

        public static CombineState GetState(MeshCombineRoot root)
        {
            if (root.editingOriginals) return CombineState.Editing;
            var id = root.GetEntityId();
            if (Cache.TryGetValue(id, out var cached)) return cached;

            return Cache[id] = ComputeState(root);
        }

        /// Not cached: the current state of the sources against the stored one.
        public static CombineState ComputeState(MeshCombineRoot root)
        {
            if (root.editingOriginals) return CombineState.Editing;
            if (string.IsNullOrEmpty(root.bakeHash)) return CombineState.Unknown;

            return ComputeHash(root) == root.bakeHash ? CombineState.Baked : CombineState.Stale;
        }

        /// Everything the bake reads from the sources under the root, plus the tool settings that decide what is combined. The generated combined objects
        /// are left out, and so is whether an original is shown or hidden (edit mode, baked): only whether it takes part counts.
        public static string ComputeHash(MeshCombineRoot root)
        {
            var settings = MeshCombineSettings.instance;
            var builder = new StringBuilder();
            builder.Append(root.recursive).Append('|').Append(MeshCombineTool.GetMinGroupSize(root)).Append('|')
                .Append(settings.requireStaticFlag).Append('|')
                .Append((int) settings.optInFlag).Append('|');
            Append(root.transform, root.transform, string.Empty, builder);
            return Hash128.Compute(builder.ToString()).ToString();
        }

        private static void Append(Transform root, Transform parent, string path, StringBuilder builder)
        {
            foreach (Transform child in parent)
            {
                if (child.name.StartsWith(MeshCombineTool.CombinedMeshPrefix) && child.GetComponent<MeshFilter>()) continue; // Generated

                var childPath = $"{path}/{child.name}";
                var go = child.gameObject;
                if (go.TryGetComponent<MeshFilter>(out var meshFilter))
                {
                    var taking = go.activeSelf || go.CompareTag("EditorOnly"); // Active, or hidden by a bake
                    builder.Append(childPath).Append(taking ? '+' : '-')
                        .Append(child.localToWorldMatrix.ToString("F3")).Append(AssetKey(meshFilter.sharedMesh))
                        .Append((int) GameObjectUtility.GetStaticEditorFlags(go)).Append(go.layer);
                    if (go.TryGetComponent<MeshRenderer>(out var renderer))
                    {
                        foreach (var material in renderer.sharedMaterials)
                        {
                            builder.Append(AssetKey(material));
                        }
                    }

                    foreach (var component in go.GetComponents<Component>())
                    {
                        AppendComponent(component, builder);
                    }

                    builder.Append(';');
                }

                Append(root, child, childPath, builder);
            }
        }

        private static void AppendComponent(Component component, StringBuilder builder)
        {
            if (!component) return;
            if (component is Transform or MeshFilter or MeshRenderer) return;

            builder.Append('<').Append(component.GetType().Name);
            // Colliders and whitelisted components are copied: their settings matter. Other components only keep the object out
            if (component is Collider || MeshCombineSettings.instance.IsMovable(component)) AppendSettings(component, builder);
            builder.Append('>');
        }

        private static void AppendSettings(Component component, StringBuilder builder)
        {
            var serialized = new SerializedObject(component);
            var property = serialized.GetIterator();
            for (var enter = true; property.NextVisible(enter); enter = true)
            {
                if (property.propertyPath == "m_Script") continue;

                builder.Append(property.propertyPath).Append('=').Append(property.propertyType switch
                {
                    SerializedPropertyType.Integer or SerializedPropertyType.LayerMask => property.longValue.ToString(),
                    SerializedPropertyType.Boolean => property.boolValue.ToString(),
                    SerializedPropertyType.Float => property.floatValue.ToString("F4"),
                    SerializedPropertyType.String => property.stringValue,
                    SerializedPropertyType.Enum => property.enumValueIndex.ToString(),
                    SerializedPropertyType.Vector3 => property.vector3Value.ToString("F4"),
                    SerializedPropertyType.ObjectReference => AssetKey(property.objectReferenceValue),
                    _ => string.Empty, // Arrays and structs: their children follow
                }).Append(',');
            }
        }

        /// Stable across sessions: the asset path and the name inside it (instance ids are not).
        private static string AssetKey(Object asset) =>
            !asset ? "null" : $"{AssetDatabase.GetAssetPath(asset)}#{asset.name}";
    }
}