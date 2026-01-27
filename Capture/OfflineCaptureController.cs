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

        // Sim speed tracking (rolling window)
        private const int SimSpeedWindowFrames = 5;
        private int framesSinceSpeedSample = 0;
        private float realTimeAtLastSample;

        private RenderTexture renderTexture;
        private Texture2D readbackTexture;
        private HardwareEncoder encoder;
        private CommandBuffer captureBuffer;

        public OfflineCaptureController(
            Camera camera,
            int width,
            int height,
            int simulationFps,
            int playbackFps,
            float durationSeconds,
            string outputPath,
            bool forceSoftwareEncoding)
        {
            this.camera = camera;
            this.width = width;
            this.height = height;
            this.simulationFps = simulationFps;
            this.playbackFps = playbackFps;
            this.totalFrames = Mathf.RoundToInt(durationSeconds * simulationFps);
            this.outputPath = outputPath;

            encoder = new HardwareEncoder
            {
                ForceSoftwareEncoding = forceSoftwareEncoding
            };
        }

        public IEnumerator RunCoroutine()
        {
            // Store original values
            float originalFixedDelta = Time.fixedDeltaTime;
            float originalMaxDelta = Time.maximumDeltaTime;
            int originalCaptureFramerate = Time.captureFramerate;
            double originalPlanetariumDelta = Planetarium.fetch.fixedDeltaTime;

            // Calculate our target delta (e.g., 1/240 = 0.004166...)
            float simFrameDelta = 1f / simulationFps;

            try
            {
                // Enable the Harmony patch override BEFORE changing Time settings
                TimeWarp_FixedDeltaTime_Patch.OverrideValue = simFrameDelta;
                TimeWarp_FixedDeltaTime_Patch.IsOverridden = true;

                // Set Unity physics to match
                Time.fixedDeltaTime = simFrameDelta;
                Time.maximumDeltaTime = simFrameDelta; // Prevent Unity from running multiple steps to "catch up"
                Time.captureFramerate = simulationFps; // One frame = exactly simFrameDelta seconds game time

                // Sync Planetarium to same step size (Planetarium.FixedUpdate uses this field directly)
                Planetarium.fetch.fixedDeltaTime = simFrameDelta;

                // Force 1x time scale to prevent KSP from accelerating physics
                Planetarium.TimeScale = 1.0;

                SetupRenderTargets();
                SetupEncoder();

                realTimeAtLastSample = Time.realtimeSinceStartup;
                framesSinceSpeedSample = 0;

                for (int i = 0; i < totalFrames; i++)
                {
                    // Wait for frame render
                    yield return new WaitForEndOfFrame();

                    // Capture the frame
                    ReadbackAndEncode();

                    // Update stats
                    DeterministicCaptureSession.CapturedFrames = i + 1;
                    DeterministicCaptureSession.CapturedSeconds = (i + 1) * simFrameDelta;

                    // Calculate sim speed percentage
                    framesSinceSpeedSample++;
                    if (framesSinceSpeedSample >= SimSpeedWindowFrames)
                    {
                        float realNow = Time.realtimeSinceStartup;
                        float realDelta = realNow - realTimeAtLastSample;
                        float expectedSimTime = SimSpeedWindowFrames * simFrameDelta;

                        DeterministicCaptureSession.SimSpeedPercent =
                            realDelta > 0.0001f ? (expectedSimTime / realDelta) * 100f : 100f;

                        realTimeAtLastSample = realNow;
                        framesSinceSpeedSample = 0;
                    }
                }

                Debug.Log("[OfflineCapture] Deterministic capture complete.");
            }
            finally
            {
                // CRITICAL: Disable the patch first so KSP returns to normal
                TimeWarp_FixedDeltaTime_Patch.IsOverridden = false;

                // Restore Unity time settings
                Time.captureFramerate = 0; // Return to real-time
                Time.fixedDeltaTime = originalFixedDelta;
                Time.maximumDeltaTime = originalMaxDelta;

                // Restore Planetarium
                Planetarium.fetch.fixedDeltaTime = originalPlanetariumDelta;

                FinalizeEncoder();
            }
        }




        private void SetupRenderTargets()
        {
            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            renderTexture.Create();

            readbackTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

            captureBuffer = new CommandBuffer
            {
                name = "Deterministic World Capture"
            };

            // Copy the fully rendered world (with post-processing, no UI)
            captureBuffer.Blit(BuiltinRenderTextureType.CurrentActive, renderTexture);

            // Hook AFTER world rendering, BEFORE UI
            camera.AddCommandBuffer(CameraEvent.AfterImageEffects, captureBuffer);
        }

        private void SetupEncoder()
        {
            if (!encoder.Initialize(width, height, playbackFps, outputPath))
                throw new Exception("Failed to initialize encoder");
        }

        private void ReadbackAndEncode()
        {
            RenderTexture.active = renderTexture;
            readbackTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readbackTexture.Apply(false, false);
            RenderTexture.active = null;

            NativeArray<byte> data =
                new NativeArray<byte>(
                    readbackTexture.GetRawTextureData<byte>(),
                    Allocator.Persistent);

            encoder.EncodeFrame(data);
        }

        private void FinalizeEncoder()
        {
            if (camera != null && captureBuffer != null)
            {
                camera.RemoveCommandBuffer(CameraEvent.AfterImageEffects, captureBuffer);
                captureBuffer.Release();
                captureBuffer = null;
            }

            encoder.RequestStop();
            encoder.Dispose();

            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.Destroy(renderTexture);
                renderTexture = null;
            }

            if (readbackTexture != null)
            {
                UnityEngine.Object.Destroy(readbackTexture);
                readbackTexture = null;
            }
        }
    }
}
