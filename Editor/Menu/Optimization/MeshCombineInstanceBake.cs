using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Xprees.EditorTools.MeshCombine;
using Object = UnityEngine.Object;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Bakes a combine root that sits inside a prefab instance (a room in a scene, or a prefab nested in another prefab) without opening the prefab:
    /// the overrides of the instance under the root are applied to the source prefab, the source asset is rebaked in the background and saved,
    /// and the instance updates by itself.
    internal static class MeshCombineInstanceBake
    {
        /// The prefab asset the root belongs to, or null with the reason why it cannot be baked in the background.
        public static string GetSourcePath(MeshCombineRoot root, out string error)
        {
            error = null;
            var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(root.gameObject);
            if (!nearest)
            {
                error = "The root is not part of a prefab instance.";
                return null;
            }

            if (PrefabUtility.GetPrefabAssetType(nearest) == PrefabAssetType.Variant)
            {
                error = "Prefab variants are not supported, bake in the base prefab.";
                return null;
            }

            var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.gameObject);
            var original = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromOriginalSource(root));
            if (string.IsNullOrEmpty(path) || path != original || !path.StartsWith("Assets/"))
            {
                error =
                    $"The root's prefab ('{original}') is not the nearest prefab instance's asset ('{path}') or not in Assets. Bake it in the prefab.";
                return null;
            }

            if (PrefabStageUtility.GetCurrentPrefabStage()?.assetPath == path)
            {
                error = $"'{path}' is open in Prefab Mode. Bake there (Rebake) or close it.";
                return null;
            }

            return path;
        }

        /// The overrides of the instance under the root (property changes, added and removed components and objects).
        public static List<Action<string>> GetOverrideApplies(MeshCombineRoot root)
        {
            var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(root.gameObject);

            bool Under(Object target)
            {
                var transform = target switch
                {
                    Component component => component.transform, GameObject gameObject => gameObject.transform, _ => null
                };
                return transform && (transform == root.transform || transform.IsChildOf(root.transform));
            }

            var applies = new List<Action<string>>();
            applies.AddRange(PrefabUtility.GetObjectOverrides(nearest).Where(o => Under(o.instanceObject))
                .Select(o => (Action<string>) (path => o.Apply(path, InteractionMode.UserAction))));
            applies.AddRange(PrefabUtility.GetAddedComponents(nearest).Where(o => Under(o.instanceComponent))
                .Select(o => (Action<string>) (path => o.Apply(path, InteractionMode.UserAction))));
            applies.AddRange(PrefabUtility.GetRemovedComponents(nearest).Where(o => Under(o.containingInstanceGameObject))
                .Select(o => (Action<string>) (path => o.Apply(path, InteractionMode.UserAction))));
            applies.AddRange(PrefabUtility.GetAddedGameObjects(nearest).Where(o => Under(o.instanceGameObject))
                .Select(o => (Action<string>) (path => o.Apply(path, InteractionMode.UserAction))));
            applies.AddRange(PrefabUtility.GetRemovedGameObjects(nearest).Where(o => Under(o.parentOfRemovedGameObjectInInstance))
                .Select(o => (Action<string>) (path => o.Apply(path, InteractionMode.UserAction))));
            return applies;
        }

        /// Apply the overrides under the root to the source prefab, rebake it headless and save it. Returns the number of overrides applied.
        /// <paramref name="confirm"/> asks first (the bake itself is not undoable, the applied overrides are).
        public static int Rebake(MeshCombineRoot root, bool confirm = true)
        {
            var path = GetSourcePath(root, out var error);
            if (path == null) throw new InvalidOperationException(error);

            var rootPath = AnimationUtility.CalculateTransformPath(root.transform,
                PrefabUtility.GetNearestPrefabInstanceRoot(root.gameObject).transform);
            var applies = GetOverrideApplies(root);
            if (confirm && !EditorUtility.DisplayDialog("Rebake in prefab asset",
                    $"Apply {applies.Count} override(s) under '{root.name}' to '{path}' and rebake its combined meshes?\n\n" +
                    "The prefab asset changes for all its instances. The applied overrides can be undone, the bake itself cannot.",
                    "Apply and rebake", "Cancel")) return 0;

            Undo.IncrementCurrentGroup();
            var undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Apply overrides and rebake in prefab asset");
            foreach (var apply in applies)
            {
                apply(path);
            }

            Undo.CollapseUndoOperations(undoGroup);

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var target = string.IsNullOrEmpty(rootPath) ? contents.transform : contents.transform.Find(rootPath);
                if (!target || !target.TryGetComponent<MeshCombineRoot>(out var sourceRoot))
                    throw new InvalidOperationException($"The combine root '{rootPath}' was not found in '{path}'.");

                MeshCombineTool.Rebake(sourceRoot.gameObject, null, path);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            CombinedMeshCleanup.FlushPending();
            return applies.Count;
        }
    }
}