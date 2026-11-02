using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Xprees.EditorTools.MeshCombine;
using Object = UnityEngine.Object;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Set of Mesh optimization tools for combining meshes in Unity.
    public static class MeshCombineTool
    {
        public const string CombinedMeshPrefix = "Combined Mesh";
        private const string EditorOnlyTag = "EditorOnly";
        private const string UntaggedTag = "Untagged";
        internal const string FallbackMeshFolder = "Assets/CombinedMeshes";
        internal const string SceneMeshSubfolder = "combined-meshes";

        /// Mesh assets of the old combined objects a rebake can write the new geometry into (by object name), and the ones it did.
        private class RebakeContext
        {
            public readonly Dictionary<string, Queue<string>> Reusable = new();
            public readonly HashSet<string> Reused = new();
        }

        [MenuItem("Tools/Optimization/Combine selected meshes (New Parent)", priority = 2)]
        public static void CombineSelectedMeshesCreateParent() => CombineIntoNewParent(Selection.gameObjects);

        [MenuItem("Tools/Optimization/Combine child meshes of selected parent", priority = 1)]
        public static void CombineChildMeshesOfSelectedParent()
        {
            var parentGameObject = Selection.activeGameObject;
            if (parentGameObject == null)
            {
                Debug.LogError("No active GameObject selected.");
                return;
            }

            CombineChildren(parentGameObject);
        }

        [MenuItem("Tools/Optimization/Combine all meshes under selected parent (recursive)", priority = 3)]
        public static void CombineDescendantMeshesOfSelectedParent()
        {
            var parentGameObject = Selection.activeGameObject;
            if (parentGameObject == null)
            {
                Debug.LogError("No active GameObject selected.");
                return;
            }

            CombineDescendants(parentGameObject);
        }

        [MenuItem("Tools/Optimization/Rebake combined meshes of selected parent", priority = 5)]
        public static void RebakeSelectedParent()
        {
            var parentGameObject = Selection.activeGameObject;
            if (parentGameObject == null)
            {
                Debug.LogError("No active GameObject selected.");
                return;
            }

            try
            {
                Rebake(parentGameObject);
            }
            catch (InvalidOperationException e)
            {
                Debug.LogError(e.Message);
            }
        }

        /// Combine the active child meshes of the parent into "Combined Mesh - ..." children (one per material, static flag and layer).
        /// The originals are deactivated and tagged EditorOnly. Mesh assets are saved next to <paramref name="meshOwnerPath"/>
        /// (prefab or scene asset path), resolved from the parent when empty. Returns the combined objects.
        /// A parent that already has combined children is rebaked instead (see <see cref="Rebake"/>).
        public static List<GameObject> CombineChildren(GameObject parent, string meshOwnerPath = null, int minGroupSize = 0)
        {
            if (HasCombinedChildren(parent)) return Rebake(parent, false, meshOwnerPath, minGroupSize);

            var pieces = parent.GetChildGameObjects().Where(IsCombinable).ToArray();
            return CombineUnderParent(parent, pieces, false, minGroupSize, "Combine child meshes of selected parent", meshOwnerPath);
        }

        /// Combine the plain mesh objects (only MeshFilter, MeshRenderer and colliders, no children) at any depth under the parent into
        /// "Combined Mesh - ..." children of the parent, like <see cref="CombineChildren"/>. Inactive objects, mesh objects with children, objects
        /// without the opt-in static flag (Project Settings > Xprees > Mesh Combine) and objects with other components (scripts, lights, LODs...)
        /// are skipped with their whole subtree. Components in the settings whitelist (navmesh modifier/obstacle) do not keep an object out.
        /// A parent that already has combined children is rebaked instead (see <see cref="Rebake"/>).
        public static List<GameObject> CombineDescendants(GameObject parent, string meshOwnerPath = null, int minGroupSize = 0)
        {
            if (HasCombinedChildren(parent)) return Rebake(parent, true, meshOwnerPath, minGroupSize);

            var pieces = new List<GameObject>();
            CollectPlainMeshes(parent.transform, pieces);
            return CombineUnderParent(parent, pieces.ToArray(), true, minGroupSize, "Combine all meshes under selected parent", meshOwnerPath);
        }

        /// The tool settings of the parent: its <see cref="MeshCombineRoot"/> (added when missing, remembering the mode and the explicit minimum).
        private static MeshCombineRoot EnsureRoot(GameObject parent, bool recursive, int minGroupSize)
        {
            if (!parent.TryGetComponent<MeshCombineRoot>(out var root)) root = Undo.AddComponent<MeshCombineRoot>(parent);
            else Undo.RecordObject(root, "Update Mesh Combine Root");

            root.recursive = recursive;
            root.editingOriginals = false; // Combining always leaves the edit mode
            if (minGroupSize > 0) root.minGroupSize = minGroupSize;
            EditorUtility.SetDirty(root);
            return root;
        }

        /// Remember the state of the sources the combined meshes were just baked from (stale detection).
        private static void StoreBakeHash(MeshCombineRoot root)
        {
            Undo.RecordObject(root, "Store Mesh Combine bake state");
            root.bakeHash = MeshCombineState.ComputeHash(root);
            EditorUtility.SetDirty(root);
        }

        /// Show the originals for editing: the combined objects are hidden (their mesh assets stay), the EditorOnly originals are active again.
        /// <see cref="Rebake"/> (the Bake button, or saving) combines them again. Throws on a prefab instance (edit the prefab).
        public static void EnterEditMode(MeshCombineRoot root)
        {
            const string undoName = "Edit originals of combined meshes";
            var parent = root.gameObject;
            if (PrefabUtility.IsPartOfPrefabInstance(parent))
                throw new InvalidOperationException($"'{parent.name}' is part of a prefab instance, open the prefab and edit there.");
            if (root.editingOriginals) return;

            Undo.IncrementCurrentGroup();
            var undoGroupIndex = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            Undo.RecordObject(root, undoName);
            foreach (var combined in GetCombinedChildren(parent))
            {
                Undo.RegisterCompleteObjectUndo(combined, undoName);
                combined.SetActive(false); // Not tagged EditorOnly: a rebake tells it from the merged-in old combined meshes
            }

            foreach (var original in parent.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).Where(IsHiddenOriginal).ToList())
            {
                if (IsInsideCombinedMesh(original.transform, parent.transform)) continue;

                Undo.RegisterCompleteObjectUndo(original, undoName);
                original.SetActive(true);
                original.tag = UntaggedTag;
            }

            root.editingOriginals = true;
            EditorUtility.SetDirty(root);
            Undo.CollapseUndoOperations(undoGroupIndex);
            MeshCombineAutoBake.RefreshAutoSavePause(); // Prefab Mode Auto Save would save (and with it end) the edit mode
        }

        /// A generated "Combined Mesh" object (or a child of it, e.g. its colliders) between the object and the parent.
        internal static bool IsInsideCombinedMesh(Transform candidate, Transform parent)
        {
            for (var t = candidate; t && t != parent; t = t.parent)
            {
                if (t.name.StartsWith(CombinedMeshPrefix) && t.GetComponent<MeshFilter>()) return true;
            }

            return false;
        }

        /// The originals a combined mesh was baked from: the objects under the root (outside the combined objects) that are hidden by a bake, or
        /// shown for editing, with the same material, static flags, layer and navmesh modifier settings.
        internal static List<GameObject> GetSources(GameObject combined, MeshCombineRoot root)
        {
            var material = combined.GetComponent<MeshRenderer>() ? combined.GetComponent<MeshRenderer>().sharedMaterial : null;
            var flags = GameObjectUtility.GetStaticEditorFlags(combined);
            var signature = GroupedComponentSignature(combined);
            return root.GetComponentsInChildren<MeshRenderer>(true)
                .Select(renderer => renderer.gameObject)
                .Where(go => go != combined && !IsInsideCombinedMesh(go.transform, root.transform) && IsPlainOrMovable(go)
                             && (IsHiddenOriginal(go) || (root.editingOriginals && go.activeSelf))
                             && go.layer == combined.layer && GameObjectUtility.GetStaticEditorFlags(go) == flags
                             && go.GetComponent<MeshRenderer>().sharedMaterials.Contains(material)
                             && GroupedComponentSignature(go) == signature)
                .ToList();
        }

        /// Why a mesh object under a combine root is not combined, or null when it is (or is a hidden original).
        internal static string GetNotCombinedReason(GameObject candidate)
        {
            if (!candidate.activeSelf) return null;
            if (candidate.transform.childCount > 0) return "has children";

            foreach (var component in candidate.GetComponents<Component>())
            {
                if (!component) return "missing script";
                if (IsPlainMeshComponent(component) || MeshCombineSettings.instance.IsMovable(component)) continue;

                return $"script: {component.GetType().Name}";
            }

            return MeshCombineSettings.instance.HasOptIn(candidate)
                ? "alone in its group or not baked"
                : $"no {MeshCombineSettings.instance.optInFlag}";
        }

        internal static int GetMinGroupSize(MeshCombineRoot root) =>
            root && root.minGroupSize > 0 ? root.minGroupSize : Mathf.Max(1, MeshCombineSettings.instance.defaultMinGroupSize);

        /// Active, no children, a mesh, only plain or whitelisted components, and the opt-in static flag.
        private static bool IsCombinable(GameObject candidate) =>
            candidate.activeSelf && IsPlainOrMovable(candidate) && MeshCombineSettings.instance.HasOptIn(candidate);

        /// Structure only (no activity or opt-in): no children, a MeshFilter, and only plain or whitelisted components.
        private static bool IsPlainOrMovable(GameObject candidate) =>
            candidate.transform.childCount == 0 && candidate.TryGetComponent<MeshFilter>(out _)
                                                && candidate.GetComponents<Component>().All(component =>
                                                    component && (IsPlainMeshComponent(component)
                                                                  || MeshCombineSettings.instance.IsMovable(component)));

        /// A direct child of the parent is a "Combined Mesh - ..." object created by this tool.
        public static bool HasCombinedChildren(GameObject parent) => GetCombinedChildren(parent).Count > 0;

        internal static List<GameObject> GetCombinedChildren(GameObject parent) =>
            parent.transform.Cast<Transform>()
                .Where(child => child.name.StartsWith(CombinedMeshPrefix) && child.GetComponent<MeshFilter>())
                .Select(child => child.gameObject)
                .ToList();

        /// Combine the parent again from its originals (the inactive EditorOnly objects): the old combined objects are removed (the disabled ones merged in
        /// by older recursive combines too), the originals are enabled and combined, and the new geometry is written into the old mesh assets (same
        /// GUID, matched by object name). Mesh assets nothing uses any more are queued for deletion until the scene/prefab is saved.
        /// <paramref name="recursive"/> null detects it: recursive when an original sits deeper than a direct child.
        /// Throws when the parent is part of a prefab instance (rebake it in the prefab).
        public static List<GameObject> Rebake(GameObject parent, bool? recursive = null, string meshOwnerPath = null, int minGroupSize = 0)
        {
            const string undoName = "Rebake combined meshes";
            if (PrefabUtility.IsPartOfPrefabInstance(parent))
                throw new InvalidOperationException($"'{parent.name}' is part of a prefab instance, open the prefab and rebake there.");

            Undo.IncrementCurrentGroup();
            var undoGroupIndex = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            Undo.RegisterCompleteObjectUndo(parent, undoName);

            var context = new RebakeContext();
            var leftovers = new List<string>();
            foreach (var old in GetCombinedChildren(parent))
            {
                var path = AssetDatabase.GetAssetPath(old.GetComponent<MeshFilter>().sharedMesh);
                // Hidden by the edit mode (not EditorOnly) or active: its asset is reused. EditorOnly + hidden: merged in by an older recursive combine
                if ((old.activeSelf || !old.CompareTag(EditorOnlyTag)) && CombinedMeshCleanup.IsCombinedMeshPath(path))
                {
                    if (!context.Reusable.TryGetValue(old.name, out var queue)) context.Reusable[old.name] = queue = new Queue<string>();
                    queue.Enqueue(path);
                }
                else leftovers.Add(path); // Disabled: merged in by an older recursive combine, its geometry is in the active ones

                Undo.DestroyObjectImmediate(old);
            }

            // The stored mode wins over detection; an explicit argument replaces it
            parent.TryGetComponent<MeshCombineRoot>(out var existingRoot);
            var originals = new List<GameObject>();
            if (recursive ?? existingRoot?.recursive ?? HasHiddenOriginalsBelowChildren(parent))
                CollectPlainMeshes(parent.transform, originals, true);
            else originals.AddRange(parent.GetChildGameObjects(true).Where(IsHiddenOriginal));
            recursive ??= existingRoot ? existingRoot.recursive : originals.Any(original => original.transform.parent != parent.transform);
            var root = EnsureRoot(parent, recursive.Value, minGroupSize);

            // All originals come back (EditorOnly removed). Those that no longer qualify (flag removed, group too small) simply stay active
            foreach (var original in originals.Where(original => !original.activeSelf))
            {
                Undo.RegisterCompleteObjectUndo(original, undoName);
                original.SetActive(true);
                original.tag = UntaggedTag;
            }

            GameObject[] pieces;
            if (recursive.Value)
            {
                var plainMeshes = new List<GameObject>();
                CollectPlainMeshes(parent.transform, plainMeshes);
                pieces = plainMeshes.ToArray();
            }
            else pieces = parent.GetChildGameObjects().Where(IsCombinable).ToArray();

            foreach (var piece in pieces)
            {
                Undo.RegisterCompleteObjectUndo(piece, undoName);
            }

            var combined = CombineMeshesAndColliders(parent, pieces, meshOwnerPath, GetMinGroupSize(root), context);
            StoreBakeHash(root);
            MeshCombineAutoBake.RefreshAutoSavePause();

            Undo.CollapseUndoOperations(undoGroupIndex);
            CombinedMeshUndo.Track(undoGroupIndex, parent, meshOwnerPath,
                combined.Where(go => !context.Reused.Contains(AssetDatabase.GetAssetPath(go.GetComponent<MeshFilter>().sharedMesh))));

            leftovers.AddRange(context.Reusable.Values.SelectMany(queue => queue)); // Old assets no new object took over
            CombinedMeshCleanup.QueueLeftovers(leftovers);
            return combined;
        }

        private static bool IsHiddenOriginal(GameObject candidate) =>
            !candidate.activeSelf && candidate.CompareTag(EditorOnlyTag) && !candidate.name.StartsWith(CombinedMeshPrefix)
            && candidate.TryGetComponent<MeshFilter>(out _);

        private static bool HasHiddenOriginalsBelowChildren(GameObject parent) =>
            parent.GetComponentsInChildren<Transform>(true).Any(child => child.parent != parent.transform && child != parent.transform
                && IsHiddenOriginal(child.gameObject));

        private static void CollectPlainMeshes(Transform parent, List<GameObject> pieces, bool includeHiddenOriginals = false)
        {
            foreach (Transform child in parent)
            {
                var childObject = child.gameObject;
                var hidden = !childObject.activeSelf;
                if (hidden && !(includeHiddenOriginals && IsHiddenOriginal(childObject))) continue; // Inactive containers are not reachable

                var components = childObject.GetComponents<Component>();
                if (components.Length == 1) CollectPlainMeshes(child, pieces, includeHiddenOriginals); // Empty container (only Transform)
                else if (IsPlainOrMovable(childObject) && (includeHiddenOriginals || MeshCombineSettings.instance.HasOptIn(childObject)))
                    pieces.Add(childObject);
            }
        }

        private static bool IsPlainMeshComponent(Component component) =>
            component is Transform or MeshFilter or MeshRenderer or Collider;

        private static List<GameObject> CombineUnderParent(
            GameObject parent,
            GameObject[] children,
            bool recursive,
            int minGroupSize,
            string undoName,
            string meshOwnerPath
        )
        {
            Undo.IncrementCurrentGroup();
            var undoGroupIndex = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            Undo.RegisterCompleteObjectUndo(parent, undoName);
            foreach (var child in children)
            {
                Undo.RegisterCompleteObjectUndo(child, undoName);
            }

            var root = EnsureRoot(parent, recursive, minGroupSize);
            var combined = CombineMeshesAndColliders(parent, children, meshOwnerPath, GetMinGroupSize(root));
            StoreBakeHash(root);

            Undo.CollapseUndoOperations(undoGroupIndex);
            CombinedMeshUndo.Track(undoGroupIndex, parent, meshOwnerPath, combined);
            return combined;
        }

        /// Combine the objects into a new "Combined Mesh" parent placed under <paramref name="container"/>. Without a container it goes to the
        /// scene root of the first object, or under the prefab root when the objects are inside a prefab (a new root would not be saved).
        public static List<GameObject> CombineIntoNewParent(GameObject[] objects, Transform container = null, string meshOwnerPath = null)
        {
            const string undoName = "Combine selected meshes (New Parent)";
            if (objects.Length == 0) return new List<GameObject>();

            Undo.IncrementCurrentGroup();
            var undoGroupIndex = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            var parentGameObject = new GameObject(CombinedMeshPrefix)
            {
                isStatic = true, // Set parent as static by default - children may vary
            };

            var firstObject = objects[0];
            if (container == null && EditorSceneManager.IsPreviewScene(firstObject.scene)) container = firstObject.transform.root;
            if (container != null) parentGameObject.transform.SetParent(container, false);
            else SceneManager.MoveGameObjectToScene(parentGameObject, firstObject.scene);
            parentGameObject.transform.SetSiblingIndex(0); // First in its container / scene root
            Undo.RegisterCreatedObjectUndo(parentGameObject, undoName);

            foreach (var selectedObject in objects)
            {
                Undo.RegisterCompleteObjectUndo(selectedObject, undoName);
            }

            var combined = CombineMeshesAndColliders(parentGameObject, objects, meshOwnerPath,
                1); // The user picked these objects: no opt-in, no minimum

            Undo.CollapseUndoOperations(undoGroupIndex);
            CombinedMeshUndo.Track(undoGroupIndex, parentGameObject, meshOwnerPath, combined);
            return combined;
        }

        /// A "Combined Mesh - ..." object created by this tool.
        public static bool IsCombinedMesh(MeshFilter meshFilter) => meshFilter.name.StartsWith(CombinedMeshPrefix);

        /// The mesh is missing or only in memory (stored in a scene at best, dropped when a prefab is saved).
        public static bool HasUnsavedMesh(MeshFilter meshFilter) =>
            !meshFilter.sharedMesh || !EditorUtility.IsPersistent(meshFilter.sharedMesh);

        /// Save the combined mesh as an asset: a copy of the in-memory mesh, or a missing mesh rebuilt from the EditorOnly originals next to it
        /// (siblings with the same material, static flag and layer). Returns null when there is nothing to rebuild from.
        public static Mesh SaveOrRebuildCombinedMesh(MeshFilter combined, string meshOwnerPath = null)
        {
            var mesh = combined.sharedMesh ? Object.Instantiate(combined.sharedMesh) : RebuildFromOriginals(combined);
            if (!mesh) return null;

            if (NeedsLightmapUVs(GameObjectUtility.GetStaticEditorFlags(combined.gameObject))
                && !mesh.HasVertexAttribute(VertexAttribute.TexCoord1)) GenerateLightmapUVs(mesh);

            mesh.name = combined.name;
            SaveMeshAsset(mesh, combined.gameObject, meshOwnerPath);
            Undo.RecordObject(combined, "Save combined mesh");
            combined.sharedMesh = mesh;
            return mesh;
        }

        /// Combine the EditorOnly originals next to the combined object into a new in-memory mesh (not saved). Null when there are none.
        public static Mesh RebuildFromOriginals(MeshFilter combined)
        {
            var material = combined.GetComponent<MeshRenderer>()?.sharedMaterial;
            var combinedObject = combined.gameObject;
            var toCombinedSpace = combined.transform.worldToLocalMatrix;
            var parts = combined.transform.parent
                ? combined.transform.parent.Cast<Transform>()
                    .Where(original => original != combined.transform && !original.gameObject.activeSelf && original.CompareTag(EditorOnlyTag)
                                       && GameObjectUtility.GetStaticEditorFlags(original.gameObject)
                                       == GameObjectUtility.GetStaticEditorFlags(combinedObject)
                                       && original.gameObject.layer == combinedObject.layer
                                       && GroupedComponentSignature(original.gameObject) == GroupedComponentSignature(combinedObject))
                    .Select(original => (meshFilter: original.GetComponent<MeshFilter>(), renderer: original.GetComponent<MeshRenderer>()))
                    .Where(x => x.meshFilter && x.meshFilter.sharedMesh && x.renderer)
                    .SelectMany(x => SubMeshParts(x.meshFilter, x.renderer))
                    .Where(x => x.material == material)
                    .Select(x => new CombineInstance
                    {
                        mesh = x.meshFilter.sharedMesh, subMeshIndex = x.subMeshIndex,
                        transform = toCombinedSpace * x.meshFilter.transform.localToWorldMatrix,
                    })
                    .ToArray()
                : Array.Empty<CombineInstance>();
            return parts.Length > 0
                ? CreateCombinedMesh(parts, combined.name, NeedsLightmapUVs(GameObjectUtility.GetStaticEditorFlags(combinedObject)))
                : null;
        }

        /// Lightmap UVs (UV2) are generated when the objects contribute GI, otherwise removed.
        private static Mesh CreateCombinedMesh(CombineInstance[] parts, string name, bool generateLightmapUVs)
        {
            var vertexCount = parts.Sum(c => (long) c.mesh.vertexCount);
            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertexCount > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.CombineMeshes(parts);
            if (generateLightmapUVs) GenerateLightmapUVs(mesh);
            else RemoveLightmapUVs(mesh); // Source meshes (FBX) may bring their own UV2
            return mesh;
        }

        /// Unique lightmap UVs (UV2) for a static combined mesh. Without them the lightmapper falls back to UV0, which on palette-textured
        /// meshes (PolygonOffice) packs the whole combined room into a few texels: flat, smeared baked light and bounce.
        public static void GenerateLightmapUVs(Mesh mesh)
        {
            if (!Unwrapping.GenerateSecondaryUVSet(mesh)) Debug.LogWarning($"Lightmap UV generation failed for '{mesh.name}'.");
        }

        /// Only objects that contribute to GI are lightmapped. Without the flag the UV2 channel is dead weight (8 bytes per vertex).
        public static bool NeedsLightmapUVs(StaticEditorFlags staticFlags) => (staticFlags & StaticEditorFlags.ContributeGI) != 0;

        /// Drop the lightmap UVs (UV2), e.g. from meshes of a scene that is not baked.
        public static void RemoveLightmapUVs(Mesh mesh) => mesh.uv2 = null;

        /// The settings of the whitelisted "grouped" components (navmesh modifier) of the object. Objects with different ones are combined apart.
        private static string GroupedComponentSignature(GameObject gameObject)
        {
            var signature = new StringBuilder();
            foreach (var type in MeshCombineSettings.instance.GroupedTypes)
            {
                var component = gameObject.GetComponent(type);
                if (!component) continue;

                signature.Append(type.Name).Append('{');
                var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = true)
                {
                    if (property.propertyPath == "m_Script") continue;

                    signature.Append(property.propertyPath).Append('=').Append(property.propertyType switch
                    {
                        SerializedPropertyType.Integer or SerializedPropertyType.LayerMask => property.intValue.ToString(),
                        SerializedPropertyType.Boolean => property.boolValue.ToString(),
                        SerializedPropertyType.Float => property.floatValue.ToString("R"),
                        SerializedPropertyType.String => property.stringValue,
                        SerializedPropertyType.Enum => property.enumValueIndex.ToString(),
                        SerializedPropertyType.ObjectReference => property.objectReferenceInstanceIDValue.ToString(),
                        _ => string.Empty, // Arrays and structs: their children follow
                    }).Append(';');
                }

                signature.Append('}');
            }

            return signature.ToString();
        }

        /// Short name of the grouped components' settings for the combined object ("Not Walkable", or the component type), "" without any.
        private static string GroupedComponentLabel(GameObject gameObject)
        {
            foreach (var type in MeshCombineSettings.instance.GroupedTypes)
            {
                var component = gameObject.GetComponent(type);
                if (!component) continue;

                var serialized = new SerializedObject(component);
                var overrideArea = serialized.FindProperty("m_OverrideArea");
                var area = serialized.FindProperty("m_Area");
                if (overrideArea is not { boolValue: true } || area == null) return type.Name;

                var areaName = NavMesh.GetAreaNames().FirstOrDefault(name => NavMesh.GetAreaFromName(name) == area.intValue);
                return string.IsNullOrEmpty(areaName) ? $"Area{area.intValue}" : areaName.Replace(" ", string.Empty);
            }

            return string.Empty;
        }

        private static List<GameObject> CombineMeshesAndColliders(
            GameObject parent,
            GameObject[] children,
            string meshOwnerPath,
            int minGroupSize,
            RebakeContext rebake = null
        )
        {
            var parts = children
                .Select(child => child.GetComponent<MeshFilter>())
                .Where(mf => mf != null && mf.sharedMesh != null && mf.TryGetComponent<MeshRenderer>(out _)) // Skip non-mesh objects
                .SelectMany(mf => SubMeshParts(mf, mf.GetComponent<MeshRenderer>()))
                .ToList();

            // Group by material, static flags (e.g. glass without Occluder Static stays apart), layer, and the navmesh modifier settings
            var groupKeys = parts.Select(x => x.meshFilter.gameObject).Distinct()
                .ToDictionary(go => go, go => (GameObjectUtility.GetStaticEditorFlags(go), go.layer, GroupedComponentSignature(go)));
            var materialGroups = DropSmallGroups(parts, groupKeys, minGroupSize)
                .GroupBy(x => (x.material, groupKeys[x.meshFilter.gameObject].Item1, groupKeys[x.meshFilter.gameObject].layer,
                    groupKeys[x.meshFilter.gameObject].Item3));

            var combined = materialGroups
                .Select(materialGroup => CombineMeshAndCollidersForOneMaterial(parent, materialGroup, meshOwnerPath, rebake))
                .Where(groupParent => groupParent)
                .ToList();

            // The generated objects first, in creation order, above the originals
            for (var i = 0; i < combined.Count; i++)
            {
                combined[i].transform.SetSiblingIndex(i);
            }

            return combined;
        }

        /// Groups with fewer objects than the minimum stay uncombined. An object leaves with all its parts (a monitor with a body and a screen
        /// must not be half combined), which can shrink other groups, so this repeats until nothing changes.
        private static List<(MeshFilter meshFilter, MeshRenderer renderer, int subMeshIndex, Material material)> DropSmallGroups(
            List<(MeshFilter meshFilter, MeshRenderer renderer, int subMeshIndex, Material material)> parts,
            Dictionary<GameObject, (StaticEditorFlags, int layer, string)> groupKeys,
            int minGroupSize
        )
        {
            while (minGroupSize > 1)
            {
                var small = parts
                    .GroupBy(x => (x.material, groupKeys[x.meshFilter.gameObject]))
                    .Where(group => group.Select(x => x.meshFilter).Distinct().Count() < minGroupSize)
                    .SelectMany(group => group.Select(x => x.meshFilter))
                    .ToHashSet();
                if (small.Count == 0) break;

                parts = parts.Where(x => !small.Contains(x.meshFilter)).ToList();
            }

            return parts;
        }

        /// One part per rendered submesh with its material slot (a monitor gives its body and its screen), so a multi-material mesh keeps
        /// every submesh. Submeshes without a material and extra material slots (drawn again over the last submesh) are left out.
        private static IEnumerable<(MeshFilter meshFilter, MeshRenderer renderer, int subMeshIndex, Material material)> SubMeshParts(
            MeshFilter meshFilter,
            MeshRenderer renderer
        )
        {
            var materials = renderer.sharedMaterials;
            var count = Mathf.Min(meshFilter.sharedMesh.subMeshCount, materials.Length);
            for (var i = 0; i < count; i++)
            {
                if (materials[i]) yield return (meshFilter, renderer, i, materials[i]);
            }
        }

        /// Combine meshes that share the same material into a single mesh. Destroy original meshes if specified or just disable.
        private static GameObject CombineMeshAndCollidersForOneMaterial(
            GameObject parent,
            IGrouping<(Material material, StaticEditorFlags staticFlags, int layer, string grouped),
                    (MeshFilter meshFilter, MeshRenderer renderer, int subMeshIndex, Material material)>
                materialGroup,
            string meshOwnerPath,
            RebakeContext rebake,
            bool destroyOriginals = false
        )
        {
            var combineGroup = materialGroup.ToArray();
            var groupLength = combineGroup.Length;
            if (groupLength <= 0) return null;

            var combineMeshes = new CombineInstance[groupLength];

            var staticFlags = materialGroup.Key.staticFlags;
            var shouldBeStatic = staticFlags != 0;
            var objectLayer = materialGroup.Key.layer;
            var groupLabel = GroupedComponentLabel(combineGroup[0].meshFilter.gameObject);
            var containerName = string.IsNullOrEmpty(groupLabel)
                ? ColliderCopyUtility.CollidersContainerName
                : $"{ColliderCopyUtility.CollidersContainerName} ({groupLabel})";
            var groupParent = new GameObject { layer = objectLayer };
            GameObjectUtility.SetStaticEditorFlags(groupParent, staticFlags); // The originals' flags, not all of them (isStatic)
            groupParent.transform.SetParent(parent.transform);
            Undo.RegisterCreatedObjectUndo(groupParent, "Combine Meshes parent");

            for (var i = 0; i < groupLength; i++)
            {
                var (meshFilter, _, subMeshIndex, _) = combineGroup[i];

                combineMeshes[i] = new CombineInstance
                {
                    mesh = meshFilter.sharedMesh,
                    subMeshIndex = subMeshIndex,
                    transform = meshFilter.transform.localToWorldMatrix,
                };

                // Copy colliders from original mesh objects to the new combined mesh object, once: with the first submesh
                // Not needed to disable colliders as we are deactivating the whole object
                if (subMeshIndex == 0)
                {
                    foreach (var collider in meshFilter.GetComponentsInChildren<Collider>())
                    {
                        collider.CopyColliderTo(groupParent, containerName);
                    }

                    // Whitelisted components with their own frame (navmesh obstacle) follow as a child at the object's pose
                    foreach (var type in MeshCombineSettings.instance.CopiedTypes)
                    {
                        foreach (var component in meshFilter.GetComponents(type))
                        {
                            ColliderCopyUtility.CopyComponentToChild(component, groupParent, containerName);
                        }
                    }
                }

                if (destroyOriginals)
                {
                    Undo.DestroyObjectImmediate(meshFilter.gameObject); // Destroy child mesh objects after combining
                    continue;
                }

                var originalGameObject = meshFilter.gameObject;
                Undo.RegisterCompleteObjectUndo(originalGameObject, "Deactivate original mesh");
                originalGameObject.SetActive(false); // Deactivate child mesh objects after combining
                originalGameObject.tag = EditorOnlyTag; // Kept as a building block in the editor, stripped from builds
            }

            var groupedMaterial = materialGroup.Key.material;
            groupParent.name =
                $"{CombinedMeshPrefix} - {groupedMaterial.name} - {(shouldBeStatic ? "Static" : "Dynamic")} - {LayerMask.LayerToName(objectLayer)}"
                + (string.IsNullOrEmpty(groupLabel) ? string.Empty : $" - {groupLabel}");

            // The combined mesh is what the navmesh surface (render meshes) sees: it gets the modifier of the group, and so does the collider container
            foreach (var type in MeshCombineSettings.instance.GroupedTypes)
            {
                var modifier = combineGroup[0].meshFilter.GetComponent(type);
                if (!modifier) continue;

                ColliderCopyUtility.CopyComponentTo(modifier, groupParent);
                var container = groupParent.transform.Find(containerName);
                if (container) ColliderCopyUtility.CopyComponentTo(modifier, container.gameObject, true);
            }

            var combinedMesh = CreateCombinedMesh(combineMeshes, groupParent.name, NeedsLightmapUVs(staticFlags));
            // Prefabs can't serialize in-memory meshes, the reference would be lost on save. A rebake writes into the old asset of the same name
            string reusePath = null;
            if (rebake != null && rebake.Reusable.TryGetValue(groupParent.name, out var reusable) && reusable.Count > 0)
            {
                reusePath = reusable.Dequeue();
                rebake.Reused.Add(reusePath);
            }

            combinedMesh = SaveMeshAsset(combinedMesh, parent, meshOwnerPath, reusePath);

            var meshRenderer = groupParent.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = groupedMaterial;

            var meshFilterParent = groupParent.AddComponent<MeshFilter>();
            meshFilterParent.sharedMesh = combinedMesh;

            Undo.RegisterCompleteObjectUndo(groupParent, "Activate combined mesh");
            groupParent.SetActive(true); // Activate parent mesh object
            ClearEditorOnlyTag(groupParent);

            Undo.RegisterCompleteObjectUndo(parent, "Ensure parent active");
            parent.SetActive(true); // Ensure parent is active
            ClearEditorOnlyTag(parent);

            EditorUtility.SetDirty(parent);
            return groupParent;
        }

        /// Save the mesh as an asset next to the prefab ("&lt;Prefab&gt;_CombinedMeshes" folder) or in the scene folder ("&lt;Scene&gt;/combined-meshes")
        /// owning the parent,
        /// or next to <paramref name="ownerPath"/> when given (prefab contents loaded outside Prefab Mode have no owner to resolve).
        /// Asset creation is not undoable - undoing the combine leaves the mesh asset orphaned.
        /// With <paramref name="reusePath"/> the geometry is copied into that existing mesh asset (same GUID, undoable) and it is returned.
        private static Mesh SaveMeshAsset(Mesh mesh, GameObject parent, string ownerPath = null, string reusePath = null)
        {
            var existing = string.IsNullOrEmpty(reusePath) ? null : AssetDatabase.LoadAssetAtPath<Mesh>(reusePath);
            if (existing)
            {
                MeshUtility.Optimize(mesh);
                MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
                Undo.RegisterCompleteObjectUndo(existing, "Rebake combined mesh");
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = Path.GetFileNameWithoutExtension(reusePath); // Main object name must match the file (e.g. "...Default2")
                Object.DestroyImmediate(mesh);
                MeshAssetOptimizationHeader.SetReadable(existing, false);
                AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }

            if (string.IsNullOrEmpty(ownerPath)) ownerPath = PrefabStageUtility.GetPrefabStage(parent)?.assetPath;
            if (string.IsNullOrEmpty(ownerPath) && PrefabUtility.IsPartOfPrefabInstance(parent))
                ownerPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(parent);
            if (string.IsNullOrEmpty(ownerPath)) ownerPath = parent.scene.path;

            var folder = FallbackMeshFolder;
            if (!string.IsNullOrEmpty(ownerPath) && ownerPath.StartsWith("Assets/"))
            {
                var ownerFolder = $"{Path.GetDirectoryName(ownerPath)!.Replace('\\', '/')}/{Path.GetFileNameWithoutExtension(ownerPath)}";
                // Scenes use their default data folder (lighting, navmesh...) named like the scene
                folder = ownerPath.EndsWith(".unity") ? $"{ownerFolder}/{SceneMeshSubfolder}" : $"{ownerFolder}_CombinedMeshes";
            }

            EnsureFolder(folder);
            var fileName = string.Concat(mesh.name.Split(Path.GetInvalidFileNameChars()));
            var path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{fileName}.asset");

            // Defaults: smaller build (compression) and no CPU copy at runtime (combined meshes are only rendered). Tweak in the Mesh inspector header
            MeshUtility.Optimize(mesh);
            MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
            AssetDatabase.CreateAsset(mesh, path);
            MeshAssetOptimizationHeader.SetReadable(mesh, false);
            AssetDatabase.SaveAssetIfDirty(mesh);
            return mesh;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;

            var parentFolder = Path.GetDirectoryName(folder)!.Replace('\\', '/');
            EnsureFolder(parentFolder);
            AssetDatabase.CreateFolder(parentFolder, Path.GetFileName(folder));
        }

        /// Clear the EditorOnly tag so the combined mesh is not stripped from builds together with its building blocks.
        private static void ClearEditorOnlyTag(GameObject gameObject)
        {
            if (!gameObject.CompareTag(EditorOnlyTag)) return;

            Undo.RecordObject(gameObject, "Clear EditorOnly Tag");
            gameObject.tag = UntaggedTag;
        }

        private static IEnumerable<GameObject> GetChildGameObjects(this GameObject parent, bool includeInactive = false)
        {
            var childCount = parent.transform.childCount;
            for (var i = 0; i < childCount; i++)
            {
                var childGameObject = parent.transform.GetChild(i).gameObject;
                if (includeInactive || childGameObject.activeSelf)
                {
                    yield return childGameObject;
                }
            }
        }
    }
}