#include "CinematicRecorderNative.h"
#include "EmbeddedResources.h"
#include "TemporalAccumulation.h"  // TAB accumulation compute shader
#include "CASSharpen.h"            // CAS sharpening compute shader
#include "HiZ.h"                   // Hi-Z generation compute shader

#include <string>
#include <vector>
#include <mutex>
#include <cstring>
#include <cmath>  // NEW: For exp() function in Gaussian calculation

// ---------------- AMF 1.5 ----------------
#include "AMFFactory.h"
#include "components/VideoEncoderVCE.h"
#include "components/VideoEncoderHEVC.h"
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
// Simple file logger for debugging
#include <fstream>
#include <ctime>
#include <iomanip>

static std::mutex g_logMutex;
static std::ofstream g_logFile;
static bool g_logInitialized = false;

static void InitLogFile()
{
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (g_logInitialized) return;
    
    // Get the DLL's own path
    char dllPath[MAX_PATH] = {0};
    HMODULE hModule = NULL;
    
    // Get handle to this DLL using an address within the DLL
    if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | 
                           GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                           (LPCSTR)&InitLogFile, &hModule))
    {
        GetModuleFileNameA(hModule, dllPath, MAX_PATH);
    }
    
    // If we got the path, extract directory and go up one level
    if (strlen(dllPath) > 0)
    {
        // Find last backslash (before DLL filename)
        char* lastSlash = strrchr(dllPath, '\\');
        if (lastSlash)
        {
            *lastSlash = '\0';  // Remove DLL filename, now ends with \PluginData
            
            // Find the next backslash (before PluginData folder)
            char* secondLastSlash = strrchr(dllPath, '\\');
            if (secondLastSlash)
            {
                *(secondLastSlash + 1) = '\0';  // Keep backslash, remove \PluginData
                strcat_s(dllPath, MAX_PATH, "CinematicRecorder_Native.log");
                g_logFile.open(dllPath, std::ios::app);
            }
        }
    }
    
    // Fallback if we couldn't get DLL path
    if (!g_logFile.is_open())
    {
        g_logFile.open("CinematicRecorder_Native.log", std::ios::app);
    }
    
    g_logInitialized = true;
    
    if (g_logFile.is_open())
    {
        auto now = std::time(nullptr);
        auto tm = *std::localtime(&now);
        g_logFile << "\n=== Session started at " 
                  << std::put_time(&tm, "%Y-%m-%d %H:%M:%S") 
                  << " ===\n" << std::flush;
    }
}

static void LogToFile(const char* fmt, ...)
{
    InitLogFile();
    
    std::lock_guard<std::mutex> lock(g_logMutex);
    if (!g_logFile.is_open()) return;
    
    // Timestamp
    auto now = std::time(nullptr);
    auto tm = *std::localtime(&now);
    g_logFile << "[" << std::put_time(&tm, "%H:%M:%S") << "] ";
    
    // Message
    char buffer[1024];
    va_list args;
    va_start(args, fmt);
    vsnprintf(buffer, sizeof(buffer), fmt, args);
    va_end(args);
    
    g_logFile << buffer << std::endl << std::flush;
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

thread_local char g_errorBuffer[1024];

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

// MODIFIED: Extended EncoderContext with TAB resources
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
    bool hevcMode = false;                  // true if using HEVC, false if H.264
    std::mutex writeMutex;
    AmfEncoderSettings settings;            // Store settings for reference
    
    // Blue Noise Dithering Resources (existing)
    ID3D11ComputeShader* ditherShader = nullptr;
    ID3D11Buffer*        constantBuffer = nullptr;
    ID3D11Texture2D*     blueNoiseTexture = nullptr;
    ID3D11ShaderResourceView* blueNoiseSRV = nullptr;
    ID3D11UnorderedAccessView* encoderUAV[2] = {}; // UAVs for d3dTextures
    
    bool useBlueNoiseDither = false;
    UINT frameCounter = 0;
    
    // NEW: Temporal Accumulation Blur Resources
    ID3D11Texture2D* accumulationArray[2] = {nullptr, nullptr};           // Double buffered
    ID3D11ShaderResourceView* accumulationSRV[2] = {nullptr, nullptr};    // SRV for each array
    int currentAccumBuffer = 0;                                           // 0 or 1, toggles per frame
    ID3D11ComputeShader* tabComputeShader = nullptr;        // TAB compute shader
    ID3D11ComputeShader* casShader = nullptr;               // CAS sharpening compute shader
    ID3D11Buffer* tabWeightBuffer = nullptr;                // Constant buffer for Gaussian weights
    bool isTabMode = false;                                 // TAB enabled flag
    int tabSubFrameCount = 8;                               // Number of sub-frames (typically 8)
    int currentSubFrame = 0;                                // Current sub-frame index being filled
    ID3D11Query* preComputeQuery = nullptr;                 // GPU sync query for pre-compute
    ID3D11Query* postComputeQuery = nullptr;                // GPU sync query for post-compute
    float tabWeights[8] = {0};                              // Gaussian weights
    float tabTotalWeight = 0;                               // Sum of weights for normalization
    std::mutex tabMutex;                                    // Protects TAB state during Submit/Finalize
    
    // NEW: Sharpening filter settings
    bool tabSharpeningEnabled = false;
    float tabSharpeningStrength = 0.25f;
    ID3D11Buffer* tabSharpeningBuffer = nullptr;            // Constant buffer for sharpening params
    
    // NEW: Non-TAB path synchronization resources
    std::mutex encodeMutex;                                 // Protects Non-TAB encode operations
    ID3D11Query* encodeSyncQuery = nullptr;                 // GPU sync query for Non-TAB path
};

// Forward declaration
static bool WriteHeader(EncoderContext* ctx);

// NEW: Helper function to create TAB resources
static bool CreateTabResources(EncoderContext* ctx, const TabSettings* settings)
{
    HRESULT hr;
    
    // Store settings
    ctx->isTabMode = (settings->Enabled != 0);
    ctx->tabSubFrameCount = settings->SubFrameCount;
    if (ctx->tabSubFrameCount < 1 || ctx->tabSubFrameCount > 8)
        ctx->tabSubFrameCount = 8;
    
    // Calculate Gaussian weights
    float sigma = settings->Sigma;
    if (sigma <= 0.0f) sigma = 1.5f;
    
    ctx->tabTotalWeight = 0.0f;
    float center = (ctx->tabSubFrameCount - 1) / 2.0f;
    
    for (int i = 0; i < ctx->tabSubFrameCount; i++)
    {
        float x = i - center;
        float weight = expf(-(x * x) / (2.0f * sigma * sigma));
        ctx->tabWeights[i] = weight;
        ctx->tabTotalWeight += weight;
    }
    
    // Normalize weights
    for (int i = 0; i < ctx->tabSubFrameCount; i++)
    {
        ctx->tabWeights[i] /= ctx->tabTotalWeight;
    }
    
    // Create accumulation array texture (ArraySize=8)
    D3D11_TEXTURE2D_DESC arrayDesc = {};
    arrayDesc.Width = ctx->width;
    arrayDesc.Height = ctx->height;
    arrayDesc.MipLevels = 1;
    arrayDesc.ArraySize = ctx->tabSubFrameCount;
    arrayDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    arrayDesc.SampleDesc.Count = 1;
    arrayDesc.Usage = D3D11_USAGE_DEFAULT;
    arrayDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
    
    // Create BOTH arrays [0] and [1]
    for (int buf = 0; buf < 2; buf++)
    {
        hr = ctx->device->CreateTexture2D(&arrayDesc, nullptr, &ctx->accumulationArray[buf]);
        if (FAILED(hr))
        {
            SetError("Failed to create accumulation array texture");
            return false;
        }
        
        // Clear to black
        ID3D11UnorderedAccessView* clearUAV = nullptr;
        D3D11_UNORDERED_ACCESS_VIEW_DESC clearUAVDesc = {};
        clearUAVDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        clearUAVDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2DARRAY;
        clearUAVDesc.Texture2DArray.MipSlice = 0;
        clearUAVDesc.Texture2DArray.FirstArraySlice = 0;
        clearUAVDesc.Texture2DArray.ArraySize = ctx->tabSubFrameCount;
        
        if (SUCCEEDED(ctx->device->CreateUnorderedAccessView(ctx->accumulationArray[buf], &clearUAVDesc, &clearUAV))) {
            UINT clearValues[4] = {0, 0, 0, 0};
            ctx->context->ClearUnorderedAccessViewUint(clearUAV, clearValues);
            clearUAV->Release();
        }
    }
    ctx->context->Flush(); // One flush after both clears
    
    // Create SRVs for both arrays
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_R10G10B10A2_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2DARRAY;
    srvDesc.Texture2DArray.MipLevels = 1;
    srvDesc.Texture2DArray.ArraySize = ctx->tabSubFrameCount;
    
    for (int buf = 0; buf < 2; buf++)
    {
        hr = ctx->device->CreateShaderResourceView(ctx->accumulationArray[buf], &srvDesc, &ctx->accumulationSRV[buf]);
        if (FAILED(hr))
        {
            SetError("Failed to create accumulation array SRV");
            return false;
        }
    }
    
    // Create TAB compute shader
    hr = ctx->device->CreateComputeShader(
        g_TemporalAccumulationCS, 
        sizeof(g_TemporalAccumulationCS),
        nullptr, 
        &ctx->tabComputeShader
    );
    if (FAILED(hr))
    {
        SetError("Failed to create TAB compute shader");
        return false;
    }
    
    // Create CAS sharpening compute shader
    hr = ctx->device->CreateComputeShader(
        g_CASSharpenCS,
        sizeof(g_CASSharpenCS),
        nullptr,
        &ctx->casShader
    );
    if (FAILED(hr))
    {
        SetError("Failed to create CAS compute shader");
        return false;
    }
    
    // Create constant buffer for weights
    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = 48;
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    
    struct WeightData {
        float weights[8];
        float totalWeight;
        float padding[3];
    } weightData;
    
    memcpy(weightData.weights, ctx->tabWeights, sizeof(float) * 8);
    weightData.totalWeight = 1.0f;
    weightData.padding[0] = weightData.padding[1] = weightData.padding[2] = 0.0f;
    
    D3D11_SUBRESOURCE_DATA initData = {};
    initData.pSysMem = &weightData;
    
    hr = ctx->device->CreateBuffer(&cbDesc, &initData, &ctx->tabWeightBuffer);
    if (FAILED(hr))
    {
        SetError("Failed to create TAB weight constant buffer");
        return false;
    }
    
ctx->currentSubFrame = 0;
    ctx->currentAccumBuffer = 0; // Start with buffer 0
    
    // Create persistent GPU queries for hard synchronization (no memory thrashing)
    D3D11_QUERY_DESC queryDesc = {};
    queryDesc.Query = D3D11_QUERY_EVENT;  // Simple event query
    
    if (FAILED(ctx->device->CreateQuery(&queryDesc, &ctx->preComputeQuery))) {
        SetError("Failed to create pre-compute sync query");
        return false;
    }
    
    if (FAILED(ctx->device->CreateQuery(&queryDesc, &ctx->postComputeQuery))) {
        SetError("Failed to create post-compute sync query");
        return false;
    }
    
    LogToFile("[CinematicRecorder] Temporal Accumulation Blur enabled (double-buffered)");
    return true;
}

// NEW: Helper function to destroy TAB resources
static void DestroyTabResources(EncoderContext* ctx)
{
    if (ctx->postComputeQuery) { ctx->postComputeQuery->Release(); ctx->postComputeQuery = nullptr; }
    if (ctx->preComputeQuery) { ctx->preComputeQuery->Release(); ctx->preComputeQuery = nullptr; }
    if (ctx->casShader) { ctx->casShader->Release(); ctx->casShader = nullptr; }
    if (ctx->tabSharpeningBuffer) { ctx->tabSharpeningBuffer->Release(); ctx->tabSharpeningBuffer = nullptr; }
    if (ctx->tabWeightBuffer) { ctx->tabWeightBuffer->Release(); ctx->tabWeightBuffer = nullptr; }
    if (ctx->tabComputeShader) { ctx->tabComputeShader->Release(); ctx->tabComputeShader = nullptr; }
    
    // Release both SRVs and arrays
    for (int i = 0; i < 2; i++) {
        if (ctx->accumulationSRV[i]) { ctx->accumulationSRV[i]->Release(); ctx->accumulationSRV[i] = nullptr; }
        if (ctx->accumulationArray[i]) { ctx->accumulationArray[i]->Release(); ctx->accumulationArray[i] = nullptr; }
    }
    
    ctx->isTabMode = false;
    ctx->currentSubFrame = 0;
    ctx->currentAccumBuffer = 0;
    ctx->tabSharpeningEnabled = false;
}

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
    par->color_range = AVCOL_RANGE_JPEG;
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

static bool CreateDitheringResources(EncoderContext* ctx) {
    HRESULT hr;
    D3D11_SUBRESOURCE_DATA initData = {};
    
    // 1. Create Compute Shader from embedded bytecode
    hr = ctx->device->CreateComputeShader(
        g_BlueNoiseDitherCS, 
        sizeof(g_BlueNoiseDitherCS),
        nullptr, 
        &ctx->ditherShader
    );
    if (FAILED(hr)) {
        LogToFile("[CR] Failed to create dither compute shader");
        return false;
    }

    // 2. Create Constant Buffer (16 bytes: 4 uints)
    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = 16; // width, height, frameIdx, flags
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    
    hr = ctx->device->CreateBuffer(&cbDesc, nullptr, &ctx->constantBuffer);
    if (FAILED(hr)) {
        LogToFile("[CR] Failed to create constant buffer");
        return false;
    }

    // 3. Blue Noise Texture (256x256 R8_UNORM)
    D3D11_TEXTURE2D_DESC noiseDesc = {};
    noiseDesc.Width = 256;
    noiseDesc.Height = 256;
    noiseDesc.MipLevels = 1;
    noiseDesc.ArraySize = 1;
    noiseDesc.Format = DXGI_FORMAT_R8_UNORM;
    noiseDesc.SampleDesc.Count = 1;
    noiseDesc.Usage = D3D11_USAGE_IMMUTABLE;
    noiseDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    
    initData.pSysMem = g_BlueNoise256x256R8;
    initData.SysMemPitch = 256; // 256 bytes per row
    
    hr = ctx->device->CreateTexture2D(&noiseDesc, &initData, &ctx->blueNoiseTexture);
    if (FAILED(hr)) {
        LogToFile("[CR] Failed to create blue noise texture");
        return false;
    }
    
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_R8_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = 1;
    
    hr = ctx->device->CreateShaderResourceView(ctx->blueNoiseTexture, &srvDesc, &ctx->blueNoiseSRV);
    if (FAILED(hr)) return false;

    return true;
}

static void DestroyDitheringResources(EncoderContext* ctx) {
    for (int i = 0; i < 2; i++) {
        if (ctx->encoderUAV[i]) { ctx->encoderUAV[i]->Release(); ctx->encoderUAV[i] = nullptr; }
    }
    if (ctx->blueNoiseSRV) { ctx->blueNoiseSRV->Release(); ctx->blueNoiseSRV = nullptr; }
    if (ctx->blueNoiseTexture) { ctx->blueNoiseTexture->Release(); ctx->blueNoiseTexture = nullptr; }
    if (ctx->constantBuffer) { ctx->constantBuffer->Release(); ctx->constantBuffer = nullptr; }
    if (ctx->ditherShader) { ctx->ditherShader->Release(); ctx->ditherShader = nullptr; }
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

// MODIFIED: Accepts settings parameter
extern "C" __declspec(dllexport)
CREncoderHandle CR_InitEncoder(
    int width,
    int height,
    int fps,
    const char* outputPath,
    const AmfEncoderSettings* settings)
{
    if (!g_UnityD3D11Device)
    {
        SetError("Unity D3D11 device not set - call CR_SetUnityD3D11Device first or use CR_InitEncoderFromTexture");
        return nullptr;
    }

    if (!settings)
    {
        SetError("Encoder settings pointer is null");
        return nullptr;
    }

    EncoderContext* ctx = new EncoderContext();
    ctx->width  = width;
    ctx->height = height;
    ctx->fps    = fps;
    ctx->settings = *settings; // Copy settings

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

    // Create encoder based on requested codec (0=H264, 1=HEVC)
    if (settings->Codec == 1) // HEVC requested
    {
        res = g_AMFFactory.GetFactory()->CreateComponent(
            ctx->amfContext,
            AMFVideoEncoder_HEVC,
            &ctx->encoder);
        
        if (res == AMF_OK)
        {
            ctx->hevcMode = true;
        }
        else
        {
            // HEVC failed, try H.264 as fallback with warning
            res = g_AMFFactory.GetFactory()->CreateComponent(
                ctx->amfContext,
                AMFVideoEncoderVCE_AVC,
                &ctx->encoder);
            
            if (res != AMF_OK)
            {
                SetError("HEVC requested but not available, and H.264 fallback failed");
                delete ctx;
                return nullptr;
            }
            ctx->hevcMode = false;
            // Log that we fell back
            // (Could set a warning string but not error since we have a valid fallback)
        }
    }
    else // H.264 requested (0 or default)
    {
        res = g_AMFFactory.GetFactory()->CreateComponent(
            ctx->amfContext,
            AMFVideoEncoderVCE_AVC,
            &ctx->encoder);
        
        if (res != AMF_OK)
        {
            SetError("H.264 encoder creation failed");
            delete ctx;
            return nullptr;
        }
        ctx->hevcMode = false;
    }

    // Configure encoder based on mode using settings
    if (ctx->hevcMode)
    {
        // HEVC settings
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_USAGE, AMF_VIDEO_ENCODER_HEVC_USAGE_TRANSCODING);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_PROFILE, AMF_VIDEO_ENCODER_HEVC_PROFILE_MAIN);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_TIER, AMF_VIDEO_ENCODER_HEVC_TIER_HIGH);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_FRAMESIZE, AMFConstructSize(width, height));
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_FRAMERATE, AMFConstructRate(fps, 1));
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_GOP_SIZE, settings->GopSize);
        
        // Rate control settings
        if (settings->RateControlMode == 0) // CQP
        {
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_RATE_CONTROL_METHOD, 
                AMF_VIDEO_ENCODER_HEVC_RATE_CONTROL_METHOD_CONSTANT_QP);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_QP_I, settings->QpI);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_QP_P, settings->QpP);
        }
        else if (settings->RateControlMode == 1) // HQVBR (High Quality VBR)
        {
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_RATE_CONTROL_METHOD, 
                AMF_VIDEO_ENCODER_HEVC_RATE_CONTROL_METHOD_HIGH_QUALITY_VBR);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_TARGET_BITRATE, 
                settings->TargetBitrateKbps * 1000);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_VBV_BUFFER_SIZE, 
                settings->TargetBitrateKbps * 1000);

            // Allow encoder to go as low as QP 2 for smooth gradients
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_MIN_QP_I, 2);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_MIN_QP_P, 2);

            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_PREENCODE_ENABLE, true); //Pre-Encode required for HQVBR
            
            
            // VBAQ works with HQVBR (unlike CQP) - redistributes bits to protect smooth gradients
            if (settings->EnableVbaq)
            {
                res = ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_ENABLE_VBAQ, true);
                if (res == AMF_OK) {
                    LogToFile("[CinematicRecorder] HQVBR + VBAQ enabled");
                }
            }
        }
        else // CBR (2 or default)
        {
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_RATE_CONTROL_METHOD, 
                AMF_VIDEO_ENCODER_HEVC_RATE_CONTROL_METHOD_CBR);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_TARGET_BITRATE, 
                settings->TargetBitrateKbps * 1000);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_VBV_BUFFER_SIZE, 
                settings->TargetBitrateKbps * 1000);
        }
        
        // Quality preset (0=Speed, 1=Balanced, 2=Quality)
        amf_int64 qualityPreset = AMF_VIDEO_ENCODER_HEVC_QUALITY_PRESET_BALANCED;
        if (settings->QualityPreset == 0)
            qualityPreset = AMF_VIDEO_ENCODER_HEVC_QUALITY_PRESET_SPEED;
        else if (settings->QualityPreset == 2)
            qualityPreset = AMF_VIDEO_ENCODER_HEVC_QUALITY_PRESET_QUALITY;
            
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_QUALITY_PRESET, qualityPreset);
        
        // Color settings
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_INPUT_COLOR_PROFILE, AMF_VIDEO_ENCODER_COLOR_PROFILE_FULL);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_INPUT_COLOR_PRIMARIES, AMF_COLOR_PRIMARIES_BT709);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_INPUT_TRANSFER_CHARACTERISTIC, AMF_COLOR_TRANSFER_CHARACTERISTIC_SRGB);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_HEVC_OUTPUT_COLOR_PROFILE, AMF_VIDEO_ENCODER_COLOR_PROFILE_FULL);
    }
    else
    {
        // H.264 settings
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_USAGE, AMF_VIDEO_ENCODER_USAGE_TRANSCODING);
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_FRAMESIZE, AMFConstructSize(width, height));
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_FRAMERATE, AMFConstructRate(fps, 1));
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_IDR_PERIOD, settings->GopSize);
        
        // Rate control settings
        if (settings->RateControlMode == 0) // CQP
        {
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD, 
                AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD_CONSTANT_QP);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_QP_I, settings->QpI);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_QP_P, settings->QpP);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_QP_B, settings->QpB);
        }
        else if (settings->RateControlMode == 1) // VBR
        {
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD, 
                AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD_PEAK_CONSTRAINED_VBR);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_TARGET_BITRATE, 
                settings->TargetBitrateKbps * 1000);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_VBV_BUFFER_SIZE, 
                settings->TargetBitrateKbps * 1000);
        }
        else // CBR
        {
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD, 
                AMF_VIDEO_ENCODER_RATE_CONTROL_METHOD_CBR);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_TARGET_BITRATE, 
                settings->TargetBitrateKbps * 1000);
            ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_VBV_BUFFER_SIZE, 
                settings->TargetBitrateKbps * 1000);
        }
        
        // Quality preset
        amf_int64 qualityPreset = AMF_VIDEO_ENCODER_QUALITY_PRESET_BALANCED;
        if (settings->QualityPreset == 0)
            qualityPreset = AMF_VIDEO_ENCODER_QUALITY_PRESET_SPEED;
        else if (settings->QualityPreset == 2)
            qualityPreset = AMF_VIDEO_ENCODER_QUALITY_PRESET_QUALITY;
            
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_QUALITY_PRESET, qualityPreset);
        
        // B-frames disabled for latency
        ctx->encoder->SetProperty(AMF_VIDEO_ENCODER_B_PIC_PATTERN, 0);
        
        // Color settings
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
    desc.BindFlags = D3D11_BIND_RENDER_TARGET | 
                 D3D11_BIND_SHADER_RESOURCE | 
                 D3D11_BIND_UNORDERED_ACCESS;  // Required for compute write

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

        // Pre-create UAV for TAB compute shader output
        D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Texture2D.MipSlice = 0;
        ctx->device->CreateUnorderedAccessView(ctx->d3dTextures[i], &uavDesc, &ctx->encoderUAV[i]);
    }

    // Initialize Blue Noise dithering if requested
    ctx->useBlueNoiseDither = (settings->UseBlueNoiseDither != 0);

    if (ctx->useBlueNoiseDither) {
        if (!CreateDitheringResources(ctx)) {
            LogToFile("[CR] Blue Noise init failed, falling back to CopyResource");
            ctx->useBlueNoiseDither = false;
        } else {
            // Create UAVs on the encoder textures for compute shader output
            HRESULT hr;
            for (int i = 0; i < 2; i++) {
                D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
                uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
                uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
                uavDesc.Texture2D.MipSlice = 0;
                
                hr = ctx->device->CreateUnorderedAccessView(
                    ctx->d3dTextures[i], &uavDesc, &ctx->encoderUAV[i]);
                    
                if (FAILED(hr)) {
                    LogToFile("[CR] Failed to create encoder UAV");
                    ctx->useBlueNoiseDither = false;
                    DestroyDitheringResources(ctx);
                    break;
                }
            }
            
            if (ctx->useBlueNoiseDither) {
                LogToFile("[CR] Blue Noise dithering enabled");
            }
        }
    }
    
    // Create GPU sync query for Non-TAB path (ensures copy/compute complete before encode)
    D3D11_QUERY_DESC queryDesc = {};
    queryDesc.Query = D3D11_QUERY_EVENT;
    if (FAILED(ctx->device->CreateQuery(&queryDesc, &ctx->encodeSyncQuery))) {
        LogToFile("[CR] Warning: Failed to create Non-TAB encode sync query");
        // Non-fatal: can still work with Flush() fallback
    }

    ctx->initialized = true;
    return ctx;
}

// MODIFIED: Accepts settings and passes through
extern "C" __declspec(dllexport)
CREncoderHandle CR_InitEncoderFromTexture(
    ID3D11Texture2D* d3d11Texture,
    int width,
    int height,
    int fps,
    const char* outputPath,
    const AmfEncoderSettings* settings)
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
    return CR_InitEncoder(width, height, fps, outputPath, settings);
}

// NEW: Configure Temporal Accumulation Blur mode
extern "C" __declspec(dllexport)
int CR_SetTemporalAccumulation(CREncoderHandle encoder, const TabSettings* settings)
{
    EncoderContext* ctx = (EncoderContext*)encoder;
    if (!ctx || !ctx->initialized)
    {
        SetError("Invalid encoder context");
        return -1;
    }
    
    if (!settings)
    {
        SetError("Null TAB settings");
        return -1;
    }
    
    // Clean up any existing TAB resources first
    if (ctx->isTabMode)
    {
        DestroyTabResources(ctx);
    }
    
    // If enabling TAB, create resources
    if (settings->Enabled)
    {
        if (!CreateTabResources(ctx, settings))
        {
            // Error already set by CreateTabResources
            return -1;
        }
    }
    
    return 0;
}

// NEW: Configure sharpening filter for TAB output
extern "C" __declspec(dllexport)
int CR_SetTabSharpening(CREncoderHandle encoder, int enabled, float strength)
{
    EncoderContext* ctx = (EncoderContext*)encoder;
    if (!ctx || !ctx->initialized)
    {
        SetError("Invalid encoder context");
        return -1;
    }
    
    // CRITICAL: Acquire mutex to protect TAB state
    std::lock_guard<std::mutex> lock(ctx->tabMutex);
    
    ctx->tabSharpeningEnabled = (enabled != 0);
    ctx->tabSharpeningStrength = strength;
    
    // Clamp strength to valid range
    if (ctx->tabSharpeningStrength < 0.0f) ctx->tabSharpeningStrength = 0.0f;
    if (ctx->tabSharpeningStrength > 1.0f) ctx->tabSharpeningStrength = 1.0f;
    
    LogToFile("[CinematicRecorder] Sharpening %s (strength=%.2f)", 
              ctx->tabSharpeningEnabled ? "enabled" : "disabled", 
              ctx->tabSharpeningStrength);
    
    return 0;
}

// NEW: Submit a single sub-frame to the accumulation array
extern "C" __declspec(dllexport)
int CR_SubmitSubFrame(CREncoderHandle encoder, ID3D11Texture2D* unityTexture, int subFrameIndex)
{
    EncoderContext* ctx = (EncoderContext*)encoder;
    if (!ctx || !ctx->initialized)
    {
        SetError("Invalid encoder context");
        return -1;
    }
    
    if (!ctx->isTabMode)
    {
        SetError("TAB mode not enabled");
        return -1;
    }
    
    if (!unityTexture)
    {
        SetError("Null unity texture");
        return -1;
    }
    
if (subFrameIndex < 0 || subFrameIndex >= ctx->tabSubFrameCount)
    {
        SetError("Sub-frame index out of range");
        return -1;
    }

    // CRITICAL: Acquire mutex FIRST - protects all state access and GPU operations
    std::lock_guard<std::mutex> lock(ctx->tabMutex);

    // CRITICAL: Ensure previous frame's compute is done before we overwrite this buffer
    // This prevents resource hazards where we write to accumulationArray while compute is reading
    if (subFrameIndex == 0 && ctx->postComputeQuery)
    {
        ctx->context->End(ctx->preComputeQuery);  // Insert marker
        DWORD startTime = GetTickCount();
        while (S_FALSE == ctx->context->GetData(ctx->preComputeQuery, nullptr, 0, 0))
        {
            Sleep(1);  // Yield CPU to allow driver processing
            // Add timeout detection (1 second)
            if (GetTickCount() - startTime > 1000)
            {
                LogToFile("[CR] FATAL: preComputeQuery timeout in SubmitSubFrame - GPU sync broken");
                break;
            }
        }
    }

    // Validate sub-frame order (prevents gaps in accumulation)
    if (subFrameIndex != ctx->currentSubFrame)
    {
        LogToFile("[CR] ERROR: Out-of-order sub-frame submission. Expected %d, got %d", 
                  ctx->currentSubFrame, subFrameIndex);
        return -1;
    }
    
    // Validate dimensions
    D3D11_TEXTURE2D_DESC srcDesc;
    unityTexture->GetDesc(&srcDesc);
    if (srcDesc.Width != (UINT)ctx->width || srcDesc.Height != (UINT)ctx->height)
    {
        LogToFile("[CR] SUBFRAME DIMENSION ERROR: Expected %dx%d but got %ux%u (subFrame %d)", 
                  ctx->width, ctx->height, srcDesc.Width, srcDesc.Height, subFrameIndex);
    }

    // Copy to CURRENT double-buffer (0 or 1)
    ctx->context->CopySubresourceRegion(
        ctx->accumulationArray[ctx->currentAccumBuffer],  // Use current buffer
        subFrameIndex,
        0, 0, 0,
        unityTexture,
        0,
        nullptr
    );
    // Only flush on last sub-frame to reduce driver overhead
    // Query sync in FinalizeTemporalFrame ensures proper ordering
    if (subFrameIndex == ctx->tabSubFrameCount - 1)
    {
        ctx->context->Flush();
    }
    ctx->currentSubFrame++;
    return 0;
}

// NEW: Finalize accumulated frames and encode
extern "C" __declspec(dllexport)
int CR_FinalizeTemporalFrame(CREncoderHandle encoder, long long outputFrameIndex)
{
    EncoderContext* ctx = (EncoderContext*)encoder;
    if (!ctx || !ctx->initialized)
    {
        SetError("Invalid encoder context");
        return -1;
    }
    
    if (!ctx->isTabMode)
    {
        SetError("TAB mode not enabled");
        return -1;
    }

    // CRITICAL: Acquire mutex FIRST - protects all state access
    std::lock_guard<std::mutex> lock(ctx->tabMutex);

    // NOW safe to check sub-frame count (inside mutex)
    if (ctx->currentSubFrame != ctx->tabSubFrameCount) {
        LogToFile("[CR] WARNING: Finalizing frame %lld with only %d/%d sub-frames! Output will be dark.", 
                  outputFrameIndex, ctx->currentSubFrame, ctx->tabSubFrameCount);
        
        // DEBUG: Log which specific slices are missing (for debugging 16x16 artifacts)
        for (int i = ctx->currentSubFrame; i < ctx->tabSubFrameCount; i++) {
            LogToFile("[CR] DEBUG: Slice %d/%d unfilled for frame %lld (will read stale data)", 
                      i, ctx->tabSubFrameCount, outputFrameIndex);
        }
    }
    
    // CRITICAL: Save count THEN reset immediately so next frame starts fresh even if we crash
    int submittedSubFrames = ctx->currentSubFrame;
    ctx->currentSubFrame = 0;
    
    // Guard against re-entry or partial finalization
    if (submittedSubFrames == 0) {
        LogToFile("[CR] WARNING: Finalize called with 0 sub-frames, skipping frame %lld", outputFrameIndex);
        return -1;
    }
    
    int idx = ctx->bufferIndex;
    ctx->bufferIndex = 1 - idx;

    // HARD SYNC: Ensure all 8 sub-frame copies are complete before computing
    if (ctx->preComputeQuery) {
        ctx->context->End(ctx->preComputeQuery);  // Insert marker
        // Stall CPU until GPU reaches this point (all copies before End() are complete)
        DWORD startTime = GetTickCount();
        while (S_FALSE == ctx->context->GetData(ctx->preComputeQuery, nullptr, 0, 0)) {
            Sleep(1);  // Yield CPU to allow driver processing
            // Add timeout detection (5 seconds - should never take this long)
            if (GetTickCount() - startTime > 5000) {
                LogToFile("[CR] FATAL: preComputeQuery timeout in Finalize - GPU sync broken");
                break;
            }
        }
    }
    
    // Bind compute shader
    ctx->context->CSSetShader(ctx->tabComputeShader, nullptr, 0);
    
    // Update sharpening constant buffer (create lazily if needed)
    if (!ctx->tabSharpeningBuffer)
    {
        D3D11_BUFFER_DESC cbDesc = {};
        cbDesc.ByteWidth = 16; // 4 floats: enabled, strength, padding x2
        cbDesc.Usage = D3D11_USAGE_DYNAMIC;
        cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        
        ctx->device->CreateBuffer(&cbDesc, nullptr, &ctx->tabSharpeningBuffer);
    }
    
    if (ctx->tabSharpeningBuffer)
    {
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (SUCCEEDED(ctx->context->Map(ctx->tabSharpeningBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped)))
        {
            struct SharpenParams {
                int enabled;
                float strength;
                float padding[2];
            } params;
            
            params.enabled = ctx->tabSharpeningEnabled ? 1 : 0;
            params.strength = ctx->tabSharpeningStrength;
            params.padding[0] = params.padding[1] = 0.0f;
            
            memcpy(mapped.pData, &params, sizeof(params));
            ctx->context->Unmap(ctx->tabSharpeningBuffer, 0);
        }
    }
    
    // Bind constant buffers: b0 = weights, b1 = sharpening
    ID3D11Buffer* constantBuffers[2] = { ctx->tabWeightBuffer, ctx->tabSharpeningBuffer };
    ctx->context->CSSetConstantBuffers(0, 2, constantBuffers);
    
    // Bind CURRENT accumulation array (double-buffered)
    ctx->context->CSSetShaderResources(0, 1, &ctx->accumulationSRV[ctx->currentAccumBuffer]);
    
    // Bind UAV
    ID3D11UnorderedAccessView* uav = ctx->encoderUAV[idx];
    bool createdTemporaryUav = false;
    ID3D11UnorderedAccessView* bnUav = nullptr;
    bool createdBnUav = false;
    
    if (!uav)
    {
        D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Texture2D.MipSlice = 0;
        
        HRESULT hr = ctx->device->CreateUnorderedAccessView(ctx->d3dTextures[idx], &uavDesc, &uav);
        if (FAILED(hr))
        {
            SetError("Failed to create UAV for TAB");
            return -1;
        }
        createdTemporaryUav = true;
    }
    
    ctx->context->CSSetUnorderedAccessViews(0, 1, &uav, nullptr);
    
    // Dispatch TAB accumulation
    UINT dispatchX = (ctx->width + 15) / 16;
    UINT dispatchY = (ctx->height + 15) / 16;
    ctx->context->Dispatch(dispatchX, dispatchY, 1);
    
    // HARD SYNC: TAB completion before CAS
    if (ctx->postComputeQuery) {
        ctx->context->End(ctx->postComputeQuery);
        DWORD startTime = GetTickCount();
        while (S_FALSE == ctx->context->GetData(ctx->postComputeQuery, nullptr, 0, 0)) {
            Sleep(1);
            if (GetTickCount() - startTime > 5000) {
                LogToFile("[CR] FATAL: postComputeQuery timeout after TAB");
                break;
            }
        }
    }

    // Unbind TAB resources completely
    ID3D11UnorderedAccessView* nullUAV[1] = { nullptr };
    ID3D11ShaderResourceView* nullSRV[1] = { nullptr };
    ID3D11Buffer* nullCB[1] = { nullptr };
    ctx->context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    ctx->context->CSSetShaderResources(0, 1, nullSRV);
    ctx->context->CSSetConstantBuffers(0, 1, nullCB);
    ctx->context->CSSetShader(nullptr, nullptr, 0);
    
    // EXTRA SYNC: Flush context to ensure all GPU work completes before next pass
    ctx->context->Flush();
    
    // Create SRV for TAB result (used by CAS)
    ID3D11ShaderResourceView* tabResultSRV = nullptr;
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = 1;
    
    HRESULT hr = ctx->device->CreateShaderResourceView(ctx->d3dTextures[idx], &srvDesc, &tabResultSRV);
    if (FAILED(hr)) {
        SetError("Failed to create SRV for TAB result");
        if (createdTemporaryUav && uav) uav->Release();
        return -1;
    }
    
    // Stage 2: CAS Sharpening (if enabled)
    int casOutputIdx = idx;
    if (ctx->tabSharpeningEnabled && ctx->casShader)
    {
        casOutputIdx = 1 - idx;
        
        // Update CAS constant buffer
        if (ctx->tabSharpeningBuffer)
        {
            D3D11_MAPPED_SUBRESOURCE mapped;
            if (SUCCEEDED(ctx->context->Map(ctx->tabSharpeningBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped)))
            {
                struct CASParams {
                    float sharpness;
                    float padding[3];
                } params;
                
                params.sharpness = ctx->tabSharpeningStrength;
                params.padding[0] = params.padding[1] = params.padding[2] = 0.0f;
                
                memcpy(mapped.pData, &params, sizeof(params));
                ctx->context->Unmap(ctx->tabSharpeningBuffer, 0);
            }
        }
        
        // Bind CAS shader
        ctx->context->CSSetShader(ctx->casShader, nullptr, 0);
        ctx->context->CSSetConstantBuffers(0, 1, &ctx->tabSharpeningBuffer);
        ctx->context->CSSetShaderResources(0, 1, &tabResultSRV);
        
        // Bind output UAV (other texture)
        ID3D11UnorderedAccessView* casUav = ctx->encoderUAV[casOutputIdx];
        bool createdCasUav = false;
        if (!casUav)
        {
            D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
            uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
            uavDesc.Texture2D.MipSlice = 0;
            
            hr = ctx->device->CreateUnorderedAccessView(ctx->d3dTextures[casOutputIdx], &uavDesc, &casUav);
            if (FAILED(hr)) {
                SetError("Failed to create UAV for CAS output");
                tabResultSRV->Release();
                if (createdTemporaryUav && uav) uav->Release();
                return -1;
            }
            createdCasUav = true;
        }
        
        ctx->context->CSSetUnorderedAccessViews(0, 1, &casUav, nullptr);
        
        // Dispatch CAS
        ctx->context->Dispatch(dispatchX, dispatchY, 1);
        
        // HARD SYNC: CAS completion
        if (ctx->postComputeQuery) {
            ctx->context->End(ctx->postComputeQuery);
            DWORD startTime = GetTickCount();
            while (S_FALSE == ctx->context->GetData(ctx->postComputeQuery, nullptr, 0, 0)) {
                Sleep(1);
                if (GetTickCount() - startTime > 5000) {
                    LogToFile("[CR] FATAL: postComputeQuery timeout after CAS");
                    break;
                }
            }
        }
        
        // Unbind CAS
        ctx->context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
        ctx->context->CSSetShaderResources(0, 1, nullSRV);
        ctx->context->CSSetShader(nullptr, nullptr, 0);
        
        if (createdCasUav && casUav) casUav->Release();
    }
    
    // Blue Noise path reads from CAS output (or TAB output if CAS disabled)
    int outputIdx = casOutputIdx;
    if (ctx->useBlueNoiseDither && ctx->ditherShader)
    {
        int bnOutputIdx = 1 - casOutputIdx;
        
        // Create SRV from CAS output texture
        ID3D11ShaderResourceView* casResultSRV = nullptr;
        if (ctx->tabSharpeningEnabled)
        {
            hr = ctx->device->CreateShaderResourceView(ctx->d3dTextures[casOutputIdx], &srvDesc, &casResultSRV);
            if (FAILED(hr)) {
                SetError("Failed to create SRV for Blue Noise input");
                tabResultSRV->Release();
                if (createdTemporaryUav && uav) uav->Release();
                return -1;
            }
        }
        
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (SUCCEEDED(ctx->context->Map(ctx->constantBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped)))
        {
            struct DitherParams {
                uint32_t width;
                uint32_t height;
                uint32_t frameIdx;
                uint32_t flags;
            } params;
            
            params.width = ctx->width;
            params.height = ctx->height;
            params.frameIdx = ctx->frameCounter++;
            params.flags = 0;
            
            memcpy(mapped.pData, &params, sizeof(params));
            ctx->context->Unmap(ctx->constantBuffer, 0);
        }
        
        ctx->context->CSSetShader(ctx->ditherShader, nullptr, 0);
        ctx->context->CSSetConstantBuffers(0, 1, &ctx->constantBuffer);
        
        // Use CAS result SRV if sharpening enabled, otherwise use TAB result
        ID3D11ShaderResourceView* inputSrv = (ctx->tabSharpeningEnabled && casResultSRV) ? casResultSRV : tabResultSRV;
        ID3D11ShaderResourceView* srvs[2] = { inputSrv, ctx->blueNoiseSRV };
        ctx->context->CSSetShaderResources(0, 2, srvs);
        
        bnUav = ctx->encoderUAV[bnOutputIdx];
        bool createdBnUavLocal = false;
        if (!bnUav)
        {
            D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
            uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
            uavDesc.Texture2D.MipSlice = 0;
            
            HRESULT hr = ctx->device->CreateUnorderedAccessView(ctx->d3dTextures[bnOutputIdx], &uavDesc, &bnUav);
            if (FAILED(hr)) {
                SetError("Failed to create UAV for Blue Noise output");
                if (casResultSRV) casResultSRV->Release();
                tabResultSRV->Release();
                if (createdTemporaryUav && uav) uav->Release();
                return -1;
            }
            createdBnUavLocal = true;
        }
        ctx->context->CSSetUnorderedAccessViews(0, 1, &bnUav, nullptr);
        
        ctx->context->Dispatch(dispatchX, dispatchY, 1);
        
        ctx->context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
        ID3D11ShaderResourceView* nullSRVs[2] = { nullptr, nullptr };
        ctx->context->CSSetShaderResources(0, 2, nullSRVs);
        ctx->context->CSSetShader(nullptr, nullptr, 0);
        ctx->context->Flush();
        
        if (casResultSRV) casResultSRV->Release();
        tabResultSRV->Release();
        
        outputIdx = bnOutputIdx;
        if (createdBnUavLocal) {
            bnUav->Release();
            bnUav = nullptr;
        }
        
        // Unbind all Blue Noise resources
        ctx->context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
        ID3D11ShaderResourceView* nullSRVs2[2] = { nullptr, nullptr };
        ctx->context->CSSetShaderResources(0, 2, nullSRVs2);
        ctx->context->CSSetConstantBuffers(0, 1, nullCB);
        ctx->context->CSSetShader(nullptr, nullptr, 0);
        ctx->context->Flush();
    }
    else
    {
        // No Blue Noise - just release the TAB result SRV
        tabResultSRV->Release();
        
        // Unbind CAS resources if they were used
        ctx->context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
        ctx->context->CSSetShaderResources(0, 1, nullSRV);
        ctx->context->CSSetConstantBuffers(0, 1, nullCB);
        ctx->context->CSSetShader(nullptr, nullptr, 0);
        ctx->context->Flush();
    }
    
    if (createdTemporaryUav && uav) uav->Release();
    
    // Submit to AMF
    AMF_RESULT res;
    do {
        res = ctx->encoder->SubmitInput(ctx->amfSurfaces[outputIdx]);
        if (res == AMF_INPUT_FULL) {
            amf::AMFDataPtr data;
            if (ctx->encoder->QueryOutput(&data) == AMF_OK && data) {
                amf::AMFBufferPtr buffer(data);
                AVPacket pkt{};
                av_init_packet(&pkt);
                
                amf_int64 outputDataType;
                const wchar_t* outputTypeProp = ctx->hevcMode ? 
                    AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE : 
                    AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE;
                buffer->GetProperty(outputTypeProp, &outputDataType);
                
                if (ctx->hevcMode) {
                    if (outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_IDR ||
                        outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_I)
                        pkt.flags |= AV_PKT_FLAG_KEY;
                } else {
                    if (outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_IDR ||
                        outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_I)
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
        }
    } while (res == AMF_INPUT_FULL);

    if (res != AMF_OK)
    {
        SetError("AMF SubmitInput failed in TAB finalization");
        return -1;
    }

    // Drain encoded packets
    amf::AMFDataPtr data;
    while (ctx->encoder->QueryOutput(&data) == AMF_OK && data)
    {
        amf::AMFBufferPtr buffer(data);
        AVPacket pkt{};
        av_init_packet(&pkt);

        amf_int64 outputDataType;
        const wchar_t* outputTypeProp = ctx->hevcMode ? 
            AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE : 
            AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE;
            
        if (ctx->hevcMode)
            outputDataType = AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_P;
        else
            outputDataType = AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_P;
            
        buffer->GetProperty(outputTypeProp, &outputDataType);
        
        if (ctx->hevcMode)
        {
            if (outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_IDR ||
                outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_I)
            {
                pkt.flags |= AV_PKT_FLAG_KEY;
            }
        }
        else
        {
            if (outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_IDR ||
                outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_I)
            {
                pkt.flags |= AV_PKT_FLAG_KEY;
            }
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
    
    // CRITICAL: Toggle accumulation buffer for next frame (prevents resource hazards)
    ctx->currentAccumBuffer = 1 - ctx->currentAccumBuffer;

    return 0;
}

// CRITICAL: Copy from Unity texture (source) to our encoder texture (destination), then submit owned texture
// MODIFIED: This is the standard path when TAB is NOT enabled. When TAB is enabled, use CR_SubmitSubFrame + CR_FinalizeTemporalFrame instead.
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

    // Check if TAB mode is enabled - if so, CR_EncodeFrame should not be called directly
    // Instead, use CR_SubmitSubFrame followed by CR_FinalizeTemporalFrame
    if (ctx->isTabMode)
    {
        SetError("TAB mode is enabled. Use CR_SubmitSubFrame and CR_FinalizeTemporalFrame instead of CR_EncodeFrame");
        return -1;
    }

    // CRITICAL: Acquire mutex to protect Non-TAB encode operations
    std::lock_guard<std::mutex> lock(ctx->encodeMutex);

    // Validate format (keep for troubleshooting user reports)
    D3D11_TEXTURE2D_DESC srcDesc;
    unityTexture->GetDesc(&srcDesc);
    
    bool validFormat = false;
    switch (srcDesc.Format)
    {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS:
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
        case DXGI_FORMAT_B8G8R8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
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

    // Validate dimensions
    if (srcDesc.Width != (UINT)ctx->width || srcDesc.Height != (UINT)ctx->height)
    {
        LogToFile("[CR] ENCODE DIMENSION ERROR: Expected %dx%d but got %ux%u", 
                  ctx->width, ctx->height, srcDesc.Width, srcDesc.Height);
    }

    int idx = ctx->bufferIndex;
    ctx->bufferIndex = 1 - idx;

    // GPU copy Unity texture → encoder-owned texture
    if (ctx->useBlueNoiseDither) {
    // --- Path B: Blue Noise Compute Dither ---
    
    // 1. Update constant buffer (Resolution, FrameIndex, Flags)
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(ctx->context->Map(ctx->constantBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        struct DitherParams {
            uint32_t width;
            uint32_t height;
            uint32_t frameIdx;
            uint32_t flags;  // Bit 0 = BGRA swizzle
        } params;
        
        params.width = ctx->width;
        params.height = ctx->height;
        params.frameIdx = ctx->frameCounter++;
        // Detect BGRA from Unity texture descriptor
        params.flags = (srcDesc.Format == DXGI_FORMAT_B8G8R8A8_UNORM || 
                       srcDesc.Format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB) ? 1 : 0;
        
        memcpy(mapped.pData, &params, sizeof(params));
        ctx->context->Unmap(ctx->constantBuffer, 0);
    }
    
    // 2. Create temporary SRV for the incoming Unity texture
    ID3D11ShaderResourceView* inputSRV = nullptr;
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    // Match the format of the source, or use TYPELESS variant
    srvDesc.Format = (srcDesc.Format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB || 
                      srcDesc.Format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB) 
                      ? DXGI_FORMAT_R8G8B8A8_UNORM_SRGB 
                      : DXGI_FORMAT_R8G8B8A8_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = 1;
    
    HRESULT hr = ctx->device->CreateShaderResourceView(unityTexture, &srvDesc, &inputSRV);
    if (FAILED(hr)) {
        // Fallback to copy if SRV creation fails
        ctx->context->CopyResource(ctx->d3dTextures[idx], unityTexture);
    } else {
        // 3. Bind compute pipeline
        ctx->context->CSSetShader(ctx->ditherShader, nullptr, 0);
        ctx->context->CSSetConstantBuffers(0, 1, &ctx->constantBuffer);
        
        ID3D11ShaderResourceView* srvs[2] = { inputSRV, ctx->blueNoiseSRV };
        ctx->context->CSSetShaderResources(0, 2, srvs);
        
        ID3D11UnorderedAccessView* uavs[1] = { ctx->encoderUAV[idx] };
        ctx->context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
        
        // 4. Dispatch (16x16 threads per group)
        UINT dispatchX = (ctx->width + 15) / 16;
        UINT dispatchY = (ctx->height + 15) / 16;
        ctx->context->Dispatch(dispatchX, dispatchY, 1);
        
        // 5. CRITICAL: Unbind UAV so AMF can use texture as SRV/Input
        ID3D11UnorderedAccessView* nullUAV[1] = { nullptr };
        ctx->context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
        
        // Unbind SRVs and shader (clean state)
        ID3D11ShaderResourceView* nullSRVs[2] = { nullptr, nullptr };
        ctx->context->CSSetShaderResources(0, 2, nullSRVs);
        ctx->context->CSSetShader(nullptr, nullptr, 0);
        
        // 6. Ensure GPU completes before AMF submits
        ctx->context->Flush();
        
        inputSRV->Release();
    }
} else {
    // --- Path A: Standard CopyResource ---
    ctx->context->CopyResource(ctx->d3dTextures[idx], unityTexture);
}

// HARD SYNC: Ensure GPU completion before encoder reads
if (ctx->encodeSyncQuery) {
    ctx->context->End(ctx->encodeSyncQuery);  // Insert marker
    DWORD startTime = GetTickCount();
    while (S_FALSE == ctx->context->GetData(ctx->encodeSyncQuery, nullptr, 0, 0)) {
        Sleep(1);  // Yield CPU to allow driver processing
        // Add timeout detection (5 seconds - should never take this long)
        if (GetTickCount() - startTime > 5000) {
            LogToFile("[CR] FATAL: encodeSyncQuery timeout in CR_EncodeFrame - GPU sync broken");
            break;
        }
    }
} else {
    // Fallback: Flush if query not available
    ctx->context->Flush();
}

 // Submit to AMF encoder - handle INPUT_FULL by draining until accepted
    AMF_RESULT res;
    do {
        res = ctx->encoder->SubmitInput(ctx->amfSurfaces[idx]);
        if (res == AMF_INPUT_FULL) {
            // Queue full - must drain an output frame before retrying
            amf::AMFDataPtr data;
            if (ctx->encoder->QueryOutput(&data) == AMF_OK && data) {
                // Process frame immediately to free the slot
                amf::AMFBufferPtr buffer(data);
                AVPacket pkt{};
                av_init_packet(&pkt);
                
                amf_int64 outputDataType;
                const wchar_t* outputTypeProp = ctx->hevcMode ? 
                    AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE : 
                    AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE;
                buffer->GetProperty(outputTypeProp, &outputDataType);
                
                if (ctx->hevcMode) {
                    if (outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_IDR ||
                        outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_I)
                        pkt.flags |= AV_PKT_FLAG_KEY;
                } else {
                    if (outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_IDR ||
                        outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_I)
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
        }
    } while (res == AMF_INPUT_FULL);

    if (res != AMF_OK)
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
        amf_int64 outputDataType;
        const wchar_t* outputTypeProp = ctx->hevcMode ? 
            AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE : 
            AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE;
            
        if (ctx->hevcMode)
            outputDataType = AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_P;
        else
            outputDataType = AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_P;
            
        buffer->GetProperty(outputTypeProp, &outputDataType);
        
        // Check if keyframe
        if (ctx->hevcMode)
        {
            if (outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_IDR ||
                outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_I)
            {
                pkt.flags |= AV_PKT_FLAG_KEY;
            }
        }
        else
        {
            if (outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_IDR ||
                outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_I)
            {
                pkt.flags |= AV_PKT_FLAG_KEY;
            }
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

    // NEW: Clean up TAB resources first
    if (ctx->isTabMode)
    {
        DestroyTabResources(ctx);
    }

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
            amf_int64 outputDataType;
            const wchar_t* outputTypeProp = ctx->hevcMode ? 
                AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE : 
                AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE;
                
            if (ctx->hevcMode)
                outputDataType = AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_P;
            else
                outputDataType = AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_P;
                
            buffer->GetProperty(outputTypeProp, &outputDataType);
            
            // Check if keyframe
            if (ctx->hevcMode)
            {
                if (outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_IDR ||
                    outputDataType == AMF_VIDEO_ENCODER_HEVC_OUTPUT_DATA_TYPE_I)
                {
                    pkt.flags |= AV_PKT_FLAG_KEY;
                }
            }
            else
            {
                if (outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_IDR ||
                    outputDataType == AMF_VIDEO_ENCODER_OUTPUT_DATA_TYPE_I)
                {
                    pkt.flags |= AV_PKT_FLAG_KEY;
                }
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
    if (ctx->useBlueNoiseDither) {
    DestroyDitheringResources(ctx);
    }
    for (int i = 0; i < 2; i++)
    {
        if (ctx->encoderUAV[i]) { ctx->encoderUAV[i]->Release(); ctx->encoderUAV[i] = nullptr; }
        if (ctx->amfSurfaces[i]) ctx->amfSurfaces[i] = nullptr;
        if (ctx->d3dTextures[i]) { ctx->d3dTextures[i]->Release(); ctx->d3dTextures[i] = nullptr; }
    }
    
    // Clean up Non-TAB sync query
    if (ctx->encodeSyncQuery) { ctx->encodeSyncQuery->Release(); ctx->encodeSyncQuery = nullptr; }

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

// ============================================================================
// GTAO Debug Test - Full GTAO compute shader and PNG output
// ============================================================================

#define STB_IMAGE_WRITE_IMPLEMENTATION
#include "stb_image_write.h"
#include "GTAO.h"  // Compiled compute shader bytecode

static struct {
    ID3D11Texture2D* depthTexture = nullptr;
    ID3D11Texture2D* normalTexture = nullptr;
    int width = 0;
    int height = 0;
    float invProj[16] = {};       // Inverse projection matrix from Unity
    float worldToView[9] = {};     // World-to-view matrix (3x3 rotation)
    float nearPlane = 0.1f;        // Camera near plane
    float farPlane = 1000.0f;      // Camera far plane
    int frameIndex = 0;            // For temporal noise (0-7 cycle)
    ID3D11Texture2D* blueNoiseTexture = nullptr;  // Cached blue noise texture
    ID3D11ShaderResourceView* blueNoiseSRV = nullptr;  // Cached SRV
} g_GTAODebugTest;

// GTAO params constant buffer (matches GTAO.hlsl - XeGTAO style)
// Total: 80 bytes (5 float4s)
struct GTAOParams {
    // float4 #1 (offset 0)
    float ndcToViewMul[2];      // tanHalfFOV * float2(2, -2)
    float ndcToViewAdd[2];      // tanHalfFOV * float2(-1, 1)
    // float4 #2 (offset 16)
    float depthUnpackConsts[2]; // x = (far*near)/(far-near), y = -near/(far-near)
    float resolution[2];        // Width, Height
    // float4 #3 (offset 32)
    float invResolution[2];     // 1/Width, 1/Height
    float effectRadius;         // World-space sampling radius
    float falloffRange;         // Default 0.615
    // float4 #4 (offset 48) - 16 bytes exactly
    float intensity;            // AO intensity multiplier
    float sampleDistributionPower; // Default 2.0
    int sliceCount;             // Number of slices (4-8)
    int stepsPerSlice;          // Steps per direction (8-16)
    // float4 #5 (offset 64)
    int FrameIndex;             // 0-7 temporal frame index
    float depthMipSamplingOffset; // Hi-Z mip offset (typically 1.0-2.0)
    float __pad1;
    float __pad2;
    // float4 #6, #7, #8 - World-to-View matrix as three float4s
    float worldToViewRow0[4];   // offset 80: [row0.x, row0.y, row0.z, 0]
    float worldToViewRow1[4];   // offset 96: [row1.x, row1.y, row1.z, 0]
    float worldToViewRow2[4];   // offset 112: [row2.x, row2.y, row2.z, 0]
    // Total: 128 bytes exactly (8 float4s)
};

// Initialize blue noise texture (one-time, immutable)
static void InitializeBlueNoiseResources(ID3D11Device* device)
{
    if (g_GTAODebugTest.blueNoiseTexture) return;  // Already initialized
    
    D3D11_TEXTURE2D_DESC desc = {};
    desc.Width = 256;
    desc.Height = 256;
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_R8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_IMMUTABLE;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    
    D3D11_SUBRESOURCE_DATA initData = {};
    initData.pSysMem = g_BlueNoise256x256R8;
    initData.SysMemPitch = 256;  // Bytes per row
    
    HRESULT hr = device->CreateTexture2D(&desc, &initData, &g_GTAODebugTest.blueNoiseTexture);
    if (SUCCEEDED(hr) && g_GTAODebugTest.blueNoiseTexture) {
        device->CreateShaderResourceView(g_GTAODebugTest.blueNoiseTexture, nullptr, 
                                         &g_GTAODebugTest.blueNoiseSRV);
    }
}

extern "C" __declspec(dllexport)
void CR_GTAODebugSetInput(ID3D11Texture2D* depthTex, ID3D11Texture2D* normalTex, int width, int height,
                          const float* invProj, const float* worldToView, float nearPlane, float farPlane,
                          int frameIndex)
{
    g_GTAODebugTest.depthTexture = depthTex;
    g_GTAODebugTest.normalTexture = normalTex;
    g_GTAODebugTest.width = width;
    g_GTAODebugTest.height = height;
    g_GTAODebugTest.nearPlane = nearPlane;
    g_GTAODebugTest.farPlane = farPlane;
    g_GTAODebugTest.frameIndex = frameIndex;
    
    if (invProj) {
        memcpy(g_GTAODebugTest.invProj, invProj, sizeof(float) * 16);
    } else {
        // Identity fallback
        memset(g_GTAODebugTest.invProj, 0, sizeof(g_GTAODebugTest.invProj));
        g_GTAODebugTest.invProj[0] = 1.0f;
        g_GTAODebugTest.invProj[5] = 1.0f;
        g_GTAODebugTest.invProj[10] = 1.0f;
        g_GTAODebugTest.invProj[15] = 1.0f;
    }
    
    if (worldToView) {
        memcpy(g_GTAODebugTest.worldToView, worldToView, sizeof(float) * 9);
    } else {
        memset(g_GTAODebugTest.worldToView, 0, sizeof(g_GTAODebugTest.worldToView));
        g_GTAODebugTest.worldToView[0] = 1.0f;
        g_GTAODebugTest.worldToView[4] = 1.0f;
        g_GTAODebugTest.worldToView[8] = 1.0f;
    }
    
    // Initialize blue noise resources (one-time)
    if (depthTex && !g_GTAODebugTest.blueNoiseTexture) {
        ID3D11Device* device = nullptr;
        depthTex->GetDevice(&device);
        if (device) {
            InitializeBlueNoiseResources(device);
            device->Release();
        }
    }
}

// Helper to save R32_FLOAT texture as grayscale PNG
static bool SaveR32FloatTextureAsPNG(ID3D11Texture2D* texture, const char* filename, float scale = 1.0f)
{
    if (!texture) return false;
    
    ID3D11Device* device = nullptr;
    texture->GetDevice(&device);
    if (!device) return false;
    
    ID3D11DeviceContext* context = nullptr;
    device->GetImmediateContext(&context);
    if (!context) { device->Release(); return false; }
    
    D3D11_TEXTURE2D_DESC srcDesc;
    texture->GetDesc(&srcDesc);
    
    D3D11_TEXTURE2D_DESC stagingDesc = srcDesc;
    stagingDesc.Usage = D3D11_USAGE_STAGING;
    stagingDesc.BindFlags = 0;
    stagingDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    stagingDesc.MiscFlags = 0;
    
    ID3D11Texture2D* stagingTex = nullptr;
    HRESULT hr = device->CreateTexture2D(&stagingDesc, nullptr, &stagingTex);
    if (FAILED(hr)) { context->Release(); device->Release(); return false; }
    
    context->CopyResource(stagingTex, texture);
    context->Flush();
    
    D3D11_MAPPED_SUBRESOURCE mapped;
    hr = context->Map(stagingTex, 0, D3D11_MAP_READ, 0, &mapped);
    if (FAILED(hr)) { stagingTex->Release(); context->Release(); device->Release(); return false; }
    
    int width = srcDesc.Width;
    int height = srcDesc.Height;
    std::vector<uint8_t> pixels(width * height);
    
    for (int y = 0; y < height; y++)
    {
        float* srcRow = (float*)((uint8_t*)mapped.pData + y * mapped.RowPitch);
        for (int x = 0; x < width; x++)
        {
            float val = srcRow[x] * scale;
            if (val < 0.0f) val = 0.0f;
            if (val > 1.0f) val = 1.0f;
            pixels[y * width + x] = (uint8_t)(val * 255.0f);
        }
    }
    
    context->Unmap(stagingTex, 0);
    stagingTex->Release();
    context->Release();
    device->Release();
    
    return stbi_write_png(filename, width, height, 1, pixels.data(), width) != 0;
}

extern "C" __declspec(dllexport)
int CR_GTAODebugExecute(const char* outputDirectory)
{
    char path[MAX_PATH];
    bool success = true;
    
    // Validate input
    if (!g_GTAODebugTest.depthTexture) {
        LogToFile("[GTAO] Error: depth texture is null");
        return -1;
    }
    
    // Get device from depth texture
    ID3D11Device* device = nullptr;
    g_GTAODebugTest.depthTexture->GetDevice(&device);
    if (!device) {
        LogToFile("[GTAO] Failed to get device from depth texture");
        return -1;
    }
    
    ID3D11DeviceContext* context = nullptr;
    device->GetImmediateContext(&context);
    if (!context) {
        device->Release();
        LogToFile("[GTAO] Failed to get context");
        return -1;
    }
    
    int width = g_GTAODebugTest.width;
    int height = g_GTAODebugTest.height;
    
    // Create AO output texture (R32_FLOAT)
    D3D11_TEXTURE2D_DESC aoDesc = {};
    aoDesc.Width = width;
    aoDesc.Height = height;
    aoDesc.MipLevels = 1;
    aoDesc.ArraySize = 1;
    aoDesc.Format = DXGI_FORMAT_R32_FLOAT;
    aoDesc.SampleDesc.Count = 1;
    aoDesc.Usage = D3D11_USAGE_DEFAULT;
    aoDesc.BindFlags = D3D11_BIND_UNORDERED_ACCESS | D3D11_BIND_SHADER_RESOURCE;
    
    ID3D11Texture2D* aoTexture = nullptr;
    HRESULT hr = device->CreateTexture2D(&aoDesc, nullptr, &aoTexture);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create AO texture");
        context->Release();
        device->Release();
        return -1;
    }
    
    // Create UAV for AO output
    ID3D11UnorderedAccessView* aoUAV = nullptr;
    D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
    uavDesc.Format = DXGI_FORMAT_R32_FLOAT;
    uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
    hr = device->CreateUnorderedAccessView(aoTexture, &uavDesc, &aoUAV);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create AO UAV");
        aoTexture->Release();
        context->Release();
        device->Release();
        return -1;
    }
    
    // Create Hi-Z texture with mip chain
    int hiZMipCount = (int)(log2(max(width, height))) + 1;
    hiZMipCount = min(hiZMipCount, 12);  // Cap at 12 mips (4096->1)
    
    D3D11_TEXTURE2D_DESC hiZDesc = {};
    hiZDesc.Width = width;
    hiZDesc.Height = height;
    hiZDesc.MipLevels = hiZMipCount;
    hiZDesc.ArraySize = 1;
    hiZDesc.Format = DXGI_FORMAT_R32_FLOAT;
    hiZDesc.SampleDesc.Count = 1;
    hiZDesc.Usage = D3D11_USAGE_DEFAULT;
    hiZDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
    
    ID3D11Texture2D* hiZTexture = nullptr;
    hr = device->CreateTexture2D(&hiZDesc, nullptr, &hiZTexture);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create Hi-Z texture");
        aoUAV->Release();
        aoTexture->Release();
        context->Release();
        device->Release();
        return -1;
    }
    
    // Create Hi-Z compute shader
    ID3D11ComputeShader* hiZShader = nullptr;
    hr = device->CreateComputeShader(g_HiZCS, sizeof(g_HiZCS), nullptr, &hiZShader);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create Hi-Z compute shader");
        hiZTexture->Release();
        aoUAV->Release();
        aoTexture->Release();
        context->Release();
        device->Release();
        return -1;
    }
    
    // Create Hi-Z constant buffer
    struct HiZParams {
        int sourceDim[2];
        int isFirstIteration;
        int __pad;
    };
    
    D3D11_BUFFER_DESC hiZCBDesc = {};
    hiZCBDesc.ByteWidth = sizeof(HiZParams);
    hiZCBDesc.Usage = D3D11_USAGE_DYNAMIC;
    hiZCBDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    hiZCBDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    
    ID3D11Buffer* hiZConstantBuffer = nullptr;
    hr = device->CreateBuffer(&hiZCBDesc, nullptr, &hiZConstantBuffer);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create Hi-Z constant buffer");
        hiZShader->Release();
        hiZTexture->Release();
        aoUAV->Release();
        aoTexture->Release();
        context->Release();
        device->Release();
        return -1;
    }
    
    // Get raw depth texture format for first Hi-Z iteration
    D3D11_TEXTURE2D_DESC rawDepthDesc;
    g_GTAODebugTest.depthTexture->GetDesc(&rawDepthDesc);
    
    // Copy raw depth to mip 0 of Hi-Z texture
    context->CopySubresourceRegion(
        hiZTexture, 0, 0, 0, 0,  // Dest: hiZTexture, mip 0, offset (0,0,0)
        g_GTAODebugTest.depthTexture, 0, nullptr // Source: raw depth, mip 0, full rect
    );
    
    // Generate remaining Hi-Z mips (1 to hiZMipCount-1)
    int currentWidth = width;
    int currentHeight = height;
    
    for (int mipLevel = 0; mipLevel < hiZMipCount - 1; mipLevel++) {
        // Create UAV for output mip
        D3D11_UNORDERED_ACCESS_VIEW_DESC hiZUavDesc = {};
        hiZUavDesc.Format = DXGI_FORMAT_R32_FLOAT;
        hiZUavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
        hiZUavDesc.Texture2D.MipSlice = mipLevel + 1;  // Write to next mip
        
        ID3D11UnorderedAccessView* hiZOutputUAV = nullptr;
        hr = device->CreateUnorderedAccessView(hiZTexture, &hiZUavDesc, &hiZOutputUAV);
        if (FAILED(hr)) {
            LogToFile("[GTAO] Failed to create Hi-Z UAV for mip %d", mipLevel + 1);
            continue;
        }
        
        // Fill constant buffer
        D3D11_MAPPED_SUBRESOURCE hiZMapped;
        if (SUCCEEDED(context->Map(hiZConstantBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &hiZMapped))) {
            HiZParams* params = (HiZParams*)hiZMapped.pData;
            params->sourceDim[0] = currentWidth;
            params->sourceDim[1] = currentHeight;
            params->isFirstIteration = (mipLevel == 0) ? 1 : 0;
            params->__pad = 0;
            context->Unmap(hiZConstantBuffer, 0);
        }
        
        // Bind source (SRV) - use raw depth for first iteration, HiZ for subsequent
        ID3D11ShaderResourceView* hiZSourceSRV = nullptr;
        if (mipLevel == 0) {
            // First iteration: read from raw depth texture
            D3D11_SHADER_RESOURCE_VIEW_DESC srcSrvDesc = {};
            srcSrvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            srcSrvDesc.Texture2D.MipLevels = 1;
            srcSrvDesc.Format = (rawDepthDesc.Format == 39) ? DXGI_FORMAT_R32_FLOAT : rawDepthDesc.Format;
            device->CreateShaderResourceView(g_GTAODebugTest.depthTexture, &srcSrvDesc, &hiZSourceSRV);
        } else {
            // Subsequent iterations: read from previous HiZ mip
            D3D11_SHADER_RESOURCE_VIEW_DESC srcSrvDesc = {};
            srcSrvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
            srcSrvDesc.Texture2D.MostDetailedMip = mipLevel;
            srcSrvDesc.Texture2D.MipLevels = 1;
            srcSrvDesc.Format = DXGI_FORMAT_R32_FLOAT;
            device->CreateShaderResourceView(hiZTexture, &srcSrvDesc, &hiZSourceSRV);
        }
        
        // Dispatch
        context->CSSetShader(hiZShader, nullptr, 0);
        context->CSSetConstantBuffers(0, 1, &hiZConstantBuffer);
        ID3D11ShaderResourceView* srvs[1] = { hiZSourceSRV };
        context->CSSetShaderResources(0, 1, srvs);
        ID3D11UnorderedAccessView* uavs[1] = { hiZOutputUAV };
        context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
        
        UINT dispatchX = (currentWidth / 2 + 7) / 8;
        UINT dispatchY = (currentHeight / 2 + 7) / 8;
        context->Dispatch(dispatchX, dispatchY, 1);
        
        // Cleanup per-mip resources
        hiZOutputUAV->Release();
        hiZSourceSRV->Release();
        
        // Update dimensions for next iteration
        currentWidth = max(1, currentWidth / 2);
        currentHeight = max(1, currentHeight / 2);
    }
    
    // Unbind Hi-Z shader
    ID3D11UnorderedAccessView* nullHiZUAV[1] = { nullptr };
    ID3D11ShaderResourceView* nullHiZSRV[1] = { nullptr };
    context->CSSetUnorderedAccessViews(0, 1, nullHiZUAV, nullptr);
    context->CSSetShaderResources(0, 1, nullHiZSRV);
    context->CSSetShader(nullptr, nullptr, 0);
    
    // Create SRVs for input textures (GTAO will sample from Hi-Z)
    ID3D11ShaderResourceView* depthSRV = nullptr;
    ID3D11ShaderResourceView* normalSRV = nullptr;
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = -1;  // Expose all mip levels for Hi-Z sampling
    
    // Use Hi-Z texture for GTAO sampling (has mip chain)
    srvDesc.Format = DXGI_FORMAT_R32_FLOAT;
    device->CreateShaderResourceView(hiZTexture, &srvDesc, &depthSRV);
    
    // Normals are ARGB2101010 from Unity
    srvDesc.Format = DXGI_FORMAT_R10G10B10A2_UNORM;
    device->CreateShaderResourceView(g_GTAODebugTest.normalTexture, &srvDesc, &normalSRV);
    
    // Create point sampler for Hi-Z sampling (register s0)
    D3D11_SAMPLER_DESC sampDesc = {};
    sampDesc.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT;  // Point sampling for Hi-Z
    sampDesc.AddressU = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.AddressV = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
    sampDesc.ComparisonFunc = D3D11_COMPARISON_NEVER;
    sampDesc.MinLOD = 0;
    sampDesc.MaxLOD = D3D11_FLOAT32_MAX;
    
    ID3D11SamplerState* pointSampler = nullptr;
    hr = device->CreateSamplerState(&sampDesc, &pointSampler);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create point sampler");
        if (depthSRV) depthSRV->Release();
        if (normalSRV) normalSRV->Release();
        context->Release();
        device->Release();
        return -1;
    }
    
    // Create compute shader
    ID3D11ComputeShader* aoShader = nullptr;
    hr = device->CreateComputeShader(g_GTAOCS, sizeof(g_GTAOCS), nullptr, &aoShader);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create compute shader");
        aoUAV->Release();
        aoTexture->Release();
        if (depthSRV) depthSRV->Release();
        if (normalSRV) normalSRV->Release();
        context->Release();
        device->Release();
        return -1;
    }
    
    // Create constant buffer
    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = sizeof(GTAOParams);
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    
    ID3D11Buffer* constantBuffer = nullptr;
    hr = device->CreateBuffer(&cbDesc, nullptr, &constantBuffer);
    if (FAILED(hr)) {
        LogToFile("[GTAO] Failed to create constant buffer");
        aoShader->Release();
        aoUAV->Release();
        aoTexture->Release();
        if (depthSRV) depthSRV->Release();
        if (normalSRV) normalSRV->Release();
        context->Release();
        device->Release();
        return -1;
    }
    
    // Fill constant buffer
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(context->Map(constantBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        GTAOParams* params = (GTAOParams*)mapped.pData;
        
        // Compute XeGTAO constants from inverse projection matrix
        // invProj is the inverse projection, so invProj[0,0] = tanHalfFOVX, invProj[1,1] = tanHalfFOVY
        float tanHalfFOVX = g_GTAODebugTest.invProj[0]; // _m00 in column-major
        float tanHalfFOVY = g_GTAODebugTest.invProj[5]; // _m11 in column-major
        
        // View reconstruction constants - standard Unity conventions
        params->ndcToViewMul[0] = tanHalfFOVX * 2.0f;    // Positive X
        params->ndcToViewAdd[0] = tanHalfFOVX * -1.0f;
        params->ndcToViewMul[1] = tanHalfFOVY * -2.0f;   // Negative Y (Unity Y-down)
        params->ndcToViewAdd[1] = tanHalfFOVY * 1.0f;
        
        // Stable reversed-Z linearization: pass raw near/far, let shader do the math
        float n = g_GTAODebugTest.nearPlane;
        float f = g_GTAODebugTest.farPlane;
        params->depthUnpackConsts[0] = n;  // Near plane (e.g., 0.21)
        params->depthUnpackConsts[1] = f;  // Far plane (e.g., 750000.0)
        
        params->resolution[0] = (float)width;
        params->resolution[1] = (float)height;
        params->invResolution[0] = 1.0f / width;
        params->invResolution[1] = 1.0f / height;
        params->effectRadius = 2.0f;              // World-space radius
        params->falloffRange = 0.615f;            // XeGTAO default
        params->intensity = 0.8f;                 // AO intensity (REFERENCE default)
        params->sliceCount = 2;                   // 2 slices (REFERENCE Medium preset)
        params->stepsPerSlice = 4;                // 4 steps per direction (REFERENCE Medium preset)
        params->sampleDistributionPower = 2.0f;   // Quadratic distribution
        params->depthMipSamplingOffset = 100.0f;   // TEMP: Force mip 0 sampling to test Hi-Z issues
        
        // World-to-view matrix (3x3 rotation) - as three float4s for clean alignment
        params->FrameIndex = g_GTAODebugTest.frameIndex;
        // Row0 = [row0.x, row0.y, row0.z, 0]
        params->worldToViewRow0[0] = g_GTAODebugTest.worldToView[0];
        params->worldToViewRow0[1] = g_GTAODebugTest.worldToView[1];
        params->worldToViewRow0[2] = g_GTAODebugTest.worldToView[2];
        params->worldToViewRow0[3] = 0.0f;
        // Row1 = [row1.x, row1.y, row1.z, 0]
        params->worldToViewRow1[0] = g_GTAODebugTest.worldToView[3];
        params->worldToViewRow1[1] = g_GTAODebugTest.worldToView[4];
        params->worldToViewRow1[2] = g_GTAODebugTest.worldToView[5];
        params->worldToViewRow1[3] = 0.0f;
        // Row2 = [row2.x, row2.y, row2.z, 0] - passed directly from C#
        params->worldToViewRow2[0] = g_GTAODebugTest.worldToView[6];
        params->worldToViewRow2[1] = g_GTAODebugTest.worldToView[7];
        params->worldToViewRow2[2] = g_GTAODebugTest.worldToView[8];
        params->worldToViewRow2[3] = 0.0f;
        
        context->Unmap(constantBuffer, 0);
    }
    
    // Bind and dispatch
    context->CSSetShader(aoShader, nullptr, 0);
    context->CSSetConstantBuffers(0, 1, &constantBuffer);
    ID3D11ShaderResourceView* srvs[3] = { depthSRV, normalSRV, g_GTAODebugTest.blueNoiseSRV };
    context->CSSetShaderResources(0, 3, srvs);
    context->CSSetSamplers(0, 1, &pointSampler);  // Bind point sampler for Hi-Z
    ID3D11UnorderedAccessView* uavs[1] = { aoUAV };
    context->CSSetUnorderedAccessViews(0, 1, uavs, nullptr);
    
    // 8x8 thread groups, so divide by 8 (not 16)
    UINT dispatchX = (width + 7) / 8;
    UINT dispatchY = (height + 7) / 8;
    context->Dispatch(dispatchX, dispatchY, 1);
    context->Flush();
    
    // Unbind
    ID3D11UnorderedAccessView* nullUAV[1] = { nullptr };
    ID3D11ShaderResourceView* nullSRV[3] = { nullptr, nullptr, nullptr };
    ID3D11SamplerState* nullSampler[1] = { nullptr };
    context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    context->CSSetShaderResources(0, 3, nullSRV);
    context->CSSetSamplers(0, 1, nullSampler);
    context->CSSetShader(nullptr, nullptr, 0);
    
    // Save AO output
    snprintf(path, MAX_PATH, "%s\\ao_output.png", outputDirectory ? outputDirectory : ".");
    if (!SaveR32FloatTextureAsPNG(aoTexture, path, 1.0f))
    {
        LogToFile("[GTAO] Failed to save AO output");
        success = false;
    }
    
    // Cleanup
    constantBuffer->Release();
    aoShader->Release();
    aoUAV->Release();
    if (depthSRV) depthSRV->Release();
    if (normalSRV) normalSRV->Release();
    if (pointSampler) pointSampler->Release();
    hiZShader->Release();
    hiZConstantBuffer->Release();
    hiZTexture->Release();
    aoTexture->Release();
    context->Release();
    device->Release();
    
    return success ? 0 : -1;
}