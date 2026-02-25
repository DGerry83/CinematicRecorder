using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinematicRecorder.Core
{
    /// <summary>
    /// Debug utility to capture depth/normal buffers and compute GTAO via native plugin.
    /// </summary>
    public class DebugTextureDumper : MonoBehaviour
    {
        #region Static Initialization
        static DebugTextureDumper()
        {
            try
            {
                string assemblyPath = Path.GetDirectoryName(typeof(DebugTextureDumper).Assembly.Location);
                if (assemblyPath != null)
                {
                    string pluginDataPath = Path.GetFullPath(Path.Combine(assemblyPath, "..", "PluginData"));
                    string ffmpegPath = Path.Combine(pluginDataPath, "FFMpeg");
                    string dllPath = Path.Combine(pluginDataPath, "CinematicRecorderNative.dll");

                    Debug.Log($"[DebugTextureDumper] Loading native DLL from: {dllPath}");

                    if (!Directory.Exists(ffmpegPath))
                    {
                        Debug.LogError($"[DebugTextureDumper] FFmpeg folder not found: {ffmpegPath}");
                        return;
                    }

                    if (!File.Exists(dllPath))
                    {
                        Debug.LogError($"[DebugTextureDumper] Native DLL not found: {dllPath}");
                        return;
                    }

                    SetDllDirectory(ffmpegPath);

                    IntPtr hModule = LoadLibrary(dllPath);
                    if (hModule == IntPtr.Zero)
                    {
                        Debug.LogError($"[DebugTextureDumper] LoadLibrary failed: {Marshal.GetLastWin32Error()}");
                        return;
                    }

                    Debug.Log("[DebugTextureDumper] Native DLL loaded successfully");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DebugTextureDumper] Static init error: {ex}");
            }
        }
        #endregion

        #region Native Imports
        [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetDllDirectory(string lpPathName);

        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("CinematicRecorderNative", CallingConvention = CallingConvention.Cdecl)]
        private static extern void CR_GTAODebugSetInput(IntPtr depthTex, IntPtr normalTex, int width, int height);
        
        [DllImport("CinematicRecorderNative", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        private static extern int CR_GTAODebugExecute([MarshalAs(UnmanagedType.LPStr)] string outputDirectory);
        #endregion
        private Camera _camera;
        private CommandBuffer _depthCommandBuffer;
        private CommandBuffer _normalCommandBuffer;
        private RenderTexture _depthRT;
        private RenderTexture _normalRT;
        private string _outputDir;
        private string _timestamp;
        private int _frameCount = 0;
        private bool _initialized = false;
        
        /// <summary>
        /// Static entry point.
        /// </summary>
        public static void PerformDump()
        {
            Debug.Log("[DebugTextureDumper] PerformDump called");
            
            Camera camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[DebugTextureDumper] No main camera found!");
                return;
            }
            
            GameObject dumperObj = new GameObject("DebugTextureDumper");
            dumperObj.AddComponent<DebugTextureDumper>();
        }
        
        void Start()
        {
            _camera = Camera.main;
            if (_camera == null)
            {
                Destroy(gameObject);
                return;
            }
            
            int width = _camera.pixelWidth;
            int height = _camera.pixelHeight;
            
            // Create output directory
            string assemblyDir = Path.GetDirectoryName(typeof(DebugTextureDumper).Assembly.Location);
            _outputDir = Path.GetFullPath(Path.Combine(assemblyDir, "..", "DebugDumps"));
            
            if (!Directory.Exists(_outputDir))
            {
                Directory.CreateDirectory(_outputDir);
            }
            
            _timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            
            // Create render textures
            _depthRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _depthRT.Create();
            
            _normalRT = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _normalRT.Create();
            
            // Create command buffers using simple Blit
            _depthCommandBuffer = new CommandBuffer();
            _depthCommandBuffer.name = "DebugDumpDepth";
            _depthCommandBuffer.Blit(BuiltinRenderTextureType.ResolvedDepth, _depthRT);
            
            _normalCommandBuffer = new CommandBuffer();
            _normalCommandBuffer.name = "DebugDumpNormals";
            _normalCommandBuffer.Blit(BuiltinRenderTextureType.GBuffer2, _normalRT);
            
            // Add to camera
            _camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _depthCommandBuffer);
            _camera.AddCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _normalCommandBuffer);
            
            _initialized = true;
            Debug.Log("[DebugTextureDumper] Initialized");
        }
        
        void Update()
        {
            if (!_initialized) return;
            
            _frameCount++;
            
            if (_frameCount == 2)
            {
                StartCoroutine(CaptureAndSave());
            }
        }
        
        IEnumerator CaptureAndSave()
        {
            // Remove command buffers
            if (_camera != null)
            {
                _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _depthCommandBuffer);
                _camera.RemoveCommandBuffer(CameraEvent.BeforeImageEffectsOpaque, _normalCommandBuffer);
            }
            _depthCommandBuffer.Release();
            _normalCommandBuffer.Release();
            
            yield return null;
            
            // Save C# side PNGs
            string depthCSPath = Path.Combine(_outputDir, $"depth_{_timestamp}_cs.png");
            string normalCSPath = Path.Combine(_outputDir, $"normal_{_timestamp}_cs.png");
            
            SaveRenderTextureAsPNG(_depthRT, depthCSPath);
            SaveRenderTextureAsPNG(_normalRT, normalCSPath);
            
            Debug.Log($"[DebugTextureDumper] C# PNGs saved: {depthCSPath}, {normalCSPath}");
            
            // Call native plugin
            try
            {
                IntPtr depthPtr = _depthRT.GetNativeTexturePtr();
                IntPtr normalPtr = _normalRT.GetNativeTexturePtr();
                
                Debug.Log($"[DebugTextureDumper] Passing to native: depth={depthPtr}, normal={normalPtr}");
                
                CR_GTAODebugSetInput(depthPtr, normalPtr, _depthRT.width, _depthRT.height);
                int result = CR_GTAODebugExecute(_outputDir);
                
                if (result == 0)
                {
                    Debug.Log("[DebugTextureDumper] Native plugin completed successfully");
                }
                else
                {
                    Debug.LogError($"[DebugTextureDumper] Native plugin failed with code: {result}");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DebugTextureDumper] Native plugin exception: {ex.Message}");
            }
            
            // Cleanup
            if (_depthRT != null) _depthRT.Release();
            if (_normalRT != null) _normalRT.Release();
            Destroy(gameObject);
        }
        
        void SaveRenderTextureAsPNG(RenderTexture rt, string filename)
        {
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(filename, tex.EncodeToPNG());
            Destroy(tex);
            RenderTexture.active = null;
        }
    }
}
