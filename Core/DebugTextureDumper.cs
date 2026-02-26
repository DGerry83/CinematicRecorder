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
        private static extern void CR_GTAODebugSetInput(IntPtr depthTex, IntPtr normalTex, int width, int height,
                                                          [In] float[] invProj, [In] float[] worldToView, float nearPlane, float farPlane);
        
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
            
            // Create render textures - depth as RFloat (preserve full float precision like Deferred)
            _depthRT = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
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
            
            SaveRenderTextureAsPNG(_depthRT, depthCSPath, isDepth: true);
            SaveRenderTextureAsPNG(_normalRT, normalCSPath, isDepth: false);
            
            Debug.Log($"[DebugTextureDumper] C# PNGs saved: {depthCSPath}, {normalCSPath}");
            
            // Call native plugin
            try
            {
                IntPtr depthPtr = _depthRT.GetNativeTexturePtr();
                IntPtr normalPtr = _normalRT.GetNativeTexturePtr();
                
                Debug.Log($"[DebugTextureDumper] Passing to native: depth={depthPtr}, normal={normalPtr}");
                
                // Get camera projection matrix and convert to array for marshaling
                // Use GL.GetGPUProjectionMatrix to get GPU-compatible matrix (handles coordinate conversion)
                Matrix4x4 projMatrix = GL.GetGPUProjectionMatrix(_camera.projectionMatrix, false);
                Matrix4x4 invProjMatrix = projMatrix.inverse;
                float[] invProjArray = new float[16];
                for (int i = 0; i < 16; i++)
                {
                    invProjArray[i] = invProjMatrix[i];
                }
                
                // Get world-to-camera matrix (3x3 rotation only)
                // Extract ROWS to match HLSL float3x3(row0, row1, row2) layout
                Matrix4x4 worldToCamera = _camera.worldToCameraMatrix;
                float[] worldToViewArray = new float[9];
                // Row 0
                worldToViewArray[0] = worldToCamera[0, 0];
                worldToViewArray[1] = worldToCamera[0, 1];
                worldToViewArray[2] = worldToCamera[0, 2];
                // Row 1
                worldToViewArray[3] = worldToCamera[1, 0];
                worldToViewArray[4] = worldToCamera[1, 1];
                worldToViewArray[5] = worldToCamera[1, 2];
                // Row 2
                worldToViewArray[6] = worldToCamera[2, 0];
                worldToViewArray[7] = worldToCamera[2, 1];
                worldToViewArray[8] = worldToCamera[2, 2];
                
                Debug.Log($"[DebugTextureDumper] Camera: near={_camera.nearClipPlane}, far={_camera.farClipPlane}");
                
                CR_GTAODebugSetInput(depthPtr, normalPtr, _depthRT.width, _depthRT.height,
                                     invProjArray, worldToViewArray, _camera.nearClipPlane, _camera.farClipPlane);
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
        
        void SaveRenderTextureAsPNG(RenderTexture rt, string filename, bool isDepth = false)
        {
            // For depth (RFloat), convert to ARGB32 for PNG export while preserving original
            if (isDepth && rt.format == RenderTextureFormat.RFloat)
            {
                RenderTexture tempRT = RenderTexture.GetTemporary(rt.width, rt.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(rt, tempRT);
                
                RenderTexture.active = tempRT;
                Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(filename, tex.EncodeToPNG());
                Destroy(tex);
                RenderTexture.active = null;
                
                RenderTexture.ReleaseTemporary(tempRT);
            }
            else
            {
                // Normal path for ARGB32
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
}
