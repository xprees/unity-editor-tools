using UnityEditor;
using UnityEngine;

namespace Xprees.EditorTools.Editor.Extensions
{
    public static class PrefabExtensions
    {
        /// Creates a prefab variant at the path from the asset at the asset path and destroys the instances
        public static void CreatePrefabVariantFromAsset(string assetPath, string prefabVariantPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            var prefabVariant = prefab.CreatePrefabVariant(prefabVariantPath);
            Object.DestroyImmediate(prefabVariant); // Destroy the instance in Editor
        }

        /// Creates a prefab variant at the path and returns the instance
        /// <a href="https://discussions.unity.com/t/solved-creating-prefab-variant-with-script/712284/4">Reference</a>
        public static GameObject CreatePrefabVariant(this Object prefab, string prefabVariantPath)
        {
            var objSource = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            var prefabVariant = PrefabUtility.SaveAsPrefabAsset(objSource, prefabVariantPath);
            return prefabVariant;
        }
    }
}