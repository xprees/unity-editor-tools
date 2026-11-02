using UnityEngine;

namespace Xprees.EditorTools.MeshCombine
{
    /// Marks a parent whose meshes are combined by the Mesh Combine tool (Tools/Optimization) and keeps its settings. Editor data only:
    /// it has no runtime behaviour, the combined objects and their mesh assets are what the game uses.
    [DisallowMultipleComponent]
    public class MeshCombineRoot : MonoBehaviour
    {
        [Tooltip("Combine the plain mesh objects at any depth under this object, not only its direct children.")]
        public bool recursive;

        [Min(0)]
        [Tooltip(
            "A group of meshes (same material, static flags, layer, navmesh modifier) with fewer objects than this stays uncombined. 0 = project default (Project Settings > Xprees > Mesh Combine).")]
        public int minGroupSize;

        [HideInInspector] public string bakeHash;
    }
}