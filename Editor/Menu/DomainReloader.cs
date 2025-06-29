using UnityEditor;

namespace Xprees.EditorTools.Editor.Menu
{

    public static class DomainReloader
    {
        /// This method is used to reload the Unity domain, which is useful when you want to apply changes made to scripts without restarting the editor.
        [MenuItem("Tools/Reload Domain")]
        public static void ReloadDomain()
        {
            EditorUtility.RequestScriptReload();
        }
    }
}