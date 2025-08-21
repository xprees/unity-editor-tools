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
    /// Set of tools for finding and removing missing scripts in Unity scenes and prefabs.
    public static class MissingScriptsTools
    {
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