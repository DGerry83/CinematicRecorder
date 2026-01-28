using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace CinematicRecorder.Capture
{
    public unsafe class AmfZeroCopyEncoder : IDisposable
    {
        private IntPtr _encoderHandle;
        private bool _isInitialized;
        private bool _isDisposed;
        private const string PluginName = "CinematicRecorderNative";

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

        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        // NEW: Import for explicit device setting (alternative to InitFromTexture)
        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern void CR_SetUnityD3D11Device(IntPtr device);

        // MODIFIED: Removed device parameter from import (now global)
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
            [MarshalAs(UnmanagedType.LPStr)] string outputPath);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_EncodeFrame(
            IntPtr encoder,
            IntPtr d3d11Texture,
            long frameIndex);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern int CR_ShutdownEncoder(IntPtr encoder);

        [DllImport(PluginName, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr CR_GetLastError();

        public bool IsInitialized => _isInitialized;

        // NEW: Optional explicit device initialization (call once before first encoder init)
        // You can get the native device pointer from Unity's Rendering.GraphicsDevice interfaces
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

        // Maintains backward compatibility: extracts device from texture internally
        public bool Initialize(int width, int height, int fps, string outputPath, IntPtr d3d11TexturePtr)
        {
            if (_isInitialized)
                return true;

            if (d3d11TexturePtr == IntPtr.Zero)
            {
                Debug.LogError("[AmfZeroCopyEncoder] D3D11 texture pointer is null");
                return false;
            }

            Debug.Log($"[AmfZeroCopyEncoder] Initializing: {width}x{height}@{fps}, tex=0x{d3d11TexturePtr.ToInt64():X}, path={outputPath}");

            try
            {
                // This will extract the device from the texture and store it globally in C++
                // Subsequent calls can use CR_InitEncoder (without device) if desired
                _encoderHandle = CR_InitEncoderFromTexture(d3d11TexturePtr, width, height, fps, outputPath);

                if (_encoderHandle == IntPtr.Zero)
                {
                    string err = Marshal.PtrToStringAnsi(CR_GetLastError()) ?? "Unknown native error";
                    Debug.LogError($"[AmfZeroCopyEncoder] Native init returned null: {err}");
                    return false;
                }

                _isInitialized = true;
                Debug.Log($"[AmfZeroCopyEncoder] Initialized successfully");
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
            catch (Exception ex)
            {
                Debug.LogError($"[AmfZeroCopyEncoder] Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

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
                // Native code will CopyResource from unityTexture to internal owned texture
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

        public void Dispose()
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            Shutdown();
        }
    }
}