using System;
using UnityEngine;

namespace CinematicRecorder.Integration
{
    /// <summary>
    /// OBSOLETE: Use CameraToolsAdapter.Instance instead.
    /// This class is maintained for backward compatibility during transition.
    /// </summary>
    [Obsolete("Use CameraToolsAdapter.Instance instead")]
    public static class CameraToolsBridge
    {
        public static bool IsAvailable => CameraToolsAdapter.Instance.IsAvailable;

        public static bool IsActive() => CameraToolsAdapter.Instance.IsActive;

        public static ToolModes GetCurrentMode() => CameraToolsAdapter.Instance.CurrentMode;

        public static bool PathExists(int index) => CameraToolsAdapter.Instance.PathExists(index);

        public static CameraToolsSettings CaptureCurrentSettings() => CameraToolsAdapter.Instance.CaptureSettings();

        public static void ActivateMode(ToolModes mode, CameraToolsSettings settings = null) =>
            CameraToolsAdapter.Instance.ActivateMode(mode, settings);

        public static void Revert() => CameraToolsAdapter.Instance.Revert();

        public static void ReleaseControlWithoutReverting() =>
            CameraToolsAdapter.Instance.ReleaseControlWithoutReverting();
    }
}