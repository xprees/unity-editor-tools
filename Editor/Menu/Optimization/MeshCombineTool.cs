using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
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
        public static List<GameObject> CombineChildren(GameObject parent, string meshOwnerPath = null) =>
            HasCombinedChildren(parent)
                ? Rebake(parent, false, meshOwnerPath)
                : CombineUnderParent(parent, parent.GetChildGameObjects().ToArray(), "Combine child meshes of selected parent", meshOwnerPath);

        /// Combine the plain mesh objects (only MeshFilter, MeshRenderer and colliders, no children) at any depth under the parent into
        /// "Combined Mesh - ..." children of the parent, like <see cref="CombineChildren"/>. Inactive objects, mesh objects with children and objects
        /// with other components (scripts, lights, LODs...) are skipped with their whole subtree.
        /// A parent that already has combined children is rebaked instead (see <see cref="Rebake"/>).
        public static List<GameObject> CombineDescendants(GameObject parent, string meshOwnerPath = null)
        {
            if (HasCombinedChildren(parent)) return Rebake(parent, true, meshOwnerPath);

            var pieces = new List<GameObject>();
            CollectPlainMeshes(parent.transform, pieces);
            return CombineUnderParent(parent, pieces.ToArray(), "Combine all meshes under selected parent", meshOwnerPath);
        }

        /// A direct child of the parent is a "Combined Mesh - ..." object created by this tool.
        public static bool HasCombinedChildren(GameObject parent) => GetCombinedChildren(parent).Count > 0;

        private static List<GameObject> GetCombinedChildren(GameObject parent) =>
            parent.transform.Cast<Transform>()
                .Where(child => child.name.StartsWith(CombinedMeshPrefix) && child.GetComponent<MeshFilter>())
                .Select(child => child.gameObject)
                .ToList();

        /// Combine the parent again from its originals (the inactive EditorOnly objects): the old combined objects are removed (the disabled ones merged in
        /// by older recursive combines too), the originals are enabled and combined, and the new geometry is written into the old mesh assets (same
        /// GUID, matched by object name). Mesh assets nothing uses any more are queued for deletion until the scene/prefab is saved.
        /// <paramref name="recursive"/> null detects it: recursive when an original sits deeper than a direct child.
        /// Throws when the parent is part of a prefab instance (rebake it in the prefab).
        public static List<GameObject> Rebake(GameObject parent, bool? recursive = null, string meshOwnerPath = null)
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
                if (old.activeSelf && CombinedMeshCleanup.IsCombinedMeshPath(path))
                {
                    if (!context.Reusable.TryGetValue(old.name, out var queue)) context.Reusable[old.name] = queue = new Queue<string>();
                    queue.Enqueue(path);
                }
                else leftovers.Add(path); // Disabled: merged in by an older recursive combine, its geometry is in the active ones

                Undo.DestroyObjectImmediate(old);
            }

            var originals = new List<GameObject>();
            if (recursive ?? HasHiddenOriginalsBelowChildren(parent)) CollectPlainMeshes(parent.transform, originals, true);
            else originals.AddRange(parent.GetChildGameObjects(true).Where(IsHiddenOriginal));
            recursive ??= originals.Any(original => original.transform.parent != parent.transform);

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
            else pieces = parent.GetChildGameObjects().ToArray();

            foreach (var piece in pieces)
            {
                Undo.RegisterCompleteObjectUndo(piece, undoName);
            }

            var combined = CombineMeshesAndColliders(parent, pieces, meshOwnerPath, context);

            // Originals that could not be combined stay disabled: active and EditorOnly would strip them from builds
            foreach (var original in originals.Where(original => original && original.activeSelf))
            {
                Debug.LogWarning($"'{original.name}' could not be combined (no mesh or renderer), it stays disabled.", original);
                original.SetActive(false);
                original.tag = EditorOnlyTag;
            }

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
                else if (child.childCount == 0 && components.All(IsPlainMeshComponent) && childObject.TryGetComponent<MeshFilter>(out _))
                    pieces.Add(childObject);
            }
        }

        private static bool IsPlainMeshComponent(Component component) =>
            component is Transform or MeshFilter or MeshRenderer or Collider;

        private static List<GameObject> CombineUnderParent(GameObject parent, GameObject[] children, string undoName, string meshOwnerPath)
        {
            Undo.IncrementCurrentGroup();
            var undoGroupIndex = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            Undo.RegisterCompleteObjectUndo(parent, undoName);
            foreach (var child in children)
            {
                Undo.RegisterCompleteObjectUndo(child, undoName);
            }

            var combined = CombineMeshesAndColliders(parent, children, meshOwnerPath);

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

            var combined = CombineMeshesAndColliders(parentGameObject, objects, meshOwnerPath);

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

            if (combined.gameObject.isStatic && !mesh.HasVertexAttribute(VertexAttribute.TexCoord1)) GenerateLightmapUVs(mesh);

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
                                       && original.gameObject.layer == combinedObject.layer)
                    .Select(original => (meshFilter: original.GetComponent<MeshFilter>(), renderer: original.GetComponent<MeshRenderer>()))
                    .Where(x => x.meshFilter && x.meshFilter.sharedMesh && x.renderer && x.renderer.sharedMaterial == material)
                    .Select(x => new CombineInstance
                        { mesh = x.meshFilter.sharedMesh, transform = toCombinedSpace * x.meshFilter.transform.localToWorldMatrix })
                    .ToArray()
                : Array.Empty<CombineInstance>();
            return parts.Length > 0 ? CreateCombinedMesh(parts, combined.name, combinedObject.isStatic) : null;
        }

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
            return mesh;
        }

        /// Unique lightmap UVs (UV2) for a static combined mesh. Without them the lightmapper falls back to UV0, which on palette-textured
        /// meshes (PolygonOffice) packs the whole combined room into a few texels: flat, smeared baked light and bounce.
        public static void GenerateLightmapUVs(Mesh mesh)
        {
            if (!Unwrapping.GenerateSecondaryUVSet(mesh)) Debug.LogWarning($"Lightmap UV generation failed for '{mesh.name}'.");
        }

        private static List<GameObject> CombineMeshesAndColliders(
            GameObject parent,
            GameObject[] children,
            string meshOwnerPath,
            RebakeContext rebake = null
        )
        {
            var meshFilters = children.Select(child => child.GetComponent<MeshFilter>()).ToArray();
            var materialGroups = meshFilters
                .Where(mf => mf != null && mf.sharedMesh != null && mf.TryGetComponent<MeshRenderer>(out _)) // Skip non-mesh objects
                .Select(mf => (mf, mf.GetComponent<MeshRenderer>()))
                .GroupBy(x =>
                {
                    var (mf, renderer) = x;
                    var mfGameObject = mf.gameObject;
                    // Group by material, static flags (e.g. glass without Occluder Static stays apart), and layer
                    return (renderer.sharedMaterial, GameObjectUtility.GetStaticEditorFlags(mfGameObject), mfGameObject.layer);
                });

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

        /// Combine meshes that share the same material into a single mesh. Destroy original meshes if specified or just disable.
        private static GameObject CombineMeshAndCollidersForOneMaterial(
            GameObject parent,
            IGrouping<(Material material, StaticEditorFlags staticFlags, int layer), (MeshFilter meshFilter, MeshRenderer renderer)>
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
            var groupParent = new GameObject { layer = objectLayer };
            GameObjectUtility.SetStaticEditorFlags(groupParent, staticFlags); // The originals' flags, not all of them (isStatic)
            groupParent.transform.SetParent(parent.transform);
            Undo.RegisterCreatedObjectUndo(groupParent, "Combine Meshes parent");

            for (var i = 0; i < groupLength; i++)
            {
                var (meshFilter, _) = combineGroup[i];

                combineMeshes[i] = new CombineInstance
                {
                    mesh = meshFilter.sharedMesh,
                    transform = meshFilter.transform.localToWorldMatrix,
                };

                // Copy colliders from original mesh objects to the new combined mesh object
                // Not needed to disable colliders as we are deactivating the whole object
                foreach (var collider in meshFilter.GetComponentsInChildren<Collider>())
                {
                    collider.CopyColliderTo(groupParent);
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
                $"{CombinedMeshPrefix} - {groupedMaterial.name} - {(shouldBeStatic ? "Static" : "Dynamic")} - {LayerMask.LayerToName(objectLayer)}";

            var combinedMesh = CreateCombinedMesh(combineMeshes, groupParent.name, shouldBeStatic);
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