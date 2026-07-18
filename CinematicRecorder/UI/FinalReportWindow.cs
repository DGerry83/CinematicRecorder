using CinematicRecorder.Audio;
using CinematicRecorder.Core;
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
        private string audioFilePath;
        private bool wasUnlimitedRecording;
        private float showStartTime = -1f;
        private const float REPORT_TIMEOUT_SECONDS = 30f;
        private string _ffmpegPath;
        private bool _isMuxing = false;
        private bool _muxingCompleted = false;
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
            string audioPath,
            bool unlimited = false,
            string ffmpegPath = null)
        {
            capturedFrames = frames;
            simulatedSeconds = simSeconds;
            outputDuration = outDuration;
            realWorldCaptureTime = realTimeSeconds;
            encodingModeUsed = encodingMode;
            outputFilePath = filePath;
            audioFilePath = audioPath;
            wasUnlimitedRecording = unlimited;
            _ffmpegPath = ffmpegPath;

            shouldShow = true;
            showStartTime = Time.realtimeSinceStartup;

            Debug.Log(string.Format(Report.FinalReportLog, frames, simSeconds, realTimeSeconds, encodingMode, unlimited, filePath));
        }
        public void HideReport()
        {
            shouldShow = false;
            showStartTime = -1f;
            _muxingCompleted = false;
            _isMuxing = false;
            if (DeterministicCaptureSession.IsRunning)
            {
                UnityEngine.Debug.Log("[FinalReportWindow] HideReport called while session still running. Forcing EndSession.");
                DeterministicCaptureSession.EndSession();
            }
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
            // Add Mux Audio button if we have both video and audio
            if (!string.IsNullOrEmpty(audioFilePath) && !string.IsNullOrEmpty(_ffmpegPath))
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (_muxingCompleted)
                {
                    // Show completed state - green text, disabled
                    GUIStyle completedStyle = new GUIStyle(HighLogic.Skin.button);
                    completedStyle.normal.textColor = Color.green;
                    completedStyle.fontStyle = FontStyle.Bold;
                    GUI.enabled = false;
                    GUILayout.Button("Muxed!", completedStyle, GUILayout.Width(100), GUILayout.Height(25));
                    GUI.enabled = true;
                }
                else
                {
                    // Show normal or muxing state
                    GUI.enabled = !_isMuxing;
                    string buttonText = _isMuxing ? Report.MuxingInProgress : Report.MuxAudioButton;

                    if (GUILayout.Button(buttonText, GUILayout.Width(100), GUILayout.Height(25)))
                    {
                        StartMuxing();
                    }
                    GUI.enabled = true;
                }

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(audioFilePath))
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Audio File:", GUILayout.Width(130));
                GUI.enabled = false;
                GUILayout.TextField(Path.GetFileName(audioFilePath), HighLogic.Skin.textField);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(CinematicUIResources.Spacing.LARGE);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(Common.Okay, HighLogic.Skin.button, GUILayout.Width(100), GUILayout.Height(30)))
            {
                HideReport();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUI.DragWindow();
        }

        private string NormalizePath(string path)
        {
            return path.Replace('/', '\\');
        }
        private void StartMuxing()
        {
            if (_isMuxing || string.IsNullOrEmpty(_ffmpegPath))
                return;

            _isMuxing = true;

            AudioMuxingUtility.MuxAudioVideo(
                outputFilePath,
                audioFilePath,
                _ffmpegPath,
                (success, result) =>
                {
                    _isMuxing = false;

                    if (success)
                    {
                        _muxingCompleted = true;
                        outputFilePath = result; // Update to muxed path
                        ScreenMessages.PostScreenMessage(Report.MuxingComplete, 3f, ScreenMessageStyle.UPPER_CENTER);
                    }
                    else
                    {
                        ScreenMessages.PostScreenMessage(result, 3f, ScreenMessageStyle.UPPER_CENTER);
                    }
                });
        }
        /// <summary>
        /// Returns just the filename without any path
        /// </summary>
        private string GetDisplayPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return string.Empty;

            // If path is a directory (PNG sequence), show folder name with indicator
            if (Directory.Exists(fullPath))
            {
                return Path.GetFileName(fullPath) + " (PNG Sequence)";
            }

            return Path.GetFileName(fullPath);
        }
        /// <summary>
        /// Opens the file explorer to the directory containing the output file
        /// </summary>
        private void OpenContainingFolder()
        {
            try
            {
                string folderPath;

                // PNG sequence paths point directly to the folder
                if (Directory.Exists(outputFilePath))
                {
                    folderPath = outputFilePath;
                }
                else
                {
                    folderPath = Path.GetDirectoryName(outputFilePath);
                }

                if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
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
        /// <summary>
        /// Checks for timeout condition to force session cleanup if user leaves report open.
        /// </summary>
        void Update()
        {
            if (shouldShow && showStartTime > 0 && DeterministicCaptureSession.IsRunning)
            {
                float elapsed = Time.realtimeSinceStartup - showStartTime;
                if (elapsed > REPORT_TIMEOUT_SECONDS)
                {
                    UnityEngine.Debug.LogWarning(string.Format(
                        "[FinalReportWindow] Report timeout reached ({0}s). Forcing session end.",
                        REPORT_TIMEOUT_SECONDS));

                    DeterministicCaptureSession.EndSession();
                    showStartTime = -1f; 
                }
            }
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