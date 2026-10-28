using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Finds and deletes combined mesh assets nothing references any more (left over from rebakes), and deletes the ones a rebake replaced once
    /// the scene/prefab that referenced them is saved. A mesh is used when any project text asset (prefab, scene, asset, material) references its GUID.
    [InitializeOnLoad]
    public static class CombinedMeshCleanup
    {
        private const string PendingKey = "Xprees.CombinedMeshCleanup.Pending";
        private readonly static Regex GuidRegex = new("guid: ([0-9a-f]{32})", RegexOptions.Compiled);
        private readonly static string[] ReferencingExtensions = { ".prefab", ".unity", ".asset", ".mat" };

        static CombinedMeshCleanup()
        {
            EditorSceneManager.sceneSaved += _ => EditorApplication.delayCall += () => FlushPending();
            PrefabStage.prefabSaved += _ => EditorApplication.delayCall += () => FlushPending();
        }

        [MenuItem("Tools/Optimization/Delete unused combined meshes", priority = 4)]
        public static void DeleteUnusedFromMenu()
        {
            if (HasUnsavedOpenAssets())
            {
                EditorUtility.DisplayDialog("Unused combined meshes",
                    "Save or close the open scenes and prefabs first: a new mesh may be referenced only in memory.",
                    "OK");
                return;
            }

            var unused = FindUnusedMeshes();
            if (unused.Count == 0)
            {
                Debug.Log("No unused combined meshes.");
                return;
            }

            Debug.Log($"Unused combined meshes ({unused.Count}):\n{string.Join("\n", unused)}");
            if (!EditorUtility.DisplayDialog("Delete unused combined meshes",
                    $"Delete {unused.Count} combined mesh asset(s) that no prefab, scene or asset references? The list is in the Console. Deleting is not undoable.",
                    "Delete", "Cancel")) return;

            Debug.Log($"Deleted {Delete(unused)} unused combined mesh asset(s).");
        }

        /// Mesh asset in a folder this tool saves combined meshes to (never an arbitrary mesh).
        public static bool IsCombinedMeshPath(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith("Assets/") && path.EndsWith(".asset")
            && (path.Contains("_CombinedMeshes/") || path.Contains($"/{MeshCombineTool.SceneMeshSubfolder}/")
                                                  || path.StartsWith($"{MeshCombineTool.FallbackMeshFolder}/"));

        /// Combined mesh assets no project file references. Optionally limited to the candidate paths.
        public static List<string> FindUnusedMeshes(IEnumerable<string> candidates = null)
        {
            var paths = (candidates ?? AssetDatabase.FindAssets("t:Mesh").Select(AssetDatabase.GUIDToAssetPath))
                .Where(IsCombinedMeshPath).Distinct().ToList();
            if (paths.Count == 0) return paths;

            var referenced = CollectReferencedGuids();
            return paths.Where(path => !referenced.Contains(AssetDatabase.AssetPathToGUID(path))).ToList();
        }

        public static int Delete(IEnumerable<string> paths) => paths.Count(AssetDatabase.DeleteAsset);

        /// A scene or prefab being edited may reference a mesh only in memory (not on disk yet), so nothing may be deleted meanwhile.
        public static bool HasUnsavedOpenAssets()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty) return true;
            }

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage != null && stage.scene.isDirty;
        }

        /// Mesh assets a rebake replaced: deleted once nothing references them (the saved file still does until it is saved).
        public static void QueueLeftovers(IEnumerable<string> paths)
        {
            var pending = GetPending();
            pending.AddRange(paths.Where(IsCombinedMeshPath));
            SetPending(pending.Distinct().ToList());
        }

        /// Delete the queued leftovers nothing references. Called after a scene/prefab is saved, and after a headless (MCP) save.
        public static int FlushPending()
        {
            var pending = GetPending();
            if (pending.Count == 0 || HasUnsavedOpenAssets()) return 0;

            var unused = FindUnusedMeshes(pending);
            var deleted = Delete(unused);
            SetPending(pending.Where(path => !unused.Contains(path) && File.Exists(path)).ToList()); // Still referenced: wait for the next save
            return deleted;
        }

        private static HashSet<string> CollectReferencedGuids()
        {
            var guids = new HashSet<string>();
            foreach (var file in Directory.EnumerateFiles("Assets", "*.*", SearchOption.AllDirectories))
            {
                if (!ReferencingExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase) || !IsTextYaml(file)) continue;

                foreach (Match match in GuidRegex.Matches(File.ReadAllText(file)))
                {
                    guids.Add(match.Groups[1].Value);
                }
            }

            return guids;
        }

        private static bool IsTextYaml(string file)
        {
            using var stream = File.OpenRead(file);
            var header = new byte[5];
            return stream.Read(header, 0, 5) == 5 && header[0] == '%' && header[1] == 'Y' && header[2] == 'A' && header[3] == 'M'
                   && header[4] == 'L';
        }

        private static List<string> GetPending() =>
            SessionState.GetString(PendingKey, "").Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();

        private static void SetPending(List<string> paths) => SessionState.SetString(PendingKey, string.Join("|", paths));
    }
}