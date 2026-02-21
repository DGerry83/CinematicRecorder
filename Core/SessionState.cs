using System;
using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Persists settings for the entire game session (until KSP is closed).
    /// Scene switches do NOT reset these values.
    /// </summary>
    public static class SessionState
    {

        #region Capture Timing
        public static int SimFpsIndex { get; set; } = 2;        // Default 60
        public static int PlaybackFpsIndex { get; set; } = 2;
        public static bool LockFps { get; set; } = true;
        public static float DurationSeconds { get; set; } = 10f;
        public static bool ForceSoftwareEncoding { get; set; } = false;
        public static bool PngSequence { get; set; } = false;
        public static bool EnableAudioCapture { get; set; } = false;
        #endregion

        #region Encoder Selection
        /// <summary>
        /// Selected encoder: 0=AMF (AMD HEVC), 1=NVENC (NVIDIA HEVC), 2=CPU (x264)
        /// </summary>
        public static int SelectedEncoderTab { get; set; } = 0;
        #endregion

        #region AMF Settings
        public static float AmfQualitySlider { get; set; } = 0.5f; // Quality ↔ File Size slider (0–1)

        /// <summary>
        ///  0 = CQP, 1 = VBR, 2 = CBR
        /// </summary>
        public static int AmfRateControlMode { get; set; } = 0;
        public static int AmfTargetBitrate { get; set; } = 80; // Mbps (used for VBR / CBR)
        /// <summary>
        /// 0 = Fast, 1 = Balanced, 2 = Quality
        /// </summary>
        public static int AmfEncoderSpeed { get; set; } = 2;
        public static bool AmfShowAdvanced { get; set; } = false;
        public static bool AmfUseBlueNoiseDither { get; set; } = true; // Default ON for quality

        /// <summary>
        /// Calculates QP value (0-28) from quality slider. Lower is better quality.
        /// </summary>
        public static int AmfCqpValue => Mathf.RoundToInt(Mathf.Lerp(24f, 0f, AmfQualitySlider));
        #endregion

        #region NVENC Settings
        public static float NvencQualitySlider { get; set; } = 0.5f;
        /// <summary>
        /// 0 = CQ, 1 = VBR, 2 = CBR
        /// </summary>
        public static int NvencRateControlMode { get; set; } = 0;
        public static int NvencTargetBitrate { get; set; } = 80; // Mbps
        /// <summary>
        /// Encoder preset 0 = Fast, 1 = Balanced, 2 = Quality
        /// </summary>
        public static int NvencPreset { get; set; } = 2;
        public static bool NvencShowAdvanced { get; set; } = false;
        /// <summary>
        /// Calculates NVENC CQ value (0-28) from quality slider. Lower is better quality.
        /// </summary>
        public static int NvencCqValue => Mathf.RoundToInt(Mathf.Lerp(24f, 0f, NvencQualitySlider));
        #endregion

        #region CPU Settings  
        public static float CpuQualitySlider { get; set; } = 0.5f;
        /// <summary>
        /// 0 = CRF, 1 = VBR, 2 = CBR
        /// </summary>
        public static int CpuRateControlMode { get; set; } = 0;
        public static int CpuTargetBitrate { get; set; } = 80; // Mbps
        /// <summary>
        /// 0 = Speed, 1 = Balanced, 2 = Quality
        /// </summary>
        public static int CpuPreset { get; set; } = 2;
        public static bool CpuShowAdvanced { get; set; } = false;
        /// <summary>
        /// Calculates x264 CRF value (0-28) from quality slider. Lower is better quality.
        /// </summary>
        public static int CpuCrfValue => Mathf.RoundToInt(Mathf.Lerp(24f, 0f, CpuQualitySlider));
        #endregion

        #region Ramp Configuration
        public static float RampDurationDefault { get; set; } = 0.5f;  // 0.1 to 3.0 seconds
        public static float RampExponent { get; set; } = 2.0f;         // 0.3 (rush start) to 3.0 (rush end)
        public const float RampExponentMin = 0.05f;
        public const float RampExponentMax = 3.0f;
        #endregion

        #region CameraTools Integration
        /// <summary>
        /// When true, pathing cameras advance by video frame time (1/60s per frame).
        /// When false (default), pathing cameras advance by physics time.
        /// Used for Kraken-Time recording where physics runs at 10,000fps but video outputs at 60fps.
        /// </summary>
        public static bool CameraPathPlaybackTiming { get; set; } = false;
        #endregion

        #region Temporal Accumulation
        /// <summary>
        /// Enable Temporal Accumulation Blur (motion blur via sub-frame accumulation).
        /// Only available with GPU zero-copy encoders (AMF/NVENC).
        /// </summary>
        public static bool EnableTemporalAccumulation { get; set; } = false;

        /// <summary>
        /// Number of sub-frames to accumulate per output frame (default 8 for 180° shutter at 60fps).
        /// Higher values = smoother blur but more GPU memory and processing time.
        /// </summary>
        public static int TabSubFrameCount { get; set; } = 8;

        /// <summary>
        /// Gaussian sigma for sub-frame weighting (default 1.5).
        /// Lower = sharper center weight, Higher = more even distribution.
        /// </summary>
        public static float TabSigma { get; set; } = 1.5f;
        #endregion

        #region Utility Methods
        public static void ResetCaptureSettings()
        {
            DurationSeconds = 10f;
        }
        public static int ValidateRateControlMode(int mode)
        {
            if (mode > 1) return 1; // Force VBR if somehow CBR was selected
            return mode;
        }
        /// <summary>
        /// Rough bitrate estimate for UI display / planning.
        /// </summary>
        /// <param name="encoderType">0=AMF, 1=NVENC, 2=CPU</param>
        public static float GetEstimatedBitrateMbps(int encoderType)
        {
            float quality = 0.7f;
            int rateControlMode = 0;
            int targetBitrate = 80;

            switch (encoderType)
            {
                case 0:
                    quality = AmfQualitySlider;
                    rateControlMode = AmfRateControlMode;
                    targetBitrate = AmfTargetBitrate;
                    break;

                case 1:
                    quality = NvencQualitySlider;
                    rateControlMode = NvencRateControlMode;
                    targetBitrate = NvencTargetBitrate;
                    break;

                case 2:
                    quality = CpuQualitySlider;
                    rateControlMode = CpuRateControlMode;
                    targetBitrate = CpuTargetBitrate;
                    break;
            }

            // VBR / CBR → fixed bitrate
            if (rateControlMode == 1 || rateControlMode == 2)
                return targetBitrate;

            // CRF / CQ / CQP heuristic
            return Mathf.Lerp(20f, 120f, quality);
        }
        #endregion

    }
}