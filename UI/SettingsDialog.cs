using CinematicRecorder.Capture;
using CinematicRecorder.Core;
using CinematicRecorder.Integration;
using System;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Main settings dialog for CinematicRecorder. Manages capture settings,
    /// encoder configuration, and recording start/stop.
    /// </summary>
    public class SettingsDialog : MonoBehaviour
    {
        #region Fields & State
        private Rect windowRect = new Rect(
            CinematicUIResources.Windows.Settings.DEFAULT_X,
            CinematicUIResources.Windows.Settings.DEFAULT_Y,
            CinematicUIResources.Layout.Settings.MAIN_PANEL_WIDTH,
            CinematicUIResources.Windows.Settings.COLLAPSED_HEIGHT
        );

        private bool renderDisplay;
        private bool showEncodingSettings;
        private bool stopRequested;

        private GUIStyle windowStyle;
        private bool stylesInitialized;
        private bool showAdvancedPanel = false;

        private readonly int[] frameratePresets = { 24, 30, 60, 120, 240, 384 };
        private readonly string[] encoderTabNames = { Settings.EncoderAMD, Settings.EncoderNVIDIA, Settings.EncoderCPU };
        private readonly string[] rateControlNames = { Settings.RateControlCQP, Settings.RateControlVBR };
        private readonly string[] speedPresetNames = { Settings.SpeedPresetSpeed, Settings.SpeedPresetBalanced, Settings.SpeedPresetQuality };
        #endregion
        #region Unity Lifecycle
        private void OnGUI()
        {
            if (!renderDisplay) return;

            if (Event.current.type == EventType.Layout)
            {
                float targetWidth = showAdvancedPanel
                    ? (CinematicUIResources.Layout.Settings.MAIN_PANEL_WIDTH + CinematicUIResources.Layout.Settings.ADVANCED_PANEL_WIDTH + 10)
                    : CinematicUIResources.Layout.Settings.MAIN_PANEL_WIDTH;
                windowRect.width = Mathf.Lerp(windowRect.width, targetWidth, 0.25f);
                if (Mathf.Abs(windowRect.width - targetWidth) < 1f)
                    windowRect.width = targetWidth;

                float targetHeight = showEncodingSettings
                    ? CinematicUIResources.Windows.Settings.EXPANDED_HEIGHT
                    : CinematicUIResources.Windows.Settings.COLLAPSED_HEIGHT;
                windowRect.height = Mathf.Lerp(windowRect.height, targetHeight, 0.25f);
                if (Mathf.Abs(windowRect.height - targetHeight) < 0.5f)
                    windowRect.height = targetHeight;
            }

            windowRect = GUILayout.Window(
                CinematicUIResources.Windows.IDs.Settings,
                windowRect,
                DrawWindow,
                Settings.WindowTitle,
                windowStyle
            );
        }
        #endregion
        #region Initialization
        private void InitStyles()
        {
            if (stylesInitialized) return;
            windowStyle = CinematicUIResources.Styles.Window();
            stylesInitialized = true;
        }
        #endregion
        #region Window Layout
        private void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            DrawStatusSection();
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();

            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Settings.ADVANCED_TOGGLE_WIDTH));
            GUIStyle advStyle = CinematicUIResources.Styles.Button();
            if (showAdvancedPanel)
            {
                advStyle.normal.textColor = CinematicUIResources.Colors.TOGGLE_ACTIVE_GREEN;
                advStyle.fontStyle = FontStyle.Bold;
            }

            string arrow = showAdvancedPanel ? Common.arrowL : Common.arrowR;
            string buttonText = arrow + Settings.AdvancedButton;
            if (GUILayout.Button(buttonText, advStyle, GUILayout.Height(CinematicUIResources.Layout.Settings.ADVANCED_TOGGLE_HEIGHT)))
            {
                showAdvancedPanel = !showAdvancedPanel;
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Settings.MAIN_PANEL_WIDTH - CinematicUIResources.Spacing.NORMAL * 2));

            DrawCaptureTimingSection();
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            DrawDurationSection();
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            DrawEncodingFoldout();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawRecordButton();
            GUILayout.EndVertical();

            if (showAdvancedPanel)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT / 2);
                GUI.color = CinematicUIResources.Colors.SEPARATOR_GRAY;
                GUILayout.Box("", GUILayout.Width(CinematicUIResources.Layout.SEPARATOR_LINE_WIDTH), GUILayout.ExpandHeight(true));
                GUI.color = Color.white;
                GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

                GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Settings.ADVANCED_PANEL_WIDTH - CinematicUIResources.Layout.Settings.ADVANCED_MARGIN));
                DrawAdvancedContent();
                GUILayout.EndVertical();
            }
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }
        private void DrawRecordButton()
        {
            bool running = DeterministicCaptureSession.IsRunning;
            GUI.color = running ? Color.red : Color.green;
            if (GUILayout.Button(
                running ? Settings.StopRecording : Settings.StartRecording,
                GUILayout.Height(CinematicUIResources.Layout.BTN_HEIGHT_RECORD)))
            {
                if (running)
                {
                    stopRequested = true;
                    DeterministicCaptureSession.RequestStop();
                }
                else
                {
                    StartRecording();
                }
            }
            GUI.color = Color.white;
        }
        private void StartRecording()
        {
            stopRequested = false;
            int simFps = frameratePresets[SessionState.SimFpsIndex];
            int playbackFps = frameratePresets[SessionState.PlaybackFpsIndex];
            bool forceSoftware = SessionState.SelectedEncoderTab == 2;
            bool zeroCopy = SessionState.SelectedEncoderTab != 2;

            DeterministicCaptureSession.Run(
                    simFps,
                    playbackFps,
                    SessionState.DurationSeconds,
                    forceSoftware,
                    zeroCopy);
        }
        #endregion
        #region Status Display
        private void DrawStatusSection()
        {
            if (stopRequested && !DeterministicCaptureSession.IsRunning)
            {
                stopRequested = false;
            }

            if (DeterministicCaptureSession.IsRunning && !stopRequested)
            {
                bool unlimited = DeterministicCaptureSession.IsUnlimitedMode;

                GUIStyle style = CinematicUIResources.Styles.Status(CinematicUIResources.Colors.Status.RECORDING);

                if (unlimited)
                {
                    GUILayout.Label(Settings.UnlimitedRecordingStatus, style);
                    GUILayout.Label(string.Format(Recording.SimulatedUnlimitedFormat, DeterministicCaptureSession.AccumulatedSimulatedSeconds));
                    GUILayout.Label(string.Format(Recording.FramesUnlimitedFormat, DeterministicCaptureSession.CapturedFrames));

                    float captureFps = DeterministicCaptureSession.CaptureFPS;
                    float playbackFps = frameratePresets[SessionState.PlaybackFpsIndex];
                    float ratio = playbackFps > 0.1f ? captureFps / playbackFps : 0f;

                    GUIStyle fpsStyle = CinematicUIResources.Styles.Status(Color.white);
                    ApplyFpsColorGradient(fpsStyle, ratio);
                    GUILayout.Label(string.Format(Recording.CaptureRateFormat, captureFps), fpsStyle);
                }
                else
                {
                    GUILayout.Label(Settings.RecordingStatus, style);
                    GUILayout.Label(string.Format(Settings.TimeProgressFormat, DeterministicCaptureSession.AccumulatedSimulatedSeconds, DeterministicCaptureSession.TargetSeconds));
                    GUILayout.Label(string.Format(Settings.FramesProgressFormat, DeterministicCaptureSession.CapturedFrames, DeterministicCaptureSession.TargetFrames));

                    float captureFps = DeterministicCaptureSession.CaptureFPS;
                    int framesRemaining = DeterministicCaptureSession.TargetFrames - DeterministicCaptureSession.CapturedFrames;
                    float secondsRemaining = captureFps > 0.1f ? framesRemaining / captureFps : 0f;
                    TimeSpan remaining = TimeSpan.FromSeconds(secondsRemaining);

                    float playbackFps = frameratePresets[SessionState.PlaybackFpsIndex];
                    float ratio = playbackFps > 0.1f ? captureFps / playbackFps : 0f;

                    GUIStyle fpsStyle = CinematicUIResources.Styles.Status(Color.white);
                    ApplyFpsColorGradient(fpsStyle, ratio);

                    GUILayout.Label(string.Format(Settings.CaptureRatePercentFormat, captureFps, ratio * 100), fpsStyle);
                    GUILayout.Label(string.Format(Settings.EstimatedRemainingFormat, remaining));
                }
            }
            else if (stopRequested)
            {
                GUIStyle style = CinematicUIResources.Styles.Status(CinematicUIResources.Colors.Status.STOPPING);
                GUILayout.Label(Settings.StoppingStatus, style);
            }
            else
            {
                int fps = frameratePresets[SessionState.PlaybackFpsIndex];
                GUILayout.Label(string.Format(Settings.ReadyStatusFormat, Screen.width, Screen.height, fps));
            }
        }
        private void ApplyFpsColorGradient(GUIStyle style, float ratio)
        {
            if (ratio < 0.10f)
                style.normal.textColor = new Color(0.5f, 0f, 0f);
            else if (ratio < 0.30f)
            {
                float t = Mathf.Pow((ratio - 0.10f) / 0.20f, 0.5f);
                style.normal.textColor = Color.Lerp(Color.red, new Color(1f, 0.5f, 0f), t);
            }
            else if (ratio < 0.60f)
            {
                float t = Mathf.Pow((ratio - 0.30f) / 0.30f, 2.0f);
                style.normal.textColor = Color.Lerp(new Color(1f, 0.5f, 0f), Color.yellow, t);
            }
            else if (ratio < 0.95f)
            {
                float t = (ratio - 0.60f) / 0.35f;
                style.normal.textColor = Color.Lerp(Color.yellow, Color.green, t);
            }
            else if (ratio <= 1.05f)
                style.normal.textColor = Color.green;
            else if (ratio < 1.25f)
            {
                float t = (ratio - 1.05f) / 0.20f;
                style.normal.textColor = Color.Lerp(Color.green, Color.cyan, t);
            }
            else
                style.normal.textColor = Color.cyan;
        }
        #endregion
        #region Capture Settings
        private void DrawCaptureTimingSection()
        {
            GUILayout.BeginVertical();
            GUILayout.Label(Settings.CaptureFPS, HighLogic.Skin.label);
            DrawFpsSelector(SessionState.SimFpsIndex, v => SessionState.SimFpsIndex = v);

            GUILayout.Space(CinematicUIResources.Spacing.MINIMAL);
            GUILayout.BeginHorizontal();
            GUI.enabled = !SessionState.LockFps;
            GUILayout.Label(Settings.PlaybackFPS, HighLogic.Skin.label, GUILayout.Width(CinematicUIResources.Layout.FPS.PLAYBACK_LABEL_WIDTH));
            GUI.enabled = true;

            GUIStyle lockStyle = CinematicUIResources.Styles.Toggle();
            if (SessionState.LockFps)
            {
                lockStyle.normal.textColor = Color.green;
                lockStyle.fontStyle = FontStyle.Bold;
            }

            SessionState.LockFps = GUILayout.Toggle(SessionState.LockFps, Settings.LockToggle, lockStyle, GUILayout.Width(CinematicUIResources.Layout.FPS.LOCK_TOGGLE_WIDTH), GUILayout.ExpandWidth(false));
            GUILayout.EndHorizontal();

            GUI.enabled = !SessionState.LockFps;
            DrawFpsSelector(SessionState.PlaybackFpsIndex, v => SessionState.PlaybackFpsIndex = v);
            GUI.enabled = true;

            if (SessionState.LockFps)
                SessionState.PlaybackFpsIndex = SessionState.SimFpsIndex;

            float sim = frameratePresets[SessionState.SimFpsIndex];
            float play = frameratePresets[SessionState.PlaybackFpsIndex];
            GUILayout.Label(string.Format(Settings.PlaybackSpeedFormat, play / sim));
            GUILayout.EndVertical();
        }

        private void DrawDurationSection()
        {
            GUILayout.Label(Settings.SimulatedTimeLabel, HighLogic.Skin.label);
            GUILayout.BeginHorizontal();

            if (GUILayout.Button(Settings.DurationDecrement, GUILayout.Width(CinematicUIResources.Layout.Duration.BTN_WIDTH)))
                SessionState.DurationSeconds = Mathf.Max(0f, SessionState.DurationSeconds - CinematicUIResources.Layout.Duration.STEP);

            string displayText = SessionState.DurationSeconds <= 0 ? "∞" : SessionState.DurationSeconds.ToString("0.0");
            string text = GUILayout.TextField(displayText, GUILayout.Width(CinematicUIResources.Layout.Duration.FIELD_WIDTH));

            if (float.TryParse(text, out float parsed))
                SessionState.DurationSeconds = Mathf.Clamp(parsed, 0f, 3600f);

            if (GUILayout.Button(Settings.DurationIncrement, GUILayout.Width(CinematicUIResources.Layout.Duration.BTN_WIDTH)))
            {
                SessionState.DurationSeconds += CinematicUIResources.Layout.Duration.STEP;
                if (DeterministicCaptureSession.IsRunning)
                    DeterministicCaptureSession.ExtendDuration(CinematicUIResources.Layout.Duration.STEP);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawFpsSelector(int index, Action<int> setter)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Common.arrowL, GUILayout.Width(CinematicUIResources.Layout.FPS.SELECTOR_WIDTH)) && index > 0)
                setter(index - 1);

            GUILayout.Label(string.Format(Settings.FPSDisplayFormat, frameratePresets[index]), GUILayout.Width(CinematicUIResources.Layout.FPS.LABEL_WIDTH));

            if (GUILayout.Button(Common.arrowR, GUILayout.Width(CinematicUIResources.Layout.FPS.SELECTOR_WIDTH)) && index < frameratePresets.Length - 1)
                setter(index + 1);
            GUILayout.EndHorizontal();
        }
        #endregion
        #region Encoding Settings
        private void DrawEncodingFoldout()
        {
            string label = showEncodingSettings ? Settings.HideEncoding : Settings.ShowEncoding;
            if (GUILayout.Button(label, HighLogic.Skin.button))
                showEncodingSettings = !showEncodingSettings;

            if (!showEncodingSettings) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label(Settings.EncoderTitle, HighLogic.Skin.label);

            int selected = SessionState.SelectedEncoderTab;
            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(selected == 0, Settings.EncoderAMD, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.BTN_WIDTH_AMD)))
                selected = 0;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            if (GUILayout.Toggle(selected == 1, Settings.EncoderNVIDIA, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.BTN_WIDTH_NVIDIA)))
                selected = 1;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            if (GUILayout.Toggle(selected == 2, Settings.EncoderCPU, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.BTN_WIDTH_CPU)))
                selected = 2;

            GUILayout.EndHorizontal();
            SessionState.SelectedEncoderTab = selected;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            if (selected == 0)
                DrawAmfSettings();
            else if (selected == 1)
                DrawNvencSettings();
            else
                DrawCpuSettings();

            GUILayout.EndVertical();
        }

        private void DrawAmfSettings()
        {
            GUILayout.Label(Settings.AMDHEVC, HighLogic.Skin.label);

            int selectedRc = SessionState.AmfRateControlMode;
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedRc == 0, Settings.RateControlCQP, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.RATECONTROL_WIDTH_QUALITY)))
                selectedRc = 0;
            if (GUILayout.Toggle(selectedRc == 1, Settings.RateControlVBR, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.RATECONTROL_WIDTH_VBR)))
                selectedRc = 1;
            GUILayout.EndHorizontal();
            SessionState.AmfRateControlMode = selectedRc;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            if (SessionState.AmfRateControlMode == 0)
            {
                GUILayout.Label(Settings.QualityLabel, HighLogic.Skin.label);
                SessionState.AmfQualitySlider = GUILayout.HorizontalSlider(SessionState.AmfQualitySlider, 0f, 1f);
                int qp = SessionState.AmfCqpValue;
                string qualityDesc = qp <= 8 ? Settings.QualityNearLossless :
                                     qp <= 14 ? Settings.QualityMaster :
                                     qp <= 20 ? Settings.QualityHigh :
                                     Settings.QualityCompressed;
                GUILayout.Label(string.Format(Settings.QPFormat, qp, qualityDesc));

                GUIStyle infoStyle = CinematicUIResources.Styles.Help();
                GUILayout.Label(Settings.CQLabel, infoStyle);
            }
            else
            {
                GUILayout.Label(Settings.TargetBitrateLabel, HighLogic.Skin.label);
                int estimatedMB = (SessionState.AmfTargetBitrate * 5) / 4;
                GUILayout.Label(string.Format(Settings.BitrateEstimateFormat, SessionState.AmfTargetBitrate, estimatedMB));

                SessionState.AmfTargetBitrate = Mathf.Clamp((int)GUILayout.HorizontalSlider(SessionState.AmfTargetBitrate, 10, 200), 10, 200);

                GUIStyle infoStyle = CinematicUIResources.Styles.Help();
                GUILayout.Label(Settings.VBRLabel, infoStyle);
            }

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            GUILayout.Label(Settings.EncodingSpeedLabel, HighLogic.Skin.label);
            int selectedSpeed = SessionState.AmfEncoderSpeed;
            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(selectedSpeed == 0, Settings.SpeedPresetSpeed, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_SPEED)))
                selectedSpeed = 0;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            if (GUILayout.Toggle(selectedSpeed == 1, Settings.SpeedPresetBalanced, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_BALANCED)))
                selectedSpeed = 1;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            if (GUILayout.Toggle(selectedSpeed == 2, Settings.SpeedPresetQuality, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_QUALITY)))
                selectedSpeed = 2;

            GUILayout.EndHorizontal();
            SessionState.AmfEncoderSpeed = selectedSpeed;
        }

        private void DrawNvencSettings()
        {
            GUILayout.Label(Settings.NvidiaHEVC, HighLogic.Skin.label);

            int selectedRc = SessionState.NvencRateControlMode;
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedRc == 0, Settings.RateControlCQP, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.RATECONTROL_WIDTH_QUALITY)))
                selectedRc = 0;
            if (GUILayout.Toggle(selectedRc == 1, Settings.RateControlVBR, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.RATECONTROL_WIDTH_VBR)))
                selectedRc = 1;
            GUILayout.EndHorizontal();
            SessionState.NvencRateControlMode = selectedRc;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            if (SessionState.NvencRateControlMode == 0)
            {
                GUILayout.Label(Settings.QualityLabel, HighLogic.Skin.label);
                SessionState.NvencQualitySlider = GUILayout.HorizontalSlider(SessionState.NvencQualitySlider, 0f, 1f);
                int crf = SessionState.CpuCrfValue;
                string qualityDesc = crf <= 8 ? Settings.QualityNearLossless :
                                     crf <= 14 ? Settings.QualityMaster :
                                     crf <= 20 ? Settings.QualityHigh :
                                     Settings.QualityCompressed;
                GUILayout.Label(string.Format(Settings.CRFFormat, crf, qualityDesc));

                GUIStyle infoStyle = CinematicUIResources.Styles.Help();
                GUILayout.Label(Settings.CQLabel, infoStyle);
            }
            else
            {
                GUILayout.Label(Settings.TargetBitrateLabel, HighLogic.Skin.label);
                int estimatedMB = (SessionState.NvencTargetBitrate * 5) / 4;
                GUILayout.Label(string.Format(Settings.BitrateEstimateFormat, SessionState.NvencTargetBitrate, estimatedMB));
                SessionState.NvencTargetBitrate = Mathf.Clamp((int)GUILayout.HorizontalSlider(SessionState.NvencTargetBitrate, 10, 200), 10, 200);

                GUIStyle infoStyle = CinematicUIResources.Styles.Help();
                GUILayout.Label(Settings.VBRLabel, infoStyle);
            }

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            GUILayout.Label(Settings.EncodingSpeedLabel, HighLogic.Skin.label);
            int selectedSpeed = SessionState.NvencPreset;
            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(selectedSpeed == 0, Settings.SpeedPresetSpeed, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_SPEED)))
                selectedSpeed = 0;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            if (GUILayout.Toggle(selectedSpeed == 1, Settings.SpeedPresetBalanced, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_BALANCED)))
                selectedSpeed = 1;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            if (GUILayout.Toggle(selectedSpeed == 2, Settings.SpeedPresetQuality, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_QUALITY)))
                selectedSpeed = 2;

            GUILayout.EndHorizontal();
            SessionState.NvencPreset = selectedSpeed;
        }

        private void DrawCpuSettings()
        {
            GUILayout.Label(Settings.CPUx264, HighLogic.Skin.label);

            int selectedRc = SessionState.CpuRateControlMode;
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(selectedRc == 0, Settings.RateControlCRF, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.RATECONTROL_WIDTH_QUALITY)))
                selectedRc = 0;
            if (GUILayout.Toggle(selectedRc == 1, Settings.RateControlVBR, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.RATECONTROL_WIDTH_VBR)))
                selectedRc = 1;
            GUILayout.EndHorizontal();
            SessionState.CpuRateControlMode = selectedRc;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            if (SessionState.CpuRateControlMode == 0)
            {
                GUILayout.Label(Settings.QualityLabel, HighLogic.Skin.label);
                SessionState.CpuQualitySlider = GUILayout.HorizontalSlider(SessionState.CpuQualitySlider, 0f, 1f);
                int crf = SessionState.CpuCrfValue;
                string qualityDesc = crf <= 8 ? Settings.QualityNearLossless :
                                     crf <= 14 ? Settings.QualityMaster :
                                     crf <= 20 ? Settings.QualityHigh :
                                     Settings.QualityCompressed;
                GUILayout.Label(string.Format(Settings.CRFFormat, crf, qualityDesc));

                GUIStyle infoStyle = CinematicUIResources.Styles.Help();
                GUILayout.Label(Settings.CQLabel, infoStyle);
            }
            else
            {
                GUILayout.Label(Settings.TargetBitrateLabel, HighLogic.Skin.label);
                int estimatedMB = (SessionState.CpuTargetBitrate * 5) / 4;
                GUILayout.Label(string.Format(Settings.BitrateEstimateFormat, SessionState.CpuTargetBitrate, estimatedMB));
                SessionState.CpuTargetBitrate = Mathf.Clamp((int)GUILayout.HorizontalSlider(SessionState.CpuTargetBitrate, 10, 200), 10, 200);

                GUIStyle infoStyle = CinematicUIResources.Styles.Help();
                GUILayout.Label(Settings.VBRLabel, infoStyle);
            }

            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            GUILayout.Label(Settings.EncodingSpeedLabel, HighLogic.Skin.label);
            int selectedSpeed = SessionState.CpuPreset;
            GUILayout.BeginHorizontal();

            if (GUILayout.Toggle(selectedSpeed == 0, Settings.SpeedPresetSpeed, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_SPEED)))
                selectedSpeed = 0;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            if (GUILayout.Toggle(selectedSpeed == 1, Settings.SpeedPresetBalanced, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_BALANCED)))
                selectedSpeed = 1;
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            if (GUILayout.Toggle(selectedSpeed == 2, Settings.SpeedPresetQuality, HighLogic.Skin.toggle, GUILayout.Width(CinematicUIResources.Layout.Encoder.SPEED_WIDTH_QUALITY)))
                selectedSpeed = 2;

            GUILayout.EndHorizontal();
            SessionState.CpuPreset = selectedSpeed;
        }
        #endregion
        #region Advanced Panel
        private void DrawAdvancedContent()
        {
            GUIStyle headerStyle = CinematicUIResources.Styles.Header();
            GUILayout.Label(Settings.AdvancedOptionsHeader, headerStyle);
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawSafeModeToggle();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawPngSequenceToggle();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            GUIStyle tooltipStyle = CinematicUIResources.Styles.Help();
            tooltipStyle.wordWrap = true;


            if (SessionState.SelectedEncoderTab == 0)
            {
                GUIStyle ditherStyle = CinematicUIResources.Styles.Toggle();
                if (!SessionState.AmfUseBlueNoiseDither)
                    ditherStyle.normal.textColor = CinematicUIResources.Colors.INFO_ORANGE;

                SessionState.AmfUseBlueNoiseDither = GUILayout.Toggle(
                    SessionState.AmfUseBlueNoiseDither,
                    Settings.GradientProtection,
                    ditherStyle
                );

                
                tooltipStyle.wordWrap = true;

                if (SessionState.AmfUseBlueNoiseDither)
                    GUILayout.Label(Settings.GradientTooltip, tooltipStyle);
            }
            else
            {
                GUIStyle infoStyle = CinematicUIResources.Styles.Info();
                infoStyle.wordWrap = true;
                GUILayout.Label(Settings.AMFOnlyWarning, infoStyle);
            }

            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            GUIStyle placeholderStyle = CinematicUIResources.Styles.Info();
            GUILayout.Label(Settings.PostProcessText, placeholderStyle);
        }
        private void DrawSafeModeToggle()
        {
            // Lock during recording to prevent mid-stream codec switches
            bool wasEnabled = GUI.enabled;
            if (DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            // Style: Green + Bold when active
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.ForceSoftwareEncoding)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            // Toggle
            bool newValue = GUILayout.Toggle(
                SessionState.ForceSoftwareEncoding,
                Settings.SafeModeToggle,
                toggleStyle
            );

            if (newValue != SessionState.ForceSoftwareEncoding && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.ForceSoftwareEncoding = newValue;
                UnityEngine.Debug.Log($"[CinematicRecorder] ForceSoftwareEncoding = {newValue}");
            }

            // Help text
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            GUIStyle helpStyle = CinematicUIResources.Styles.Help();
            helpStyle.wordWrap = true;
            GUILayout.Label(Settings.SafeModeTooltip, helpStyle);

            // Warning when disabled during recording
            if (DeterministicCaptureSession.IsRunning)
            {
                GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
                GUIStyle warningStyle = CinematicUIResources.Styles.Label(
                    CinematicUIResources.Colors.INFO_ORANGE,
                    fontSize: CinematicUIResources.Typography.INFO
                );
                GUILayout.Label(Settings.SafeModeRecordingWarning, warningStyle);
            }

            GUI.enabled = wasEnabled;
        }
        private void DrawPngSequenceToggle()
        {
            bool wasEnabled = GUI.enabled;
            if (DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            // Style: Green + Bold when active (matching Safe Mode pattern)
            GUIStyle toggleStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.PngSequence)
            {
                toggleStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                toggleStyle.fontStyle = FontStyle.Bold;
            }

            bool newValue = GUILayout.Toggle(
                SessionState.PngSequence,
                Settings.PngSequenceToggle,
                toggleStyle
            );

            if (newValue != SessionState.PngSequence && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.PngSequence = newValue;
                if (newValue)
                {
                    // Force software encoding when PNG mode is enabled (hardware encoders can't output PNGs)
                    SessionState.ForceSoftwareEncoding = true;
                    UnityEngine.Debug.Log("[CinematicRecorder] PNG Sequence enabled - forcing software encoding path");
                }
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            GUIStyle helpStyle = CinematicUIResources.Styles.Help();
            helpStyle.wordWrap = true;
            GUILayout.Label(Settings.PngSequenceTooltip, helpStyle);

            GUI.enabled = wasEnabled;
        }
        #endregion
        #region Public API
        public bool IsVisible => renderDisplay;
        public event Action OnDialogDismissed;
        /// <summary>
        /// Data container for capture completion report.
        /// </summary>
        public class CaptureReport
        {
            public int CapturedFrames;
            public float SimulatedSeconds;
            public float OutputDuration;
            public float RealWorldCaptureTime;
            public string EncodingMode;
            public string OutputFilePath;
        }
        public void Show()
        {
            renderDisplay = true;
            stopRequested = false;
            InitStyles();
        }
        public void Hide()
        {
            renderDisplay = false;
            OnDialogDismissed?.Invoke();
        }
        #endregion
    }
}