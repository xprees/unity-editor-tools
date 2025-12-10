using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Set of Mesh optimization tools for combining meshes in Unity.
    public static class MeshCombineTool
    {
        [MenuItem("Tools/Optimization/Combine selected meshes (New Parent)", priority = 2)]
        public static void CombineSelectedMeshesCreateParent()
        {
            var selectedGameObjects = Selection.gameObjects;

            Undo.IncrementCurrentGroup();
            var undoGroupIndex = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Combine selected meshes (New Parent)");

            var parentGameObject = new GameObject("Combined Mesh")
            {
                isStatic = true, // Set parent as static by default - children may vary
            };
            Undo.RegisterCreatedObjectUndo(parentGameObject, "Combine selected meshes (New Parent)");

            foreach (var selectedObject in selectedGameObjects)
            {
                Undo.RegisterCompleteObjectUndo(selectedObject, "Combine selected meshes (New Parent)");
            }

            CombineMeshesAndColliders(parentGameObject, selectedGameObjects);

            Undo.CollapseUndoOperations(undoGroupIndex);
        }

        [MenuItem("Tools/Optimization/Combine child meshes of selected parent", priority = 1)]
        public static void CombineChildMeshesOfSelectedParent()
        {
            var parentGameObject = Selection.activeGameObject;
            if (parentGameObject == null)
            {
                Debug.LogError("No active GameObject selected.");
                return;
            }

            var childGameObject = parentGameObject.GetChildGameObjects().ToArray();

            Undo.IncrementCurrentGroup();
            var undoGroupIndex = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Combine child meshes of selected parent");

            Undo.RegisterCompleteObjectUndo(parentGameObject, "Combine child meshes of selected parent");
            foreach (var child in childGameObject)
            {
                Undo.RegisterCompleteObjectUndo(child, "Combine child meshes of selected parent");
            }

            CombineMeshesAndColliders(parentGameObject, childGameObject);

            Undo.CollapseUndoOperations(undoGroupIndex);
        }


        private static void CombineMeshesAndColliders(GameObject parent, GameObject[] children)
        {
            var meshFilters = children.Select(child => child.GetComponent<MeshFilter>()).ToArray();
            var materialGroups = meshFilters
                .Select(mf => (mf, mf.GetComponent<MeshRenderer>()))
                .GroupBy(x =>
                {
                    var (mf, renderer) = x;
                    var mfGameObject = mf.gameObject;
                    return (renderer.sharedMaterial, mfGameObject.isStatic, mfGameObject.layer); // Group by material, static flag, and layer
                });


            foreach (var materialGroup in materialGroups)
            {
                CombineMeshAndCollidersForOneMaterial(parent, materialGroup);
            }
        }

        /// Combine meshes that share the same material into a single mesh. Destroy original meshes if specified or just disable.
        private static void CombineMeshAndCollidersForOneMaterial(
            GameObject parent,
            IGrouping<(Material material, bool isStatic, int layer), (MeshFilter meshFilter, MeshRenderer renderer)> materialGroup,
            bool destroyOriginals = false
        )
        {
            var combineGroup = materialGroup.ToArray();
            var groupLength = combineGroup.Length;
            if (groupLength <= 0) return;

            var combineMeshes = new CombineInstance[groupLength];

            var shouldBeStatic = materialGroup.Key.isStatic;
            var objectLayer = materialGroup.Key.layer;
            var groupParent = new GameObject
            {
                isStatic = shouldBeStatic,
                layer = objectLayer,
            };
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
                var colliders = meshFilter.GetComponentsInChildren<Collider>();
                if (colliders.Length <= 0) continue;

                foreach (var collider in colliders)
                {
                    collider.CopyColliderTo(groupParent);
                    // Not needed to disable collider as we are deactivating the whole object
                }

                if (destroyOriginals)
                {
                    Undo.DestroyObjectImmediate(meshFilter.gameObject); // Destroy child mesh objects after combining
                    continue;
                }

                Undo.RegisterCompleteObjectUndo(meshFilter.gameObject, "Deactivate original mesh");
                meshFilter.gameObject.SetActive(false); // Deactivate child mesh objects after combining
            }

            var combinedMesh = new Mesh();
            combinedMesh.CombineMeshes(combineMeshes);

            var groupedMaterial = materialGroup.Key.material;
            groupParent.name =
                $"Combined Mesh - {groupedMaterial.name} - {(shouldBeStatic ? "Static" : "Dynamic")} - {LayerMask.LayerToName(objectLayer)}";

            var meshRenderer = groupParent.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = groupedMaterial;

            var meshFilterParent = groupParent.AddComponent<MeshFilter>();
            meshFilterParent.sharedMesh = combinedMesh;

            Undo.RegisterCompleteObjectUndo(groupParent, "Activate combined mesh");
            groupParent.SetActive(true); // Activate parent mesh object

            Undo.RegisterCompleteObjectUndo(parent, "Ensure parent active");
            parent.SetActive(true); // Ensure parent is active

            EditorUtility.SetDirty(parent);
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