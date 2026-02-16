using System;
using System.IO;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    public class FinalReportWindow : MonoBehaviour
    {
        #region Fields & State
        private Rect windowRect = new Rect(
            CinematicUIResources.Windows.FinalReport.DEFAULT_X,
            CinematicUIResources.Windows.FinalReport.DEFAULT_Y,
            CinematicUIResources.Windows.FinalReport.WIDTH,
            CinematicUIResources.Windows.FinalReport.HEIGHT
        );

        private GUIStyle windowStyle;
        private bool hasInitStyles = false;
        private bool shouldShow = false;

        private int capturedFrames;
        private float simulatedSeconds;
        private float outputDuration;
        private float realWorldCaptureTime;
        private string encodingModeUsed;
        private string outputFilePath;
        private bool wasUnlimitedRecording;
        #endregion
        #region Public API
        public bool IsVisible => shouldShow;
        public void ShowReport(
            int frames,
            float simSeconds,
            float outDuration,
            float realTimeSeconds,
            string encodingMode,
            string filePath,
            bool unlimited = false)
        {
            capturedFrames = frames;
            simulatedSeconds = simSeconds;
            outputDuration = outDuration;
            realWorldCaptureTime = realTimeSeconds;
            encodingModeUsed = encodingMode;
            outputFilePath = filePath;
            wasUnlimitedRecording = unlimited;

            shouldShow = true;

            Debug.Log(string.Format(Report.FinalReportLog, frames, simSeconds, realTimeSeconds, encodingMode, unlimited, filePath));
        }
        public void HideReport()
        {
            shouldShow = false;
        }
        #endregion
        #region Private Implementation
        private void InitStyles()
        {
            if (hasInitStyles) return;
            windowStyle = CinematicUIResources.Styles.Window();
            hasInitStyles = true;
        }
        private void OnWindow(int windowId)
        {
            GUILayout.BeginVertical();

            GUIStyle headerStyle = CinematicUIResources.Styles.Header();

            GUILayout.Label(Report.SummaryHeader, headerStyle);
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            GUILayout.BeginHorizontal();
            GUILayout.Label(Report.FramesCaptured, GUILayout.Width(130));
            GUILayout.Label(capturedFrames.ToString("N0"), GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(Report.SimulatedTime, GUILayout.Width(130));
            GUILayout.Label(simulatedSeconds.ToString("F2") + Report.SecondsUnit, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(wasUnlimitedRecording ? Report.OutputDurationUnlimited : Report.OutputDuration, GUILayout.Width(130));
            GUILayout.Label(outputDuration.ToString("F2") + Report.SecondsUnit, GUILayout.Width(100));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(Report.RealCaptureTime, GUILayout.Width(130));
            GUILayout.Label(FormatTimeSpan(TimeSpan.FromSeconds(realWorldCaptureTime)), GUILayout.Width(150));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(Report.EncodingMode, GUILayout.Width(130));
            GUILayout.Label(encodingModeUsed);
            GUILayout.EndHorizontal();

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            GUILayout.Label(Report.FilenameLabel, HighLogic.Skin.label);

            GUILayout.BeginHorizontal();

            // Display relative path from GameData if possible, otherwise truncate
            string displayPath = GetDisplayPath(outputFilePath);
            GUI.enabled = false;
            GUILayout.TextField(displayPath, HighLogic.Skin.textField);
            GUI.enabled = true;

            // Open Folder button
            if (GUILayout.Button(Report.OpenFolder, GUILayout.Width(90), GUILayout.Height(25)))
            {
                OpenContainingFolder();
            }

            GUILayout.EndHorizontal();

            GUILayout.Space(CinematicUIResources.Spacing.LARGE);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(Common.Okay, HighLogic.Skin.button, GUILayout.Width(100), GUILayout.Height(30)))
            {
                shouldShow = false;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUI.DragWindow();
        }
        /// <summary>
        /// Returns just the filename without any path
        /// </summary>
        private string GetDisplayPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;
            return Path.GetFileName(fullPath);
        }
        /// <summary>
        /// Opens the file explorer to the directory containing the output file
        /// </summary>
        private void OpenContainingFolder()
        {
            try
            {
                string folderPath = Path.GetDirectoryName(outputFilePath);

                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    // Use Unity's cross-platform URL opener with file protocol
                    // Convert backslashes to forward slashes for URL format
                    string url = "file:///" + folderPath.Replace("\\", "/");
                    Application.OpenURL(url);

                    Debug.Log(string.Format(Report.OpeningFolderLog, folderPath));
                }
                else
                {
                    Debug.Log(string.Format(Report.CannotOpenFolderLog, folderPath));
                    ScreenMessages.PostScreenMessage(Report.FolderNotFound, 3f, ScreenMessageStyle.UPPER_CENTER);
                }
            }
            catch (Exception ex)
            {
                Debug.Log(string.Format(Report.FailedToOpenFolderLog, ex.Message));
                ScreenMessages.PostScreenMessage(Report.FailedToOpenFolder, 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }
        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            return $"{ts.Minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds / 100:D1}";
        }
        #endregion
        #region Unity Lifecycle
        void Start()
        {
            InitStyles();
        }
        void OnGUI()
        {
            if (!shouldShow) return;

            windowRect = GUILayout.Window(
                CinematicUIResources.Windows.IDs.FinalReport,
                windowRect,
                OnWindow,
                Report.WindowTitle,
                windowStyle
            );
        }
        #endregion

    }
}