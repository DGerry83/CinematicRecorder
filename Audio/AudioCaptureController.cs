using System;
using System.IO;
using Unity.Collections;
using UnityEngine;

namespace CinematicRecorder.Audio
{
    public class WAVEncoder : IDisposable
    {
        private BinaryWriter _binwriter;
        private readonly int _sampleRate;
        private readonly ushort _channelCount;
        private bool _isDisposed;

        public WAVEncoder(string filename, int sampleRate, ushort channelCount)
        {
            _sampleRate = sampleRate;
            _channelCount = channelCount;
            var stream = new FileStream(filename, FileMode.Create);
            _binwriter = new BinaryWriter(stream);
            for (int n = 0; n < 44; n++) _binwriter.Write((byte)0);
        }

        public void AddSamples(NativeArray<float> data)
        {
            if (_binwriter == null) return;
            for (int n = 0; n < data.Length; n++) _binwriter.Write(data[n]);
        }

        public void Stop()
        {
            if (_binwriter == null) return;
            var closewriter = _binwriter;
            _binwriter = null;
            long pos = closewriter.BaseStream.Length;
            closewriter.Seek(0, SeekOrigin.Begin);
            closewriter.Write((byte)'R'); closewriter.Write((byte)'I'); closewriter.Write((byte)'F'); closewriter.Write((byte)'F');
            closewriter.Write((uint)(pos - 8));
            closewriter.Write((byte)'W'); closewriter.Write((byte)'A'); closewriter.Write((byte)'V'); closewriter.Write((byte)'E');
            closewriter.Write((byte)'f'); closewriter.Write((byte)'m'); closewriter.Write((byte)'t'); closewriter.Write((byte)' ');
            closewriter.Write((uint)16);
            closewriter.Write((ushort)3); // float
            closewriter.Write((ushort)_channelCount);
            closewriter.Write((uint)_sampleRate);
            closewriter.Write((uint)((_sampleRate * _channelCount * 32) / 8));
            closewriter.Write((ushort)((_channelCount * 32) / 8));
            closewriter.Write((ushort)32);
            closewriter.Write((byte)'d'); closewriter.Write((byte)'a'); closewriter.Write((byte)'t'); closewriter.Write((byte)'a');
            closewriter.Write((uint)(pos - 44));
            closewriter.Seek((int)pos, SeekOrigin.Begin);
            closewriter.Flush();
            closewriter.Close();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Stop();
        }
    }

    public class AudioCaptureController : IDisposable
    {
        private readonly string _outputPath;
        private readonly int _sampleRate;
        private readonly ushort _channelCount;
        private readonly int _playbackFps;
        private WAVEncoder _encoder;

        // Ring buffer for streaming resampling - persists between frames
        private NativeArray<float> _ringBuffer;
        private int _writePosition;  // Where we write incoming audio
        private int _readPosition;   // Where we read for resampling
        private int _bufferedSamples; // Total samples in buffer
        private const int RING_BUFFER_SIZE = 48000 * 20; // 20 seconds max

        // Resampling state
        private float _resamplePhase; // Fractional position for interpolation

        private bool _isInitialized;
        private bool _isDisposed;

        public string OutputPath => _outputPath;

        public AudioCaptureController(string outputPath, int playbackFps)
        {
            _outputPath = outputPath;
            _playbackFps = playbackFps;
            _sampleRate = AudioSettings.outputSampleRate;
            _channelCount = GetChannelCount(AudioSettings.speakerMode);
        }

        private ushort GetChannelCount(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Stereo: return 2;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 7;
                case AudioSpeakerMode.Prologic: return 2;
                default: return 2;
            }
        }

        public void Initialize()
        {
            if (_isInitialized) return;
            Debug.Log($"[AudioCapture] Init: {_sampleRate}Hz, {_channelCount}ch, Playback: {_playbackFps}fps");
            AudioRenderer.Start();
            _encoder = new WAVEncoder(_outputPath, _sampleRate, _channelCount);
            _ringBuffer = new NativeArray<float>(RING_BUFFER_SIZE, Allocator.Persistent);
            _writePosition = 0;
            _readPosition = 0;
            _bufferedSamples = 0;
            _resamplePhase = 0f;
            _isInitialized = true;
        }

        /// <summary>
        /// Capture audio for this physics frame. 
        /// At high FPS, GetSampleCountForCaptureFrame may return 0, so we calculate expected samples.
        /// </summary>
        public void CaptureSubFrame(float physicsDeltaTime)
        {
            if (!_isInitialized) return;

            // Try Unity's method first
            int sampleCount = AudioRenderer.GetSampleCountForCaptureFrame();

            // Fallback: calculate expected samples based on physics delta
            if (sampleCount == 0)
            {
                sampleCount = Mathf.RoundToInt(_sampleRate * physicsDeltaTime);
                // Debug.Log($"[AudioCapture] Calculated {sampleCount} samples for {physicsDeltaTime}s");
            }

            if (sampleCount == 0) return;

            int totalSamples = sampleCount * _channelCount;
            NativeArray<float> buffer = new NativeArray<float>(totalSamples, Allocator.Temp);

            try
            {
                AudioRenderer.Render(buffer);

                // Write to ring buffer
                for (int i = 0; i < totalSamples; i++)
                {
                    _ringBuffer[_writePosition] = buffer[i];
                    _writePosition = (_writePosition + 1) % RING_BUFFER_SIZE;
                }
                _bufferedSamples += totalSamples;

                // Safety: don't overflow
                if (_bufferedSamples > RING_BUFFER_SIZE - totalSamples)
                {
                    Debug.LogWarning("[AudioCapture] Ring buffer nearly full, advancing read position");
                    _readPosition = (_writePosition + RING_BUFFER_SIZE / 2) % RING_BUFFER_SIZE; // Drop half
                    _bufferedSamples = RING_BUFFER_SIZE / 2;
                }
            }
            finally
            {
                buffer.Dispose();
            }
        }

        /// <summary>
        /// Resample and write output based on current simulation speed.
        /// stretchFactor = currentSimFps / playbackFps
        /// If sim runs faster than playback, we stretch audio (more output samples).
        /// </summary>
        public void FinalizeOutputFrame(float currentSimFps)
        {
            if (!_isInitialized || _bufferedSamples < _channelCount * 2) return;

            // Calculate how many output samples to generate
            // If sim runs at 60fps, playback at 24fps: ratio = 2.5, output 2.5x samples
            float stretchRatio = currentSimFps / _playbackFps;

            // How many input samples to consume this frame
            int inputSamplesPerFrame = Mathf.RoundToInt((_sampleRate / currentSimFps) * _channelCount);
            int outputSamplesPerFrame = Mathf.RoundToInt(inputSamplesPerFrame * stretchRatio);

            if (outputSamplesPerFrame <= 0 || _bufferedSamples < inputSamplesPerFrame) return;

            NativeArray<float> output = new NativeArray<float>(outputSamplesPerFrame, Allocator.Temp);

            try
            {
                // Streaming resampling with cubic interpolation for smoother results
                float step = 1f / stretchRatio; // Consumption rate

                for (int i = 0; i < outputSamplesPerFrame; i++)
                {
                    float srcPos = _resamplePhase + (i * step);
                    int srcIdx = (int)srcPos;
                    float frac = srcPos - srcIdx;

                    // Wrap read position
                    int idx0 = (_readPosition + srcIdx) % RING_BUFFER_SIZE;
                    int idx1 = (_readPosition + srcIdx + 1) % RING_BUFFER_SIZE;
                    int idx2 = (_readPosition + srcIdx + 2) % RING_BUFFER_SIZE;
                    int idx_1 = (_readPosition + srcIdx - 1 + RING_BUFFER_SIZE) % RING_BUFFER_SIZE; // Previous

                    // Cubic interpolation (Catmull-Rom spline)
                    float p0 = _ringBuffer[idx_1];
                    float p1 = _ringBuffer[idx0];
                    float p2 = _ringBuffer[idx1];
                    float p3 = _ringBuffer[idx2];

                    float frac2 = frac * frac;
                    float frac3 = frac2 * frac;

                    // Catmull-Rom weights
                    float v0 = (-0.5f * p0) + (1.5f * p1) - (1.5f * p2) + (0.5f * p3);
                    float v1 = p0 - (2.5f * p1) + (2.0f * p2) - (0.5f * p3);
                    float v2 = (-0.5f * p0) + (0.5f * p2);
                    float v3 = p1;

                    output[i] = ((v0 * frac3) + (v1 * frac2) + (v2 * frac) + v3) * AudioListener.volume;
                }

                _encoder.AddSamples(output);

                // Advance read position by how much we consumed
                int consumed = (int)(_resamplePhase + (outputSamplesPerFrame * step));
                _readPosition = (_readPosition + consumed) % RING_BUFFER_SIZE;
                _bufferedSamples -= consumed;
                _resamplePhase = (_resamplePhase + (outputSamplesPerFrame * step)) - consumed; // Keep fractional part
            }
            finally
            {
                output.Dispose();
            }
        }

        public void Shutdown()
        {
            if (!_isInitialized) return;

            // Flush remaining audio at 1:1 ratio
            if (_bufferedSamples > 0 && _encoder != null)
            {
                int remaining = Mathf.Min(_bufferedSamples, RING_BUFFER_SIZE - _readPosition);
                if (remaining > 0)
                {
                    NativeArray<float> final = new NativeArray<float>(remaining, Allocator.Temp);
                    for (int i = 0; i < remaining; i++)
                        final[i] = _ringBuffer[(_readPosition + i) % RING_BUFFER_SIZE];
                    _encoder.AddSamples(final);
                    final.Dispose();
                }
            }

            Debug.Log("[AudioCapture] Shutdown");
            if (_encoder != null) { _encoder.Dispose(); _encoder = null; }
            if (_ringBuffer.IsCreated) _ringBuffer.Dispose();
            AudioRenderer.Stop();
            _isInitialized = false;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            Shutdown();
        }
    }
}