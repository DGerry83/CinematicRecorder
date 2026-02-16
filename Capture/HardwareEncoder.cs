using FFmpeg.AutoGen;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using CinematicRecorder.Core;

namespace CinematicRecorder.Capture
{
    public unsafe class HardwareEncoder : IDisposable
    {
        #region Native Resources
        private AVCodecContext* codecContext;
        private AVFormatContext* formatContext;
        private AVStream* videoStream;
        private AVFrame* frame;
        private AVPacket* packet;
        private SwsContext* swsContext;

        private int width, height, fps;
        private long framePts;
        private string outputPath;
        private bool isInitialized;
        private bool stopping;
        #endregion
        #region Threading
        private ConcurrentQueue<EncoderCommandItem> commandQueue;
        private Thread encoderThread;
        private volatile bool encoderAlive;
        private struct EncoderCommandItem
        {
            public EncoderCommand Command;
            public NativeArray<byte> Frame;
        }

        private enum EncoderCommand
        {
            Frame,
            Stop
        }
        #endregion
        #region Configuration
        public bool ForceSoftwareEncoding { get; set; }
        public enum EncoderType { NVENC, AMF, QuickSync, CPU }
        public EncoderType ActiveEncoder { get; private set; }
        #endregion
        #region Public API
        /// <summary>
        /// Attempts to initialize hardware encoding with fallback chain: HEVC (NVENC->AMF->QSV) -> H.264 (NVENC->AMF->QSV) -> CPU.
        /// </summary>
        public bool Initialize(int w, int h, int frameRate, string outputFile)
        {
            width = w;
            height = h;
            fps = frameRate;
            outputPath = outputFile;

            try
            {
                TestAvailableEncoders();
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] FFmpeg validation failed: " + ex.Message);
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            if (ForceSoftwareEncoding)
            {
                UnityEngine.Debug.Log("[HardwareEncoder] Force software encoding enabled");
                return InitCpu();
            }

            // Try HEVC first (original priority order with QuickSync restored)
            if (TryInitializeEncoder("hevc_nvenc", EncoderType.NVENC)) return true;
            if (TryInitializeEncoder("hevc_amf", EncoderType.AMF)) return true;
            if (TryInitializeEncoder("hevc_qsv", EncoderType.QuickSync)) return true;

            // Fall back to H.264
            if (TryInitializeEncoder("h264_nvenc", EncoderType.NVENC)) return true;
            if (TryInitializeEncoder("h264_amf", EncoderType.AMF)) return true;
            if (TryInitializeEncoder("h264_qsv", EncoderType.QuickSync)) return true;

            // Final fallback to CPU
            UnityEngine.Debug.Log("[HardwareEncoder] No hardware encoder available, falling back to CPU");
            return InitCpu();
        }
        /// <summary>
        /// Queues a frame for encoding. For CPU encoding, this is asynchronous; for hardware encoders, blocks until complete.
        /// Disposes the NativeArray after encoding (caller should not reuse).
        /// </summary>
        public void EncodeFrame(NativeArray<byte> rgba)
        {
            if (!isInitialized || stopping)
            {
                if (rgba.IsCreated) rgba.Dispose();
                return;
            }

            if (ActiveEncoder == EncoderType.CPU)
            {
                if (!encoderAlive)
                {
                    if (rgba.IsCreated) rgba.Dispose();
                    return;
                }
                commandQueue.Enqueue(new EncoderCommandItem { Command = EncoderCommand.Frame, Frame = rgba });
            }
            else
            {
                EncodeFrameInternal(rgba);
                if (rgba.IsCreated) rgba.Dispose();
            }
        }
        /// <summary>
        /// Signals the encoder to finish and flush remaining frames. Blocks until complete for CPU path.
        /// </summary>
        public void RequestStop()
        {
            if (!isInitialized || stopping)
                return;

            stopping = true;

            if (ActiveEncoder == EncoderType.CPU)
            {
                if (encoderAlive)
                {
                    commandQueue.Enqueue(new EncoderCommandItem { Command = EncoderCommand.Stop });
                    if (encoderThread != null)
                        encoderThread.Join();
                }
                Cleanup();
            }
            else
            {
                FlushEncoder();
                Cleanup();
            }
        }
        public void Dispose()
        {
            RequestStop();
        }
        #endregion
        #region Encoder Implementation
        private void TestAvailableEncoders()
        {
            UnityEngine.Debug.Log("[HardwareEncoder] Testing available encoders...");

            AVCodec* nvenc = ffmpeg.avcodec_find_encoder_by_name("h264_nvenc");
            if (nvenc != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found NVENC encoder");
            else
                UnityEngine.Debug.Log("[HardwareEncoder] NVENC encoder not available");

            AVCodec* amf = ffmpeg.avcodec_find_encoder_by_name("h264_amf");
            if (amf != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found AMF encoder");
            else
                UnityEngine.Debug.Log("[HardwareEncoder] AMF encoder not available");

            AVCodec* qsv = ffmpeg.avcodec_find_encoder_by_name("h264_qsv");
            if (qsv != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found QuickSync encoder");
            else
                UnityEngine.Debug.Log("[HardwareEncoder] QuickSync encoder not available");

            AVCodec* hevcNvenc = ffmpeg.avcodec_find_encoder_by_name("hevc_nvenc");
            if (hevcNvenc != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found HEVC NVENC encoder");

            AVCodec* hevcAmf = ffmpeg.avcodec_find_encoder_by_name("hevc_amf");
            if (hevcAmf != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found HEVC AMF encoder");

            AVCodec* cpu = ffmpeg.avcodec_find_encoder_by_name("libx264");
            if (cpu != null)
                UnityEngine.Debug.Log("[HardwareEncoder] Found CPU encoder (libx264)");
            else
                UnityEngine.Debug.Log("[HardwareEncoder] CPU encoder not available");
        }
        private bool InitCpu()
        {
            if (!TryInitializeEncoder("libx264", EncoderType.CPU))
                return false;

            commandQueue = new ConcurrentQueue<EncoderCommandItem>();
            encoderAlive = true;

            encoderThread = new Thread(EncoderThreadMain)
            {
                IsBackground = false,
                Name = "CinematicRecorder_Encoder"
            };
            encoderThread.Start();

            return true;
        }
        private bool TryInitializeEncoder(string codecName, EncoderType type)
        {
            UnityEngine.Debug.Log("[HardwareEncoder] >>> Trying encoder: " + codecName);

            int ret;

            AVOutputFormat* outputFormat = ffmpeg.av_guess_format("matroska", null, null);
            if (outputFormat == null)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Could not find MKV muxer.");
                return false;
            }

            AVFormatContext* localFormatContext = ffmpeg.avformat_alloc_context();
            if (localFormatContext == null)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to allocate format context.");
                return false;
            }
            localFormatContext->oformat = outputFormat;

            AVCodec* codec = ffmpeg.avcodec_find_encoder_by_name(codecName);
            if (codec == null)
            {
                ffmpeg.avformat_free_context(localFormatContext);
                return false; // Codec not available, this is expected in fallback chain
            }

            AVCodecContext* localCodecContext = ffmpeg.avcodec_alloc_context3(codec);
            if (localCodecContext == null)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to allocate codec context.");
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            AVStream* localVideoStream = ffmpeg.avformat_new_stream(localFormatContext, codec);
            if (localVideoStream == null)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to create video stream.");
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            localCodecContext->width = width;
            localCodecContext->height = height;
            localCodecContext->time_base = new AVRational { num = 1, den = fps };
            localCodecContext->framerate = new AVRational { num = fps, den = 1 };
            localCodecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;
            localCodecContext->gop_size = fps * 2;
            localCodecContext->max_b_frames = 0;

            // Apply quality settings based on encoder type
            if (!ApplyQualitySettings(localCodecContext, type))
            {
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            if ((localFormatContext->oformat->flags & ffmpeg.AVFMT_GLOBALHEADER) != 0)
            {
                localCodecContext->flags |= ffmpeg.AV_CODEC_FLAG_GLOBAL_HEADER;
            }

            ret = ffmpeg.avcodec_open2(localCodecContext, codec, null);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] avcodec_open2 failed: " + AvErrorToString(ret));
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            ret = ffmpeg.avcodec_parameters_from_context(localVideoStream->codecpar, localCodecContext);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to copy codec parameters: " + AvErrorToString(ret));
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            localVideoStream->time_base = localCodecContext->time_base;

            ret = ffmpeg.avio_open(&localFormatContext->pb, outputPath, ffmpeg.AVIO_FLAG_WRITE);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to open output file: " + AvErrorToString(ret));
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            ret = ffmpeg.avformat_write_header(localFormatContext, null);
            if (ret < 0)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to write file header: " + AvErrorToString(ret));
                ffmpeg.avio_closep(&localFormatContext->pb);
                ffmpeg.avcodec_free_context(&localCodecContext);
                ffmpeg.avformat_free_context(localFormatContext);
                return false;
            }

            codecContext = localCodecContext;
            formatContext = localFormatContext;
            videoStream = localVideoStream;

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

            UnityEngine.Debug.Log("[HardwareEncoder] SUCCESS: Initialized " + type + " encoder (" + codecName + ")");
            return true;
        }
        private void EncoderThreadMain()
        {
            UnityEngine.Debug.Log("[HardwareEncoder] Encoder thread started");

            while (true)
            {
                EncoderCommandItem item;
                if (!commandQueue.TryDequeue(out item))
                {
                    Thread.Sleep(1);
                    continue;
                }

                if (item.Command == EncoderCommand.Stop)
                {
                    FlushEncoder();
                    break;
                }

                EncodeFrameInternal(item.Frame);
                if (item.Frame.IsCreated) item.Frame.Dispose();
            }

            encoderAlive = false;
            UnityEngine.Debug.Log("[HardwareEncoder] Encoder thread exited cleanly");
        }
        private void EncodeFrameInternal(NativeArray<byte> rgba)
        {
            byte* srcPtr = (byte*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(rgba);

            byte_ptrArray4 srcData = new byte_ptrArray4();
            int_array4 srcLinesize = new int_array4();

            srcData[0] = srcPtr + (height - 1) * width * 4;
            srcLinesize[0] = -width * 4;

            byte_ptrArray4 dstData = new byte_ptrArray4();
            int_array4 dstLinesize = new int_array4();

            dstData[0] = frame->data[0];
            dstData[1] = frame->data[1];
            dstData[2] = frame->data[2];

            dstLinesize[0] = frame->linesize[0];
            dstLinesize[1] = frame->linesize[1];
            dstLinesize[2] = frame->linesize[2];

            ffmpeg.sws_scale(swsContext, srcData, srcLinesize, 0, height, dstData, dstLinesize);

            frame->pts = framePts++;

            int ret = ffmpeg.avcodec_send_frame(codecContext, frame);
            if (ret < 0)
                return;

            while (ret >= 0)
            {
                ret = ffmpeg.avcodec_receive_packet(codecContext, packet);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF || ret < 0)
                    break;

                ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, videoStream->time_base);
                packet->stream_index = videoStream->index;
                ffmpeg.av_interleaved_write_frame(formatContext, packet);
                ffmpeg.av_packet_unref(packet);
            }
        }
        private void FlushEncoder()
        {
            ffmpeg.avcodec_send_frame(codecContext, null);

            while (true)
            {
                int ret = ffmpeg.avcodec_receive_packet(codecContext, packet);
                if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF || ret < 0)
                    break;

                ffmpeg.av_packet_rescale_ts(packet, codecContext->time_base, videoStream->time_base);
                packet->stream_index = videoStream->index;
                ffmpeg.av_interleaved_write_frame(formatContext, packet);
                ffmpeg.av_packet_unref(packet);
            }

            ffmpeg.av_write_trailer(formatContext);
        }
        #endregion
        #region Quality Settings
        /// <summary>
        /// Applies encoder-specific quality and rate control settings based on SessionState configuration.
        /// </summary>
        private bool ApplyQualitySettings(AVCodecContext* ctx, EncoderType type)
        {
            try
            {
                switch (type)
                {
                    case EncoderType.CPU:
                        ApplyCpuSettings(ctx);
                        break;
                    case EncoderType.NVENC:
                        ApplyNvencSettings(ctx);
                        break;
                    case EncoderType.AMF:
                        ApplyAmfSettings(ctx);
                        break;
                    case EncoderType.QuickSync:
                        ApplyQuickSyncSettings(ctx);
                        break;
                }
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[HardwareEncoder] Failed to apply quality settings: " + ex.Message);
                return false;
            }
        }
        private void ApplyCpuSettings(AVCodecContext* ctx)
        {
            string preset;
            switch (SessionState.CpuPreset)
            {
                case 0:
                    preset = "fast";
                    break;
                case 2:
                    preset = "slow";
                    break;
                default:
                    preset = "medium";
                    break;
            }

            ffmpeg.av_opt_set(ctx->priv_data, "preset", preset, 0);
            ffmpeg.av_opt_set(ctx->priv_data, "profile", "high", 0);
            ffmpeg.av_opt_set(ctx->priv_data, "level", "5.2", 0);
            ffmpeg.av_opt_set(ctx->priv_data, "threads", "0", 0);

            if (SessionState.CpuRateControlMode == 0) // CRF (Quality-based)
            {
                ffmpeg.av_opt_set(ctx->priv_data, "crf", SessionState.CpuCrfValue.ToString(), 0);
            }
            else // VBR or CBR (Bitrate-based)
            {
                long bitrate = SessionState.CpuTargetBitrate * 1000L;
                ctx->bit_rate = bitrate;
                ctx->rc_max_rate = bitrate;
                ctx->rc_buffer_size = (int)bitrate;

                if (SessionState.CpuRateControlMode == 2) // CBR
                    ffmpeg.av_opt_set(ctx->priv_data, "nal-hrd", "cbr", 0);
            }

            UnityEngine.Debug.Log("[HardwareEncoder] CPU settings: preset=" + preset + ", " +
                (SessionState.CpuRateControlMode == 0 ? "crf=" + SessionState.CpuCrfValue : "bitrate=" + SessionState.CpuTargetBitrate + "Mbps"));
        }
        private void ApplyNvencSettings(AVCodecContext* ctx)
        {
            // Map SessionState preset (0=Speed, 1=Balanced, 2=Quality) to NVENC presets (p1-p7)
            string preset;
            switch (SessionState.NvencPreset)
            {
                case 0:
                    preset = "p1"; // Speed
                    break;
                case 2:
                    preset = "p7"; // Quality
                    break;
                default:
                    preset = "p4"; // Balanced (P4 is FFmpeg NVENC default)
                    break;
            }

            ffmpeg.av_opt_set(ctx->priv_data, "preset", preset, 0);

            if (SessionState.NvencRateControlMode == 0) // CQ (Constant Quality)
            {
                ffmpeg.av_opt_set(ctx->priv_data, "rc", "constqp", 0);
                ffmpeg.av_opt_set(ctx->priv_data, "cq", SessionState.NvencCqValue.ToString(), 0);
            }
            else if (SessionState.NvencRateControlMode == 1) // VBR
            {
                ffmpeg.av_opt_set(ctx->priv_data, "rc", "vbr", 0);
                ctx->bit_rate = SessionState.NvencTargetBitrate * 1000L;
                ctx->rc_max_rate = SessionState.NvencTargetBitrate * 1000L;
            }
            else // CBR
            {
                ffmpeg.av_opt_set(ctx->priv_data, "rc", "cbr", 0);
                ctx->bit_rate = SessionState.NvencTargetBitrate * 1000L;
                ctx->rc_min_rate = SessionState.NvencTargetBitrate * 1000L;
                ctx->rc_max_rate = SessionState.NvencTargetBitrate * 1000L;
            }

            UnityEngine.Debug.Log("[HardwareEncoder] NVENC settings: preset=" + preset + ", " +
                (SessionState.NvencRateControlMode == 0 ? "cq=" + SessionState.NvencCqValue : "bitrate=" + SessionState.NvencTargetBitrate + "Mbps"));
        }
        private void ApplyAmfSettings(AVCodecContext* ctx)
        {
            // Map SessionState preset to AMF quality setting
            string quality;
            switch (SessionState.AmfEncoderSpeed)
            {
                case 0:
                    quality = "speed";
                    break;
                case 2:
                    quality = "quality";
                    break;
                default:
                    quality = "balanced";
                    break;
            }

            ffmpeg.av_opt_set(ctx->priv_data, "quality", quality, 0);

            if (SessionState.AmfRateControlMode == 0) // CQP (Constant QP)
            {
                ffmpeg.av_opt_set(ctx->priv_data, "rc", "cqp", 0);

                int baseQp = SessionState.AmfCqpValue;
                ffmpeg.av_opt_set(ctx->priv_data, "qp_i", baseQp.ToString(), 0);
                ffmpeg.av_opt_set(ctx->priv_data, "qp_p", (baseQp + 2).ToString(), 0);
                ffmpeg.av_opt_set(ctx->priv_data, "qp_b", (baseQp + 4).ToString(), 0);
            }
            else if (SessionState.AmfRateControlMode == 1) // VBR
            {
                ffmpeg.av_opt_set(ctx->priv_data, "rc", "vbr", 0);
                ctx->bit_rate = SessionState.AmfTargetBitrate * 1000L;
                ctx->rc_max_rate = SessionState.AmfTargetBitrate * 1000L;
            }
            else // CBR
            {
                ffmpeg.av_opt_set(ctx->priv_data, "rc", "cbr", 0);
                ctx->bit_rate = SessionState.AmfTargetBitrate * 1000L;
            }

            UnityEngine.Debug.Log("[HardwareEncoder] AMF settings: quality=" + quality + ", " +
                (SessionState.AmfRateControlMode == 0 ? "cqp_i=" + SessionState.AmfCqpValue : "bitrate=" + SessionState.AmfTargetBitrate + "Mbps"));
        }
        private void ApplyQuickSyncSettings(AVCodecContext* ctx)
        {
            // QuickSync uses similar rate control concepts but different option names
            if (SessionState.NvencRateControlMode == 0) // Use NVENC settings as proxy for QuickSync
            {
                // QSV global quality (similar to CQ)
                ffmpeg.av_opt_set(ctx->priv_data, "global_quality", SessionState.NvencCqValue.ToString(), 0);
            }
            else
            {
                ctx->bit_rate = SessionState.NvencTargetBitrate * 1000L;
                ctx->rc_max_rate = SessionState.NvencTargetBitrate * 1000L;
            }

            // QSV preset: veryfast, faster, fast, medium, slow, slower, veryslow
            string preset;
            switch (SessionState.NvencPreset)
            {
                case 0:
                    preset = "veryfast";
                    break;
                case 2:
                    preset = "slow";
                    break;
                default:
                    preset = "medium";
                    break;
            }

            ffmpeg.av_opt_set(ctx->priv_data, "preset", preset, 0);
        }
        #endregion
        #region Helpers
        private static string AvErrorToString(int err)
        {
            const int bufferSize = 1024;
            byte* buffer = stackalloc byte[bufferSize];
            ffmpeg.av_strerror(err, buffer, (ulong)bufferSize);
            return Marshal.PtrToStringAnsi((IntPtr)buffer);
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
                if (formatContext->pb != null)
                    ffmpeg.avio_closep(&formatContext->pb);
                ffmpeg.avformat_free_context(formatContext);
            }
            if (swsContext != null)
            {
                ffmpeg.sws_freeContext(swsContext);
            }

            frame = null;
            packet = null;
            codecContext = null;
            formatContext = null;
            videoStream = null;
            swsContext = null;
            isInitialized = false;
        }
        #endregion
    }
}