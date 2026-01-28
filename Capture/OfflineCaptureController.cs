// File: Capture/OfflineCaptureController.cs
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
        private int totalFrames; // Not readonly anymore - can be extended
        private readonly string outputPath;
        private readonly bool useGpuZeroCopy;
        private AmfZeroCopyEncoder zeroCopyEncoder;
        private bool usingZeroCopyPath;
        private GraphicsFence prevFence;

        // Sim speed tracking (rolling window)
        private const int SimSpeedWindowFrames = 5;
        private int framesSinceSpeedSample = 0;
        private float realTimeAtLastSample;
        private float simFrameDelta;

        private RenderTexture[] renderTextures; // Double-buffered: [0] and [1]
        private Texture2D readbackTexture;
        private HardwareEncoder encoder;
        private CommandBuffer captureBuffer;

        // Track actual frames captured for final report
        private int actualCapturedFrames = 0;
        private float startTime;
        public string OutputPath => outputPath;

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
            // Store original values for restoration
            float originalFixedDelta = Time.fixedDeltaTime;
            float originalMaxDelta = Time.maximumDeltaTime;
            int originalCaptureFramerate = Time.captureFramerate;
            double originalPlanetariumDelta = Planetarium.fetch.fixedDeltaTime;

            try
            {
                // Phase 1: Setup (physics ramp will go here later)
                yield return InitializeCaptureSession();

                // Phase 2: Main capture loop
                yield return RunCaptureLoop();

                // Phase 3: Drain encoder queues
                yield return FinalizeEncoding();
            }
            finally
            {
                Cleanup(originalFixedDelta, originalMaxDelta, originalCaptureFramerate, originalPlanetariumDelta);
            }
        }

        private IEnumerator InitializeCaptureSession()
        {
            simFrameDelta = 1f / simulationFps;
            startTime = Time.realtimeSinceStartup;
            actualCapturedFrames = 0;

            // Physics setup (ramp will replace these 3 lines later)
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

            yield break;
        }

        private IEnumerator RunCaptureLoop()
        {
            float realTimeAtLastSample = Time.realtimeSinceStartup;
            int framesSinceSpeedSample = 0;
            int currentTotalFrames = totalFrames; // Snap current target (handles extension)

            for (int i = 0; i < currentTotalFrames; i++)
            {
                if (DeterministicCaptureSession.StopRequested)
                {
                    UnityEngine.Debug.Log("[OfflineCapture] Stop requested, finishing up...");
                    break;
                }

                // Handle duration extension mid-recording
                if (totalFrames != currentTotalFrames)
                {
                    currentTotalFrames = DeterministicCaptureSession.TargetFrames;
                }

                // Capture frame based on path
                if (usingZeroCopyPath)
                {
                    yield return CaptureFrameZeroCopy(i);
                }
                else
                {
                    yield return CaptureFrameStandard();
                }

                // Update progress
                float currentSimSeconds = actualCapturedFrames * simFrameDelta;
                DeterministicCaptureSession.UpdateProgress(
                    actualCapturedFrames,
                    currentSimSeconds,
                    DeterministicCaptureSession.CaptureFPS
                );

                // Rolling FPS calculation
                framesSinceSpeedSample++;
                if (framesSinceSpeedSample >= SimSpeedWindowFrames)
                {
                    float realNow = Time.realtimeSinceStartup;
                    float realDelta = realNow - realTimeAtLastSample;
                    float captureFps = realDelta > 0.0001f ? SimSpeedWindowFrames / realDelta : 0f;
                    DeterministicCaptureSession.CaptureFPS = captureFps;
                    realTimeAtLastSample = realNow;
                    framesSinceSpeedSample = 0;
                }
            }
        }

        private IEnumerator CaptureFrameZeroCopy(int frameIndex)
        {
            int renderIdx = frameIndex % 2;
            int encodeIdx = (frameIndex + 1) % 2;

            captureBuffer.Clear();
            captureBuffer.Blit(BuiltinRenderTextureType.CurrentActive, renderTextures[renderIdx]);

            yield return new WaitForEndOfFrame();

            // Encode previous frame (frameIndex - 1) 
            if (frameIndex > 0)
            {
                Graphics.WaitOnAsyncGraphicsFence(prevFence);
                IntPtr nativeTexPtr = renderTextures[encodeIdx].GetNativeTexturePtr();
                zeroCopyEncoder.EncodeFrame(nativeTexPtr, frameIndex - 1);
                actualCapturedFrames++;
            }

            prevFence = captureBuffer.CreateAsyncGraphicsFence();
        }

        private IEnumerator CaptureFrameStandard()
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

            actualCapturedFrames++;
            yield break;
        }

        private IEnumerator FinalizeEncoding()
        {
            // Encode final buffered frame for zero-copy path
            if (usingZeroCopyPath && actualCapturedFrames > 0)
            {
                int lastIdx = (actualCapturedFrames - 1) % 2;
                Graphics.WaitOnAsyncGraphicsFence(prevFence);
                IntPtr lastTexPtr = renderTextures[lastIdx].GetNativeTexturePtr();
                zeroCopyEncoder.EncodeFrame(lastTexPtr, actualCapturedFrames - 1);
            }

            UnityEngine.Debug.Log($"[OfflineCapture] Deterministic capture complete. Captured {actualCapturedFrames} frames.");
            yield break;
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
                IntPtr firstTexturePtr = renderTextures[0].GetNativeTexturePtr();

                zeroCopyEncoder = new AmfZeroCopyEncoder();

                // Build AMF settings from SessionState
                var amfSettings = new AmfZeroCopyEncoder.AmfEncoderSettings
                {
                    RateControlMode = SessionState.AmfRateControlMode,
                    TargetBitrateKbps = SessionState.AmfTargetBitrate * 1000, // Convert Mbps to Kbps
                    QpI = SessionState.AmfCqpValue,
                    QpP = SessionState.AmfCqpValue + 2,  // Stagger like HardwareEncoder does
                    QpB = SessionState.AmfCqpValue + 4,
                    QualityPreset = SessionState.AmfEncoderSpeed,
                    Codec = 1,  // 0=H264, 1=HEVC - using HEVC for zero-copy path
                    GopSize = playbackFps * 2  // 2-second GOP matching HardwareEncoder
                };

                if (!zeroCopyEncoder.Initialize(
                    width,
                    height,
                    playbackFps,
                    outputPath,
                    firstTexturePtr,
                    amfSettings))  // Pass the settings struct
                {
                    UnityEngine.Debug.LogError("[OfflineCapture] Zero-copy encoder failed to init, " +
                        "falling back to standard hardware encoder");
                    usingZeroCopyPath = false;
                }
                else
                {
                    UnityEngine.Debug.Log("[OfflineCapture] Using GPU Zero-Copy encoding path with " +
                        $"RC={amfSettings.RateControlMode}, Bitrate={amfSettings.TargetBitrateKbps}kbps");

                    if (camera != null)
                        camera.targetTexture = null;

                    return;
                }
            }

            // Standard path (existing behavior)
            if (!encoder.Initialize(width, height, playbackFps, outputPath))
                throw new Exception("Failed to initialize encoder");
        }

        private void FinalizeEncoder()
        {
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
                UnityEngine.Debug.Log("[OfflineCapture] Zero-copy encoder finalized");
            }
            else if (encoder != null)
            {
                encoder.RequestStop();
                encoder.Dispose();
            }

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
        private void Cleanup(float originalFixedDelta, float originalMaxDelta, int originalCaptureFramerate, double originalPlanetariumDelta)
        {
            TimeWarp_FixedDeltaTime_Patch.IsOverridden = false;
            Time.captureFramerate = 0;
            Time.fixedDeltaTime = originalFixedDelta;
            Time.maximumDeltaTime = originalMaxDelta;
            Planetarium.fetch.fixedDeltaTime = originalPlanetariumDelta;

            FinalizeEncoder();

            if (camera != null)
                camera.targetTexture = null;

            if (DeterministicCaptureSession.IsRunning)
            {
                DeterministicCaptureSession.UpdateProgress(
                    actualCapturedFrames,
                    actualCapturedFrames * simFrameDelta,
                    0f
                );
            }
        }
    }
}