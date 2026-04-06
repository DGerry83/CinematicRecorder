using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicRecorder.Capture
{
    public unsafe class AmfZeroCopyEncoder : IDisposable
    {
        #region Fields
        private IntPtr _encoderHandle;
        private bool _isInitialized;
        private bool _isDisposed;
        private const string PluginName = "CinematicRecorderNative";
        #endregion

        #region Static Initialization
        static AmfZeroCopyEncoder()
        {
            try
            {
                string assemblyPath = Path.GetDirectoryName(typeof(AmfZeroCopyEncoder).Assembly.Location);
                if (assemblyPath != null)
                {
                    string pluginDataPath = Path.GetFullPath(Path.Combine(assemblyPath, "..", "PluginData"));
                    string ffmpegPath = Path.Combine(pluginDataPath, "FFMpeg");
                    string dllPath = Path.Combine(pluginDataPath, "CinematicRecorderNative.dll");

                    Debug.Log($"[AmfZeroCopyEncoder] Loading from: {dllPath}");

                    if (!Directory.Exists(ffmpegPath))
                    {
                        Debug.LogError($"[AmfZeroCopyEncoder] FFmpeg folder not found: {ffmpegPath}");
                        return;
                    }

                    if (!File.Exists(dllPath))
                    {
                        Debug.LogError($"[AmfZeroCopyEncoder] Native DLL not found: {dllPath}");
                        return;
                    }

                    SetDllDirectory(ffmpegPath);

                    // Verify the specific export exists before loading
                    IntPtr hModule = LoadLibrary(dllPath);
                    if (hModule == IntPtr.Zero)
                    {
                        Debug.LogError($"[AmfZeroCopyEncoder] LoadLibrary failed: {Marshal.GetLastWin32Error()}");
                        return;
                    }

                    IntPtr procAddr = GetProcAddress(hModule, "CR_InitEncoderFromTexture");
                    if (procAddr == IntPtr.Zero)
                    {
                        Debug.LogError($"[AmfZeroCopyEncoder] Export 'CR_InitEncoderFromTexture' not found in DLL!");
                        return;
                    }

                    Debug.Log($"[AmfZeroCopyEncoder] Export found at 0x{procAddr.ToInt64():X}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] Static init error: {ex}");
            }
        }
        #endregion

        #region Native Imports
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        // Import for explicit device setting (alternative to InitFromTexture)
        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void CR_SetUnityD3D11Device(IntPtr device);

        // Removed device parameter from import (now global)
        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr CR_InitEncoder(
            int width,
            int height,
            int fps,
            [MarshalAs(UnmanagedType.LPStr)] string outputPath);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr CR_InitEncoderFromTexture(
            IntPtr d3d11Texture,
            int width,
            int height,
            int fps,
            [MarshalAs(UnmanagedType.LPStr)] string outputPath,
            ref AmfEncoderSettings settings);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_EncodeFrame(
            IntPtr encoder,
            IntPtr d3d11Texture,
            long frameIndex);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_ShutdownEncoder(IntPtr encoder);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CR_GetLastError();

        // NEW: Temporal Accumulation Blur imports

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_SetTemporalAccumulation(IntPtr encoder, ref TabSettings settings);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_SubmitSubFrame(IntPtr encoder, IntPtr d3d11Texture, int subFrameIndex);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_FinalizeTemporalFrame(IntPtr encoder, long outputFrameIndex);

        // NEW: Sharpening filter control
        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_SetTabSharpening(IntPtr encoder, int enabled, float strength);
        #endregion

        #region Structs
        [StructLayout(LayoutKind.Sequential)]
        public struct AmfEncoderSettings
        {
            public int RateControlMode;
            public int TargetBitrateKbps;
            public int QpI;
            public int QpP;
            public int QpB;
            public int QualityPreset;
            public int Codec;
            public int GopSize;
            public int EnableVbaq;
            public int UseBlueNoiseDither;
            public int Reserved2;
        }

        // NEW: TabSettings struct - must match native layout exactly
        [StructLayout(LayoutKind.Sequential)]
        public struct TabSettings
        {
            public int Enabled;       // 0 = Off, 1 = On
            public int SubFrameCount; // Number of sub-frames (typically 8)
            public float Sigma;       // Gaussian blur sigma (typically 1.5f)
        }
        #endregion

        #region Public API
        public bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initializes the global D3D11 device for all AMF encoder instances. Must be called once before first encoder initialization.
        /// </summary>
        public static bool InitializeDevice(IntPtr d3d11DevicePtr)
        {
            if (d3d11DevicePtr == IntPtr.Zero)
            {
                Debug.LogError("[AmfZeroCopyEncoder] InitializeDevice called with null pointer");
                return false;
            }

            try
            {
                CR_SetUnityD3D11Device(d3d11DevicePtr);
                Debug.Log("[AmfZeroCopyEncoder] Global D3D11 device set successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] Failed to set device: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Initializes the encoder from a D3D11 texture handle using AMF hardware acceleration.
        /// </summary>
        public bool Initialize(
            int width,
            int height,
            int fps,
            string outputPath,
            IntPtr d3d11TexturePtr,
            AmfEncoderSettings settings)
        {
            if (_isInitialized)
                return true;

            if (d3d11TexturePtr == IntPtr.Zero)
            {
                Debug.LogError("[AmfZeroCopyEncoder] D3D11 texture pointer is null");
                return false;
            }

            Debug.Log($"[AmfZeroCopyEncoder] Initializing: {width}x{height}@{fps}, " +
                $"RC={settings.RateControlMode}, Bitrate={settings.TargetBitrateKbps}kbps, " +
                $"QP={settings.QpI}, Preset={settings.QualityPreset}");

            try
            {
                _encoderHandle = CR_InitEncoderFromTexture(
                    d3d11TexturePtr,
                    width,
                    height,
                    fps,
                    outputPath,
                    ref settings);

                if (_encoderHandle == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? "Unknown native error";
                    Debug.LogError($"[AmfZeroCopyEncoder] Native init returned null: {err}");
                    return false;
                }

                _isInitialized = true;
                Debug.Log($"[AmfZeroCopyEncoder] Initialized successfully with custom AMF settings");
                return true;
            }
            catch (SEHException ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] Native code crashed (SEH): {ex.Message}");
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] Function not found in DLL: {ex.Message}");
                return false;
            }
            catch (DllNotFoundException)
            {
                // Driver dependency missing - expected on non-AMD systems, silent fail
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Encodes a single frame using the provided D3D11 texture without GPU readback.
        /// Note: Do not use this when Temporal Accumulation Blur is enabled. Use SubmitSubFrame + FinalizeTemporalFrame instead.
        /// </summary>
        public bool EncodeFrame(IntPtr d3d11TexturePtr, long frameIndex)
        {
            if (!_isInitialized || _encoderHandle == IntPtr.Zero)
                return false;

            if (d3d11TexturePtr == IntPtr.Zero)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] EncodeFrame called with null texture");
                return false;
            }

            try
            {
                int result = CR_EncodeFrame(_encoderHandle, d3d11TexturePtr, frameIndex);

                if (result != 0)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? $"Error code {result}";
                    Debug.LogError($"[AmfZeroCopyEncoder] Encode failed for frame {frameIndex}: {err}");
                    return false;
                }

                return true;
            }
            catch (SEHException ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] Encode crashed (SEH): {ex.Message}");
                return false;
            }
        }

        // NEW: Enable Temporal Accumulation Blur mode
        /// <summary>
        /// Configures Temporal Accumulation Blur mode. Must be called after Initialize but before first frame.
        /// </summary>
        /// <param name="enabled">true to enable TAB, false to disable</param>
        /// <param name="subFrameCount">Number of sub-frames to accumulate (typically 8)</param>
        /// <param name="sigma">Gaussian blur sigma (typically 1.5f)</param>
        /// <returns>true on success, false on failure</returns>
        public bool EnableTemporalAccumulation(bool enabled, int subFrameCount = 8, float sigma = 1.5f)
        {
            if (!_isInitialized || _encoderHandle == IntPtr.Zero)
            {
                Debug.LogError("[AmfZeroCopyEncoder] Cannot configure TAB - encoder not initialized");
                return false;
            }

            try
            {
                var settings = new TabSettings
                {
                    Enabled = enabled ? 1 : 0,
                    SubFrameCount = subFrameCount,
                    Sigma = sigma
                };

                int result = CR_SetTemporalAccumulation(_encoderHandle, ref settings);

                if (result != 0)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? $"Error code {result}";
                    Debug.LogError($"[AmfZeroCopyEncoder] Failed to configure TAB: {err}");
                    return false;
                }

                Debug.Log($"[AmfZeroCopyEncoder] Temporal Accumulation Blur {(enabled ? "enabled" : "disabled")} " +
                    $"(subframes={subFrameCount}, sigma={sigma})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] EnableTemporalAccumulation exception: {ex.Message}");
                return false;
            }
        }

        // NEW: Submit a single sub-frame for accumulation
        /// <summary>
        /// Copies a sub-frame to the accumulation array. Call this 8 times per output frame (indices 0-7).
        /// </summary>
        /// <param name="d3d11TexturePtr">Native D3D11 texture pointer from RenderTexture.GetNativeTexturePtr()</param>
        /// <param name="subFrameIndex">Index 0 to (SubFrameCount-1)</param>
        /// <returns>true on success</returns>
        public bool SubmitSubFrame(IntPtr d3d11TexturePtr, int subFrameIndex)
        {
            if (!_isInitialized || _encoderHandle == IntPtr.Zero)
                return false;

            if (d3d11TexturePtr == IntPtr.Zero)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] SubmitSubFrame called with null texture");
                return false;
            }

            try
            {
                int result = CR_SubmitSubFrame(_encoderHandle, d3d11TexturePtr, subFrameIndex);

                if (result != 0)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? $"Error code {result}";
                    Debug.LogError($"[AmfZeroCopyEncoder] SubmitSubFrame failed for index {subFrameIndex}: {err}");
                    return false;
                }

                return true;
            }
            catch (SEHException ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] SubmitSubFrame crashed (SEH): {ex.Message}");
                return false;
            }
        }

        // NEW: Finalize accumulated sub-frames and encode
        /// <summary>
        /// Dispatches compute shader to average accumulated sub-frames, then encodes the result.
        /// This method blocks until encoding is complete (synchronous).
        /// </summary>
        /// <param name="outputFrameIndex">Frame index for the encoded output</param>
        /// <returns>true on success</returns>
        public bool FinalizeTemporalFrame(long outputFrameIndex)
        {
            if (!_isInitialized || _encoderHandle == IntPtr.Zero)
                return false;

            try
            {
                int result = CR_FinalizeTemporalFrame(_encoderHandle, outputFrameIndex);

                if (result != 0)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? $"Error code {result}";
                    Debug.LogError($"[AmfZeroCopyEncoder] FinalizeTemporalFrame failed for frame {outputFrameIndex}: {err}");
                    return false;
                }

                return true;
            }
            catch (SEHException ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] FinalizeTemporalFrame crashed (SEH): {ex.Message}");
                return false;
            }
        }

        // NEW: Set sharpening filter parameters
        /// <summary>
        /// Configures sharpening filter (unsharp mask) for TAB output.
        /// Must be called after EnableTemporalAccumulation but before first frame.
        /// </summary>
        /// <param name="enabled">true to enable sharpening</param>
        /// <param name="strength">Sharpening strength (0.0 to 0.5)</param>
        /// <returns>true on success</returns>
        public bool SetTabSharpening(bool enabled, float strength)
        {
            if (!_isInitialized || _encoderHandle == IntPtr.Zero)
            {
                Debug.LogError("[AmfZeroCopyEncoder] Cannot configure sharpening - encoder not initialized");
                return false;
            }

            try
            {
                // Clamp strength to valid range
                strength = Mathf.Clamp(strength, 0.0f, 0.5f);
                
                int result = CR_SetTabSharpening(_encoderHandle, enabled ? 1 : 0, strength);

                if (result != 0)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? $"Error code {result}";
                    Debug.LogError($"[AmfZeroCopyEncoder] Failed to configure sharpening: {err}");
                    return false;
                }

                Debug.Log($"[AmfZeroCopyEncoder] Sharpening {(enabled ? "enabled" : "disabled")} (strength={strength:F2})");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] SetTabSharpening exception: {ex.Message}");
                return false;
            }
        }

        public void Shutdown()
        {
            if (!_isInitialized || _encoderHandle == IntPtr.Zero)
                return;

            try
            {
                int result = CR_ShutdownEncoder(_encoderHandle);
                if (result != 0)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? $"Error code {result}";
                    Debug.LogWarning($"[AmfZeroCopyEncoder] Shutdown warning: {err}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] Shutdown exception: {ex.Message}");
            }

            _encoderHandle = IntPtr.Zero;
            _isInitialized = false;
            Debug.Log("[AmfZeroCopyEncoder] Shutdown complete");
        }
        #endregion

        #region IDisposable
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            Shutdown();
        }
        #endregion
    }
}