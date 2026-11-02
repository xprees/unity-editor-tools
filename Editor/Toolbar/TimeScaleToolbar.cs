#if UNITY_6000_3_OR_NEWER
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Xprees.EditorTools.Editor.Toolbar
{
    /// Main toolbar buttons to speed up game time in Play Mode for testing.
    /// [Reset] returns to the time scale captured when Play Mode started, [Fast forward] doubles it on every click (max 32x).
    [InitializeOnLoad]
    public static class TimeScaleToolbar
    {
        private const string toolbarPath = "Play Mode/Time Scale";
        private const string forwardShortcutId = "Time Scale/Fast Forward";
        private const string resetShortcutId = "Time Scale/Reset";
        private const float maxTimeScale = 32f;

        private static float _baseline = 1f;
        private static float _shownTimeScale = -1f;

        static TimeScaleToolbar()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += RefreshIfTimeScaleChanged;
        }

        [MainToolbarElement(toolbarPath, defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static IEnumerable<MainToolbarElement> RenderTimeScaleTools()
        {
            var playing = EditorApplication.isPlaying;
            var scale = Time.timeScale;
            var paused = playing && scale <= 0f;
            _shownTimeScale = scale;

            var resetTooltip = !playing ? "Reset time scale (Play Mode only)"
                : paused ? "Game is paused (time scale 0)"
                : $"Reset time scale to {Format(_baseline)} (value at Play start)";
            var resetButton = new MainToolbarButton(
                new MainToolbarContent(Icon("PlayButton"), resetTooltip + ShortcutHint(resetShortcutId)),
                ResetSpeed)
            {
                enabled = CanReset,
            };

            var forwardTooltip = !playing ? "Fast forward: each click doubles the time scale (Play Mode only)"
                : paused ? "Game is paused (time scale 0)"
                : scale >= maxTimeScale ? $"Max speed {Format(maxTimeScale)} reached"
                : $"Fast forward to {Format(NextTimeScale(scale))} (each click doubles, max {Format(maxTimeScale)})";
            var forwardButton = new MainToolbarButton(
                new MainToolbarContent(Format(playing ? scale : 1f), Icon("Animation.NextKey"),
                    forwardTooltip + ShortcutHint(forwardShortcutId)),
                FastForward)
            {
                enabled = CanFastForward,
            };

            return new MainToolbarElement[] { resetButton, forwardButton };
        }

        private static bool CanFastForward =>
            EditorApplication.isPlaying && Time.timeScale > 0f && Time.timeScale < maxTimeScale;

        private static bool CanReset =>
            EditorApplication.isPlaying && Time.timeScale > 0f && !Mathf.Approximately(Time.timeScale, _baseline);

        // Shortcuts are rebindable in Edit > Shortcuts > Time Scale. They follow the same rules as the buttons.
        [Shortcut(forwardShortcutId, KeyCode.Period, ShortcutModifiers.Alt)]
        private static void FastForward()
        {
            if (CanFastForward) SetTimeScale(NextTimeScale(Time.timeScale));
        }

        [Shortcut(resetShortcutId, KeyCode.Comma, ShortcutModifiers.Alt)]
        private static void ResetSpeed()
        {
            if (CanReset) SetTimeScale(_baseline);
        }

        private static string ShortcutHint(string id)
        {
            var binding = ShortcutManager.instance.GetShortcutBinding(id).ToString();
            return string.IsNullOrEmpty(binding) ? "" : $" [{binding}]";
        }

        private static float NextTimeScale(float current) => Mathf.Min(Mathf.Max(current, _baseline) * 2f, maxTimeScale);

        private static void SetTimeScale(float value)
        {
            if (!EditorApplication.isPlaying) return;
            Time.timeScale = value;
            MainToolbar.Refresh(toolbarPath);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    _baseline = Time.timeScale > 0f ? Time.timeScale : 1f;
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                    Time.timeScale = _baseline;
                    break;
            }

            MainToolbar.Refresh(toolbarPath);
        }

        // The game can change the time scale too (pause menu, TimeManager), keep the label in sync.
        private static void RefreshIfTimeScaleChanged()
        {
            if (!EditorApplication.isPlaying || Mathf.Approximately(Time.timeScale, _shownTimeScale)) return;
            MainToolbar.Refresh(toolbarPath);
        }

        private static string Format(float scale) => "x" + scale.ToString("0.##", CultureInfo.InvariantCulture);

        private static Texture2D Icon(string name) => EditorGUIUtility.IconContent(name).image as Texture2D;
    }
}
#endif