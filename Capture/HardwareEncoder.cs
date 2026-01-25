using FFmpeg.AutoGen;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

namespace CinematicRecorder.Capture
{
    public unsafe class HardwareEncoder : IDisposable
    {
        // FFmpeg contexts
        private AVCodecContext* codecContext;
        private AVFormatContext* formatContext;
        private AVStream* videoStream;
        private AVFrame* frame;
        private AVPacket* packet;
        private SwsContext* swsContext;

        // Settings
        private int width, height, fps;
        private long framePts;
        private string outputPath;
        private bool isInitialized = false;
        public bool ForceSoftwareEncoding { get; set; } = false;

        // Threading for software encoding
        private ConcurrentQueue<(EncoderCommand cmd, NativeArray<byte> native)> commandQueue;
        private volatile bool encoderAlive;
        private Thread encoderThread;
        private volatile bool stopping;


        public enum EncoderType { NVENC, AMF, QuickSync, CPU }
        public EncoderType ActiveEncoder { get; private set; } = EncoderType.CPU;

        public bool IsInitialized => isInitialized;
        public int FrameCount => (int)framePts;

        public bool Initialize(int width, int height, int fps, string outputFile)
        {
            UnityEngine.Debug.Log($"[HardwareEncoder] Initialize called: {width}x{height}@{fps} -> {outputFile}");

            this.width = width;
            this.height = height;
            this.fps = fps;
            this.outputPath = outputFile;

            // Test FFmpeg load
            try
            {
                var testPtr = ffmpeg.avcodec_find_encoder_by_name("libx264");
                if (testPtr == null)
                {
                    UnityEngine.Debug.LogError("[HardwareEncoder] FFmpeg DLLs not loaded");
                    return false;
                }
                TestAvailableEncoders();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[HardwareEncoder] FFmpeg load failed: {ex.Message}");
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            // Try encoders
            if (ForceSoftwareEncoding)
            {
                UnityEngine.Debug.Log("[HardwareEncoder] Force software encoding enabled");
                if (TryInitializeEncoder("libx264", EncoderType.CPU))
                {
                    commandQueue = new ConcurrentQueue<(EncoderCommand, NativeArray<byte>)>();
                    encoderAlive = true;

                    encoderThread = new Thread(EncoderThreadMain)
                    {
                        IsBackground = false,
                        Name = "CinematicRecorder_Encoder"
                    };
                    encoderThread.Start();

                    return true;
                }
            }
            else
            {
                if (TryInitializeEncoder("h264_nvenc", EncoderType.NVENC)) return true;
                if (TryInitializeEncoder("h264_amf", EncoderType.AMF)) return true;
                if (TryInitializeEncoder("libx264", EncoderType.CPU))
                {
                    commandQueue = new ConcurrentQueue<(EncoderCommand, NativeArray<byte>)>();
                    encoderAlive = true;

                    encoderThread = new Thread(EncoderThreadMain)
                    {
                        IsBackground = false,
                        Name = "CinematicRecorder_Encoder"
                    };
                    encoderThread.Start();

                    return true;
                }
            }

            return false;
        }

        private void EncodeFrameInternalNative(NativeArray<byte> rgba)
        {
            byte* srcPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(rgba);

            byte_ptrArray4 srcData = new byte_ptrArray4();
            int_array4 srcLinesize = new int_array4();

            srcData[0] = srcPtr;
            srcLinesize[0] = width * 4;

            byte_ptrArray4 dstData = new byte_ptrArray4();
            int_array4 dstLinesize = new int_array4();

            dstData[0] = frame->data[0];
            dstData[1] = frame->data[1];
            dstData[2] = frame->data[2];

            dstLinesize[0] = frame->linesize[0];
            dstLinesize[1] = frame->linesize[1];
            dstLinesize[2] = frame->linesize[2];

            ffmpeg.sws_scale(
                swsContext,
                srcData,
                srcLinesize,
                0,
                height,
                dstData,
                dstLinesize);

            frame->pts = framePts++;

            int ret = ffmpeg.avcodec_send_frame(codecContext, frame);
            if (ret < 0) return;

            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_packet(codecContext, packet);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) ||
                    ret == ffmpeg.AVERROR_EOF)
                    break;

                ffmpeg.av_packet_rescale_ts(
                    packet,
                    codecContext->time_base,
                    videoStream->time_base);

                packet->stream_index = videoStream->index;
                ffmpeg.av_interleaved_write_frame(formatContext, packet);
                ffmpeg.av_packet_unref(packet);
            }
        }

        private void EncoderThreadMain()
        {
            Debug.Log("[HardwareEncoder] Encoder thread started");

            while (true)
            {
                if (!commandQueue.TryDequeue(out var item))
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (item.cmd == EncoderCommand.Stop)
                {
                    Debug.Log("[HardwareEncoder] Stop requested, flushing encoder");

                    // Flush encoder
                    ffmpeg.avcodec_send_frame(codecContext, null);

                    while (true)
                    {
                        int ret = ffmpeg.avcodec_receive_packet(codecContext, packet);
                        if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) ||
                            ret == ffmpeg.AVERROR_EOF)
                            break;

                        ffmpeg.av_packet_rescale_ts(
                            packet,
                            codecContext->time_base,
                            videoStream->time_base);

                        packet->stream_index = videoStream->index;
                        ffmpeg.av_interleaved_write_frame(formatContext, packet);
                        ffmpeg.av_packet_unref(packet);
                    }

                    ffmpeg.av_write_trailer(formatContext);
                    break; // EXIT THREAD
                }

                if (item.cmd == EncoderCommand.FrameNative)
                {
                    // ENCODER THREAD IS THE SOLE OWNER
                    EncodeFrameInternalNative(item.native);

                    if (item.native.IsCreated)
                        item.native.Dispose();
                }
            }

            encoderAlive = false;
            Debug.Log("[HardwareEncoder] Encoder thread exited cleanly");
        }



        private void EncodeFrameInternal(byte[] rgbaData)
        {
            // Convert RGBA to YUV420P
            byte_ptrArray4 srcData = new byte_ptrArray4();
            int_array4 srcLinesize = new int_array4();

            fixed (byte* srcPtr = rgbaData)
            {
                srcData[0] = srcPtr;
                srcLinesize[0] = width * 4;

                byte_ptrArray4 dstData = new byte_ptrArray4();
                int_array4 dstLinesize = new int_array4();

                dstData[0] = frame->data[0];
                dstData[1] = frame->data[1];
                dstData[2] = frame->data[2];
                dstLinesize[0] = frame->linesize[0];
                dstLinesize[1] = frame->linesize[1];
                dstLinesize[2] = frame->linesize[2];

                ffmpeg.sws_scale(swsContext, srcData, srcLinesize, 0, height, dstData, dstLinesize);
            }

            frame->pts = framePts++;

            int ret = ffmpeg.avcodec_send_frame(codecContext, frame);
            if (ret < 0) return;

            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_packet(codecContext, packet);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) break;
                if (ret < 0) return;

                ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, videoStream->time_base);
                packet->stream_index = videoStream->index;
                ffmpeg.av_interleaved_write_frame(formatContext, packet);
                ffmpeg.av_packet_unref(packet);
            }
        }

        // Called from main thread (AsyncGPUReadback callback)
        public void EncodeFrame(NativeArray<byte> rgba)
        {
            // If encoder is not ready, do nothing — Unity owns this buffer
            if (!isInitialized || stopping)
                return;

            if (ActiveEncoder != EncoderType.CPU)
            {
                // Hardware encoders: copy, but DO NOT dispose Unity-owned NativeArray
                EncodeFrameInternal(rgba.ToArray());
                return;
            }

            // CPU encoder path
            if (!encoderAlive)
                return;

            commandQueue.Enqueue((EncoderCommand.FrameNative, rgba));
        }

        private void TestAvailableEncoders()
        {
            UnityEngine.Debug.Log("[HardwareEncoder] Testing available encoders...");

            // Test NVENC
            AVCodec* nvenc = ffmpeg.avcodec_find_encoder_by_name("h264_nvenc");
            if (nvenc != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found NVENC encoder");
            else
                UnityEngine.Debug.Log("[HardwareEncoder] NVENC encoder not available");

            // Test AMF
            AVCodec* amf = ffmpeg.avcodec_find_encoder_by_name("h264_amf");
            if (amf != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found AMF encoder");
            else
                UnityEngine.Debug.Log("[HardwareEncoder] AMF encoder not available");

            // Test QuickSync
            AVCodec* qsv = ffmpeg.avcodec_find_encoder_by_name("h264_qsv");
            if (qsv != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found QuickSync encoder");
            else
                UnityEngine.Debug.Log("[HardwareEncoder] QuickSync encoder not available");

            // Test CPU
            AVCodec* cpu = ffmpeg.avcodec_find_encoder_by_name("libx264");
            if (cpu != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found CPU encoder (libx264)");
            else
                UnityEngine.Debug.Log("[HardwareEncoder] CPU encoder not available");
        }

        private bool TryInitializeEncoder(string codecName, EncoderType type)
        {
            UnityEngine.Debug.Log($"[HardwareEncoder] >>> Trying encoder: {codecName}");

            int ret;

            // 1. Guess output format (MKV)
            AVOutputFormat* outputFormat = ffmpeg.av_guess_format("matroska", null, null);
            if (outputFormat == null)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Could not find MKV muxer.");
                return false;
            }

            // 2. Allocate format context
            AVFormatContext* localFormatContext = ffmpeg.avformat_alloc_context();
            if (localFormatContext == null)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to allocate format context.");
                return false;
            }
            localFormatContext->oformat = outputFormat;

            // 3. Find encoder
            AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
            if (codec == null)
            {
                UnityEngine.Debug.LogError($"[HardwareEncoder] Codec '{codecName}' not found.");
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            // 4. Allocate codec context
            AVCodecContext* localCodecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (localCodecContext == null)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to allocate codec context.");
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            // 5. Create stream
            AVStream* localVideoStream = ffmpeg.avformat_new_stream(localFormatContext, codec);
            if (localVideoStream == null)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to create video stream.");
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            // 6. Configure codec context
            localCodecContext->width = width;
            localCodecContext->height = height;
            localCodecContext->time_base = new AVRational { num = 1, den = fps };
            localCodecContext->framerate = new AVRational { num = fps, den = 1 };
            localCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            localCodecContext->gop_size = fps*2; // Increase if necessary
            localCodecContext->max_b_frames = 0;

            // CPU-only quality settings (AMF/NVENC untouched)
            if (type == EncoderType.CPU)
            {
                ffmpeg.av_opt_set(localCodecContext->priv_data, "preset", "slow", 0);
                ffmpeg.av_opt_set(localCodecContext->priv_data, "crf", "18", 0);
                ffmpeg.av_opt_set(localCodecContext->priv_data, "profile", "high", 0);
                ffmpeg.av_opt_set(localCodecContext->priv_data, "level", "5.2", 0);
                ffmpeg.av_opt_set(localCodecContext->priv_data, "threads", "0", 0);
            }

            // 7. REQUIRED: Global header flag for MKV
            if ((localFormatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                localCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }

            // AMD Quality Settings
            if (type == EncoderType.AMF)
            {
                ffmpeg.av_opt_set(localCodecContext->priv_data, "rc", "cqp", 0);
                ffmpeg.av_opt_set(localCodecContext->priv_data, "qp_i", "18", 0);
                ffmpeg.av_opt_set(localCodecContext->priv_data, "qp_p", "20", 0);
                ffmpeg.av_opt_set(localCodecContext->priv_data, "qp_b", "22", 0);
            }

            // 8. Open codec
            ret = ffmpeg.avcodec_open2(localCodecContext, codec, null);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError(
                    $"[HardwareEncoder] avcodec_open2 failed: {AvErrorToString(ret)}");
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            // 9. Copy codec parameters to stream
            ret = ffmpeg.avcodec_parameters_from_context(
                localVideoStream->codecpar, localCodecContext);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError(
                    $"[HardwareEncoder] Failed to copy codec parameters: {AvErrorToString(ret)}");
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            localVideoStream->time_base = localCodecContext->time_base;

            // 10. Open output file
            ret = ffmpeg.avio_open(&localFormatContext->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError(
                    $"[HardwareEncoder] Failed to open output file: {AvErrorToString(ret)}");
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            // 11. Write header
            ret = ffmpeg.avformat_write_header(localFormatContext, null);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError(
                    $"[HardwareEncoder] Failed to write file header: {AvErrorToString(ret)}");
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avio_closep(&localFormatContext->pb);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            // 12. Final assignment ONLY on success
            this.codecContext = localCodecContext;
            this.formatContext = localFormatContext;
            this.videoStream = localVideoStream;

            frame = ffmpeg.av_frame_alloc();
            frame->format = (int)AVPixelFormat.AV_PIX_FMT_YUV420P;
            frame->width = width;
            frame->height = height;
            ffmpeg.av_frame_get_buffer(frame, 0);

            packet = ffmpeg.av_packet_alloc();

            swsContext = ffmpeg.sws_getContext(
                width, height, AVPixelFormat.AV_PIX_FMT_RGBA,
                width, height, AVPixelFormat.AV_PIX_FMT_YUV420P,
                ffmpeg.SWS_BILINEAR, null, null, null);

            ActiveEncoder = type;
            isInitialized = true;

            UnityEngine.Debug.Log($"[HardwareEncoder] SUCCESS: Initialized {type} encoder");
            return true;
        }

        private static string AvErrorToString(int err)
        {
            const int bufferSize = 1024;
            byte* buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(err, buffer, (ulong)bufferSize);
            return Marshal.PtrToStringAnsi((IntPtr)buffer);
        }

        public void RequestStop()
        {
            if (!isInitialized || stopping)
                return;

            stopping = true;

            if (ActiveEncoder == EncoderType.CPU)
            {
                if (encoderAlive)
                {
                    commandQueue.Enqueue((EncoderCommand.Stop, default));
                    encoderThread?.Join();   // ⬅️ HARD BARRIER
                }

                Cleanup();  // ⬅️ ONLY AFTER THREAD IS DEAD
            }
            else
            {
                // Flush hardware encoder (unchanged)
                ffmpeg.avcodec_send_frame(codecContext, null);

                while (true)
                {
                    int ret = ffmpeg.avcodec_receive_packet(codecContext, packet);
                    if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) ||
                        ret == ffmpeg.AVERROR_EOF)
                        break;

                    ffmpeg.av_packet_rescale_ts(
                        packet,
                        codecContext->time_base,
                        videoStream->time_base);

                    packet->stream_index = videoStream->index;
                    ffmpeg.av_interleaved_write_frame(formatContext, packet);
                    ffmpeg.av_packet_unref(packet);
                }

                ffmpeg.av_write_trailer(formatContext);
                Cleanup();
            }
        }

        private enum EncoderCommand
        {
            FrameNative,
            Stop
        }
        private void Cleanup()
        {
            if (!isInitialized)
                return;
            if (frame != null)
            {
                fixed (AVFrame** f = &frame) ffmpeg.av_frame_free(f);
            }
            if (packet != null)
            {
                fixed (AVPacket** p = &packet) ffmpeg.av_packet_free(p);
            }
            if (codecContext != null)
            {
                fixed (AVCodecContext** c = &codecContext) ffmpeg.avcodec_free_context(c);
            }
            if (formatContext != null)
            {
                if (formatContext->pb != null) ffmpeg.avio_closep(&formatContext->pb);
                ffmpeg.avformat_free_context(formatContext);
                formatContext = null;
            }
            if (swsContext != null)
            {
                ffmpeg.sws_freeContext(swsContext);
                swsContext = null;
            }
            codecContext = null;
            formatContext = null;
            videoStream = null;
            frame = null;
            packet = null;

            isInitialized = false;
        }

        private bool _disposed = false;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            RequestStop();
        }
    }
}