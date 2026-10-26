using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Makes the mesh assets created by a combine follow its Undo/Redo (asset creation itself is not undoable): undoing the combine moves the
    /// assets out of the project (Library/CombinedMeshUndo), redoing moves them back with the same GUID, so the redone objects find them again.
    /// Assets the saved scene/prefab file already references are kept, removing them would break the saved file.
    [InitializeOnLoad]
    internal static class CombinedMeshUndo
    {
        private const string SessionKey = "Xprees.CombinedMeshUndo";
        private const int MaxEntries = 500;
        private static readonly string StashFolder = Path.GetFullPath("Library/CombinedMeshUndo");

        [Serializable]
        private class Entry
        {
            public int undoGroup;
            public string referencingPath;
            public string assetPath;
            public string guid;
            public bool stashed;
        }

        [Serializable]
        private class State
        {
            public List<Entry> entries = new();
        }

        private static readonly State state;

        static CombinedMeshUndo()
        {
            // The undo history does not outlive the editor session: drop what a previous session stashed
            var json = SessionState.GetString(SessionKey, null);
            if (json == null && Directory.Exists(StashFolder)) Directory.Delete(StashFolder, true);

            state = (json != null ? JsonUtility.FromJson<State>(json) : null) ?? new State();
            Undo.undoRedoEvent += OnUndoRedo;
        }

        /// Remember the mesh assets of the combined objects created in the undo group.
        public static void Track(int undoGroup, GameObject parent, string meshOwnerPath, IEnumerable<GameObject> combined)
        {
            // The file the edited objects are saved to: open prefab, prefab loaded for editing (owner), or scene
            var referencingPath = PrefabStageUtility.GetPrefabStage(parent)?.assetPath;
            if (string.IsNullOrEmpty(referencingPath))
                referencingPath = string.IsNullOrEmpty(meshOwnerPath) ? parent.scene.path : meshOwnerPath;

            foreach (var mesh in combined.Select(go => go.GetComponent<MeshFilter>().sharedMesh))
            {
                var assetPath = AssetDatabase.GetAssetPath(mesh);
                if (string.IsNullOrEmpty(assetPath)) continue;

                state.entries.Add(new Entry
                {
                    undoGroup = undoGroup,
                    referencingPath = referencingPath,
                    assetPath = assetPath,
                    guid = AssetDatabase.AssetPathToGUID(assetPath),
                });
            }

            if (state.entries.Count > MaxEntries) state.entries.RemoveRange(0, state.entries.Count - MaxEntries);
            Save();
        }

        private static void OnUndoRedo(in UndoRedoInfo info)
        {
            var group = info.undoGroup;
            var entries = state.entries.Where(entry => entry.undoGroup == group).ToList();
            if (entries.Count == 0) return;

            var changed = false;
            foreach (var entry in entries)
            {
                changed |= info.isRedo ? Restore(entry) : Stash(entry);
            }

            if (!changed) return;

            AssetDatabase.Refresh();
            Save();
        }

        private static bool Stash(Entry entry)
        {
            if (entry.stashed || !File.Exists(entry.assetPath) || IsReferencedBySavedFile(entry)) return false;

            var folder = Path.Combine(StashFolder, entry.guid);
            Directory.CreateDirectory(folder);
            MoveWithMeta(entry.assetPath, Path.Combine(folder, Path.GetFileName(entry.assetPath)));
            DeleteFolderIfEmpty(Path.GetDirectoryName(entry.assetPath));
            entry.stashed = true;
            return true;
        }

        private static bool Restore(Entry entry)
        {
            var stashed = Path.Combine(StashFolder, entry.guid, Path.GetFileName(entry.assetPath));
            if (!entry.stashed || !File.Exists(stashed)) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(entry.assetPath)!);
            MoveWithMeta(stashed, entry.assetPath);
            Directory.Delete(Path.Combine(StashFolder, entry.guid), true);
            entry.stashed = false;
            return true;
        }

        private static bool IsReferencedBySavedFile(Entry entry) =>
            !string.IsNullOrEmpty(entry.referencingPath) && File.Exists(entry.referencingPath)
                                                         && File.ReadAllText(entry.referencingPath).Contains(entry.guid);

        private static void MoveWithMeta(string from, string to)
        {
            File.Move(from, to);
            File.Move(from + ".meta", to + ".meta");
        }

        /// Remove the combined meshes folder the combine created, so undo leaves nothing behind.
        private static void DeleteFolderIfEmpty(string folder)
        {
            if (string.IsNullOrEmpty(folder) || Directory.EnumerateFileSystemEntries(folder).Any()) return;

            Directory.Delete(folder);
            if (File.Exists(folder + ".meta")) File.Delete(folder + ".meta");
        }

        private static void Save() => SessionState.SetString(SessionKey, JsonUtility.ToJson(state));
    }
}