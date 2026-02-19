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
            public const string arrowL = "◀";
            public const string arrowR = "▶";
            public const string arrowUp = "▲";
            public const string arrowDn = "▼";
            public const string DeleteConfirm = "Delete preset '{0}'?";
        }
        public static class ScreenMessages
        {
            public const string EmergencyResetSceneChange = "Cinematic Recorder: Emergency reset due to scene change!";
        }

        public static class Settings
        {
            // Window Chrome & Navigation
            public const string WindowTitle = "Cinematic Recorder";
            public const string AdvancedButton = "Advanced";
            public const string HideEncoding = "▲ Hide Encoding";
            public const string ShowEncoding = "▼ Show Encoding";
            public const string StartRecording = "● Start Recording";
            public const string StopRecording = "■ Stop Recording";
            public const string DurationDecrement = "-5s";
            public const string DurationIncrement = "+5s";

            // Recording Status & Timing
            public const string RecordingStatus = "● RECORDING";
            public const string UnlimitedRecordingStatus = "● UNLIMITED RECORDING";
            public const string StoppingStatus = "■ STOPPING...";
            public const string ReadyStatusFormat = "Ready — {0}x{1} @ {2} FPS";
            public const string CaptureFPS = "Capture FPS";
            public const string PlaybackFPS = "Playback FPS";
            public const string FPSDisplayFormat = "{0} FPS";
            public const string LockToggle = "Lock";
            public const string PlaybackSpeedFormat = "Playback Speed: {0:0.##}×";
            public const string SimulatedTimeLabel = "Simulated Time (seconds)";
            public const string TimeProgressFormat = "{0:F1}s / {1:F1}s";
            public const string FramesProgressFormat = "{0:N0} / {1:N0} frames";
            public const string ElapsedTimeFormat = "{0:F1}s elapsed";
            public const string FramesCountFormat = "{0:N0} frames";
            public const string CaptureRatePercentFormat = "Capture Rate: {0:F1} FPS ({1:F0}%)";
            public const string EstimatedRemainingFormat = "Est. Remaining: {0:mm\\:ss}";

            // Advanced Options
            public const string AdvancedOptionsHeader = "Advanced Options";
            public const string GradientProtection = "Gradient Protection";
            public const string GradientTooltip = "Reduces color banding in dark areas";
            public const string AMFOnlyWarning = "Advanced options require AMD encoder.";
            public const string PostProcessText = "(Post-processing effects will appear here)";
            public const string SafeModeToggle = " Safe Mode (CPU Encoding)";
            public const string SafeModeTooltip = "Forces CPU-based x264 encoding. Use this if you experience issues with the GPU paths.";
            public const string SafeModeRecordingWarning = "⚠ Cannot modify while recording";

            // Encoder Configuration
            public const string EncoderTitle = "Encoder";
            public const string EncoderAMD = "AMD";
            public const string EncoderNVIDIA = "NVIDIA";
            public const string EncoderCPU = "CPU";
            public const string AMDHEVC = "AMD (HEVC)";
            public const string NvidiaHEVC = "NVIDIA (HEVC)";
            public const string CPUx264 = "CPU (x264)";
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
            public const string CQFormat = "CQ {0} ({1})";
            public const string CRFFormat = "CRF {0} ({1})";
            public const string QualityNearLossless = "Near Lossless";
            public const string QualityMaster = "Master Quality";
            public const string QualityHigh = "High Quality";
            public const string QualityCompressed = "Compressed";

            // PNG Sequence Strings
            public const string PngSequenceToggle = " PNG Sequence";
            public const string PngSequenceTooltip = "Output individual PNG frames. Forces software encoding and disables hardware acceleration.";
            public const string PngSequenceCompleteLog = "[CinematicRecorder] PNG Sequence complete: {0} frames written to {1}";

            // Audio Strings
            public const string EnableAudioLabel = " Enable Audio Capture";
            public const string AudioTooltip = "Records synchronized WAV audio alongside video. Does not work above 48fps capture rates.";
            public const string AudioDisabledScreenMsg = "Audio capture disabled: max 48fps";

        }

        public static class Recording
        {
            public const string WindowTitle = "Recording Controls";
            public const string RecordingStopped = "Recording stopped";
            public const string NormalSpeed = "Normal Speed";
            public const string SlowMotionFormat = "{0:F1}× Slow Motion";
            public const string TransitionSlowing = "Slowing...";
            public const string TransitionResuming = "Resuming...";
            public const string TransitionLabelFormat = "Transition: {0}";
            public const string KrakenTime = "Kraken-Time";
            public const string SuperSlow = "Super-Slow";
            public const string Slow = "Slow";
            public const string Resume = "Resume";
            public const string SpeedRampsExpand = "▼ Speed Ramps";
            public const string SpeedRampsCollapse = "▲ Speed Ramps";
            public const string DurationFormat = "Duration: {0:F2}s";
            public const string DurationHelper = "Wall-clock time for speed transitions";
            public const string BiasFormat = "Bias: {0:F2}";
            public const string LingerSlow = "Linger Slow";
            public const string LingerNormal = "Linger Normal";

            // Progress
            public const string SimulatedFormat = "Simulated: {0:F1}s / {1:F1}s";
            public const string SimulatedUnlimitedFormat = "Simulated: {0:F1}s elapsed";
            public const string FramesFormat = "Frames: {0:N0}";
            public const string FramesUnlimitedFormat = "Frames: {0:N0}";
            public const string CaptureRateFormat = "Capture Rate: {0:F1} FPS";
            public const string CaptureRatePercentFormat = "Capture Rate: {0:F1} FPS ({1:F0}%)";
            public const string EstimatedRemainingFormat = "Est. Remaining: {0:mm\\:ss}";

            // Advanced Camera Settings Panel
            public const string AdvancedCameraButton = "Adv. Camera";
            public const string AdvancedCameraHeader = "Advanced Camera Settings";
        }

        public static class AdvancedCameraOptions
        {
            // Window Title
            public const string WindowTitle = "Advanced Camera";

            // Camera Path Playback Timing
            public const string PathPlaybackTimingToggle = " Use playback timing";
            public const string PathPlaybackTimingTooltip = "Keeps path timing constant during slow-mo";

            // Camera Shake Section
            public const string ShakeHeader = "Camera Shake";
            public const string ShakeToggle = " Shake";
            public const string VelocityShakeToggle = " Velocity Shake";
            public const string ShakeIntensityLabel = "Intensity: {0:F1}";
            public const string ShakeTooltip = "Standard positional camera shake";
            public const string VelocityShakeTooltip = "Velocity-based directional shake (requires velocity data)";
            public const string IntensityTooltip = "Shake amplitude multiplier (0 = off, 10 = maximum)";

            // HullCam Overlay Section  
            public const string OverlayHeader = "HullCam Overlay";
            public const string OverlaySelectorLabel = "Overlay:";
            public const string OverlayPlaceholder = "None Available";
            public const string OverlayTooltip = "Requires HullCam API extension to enumerate overlays";

            // Serialization Info
            public const string SettingsPersisted = "Settings will persist with preset";
            public const string NoCameraToolsSlot = "Select a CameraTools slot to configure options";
        }

        public static class CameraController
        {
            public const string FoldoutExpand = "▼ Camera Panel";
            public const string FoldoutCollapse = "▲ Camera Panel";
            public const string RequiresHullCam = "Camera Panel requires HullCam VDS";
            public const string FadeOnSwapToggle = " Fade-On-Swap";
            public const string FadeDurationFormat = "Fade Duration: {0:F2}s";
            public const string ButtonIdFormat = "Cam_{0}";
            public const string ControlsHeader = "Controls:";
            public const string ControlLeftClick = "• Left-click camera to view";
            public const string ControlRightClick = "• Right-click to unassign";
            public const string ControlAssignCurrent = "• 'Assign Current' binds active cam to first open slot";
            public const string ReturnToMain = "Return to Main";
            public const string AssignCurrent = "Assign Current";
            public const string ZoomControlLabel = "Zoom Control (Velocity)";
            public const string ZoomOut = "Out";
            public const string ZoomIn = "In";
            public const string FOVFormat = "FOV: {0:F1}° / {1:F0}°";
            public const string ResetZoom = "Reset Zoom";
            public const string AutoDistanceToggle = " Auto-Distance";
            public const string AutoDistanceTooltip = "Automatically adjusts zoom based on vessel distance";
            public const string SavePreset = "Save";
            public const string DeletePreset = "Delete";
            public const string LoadPreset = "Load ▼";
            public const string ConfirmDeleteTitle = "Confirm Delete";
            public const string ConfirmUnassignTitle = "Confirm Unassign";
            public const string UnassignConfirmFormat = "Unassign camera from slot {0}?";
            public const string CameraUnavailable = "Camera unavailable (vessel may be unloaded)";
            public const string NoCameraToAssign = "No active HullCam to assign";
            public const string DeleteConfirmFormat = "Delete preset '{0}'?";
            public const string GetPartFromCameraFail = "[CamPanel] GetPartFromCamera failed: ";
            public const string Preset = "Preset";
            public const string PresetNameUniqueFormat = "{0} [{1}]";
            public const string PresetCopySuffix = " (Copy)";
            public const string SavedCameraToolsFormat = "Saved CameraTools: {0}";
            public const string SavedHullCamFormat = "Saved HullCam: {0}";
            public const string CTModeDogfight = "Dogfight";
            public const string CTModeStationary = "Stationary";
            public const string CTModePathing = "Pathing";
            public const string CTDisplayDogfightFree = "Dogfight (Free)";
            public const string CTDisplayDogfightTarget = "Dogfight (Target)";
            public const string AutoZoomHeader = "Auto-Zoom";
            public const string ConsistentFramingToggle = " Consistent Framing";
            public const string PaddingLabel = "Padding: {0:F1}x";
            public const string PaddingTooltip = "0.5x = tight fit, 1.5x = normal, 3.0x = wide";
            public const string CurrentFOVFormat = "Current FOV: {0:F1}°";

            public const string RateModeToggle = "Rate Mode";
            public const string TargetModeToggle = "Target Mode";
            public const string TargetFOVLabel = "Target FOV:";
            public const string ApproachRateLabel = "Approach Rate:";
            public const string CurveLabel = "Curve:";
            public const string GoButton = "Go";
            public const string CurveLinear = "Linear";
            public const string CurveEaseIn = "Ease In";
            public const string CurveEaseOut = "Ease Out";
            public const string CurveEaseInOut = "Ease In/Out";
            public const string TargetConsistentFramingToggle = " Target Consistent Framing";
            public const string DurationLabel = "Duration: {0:F1}s";

            public const string OverwriteConfirm = "Overwrite existing preset '{0}'?";
            public const string OverwriteYes = "Overwrite";
            public const string OverwriteNo = "Create New";
            public const string ConfirmOverwriteTitle = "Confirm Overwrite";
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
            public const string FfmpegNotFound = "FFmpeg not found";
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