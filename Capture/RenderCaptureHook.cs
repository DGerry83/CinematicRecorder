using System;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace CinematicRecorder.Capture
{
    public class RenderCapture : MonoBehaviour
    {
        // Public API for FrameCapture to control us
        public FrameCapture FrameCaptureInstance { get; set; }
        private bool _isCapturing = false;
        private int _inFlightReadbacks = 0;
        private const int MAX_IN_FLIGHT = 2;
        private readonly object _readbackLock = new object();

        public void StartCapture()
        {
            if (FrameCaptureInstance == null)
            {
                Debug.LogError("[RenderCapture] No FrameCapture instance assigned!");
                return;
            }
            _isCapturing = true;
            Debug.Log("[RenderCapture] Started capturing via OnRenderImage.");
        }

        public void StopCapture()
        {
            _isCapturing = false;
            Debug.Log("[RenderCapture] Stopped capturing.");
        }

        // UNITY CALLBACK: Called after the camera finishes rendering the 3D scene,
        // but before the final pass to screen. 'source' is the clean, post-processed buffer.
        void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            // 1. Pass the image through the pipeline (required)
            Graphics.Blit(source, destination);

            // 2. If we're recording, capture this pristine frame
            if (_isCapturing && FrameCaptureInstance != null && FrameCaptureInstance.IsRecording)
            {
                RenderTexture temp = RenderTexture.GetTemporary(
                    source.width, source.height, 0, source.format);

                // Vertical flip
                Graphics.Blit(
                    source,
                    temp,
                    new Vector2(1f, -1f),
                    new Vector2(0f, 1f));

                CaptureFrame(temp);
                RenderTexture.ReleaseTemporary(temp);
            }
        }

        private void CaptureFrame(RenderTexture source)
        {
            if (!_isCapturing)
                return;

            lock (_readbackLock)
            {
                if (_inFlightReadbacks >= MAX_IN_FLIGHT)
                    return; // Skip issuing readback this frame
                _inFlightReadbacks++;
            }

            AsyncGPUReadback.Request(
                source,
                0,
                TextureFormat.RGBA32,
                OnReadbackComplete);
        }
        public void WaitForReadbacksToComplete()
        {
            while (true)
            {
                lock (_readbackLock)
                {
                    if (_inFlightReadbacks == 0)
                        return;
                }

                System.Threading.Thread.Sleep(1);
            }
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            lock (_readbackLock)
            {
                _inFlightReadbacks--;
            }

            if (!_isCapturing)
                return;

            if (request.hasError)
            {
                Debug.LogError("[RenderCapture] GPU readback failed.");
                return;
            }

            var src = request.GetData<byte>();
            var copy = new NativeArray<byte>(src.Length, Allocator.Persistent);
            NativeArray<byte>.Copy(src, copy);

            FrameCaptureInstance?.EnqueueCapturedFrame(copy);
        }
    }
}