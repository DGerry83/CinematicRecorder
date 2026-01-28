// File: Core/SessionState.cs
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
        // ============================================================
        // CAPTURE TIMING SETTINGS
        // ============================================================

        public static int SimFpsIndex { get; set; } = 2;        // Default 60
        public static int PlaybackFpsIndex { get; set; } = 2;
        public static bool LockFps { get; set; } = true;
        public static float DurationSeconds { get; set; } = 10f;

        public static bool ForceSoftwareEncoding { get; set; } = false;
        public static bool PngSequence { get; set; } = false;

        // ============================================================
        // ENCODER TAB SELECTION
        // 0 = AMF (AMD / HEVC)
        // 1 = NVENC (NVIDIA / HEVC)
        // 2 = CPU (x264)
        // ============================================================

        public static int SelectedEncoderTab { get; set; } = 0;

        // ============================================================
        // AMF (AMD HEVC) SETTINGS
        // ============================================================

        // Quality ↔ File Size slider (0–1)
        public static float AmfQualitySlider { get; set; } = 0.7f;

        // 0 = CQP, 1 = VBR, 2 = CBR
        public static int AmfRateControlMode { get; set; } = 1;

        // Mbps (used for VBR / CBR)
        public static int AmfTargetBitrate { get; set; } = 80;

        // 0 = Speed, 1 = Balanced, 2 = Quality
        public static int AmfEncoderSpeed { get; set; } = 1;

        public static bool AmfShowAdvanced { get; set; } = false;

        // Derived QP (16–28, lower = better quality)
        public static int AmfCqpValue => Mathf.RoundToInt(Mathf.Lerp(24f, 0f, AmfQualitySlider));

        // ============================================================
        // NVENC (NVIDIA HEVC) SETTINGS
        // ============================================================

        public static float NvencQualitySlider { get; set; } = 0.7f;

        // 0 = CQ, 1 = VBR, 2 = CBR
        public static int NvencRateControlMode { get; set; } = 1;

        // Mbps
        public static int NvencTargetBitrate { get; set; } = 80;

        // 0 = Speed, 1 = Balanced, 2 = Quality
        // (maps cleanly to NVENC presets internally)
        public static int NvencPreset { get; set; } = 1;

        public static bool NvencShowAdvanced { get; set; } = false;

        // Derived CQ value (mapped like QP)
        public static int NvencCqValue => Mathf.RoundToInt(Mathf.Lerp(24f, 0f, NvencQualitySlider));

        // ============================================================
        // CPU (x264) SETTINGS
        // ============================================================

        public static float CpuQualitySlider { get; set; } = 0.7f;

        // 0 = CRF, 1 = VBR, 2 = CBR
        public static int CpuRateControlMode { get; set; } = 0;

        // Mbps
        public static int CpuTargetBitrate { get; set; } = 80;

        // 0 = Speed, 1 = Balanced, 2 = Quality
        public static int CpuPreset { get; set; } = 2;

        public static bool CpuShowAdvanced { get; set; } = false;

        // Derived CRF (16–28, lower = better)
        public static int CpuCrfValue => Mathf.RoundToInt(Mathf.Lerp(24f, 0f, CpuQualitySlider));

        // ============================================================
        // HELPERS
        // ============================================================

        public static void ResetCaptureSettings()
        {
            DurationSeconds = 10f;
        }

        public static string GetQualityLabel(float sliderValue)
        {
            int qp = Mathf.RoundToInt(Mathf.Lerp(24f, 0f, sliderValue));

            if (qp <= 8) return "Near Lossless (QP " + qp + ")";
            if (qp <= 14) return "Master Quality (QP " + qp + ")";
            if (qp <= 20) return "High Quality (QP " + qp + ")";
            return "Compressed (QP " + qp + ")";
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
    }
}
