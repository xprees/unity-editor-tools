using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;

namespace Xprees.EditorTools.Editor.Extensions
{
    public static class AssetDatabaseUtils
    {
        public static T[] FindAssets<T>(string filter = "", string searchIn = "Assets") where T : Object
        {
            var guids = AssetDatabase.FindAssets(filter, new[] { searchIn });
            var assets = new T[guids.Length];
            for (var i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                assets[i] = AssetDatabase.LoadAssetAtPath<T>(path);
            }

            return assets;
        }

        public static T[] FindAssetsOfType<T>(string searchIn = "Assets") where T : Object =>
            FindAssets<T>($"t:{typeof(T).Name}", searchIn);

        [CanBeNull]
        public static T FindFirstAssetOfType<T>(string searchIn = "Assets") where T : Object
        {
            var guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}", new[] { searchIn });
            if (guids.Length < 1) return null;

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<T>(path);
        }

    }
}