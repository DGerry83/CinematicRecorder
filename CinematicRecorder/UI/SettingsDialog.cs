using CinematicRecorder.Core;
using DearImGuiKSP;
using DearImGuiKSP.Application;
using System;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Main settings dialog for CinematicRecorder — capture timing, duration, encoder
    /// configuration, and recording start/stop. DearImGui-KSP view ported from the
    /// IMGUI MonoBehaviour in chunk C2; establishes the VIEW-1 port pattern (plain class,
    /// state in fields, <see cref="Draw"/> declares widgets only) that C3/C5/C6/C8 follow.
    /// Invoked per frame from CinematicUiHost inside the "Cinematic Recorder" window scope.
    /// </summary>
    public class SettingsDialog
    {
        #region Constants & Static State
        // FPS preset values, indexed by SessionState.SimFpsIndex / PlaybackFpsIndex.
        private static readonly int[] FrameratePresets = { 24, 30, 48, 60, 120, 240, 384 };

        // Combo item labels built once — no per-frame string building (VIEW-1).
        private static readonly string[] FpsComboItems = BuildFpsComboItems();
        private static readonly string[] SpeedPresetNames =
            { Settings.SpeedPresetSpeed, Settings.SpeedPresetBalanced, Settings.SpeedPresetQuality };

        // Greyed-stand-in labels, composed once from consts (encoding controls while recording).
        private static readonly string SafeModeGreyedOn = Settings.SafeModeToggle + " " + Settings.StateOn;
        private static readonly string SafeModeGreyedOff = Settings.SafeModeToggle + " " + Settings.StateOff;

        // Advanced-button labels per visibility state (arrow swap, as in the old UI).
        private static readonly string AdvancedButtonShown = Common.arrowL + Settings.AdvancedButton;
        private static readonly string AdvancedButtonHidden = Common.arrowR + Settings.AdvancedButton;

        // Status colors as bytes for TextColored (old Color values preserved).
        private static readonly Color32 StatusRecordingColor = new Color32(255, 255, 0, 255); // was Color.yellow
        private static readonly Color32 StatusStoppingColor = new Color32(255, 0, 0, 255);    // was Color.red
        private static readonly Color32 WarningOrangeColor = new Color32(255, 140, 0, 255);   // was Colors.INFO_ORANGE

        // Grey used for every read-only/disabled stand-in (theme-disabled text slot).
        private static readonly Color32 GreyedColor = KspPalette.TextLightGrey;
        #endregion

        #region Fields & State
        private bool renderDisplay;
        private bool stopRequested;
        private string _durationText = "10.0";
        #endregion

        #region Vendor Panel Bindings
        // One layout method serves all three encoder families (dedup of the old
        // DrawAmfSettings/DrawNvencSettings/DrawCpuSettings triplication); the
        // per-family state differences live in these one-time delegate bindings.
        private sealed class VendorPanelBindings
        {
            public string Title;
            public string[] RateControlNames;
            public string QualityValueFormat;
            public Func<int> GetRateControl;
            public Action<int> SetRateControl;
            public Func<float> GetQualitySlider;
            public Action<float> SetQualitySlider;
            public Func<int> GetQualityValue;
            public Func<int> GetTargetBitrate;
            public Action<int> SetTargetBitrate;
            public Func<int> GetSpeed;
            public Action<int> SetSpeed;
        }

        private static readonly VendorPanelBindings AmfBindings = new VendorPanelBindings
        {
            Title = Settings.AMDHEVC,
            RateControlNames = new[] { Settings.RateControlCQP, Settings.RateControlVBR },
            QualityValueFormat = Settings.QPFormat,
            GetRateControl = () => SessionState.AmfRateControlMode,
            SetRateControl = value => SessionState.AmfRateControlMode = value,
            GetQualitySlider = () => SessionState.AmfQualitySlider,
            SetQualitySlider = value => SessionState.AmfQualitySlider = value,
            GetQualityValue = () => SessionState.AmfCqpValue,
            GetTargetBitrate = () => SessionState.AmfTargetBitrate,
            SetTargetBitrate = value => SessionState.AmfTargetBitrate = value,
            GetSpeed = () => SessionState.AmfEncoderSpeed,
            SetSpeed = value => SessionState.AmfEncoderSpeed = value,
        };

        private static readonly VendorPanelBindings NvencBindings = new VendorPanelBindings
        {
            Title = Settings.NvidiaHEVC,
            RateControlNames = new[] { Settings.RateControlCRF, Settings.RateControlVBR },
            QualityValueFormat = Settings.CRFFormat,
            GetRateControl = () => SessionState.NvencRateControlMode,
            SetRateControl = value => SessionState.NvencRateControlMode = value,
            GetQualitySlider = () => SessionState.NvencQualitySlider,
            SetQualitySlider = value => SessionState.NvencQualitySlider = value,
            GetQualityValue = () => SessionState.NvencCqValue,
            GetTargetBitrate = () => SessionState.NvencTargetBitrate,
            SetTargetBitrate = value => SessionState.NvencTargetBitrate = value,
            GetSpeed = () => SessionState.NvencPreset,
            SetSpeed = value => SessionState.NvencPreset = value,
        };

        private static readonly VendorPanelBindings CpuBindings = new VendorPanelBindings
        {
            Title = Settings.CPUx264,
            RateControlNames = new[] { Settings.RateControlCRF, Settings.RateControlVBR },
            QualityValueFormat = Settings.CRFFormat,
            GetRateControl = () => SessionState.CpuRateControlMode,
            SetRateControl = value => SessionState.CpuRateControlMode = value,
            GetQualitySlider = () => SessionState.CpuQualitySlider,
            SetQualitySlider = value => SessionState.CpuQualitySlider = value,
            GetQualityValue = () => SessionState.CpuCrfValue,
            GetTargetBitrate = () => SessionState.CpuTargetBitrate,
            SetTargetBitrate = value => SessionState.CpuTargetBitrate = value,
            GetSpeed = () => SessionState.CpuPreset,
            SetSpeed = value => SessionState.CpuPreset = value,
        };
        #endregion

        #region Public API
        /// <summary>True while the dialog should be drawn.</summary>
        public bool IsVisible => renderDisplay;

        /// <summary>Fired when the dialog is hidden; the addon resets the toolbar button from it (HOST-2).</summary>
        public event Action OnDialogDismissed;

        /// <summary>Shows the dialog.</summary>
        public void Show()
        {
            renderDisplay = true;
            stopRequested = false;
        }

        /// <summary>Hides the dialog and fires <see cref="OnDialogDismissed"/>.</summary>
        public void Hide()
        {
            renderDisplay = false;
            OnDialogDismissed?.Invoke();
        }
        #endregion

        #region Draw
        /// <summary>
        /// Per-frame widget declarations for the whole dialog. Called only from
        /// CinematicUiHost, inside the "Cinematic Recorder" window scope.
        /// Layout per LAYOUT_PROPOSAL §1: status block → FPS combo row (L1) → duration
        /// row → Encoding collapsing header → record button.
        /// </summary>
        internal void Draw()
        {
            DrawStatusSection();

            // L1: one row — Capture combo ‖ Playback combo (greyed stand-in while
            // locked) ‖ Lock toggle. The library has no disabled-widget state, so the
            // locked playback combo is not declared; greyed text stands in (D-U8 entry).
            using (ImGuiEx.Row())
            {
                DrawCaptureFpsCombo();
                DrawPlaybackFpsControl();
                DrawLockToggle();
            }
            // Old Lock semantics: the playback preset follows the capture preset while locked.
            if (SessionState.LockFps)
            {
                SessionState.PlaybackFpsIndex = SessionState.SimFpsIndex;
            }

            DearImGuiKSP.DearImGuiKSP.Text(Settings.SimulatedTimeLabel);
            DrawDurationRow();

            // Default closed, matching the old foldout's initial state.
            if (DearImGuiKSP.DearImGuiKSP.CollapsingHeader(Settings.EncodingHeader))
            {
                DrawEncodingSection();
            }

            DrawRecordAndAdvancedRow();
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

                if (unlimited)
                {
                    DearImGuiKSP.DearImGuiKSP.TextColored(StatusRecordingColor, Settings.UnlimitedRecordingStatus);
                    DearImGuiKSP.DearImGuiKSP.Text(string.Format(Recording.SimulatedUnlimitedFormat,
                        DeterministicCaptureSession.AccumulatedSimulatedSeconds));
                    DearImGuiKSP.DearImGuiKSP.Text(string.Format(Recording.FramesUnlimitedFormat,
                        DeterministicCaptureSession.CapturedFrames));

                    float ratio = CaptureToPlaybackRatio();
                    DearImGuiKSP.DearImGuiKSP.TextColored(FpsGradientColor(ratio),
                        string.Format(Recording.CaptureRateFormat, DeterministicCaptureSession.CaptureFPS));
                }
                else
                {
                    DearImGuiKSP.DearImGuiKSP.TextColored(StatusRecordingColor, Settings.RecordingStatus);
                    DearImGuiKSP.DearImGuiKSP.Text(string.Format(Settings.TimeProgressFormat,
                        DeterministicCaptureSession.AccumulatedSimulatedSeconds, DeterministicCaptureSession.TargetSeconds));
                    DearImGuiKSP.DearImGuiKSP.Text(string.Format(Settings.FramesProgressFormat,
                        DeterministicCaptureSession.CapturedFrames, DeterministicCaptureSession.TargetFrames));

                    float captureFps = DeterministicCaptureSession.CaptureFPS;
                    int framesRemaining = DeterministicCaptureSession.TargetFrames - DeterministicCaptureSession.CapturedFrames;
                    float secondsRemaining = captureFps > 0.1f ? framesRemaining / captureFps : 0f;
                    TimeSpan remaining = TimeSpan.FromSeconds(secondsRemaining);

                    float ratio = CaptureToPlaybackRatio();
                    DearImGuiKSP.DearImGuiKSP.TextColored(FpsGradientColor(ratio),
                        string.Format(Settings.CaptureRatePercentFormat, captureFps, ratio * 100f));
                    DearImGuiKSP.DearImGuiKSP.Text(string.Format(Settings.EstimatedRemainingFormat, remaining));
                }
            }
            else if (stopRequested)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(StatusStoppingColor, Settings.StoppingStatus);
            }
            else
            {
                // Idle: the derived playback-speed readout folds into the status line
                // (LAYOUT_PROPOSAL §1 item 2), replacing the old standalone line.
                int fps = FrameratePresets[SessionState.PlaybackFpsIndex];
                float playbackSpeed = (float)FrameratePresets[SessionState.PlaybackFpsIndex]
                                      / FrameratePresets[SessionState.SimFpsIndex];
                DearImGuiKSP.DearImGuiKSP.Text(string.Format(Settings.ReadyStatusPlaybackFormat,
                    Screen.width, Screen.height, fps, playbackSpeed));
            }
        }

        // Same threshold bands and lerps as the old ApplyFpsColorGradient.
        private static Color32 FpsGradientColor(float ratio)
        {
            Color color;
            if (ratio < 0.10f)
            {
                color = new Color(0.5f, 0f, 0f);
            }
            else if (ratio < 0.30f)
            {
                float t = Mathf.Pow((ratio - 0.10f) / 0.20f, 0.5f);
                color = Color.Lerp(Color.red, new Color(1f, 0.5f, 0f), t);
            }
            else if (ratio < 0.60f)
            {
                float t = Mathf.Pow((ratio - 0.30f) / 0.30f, 2.0f);
                color = Color.Lerp(new Color(1f, 0.5f, 0f), Color.yellow, t);
            }
            else if (ratio < 0.95f)
            {
                float t = (ratio - 0.60f) / 0.35f;
                color = Color.Lerp(Color.yellow, Color.green, t);
            }
            else if (ratio <= 1.05f)
            {
                color = Color.green;
            }
            else if (ratio < 1.25f)
            {
                float t = (ratio - 1.05f) / 0.20f;
                color = Color.Lerp(Color.green, Color.cyan, t);
            }
            else
            {
                color = Color.cyan;
            }
            return color; // implicit Color -> Color32
        }

        private static float CaptureToPlaybackRatio()
        {
            float playbackFps = FrameratePresets[SessionState.PlaybackFpsIndex];
            return playbackFps > 0.1f ? DeterministicCaptureSession.CaptureFPS / playbackFps : 0f;
        }
        #endregion

        #region Capture Settings
        private static void DrawCaptureFpsCombo()
        {
            int simFpsIndex = SessionState.SimFpsIndex;
            if (DearImGuiKSP.DearImGuiKSP.Combo(Settings.CaptureFPS, ref simFpsIndex, FpsComboItems))
            {
                SessionState.SimFpsIndex = simFpsIndex;
            }
        }

        private static void DrawPlaybackFpsControl()
        {
            if (SessionState.LockFps)
            {
                // Locked: the combo is not declared (no disabled state in the library);
                // the effective value shows as greyed text instead.
                DearImGuiKSP.DearImGuiKSP.Text(Settings.PlaybackFPS);
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                    string.Format(Settings.FPSDisplayFormat, FrameratePresets[SessionState.PlaybackFpsIndex]));
                return;
            }

            int playbackFpsIndex = SessionState.PlaybackFpsIndex;
            if (DearImGuiKSP.DearImGuiKSP.Combo(Settings.PlaybackFPS, ref playbackFpsIndex, FpsComboItems))
            {
                SessionState.PlaybackFpsIndex = playbackFpsIndex;
            }
        }

        private static void DrawLockToggle()
        {
            bool lockFps = SessionState.LockFps;
            bool changed;
            if (SessionState.LockFps)
            {
                // Active-state styling: the old green+bold becomes a green Text scope
                // (the library has no per-widget bold).
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(Settings.LockToggle, ref lockFps);
                }
            }
            else
            {
                changed = DearImGuiKSP.DearImGuiKSP.Toggle(Settings.LockToggle, ref lockFps);
            }

            if (changed)
            {
                SessionState.LockFps = lockFps;
            }
        }

        private void DrawDurationRow()
        {
            // The field reseeds from state every frame: invalid text snaps back and 0
            // shows as ∞ — identical to the old text-field display behavior.
            _durationText = SessionState.DurationSeconds <= 0f
                ? Settings.DurationUnlimitedButton
                : SessionState.DurationSeconds.ToString("0.0");

            using (ImGuiEx.Row())
            {
                if (DearImGuiKSP.DearImGuiKSP.Button(Settings.DurationDecrement))
                {
                    SessionState.DurationSeconds = Mathf.Max(0f,
                        SessionState.DurationSeconds - CinematicUIResources.Layout.Duration.STEP);
                }

                if (DearImGuiKSP.DearImGuiKSP.InputText("##duration", ref _durationText, 16)
                    && float.TryParse(_durationText, out float parsed))
                {
                    SessionState.DurationSeconds = Mathf.Clamp(parsed, 0f, 3600f);
                }

                if (DearImGuiKSP.DearImGuiKSP.Button(Settings.DurationIncrement))
                {
                    SessionState.DurationSeconds += CinematicUIResources.Layout.Duration.STEP;
                    if (DeterministicCaptureSession.IsRunning)
                    {
                        DeterministicCaptureSession.ExtendDuration(CinematicUIResources.Layout.Duration.STEP);
                    }
                }

                if (DearImGuiKSP.DearImGuiKSP.Button(Settings.DurationUnlimitedButton))
                {
                    SessionState.DurationSeconds = 0f;
                }
            }
        }
        #endregion

        #region Encoding Settings
        private void DrawEncodingSection()
        {
            SessionState.GpuEncoder detected = SessionState.DetectedGpuEncoder;
            string detectedName =
                detected == SessionState.GpuEncoder.Nvidia ? Settings.DetectedEncoderNvenc :
                detected == SessionState.GpuEncoder.Amd ? Settings.DetectedEncoderAmf :
                Settings.DetectedEncoderCpu;
            DearImGuiKSP.DearImGuiKSP.Text(string.Format(Settings.DetectedEncoderFormat, detectedName));

            bool recording = DeterministicCaptureSession.IsRunning;

            DrawSafeModeControl(recording);

            VendorPanelBindings vendor =
                SessionState.ForceSoftwareEncoding || detected == SessionState.GpuEncoder.None ? CpuBindings :
                detected == SessionState.GpuEncoder.Nvidia ? NvencBindings :
                AmfBindings;
            DrawVendorPanel(vendor, recording);
        }

        private void DrawSafeModeControl(bool recording)
        {
            // Old semantics: Safe Mode cannot change while recording. The library has
            // no disabled-widget state, so the toggle degrades to a greyed read-only
            // stand-in while recording; the inline warning carries the affordance.
            if (recording)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                    SessionState.ForceSoftwareEncoding ? SafeModeGreyedOn : SafeModeGreyedOff);
                DearImGuiKSP.DearImGuiKSP.TextColored(WarningOrangeColor, Settings.SafeModeRecordingWarning);
                return;
            }

            bool safeMode = SessionState.ForceSoftwareEncoding;
            bool changed;
            if (SessionState.ForceSoftwareEncoding)
            {
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(Settings.SafeModeToggle, ref safeMode);
                }
            }
            else
            {
                changed = DearImGuiKSP.DearImGuiKSP.Toggle(Settings.SafeModeToggle, ref safeMode);
            }
            // Replaces the old permanent help label (#007 tooltip).
            DearImGuiKSP.DearImGuiKSP.Tooltip(Settings.SafeModeTooltip);

            if (changed && !DeterministicCaptureSession.IsRunning)
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
        }

        // One parameterized panel for AMF / NVENC / CPU. While recording, every control
        // degrades to a greyed read-only line (contract 1d / D-2 ruling).
        private void DrawVendorPanel(VendorPanelBindings vendor, bool locked)
        {
            int rateControl = vendor.GetRateControl();

            if (locked)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, vendor.Title);
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, vendor.RateControlNames[rateControl]);
                if (rateControl == 0)
                {
                    int qualityValue = vendor.GetQualityValue();
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                        string.Format(vendor.QualityValueFormat, qualityValue, DescribeQuality(qualityValue)));
                }
                else
                {
                    int bitrate = vendor.GetTargetBitrate();
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, Settings.TargetBitrateLabel);
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                        string.Format(Settings.BitrateEstimateFormat, bitrate, (bitrate * 5) / 4));
                }
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, SpeedPresetNames[vendor.GetSpeed()]);
                return;
            }

            DearImGuiKSP.DearImGuiKSP.Text(vendor.Title);

            using (ImGuiEx.Row())
            {
                int selectedRateControl = rateControl;
                DearImGuiKSP.DearImGuiKSP.RadioButton(vendor.RateControlNames[0], ref selectedRateControl, 0);
                DearImGuiKSP.DearImGuiKSP.RadioButton(vendor.RateControlNames[1], ref selectedRateControl, 1);
                vendor.SetRateControl(selectedRateControl);
            }

            if (rateControl == 0)
            {
                using (ImGuiEx.Row())
                {
                    DearImGuiKSP.DearImGuiKSP.Text(Settings.QualityLabel);
                    float qualitySlider = vendor.GetQualitySlider();
                    DearImGuiKSP.DearImGuiKSP.SliderFloat("##quality", ref qualitySlider, 0f, 1f);
                    // The old CQLabel help line, now a tooltip on the slider.
                    DearImGuiKSP.DearImGuiKSP.Tooltip(Settings.CQLabel);
                    vendor.SetQualitySlider(qualitySlider);
                    int qualityValue = vendor.GetQualityValue();
                    DearImGuiKSP.DearImGuiKSP.Text(
                        string.Format(vendor.QualityValueFormat, qualityValue, DescribeQuality(qualityValue)));
                }
            }
            else
            {
                using (ImGuiEx.Row())
                {
                    DearImGuiKSP.DearImGuiKSP.Text(Settings.TargetBitrateLabel);
                    float bitrate = vendor.GetTargetBitrate();
                    DearImGuiKSP.DearImGuiKSP.SliderFloat("##bitrate", ref bitrate, 10f, 200f);
                    // The old VBRLabel help line, now a tooltip on the slider.
                    DearImGuiKSP.DearImGuiKSP.Tooltip(Settings.VBRLabel);
                    int clampedBitrate = Mathf.Clamp((int)bitrate, 10, 200);
                    vendor.SetTargetBitrate(clampedBitrate);
                    DearImGuiKSP.DearImGuiKSP.Text(
                        string.Format(Settings.BitrateEstimateFormat, clampedBitrate, (clampedBitrate * 5) / 4));
                }
            }

            DearImGuiKSP.DearImGuiKSP.Text(Settings.EncodingSpeedLabel);
            using (ImGuiEx.Row())
            {
                int speed = vendor.GetSpeed();
                DearImGuiKSP.DearImGuiKSP.RadioButton(Settings.SpeedPresetSpeed, ref speed, 0);
                DearImGuiKSP.DearImGuiKSP.RadioButton(Settings.SpeedPresetBalanced, ref speed, 1);
                DearImGuiKSP.DearImGuiKSP.RadioButton(Settings.SpeedPresetQuality, ref speed, 2);
                vendor.SetSpeed(speed);
            }
        }

        private static string DescribeQuality(int value)
        {
            return value <= 8 ? Settings.QualityNearLossless :
                   value <= 14 ? Settings.QualityMaster :
                   value <= 20 ? Settings.QualityHigh :
                   Settings.QualityCompressed;
        }
        #endregion

        #region Record & Advanced
        private void DrawRecordAndAdvancedRow()
        {
            bool running = DeterministicCaptureSession.IsRunning;
            using (ImGuiEx.Row())
            {
                // Primary gradient CTA, 40px tall (LAYOUT_PROPOSAL §1 item 5). size.x = 0
                // auto-fits the label width (library sizing rule) — see D-U8 note.
                if (ImGuiGradients.GradientButton(
                    running ? Settings.StopRecording : Settings.StartRecording,
                    new Vector2(0f, 40f), GradientButtonStyle.Primary))
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

                DrawAdvancedButton();
            }
        }

        private static void DrawAdvancedButton()
        {
            bool advancedVisible = CinematicUiHost.Instance != null
                                   && CinematicUiHost.Instance.AdvancedSettings != null
                                   && CinematicUiHost.Instance.AdvancedSettings.IsVisible;
            if (advancedVisible)
            {
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    DrawAdvancedButtonContent(AdvancedButtonShown);
                }
            }
            else
            {
                DrawAdvancedButtonContent(AdvancedButtonHidden);
            }
        }

        private static void DrawAdvancedButtonContent(string label)
        {
            if (DearImGuiKSP.DearImGuiKSP.Button(label))
            {
                CinematicUiHost host = CinematicUiHost.Instance;
                if (host != null && host.AdvancedSettings != null)
                {
                    if (host.AdvancedSettings.IsVisible)
                    {
                        host.AdvancedSettings.Hide();
                    }
                    else
                    {
                        host.AdvancedSettings.Show();
                    }
                }
            }
        }

        private void StartRecording()
        {
            // Close any open report windows from previous recordings. FindObjectOfType
            // stays valid until chunk C6 ports FinalReportWindow off MonoBehaviour (REPORT-1).
            FinalReportWindow existingReport = UnityEngine.Object.FindObjectOfType<FinalReportWindow>();
            if (existingReport != null && existingReport.IsVisible)
            {
                existingReport.HideReport();
            }
            stopRequested = false;
            int simFps = FrameratePresets[SessionState.SimFpsIndex];
            int playbackFps = FrameratePresets[SessionState.PlaybackFpsIndex];
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

        #region Static Helpers
        private static string[] BuildFpsComboItems()
        {
            var items = new string[FrameratePresets.Length];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = string.Format(Settings.FPSDisplayFormat, FrameratePresets[i]);
            }
            return items;
        }
        #endregion
    }
}
