using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Xprees.EditorTools.Menu.Grid
{
    /// Scene view toolbar button: click toggles the pivot-aligned grid, the dropdown arrow holds the align actions
    /// (see <see cref="AlignGridShortcut" />).
    [Overlay(typeof(SceneView), "Aligned Grid")]
    public class AlignedGridOverlay : ToolbarOverlay
    {
        public AlignedGridOverlay() : base(AlignedGridDropdownToggle.Id)
        {
        }
    }

    [EditorToolbarElement(Id, typeof(SceneView))]
    internal class AlignedGridDropdownToggle : EditorToolbarDropdownToggle
    {
        public const string Id = "xprees-aligned-grid/dropdown-toggle";

        public AlignedGridDropdownToggle()
        {
            icon = EditorGUIUtility.FindTexture("Grid.Default");
            tooltip = "Aligned grid on/off - snap to a grid whose corner sits on the aligned pivot (Alt+Shift+G). Arrow: align actions.";
            SetValueWithoutNotify(AlignGridShortcut.Aligned);
            this.RegisterValueChangedCallback(_ => AlignGridShortcut.Toggle());
            dropdownClicked += ShowActions;

            // Keep in sync with the menu / shortcut and enable only when the internals are available.
            schedule.Execute(() =>
            {
                SetValueWithoutNotify(AlignGridShortcut.Aligned);
                SetEnabled(AlignGridShortcut.Available);
            }).Every(200);
        }

        private static void ShowActions()
        {
            var menu = new GenericMenu();
            AddAction(menu, "Align corner to selected pivot", AlignGridShortcut.CanAlign, AlignGridShortcut.AlignCorner);
            AddAction(menu, "Align Y plane to selected pivot", AlignGridShortcut.CanAlign, AlignGridShortcut.AlignY);
            menu.AddSeparator(string.Empty);
            AddAction(menu, "Reset to origin", true, AlignGridShortcut.Reset);
            menu.ShowAsContext();
        }

        private static void AddAction(GenericMenu menu, string label, bool enabled, GenericMenu.MenuFunction action)
        {
            if (enabled) menu.AddItem(new GUIContent(label), false, action);
            else menu.AddDisabledItem(new GUIContent(label));
        }
    }
}