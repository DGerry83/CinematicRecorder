using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicRecorder.Capture
{
    public unsafe class NvencZeroCopyEncoder : IDisposable
    {
        #region Fields
        private IntPtr _encoderHandle;
        private bool _isInitialized;
        private bool _isDisposed;
        private const string PluginName = "CinematicRecorderNative";
        #endregion
        #region Structs
        [StructLayout(LayoutKind.Sequential)]
        public struct NvencEncoderSettings
        {
            public int RateControlMode;   // 0=CQP, 1=VBR, 2=CBR
            public int TargetBitrateKbps;
            public int QpI;               // 0-51
            public int QpP;
            public int QpB;
            public int QualityPreset;     // 0=P1(Speed), 1=P4(Balanced), 2=P7(Quality)
            public int Codec;             // 0=H264, 1=HEVC
            public int GopSize;
            public int Reserved1;
            public int Reserved2;
        }
        #endregion
        #region Native Imports
        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CR_GetLastError();

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern IntPtr CR_InitNvencEncoderFromTexture(
            IntPtr d3d11Texture,
            int width,
            int height,
            int fps,
            [MarshalAs(UnmanagedType.LPStr)] string outputPath,
            ref NvencEncoderSettings settings);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_EncodeNvencFrame(
            IntPtr encoder,
            IntPtr d3d11Texture,
            long frameIndex);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_ShutdownNvencEncoder(IntPtr encoder);
        #endregion
        #region Public API
        public bool IsInitialized => _isInitialized;
        /// <summary>
        /// Initializes NVENC hardware encoder from a D3D11 texture for zero-copy GPU encoding.
        /// </summary>
        public bool Initialize(
            int width,
            int height,
            int fps,
            string outputPath,
            IntPtr d3d11TexturePtr,
            NvencEncoderSettings settings)
        {
            if (_isInitialized)
                return true;

            if (d3d11TexturePtr == IntPtr.Zero)
            {
                Debug.LogError("[NvencZeroCopyEncoder] D3D11 texture pointer is null");
                return false;
            }

            Debug.Log($"[NvencZeroCopyEncoder] Initializing: {width}x{height}@{fps}, " +
                $"RC={settings.RateControlMode}, QP={settings.QpI}, Preset={settings.QualityPreset}");

            try
            {
                _encoderHandle = CR_InitNvencEncoderFromTexture(
                    d3d11TexturePtr,
                    width,
                    height,
                    fps,
                    outputPath,
                    ref settings);

                if (_encoderHandle == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? "Unknown native error";
                    Debug.LogError($"[NvencZeroCopyEncoder] Native init returned null: {err}");
                    return false;
                }

                _isInitialized = true;
                Debug.Log($"[NvencZeroCopyEncoder] Initialized successfully");
                return true;
            }
            catch (SEHException ex)
            {
                Debug.LogError($"[NvencZeroCopyEncoder] Native code crashed (SEH): {ex.Message}");
                return false;
            }
            catch (EntryPointNotFoundException ex)
            {
                Debug.LogError($"[NvencZeroCopyEncoder] Export not found: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NvencZeroCopyEncoder] Exception: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// Encodes a frame using the configured NVENC encoder.
        /// </summary>
        public bool EncodeFrame(IntPtr d3d11TexturePtr, long frameIndex)
        {
            if (!_isInitialized || _encoderHandle == IntPtr.Zero)
                return false;

            if (d3d11TexturePtr == IntPtr.Zero)
            {
                Debug.LogError($"[NvencZeroCopyEncoder] EncodeFrame called with null texture");
                return false;
            }

            try
            {
                int result = CR_EncodeNvencFrame(_encoderHandle, d3d11TexturePtr, frameIndex);

                if (result != 0)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? $"Error code {result}";
                    Debug.LogError($"[NvencZeroCopyEncoder] Encode failed for frame {frameIndex}: {err}");
                    return false;
                }

                return true;
            }
            catch (SEHException ex)
            {
                Debug.LogError($"[NvencZeroCopyEncoder] Encode crashed (SEH): {ex.Message}");
                return false;
            }
        }

        public void Shutdown()
        {
            if (!_isInitialized || _encoderHandle == IntPtr.Zero)
                return;

            try
            {
                int result = CR_ShutdownNvencEncoder(_encoderHandle);
                if (result != 0)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? $"Error code {result}";
                    Debug.LogWarning($"[NvencZeroCopyEncoder] Shutdown warning: {err}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NvencZeroCopyEncoder] Shutdown exception: {ex.Message}");
            }

            _encoderHandle = IntPtr.Zero;
            _isInitialized = false;
            Debug.Log("[NvencZeroCopyEncoder] Shutdown complete");
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