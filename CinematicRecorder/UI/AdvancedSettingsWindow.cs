// File: AdvancedSettingsWindow.cs
using CinematicRecorder.Core;
using DearImGuiKSP;
using DearImGuiKSP.Application;
using UnityEngine;
using static CinematicRecorder.UI.CinematicUIStrings;

namespace CinematicRecorder.UI
{
    /// <summary>
    /// Advanced settings content — audio capture, UI-layer capture, PNG sequence, temporal
    /// accumulation blur (TAB), gradient protection, and sharpening. DearImGui-KSP VIEW-1
    /// port (chunk C3) of the IMGUI MonoBehaviour; the port pattern follows SettingsDialog
    /// (chunk C2). C10 (note 6, supersedes L2): no longer a floating window — the content
    /// renders as the "Advanced" tab of the main settings window, grouped into default-open
    /// CollapsingHeaders. Permanent help labels became tooltips on their toggles; conditional
    /// warnings stay inline colored text. Drawn from SettingsDialog inside the
    /// "Cinematic Recorder" window scope.
    /// </summary>
    public class AdvancedSettingsWindow
    {
        #region Constants & Static State
        // Warning color as bytes for TextColored (was Colors.INFO_ORANGE via Styles.Info() —
        // the TAB GPU-required, AMF-only, and Capture-UI conflict lines all used it).
        private static readonly Color32 WarningOrangeColor = new Color32(255, 140, 0, 255);

        // Grey used for every read-only stand-in (theme-disabled text slot).
        private static readonly Color32 GreyedColor = KspPalette.TextLightGrey;

        // Greyed-stand-in labels, composed once from consts (D-2 ruling: a stand-in for
        // every non-interactive state — while recording and while a toggle's conditions
        // are unmet; no per-frame composition).
        private static readonly string AudioGreyedOn = AdvancedSettings.AudioCaptureToggle + " " + Settings.StateOn;
        private static readonly string AudioGreyedOff = AdvancedSettings.AudioCaptureToggle + " " + Settings.StateOff;
        private static readonly string CaptureUiGreyedOn = AdvancedSettings.CaptureUiToggle + " " + Settings.StateOn;
        private static readonly string CaptureUiGreyedOff = AdvancedSettings.CaptureUiToggle + " " + Settings.StateOff;
        private static readonly string PngGreyedOn = AdvancedSettings.PngSequenceToggle + " " + Settings.StateOn;
        private static readonly string PngGreyedOff = AdvancedSettings.PngSequenceToggle + " " + Settings.StateOff;
        private static readonly string TabGreyedOn = AdvancedSettings.TemporalAccumulationToggle + " " + Settings.StateOn;
        private static readonly string TabGreyedOff = AdvancedSettings.TemporalAccumulationToggle + " " + Settings.StateOff;
        private static readonly string GradientGreyedOn = AdvancedSettings.GradientProtectionToggle + " " + Settings.StateOn;
        private static readonly string GradientGreyedOff = AdvancedSettings.GradientProtectionToggle + " " + Settings.StateOff;
        private static readonly string SharpeningGreyedOn = AdvancedSettings.SharpeningToggle + " " + Settings.StateOn;
        private static readonly string SharpeningGreyedOff = AdvancedSettings.SharpeningToggle + " " + Settings.StateOff;
        #endregion

        #region Draw
        /// <summary>
        /// Per-frame widget declarations for the advanced content. Called only from
        /// SettingsDialog's "Advanced" tab (C10 note 6). Three default-open
        /// CollapsingHeader groups (note 6 grouping): Capture — Audio, Capture UI Layer,
        /// PNG Sequence; Temporal Accumulation — TAB, Gradient Protection;
        /// Post-Processing — Sharpening (+ strength while on).
        /// </summary>
        internal static void Draw()
        {
            if (DearImGuiKSP.DearImGuiKSP.CollapsingHeader(
                AdvancedSettings.CaptureHeader, defaultOpen: true))
            {
                DrawAudioCaptureToggle();
                DrawCaptureUiLayerToggle();
                DrawPngSequenceToggle();
            }

            if (DearImGuiKSP.DearImGuiKSP.CollapsingHeader(
                AdvancedSettings.TemporalAccumulationHeader, defaultOpen: true))
            {
                DrawTemporalAccumulationToggle();
                DrawGradientProtectionToggle();
            }

            if (DearImGuiKSP.DearImGuiKSP.CollapsingHeader(
                AdvancedSettings.PostProcessingHeader, defaultOpen: true))
            {
                DrawSharpeningToggle();
            }
        }

        private static void DrawAudioCaptureToggle()
        {
            if (DeterministicCaptureSession.IsRunning)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                    SessionState.EnableAudioCapture ? AudioGreyedOn : AudioGreyedOff);
                return;
            }

            bool enableAudio = SessionState.EnableAudioCapture;
            bool changed;
            if (SessionState.EnableAudioCapture)
            {
                // Active-state styling: the old green+bold becomes a green Text scope
                // (the library has no per-widget bold).
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.AudioCaptureToggle, ref enableAudio);
                }
            }
            else
            {
                changed = DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.AudioCaptureToggle, ref enableAudio);
            }
            // Replaces the old permanent help label.
            DearImGuiKSP.DearImGuiKSP.Tooltip(AdvancedSettings.AudioCaptureTooltip);

            if (changed && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.EnableAudioCapture = enableAudio;
                UnityEngine.Debug.Log($"[CinematicRecorder] EnableAudioCapture = {enableAudio}");
            }
        }

        private static void DrawCaptureUiLayerToggle()
        {
            bool blockedByTab = SessionState.EnableTemporalAccumulation;

            // Note 8: TAB is on — the toggle renders as a real disabled widget whose
            // tooltip explains the conflict (replaces the greyed stand-in + orange
            // conflict line for this widget pair). The recording-state stand-in keeps
            // precedence while a recording is running.
            if (blockedByTab && !DeterministicCaptureSession.IsRunning)
            {
                using (ImGuiEx.Disabled(true))
                {
                    bool captureUi = SessionState.CaptureUiLayer;
                    DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.CaptureUiToggle, ref captureUi);
                    DearImGuiKSP.DearImGuiKSP.Tooltip(AdvancedSettings.CaptureUiUnavailableTooltip);
                }
                return;
            }

            if (DeterministicCaptureSession.IsRunning)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                    SessionState.CaptureUiLayer ? CaptureUiGreyedOn : CaptureUiGreyedOff);
                return;
            }

            bool captureUiLive = SessionState.CaptureUiLayer;
            bool changed;
            if (SessionState.CaptureUiLayer)
            {
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.CaptureUiToggle, ref captureUiLive);
                }
            }
            else
            {
                changed = DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.CaptureUiToggle, ref captureUiLive);
            }
            DearImGuiKSP.DearImGuiKSP.Tooltip(AdvancedSettings.CaptureUiTooltip);

            if (changed && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.CaptureUiLayer = captureUiLive;
                UnityEngine.Debug.Log($"[CinematicRecorder] CaptureUiLayer = {captureUiLive}");
            }
        }

        private static void DrawPngSequenceToggle()
        {
            if (DeterministicCaptureSession.IsRunning)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                    SessionState.PngSequence ? PngGreyedOn : PngGreyedOff);
                return;
            }

            bool pngSequence = SessionState.PngSequence;
            bool changed;
            if (SessionState.PngSequence)
            {
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.PngSequenceToggle, ref pngSequence);
                }
            }
            else
            {
                changed = DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.PngSequenceToggle, ref pngSequence);
            }
            DearImGuiKSP.DearImGuiKSP.Tooltip(AdvancedSettings.PngSequenceTooltip);

            if (changed && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.PngSequence = pngSequence;
                if (pngSequence)
                {
                    // Force software encoding when PNG mode is enabled
                    SessionState.ForceSoftwareEncoding = true;
                    UnityEngine.Debug.Log("[CinematicRecorder] PNG Sequence enabled - forcing software encoding path");

                    // Also disable TAB since PNG uses the CPU path
                    if (SessionState.EnableTemporalAccumulation)
                    {
                        SessionState.EnableTemporalAccumulation = false;
                        UnityEngine.Debug.Log("[CinematicRecorder] TAB disabled due to PNG sequence mode");
                    }
                }
            }
        }

        private static void DrawTemporalAccumulationToggle()
        {
            // TAB requires the GPU zero-copy path; supported on both AMF (AMD) and NVENC (Nvidia)
            bool hasGpuEncoder = SessionState.DetectedGpuEncoder == SessionState.GpuEncoder.Amd
                              || SessionState.DetectedGpuEncoder == SessionState.GpuEncoder.Nvidia;
            bool blockedByCaptureUi = SessionState.CaptureUiLayer;
            bool canUseTab = hasGpuEncoder && !SessionState.PngSequence && !SessionState.ForceSoftwareEncoding
                          && !blockedByCaptureUi;

            // Note 8: Capture UI Layer is on — the toggle renders as a real disabled
            // widget whose tooltip explains the conflict (replaces the greyed stand-in
            // + orange conflict line for this widget pair). The recording-state and
            // off-limits stand-ins below keep precedence otherwise.
            if (blockedByCaptureUi && !DeterministicCaptureSession.IsRunning)
            {
                using (ImGuiEx.Disabled(true))
                {
                    bool tabDisabled = SessionState.EnableTemporalAccumulation;
                    DearImGuiKSP.DearImGuiKSP.Toggle(
                        AdvancedSettings.TemporalAccumulationToggle, ref tabDisabled);
                    DearImGuiKSP.DearImGuiKSP.Tooltip(AdvancedSettings.TemporalAccumulationUnavailableTooltip);
                }
            }
            else
            {
                bool tab = SessionState.EnableTemporalAccumulation;
                if (!canUseTab || DeterministicCaptureSession.IsRunning)
                {
                    DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor, tab ? TabGreyedOn : TabGreyedOff);
                }
                else
                {
                    bool newValue = tab;
                    if (tab)
                    {
                        using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                        {
                            DearImGuiKSP.DearImGuiKSP.Toggle(
                                AdvancedSettings.TemporalAccumulationToggle, ref newValue);
                        }
                    }
                    else
                    {
                        DearImGuiKSP.DearImGuiKSP.Toggle(
                            AdvancedSettings.TemporalAccumulationToggle, ref newValue);
                    }
                    // Replaces the old permanent help label (shown when a GPU encoder is present).
                    DearImGuiKSP.DearImGuiKSP.Tooltip(AdvancedSettings.TemporalAccumulationTooltip);

                    // TAB only sticks when actually usable
                    SessionState.EnableTemporalAccumulation = newValue && canUseTab;
                }
            }

            // Conditional warning (never a tooltip): no GPU encoder — explains the stand-in.
            if (SessionState.DetectedGpuEncoder == SessionState.GpuEncoder.None)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(WarningOrangeColor, AdvancedSettings.TabGpuRequiredWarning);
            }
        }

        private static void DrawGradientProtectionToggle()
        {
            // Gradient protection only available for the AMD encoder
            bool canUseGradient = SessionState.DetectedGpuEncoder == SessionState.GpuEncoder.Amd;

            bool gradient = SessionState.AmfUseBlueNoiseDither;
            if (!canUseGradient || DeterministicCaptureSession.IsRunning)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                    gradient ? GradientGreyedOn : GradientGreyedOff);
            }
            else
            {
                bool newValue = gradient;
                bool changed;
                if (gradient)
                {
                    using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                    {
                        changed = DearImGuiKSP.DearImGuiKSP.Toggle(
                            AdvancedSettings.GradientProtectionToggle, ref newValue);
                    }
                }
                else
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(
                        AdvancedSettings.GradientProtectionToggle, ref newValue);
                }
                // The old Settings.GradientTooltip help line, now a tooltip on the toggle.
                DearImGuiKSP.DearImGuiKSP.Tooltip(Settings.GradientTooltip);

                if (canUseGradient && changed && !DeterministicCaptureSession.IsRunning)
                {
                    SessionState.AmfUseBlueNoiseDither = newValue;
                }
            }

            // Conditional warning (never a tooltip): explains why the toggle is a stand-in.
            if (!canUseGradient)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(WarningOrangeColor, AdvancedSettings.AMFOnlyWarning);
            }
        }

        private static void DrawSharpeningToggle()
        {
            // Sharpening is only available while TAB is enabled (parity with the old
            // Rendering tab, which hid the whole section otherwise).
            if (!SessionState.EnableTemporalAccumulation)
            {
                return;
            }

            if (DeterministicCaptureSession.IsRunning)
            {
                DearImGuiKSP.DearImGuiKSP.TextColored(GreyedColor,
                    SessionState.TabEnableSharpening ? SharpeningGreyedOn : SharpeningGreyedOff);
                return;
            }

            bool sharpening = SessionState.TabEnableSharpening;
            bool changed;
            if (SessionState.TabEnableSharpening)
            {
                using (ImGuiEx.StyleColor(ImGuiCol.Text, KspPalette.GreenLight))
                {
                    changed = DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.SharpeningToggle, ref sharpening);
                }
            }
            else
            {
                changed = DearImGuiKSP.DearImGuiKSP.Toggle(AdvancedSettings.SharpeningToggle, ref sharpening);
            }
            DearImGuiKSP.DearImGuiKSP.Tooltip(AdvancedSettings.SharpeningTooltip);

            if (changed && !DeterministicCaptureSession.IsRunning)
            {
                SessionState.TabEnableSharpening = sharpening;
            }

            // Strength slider (only if sharpening enabled)
            if (SessionState.TabEnableSharpening)
            {
                float strengthPercent = SessionState.TabSharpeningStrength * 100f;
                DearImGuiKSP.DearImGuiKSP.Text(
                    string.Format(AdvancedSettings.SharpeningStrengthLabel, strengthPercent));

                float strength = SessionState.TabSharpeningStrength;
                if (DearImGuiKSP.DearImGuiKSP.SliderFloat("##sharpening", ref strength, 0f, 1f))
                {
                    SessionState.TabSharpeningStrength = strength;
                }
            }
        }
        #endregion
    }
}
