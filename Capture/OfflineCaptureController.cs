using CinematicRecorder.Audio;
using CinematicRecorder.Core;
using System;
using System.Collections;
using System.IO;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinematicRecorder.Capture
{
    /// <summary>
    /// Manages deterministic offline rendering with physics timestep control and hardware encoding.
    /// </summary>
    public sealed class OfflineCaptureController
    {
        #region Fields
        private readonly Camera camera;
        private readonly int width;
        private readonly int height;
        private readonly int playbackFps;
        private readonly string outputPath;
        private readonly bool useGpuZeroCopy;

        private AmfZeroCopyEncoder zeroCopyEncoder;
        private bool usingZeroCopyPath;
        private GraphicsFence prevFence;
        private NvencZeroCopyEncoder nvencZeroCopyEncoder;
        private bool usingNvencPath;

        private const int SimSpeedWindowFrames = 5;
        private int framesSinceSpeedSample = 0;
        private float realTimeAtLastSample;


        private float simFrameDelta;  // Current physics step size, updated each frame
        private int frameIndex;
        private float startTime;

        private RenderTexture[] renderTextures;
        private Texture2D readbackTexture;
        private HardwareEncoder encoder;
        private CommandBuffer captureBuffer;

        private int actualCapturedFrames = 0;
        private readonly AudioCaptureController _audioController;

        // NEW: Temporal Accumulation Blur state
        private bool _isTabEnabled = false;
        private int _tabSubFrameCount = 8;
        private int _currentSubFrameIndex = 0;

        // NEW: Jitter state for TAB mode only (Halton sequence sub-pixel sampling)
        private Vector2[] _haltonOffsets = new Vector2[8];          // Raw offsets for debug logging and future shader use
        private bool _projectionJitterEnabled;                      // True only during TAB sub-frame loop
        #endregion

        #region Constructor
        public OfflineCaptureController(
                    Camera camera,
                    int width,
                    int height,
                    int simulationFps,
                    int playbackFps,
                    float durationSeconds,
                    string outputPath,
                    bool forceSoftwareEncoding,
                    bool useGpuZeroCopy,
                    AudioCaptureController audioController)
        {
            this.camera = camera;
            this.width = width;
            this.height = height;
            // Store original for reference, but we use dynamic calculation now
            this.playbackFps = playbackFps;
            this.outputPath = outputPath;
            this.useGpuZeroCopy = useGpuZeroCopy;
            this._audioController = audioController;

            encoder = new HardwareEncoder
            {
                ForceSoftwareEncoding = forceSoftwareEncoding
            };
        }
        // OfflineCaptureController constructor
        #endregion

        #region Public API
        public string OutputPath => outputPath;
        public AudioCaptureController AudioController => _audioController;
        /// <summary>
        /// Primary coroutine that manages the full capture lifecycle: initialization, capture loop, and finalization.
        /// Restores original time settings in finally block to ensure game state is preserved.
        /// </summary>
        public IEnumerator RunCoroutine()
        {
            float originalFixedDelta = Time.fixedDeltaTime;
            float originalMaxDelta = Time.maximumDeltaTime;
            int originalCaptureFramerate = Time.captureFramerate;
            double originalPlanetariumDelta = Planetarium.fetch.fixedDeltaTime;

            try
            {
                yield return InitializeCaptureSession();
                yield return RunCaptureLoop();
                yield return FinalizeEncoding();
            }
            finally
            {
                Cleanup(originalFixedDelta, originalMaxDelta, originalCaptureFramerate, originalPlanetariumDelta);
            }
        }
        #endregion

        #region Capture Session
        /// <summary>
        /// Ramps physics timestep from current to target over multiple frames to avoid physics explosions.
        /// Configures double-buffered render targets and initializes appropriate encoder.
        /// </summary>
        private IEnumerator InitializeCaptureSession()
        {
            startTime = Time.realtimeSinceStartup;
            actualCapturedFrames = 0;
            frameIndex = 0;

            TimeWarp_FixedDeltaTime_Patch.IsOverridden = true;

            // Linear ramp from current physics step to target
            float currentDelta = Time.fixedDeltaTime;
            float targetSimFps = DeterministicCaptureSession.GetCurrentSimulationFps();
            float targetDelta = 1f / targetSimFps;

            const float LINEAR_STEP = 0.001f; // 1ms per frame step

            // Ramp if we're far enough from target
            while (Mathf.Abs(currentDelta - targetDelta) > LINEAR_STEP)
            {
                if (currentDelta > targetDelta)
                    currentDelta -= LINEAR_STEP;
                else
                    currentDelta += LINEAR_STEP; // Shouldn't happen normally but handle it

                // Update patch AND Unity every frame during ramp
                TimeWarp_FixedDeltaTime_Patch.OverrideValue = currentDelta;
                Time.fixedDeltaTime = currentDelta;
                Time.maximumDeltaTime = currentDelta;
                if (Planetarium.fetch != null)
                    Planetarium.fetch.fixedDeltaTime = currentDelta;

                yield return null; // One frame per step
            }

            // Final snap to exact target
            simFrameDelta = targetDelta;
            TimeWarp_FixedDeltaTime_Patch.OverrideValue = targetDelta;
            Time.fixedDeltaTime = targetDelta;
            Time.maximumDeltaTime = targetDelta;
            Time.captureFramerate = Mathf.RoundToInt(targetSimFps);
            Planetarium.fetch.fixedDeltaTime = targetDelta;
            Planetarium.TimeScale = 1.0;

            SetupRenderTargets();
            SetupEncoder();

            // NEW: Configure TAB if applicable
            if (usingZeroCopyPath && !SessionState.PngSequence)
            {
                SetupTemporalAccumulation();
            }

            if (_audioController != null)
                _audioController.Initialize();

            DeterministicCaptureSession.CaptureFPS = 0f;
            realTimeAtLastSample = Time.realtimeSinceStartup;

            yield break;
        }

        // NEW: Setup TAB mode on encoder if enabled in SessionState
        private void SetupTemporalAccumulation()
        {
            // Only enable TAB if zero-copy path is active and user enabled it in settings
            if (SessionState.EnableTemporalAccumulation && usingZeroCopyPath)
            {
                _isTabEnabled = true;
                _tabSubFrameCount = SessionState.TabSubFrameCount > 0 ? SessionState.TabSubFrameCount : 8;

                if (usingNvencPath && nvencZeroCopyEncoder != null)
                {
                    // NVENC path - deferred for later implementation
                    _isTabEnabled = false;
                    Debug.LogWarning("[OfflineCapture] TAB requested but NVENC not yet implemented, disabling TAB");
                }
                else if (usingZeroCopyPath && zeroCopyEncoder != null)
                {
                    bool success = zeroCopyEncoder.EnableTemporalAccumulation(true, _tabSubFrameCount, SessionState.TabSigma);
                    if (!success)
                    {
                        _isTabEnabled = false;
                        Debug.LogError("[OfflineCapture] Failed to enable TAB on AMF encoder");
                    }
                    else
                    {
                        Debug.Log($"[OfflineCapture] Temporal Accumulation Blur enabled ({_tabSubFrameCount} sub-frames)");
                        
                        // Configure sharpening if enabled
                        if (SessionState.TabEnableSharpening)
                        {
                            bool sharpenSuccess = zeroCopyEncoder.SetTabSharpening(true, SessionState.TabSharpeningStrength);
                            if (!sharpenSuccess)
                            {
                                Debug.LogWarning("[OfflineCapture] Failed to configure sharpening");
                            }
                            else
                            {
                                Debug.Log($"[OfflineCapture] Sharpening enabled (strength={SessionState.TabSharpeningStrength:F2})");
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Main capture loop supporting both limited duration and unlimited (manual stop) modes.
        /// Updates physics timestep dynamically based on SessionState time scale changes.
        /// </summary>
        private IEnumerator RunCaptureLoop()
        {
            framesSinceSpeedSample = 0;
            bool isUnlimited = DeterministicCaptureSession.IsUnlimitedMode;

            // While-loop supports both unlimited and limited modes
            while (true)
            {
                // Check stop request first (works for both modes)
                if (DeterministicCaptureSession.StopRequested)
                {
                    UnityEngine.Debug.Log("[OfflineCapture] Stop requested, finishing up...");
                    break;
                }

                // For limited mode, check if we've reached target duration
                if (!isUnlimited && DeterministicCaptureSession.AccumulatedSimulatedSeconds > DeterministicCaptureSession.TargetSeconds + 0.0001f)
                    break;

                DeterministicCaptureSession.UpdateTimeScale();

                float currentSimFps = DeterministicCaptureSession.GetCurrentSimulationFps();

                simFrameDelta = 1f / currentSimFps;
                Time.fixedDeltaTime = simFrameDelta;
                Time.maximumDeltaTime = simFrameDelta;
                Time.captureFramerate = Mathf.RoundToInt(currentSimFps);
                TimeWarp_FixedDeltaTime_Patch.OverrideValue = simFrameDelta;
                if (Planetarium.fetch != null)
                    Planetarium.fetch.fixedDeltaTime = simFrameDelta;

                if (simFrameDelta < 0.0001f)
                {
                    UnityEngine.Debug.LogError("[OfflineCapture] Sim frame delta too small, clamping to 0.0001s");
                    simFrameDelta = 0.0001f;
                }

                // MODIFIED: Branch based on TAB mode
                if (_isTabEnabled && usingZeroCopyPath)
                {
                    // Temporal Accumulation Blur path: 16-step cycle per output frame
                    yield return RunTabCaptureCycle(currentSimFps);
                }
                else
                {
                    // Standard path: 1 step per frame
                    yield return RunStandardCaptureStep(currentSimFps);
                }

                DeterministicCaptureSession.UpdateProgress(
                    actualCapturedFrames,
                    DeterministicCaptureSession.AccumulatedSimulatedSeconds,
                    DeterministicCaptureSession.CaptureFPS
                );

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

                frameIndex++;
            }
        }

        // NEW: Standard single-step capture (original behavior)
        private IEnumerator RunStandardCaptureStep(float currentSimFps)
        {
            _audioController?.CaptureSubFrame(simFrameDelta);

            if (usingZeroCopyPath)
            {
                yield return CaptureFrameZeroCopy(frameIndex);
            }
            else
            {
                yield return CaptureFrameStandard();
            }

            DeterministicCaptureSession.AccumulatedSimulatedSeconds += simFrameDelta;

            _audioController?.FinalizeOutputFrame(currentSimFps);

            DeterministicCaptureSession.InvokeOnPhysicsStepped(simFrameDelta);
        }

        // NEW: TAB 16-step cycle (8 rendered + 8 skipped for 180° shutter)
        private IEnumerator RunTabCaptureCycle(float currentSimFps)
        {
            float stepDelta = simFrameDelta / 16.0f;

            // CRITICAL: Increase capture rate to match micro-step timing
            // Each WaitForEndOfFrame should take stepDelta seconds, not simFrameDelta
            int microStepRate = Mathf.RoundToInt(currentSimFps * 16);
            Time.captureFramerate = microStepRate;
            Time.fixedDeltaTime = stepDelta;
            Time.maximumDeltaTime = stepDelta;
            TimeWarp_FixedDeltaTime_Patch.OverrideValue = stepDelta;
            if (Planetarium.fetch != null)
                Planetarium.fetch.fixedDeltaTime = stepDelta;

            _projectionJitterEnabled = true;

            // Steps 1-8: Shutter Open - Render and accumulate sub-frames
            for (_currentSubFrameIndex = 0; _currentSubFrameIndex < _tabSubFrameCount; _currentSubFrameIndex++)
            {
                _audioController?.CaptureSubFrame(stepDelta);

                // CRITICAL: Read CameraTools' current intended matrix (FOV may have changed)
                Matrix4x4 cameraToolsMatrix = camera.projectionMatrix;

                // Apply Halton jitter to a COPY of CameraTools matrix (calculated on-the-fly)
                // ±0.707 pixel offset for sub-pixel AA (1/√2 for diagonal coverage)
                Vector2 h = HaltonSequence.Sequence23[_currentSubFrameIndex];
                float offsetX = (h.x - 0.5f) * 2.828f / width;
                float offsetY = (h.y - 0.5f) * 2.828f / height;

                Matrix4x4 jitteredMatrix = cameraToolsMatrix;
                jitteredMatrix[0, 2] += offsetX;  // m02 - horizontal shift
                jitteredMatrix[1, 2] += offsetY;  // m12 - vertical shift

                // Apply jittered matrix for rendering
                camera.projectionMatrix = jitteredMatrix;

                Debug.Log($"[OfflineCapture] Jitter applied: subFrame={_currentSubFrameIndex}, offset=({h.x:F4}, {h.y:F4}), baseFOV={cameraToolsMatrix[1,1]:F4}");

                yield return CaptureTabSubFrame(_currentSubFrameIndex);

                // CRITICAL: Restore CameraTools' clean matrix immediately after render
                // This ensures:
                // 1. CameraTools sees its intended FOV during physics/InvokeOnPhysicsStepped
                // 2. Next iteration reads fresh CameraTools state (no jitter accumulation)
                // NOTE: Use ResetProjectionMatrix() to resume Unity's FOV-driven matrix computation
                camera.ResetProjectionMatrix();

                DeterministicCaptureSession.AccumulatedSimulatedSeconds += stepDelta;
                DeterministicCaptureSession.InvokeOnPhysicsStepped(stepDelta);

                if (DeterministicCaptureSession.StopRequested)
                    yield break;
            }

            _projectionJitterEnabled = false;

            // Steps 9-16: Shutter Closed - Physics continues but no render
            int skippedFrames = _tabSubFrameCount;
            for (int skip = 0; skip < skippedFrames; skip++)
            {
                _audioController?.CaptureSubFrame(stepDelta);

                yield return new WaitForEndOfFrame();

                DeterministicCaptureSession.AccumulatedSimulatedSeconds += stepDelta;
                DeterministicCaptureSession.InvokeOnPhysicsStepped(stepDelta);

                if (DeterministicCaptureSession.StopRequested)
                    yield break;
            }

            // Restore capture rate for next outer loop iteration
            Time.captureFramerate = Mathf.RoundToInt(currentSimFps);

            if (!DeterministicCaptureSession.StopRequested)
            {
                bool success = FinalizeTemporalFrame(actualCapturedFrames);
                if (!success)
                    Debug.LogError("[OfflineCapture] TAB finalization failed");

                _audioController?.FinalizeOutputFrame(currentSimFps);
                actualCapturedFrames++;
            }
        }

        // NEW: Capture single sub-frame for TAB accumulation
        private IEnumerator CaptureTabSubFrame(int subFrameIndex)
        {
            Debug.Log($"[OfflineCapture] Starting sub-frame capture for index {subFrameIndex}");

            int renderIdx = frameIndex % 2; // Still double-buffer for safety

            captureBuffer.Clear();
            captureBuffer.Blit(BuiltinRenderTextureType.CurrentActive, renderTextures[renderIdx]);

            yield return new WaitForEndOfFrame();

            IntPtr nativeTexPtr = renderTextures[renderIdx].GetNativeTexturePtr();
            Debug.Log($"[OfflineCapture] Got native texture pointer for sub-frame {subFrameIndex}: {nativeTexPtr}");

            // Submit to accumulation array
            if (usingNvencPath && nvencZeroCopyEncoder != null)
            {
                Debug.LogWarning($"[OfflineCapture] NVENC not implemented, skipping sub-frame {subFrameIndex}");
            }
            else if (usingZeroCopyPath && zeroCopyEncoder != null)
            {
                bool success = zeroCopyEncoder.SubmitSubFrame(nativeTexPtr, subFrameIndex);
                if (success)
                    Debug.Log($"[OfflineCapture] Sub-frame {subFrameIndex} submitted successfully");
                else
                    Debug.LogError($"[OfflineCapture] FAILED to submit sub-frame {subFrameIndex}");
            }
            else
            {
                Debug.LogError($"[OfflineCapture] No valid encoder path for sub-frame {subFrameIndex}");
            }
        }

        // NEW: Finalize TAB frame (compute + encode + block)
        private bool FinalizeTemporalFrame(int outputFrameIndex)
        {
            if (usingNvencPath && nvencZeroCopyEncoder != null)
            {
                // NVENC not implemented yet
                return false;
            }
            else if (usingZeroCopyPath && zeroCopyEncoder != null)
            {
                // This blocks until encode is complete (synchronous per design requirement)
                return zeroCopyEncoder.FinalizeTemporalFrame(outputFrameIndex);
            }
            return false;
        }

        // NOTE: CalculateJitteredMatrices() removed - jitter is now calculated on-the-fly
        // in RunTabCaptureCycle() to ensure CameraTools FOV changes are respected
        #endregion

        #region Frame Capture
        /// <summary>
        /// Zero-copy capture path using GPU texture handles. Double-buffers render textures to pipeline
        /// rendering and encoding asynchronously.
        /// MODIFIED: Now returns IEnumerator for coroutine compatibility with TAB mode.
        /// </summary>
        private IEnumerator CaptureFrameZeroCopy(int frameIndex)
        {
            int renderIdx = frameIndex % 2;
            int encodeIdx = (frameIndex + 1) % 2;

            captureBuffer.Clear();
            captureBuffer.Blit(BuiltinRenderTextureType.CurrentActive, renderTextures[renderIdx]);

            yield return new WaitForEndOfFrame();

            // Encode previous frame
            if (frameIndex > 0)
            {
                Graphics.WaitOnAsyncGraphicsFence(prevFence);
                IntPtr nativeTexPtr = renderTextures[encodeIdx].GetNativeTexturePtr();

                // Route to active zero-copy encoder
                if (usingNvencPath && nvencZeroCopyEncoder != null)
                {
                    nvencZeroCopyEncoder.EncodeFrame(nativeTexPtr, frameIndex - 1);
                }
                else if (usingZeroCopyPath && zeroCopyEncoder != null) // AMF
                {
                    zeroCopyEncoder.EncodeFrame(nativeTexPtr, frameIndex - 1);
                }

                actualCapturedFrames++;
            }

            prevFence = captureBuffer.CreateAsyncGraphicsFence();
        }
        /// <summary>
        /// Standard CPU readback path using Texture2D.ReadPixels. Slower but universally compatible.
        /// </summary>
        private IEnumerator CaptureFrameStandard()
        {
            yield return new WaitForEndOfFrame();

            RenderTexture.active = renderTextures[0];
            readbackTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            readbackTexture.Apply(false, false);
            RenderTexture.active = null;

            if (SessionState.PngSequence)
            {
                // PNG Mode: Encode to PNG and write to disk synchronously
                byte[] pngData = readbackTexture.EncodeToPNG();
                string frameFileName = Path.Combine(outputPath, $"frame_{actualCapturedFrames + 1:D6}.png");
                File.WriteAllBytes(frameFileName, pngData);
            }
            else
            {
                // Video Mode: Send to hardware encoder
                NativeArray<byte> data = new NativeArray<byte>(
                    readbackTexture.GetRawTextureData<byte>(),
                    Allocator.Persistent
                );
                encoder.EncodeFrame(data);
            }

            actualCapturedFrames++;
            yield break;
        }
        /// <summary>
        /// Encodes the final pending frame from the zero-copy double buffer and logs capture statistics.
        /// MODIFIED: Handles TAB mode where finalization is done in the loop.
        /// </summary>
        private IEnumerator FinalizeEncoding()
        {
            // If TAB mode, the final frame was already encoded in the loop
            // Just ensure any pending operations complete
            if (_isTabEnabled)
            {
                UnityEngine.Debug.Log($"[OfflineCapture] TAB capture complete. Encoded {actualCapturedFrames} output frames " +
                    $"({DeterministicCaptureSession.AccumulatedSimulatedSeconds:F2}s simulated)");
                yield break;
            }

            // Standard path: Encode final buffered frame
            if (usingZeroCopyPath && actualCapturedFrames > 0)
            {
                int lastIdx = (actualCapturedFrames - 1) % 2;
                Graphics.WaitOnAsyncGraphicsFence(prevFence);
                IntPtr lastTexPtr = renderTextures[lastIdx].GetNativeTexturePtr();
                zeroCopyEncoder.EncodeFrame(lastTexPtr, actualCapturedFrames - 1);
            }

            UnityEngine.Debug.Log($"[OfflineCapture] Deterministic capture complete. Captured {actualCapturedFrames} frames " +
                $"({DeterministicCaptureSession.AccumulatedSimulatedSeconds:F2}s simulated)");
            yield break;
        }
        #endregion

        #region Encoder Setup
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
        /// <summary>
        /// Selects and initializes the best available encoder: NVENC Zero-Copy -> AMF Zero-Copy -> Standard Hardware -> CPU.
        /// </summary>
        private void SetupEncoder()
        {
            // >>> CRITICAL FIX: PNG Sequence mode must skip ALL video encoder initialization
            if (SessionState.PngSequence)
            {
                UnityEngine.Debug.Log("[OfflineCapture] PNG Sequence mode active - skipping video encoder initialization");
                usingZeroCopyPath = false;
                usingNvencPath = false;
                // Do NOT initialize 'encoder' at all - we won't use it
                return;
            }

            // Only proceed with video encoder setup if NOT in PNG mode
            if (!useGpuZeroCopy)
            {
                // CPU/Software path only
                InitCpuEncoder();
                return;
            }

            // GPU Zero-Copy Priority: NVENC -> AMF -> CPU
            if (TryInitNvenc())
            {
                Debug.Log("[OfflineCapture] Using NVENC Zero-Copy path");
                return;
            }

            if (TryInitAmf())
            {
                Debug.Log("[OfflineCapture] Using AMF Zero-Copy path");
                return;
            }

            Debug.Log("[OfflineCapture] GPU zero-copy unavailable, attempting standard hardware encoder...");
            if (encoder.Initialize(width, height, playbackFps, outputPath))
            {
                usingZeroCopyPath = false;
                return;
            }

            Debug.LogWarning("[OfflineCapture] All hardware encoders failed, falling back to CPU");
            InitCpuEncoder();
        }
        /// <summary>
        /// Attempts to initialize NVENC zero-copy encoder. Fails gracefully on non-NVIDIA systems.
        /// </summary>
        private bool TryInitNvenc()
        {
            // Quick check: Only try NVENC if we have a chance (avoid log spam on AMD systems)
            // This checks for nvEncodeAPI64.dll presence via native code, but we can also check here
            // by attempting a dummy load, or just let the native code handle it gracefully.

            nvencZeroCopyEncoder = new NvencZeroCopyEncoder();

            var settings = new NvencZeroCopyEncoder.NvencEncoderSettings
            {
                RateControlMode = SessionState.NvencRateControlMode,
                TargetBitrateKbps = SessionState.NvencTargetBitrate,
                QpI = SessionState.NvencCqValue,
                QpP = SessionState.NvencCqValue,  // NVENC CQP uses same QP for I/P usually, or P+2
                QpB = SessionState.NvencCqValue + 2,
                QualityPreset = SessionState.NvencPreset, // 0,1,2 maps to P1,P4,P7
                Codec = 1, // HEVC primary
                GopSize = playbackFps * 2,
                Reserved1 = 0,
                Reserved2 = 0
            };

            IntPtr firstTexturePtr = renderTextures[0].GetNativeTexturePtr();

            if (!nvencZeroCopyEncoder.Initialize(width, height, playbackFps, outputPath, firstTexturePtr, settings))
            {
                Debug.LogWarning("[OfflineCapture] NVENC initialization failed (expected on non-NVIDIA systems)");
                nvencZeroCopyEncoder.Dispose();
                nvencZeroCopyEncoder = null;
                return false;
            }

            usingNvencPath = true;
            usingZeroCopyPath = true;

            if (camera != null)
                camera.targetTexture = null;

            return true;
        }
        /// <summary>
        /// Attempts to initialize AMF zero-copy encoder for AMD GPUs.
        /// </summary>
        private bool TryInitAmf()
        {
            zeroCopyEncoder = new AmfZeroCopyEncoder();

            var amfSettings = new AmfZeroCopyEncoder.AmfEncoderSettings
            {
                RateControlMode = SessionState.AmfRateControlMode,
                TargetBitrateKbps = SessionState.AmfTargetBitrate * 1000,
                QpI = SessionState.AmfCqpValue,
                QpP = SessionState.AmfCqpValue + 2,
                QpB = SessionState.AmfCqpValue + 4,
                QualityPreset = SessionState.AmfEncoderSpeed,
                Codec = 1,
                GopSize = playbackFps * 2,
                EnableVbaq = 1,
                UseBlueNoiseDither = SessionState.AmfUseBlueNoiseDither ? 1 : 0,
            };

            IntPtr firstTexturePtr = renderTextures[0].GetNativeTexturePtr();

            if (!zeroCopyEncoder.Initialize(width, height, playbackFps, outputPath, firstTexturePtr, amfSettings))
            {
                zeroCopyEncoder.Dispose();
                zeroCopyEncoder = null;
                return false;
            }

            usingZeroCopyPath = true;
            if (camera != null)
                camera.targetTexture = null;

            return true;
        }
        private void InitCpuEncoder()
        {
            if (!encoder.Initialize(width, height, playbackFps, outputPath))
                throw new Exception("Failed to initialize CPU encoder");
            usingZeroCopyPath = false;
            usingNvencPath = false;
        }
        private void FinalizeEncoder()
        {
            if (captureBuffer != null && camera != null)
            {
                camera.RemoveCommandBuffer(CameraEvent.AfterImageEffects, captureBuffer);
                captureBuffer.Release();
                captureBuffer = null;
            }

            // NVENC cleanup
            if (usingNvencPath && nvencZeroCopyEncoder != null)
            {
                nvencZeroCopyEncoder.Shutdown();
                nvencZeroCopyEncoder.Dispose();
                nvencZeroCopyEncoder = null;
                usingNvencPath = false;
                Debug.Log("[OfflineCapture] NVENC encoder finalized");
            }

            // AMF cleanup
            if (usingZeroCopyPath && zeroCopyEncoder != null && !usingNvencPath)
            {
                zeroCopyEncoder.Shutdown();
                zeroCopyEncoder.Dispose();
                zeroCopyEncoder = null;
                Debug.Log("[OfflineCapture] AMF encoder finalized");
            }

            // Standard encoder cleanup
            if (!usingZeroCopyPath && encoder != null)
            {
                encoder.RequestStop();
                encoder.Dispose();
            }

            // Texture cleanup 
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
        #endregion

        #region Cleanup
        private void Cleanup(float originalFixedDelta, float originalMaxDelta, int originalCaptureFramerate, double originalPlanetariumDelta)
        {
            TimeWarp_FixedDeltaTime_Patch.IsOverridden = false;
            Time.captureFramerate = 0;
            Time.fixedDeltaTime = originalFixedDelta;
            Time.maximumDeltaTime = originalMaxDelta;
            if (Planetarium.fetch != null)
                Planetarium.fetch.fixedDeltaTime = originalPlanetariumDelta;

            if (_audioController != null)
            {
                _audioController.Shutdown();
                _audioController.Dispose();
            }

            FinalizeEncoder();

            if (camera != null)
            {
                // Note: We don't restore projection matrix here because
                // CameraTools manages its own matrix. Forcing a restore would
                // overwrite CameraTools' intended FOV state.
                camera.targetTexture = null;
            }

            if (DeterministicCaptureSession.IsRunning)
            {
                DeterministicCaptureSession.UpdateProgress(
                    actualCapturedFrames,
                    DeterministicCaptureSession.AccumulatedSimulatedSeconds,
                    0f
                );
            }
        }
        #endregion
    }
}