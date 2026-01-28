#include "CinematicRecorderNative.h"

#include <string>
#include <vector>
#include <mutex>
#include <cstring>

// ---------------- AMF 1.5 ----------------
#include "AMFFactory.h"
#include "components/VideoEncoderVCE.h"
#include "components/VideoEncoderHEVC.h"  // ADD THIS INCLUDE
#include "core/Context.h"
#include "core/Surface.h"
#include "core/Variant.h"
#include "core/Buffer.h"

// ---------------- FFmpeg ----------------
extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/opt.h>
#include <libavutil/imgutils.h>
}

#pragma comment(lib, "d3d11.lib")

#ifndef AMF_VIDEO_ENCODER_COLOR_PROFILE_FULL
#define AMF_VIDEO_ENCODER_COLOR_PROFILE_FULL 2
#endif
#ifndef AMF_COLOR_PRIMARIES_BT709
#define AMF_COLOR_PRIMARIES_BT709 1
#endif
#ifndef AMF_COLOR_TRANSFER_CHARACTERISTIC_SRGB
#define AMF_COLOR_TRANSFER_CHARACTERISTIC_SRGB 13
#endif

static thread_local char g_errorBuffer[1024];

// Global Unity device storage (owned by plugin, released on DLL unload or new set)
static ID3D11Device* g_UnityD3D11Device = nullptr;
static ID3D11DeviceContext* g_UnityD3D11Context = nullptr;

const char* CR_GetLastError()
{
    return g_errorBuffer;
}

static void SetError(const char* msg)
{
    strncpy_s(g_errorBuffer, msg, sizeof(g_errorBuffer) - 1);
    g_errorBuffer[sizeof(g_errorBuffer) - 1] = 0;
}

struct EncoderContext
{
    ID3D11Device* device = nullptr;
    ID3D11DeviceContext* context = nullptr;
    amf::AMFContextPtr   amfContext;
    amf::AMFComponentPtr encoder;
    AVFormatContext* formatContext = nullptr;
    AVStream* videoStream = nullptr;
    AVRational timeBase{};
    ID3D11Texture2D* d3dTextures[2]{};      // Encoder-owned textures (destination)
    amf::AMFSurfacePtr amfSurfaces[2];      // AMF wrappers of above
    int bufferIndex = 0;
    int width = 0;
    int height = 0;
    int fps = 0;
    int64_t frameCount = 0;
    bool initialized = false;
    bool headerWritten = false;
    bool hevcMode = false;                  // NEW: Track if using HEVC vs H.264
    std::mutex writeMutex;
};

// Forward declaration
static bool WriteHeader(EncoderContext* ctx);

static bool InitializeFFmpegMuxer(EncoderContext* ctx, const char* outputPath)
{
    const AVOutputFormat* fmt = av_guess_format("matroska", nullptr, nullptr);
    if (!fmt)
    {
        SetError("MKV muxer not found");
        return false;
    }

    if (avformat_alloc_output_context2(&ctx->formatContext, fmt, nullptr, outputPath) < 0)
    {
        SetError("avformat_alloc_output_context2 failed");
        return false;
    }

    ctx->videoStream = avformat_new_stream(ctx->formatContext, nullptr);
    if (!ctx->videoStream)
    {
        SetError("avformat_new_stream failed");
        return false;
    }

    AVCodecParameters* par = ctx->videoStream->codecpar;
    par->codec_type = AVMEDIA_TYPE_VIDEO;
    // Codec selection based on mode
    par->codec_id   = ctx->hevcMode ? AV_CODEC_ID_HEVC : AV_CODEC_ID_H264;
    par->width      = ctx->width;
    par->height     = ctx->height;
    par->format     = AV_PIX_FMT_YUV420P;
    par->color_range     = AVCOL_RANGE_JPEG;
    par->color_primaries = AVCOL_PRI_BT709;
    par->color_trc       = AVCOL_TRC_IEC61966_2_1;
    par->color_space     = AVCOL_SPC_BT709;

    ctx->timeBase = { 1, ctx->fps };
    ctx->videoStream->time_base = ctx->timeBase;
    ctx->videoStream->avg_frame_rate = { ctx->fps, 1 };

    if (!(ctx->formatContext->oformat->flags & AVFMT_NOFILE))
    {
        if (avio_open(&ctx->formatContext->pb, outputPath, AVIO_FLAG_WRITE) < 0)
        {
            SetError("avio_open failed");
            return false;
        }
    }

    return true;
}

static bool WriteHeader(EncoderContext* ctx)
{
    if (ctx->headerWritten)
        return true;

    if (avformat_write_header(ctx->formatContext, nullptr) < 0)
    {
        SetError("avformat_write_header failed");
        return false;
    }

    ctx->headerWritten = true;
    return true;
}

// NEW: Export to set Unity device once from C# (or use InitFromTexture)
extern "C" __declspec(dllexport)
void CR_SetUnityD3D11Device(ID3D11Device* device)
{
    if (!device)
    {
        SetError("CR_SetUnityD3D11Device received null device");
        return;
    }
    
    // Clean up previous if any (shouldn't happen in normal usage)
    if (g_UnityD3D11Device)
    {
        g_UnityD3D11Device->Release();
        g_UnityD3D11Device = nullptr;
    }
    if (g_UnityD3D11Context)
    {
        g_UnityD3D11Context->Release();
        g_UnityD3D11Context = nullptr;
    }
    
    g_UnityD3D11Device = device;
    g_UnityD3D11Device->AddRef();
    g_UnityD3D11Device->GetImmediateContext(&g_UnityD3D11Context);
    
    // Clear any previous error
    g_errorBuffer[0] = '\0';
}

// MODIFIED: No longer takes device parameter, uses global
extern "C" __declspec(dllexport)
CREncoderHandle CR_InitEncoder(
    int width,
    int height,
    int fps,
    const char* outputPath)
{
    if (!g_UnityD3D11Device)
    {
        SetError("Unity D3D11 device not set - call CR_SetUnityD3D11Device first or use CR_InitEncoderFromTexture");
        return nullptr;
    }

    EncoderContext* ctx = new EncoderContext();
    ctx->width  = width;
    ctx->height = height;
    ctx->fps    = fps;

    // Use the Unity device we stored earlier
    ctx->device = g_UnityD3D11Device;
    ctx->device->AddRef();
    
    // Get immediate context for CopyResource operations
    if (g_UnityD3D11Context)
    {
        ctx->context = g_UnityD3D11Context;
        ctx->context->AddRef();
    }
    else
    {
        ctx->device->GetImmediateContext(&ctx->context);
    }

    AMF_RESULT res = g_AMFFactory.Init();
    if (res != AMF_OK)
    {
        SetError("g_AMFFactory.Init failed");
        delete ctx;
        return nullptr;
    }

    res = g_AMFFactory.GetFactory()->CreateContext(&ctx->amfContext);
    if (res != AMF_OK)
    {
        SetError("CreateContext failed");
        delete ctx;
        return nullptr;
    }

    res = ctx->amfContext->InitDX11(ctx->device);
    if (res != AMF_OK)
    {
        SetError("InitDX11 failed");
        delete ctx;
        return nullptr;
    }

    // Try HEVC first (RX 400+), fall back to H.264 (all AMD GPUs)
    res = g_AMFFactory.GetFactory()->CreateComponent(
        ctx->amfContext,
        AMFVideoEncoder_HEVC,  // CORRECT: From VideoEncoderHEVC.h
        &ctx->encoder);
    
    if (res == AMF_OK)
    {
        ctx->hevcMode = true;
    }
    else
    {
        // Fallback to H.264 for older GPUs (HD 7000, R9 200/300 series)
        res = g_AMFFactory.GetFactory()->CreateComponent(
            ctx->amfContext,
            AMFVideoEncoderVCE_AVC,
            &ctx->encoder);
        
        if (res != AMF_OK)
        {
            SetError("No suitable hardware encoder found (requires AMD VCE 1.0+)");
            delete ctx;
            return nullptr;
        }
        ctx->hevcMode = false;
    }

    // Configure encoder based on mode
    if (ctx->hevcMode)
    {
        // HEVC settings - using string constants from SDK header
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_USAGE, AMF_VIDEO_ENCODER_HEVC_USAGE_TRANSCODING);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_PROFILE, AMF_VIDEO_ENCODER_HEVC_PROFILE_MAIN);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_TIER, AMF_VIDEO_ENCODER_HEVC_TIER_HIGH);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_FRAMESIZE, AMFConstructSize(width, height));
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_FRAMERATE, AMFConstructRate(fps, 1));
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_GOP_SIZE, fps * 2); // 2-second keyframes for scrubbing
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_VBV_BUFFER_SIZE, 80000000);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_INITIAL_VBV_BUFFER_FULLNESS, 80000000 / 2);
        
        // Color settings - using string constants from SDK header
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_INPUT_COLOR_PROFILE, AMF_VIDEO_ENCODER_COLOR_PROFILE_FULL);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_INPUT_COLOR_PRIMARIES, AMF_COLOR_PRIMARIES_BT709);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_INPUT_TRANSFER_CHARACTERISTIC, AMF_COLOR_TRANSFER_CHARACTERISTIC_SRGB);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_OUTPUT_COLOR_PROFILE, AMF_VIDEO_ENCODER_COLOR_PROFILE_FULL);
    }
    else
    {
        // H.264 fallback settings
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_USAGE, AMF_VIDEO_ENCODER_USAGE_TRANSCODING);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_TARGET_BITRATE, 80000000);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_FRAMESIZE, AMFConstructSize(width, height));
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_FRAMERATE, AMFConstructRate(fps, 1));
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_B_PIC_PATTERN, 0);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_INPUT_COLOR_PROFILE, 
                                  AMF_VIDEO_ENCODER_COLOR_PROFILE_FULL);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_INPUT_COLOR_PRIMARIES, 
                                  AMF_COLOR_PRIMARIES_BT709);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_INPUT_TRANSFER_CHARACTERISTIC, 
                                  AMF_COLOR_TRANSFER_CHARACTERISTIC_SRGB);
    }

    res = ctx->encoder->Init(amf::AMF_SURFACE_RGBA, width, height);
    if (res != AMF_OK)
    {
        SetError("encoder->Init failed");
        delete ctx;
        return nullptr;
    }

    // Setup FFmpeg format first (without header)
    if (!InitializeFFmpegMuxer(ctx, outputPath))
    {
        delete ctx;
        return nullptr;
    }

    // Extract extradata (available after encoder Init) and set it before header
    amf::AMFVariant var;
    const wchar_t* extradataProp = ctx->hevcMode ? AMF_VIDEO_ENCODER_HEVC_EXTRADATA : AMF_VIDEO_ENCODER_EXTRADATA;
    
    if (ctx->encoder->GetProperty(extradataProp, &var) == AMF_OK 
        && var.type == amf::AMF_VARIANT_INTERFACE)
    {
        amf::AMFBufferPtr extradata(var.pInterface);
        if (extradata)
        {
            size_t size = extradata->GetSize();
            if (size > 0)
            {
                ctx->videoStream->codecpar->extradata = 
                    (uint8_t*)av_mallocz(size + AV_INPUT_BUFFER_PADDING_SIZE);
                if (ctx->videoStream->codecpar->extradata)
                {
                    memcpy(ctx->videoStream->codecpar->extradata, 
                           extradata->GetNative(), size);
                    ctx->videoStream->codecpar->extradata_size = (int)size;
                }
            }
        }
    }

    // NOW write header (after extradata is set)
    if (!WriteHeader(ctx))
    {
        delete ctx;
        return nullptr;
    }

    // Create encoder-owned double buffers (the only textures AMF touches)
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = width;
    desc.Height = height;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;

    for (int i = 0; i < 2; i++)
    {
        if (FAILED(ctx->device->CreateTexture2D(&desc, nullptr, &ctx->d3dTextures[i])))
        {
            SetError("CreateTexture2D failed for encoder buffer");
            delete ctx;
            return nullptr;
        }

        res = ctx->amfContext->CreateSurfaceFromDX11Native(
            ctx->d3dTextures[i], &ctx->amfSurfaces[i], nullptr);

        if (res != AMF_OK)
        {
            SetError("CreateSurfaceFromDX11Native failed");
            delete ctx;
            return nullptr;
        }
    }

    ctx->initialized = true;
    return ctx;
}

// MODIFIED: Extracts device from texture, stores globally, then calls CR_InitEncoder
extern "C" __declspec(dllexport)
CREncoderHandle CR_InitEncoderFromTexture(
    ID3D11Texture2D* d3d11Texture,
    int width,
    int height,
    int fps,
    const char* outputPath)
{
    if (!d3d11Texture)
    {
        SetError("Null D3D11 texture");
        return nullptr;
    }
    
    // Only extract device if not already set (allows pre-setting via CR_SetUnityD3D11Device)
    if (!g_UnityD3D11Device)
    {
        ID3D11Device* device = nullptr;
        d3d11Texture->GetDevice(&device);
        if (!device)
        {
            SetError("Failed to get D3D11 device from texture");
            return nullptr;
        }
        
        CR_SetUnityD3D11Device(device);
        device->Release(); // CR_SetUnityD3D11Device does its own AddRef
    }
    else
    {
        // Optional: Verify texture matches our device
        ID3D11Device* textureDevice = nullptr;
        d3d11Texture->GetDevice(&textureDevice);
        if (textureDevice)
        {
            if (textureDevice != g_UnityD3D11Device)
            {
                SetError("Texture device mismatch with global Unity device");
                textureDevice->Release();
                return nullptr;
            }
            textureDevice->Release();
        }
    }
    
    // Use the device previously set by CR_SetUnityD3D11Device (either now or earlier)
    return CR_InitEncoder(width, height, fps, outputPath);
}

// CRITICAL: Copy from Unity texture (source) to our encoder texture (destination), then submit owned texture
extern "C" __declspec(dllexport)
int CR_EncodeFrame(
    CREncoderHandle encoder,
    ID3D11Texture2D* unityTexture,
    long long frameIndex)
{
    EncoderContext* ctx = (EncoderContext*)encoder;
    if (!ctx || !ctx->initialized || !unityTexture)
    {
        SetError("Invalid encoder context or null texture");
        return -1;
    }

    // Validate format (keep for troubleshooting user reports)
    D3D11_TEXTURE2D_DESC srcDesc;
    unityTexture->GetDesc(&srcDesc);
    
    bool validFormat = false;
    switch (srcDesc.Format)
    {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS:      // 27
        case DXGI_FORMAT_R8G8B8A8_UNORM:         // 28
        case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:    // 29
        case DXGI_FORMAT_B8G8R8A8_UNORM:         // 87
        case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:    // 115
            validFormat = true;
            break;
    }
    
    if (!validFormat)
    {
        char msg[256];
        snprintf(msg, sizeof(msg), "Unsupported texture format: %d (0x%x). Expected RGBA8 or BGRA8 variant.", 
            srcDesc.Format, srcDesc.Format);
        SetError(msg);
        return -1;
    }

    int idx = ctx->bufferIndex;
    ctx->bufferIndex = 1 - idx;

    // GPU copy Unity texture → encoder-owned texture
    ctx->context->CopyResource(ctx->d3dTextures[idx], unityTexture);
    
    // NOTE: We intentionally do NOT Flush() here - the fence in C# ensures 
    // the Blit completed, and CopyResource on same context is ordered.
    // This keeps the pipeline async for performance.

    // Submit to AMF encoder
    AMF_RESULT res = ctx->encoder->SubmitInput(ctx->amfSurfaces[idx]);
    if (res != AMF_OK && res != AMF_INPUT_FULL)
    {
        SetError("AMF SubmitInput failed");
        return -1;
    }

    // Drain encoded packets...
    amf::AMFDataPtr data;
    while (ctx->encoder->QueryOutput(&data) == AMF_OK && data)
    {
        amf::AMFBufferPtr buffer(data);
        AVPacket pkt{};
        av_init_packet(&pkt);

        // Use correct output data type property based on codec
        amf_int64 outputDataType = AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_P;
        const wchar_t* outputTypeProp = ctx->hevcMode ? AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE : AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE;
        buffer->GetProperty(outputTypeProp, &outputDataType);
        
        if (outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_IDR ||
            outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_I)
        {
            pkt.flags |= AV_PKT_FLAG_KEY;
        }

        pkt.data = (uint8_t*)buffer->GetNative();
        pkt.size = (int)buffer->GetSize();
        pkt.pts = ctx->frameCount;
        pkt.dts = ctx->frameCount;
        pkt.duration = 1;
        pkt.stream_index = ctx->videoStream->index;

        av_packet_rescale_ts(&pkt, ctx->timeBase, ctx->videoStream->time_base);

        {
            std::lock_guard<std::mutex> lock(ctx->writeMutex);
            av_interleaved_write_frame(ctx->formatContext, &pkt);
        }

        ctx->frameCount++;
    }

    return 0;
}

extern "C" __declspec(dllexport)
int CR_ShutdownEncoder(CREncoderHandle encoder)
{
    EncoderContext* ctx = (EncoderContext*)encoder;
    if (!ctx) return 0;

    if (ctx->encoder)
    {
        ctx->encoder->Drain();
        
        amf::AMFDataPtr data;
        while (ctx->encoder->QueryOutput(&data) == AMF_OK && data)
        {
            amf::AMFBufferPtr buffer(data);
            AVPacket pkt{};
            av_init_packet(&pkt);
            
            // Use correct output data type property based on codec
            amf_int64 outputDataType = AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_P;
            const wchar_t* outputTypeProp = ctx->hevcMode ? AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE : AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE;
            buffer->GetProperty(outputTypeProp, &outputDataType);
            
            if (outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_IDR ||
                outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_I)
            {
                pkt.flags |= AV_PKT_FLAG_KEY;
            }
            
            pkt.data = (uint8_t*)buffer->GetNative();
            pkt.size = (int)buffer->GetSize();
            pkt.pts = ctx->frameCount;
            pkt.dts = ctx->frameCount;
            pkt.duration = 1;
            pkt.stream_index = ctx->videoStream->index;
            av_packet_rescale_ts(&pkt, ctx->timeBase, ctx->videoStream->time_base);
            
            {
                std::lock_guard<std::mutex> lock(ctx->writeMutex);
                av_interleaved_write_frame(ctx->formatContext, &pkt);
            }
            ctx->frameCount++;
        }
        
        ctx->encoder->Terminate();
        ctx->encoder = nullptr;
    }

    if (ctx->formatContext)
    {
        if (ctx->headerWritten)
            av_write_trailer(ctx->formatContext);
            
        if (!(ctx->formatContext->oformat->flags & AVFMT_NOFILE))
            avio_closep(&ctx->formatContext->pb);
        avformat_free_context(ctx->formatContext);
    }

    for (int i = 0; i < 2; i++)
    {
        if (ctx->amfSurfaces[i]) ctx->amfSurfaces[i] = nullptr;
        if (ctx->d3dTextures[i]) ctx->d3dTextures[i]->Release();
        ctx->d3dTextures[i] = nullptr;
    }

    if (ctx->amfContext)
    {
        ctx->amfContext->Terminate();
        ctx->amfContext = nullptr;
    }

    g_AMFFactory.Terminate();

    // Release our refs to the device/context (globals remain for other encoders)
    if (ctx->context) 
    {
        ctx->context->Release();
        ctx->context = nullptr;
    }
    if (ctx->device) 
    {
        ctx->device->Release();
        ctx->device = nullptr;
    }

    delete ctx;
    return 0;
}