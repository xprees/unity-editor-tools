using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Xprees.EditorTools.Editor.Menu
{
    /// Set of tools for finding and removing missing scripts in Unity scenes, prefabs and ScriptableObject assets.
    public static class MissingScriptsTools
    {
        private static readonly List<Object> lastAffectedAssets = new();

        [MenuItem("Tools/Missing Scripts/Find in open scenes", priority = 1)]
        public static void FindMissingScriptsInOpenScenes() =>
            FindMissingScriptsInGameObjects(Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        [MenuItem("Tools/Missing Scripts/Find in selection", priority = 2)]
        public static void FindMissingScriptsInSelection() => FindMissingScriptsInGameObjects(Selection.gameObjects, true);

        [MenuItem("Tools/Missing Scripts/Find everywhere", priority = 3)]
        public static void FindMissingScriptsEverywhere()
        {
            var prefabs = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsNotPathFromPackages)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>);

            FindMissingScriptsInGameObjects(prefabs, true);
            FindMissingScriptsInAssets();

            if (Application.isPlaying || EditorApplication.isPlaying) return;

            var initialSceneSetup = EditorSceneManager.GetSceneManagerSetup();

            var scenes = AssetDatabase.FindAssets($"t:{nameof(Scene)}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsNotPathFromPackages)
                .Select(path => EditorSceneManager.OpenScene(path, OpenSceneMode.Additive));

            try
            {
                foreach (var scene in scenes)
                {
                    var rootGameObjects = scene.GetRootGameObjects();
                    FindMissingScriptsInGameObjects(rootGameObjects, true);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }

            EditorSceneManager.RestoreSceneManagerSetup(initialSceneSetup);
        }

        /// ScriptableObjects, graph nodes and other sub-assets have no GameObject, so the GameObject based search never sees them.
        /// A sub-object whose script is missing loads as null from <see cref="AssetDatabase.LoadAllAssetsAtPath"/>.
        [MenuItem("Tools/Missing Scripts/Find in assets (ScriptableObjects, graph nodes)", priority = 6)]
        public static void FindMissingScriptsInAssets() => ScanAssets(AssetDatabase.FindAssets("t:Object", new[] { "Assets" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(IsScannableAsset)
            .Distinct()
            .ToArray());

        /// Same scan limited to what is selected in the Project window (folders are searched recursively).
        [MenuItem("Tools/Missing Scripts/Find in selected project assets", priority = 8)]
        public static void FindMissingScriptsInSelectedAssets() => ScanAssets(GetSelectedAssetPaths());

        [MenuItem("Tools/Missing Scripts/Find in selected project assets", validate = true)]
        private static bool ValidateSelectedAssets() => Selection.assetGUIDs.Length > 0;

        /// Removes missing scripts from selected prefab assets only. Assets (ScriptableObjects, graph nodes) cannot be cleaned this way:
        /// restore the script or fix its GUID instead. Saved prefab changes are not covered by Undo.
        [MenuItem("Tools/Missing Scripts/Remove from selected prefab assets", priority = 9)]
        public static void RemoveMissingScriptsFromSelectedPrefabs()
        {
            var prefabPaths = GetSelectedAssetPaths().Where(path => path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (prefabPaths.Length == 0)
            {
                Debug.Log(
                    "No prefab assets in the selection. ScriptableObject/graph node assets cannot be cleaned automatically: restore the script or fix its GUID.");
                return;
            }

            if (!EditorUtility.DisplayDialog("Remove Missing Scripts",
                    $"Remove missing scripts from {prefabPaths.Length} prefab asset(s)? Saved prefab changes can't be undone (use version control).",
                    "Yes", "No")) return;

            var removed = 0;
            foreach (var path in prefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var count = root.GetComponentsInChildren<Transform>(true)
                        .Sum(t => GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject));
                    if (count == 0) continue;

                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    removed += count;
                    Debug.Log($"Removed {count} missing script(s) from {path}", AssetDatabase.LoadMainAssetAtPath(path));
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            Debug.Log($"Removed {removed} missing script(s) from {prefabPaths.Length} selected prefab(s).");
        }

        private static bool IsScannableAsset(string path) =>
            path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);

        private static string[] GetSelectedAssetPaths()
        {
            var paths = new List<string>();
            foreach (var guid in Selection.assetGUIDs)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetDatabase.IsValidFolder(path))
                {
                    paths.AddRange(AssetDatabase.FindAssets("t:Object", new[] { path }).Select(AssetDatabase.GUIDToAssetPath));
                    continue;
                }

                paths.Add(path);
            }

            return paths.Where(IsScannableAsset).Distinct().ToArray();
        }

        private static void ScanAssets(string[] paths)
        {
            lastAffectedAssets.Clear();
            try
            {
                for (var i = 0; i < paths.Length; i++)
                {
                    if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Missing scripts in assets", paths[i],
                            (float) i / paths.Length)) break;

                    var subAssets = AssetDatabase.LoadAllAssetsAtPath(paths[i]);
                    var missing = subAssets.Count(subAsset => subAsset == null);
                    if (missing == 0) continue;

                    // The main object is null when its own script is missing, so ping the first loadable sub-asset instead (click on the log line).
                    var pingTarget = AssetDatabase.LoadMainAssetAtPath(paths[i]) ?? subAssets.FirstOrDefault(subAsset => subAsset != null);
                    if (pingTarget) lastAffectedAssets.Add(pingTarget);
                    Debug.LogWarning($"Missing script on {missing} object(s) inside asset: {paths[i]}", pingTarget);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"Missing scripts in assets: {lastAffectedAssets.Count} asset(s) affected out of {paths.Length} scanned. " +
                      "Use Tools/Missing Scripts/Select assets from last scan to select them in the Project window.");
        }

        /// Selects every asset found by the last "Find in assets" scan in the Project window (a broken main object is represented by its first loadable sub-asset).
        [MenuItem("Tools/Missing Scripts/Select assets from last scan", priority = 7)]
        public static void SelectAssetsFromLastScan()
        {
            if (lastAffectedAssets.Count == 0)
            {
                Debug.Log("No affected assets stored. Run 'Find in assets' first.");
                return;
            }

            Selection.objects = lastAffectedAssets.ToArray();
            EditorUtility.FocusProjectWindow();
        }


        [MenuItem("Tools/Missing Scripts/Remove from selection", priority = 4)]
        public static void RemoveMissingScriptsFromSelection() =>
            RemoveMissingScriptsFromGameObjects(Selection.gameObjects);

        [MenuItem("Tools/Missing Scripts/Remove all!", priority = 5)]
        public static void RemoveMissingScripts() =>
            RemoveMissingScriptsFromGameObjects(Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None));

        private static void RemoveMissingScriptsFromGameObjects(GameObject[] gameObjects, bool searchInChildren = true)

        {
            if (!EditorUtility.DisplayDialog("Remove Missing Scripts",
                    $"Are you sure you want to remove all missing scripts from {gameObjects.Length} gameObjects?",
                    "Yes", "No"))
            {
                return;
            }

            foreach (var gameObject in gameObjects)
            {
                var components = searchInChildren ? gameObject.GetComponentsInChildren<Component>() : gameObject.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component != null) continue;

                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
                    if (!searchInChildren) break;

                    // Found missing script -> go through the gameObject and its children and remove all MonoBehaviours with missing scripts
                    for (var i = 0; i < gameObject.transform.childCount; i++)
                    {
                        var child = gameObject.transform.GetChild(i)?.gameObject;
                        if (child == null) continue;

                        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child);
                    }

                    break;
                }
            }
        }

        private static void FindMissingScriptsInGameObjects(IEnumerable<GameObject> gameObjects, bool searchInChildren = false)
        {
            foreach (var gameObject in gameObjects)
            {
                if (!gameObject) continue;

                var components = searchInChildren ? gameObject.GetComponentsInChildren<Component>() : gameObject.GetComponents<Component>();
                foreach (var component in components)
                {
                    if (component != null) continue;

                    var isHidden = gameObject.hideFlags.HasFlag(HideFlags.HideInHierarchy);
                    Debug.Log(
                        $"Missing script found on {(isHidden ? "<color=red>[Hidden]</color>" : "")} GameObject: {gameObject.name} from ({gameObject.scene.path}) at path: {AssetDatabase.GetAssetPath(gameObject)}",
                        gameObject
                    );
                }
            }
        }

        private static bool IsNotPathFromPackages(string path) => !path.StartsWith("Packages/");
    }
}