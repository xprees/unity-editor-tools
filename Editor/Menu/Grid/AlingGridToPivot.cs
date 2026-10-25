using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xprees.EditorTools.Menu.Grid
{
    /// Aligns the Scene view grid (Unity 6 Grid and Snap) to the selected object's pivot.
    /// - Snapping: the default Move tool snaps relative to the pivot (GridSnapping.snapPosition/activeFunc hook, reflection).
    /// - Drawing: Unity draws its grid lines at world multiples of the grid size and ignores the grid pivot for X/Z,
    ///   so while aligned the built-in grid is hidden and the same grid is drawn here, so what designers see is what snaps.
    public static class AlignGridShortcut
    {
        private const string AlignMenu = "Tools/Grid/Align Grid Corner to Selected Pivot %#g"; // Ctrl + Shift + G
        private const string AlignYMenu = "Tools/Grid/Align Grid Y Plane to Selected Pivot";
        private const string ToggleMenu = "Tools/Grid/Aligned Grid Enabled &#g"; // Alt + Shift + G
        private const string ResetMenu = "Tools/Grid/Reset Grid Alignment (origin)";

        private const string PivotKey = "Xprees.AlignGrid.Pivot";
        private const string AlignedKey = "Xprees.AlignGrid.Aligned";

        private const int HalfCells = 40;
        private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color AxisLineColor = new Color(1f, 1f, 1f, 0.45f);

        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Assembly EditorAssembly = typeof(UnityEditor.Editor).Assembly;
        private static readonly Type SnappingType = EditorAssembly.GetType("UnityEditor.GridSnapping");
        private static readonly PropertyInfo ShowGlobalGridProp = typeof(SceneView).GetProperty("showGlobalGrid", All);
        private static readonly FieldInfo SnapPositionField = SnappingType?.GetField("snapPosition", All);
        private static readonly FieldInfo ActiveFuncField = SnappingType?.GetField("activeFunc", All);

        internal static bool Available => ShowGlobalGridProp != null && SnapPositionField != null && ActiveFuncField != null;

        private static Vector3 Pivot => SessionState.GetVector3(PivotKey, Vector3.zero);

        internal static bool Aligned => SessionState.GetBool(AlignedKey, false);

        internal static bool CanAlign => Available && Selection.activeTransform != null;

        [InitializeOnLoadMethod]
        private static void Restore()
        {
            SceneView.duringSceneGui -= DrawGrid;
            SceneView.duringSceneGui += DrawGrid;
            if (Available && Aligned) Apply(Pivot);
        }

        [MenuItem(AlignMenu)]
        internal static void AlignCorner() => Apply(Selection.activeTransform.position);

        [MenuItem(AlignMenu, true)]
        private static bool ValidateAlign() => CanAlign;

        [MenuItem(AlignYMenu)]
        internal static void AlignY()
        {
            var pivot = Pivot;
            pivot.y = Selection.activeTransform.position.y;
            Apply(pivot);
        }

        [MenuItem(AlignYMenu, true)]
        private static bool ValidateAlignY() => ValidateAlign();

        [MenuItem(ToggleMenu)]
        internal static void Toggle()
        {
            if (Aligned) Disable();
            else Apply(Pivot);
        }

        [MenuItem(ToggleMenu, true)]
        private static bool ValidateToggle()
        {
            UnityEditor.Menu.SetChecked(ToggleMenu, Aligned);
            return Available;
        }

        [MenuItem(ResetMenu)]
        internal static void Reset()
        {
            SessionState.SetVector3(PivotKey, Vector3.zero);
            Disable();
        }

        [MenuItem(ResetMenu, true)]
        private static bool ValidateReset() => Available;

        private static void Disable()
        {
            SessionState.SetBool(AlignedKey, false);
            SnapPositionField.SetValue(null, null);
            ActiveFuncField.SetValue(null, null);
            SceneView.RepaintAll();
        }

        private static void Apply(Vector3 pivot)
        {
            SessionState.SetVector3(PivotKey, pivot);
            SessionState.SetBool(AlignedKey, true);

            SnapPositionField.SetValue(null, (Func<Vector3, Vector3>) SnapToPivotGrid);
            ActiveFuncField.SetValue(null, (Func<bool>) (() => Aligned && EditorSnapSettings.gridSnapActive));
            SetBuiltInGridVisible(false);
            SceneView.RepaintAll();
        }

        private static void SetBuiltInGridVisible(bool visible)
        {
            foreach (SceneView view in SceneView.sceneViews) ShowGlobalGridProp.SetValue(view, visible);
        }

        private static void DrawGrid(SceneView view)
        {
            if (!Aligned || Event.current.type != EventType.Repaint) return;

            var pivot = Pivot;
            var step = EditorSnapSettings.gridSize;
            if (step.x <= 0f || step.z <= 0f) return;

            // Center the drawn patch on the grid cell nearest to the camera focus point.
            var center = SnapToPivotGrid(view.pivot);
            var minX = center.x - HalfCells * step.x;
            var maxX = center.x + HalfCells * step.x;
            var minZ = center.z - HalfCells * step.z;
            var maxZ = center.z + HalfCells * step.z;

            var previousZTest = Handles.zTest;
            Handles.zTest = CompareFunction.LessEqual;

            for (var i = -HalfCells; i <= HalfCells; i++)
            {
                var x = center.x + i * step.x;
                Handles.color = Mathf.Approximately(x, pivot.x) ? AxisLineColor : LineColor;
                Handles.DrawLine(new Vector3(x, pivot.y, minZ), new Vector3(x, pivot.y, maxZ));

                var z = center.z + i * step.z;
                Handles.color = Mathf.Approximately(z, pivot.z) ? AxisLineColor : LineColor;
                Handles.DrawLine(new Vector3(minX, pivot.y, z), new Vector3(maxX, pivot.y, z));
            }

            Handles.zTest = previousZTest;
        }

        private static Vector3 SnapToPivotGrid(Vector3 position)
        {
            var pivot = Pivot;
            var step = EditorSnapSettings.gridSize;
            var local = position - pivot;
            return pivot + new Vector3(Snap(local.x, step.x), Snap(local.y, step.y), Snap(local.z, step.z));
        }

        private static float Snap(float value, float step) => step <= 0f ? value : Mathf.Round(value / step) * step;
    }
}