using CinematicRecorder.Audio;
using CinematicRecorder.Core;
using DearImGuiKSP;
using DearImGuiKSP.Application;
using System;
using System.IO;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Post-capture report view — capture summary stats, output paths, optional audio
    /// muxing, and folder access. DearImGui-KSP VIEW-1 port (chunk C6) of the IMGUI
    /// MonoBehaviour; the port pattern follows SettingsDialog (chunk C2). Drawn inside
    /// the "Recording Complete" window scope from CinematicUiHost. The 30s session-end
    /// watchdog runs in <see cref="Tick"/>, called from the host's Update — the view is
    /// a plain class with no Unity event methods.
    /// </summary>
    public class FinalReportWindow
    {
        #region Constants & Static State
        // 30s watchdog, unchanged from the MonoBehaviour version.
        private const float ReportTimeoutSeconds = 30f;

        // Green for the "Muxed!" completion state (was Color.green text on a disabled button).
        private static readonly Color32 MuxedGreenColor = KspPalette.GreenLight;

        // Grey for read-only / non-interactive stand-ins (theme-disabled text slot; the
        // library has no disabled-widget state — C2 D-U8 #1).
        private static readonly Color32 GreyedColor = KspPalette.TextLightGrey;
        #endregion

        #region Fields & State
        private bool shouldShow;
        private int capturedFrames;
        private float simulatedSeconds;
        private float outputDuration;
        private float realWorldCaptureTime;
        private string encodingModeUsed;
        private string outputFilePath;
        private string audioFilePath;
        private bool wasUnlimitedRecording;
        private float showStartTime = -1f;
        private string _ffmpegPath;
        private bool _isMuxing;
        private bool _muxingCompleted;
        // Set by the mux completion callback (background threadpool thread); posted to
        // the screen from Tick on the main thread. Unity UI calls are main-thread-only —
        // posting directly from the callback crashes the process (issue #008).
        private volatile string _pendingScreenMessage;
        #endregion

        #region Public API
        /// <summary>True while the report should be drawn.</summary>
        public bool IsVisible => shouldShow;

        /// <summary>
        /// Shows the report and captures the session outcome. Signature preserved
        /// verbatim (REPORT-1); also (re)arms the 30s session-end watchdog.
        /// </summary>
        /// <param name="frames">Total frames captured.</param>
        /// <param name="simSeconds">Simulated seconds elapsed.</param>
        /// <param name="outDuration">Output video duration at playback FPS.</param>
        /// <param name="realTimeSeconds">Real-world capture wall time.</param>
        /// <param name="encodingMode">The encoding path that actually ran.</param>
        /// <param name="filePath">Output video file (or PNG sequence folder).</param>
        /// <param name="audioPath">Captured audio file, if audio was recorded.</param>
        /// <param name="unlimited">True when the recording ran in unlimited mode.</param>
        /// <param name="ffmpegPath">Path to the FFmpeg binary, when muxing is possible.</param>
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

        /// <summary>
        /// Hides the report. If called while a capture session is still running, forces
        /// <see cref="DeterministicCaptureSession.EndSession"/> (semantics unchanged from
        /// the MonoBehaviour version).
        /// </summary>
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

        #region Watchdog
        /// <summary>
        /// Checks for timeout condition to force session cleanup if the user leaves the
        /// report open. Called from CinematicUiHost.Update — the view has no Unity
        /// event methods of its own.
        /// </summary>
        internal void Tick()
        {
            // Post any message stashed by the background-thread mux callback (issue #008).
            string pending = _pendingScreenMessage;
            if (pending != null)
            {
                _pendingScreenMessage = null;
                ScreenMessages.PostScreenMessage(pending, 3f, ScreenMessageStyle.UPPER_CENTER);
            }

            if (shouldShow && showStartTime > 0 && DeterministicCaptureSession.IsRunning)
            {
                float elapsed = Time.realtimeSinceStartup - showStartTime;
                if (elapsed > ReportTimeoutSeconds)
                {
                    UnityEngine.Debug.LogWarning(string.Format(
                        "[FinalReportWindow] Report timeout reached ({0}s). Forcing session end.",
                        ReportTimeoutSeconds));

                    DeterministicCaptureSession.EndSession();
                    showStartTime = -1f;
                }
            }
        }
        #endregion

        #region Draw
        /// <summary>
        /// Per-frame widget declarations for the whole report. Called only from
        /// CinematicUiHost, inside the "Recording Complete" window scope. Layout per
        /// LAYOUT_PROPOSAL §5: header → 5 stat rows → output paths (plain text) →
        /// footer Row (mux / open folder / okay) → L9 watchdog hint.
        /// </summary>
        internal void Draw()
        {
            // Header. The old bold-14pt header style is not expressible (no per-widget
            // font weight — C2 D-U8 #3); plain themed text stands in.
            DearImGuiKSP.DearImGuiKSP.Text(Report.SummaryHeader);

            DrawStatRows();
            DrawOutputPaths();
            DrawFooterRow();

            // L9: one-line static hint about the 30s session-end watchdog.
            DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, Report.SessionEndWatchdogHint);
        }

        // Stat values are changing data — formatting them per frame is sanctioned
        // (spec §4.3); the labels are static consts. The old fixed 130px label column
        // and the encoding-mode row's missing width constraint disappear with autoResize.
        private void DrawStatRows()
        {
            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(Report.FramesCaptured);
                DearImGuiKSP.DearImGuiKSP.Text(capturedFrames.ToString("N0"));
            }

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(Report.SimulatedTime);
                DearImGuiKSP.DearImGuiKSP.Text(simulatedSeconds.ToString("F2") + Report.SecondsUnit);
            }

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(
                    wasUnlimitedRecording ? Report.OutputDurationUnlimited : Report.OutputDuration);
                DearImGuiKSP.DearImGuiKSP.Text(outputDuration.ToString("F2") + Report.SecondsUnit);
            }

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(Report.RealCaptureTime);
                DearImGuiKSP.DearImGuiKSP.Text(FormatTimeSpan(TimeSpan.FromSeconds(realWorldCaptureTime)));
            }

            using (ImGuiEx.Row())
            {
                DearImGuiKSP.DearImGuiKSP.Text(Report.EncodingMode);
                DearImGuiKSP.DearImGuiKSP.Text(encodingModeUsed);
            }
        }

        // Readonly paths as plain text (LAYOUT_PROPOSAL §5) — the old disabled
        // TextFields are gone. Video shows the basename (or PNG-sequence folder marker);
        // the audio row keeps today's basename-only display.
        private void DrawOutputPaths()
        {
            DearImGuiKSP.DearImGuiKSP.Text(Report.FilenameLabel);
            DearImGuiKSP.DearImGuiKSP.Text(GetDisplayPath(outputFilePath));

            if (!string.IsNullOrEmpty(audioFilePath))
            {
                using (ImGuiEx.Row())
                {
                    DearImGuiKSP.DearImGuiKSP.Text(Report.AudioFileLabel);
                    DearImGuiKSP.DearImGuiKSP.Text(Path.GetFileName(audioFilePath));
                }
            }
        }

        // One footer Row (LAYOUT_PROPOSAL §5): [Mux Audio (if applicable)][Open Folder][Okay].
        // The mux slot has three states — live button / greyed "Muxing..." stand-in (the
        // library has no disabled-widget state, C3 D-2 idiom) / green "Muxed!" text.
        private void DrawFooterRow()
        {
            bool muxApplicable = !string.IsNullOrEmpty(audioFilePath) && !string.IsNullOrEmpty(_ffmpegPath);

            using (ImGuiEx.Row())
            {
                if (muxApplicable)
                {
                    if (_muxingCompleted)
                    {
                        DearImGuiKSP.DearImGuiKSP.TextColored(MuxedGreenColor, Report.MuxedButton);
                    }
                    else if (_isMuxing)
                    {
                        DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, Report.MuxingInProgress);
                    }
                    else if (DearImGuiKSP.DearImGuiKSP.Button(Report.MuxAudioButton))
                    {
                        StartMuxing();
                    }
                }

                if (DearImGuiKSP.DearImGuiKSP.Button(Report.OpenFolder))
                {
                    OpenContainingFolder();
                }

                if (DearImGuiKSP.DearImGuiKSP.Button(Common.Okay))
                {
                    HideReport();
                }
            }
        }
        #endregion

        #region Private Implementation
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
                        _pendingScreenMessage = Report.MuxingComplete;
                    }
                    else
                    {
                        _pendingScreenMessage = result;
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
                return Path.GetFileName(fullPath) + Report.PngSequenceSuffix;
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
    }
}
