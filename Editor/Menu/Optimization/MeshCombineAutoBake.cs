using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Xprees.EditorTools.MeshCombine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Keeps the combined meshes in step with their sources: bakes the combine roots when a scene or prefab is saved (stale ones when the setting is on,
    /// roots in "Edit originals" mode always, so the file on disk is baked), and before a build (opt-in, fails the build on an error).
    [InitializeOnLoad]
    internal static class MeshCombineAutoBake
    {
        private const BindingFlags Internal = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly PropertyInfo AutoSaveProperty = typeof(PrefabStage).GetProperty("autoSave", Internal);
        private static readonly PropertyInfo AutoSaveDisabledProperty = typeof(PrefabStage).GetProperty("temporarilyDisableAutoSave", Internal);
        private static readonly FieldInfo AutoSaveDisabledField = typeof(PrefabStage).GetField("m_TemporarilyDisableAutoSave", Internal);
        private static PrefabStage pausedStage;
        private static bool editingInStage;
        private static double nextCheck;

        static MeshCombineAutoBake()
        {
            EditorSceneManager.sceneSaving += (scene, _) => BakeRoots(scene.GetRootGameObjects());
            PrefabStage.prefabSaving += prefabRoot =>
            {
                // Auto Save of Prefab Mode saves after every change: baking then would end the Edit originals mode right away and rebake on every move.
                // Such a save is safe (a root in edit mode ships its originals, only unbaked). While a root is in edit mode Auto Save is paused
                // (KeepAutoSavePaused), so the saves that still happen are manual ones, and those bake
                if (!IsPrefabStageAutoSave()) BakeRoots(new[] { prefabRoot });
            };
            EditorApplication.update += KeepAutoSavePaused;
        }

        /// A save of the open prefab stage that Auto Save does by itself: Auto Save is on and not paused (internal properties: false when gone).
        private static bool IsPrefabStageAutoSave()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null && AutoSaveProperty?.GetValue(stage) is true && AutoSaveDisabledProperty?.GetValue(stage) is not true;
        }

        /// While a root of the open prefab is in Edit originals mode Auto Save of that prefab stage is paused with Unity's own temporary-disable flag
        /// (not persisted, a closed stage forgets it). The prefab then shows unsaved changes: Save bakes, and leaving asks Save/Discard.
        /// The flag is released again when no root is editing.
        internal static void RefreshAutoSavePause()
        {
            nextCheck = 0;
            KeepAutoSavePaused();
        }

        private static void KeepAutoSavePaused()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
            {
                pausedStage = null;
                return;
            }

            if (EditorApplication.timeSinceStartup >= nextCheck)
            {
                nextCheck = EditorApplication.timeSinceStartup + 0.5;
                editingInStage = stage.prefabContentsRoot && stage.prefabContentsRoot.GetComponentsInChildren<MeshCombineRoot>(true)
                    .Any(root => root.editingOriginals);
            }

            if (editingInStage)
            {
                AutoSaveDisabledField?.SetValue(stage, true); // Unity may reset it, so it is set again every update
                pausedStage = stage;
            }
            else if (ReferenceEquals(pausedStage, stage))
            {
                AutoSaveDisabledField?.SetValue(stage, false);
                pausedStage = null;
            }
        }

        [MenuItem("Tools/Optimization/Rebake all stale combined meshes", priority = 6)]
        public static void RebakeAllStaleFromMenu()
        {
            try
            {
                Debug.Log($"Rebaked {RebakeAllStale()} stale combine root(s) in prefabs and scenes.");
            }
            catch (Exception e) when (e is InvalidOperationException or BuildFailedException)
            {
                Debug.LogError(e.Message);
            }
        }

        /// Bake the roots of the saved objects. Never blocks the save: errors are logged.
        private static void BakeRoots(IEnumerable<GameObject> roots)
        {
            foreach (var root in roots.SelectMany(go => go.GetComponentsInChildren<MeshCombineRoot>(true)))
            {
                if (PrefabUtility.IsPartOfPrefabInstance(root.gameObject)) continue; // Baked in the prefab

                var state = MeshCombineState.ComputeState(root);
                if (state != CombineState.Editing && !(state == CombineState.Stale && MeshCombineSettings.instance.autoRebakeOnSave)) continue;

                try
                {
                    MeshCombineTool.Rebake(root.gameObject);
                }
                catch (InvalidOperationException e)
                {
                    Debug.LogError($"Mesh Combine: '{root.name}' not baked on save. {e.Message}", root);
                }
            }
        }

        /// Rebake the stale (or editing) roots of all prefabs and scenes and save them. Returns the number of roots baked.
        /// Throws when a root cannot be baked, or when an open scene has unsaved changes (it would be saved too).
        public static int RebakeAllStale()
        {
            if (Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(scene => scene.isDirty))
                throw new InvalidOperationException("Save the open scenes first: rebaking all stale combined meshes saves scenes and prefabs.");

            var scriptGuid = AssetDatabase.FindAssets("t:MonoScript MeshCombineRoot").FirstOrDefault(guid =>
                AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid))?.GetClass() == typeof(MeshCombineRoot));
            if (scriptGuid == null) return 0;

            var baked = 0;
            var paths = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { "Assets" }).Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => File.ReadAllText(path).Contains(scriptGuid)).OrderBy(path => path.EndsWith(".prefab") ? 0 : 1).ToList();
            foreach (var path in paths)
            {
                baked += path.EndsWith(".prefab") ? RebakePrefab(path) : RebakeScene(path);
            }

            AssetDatabase.SaveAssets();
            CombinedMeshCleanup.FlushPending();
            return baked;
        }

        private static int RebakePrefab(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var count = BakeStale(root.GetComponentsInChildren<MeshCombineRoot>(true), path);
                if (count > 0) PrefabUtility.SaveAsPrefabAsset(root, path);
                return count;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int RebakeScene(string path)
        {
            var wasOpen = Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Any(scene => scene.path == path);
            var scene = wasOpen ? SceneManager.GetSceneByPath(path) : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var count = BakeStale(scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MeshCombineRoot>(true)), path);
                if (count > 0) EditorSceneManager.SaveScene(scene);
                return count;
            }
            finally
            {
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static int BakeStale(IEnumerable<MeshCombineRoot> roots, string ownerPath)
        {
            var count = 0;
            foreach (var root in roots.Where(root => !PrefabUtility.IsPartOfPrefabInstance(root.gameObject)))
            {
                if (MeshCombineState.ComputeState(root) is not (CombineState.Stale or CombineState.Editing)) continue;

                try
                {
                    MeshCombineTool.Rebake(root.gameObject, null, ownerPath.EndsWith(".prefab") ? ownerPath : null);
                }
                catch (InvalidOperationException e)
                {
                    throw new BuildFailedException($"Mesh Combine: '{root.name}' in {ownerPath} could not be rebaked. {e.Message}");
                }

                count++;
            }

            return count;
        }
    }

    /// Opt-in (Project Settings > Xprees > Mesh Combine): rebake the stale combine roots before every build.
    internal class MeshCombineBuildPreprocess : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (!MeshCombineSettings.instance.rebakeOnBuild) return;

            try
            {
                var count = MeshCombineAutoBake.RebakeAllStale();
                if (count > 0) Debug.Log($"Mesh Combine: rebaked {count} stale combine root(s) before the build.");
            }
            catch (InvalidOperationException e)
            {
                throw new BuildFailedException($"Mesh Combine: {e.Message}");
            }
        }
    }
}