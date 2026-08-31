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
        private AdvancedSettingsWindow advancedSettingsWindow;

        private readonly int[] frameratePresets = { 24, 30, 48, 60, 120, 240, 384 };
        private readonly string[] rateControlNames = { Settings.RateControlCQP, Settings.RateControlVBR };
        private readonly string[] speedPresetNames = { Settings.SpeedPresetSpeed, Settings.SpeedPresetBalanced, Settings.SpeedPresetQuality };
        #endregion

        #region Unity Lifecycle
        void Start()
        {
            advancedSettingsWindow = gameObject.AddComponent<AdvancedSettingsWindow>();
            advancedSettingsWindow.Initialize(this);
        }

        void OnDestroy()
        {
            if (advancedSettingsWindow != null)
            {
                Destroy(advancedSettingsWindow);
            }
        }

        private void OnGUI()
        {
            if (!renderDisplay) return;

            if (Event.current.type == EventType.Layout)
            {
                float targetWidth = CinematicUIResources.Layout.Settings.MAIN_PANEL_WIDTH;
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
            bool advancedVisible = advancedSettingsWindow != null && advancedSettingsWindow.IsVisible;
            if (advancedVisible)
            {
                advStyle.normal.textColor = CinematicUIResources.Colors.TOGGLE_ACTIVE_GREEN;
                advStyle.fontStyle = FontStyle.Bold;
            }

            string arrow = advancedVisible ? Common.arrowL : Common.arrowR;
            string buttonText = arrow + Settings.AdvancedButton;
            // #007: hover tooltip explains the section (audio capture, TAB, CAS)
            if (GUILayout.Button(new GUIContent(buttonText, Settings.AdvancedButtonTooltip), advStyle, GUILayout.Height(CinematicUIResources.Layout.Settings.ADVANCED_TOGGLE_HEIGHT)))
            {
                ToggleAdvancedSettingsWindow();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(CinematicUIResources.Layout.Settings.MAIN_PANEL_WIDTH - CinematicUIResources.Spacing.NORMAL * 2));

            DrawCaptureTimingSection();
            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            DrawDurationSection();
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);
            DrawEncodingFoldout();
            GUILayout.Space(CinematicUIResources.Spacing.LARGE);
            DrawRecordButton();
            GUILayout.EndVertical();

            DrawHoverTooltip();
            GUI.DragWindow();
        }

        /// <summary>
        /// Renders GUI.tooltip as a small overlay box at the mouse position.
        /// IMGUI does not draw tooltips automatically inside GUILayout.Window;
        /// drawn last so it floats above the layout and occupies no layout space.
        /// </summary>
        private void DrawHoverTooltip()
        {
            if (Event.current.type != EventType.Repaint) return;

            string tooltip = GUI.tooltip;
            if (string.IsNullOrEmpty(tooltip)) return;

            GUIStyle tipStyle = new GUIStyle(HighLogic.Skin.box)
            {
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };

            const float width = 180f;
            GUIContent content = new GUIContent(tooltip);
            float height = tipStyle.CalcHeight(content, width);

            Vector2 pos = Event.current.mousePosition + new Vector2(12f, 8f);
            float x = Mathf.Clamp(pos.x, 4f, windowRect.width - width - 4f);
            float y = Mathf.Clamp(pos.y, 4f, windowRect.height - height - 4f);

            GUI.Box(new Rect(x, y, width, height), content, tipStyle);
        }

        private void ToggleAdvancedSettingsWindow()
        {
            if (advancedSettingsWindow == null)
            {
                advancedSettingsWindow = gameObject.AddComponent<AdvancedSettingsWindow>();
                advancedSettingsWindow.Initialize(this);
                advancedSettingsWindow.Show();
            }
            else
            {
                if (advancedSettingsWindow.IsVisible)
                {
                    advancedSettingsWindow.Hide();
                }
                else
                {
                    advancedSettingsWindow.Show();
                }
            }
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
            // Close any open report windows from previous recordings
            FinalReportWindow existingReport = UnityEngine.Object.FindObjectOfType<FinalReportWindow>();
            if (existingReport != null && existingReport.IsVisible)
            {
                existingReport.HideReport();
            }
            stopRequested = false;
            int simFps = frameratePresets[SessionState.SimFpsIndex];
            int playbackFps = frameratePresets[SessionState.PlaybackFpsIndex];
            // Safe Mode forces the CPU path; otherwise zero-copy is tried automatically
            // (availability probes pick NVENC or AMF - no manual GPU selection).
            bool forceSoftware = SessionState.ForceSoftwareEncoding;
            bool zeroCopy = !SessionState.ForceSoftwareEncoding;

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

            string displayText = SessionState.DurationSeconds <= 0 ? Settings.DurationUnlimitedButton : SessionState.DurationSeconds.ToString("0.0");
            string text = GUILayout.TextField(displayText, GUILayout.Width(CinematicUIResources.Layout.Duration.FIELD_WIDTH));

            if (float.TryParse(text, out float parsed))
                SessionState.DurationSeconds = Mathf.Clamp(parsed, 0f, 3600f);

            if (GUILayout.Button(Settings.DurationIncrement, GUILayout.Width(CinematicUIResources.Layout.Duration.BTN_WIDTH)))
            {
                SessionState.DurationSeconds += CinematicUIResources.Layout.Duration.STEP;
                if (DeterministicCaptureSession.IsRunning)
                    DeterministicCaptureSession.ExtendDuration(CinematicUIResources.Layout.Duration.STEP);
            }

            if (GUILayout.Button(Settings.DurationUnlimitedButton, GUILayout.Width(CinematicUIResources.Layout.Duration.BTN_WIDTH)))
                SessionState.DurationSeconds = 0f;

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

            // GPU auto-detection (Phase 3): show what the probes found and only the
            // options relevant to it - users never pick a GPU family manually.
            SessionState.GpuEncoder detected = SessionState.DetectedGpuEncoder;
            string detectedName =
                detected == SessionState.GpuEncoder.Nvidia ? Settings.DetectedEncoderNvenc :
                detected == SessionState.GpuEncoder.Amd ? Settings.DetectedEncoderAmf :
                Settings.DetectedEncoderCpu;
            GUILayout.Label(string.Format(Settings.DetectedEncoderFormat, detectedName), HighLogic.Skin.label);
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            // Safe Mode (forced CPU encoding) - disabled while recording.
            // Same behavior as the old Advanced-tab toggle (single home now).
            bool wasEnabled = GUI.enabled;
            if (DeterministicCaptureSession.IsRunning)
                GUI.enabled = false;

            GUIStyle safeModeStyle = new GUIStyle(HighLogic.Skin.toggle);
            if (SessionState.ForceSoftwareEncoding)
            {
                safeModeStyle.normal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                safeModeStyle.onNormal.textColor = CinematicUIResources.Colors.GLOW_GREEN;
                safeModeStyle.fontStyle = FontStyle.Bold;
            }

            bool safeMode = GUILayout.Toggle(
                SessionState.ForceSoftwareEncoding,
                Settings.SafeModeToggle,
                safeModeStyle
            );
            if (safeMode != SessionState.ForceSoftwareEncoding && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.ForceSoftwareEncoding = safeMode;
                UnityEngine.Debug.Log($"[CinematicRecorder] Safe Mode (force CPU) = {safeMode}");

                // TAB requires GPU - disable it when software encoding is forced
                if (safeMode && SessionState.EnableTemporalAccumulation)
                {
                    SessionState.EnableTemporalAccumulation = false;
                    UnityEngine.Debug.Log("[CinematicRecorder] TAB disabled due to Safe Mode");
                }
            }

            GUILayout.Space(CinematicUIResources.Spacing.TIGHT);
            GUIStyle safeModeHelp = CinematicUIResources.Styles.Help();
            safeModeHelp.wordWrap = true;
            GUILayout.Label(Settings.SafeModeTooltip, safeModeHelp);

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
            GUILayout.Space(CinematicUIResources.Spacing.NORMAL);

            // Quality options only for the encoder that will actually be used
            if (SessionState.ForceSoftwareEncoding || detected == SessionState.GpuEncoder.None)
                DrawCpuSettings();
            else if (detected == SessionState.GpuEncoder.Nvidia)
                DrawNvencSettings();
            else
                DrawAmfSettings();

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
                // Bug fix: this showed CpuCrfValue (the CPU slider's value), so the
                // NVENC slider appeared to do nothing
                int cq = SessionState.NvencCqValue;
                string qualityDesc = cq <= 8 ? Settings.QualityNearLossless :
                                     cq <= 14 ? Settings.QualityMaster :
                                     cq <= 20 ? Settings.QualityHigh :
                                     Settings.QualityCompressed;
                GUILayout.Label(string.Format(Settings.CRFFormat, cq, qualityDesc));

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

        #region Public API
        public bool IsVisible => renderDisplay;
        public event Action OnDialogDismissed;

        /// <summary>
        /// Returns the current window rectangle for docking.
        /// </summary>
        public Rect GetWindowRect() => windowRect;

        /// <summary>
        /// Returns the X coordinate of the right edge for docking child windows.
        /// </summary>
        public float GetDockEdgeX()
        {
            return windowRect.x + CinematicUIResources.Layout.Settings.MAIN_PANEL_WIDTH;
        }

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