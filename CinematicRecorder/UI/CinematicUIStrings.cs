namespace CinematicRecorder.UI
{
    public static class CinematicUIStrings
    {
        public static class Common
        {
            public const string Okay = "Okay";
            public const string Cancel = "Cancel";
            public const string Yes = "Yes";
            public const string No = "No";

            // Dependency notice (logged by the DearImGui-KSP host at startup)
            public const string DearImGuiKspUnavailableLog = "[CinematicRecorder] DearImGui-KSP not available (not installed or self-disabled); CinematicRecorder UI disabled.";
        }
        public static class ScreenMessages
        {
            public const string EmergencyResetSceneChange = "Cinematic Recorder: Emergency reset due to scene change!";
        }

        public static class Settings
        {
            // Window Chrome & Navigation
            public const string WindowTitle = "Cinematic Recorder";
            public const string MainTab = "Main";
            public const string AdvancedTab = "Advanced";
            public const string StartRecording = "• Start Recording";
            public const string StopRecording = "• Stop Recording";
            public const string DurationDecrement = "-5s";
            public const string DurationIncrement = "+5s";
            public const string DurationUnlimitedButton = "∞";

            // Recording Status & Timing
            public const string RecordingStatus = "• RECORDING";
            public const string UnlimitedRecordingStatus = "• UNLIMITED RECORDING";
            public const string StoppingStatus = "• STOPPING...";
            public const string CaptureFPS = "Capture FPS";
            public const string PlaybackFPS = "Playback FPS";
            public const string FPSDisplayFormat = "{0} FPS";
            public const string LockToggle = "Lock";
            public const string SimulatedTimeLabel = "Simulated Time (seconds)";
            public const string TimeProgressFormat = "{0:F1}s / {1:F1}s";
            public const string FramesProgressFormat = "{0:N0} / {1:N0} frames";
            public const string CaptureRatePercentFormat = "Capture Rate: {0:F1} FPS ({1:F0}%)";
            public const string EstimatedRemainingFormat = "Est. Remaining: {0:mm\\:ss}";

            // Advanced Options
            public const string GradientTooltip = "Reduces color banding in dark areas";
            public const string SafeModeToggle = " Safe Mode (CPU Encoding)";
            public const string SafeModeTooltip = "Forces CPU-based x264 encoding. Use this if you experience issues with the GPU paths.";
            public const string SafeModeRecordingWarning = "Cannot modify while recording";

            // Encoder Configuration
            public const string AMDHEVC = "AMD (HEVC)";
            public const string NvidiaHEVC = "NVIDIA (HEVC)";
            public const string CPUx264 = "CPU (x264)";
            // GPU auto-detection (Phase 3): replaces the manual vendor picker
            public const string DetectedEncoderFormat = "Detected encoder: {0}";
            public const string DetectedEncoderNvenc = "NVENC (NVIDIA)";
            public const string DetectedEncoderAmf = "AMF (AMD)";
            public const string DetectedEncoderCpu = "CPU (no GPU encoder detected)";
            public const string QualityLabel = "Quality Level:";
            public const string RateControlCQP = "Quality(CQ)";
            public const string RateControlVBR = "VBR";
            public const string RateControlCRF = "Quality(CRF)";
            public const string TargetBitrateLabel = "Target Bitrate:";
            public const string BitrateEstimateFormat = "{0} Mbps (~{1} MB per 10s)";
            public const string CQLabel = "File size varies by scene complexity";
            public const string VBRLabel = "Quality adjusts automatically to hit target";
            public const string EncodingSpeedLabel = "Encoding Speed:";
            public const string SpeedPresetSpeed = "Speed";
            public const string SpeedPresetBalanced = "Balanced";
            public const string SpeedPresetQuality = "Quality";

            // Quality Level Formats & Descriptions
            public const string QPFormat = "QP {0} ({1})";
            public const string CRFFormat = "CRF {0} ({1})";
            public const string QualityNearLossless = "Near Lossless";
            public const string QualityMaster = "Master Quality";
            public const string QualityHigh = "High Quality";
            public const string QualityCompressed = "Compressed";

            // Audio Strings
            public const string AudioDisabledScreenMsg = "Audio capture disabled: max 30fps";

            // DearImGui-KSP port additions (chunk C2)
            public const string EncodingHeader = "Encoding";
            public const string ReadyStatusPlaybackFormat = "Ready — {0}x{1} @ {2} FPS — Playback Speed: {3:0.##}×";
            public const string StateOn = "ON";
            public const string StateOff = "OFF";

        }

        public static class Recording
        {
            public const string WindowTitle = "Recording Controls";
            public const string RecordingStopped = "Recording stopped";
            public const string NormalSpeed = "Normal Speed";
            public const string SlowMotionFormat = "{0:F1}× Slow Motion";
            public const string TransitionSlowing = "Slowing...";
            public const string TransitionResuming = "Resuming...";
            public const string KrakenTime = "Kraken-Time";
            public const string SuperSlow = "Super-Slow";
            public const string Slow = "Slow";
            public const string Resume = "Resume";
            public const string DurationHelper = "Playback time for speed transitions (seconds of finished video)";

            // Progress
            public const string SimulatedFormat = "Simulated: {0:F1}s / {1:F1}s";
            public const string SimulatedUnlimitedFormat = "Simulated: {0:F1}s elapsed";
            public const string FramesUnlimitedFormat = "Frames: {0:N0}";
            public const string CaptureRateFormat = "Capture Rate: {0:F1} FPS";

            // DearImGui-KSP port additions (chunk C5)
            public const string SpeedRampsHeader = "Speed Ramps";
            public const string StatusTransitionFormat = "{0} — Transition: {1}";
            public const string RampBiasLabel = "Bias";
            public const string RampBiasTooltip = "← Linger Slow — Linger Normal →";
        }

        public static class AdvancedSettings
        {
            // C10: the floating window is gone (L2 superseded) — this section's content
            // renders as the main window's "Advanced" tab, grouped under these headers.
            public const string CaptureHeader = "Capture";
            public const string TemporalAccumulationHeader = "Temporal Accumulation";
            public const string PostProcessingHeader = "Post-Processing";

            // Encoding tab
            public const string AudioCaptureToggle = " Enable Audio Capture";
            public const string AudioCaptureTooltip = "Records synchronized WAV audio alongside video. Does not work above 30fps capture rates.";
            public const string PngSequenceToggle = " PNG Sequence Mode";
            public const string PngSequenceTooltip = "Outputs individual PNG frames. Forces software encoding and disables hardware acceleration.";
            public const string CaptureUiToggle = " Capture UI Layer";
            public const string CaptureUiTooltip = "Includes the game UI layer in the recorded video. Applies at capture start; cannot be changed while recording.";
            public const string CaptureUiUnavailableTooltip = "Unavailable while Temporal Accumulation Blur is on.";
            public const string TemporalAccumulationUnavailableTooltip = "Unavailable while Capture UI Layer is on.";

            // Rendering tab
            public const string TemporalAccumulationToggle = " Temporal Accumulation Blur";
            public const string TemporalAccumulationTooltip = "Simulates motion blur by accumulating multiple sub-frames per output frame. Only available with GPU encoding.";
            public const string TabGpuRequiredWarning = "Temporal Accumulation requires a GPU encoder (AMD or NVIDIA).";
            public const string GradientProtectionToggle = " Gradient Protection";
            public const string AMFOnlyWarning = "Advanced options require AMD encoder.";

            // Sharpening (only when TAB enabled)
            public const string SharpeningToggle = " Sharpening";
            public const string SharpeningTooltip = "Applies contrast-adaptive sharpening to counteract TAB softness.";
            public const string SharpeningStrengthLabel = "Sharpness: {0:F0}%";
        }

        public static class CameraController
        {
            public const string RequiresHullCam = "Camera Panel requires HullCam VDS";
            public const string FadeOnSwapToggle = " Fade-On-Swap";
            public const string FadeDurationValueFormat = "{0:F2}s";
            public const string ButtonIdFormat = "Cam_{0}";
            public const string ControlsHeader = "Controls:";
            public const string ControlLeftClick = "• Left-click camera to view";
            public const string ControlAssignCurrent = "• 'Assign Current' binds active cam to first open slot";
            public const string ReturnToMain = "Return to Main";
            public const string AssignCurrent = "Assign Current";
            public const string ZoomOut = "Out";
            public const string ZoomIn = "In";
            public const string FOVFormat = "FOV: {0:F1}° / {1:F0}°";
            public const string ResetZoom = "Reset Zoom";
            public const string SavePreset = "Save";
            public const string DeletePreset = "Delete";
            public const string LoadPreset = "Load";
            public const string ConfirmDeleteTitle = "Confirm Delete";
            public const string ConfirmUnassignTitle = "Confirm Unassign";
            public const string UnassignConfirmFormat = "Unassign camera from slot {0}?";
            public const string CameraUnavailable = "Camera unavailable (vessel may be unloaded)";
            public const string NoCameraToAssign = "No active HullCam to assign";
            public const string DeleteConfirmFormat = "Delete preset '{0}'?";
            public const string GetPartFromCameraFail = "[CamPanel] GetPartFromCamera failed: ";
            public const string Preset = "Preset";
            public const string PresetCopySuffix = " (Copy)";
            public const string SavedCameraToolsFormat = "Saved CameraTools: {0}";
            public const string SavedHullCamFormat = "Saved HullCam: {0}";
            public const string AutoZoomHeader = "Auto-Zoom";
            public const string ConsistentFramingToggle = " Consistent Framing";
            public const string PaddingTooltip = "0.5x = tight fit, 1.5x = normal, 3.0x = wide";
            public const string CurrentFOVFormat = "Current FOV: {0:F1}°";

            public const string RateModeToggle = "Rate Mode";
            public const string TargetModeToggle = "Target Mode";
            public const string TargetFOVLabel = "Target FOV:";
            public const string CurveLabel = "Curve:";
            public const string GoButton = "Go";
            public const string CurveLinear = "Linear";
            public const string CurveEaseIn = "Ease In";
            public const string CurveEaseOut = "Ease Out";
            public const string CurveEaseInOut = "Ease In/Out";
            public const string TargetConsistentFramingToggle = " Target Consistent Framing";

            public const string OverwriteConfirm = "Overwrite existing preset '{0}'?";
            public const string OverwriteYes = "Overwrite";
            public const string OverwriteNo = "Create New";
            public const string ConfirmOverwriteTitle = "Confirm Overwrite";
            public const string IvaZoomDisabledNotice = "Camera controls are disabled while in IVA";

            // DearImGui-KSP port additions (chunk C8) — CollapsingHeader labels, the
            // L5 unassign affordance, zoom-row labels/values, and tooltips per
            // LAYOUT_PROPOSAL §4.
            public const string CameraPanelHeader = "Camera Panel";
            public const string ZoomHeader = "Zoom";
            public const string SlotLabelFormat = "Slot {0}: {1}";
            public const string UnassignButton = "× Unassign";
            public const string PaddingRowLabel = "Padding";
            public const string PaddingValueFormat = "{0:F1}x";
            public const string ZoomDurationLabel = "Duration";
            public const string DurationValueFormat = "{0:F1}s";
            public const string ZoomDurationRangeTooltip = "0.0 — 5.0";

            // Relocated verbatim from inline literals in the IMGUI panel (invariant 3).
            public const string InvalidPathIndexMessage = "Cannot activate - invalid path index";
            public const string SavedPathNoLongerExistsMessage = "Saved path no longer exists";
            public const string NoPathSelectedMessage = "Cannot save: No path selected in CameraTools";
        }

        public static class Report
        {
            public const string WindowTitle = "Recording Complete";
            public const string SummaryHeader = "Capture Summary";
            public const string FramesCaptured = "Frames Captured:";
            public const string SimulatedTime = "Simulated Time:";
            public const string OutputDuration = "Output Duration:";
            public const string OutputDurationUnlimited = "Output Duration (Unlimited):";
            public const string RealCaptureTime = "Real Capture Time:";
            public const string EncodingMode = "Encoding Mode:";
            public const string FilenameLabel = "Filename:";
            public const string AudioFileLabel = "Audio File:";
            public const string OpenFolder = "Open Folder";
            public const string SecondsUnit = " sec";
            public const string FolderNotFound = "Folder not found";
            public const string FailedToOpenFolder = "Failed to open folder";
            public const string FinalReportLog = "[CinematicRecorder] Final Report - Frames: {0}, SimTime: {1:F1}s, RealTime: {2:F1}s, Mode: {3}, Unlimited: {4}, File: {5}";
            public const string OpeningFolderLog = "[CinematicRecorder] Opening folder: {0}";
            public const string CannotOpenFolderLog = "[CinematicRecorder] Cannot open folder, directory not found: {0}";
            public const string FailedToOpenFolderLog = "[CinematicRecorder] Failed to open folder: {0}";
            public const string MuxAudioButton = "Mux Audio";
            public const string MuxingInProgress = "Muxing...";
            public const string MuxingComplete = "Muxing complete!";
            public const string MuxedButton = "Muxed!";
            public const string SessionEndWatchdogHint = "Session ends 30s after this report.";
            public const string PngSequenceSuffix = " (PNG Sequence)";
        }

        public static class CurveDescriptions
        {
            public const string Linear = "Linear transition";
            public const string LingerNormalRushSlow = "Linger at normal speed, rush through slow-motion";
            public const string GradualEntryFastExit = "Gradual entry, fast exit to slow-mo";
            public const string SnapToSlow = "Snap to slow-mo, linger there";
            public const string FastEntryGentleExit = "Fast entry, gentle exit";
            public const string Moderate = "Moderate curve";
        }
    }
}
