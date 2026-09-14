using System;
using System.IO;
using UnityEngine;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// File-based persistence for the user-facing settings held in SessionState.
    /// Saves to GameData/CinematicRecorder/PluginData/CinematicRecorderSettings.cfg
    /// in ConfigNode format (root node CINEMATIC_RECORDER_SETTINGS, version = 1).
    /// Load runs once per process; every loaded value is range-clamped and any
    /// failure leaves the C# defaults in place. Static class — no GameObject.
    /// </summary>
    public static class SettingsPersistence
    {
        #region Constants
        private const string ConfigRootNode = "CINEMATIC_RECORDER_SETTINGS";
        private const string ConfigFileName = "CinematicRecorderSettings.cfg";
        private const int ConfigVersion = 1;

        // Maximum valid SimFpsIndex / PlaybackFpsIndex. Mirrors the 7 entries of
        // SettingsDialog.FrameratePresets (SettingsDialog.cs:24) and is duplicated
        // here deliberately — the preset array is private to the UI, and moving it
        // would touch UI code for no behavioral gain.
        private const int FpsPresetMaxIndex = 6;
        #endregion

        #region State
        // Once-per-process load guard (the addon's Start() runs at every
        // Flight-scene entry, but settings must only be read from disk the first time).
        private static bool _loaded;
        #endregion

        #region Public API
        /// <summary>
        /// Restores persisted settings from the cfg file into SessionState, clamping
        /// every value to its valid range. Guarded to run once per process. A missing
        /// file, unreadable file, missing key, or parse error leaves the current
        /// values (the C# defaults) in place — never throws.
        /// </summary>
        public static void LoadFromDisk()
        {
            if (_loaded)
                return;
            _loaded = true;

            try
            {
                string configPath = GetConfigPath();

                // First run — nothing to restore, C# defaults stay in place
                if (!File.Exists(configPath))
                    return;

                ConfigNode root = ConfigNode.Load(configPath);
                ConfigNode node = root != null ? root.GetNode(ConfigRootNode) : null;
                if (node == null)
                {
                    Debug.LogWarning("[CinematicRecorder] Settings file has no "
                        + ConfigRootNode + " node; using defaults");
                    return;
                }

                SessionState.SimFpsIndex = ReadInt(node, "simFpsIndex",
                    SessionState.SimFpsIndex, 0, FpsPresetMaxIndex);
                SessionState.PlaybackFpsIndex = ReadInt(node, "playbackFpsIndex",
                    SessionState.PlaybackFpsIndex, 0, FpsPresetMaxIndex);
                SessionState.LockFps = ReadBool(node, "lockFps", SessionState.LockFps);
                SessionState.DurationSeconds = ReadFloat(node, "durationSeconds",
                    SessionState.DurationSeconds, 0f, 86400f);
                SessionState.ForceSoftwareEncoding = ReadBool(node, "forceSoftwareEncoding",
                    SessionState.ForceSoftwareEncoding);
                SessionState.PngSequence = ReadBool(node, "pngSequence", SessionState.PngSequence);
                SessionState.EnableAudioCapture = ReadBool(node, "enableAudioCapture",
                    SessionState.EnableAudioCapture);
                SessionState.CaptureUiLayer = ReadBool(node, "captureUiLayer", SessionState.CaptureUiLayer);

                SessionState.AmfQualitySlider = ReadFloat(node, "amfQualitySlider",
                    SessionState.AmfQualitySlider, 0f, 1f);
                SessionState.NvencQualitySlider = ReadFloat(node, "nvencQualitySlider",
                    SessionState.NvencQualitySlider, 0f, 1f);
                SessionState.CpuQualitySlider = ReadFloat(node, "cpuQualitySlider",
                    SessionState.CpuQualitySlider, 0f, 1f);
                SessionState.AmfRateControlMode = SessionState.ValidateRateControlMode(
                    ReadInt(node, "amfRateControlMode", SessionState.AmfRateControlMode, 0, 2));
                SessionState.NvencRateControlMode = SessionState.ValidateRateControlMode(
                    ReadInt(node, "nvencRateControlMode", SessionState.NvencRateControlMode, 0, 2));
                SessionState.CpuRateControlMode = SessionState.ValidateRateControlMode(
                    ReadInt(node, "cpuRateControlMode", SessionState.CpuRateControlMode, 0, 2));
                SessionState.AmfTargetBitrate = ReadInt(node, "amfTargetBitrate",
                    SessionState.AmfTargetBitrate, 10, 200);
                SessionState.NvencTargetBitrate = ReadInt(node, "nvencTargetBitrate",
                    SessionState.NvencTargetBitrate, 10, 200);
                SessionState.CpuTargetBitrate = ReadInt(node, "cpuTargetBitrate",
                    SessionState.CpuTargetBitrate, 10, 200);
                SessionState.AmfEncoderSpeed = ReadInt(node, "amfEncoderSpeed",
                    SessionState.AmfEncoderSpeed, 0, 2);
                SessionState.NvencPreset = ReadInt(node, "nvencPreset", SessionState.NvencPreset, 0, 2);
                SessionState.CpuPreset = ReadInt(node, "cpuPreset", SessionState.CpuPreset, 0, 2);
                SessionState.AmfUseBlueNoiseDither = ReadBool(node, "amfUseBlueNoiseDither",
                    SessionState.AmfUseBlueNoiseDither);

                SessionState.RampDurationDefault = ReadFloat(node, "rampDurationDefault",
                    SessionState.RampDurationDefault, 0.1f, 3f);
                SessionState.RampExponent = ReadFloat(node, "rampExponent",
                    SessionState.RampExponent, SessionState.RampExponentMin, SessionState.RampExponentMax);

                SessionState.EnableTemporalAccumulation = ReadBool(node, "enableTemporalAccumulation",
                    SessionState.EnableTemporalAccumulation);
                SessionState.TabEnableSharpening = ReadBool(node, "tabEnableSharpening",
                    SessionState.TabEnableSharpening);
                SessionState.TabSubFrameCount = ReadInt(node, "tabSubFrameCount",
                    SessionState.TabSubFrameCount, 1, 64);
                SessionState.TabSigma = ReadFloat(node, "tabSigma", SessionState.TabSigma, 0.1f, 10f);
                // Clamp 0..1.0 — matches the AdvancedSettingsWindow slider (:353);
                // SessionState's doc comment (0..0.5) is stale, follow-up housekeeping.
                SessionState.TabSharpeningStrength = ReadFloat(node, "tabSharpeningStrength",
                    SessionState.TabSharpeningStrength, 0f, 1f);

                Debug.Log("[CinematicRecorder] Settings loaded from " + configPath);
            }
            catch (Exception ex)
            {
                Debug.LogError("[CinematicRecorder] Failed to load settings: " + ex);
            }
        }

        /// <summary>
        /// Serializes the current SessionState settings to the cfg file in PluginData.
        /// Failures are logged and swallowed — never throws.
        /// </summary>
        public static void SaveToDisk()
        {
            try
            {
                string pluginData = Path.Combine(
                    KSPUtil.ApplicationRootPath,
                    "GameData",
                    "CinematicRecorder",
                    "PluginData");

                Directory.CreateDirectory(pluginData);

                ConfigNode root = new ConfigNode();
                ConfigNode node = root.AddNode(ConfigRootNode);
                node.AddValue("version", ConfigVersion);

                node.AddValue("simFpsIndex", SessionState.SimFpsIndex);
                node.AddValue("playbackFpsIndex", SessionState.PlaybackFpsIndex);
                node.AddValue("lockFps", SessionState.LockFps);
                node.AddValue("durationSeconds", SessionState.DurationSeconds);
                node.AddValue("forceSoftwareEncoding", SessionState.ForceSoftwareEncoding);
                node.AddValue("pngSequence", SessionState.PngSequence);
                node.AddValue("enableAudioCapture", SessionState.EnableAudioCapture);
                node.AddValue("captureUiLayer", SessionState.CaptureUiLayer);

                node.AddValue("amfQualitySlider", SessionState.AmfQualitySlider);
                node.AddValue("nvencQualitySlider", SessionState.NvencQualitySlider);
                node.AddValue("cpuQualitySlider", SessionState.CpuQualitySlider);
                node.AddValue("amfRateControlMode", SessionState.AmfRateControlMode);
                node.AddValue("nvencRateControlMode", SessionState.NvencRateControlMode);
                node.AddValue("cpuRateControlMode", SessionState.CpuRateControlMode);
                node.AddValue("amfTargetBitrate", SessionState.AmfTargetBitrate);
                node.AddValue("nvencTargetBitrate", SessionState.NvencTargetBitrate);
                node.AddValue("cpuTargetBitrate", SessionState.CpuTargetBitrate);
                node.AddValue("amfEncoderSpeed", SessionState.AmfEncoderSpeed);
                node.AddValue("nvencPreset", SessionState.NvencPreset);
                node.AddValue("cpuPreset", SessionState.CpuPreset);
                node.AddValue("amfUseBlueNoiseDither", SessionState.AmfUseBlueNoiseDither);

                node.AddValue("rampDurationDefault", SessionState.RampDurationDefault);
                node.AddValue("rampExponent", SessionState.RampExponent);

                node.AddValue("enableTemporalAccumulation", SessionState.EnableTemporalAccumulation);
                node.AddValue("tabEnableSharpening", SessionState.TabEnableSharpening);
                node.AddValue("tabSubFrameCount", SessionState.TabSubFrameCount);
                node.AddValue("tabSigma", SessionState.TabSigma);
                node.AddValue("tabSharpeningStrength", SessionState.TabSharpeningStrength);

                root.Save(Path.Combine(pluginData, ConfigFileName));
            }
            catch (Exception ex)
            {
                Debug.LogError("[CinematicRecorder] Failed to save settings: " + ex);
            }
        }
        #endregion

        #region Helpers
        private static string GetConfigPath()
        {
            return Path.Combine(
                KSPUtil.ApplicationRootPath,
                "GameData",
                "CinematicRecorder",
                "PluginData",
                ConfigFileName);
        }

        /// <summary>
        /// Reads an int key, falling back to the current value when the key is missing
        /// or invalid, and clamps the result to [min, max].
        /// </summary>
        private static int ReadInt(ConfigNode node, string key, int fallback, int min, int max)
        {
            string raw = node.GetValue(key);
            int value;
            if (raw == null || !int.TryParse(raw, out value))
                return fallback;
            return Mathf.Clamp(value, min, max);
        }

        /// <summary>
        /// Reads a float key, falling back to the current value when the key is missing
        /// or invalid, and clamps the result to [min, max].
        /// </summary>
        private static float ReadFloat(ConfigNode node, string key, float fallback, float min, float max)
        {
            string raw = node.GetValue(key);
            float value;
            if (raw == null || !float.TryParse(raw, out value))
                return fallback;
            return Mathf.Clamp(value, min, max);
        }

        /// <summary>
        /// Reads a bool key, falling back to the current value when the key is missing
        /// or invalid.
        /// </summary>
        private static bool ReadBool(ConfigNode node, string key, bool fallback)
        {
            string raw = node.GetValue(key);
            bool value;
            if (raw == null || !bool.TryParse(raw, out value))
                return fallback;
            return value;
        }
        #endregion
    }
}
