using System;
using System.Collections;
using System.IO;
using UnityEngine.Rendering;
using Unity.Collections;
using UnityEngine;
using CinematicRecorder.Core;

namespace CinematicRecorder.Capture
{
    public sealed class OfflineCaptureController
    {
        private readonly Camera camera;
        private readonly int width;
        private readonly int height;
        private readonly int simulationFps;
        private readonly int playbackFps;
        private readonly int totalFrames;
        private readonly string outputPath;
        private readonly bool useGpuZeroCopy;
        private AmfZeroCopyEncoder zeroCopyEncoder;
        private bool usingZeroCopyPath;
        private GraphicsFence prevFence;

        // Sim speed tracking (rolling window)
        private const int SimSpeedWindowFrames = 5;
        private int framesSinceSpeedSample = 0;
        private float realTimeAtLastSample;

        private RenderTexture[] renderTextures; // Double-buffered: [0] and [1]
        private Texture2D readbackTexture;
        private HardwareEncoder encoder;
        private CommandBuffer captureBuffer; // Only used for standard path

        public OfflineCaptureController(
                    Camera camera,
                    int width,
                    int height,
                    int simulationFps,
                    int playbackFps,
                    float durationSeconds,
                    string outputPath,
                    bool forceSoftwareEncoding,
                    bool useGpuZeroCopy)
        {
            this.camera = camera;
            this.width = width;
            this.height = height;
            this.simulationFps = simulationFps;
            this.playbackFps = playbackFps;
            this.totalFrames = Mathf.RoundToInt(durationSeconds * simulationFps);
            this.outputPath = outputPath;
            this.useGpuZeroCopy = useGpuZeroCopy;

            encoder = new HardwareEncoder
            {
                ForceSoftwareEncoding = forceSoftwareEncoding
            };
        }

        public IEnumerator RunCoroutine()
        {
            float originalFixedDelta = Time.fixedDeltaTime;
            float originalMaxDelta = Time.maximumDeltaTime;
            int originalCaptureFramerate = Time.captureFramerate;
            double originalPlanetariumDelta = Planetarium.fetch.fixedDeltaTime;
            float simFrameDelta = 1f / simulationFps;

            try
            {
                TimeWarp_FixedDeltaTime_Patch.OverrideValue = simFrameDelta;
                TimeWarp_FixedDeltaTime_Patch.IsOverridden = true;
                Time.fixedDeltaTime = simFrameDelta;
                Time.maximumDeltaTime = simFrameDelta;
                Time.captureFramerate = simulationFps;
                Planetarium.fetch.fixedDeltaTime = simFrameDelta;
                Planetarium.TimeScale = 1.0;

                SetupRenderTargets();
                SetupEncoder();

                DeterministicCaptureSession.CaptureFPS = 0f;

                realTimeAtLastSample = Time.realtimeSinceStartup;
                framesSinceSpeedSample = 0;

                for (int i = 0; i < totalFrames; i++)
                {
                    if (usingZeroCopyPath)
                    {
                        int renderIdx = i % 2;
                        int encodeIdx = (i + 1) % 2;

                        captureBuffer.Clear();
                        captureBuffer.Blit(BuiltinRenderTextureType.CurrentActive, renderTextures[renderIdx]);

                        yield return new WaitForEndOfFrame();

                        if (i > 0)
                        {
                            Graphics.WaitOnAsyncGraphicsFence(prevFence);

                            IntPtr nativeTexPtr = renderTextures[encodeIdx].GetNativeTexturePtr();
                            zeroCopyEncoder.EncodeFrame(nativeTexPtr, i - 1);
                        }

                        prevFence = captureBuffer.CreateAsyncGraphicsFence();
                    }
                    else
                    {
                        yield return new WaitForEndOfFrame();

                        RenderTexture.active = renderTextures[0];
                        readbackTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        readbackTexture.Apply(false, false);
                        RenderTexture.active = null;

                        NativeArray<byte> data = new NativeArray<byte>(
                            readbackTexture.GetRawTextureData<byte>(),
                            Allocator.Persistent
                        );
                        encoder.EncodeFrame(data);
                    }

                    DeterministicCaptureSession.CapturedFrames = i + 1;
                    DeterministicCaptureSession.CapturedSeconds = (i + 1) * simFrameDelta;

                    framesSinceSpeedSample++;
                    if (framesSinceSpeedSample >= SimSpeedWindowFrames)
                    {
                        float realNow = Time.realtimeSinceStartup;
                        float realDelta = realNow - realTimeAtLastSample;

                        // Calculate actual capture FPS (frames per real-world second)
                        float captureFps = realDelta > 0.0001f ? SimSpeedWindowFrames / realDelta : 0f;
                        DeterministicCaptureSession.CaptureFPS = captureFps;

                        realTimeAtLastSample = realNow;
                        framesSinceSpeedSample = 0;
                    }
                }

                if (usingZeroCopyPath && totalFrames > 0)
                {
                    int lastIdx = (totalFrames - 1) % 2;
                    Graphics.WaitOnAsyncGraphicsFence(prevFence);

                    IntPtr lastTexPtr = renderTextures[lastIdx].GetNativeTexturePtr();
                    zeroCopyEncoder.EncodeFrame(lastTexPtr, totalFrames - 1);
                }

                Debug.Log("[OfflineCapture] Deterministic capture complete.");
            }
            finally
            {
                TimeWarp_FixedDeltaTime_Patch.IsOverridden = false;
                Time.captureFramerate = 0;
                Time.fixedDeltaTime = originalFixedDelta;
                Time.maximumDeltaTime = originalMaxDelta;
                Planetarium.fetch.fixedDeltaTime = originalPlanetariumDelta;

                FinalizeEncoder();

                if (camera != null)
                    camera.targetTexture = null;
            }
        }

        private void SetupRenderTargets()
        {
            readbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            renderTextures = new RenderTexture[2];
            for (int i = 0; i < 2; i++)
            {
                renderTextures[i] = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                renderTextures[i].Create();
            }

            if (useGpuZeroCopy)
            {
                captureBuffer = new CommandBuffer
                {
                    name = "ZeroCopy Camera Capture"
                };

                // IMPORTANT: This runs DURING camera rendering
                captureBuffer.Blit(
                    BuiltinRenderTextureType.CurrentActive,
                    renderTextures[0]
                );

                camera.AddCommandBuffer(
                    CameraEvent.AfterImageEffects,
                    captureBuffer
                );
            }
            else
            {
                captureBuffer = new CommandBuffer
                {
                    name = "Deterministic World Capture"
                };

                captureBuffer.Blit(
                    BuiltinRenderTextureType.CurrentActive,
                    renderTextures[0]
                );

                camera.AddCommandBuffer(
                    CameraEvent.AfterImageEffects,
                    captureBuffer
                );
            }
        }


        private void SetupEncoder()
        {
            usingZeroCopyPath = useGpuZeroCopy;

            if (usingZeroCopyPath)
            {
                // Initialize with first texture - native will extract device
                IntPtr firstTexturePtr = renderTextures[0].GetNativeTexturePtr();

                zeroCopyEncoder = new AmfZeroCopyEncoder();
                if (!zeroCopyEncoder.Initialize(width, height, playbackFps, outputPath, firstTexturePtr))
                {
                    Debug.LogError("[OfflineCapture] Zero-copy encoder failed to init, falling back to standard hardware encoder");
                    usingZeroCopyPath = false;
                    // Fall through to standard init
                }
                else
                {
                    Debug.Log("[OfflineCapture] Using GPU Zero-Copy encoding path");

                    // Ensure camera has no target initially (we set it per-frame)
                    if (camera != null)
                        camera.targetTexture = null;

                    return;  // Success on zero-copy path
                }
            }

            // Standard path (existing behavior)
            if (!encoder.Initialize(width, height, playbackFps, outputPath))
                throw new Exception("Failed to initialize encoder");
        }

        private void FinalizeEncoder()
        {
            // Cleanup CommandBuffer if used (standard path)
            if (captureBuffer != null && camera != null)
            {
                camera.RemoveCommandBuffer(CameraEvent.AfterImageEffects, captureBuffer);
                captureBuffer.Release();
                captureBuffer = null;
            }

            if (usingZeroCopyPath && zeroCopyEncoder != null)
            {
                zeroCopyEncoder.Shutdown();
                zeroCopyEncoder.Dispose();
                zeroCopyEncoder = null;
                Debug.Log("[OfflineCapture] Zero-copy encoder finalized");
            }
            else if (encoder != null)
            {
                encoder.RequestStop();
                encoder.Dispose();
            }

            // Cleanup render textures
            if (renderTextures != null)
            {
                for (int i = 0; i < 2; i++)
                {
                    if (renderTextures[i] != null)
                    {
                        renderTextures[i].Release();
                        UnityEngine.Object.Destroy(renderTextures[i]);
                        renderTextures[i] = null;
                    }
                }
                renderTextures = null;
            }

            if (readbackTexture != null)
            {
                UnityEngine.Object.Destroy(readbackTexture);
                readbackTexture = null;
            }
        }
    }
}