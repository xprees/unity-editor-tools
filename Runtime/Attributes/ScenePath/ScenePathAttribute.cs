using UnityEngine;

namespace Xprees.EditorTools.Attributes.ScenePath
{
    /// Draws a string field holding a scene asset path ("Assets/.../Level.unity") as a SceneAsset object field.
    /// The stored value stays a plain path string, so it works at runtime without editor references.
    public class ScenePathAttribute : PropertyAttribute
    {
    }
}
