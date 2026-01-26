using System;
using System.Collections.Concurrent;
using System.IO;
using UnityEngine;
using CinematicRecorder.Core;
using Unity.Collections;

namespace CinematicRecorder.Capture
{
    public class FrameCapture : MonoBehaviour
    {
        public bool ForceSoftwareEncoding { get; set; } = false;

        [Header("Settings")]
        private int targetFPS = 60;
        private bool usePNGSequence = false;
        private string outputDirectory;

        [Header("Components")]
        private Camera targetCamera;
        private RenderCapture renderCapture;
        private HardwareEncoder hardwareEncoder;

        [Header("State")]
        private bool isRecording = false;
        private int capturedFrames = 0;
        private ConcurrentQueue<NativeArray<byte>> _frameQueue = new ConcurrentQueue<NativeArray<byte>>();

        public bool IsRecording => isRecording;
        public int CapturedFrames => capturedFrames;
        public HardwareEncoder.EncoderType ActiveEncoderType => hardwareEncoder?.ActiveEncoder ?? HardwareEncoder.EncoderType.CPU;

        // NEW: Store screen resolution when recording starts
        private int captureWidth = 0;
        private int captureHeight = 0;

        // Profiling
        private float lastReportTime;
        private int framesSinceReport;

        // Safety limits (Step 2)
        private const int MaxFramesInFlight = 2; // matches your readback cap
        private const long MaxBytesInFlight = 512L * 1024 * 1024; // 512 MB hard ceiling
        private bool stopRequested = false;

        // Instrumentation (Step 1)
        private long bytesInFlight = 0;
        private long peakBytesInFlight = 0;
        private int peakFramesInFlight = 0;
        private int droppedFrames = 0;

        public void Initialize(int fps, string outputDir, bool pngSequence)
        {
            targetFPS = fps;
            outputDirectory = outputDir;
            usePNGSequence = pngSequence;

            FindMainCamera();
        }

        private void FindMainCamera()
        {
            // Target the primary 3D camera. "FlightCamera" is the most reliable in KSP.
            GameObject flightCamObj = GameObject.Find("FlightCamera");
            if (flightCamObj != null)
            {
                targetCamera = flightCamObj.GetComponent<Camera>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
                Debug.LogWarning($"[FrameCapture] Using Camera.main ({targetCamera?.name}) as fallback.");
            }

            if (targetCamera == null)
            {
                Debug.LogError("[FrameCapture] Could not find a target camera.");
                enabled = false;
                return;
            }

            Debug.Log($"[FrameCapture] Target camera: {targetCamera.name}");

            // Attach or find the RenderCapture hook
            renderCapture = targetCamera.GetComponent<RenderCapture>();
            if (renderCapture == null)
            {
                renderCapture = targetCamera.gameObject.AddComponent<RenderCapture>();
            }
            renderCapture.FrameCaptureInstance = this;
        }

        public void StartRecording()
        {
            if (isRecording || targetCamera == null) return;
            stopRequested = false;

            // SET RESOLUTION TO SCREEN RESOLUTION
            captureWidth = Screen.width;
            captureHeight = Screen.height;

            Debug.Log($"[FrameCapture] Starting recording at {captureWidth}x{captureHeight}@{targetFPS}FPS");

            if (usePNGSequence)
            {
                // PNG sequence logic
                Debug.LogError("[FrameCapture] PNG sequence not re-implemented in this fix.");
                return;
            }

            // Initialize the hardware encoder
            string fileName = $"Cinematic_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.mkv";
            string fullPath = Path.Combine(outputDirectory, fileName);

            hardwareEncoder = new HardwareEncoder();
            hardwareEncoder.ForceSoftwareEncoding = this.ForceSoftwareEncoding;

            if (!hardwareEncoder.Initialize(captureWidth, captureHeight, targetFPS, fullPath))
            {
                Debug.LogError("[FrameCapture] Failed to initialize encoder!");
                return;
            }

            // Start the OnRenderImage capture hook
            renderCapture.StartCapture();

            isRecording = true;
            capturedFrames = 0;

            Debug.Log($"[FrameCapture] Started MKV recording with {hardwareEncoder.ActiveEncoder}");
        }

        public void StopRecording()
        {
            // Idempotent
            if (stopRequested)
                return;

            stopRequested = true;

            if (isRecording)
            {
                Debug.Log("[FrameCapture] Stop requested — entering finalization");
            }

            // Stop new frames immediately
            isRecording = false;

            // Stop GPU capture hook
            renderCapture.StopCapture();

            // DO NOT stop encoder yet — Update() will drain the queue
        }

        // Called from RenderCapture's AsyncGPUReadback callback (may be on render thread)
        public void EnqueueCapturedFrame(NativeArray<byte> frameData)
        {
            if (!frameData.IsCreated)
                return;

            int frameBytes = frameData.Length;

            if (!isRecording)
            {
                frameData.Dispose();
                return;
            }

            // STEP 2: hard safety caps
            if (_frameQueue.Count >= MaxFramesInFlight ||
                bytesInFlight + frameBytes > MaxBytesInFlight)
            {
                droppedFrames++;
                frameData.Dispose();

                if ((droppedFrames % 60) == 1)
                {
                    Debug.LogWarning(
                        $"[FrameCapture] Dropping frame — " +
                        $"Queue: {_frameQueue.Count}/{MaxFramesInFlight}, " +
                        $"Bytes: {bytesInFlight / (1024 * 1024)} MB"
                    );
                }

                return;
            }

            // Accept frame
            _frameQueue.Enqueue(frameData);
            bytesInFlight += frameBytes;

            peakBytesInFlight = Math.Max(peakBytesInFlight, bytesInFlight);
            peakFramesInFlight = Math.Max(peakFramesInFlight, _frameQueue.Count);
        }



        void Update()
        {
            // Drain frames while recording OR finalizing
            while ((isRecording || stopRequested) &&
                   _frameQueue.TryDequeue(out NativeArray<byte> frameData))
            {
                int frameBytes = frameData.Length;
                bytesInFlight -= frameBytes;

                if (hardwareEncoder != null && hardwareEncoder.IsInitialized)
                {
                    hardwareEncoder.EncodeFrame(frameData);
                    capturedFrames++;
                    framesSinceReport++;
                }
                else
                {
                    frameData.Dispose();
                }
            }

            // PERF REPORT (unchanged)
            if (framesSinceReport > 0 &&
                Time.realtimeSinceStartup - lastReportTime >= 1.0f)
            {
                Debug.Log(
                    $"[Perf] Encoded: {capturedFrames} | " +
                    $"Queue: {_frameQueue.Count} | " +
                    $"Bytes in flight: {bytesInFlight / (1024 * 1024)} MB | " +
                    $"Dropped: {droppedFrames}"
                );

                lastReportTime = Time.realtimeSinceStartup;
                framesSinceReport = 0;
            }

            // FINALIZATION COMPLETE
            if (stopRequested &&
                _frameQueue.IsEmpty &&
                bytesInFlight == 0 &&
                hardwareEncoder != null)
            {
                Debug.Log(
                    $"[FrameCapture] Finalizing recording — total frames encoded: {capturedFrames}"
                );

                hardwareEncoder.RequestStop();
                hardwareEncoder.Dispose();
                hardwareEncoder = null;

                stopRequested = false;
            }
        }

        void OnDisable()
        {
            if (isRecording)
            {
                Debug.Log("[FrameCapture] OnDisable — forcing recording stop");
                StopRecording();
            }
        }
        void OnApplicationPause(bool paused)
        {
            if (paused && isRecording)
            {
                Debug.Log("[FrameCapture] Application paused — forcing recording stop");
                StopRecording();
            }
        }
        void OnApplicationQuit()
        {
            if (isRecording)
            {
                Debug.Log("[FrameCapture] Application quitting — forcing recording stop");
                StopRecording();
            }
        }

        void OnDestroy()
        {
            StopRecording();

            // Force finalization if Unity is tearing down
            if (hardwareEncoder != null)
            {
                hardwareEncoder.RequestStop();
                hardwareEncoder.Dispose();
                hardwareEncoder = null;
            }
        }
    }
}