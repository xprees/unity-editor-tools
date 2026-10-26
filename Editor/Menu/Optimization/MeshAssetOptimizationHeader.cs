using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Xprees.EditorTools.Editor.Menu.Optimization
{
    /// Import-like settings for meshes saved as .asset files (combined or generated meshes), which have no importer to set them in.
    /// Drawn in the header of the Mesh inspector: compression, Read/Write, vertex cache optimization and Mesh LODs. Works on multi-selection, undoable.
    [InitializeOnLoad]
    public static class MeshAssetOptimizationHeader
    {
        private const string ReadableProperty = "m_IsReadable";

        private static readonly GUIContent[] CompressionNames =
            { new("Off"), new("Low"), new("Medium"), new("High") };

        private static readonly int[] CompressionValues = { 0, 1, 2, 3 };

        private static readonly GUIContent CompressionLabel =
            new("Compression", "Quantizes the vertex data in the build: smaller download, unpacked on load (no runtime memory saving).");

        private static readonly GUIContent ReadableLabel =
            new("Read/Write",
                "Keeps a CPU copy of the mesh at runtime. Only needed when scripts read the mesh or a runtime MeshCollider uses it.");

        private static readonly GUIContent LodLimitLabel = new("Max LOD level", "Highest Mesh LOD level to generate (LOD0 is the full mesh).");

        private static readonly GUIContent DiscardOddLabel =
            new("Discard odd levels", "Keep every second level only: bigger steps, less memory.");

        private static int lodLimit = 4;
        private static bool discardOddLevels;

        static MeshAssetOptimizationHeader() => UnityEditor.Editor.finishedDefaultHeaderGUI += DrawHeader;

        public static void SetReadable(Mesh mesh, bool readable)
        {
            var serializedMesh = new SerializedObject(mesh);
            serializedMesh.FindProperty(ReadableProperty).boolValue = readable;
            serializedMesh.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void DrawHeader(UnityEditor.Editor editor)
        {
            if (editor.target is not Mesh) return;

            var meshes = editor.targets.OfType<Mesh>().Where(IsMeshAsset).ToArray();
            if (meshes.Length == 0 || meshes.Length != editor.targets.Length) return; // Model sub-meshes use the model importer

            EditorGUILayout.LabelField("Mesh optimization", EditorStyles.boldLabel);

            var damaged = meshes.Any(mesh => mesh.vertexCount == 0);
            if (damaged)
            {
                EditorGUILayout.HelpBox("A selected mesh has no vertex data (its compression was changed through the serialized flag only). "
                                        + "Reimport or restore the asset; the settings below are disabled.", MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(damaged))
            {
                DrawCompression(meshes);
                DrawReadable(editor.serializedObject);

                // MeshUtility.Optimize does not know Mesh LODs: it merges the LOD index ranges into LOD0 and breaks the mesh
                using (new EditorGUI.DisabledScope(meshes.Any(mesh => mesh.lodCount > 1)))
                {
                    if (GUILayout.Button(new GUIContent("Optimize vertex cache",
                            "Reorders vertices and triangles for faster GPU rendering. Not available on meshes with LODs: clear them first.")))
                    {
                        Modify(meshes, "Optimize mesh", MeshUtility.Optimize);
                    }
                }

                var withoutLightmapUVs = meshes.Count(mesh => !mesh.HasVertexAttribute(VertexAttribute.TexCoord1));
                if (GUILayout.Button(new GUIContent(
                        withoutLightmapUVs > 0 ? $"Generate lightmap UVs ({withoutLightmapUVs} missing)" : "Regenerate lightmap UVs",
                        "Unique UV2 for baked lighting. Without it the lightmapper uses UV0, which on palette-textured combined meshes "
                        + "packs the whole mesh into a few lightmap texels. Rebake the lighting afterwards.")))
                {
                    Modify(meshes, "Generate lightmap UVs", MeshCombineTool.GenerateLightmapUVs);
                }

                DrawMeshLods(meshes);
            }

            EditorGUILayout.Space();
        }

        // Only MeshUtility.SetMeshCompression really (de)quantizes the vertex data: writing the serialized m_MeshCompression flag
        // alone leaves the mesh without vertices (invisible in the scene).
        private static void DrawCompression(Mesh[] meshes)
        {
            var levels = meshes.Select(MeshUtility.GetMeshCompression).Distinct().ToArray();
            EditorGUI.showMixedValue = levels.Length > 1;
            EditorGUI.BeginChangeCheck();
            var level = EditorGUILayout.IntPopup(CompressionLabel, (int) levels[0], CompressionNames, CompressionValues);
            EditorGUI.showMixedValue = false;
            if (!EditorGUI.EndChangeCheck()) return;

            Modify(meshes, "Change mesh compression", mesh => MeshUtility.SetMeshCompression(mesh, (ModelImporterMeshCompression) level));
        }

        private static void DrawReadable(SerializedObject serializedMeshes)
        {
            serializedMeshes.Update();
            EditorGUILayout.PropertyField(serializedMeshes.FindProperty(ReadableProperty), ReadableLabel);
            serializedMeshes.ApplyModifiedProperties();
        }

        private static void DrawMeshLods(Mesh[] meshes)
        {
            var lodCounts = meshes.Select(mesh => mesh.lodCount).Distinct().ToArray();
            EditorGUILayout.LabelField("Mesh LODs", lodCounts.Length == 1 ? (lodCounts[0] - 1).ToString() : "—");

            lodLimit = Mathf.Max(1, EditorGUILayout.IntField(LodLimitLabel, lodLimit));
            discardOddLevels = EditorGUILayout.Toggle(DiscardOddLabel, discardOddLevels);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate LODs"))
                {
                    var flags = discardOddLevels ? MeshLodUtility.LodGenerationFlags.DiscardOddLevels : 0;
                    Modify(meshes, "Generate mesh LODs", mesh => MeshLodUtility.GenerateMeshLods(mesh, flags, lodLimit));
                }

                using (new EditorGUI.DisabledScope(meshes.All(mesh => mesh.lodCount <= 1)))
                {
                    if (GUILayout.Button("Clear LODs")) Modify(meshes, "Clear mesh LODs", mesh => mesh.lodCount = 1);
                }
            }

            EditorGUILayout.HelpBox(
                "The LOD level is picked from the renderer's size on screen. Large combined meshes (whole rooms) rarely switch, "
                + "LODs pay off on meshes of small props.", MessageType.None);
        }

        private static void Modify(Mesh[] meshes, string undoName, Action<Mesh> change)
        {
            Undo.RecordObjects(meshes.Cast<Object>().ToArray(), undoName);
            foreach (var mesh in meshes)
            {
                change(mesh);
                EditorUtility.SetDirty(mesh);
            }
        }

        private static bool IsMeshAsset(Mesh mesh) =>
            AssetDatabase.IsMainAsset(mesh) && AssetDatabase.GetAssetPath(mesh).EndsWith(".asset");
    }
}