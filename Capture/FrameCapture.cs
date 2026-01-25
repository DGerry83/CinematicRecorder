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
            if (!isRecording)
                return;

            isRecording = false;
            renderCapture.StopCapture();

            hardwareEncoder?.RequestStop();
            hardwareEncoder = null;
        }

        // Called from RenderCapture's AsyncGPUReadback callback (may be on render thread)
        public void EnqueueCapturedFrame(NativeArray<byte> frameData)
        {
            if (isRecording)
            {
                _frameQueue.Enqueue(frameData);
            }
            else
            {
                if (frameData.IsCreated)
                    frameData.Dispose();
            }
        }



        void Update()
        {
            // Process the frame queue on the main thread.
            // This is where we feed frames to the encoder.
            while (isRecording && _frameQueue.TryDequeue(out NativeArray<byte> frameData))
            {
                if (hardwareEncoder != null && hardwareEncoder.IsInitialized)
                {
                    hardwareEncoder.EncodeFrame(frameData);
                    capturedFrames++;
                }
                // Dispose the native array after encoding
                if (frameData.IsCreated) frameData.Dispose();

                // Optional diagnostic log
                if (capturedFrames % 60 == 0)
                {
                    Debug.Log($"[FrameCapture] Encoded {capturedFrames} frames.");
                }
            }
        }

        void OnDestroy()
        {
            if (isRecording) StopRecording();
            hardwareEncoder?.Dispose();
        }
    }
}