#include "CinematicRecorderNative.h"
#include "EmbeddedResources.h"
#include "TemporalAccumulation.h"  // NEW: Include the generated header for compute shader bytecode

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
    ID3D11Texture2D* accumulationArray = nullptr;           // ArraySize=8, R16G16B16A16_FLOAT
    ID3D11ShaderResourceView* accumulationSRV = nullptr;    // SRV for the array
    ID3D11ComputeShader* tabComputeShader = nullptr;        // TAB compute shader
    ID3D11Buffer* tabWeightBuffer = nullptr;                // Constant buffer for Gaussian weights
    bool isTabMode = false;                                 // TAB enabled flag
    int tabSubFrameCount = 8;                               // Number of sub-frames (typically 8)
    int currentSubFrame = 0;                                // Current sub-frame index being filled
    float tabWeights[8] = {0};                              // Gaussian weights
    float tabTotalWeight = 0;                               // Sum of weights for normalization
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
    
    // Normalize weights (divide by sum)
    for (int i = 0; i < ctx->tabSubFrameCount; i++)
    {
        ctx->tabWeights[i] /= ctx->tabTotalWeight;
    }
    
    // Create accumulation array texture (ArraySize=8, R16G16B16A16_FLOAT)
    D3D11_TEXTURE2D_DESC arrayDesc = {};
    arrayDesc.Width = ctx->width;
    arrayDesc.Height = ctx->height;
    arrayDesc.MipLevels = 1;
    arrayDesc.ArraySize = ctx->tabSubFrameCount;  // 8 slices
    arrayDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    arrayDesc.SampleDesc.Count = 1;
    arrayDesc.Usage = D3D11_USAGE_DEFAULT;
    arrayDesc.BindFlags = D3D11_BIND_SHADER_RESOURCE;  // Will be read by compute shader
    
    hr = ctx->device->CreateTexture2D(&arrayDesc, nullptr, &ctx->accumulationArray);
    if (FAILED(hr))
    {
        SetError("Failed to create accumulation array texture");
        return false;
    }
    
    // Create SRV for the array
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2DARRAY;
    srvDesc.Texture2DArray.MipLevels = 1;
    srvDesc.Texture2DArray.ArraySize = ctx->tabSubFrameCount;
    
    hr = ctx->device->CreateShaderResourceView(ctx->accumulationArray, &srvDesc, &ctx->accumulationSRV);
    if (FAILED(hr))
    {
        SetError("Failed to create accumulation array SRV");
        return false;
    }
    
    // Create compute shader from embedded bytecode
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
    
    // Create constant buffer for weights (16 bytes per float4, 2 float4s + 1 float + padding)
    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = 48;  // 2*float4 (32) + float TotalWeight (4) + padding (12) = 48 bytes
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    
    // Prepare initial data (weights packed into float4s)
    struct WeightData {
        float weights[8];  // 2 float4s
        float totalWeight;
        float padding[3];  // Align to 16 bytes
    } weightData;
    
    memcpy(weightData.weights, ctx->tabWeights, sizeof(float) * 8);
    weightData.totalWeight = 1.0f;  // Already normalized, but shader expects this
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
    
    LogToFile("[CinematicRecorder] Temporal Accumulation Blur enabled");
    return true;
}

// NEW: Helper function to destroy TAB resources
static void DestroyTabResources(EncoderContext* ctx)
{
    if (ctx->tabWeightBuffer) { ctx->tabWeightBuffer->Release(); ctx->tabWeightBuffer = nullptr; }
    if (ctx->tabComputeShader) { ctx->tabComputeShader->Release(); ctx->tabComputeShader = nullptr; }
    if (ctx->accumulationSRV) { ctx->accumulationSRV->Release(); ctx->accumulationSRV = nullptr; }
    if (ctx->accumulationArray) { ctx->accumulationArray->Release(); ctx->accumulationArray = nullptr; }
    ctx->isTabMode = false;
    ctx->currentSubFrame = 0;
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

    LogToFile("[CR] Copying sub-frame %d to accumulation array", subFrameIndex);

    // DEBUG: Check source texture format
    D3D11_TEXTURE2D_DESC srcDesc;
    unityTexture->GetDesc(&srcDesc);
    LogToFile("[CR] SubmitSubFrame %d: Source format = %d (88=R8G8B8A8_UNORM, 91=B8G8R8A8_UNORM)", 
        subFrameIndex, srcDesc.Format);
    
    // Copy from Unity texture to specific array slice
    ctx->context->CopySubresourceRegion(
        ctx->accumulationArray,
        subFrameIndex,
        0, 0, 0,
        unityTexture,
        0,
        nullptr
    );
    
    LogToFile("[CR] CopySubresourceRegion executed for sub-frame %d", subFrameIndex);
    
    // Ensure copy completes immediately for debug verification
    ctx->context->Flush();
    
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
    
    int idx = ctx->bufferIndex;
    ctx->bufferIndex = 1 - idx;
    
    // 1. Ensure all sub-frame copies have completed
    ctx->context->Flush();
    
    // 2. Bind compute shader and resources
    ctx->context->CSSetShader(ctx->tabComputeShader, nullptr, 0);
    
    // Bind constant buffer (b0)
    ctx->context->CSSetConstantBuffers(0, 1, &ctx->tabWeightBuffer);
    
    // Bind SRV (t0) - the accumulation array
    ctx->context->CSSetShaderResources(0, 1, &ctx->accumulationSRV);
    
    // Bind UAV (u0) - the encoder output texture
    ID3D11UnorderedAccessView* uav = ctx->encoderUAV[idx];
    bool createdTemporaryUav = false;

    ID3D11UnorderedAccessView* bnUav = nullptr;      // Blue Noise output UAV (separate from TAB)
    bool createdBnUav = false;                       // Track if we allocated bnUav temporarily
    
    if (!uav)
    {
        // Create temporary UAV (Blue Noise resources missing or disabled)
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
    
    // 3. Dispatch compute shader (16x16 threads per group)
    UINT dispatchX = (ctx->width + 15) / 16;
    UINT dispatchY = (ctx->height + 15) / 16;

#ifndef NDEBUG
    // DEBUG: Verify resources are bound before dispatch
    LogToFile("[CR] Finalize: About to dispatch compute shader");
    
    // DEBUG: Check if accumulation array slice 0 has valid data
    ID3D11Texture2D* debugAccum = nullptr;
    D3D11_TEXTURE2D_DESC accumSliceDesc = {};
    ctx->accumulationArray->GetDesc(&accumSliceDesc);
    // Create a standard 2D texture (not array) to copy slice 0 into
    accumSliceDesc.ArraySize = 1;
    accumSliceDesc.Usage = D3D11_USAGE_STAGING;
    accumSliceDesc.BindFlags = 0;
    accumSliceDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    accumSliceDesc.MiscFlags = 0;
    
    if (SUCCEEDED(ctx->device->CreateTexture2D(&accumSliceDesc, nullptr, &debugAccum))) {
        // Copy slice 0 (first sub-frame) to our debug texture
        ctx->context->CopySubresourceRegion(debugAccum, 0, 0, 0, 0, ctx->accumulationArray, 0, nullptr);
        
        D3D11_MAPPED_SUBRESOURCE mappedAccum;
        if (SUCCEEDED(ctx->context->Map(debugAccum, 0, D3D11_MAP_READ, 0, &mappedAccum))) {
            // R8G8B8A8_UNORM format in accumulation array
            uint8_t* pixel = (uint8_t*)mappedAccum.pData;
            bool hasData = (pixel[0] != 0 || pixel[1] != 0 || pixel[2] != 0);
            LogToFile("[CR] Accumulation slice 0 check: %s (first pixel R=%u G=%u B=%u)", 
                hasData ? "HAS DATA" : "BLACK/ZERO", pixel[0], pixel[1], pixel[2]);
            ctx->context->Unmap(debugAccum, 0);
        }
        debugAccum->Release();
    }
    
    LogToFile("[CR]   - Compute shader: %s", ctx->tabComputeShader ? "OK (not null)" : "NULL");
    LogToFile("[CR]   - Accumulation SRV: %s", ctx->accumulationSRV ? "OK (not null)" : "NULL");
    LogToFile("[CR]   - Weight buffer: %s", ctx->tabWeightBuffer ? "OK (not null)" : "NULL");
    LogToFile("[CR]   - Output UAV: %s", uav ? "OK (not null)" : "NULL");
    LogToFile("[CR]   - Dispatch size: %dx%d (texture size: %dx%d)", dispatchX, dispatchY, ctx->width, ctx->height);
#endif

    ctx->context->Dispatch(dispatchX, dispatchY, 1);
    
#ifndef NDEBUG
    LogToFile("[CR] Finalize: Compute shader dispatch completed");

    // DEBUG: Read back pixel statistics to verify output
    ID3D11Texture2D* debugTexture = nullptr;
    D3D11_TEXTURE2D_DESC debugDesc = {};
    ctx->d3dTextures[idx]->GetDesc(&debugDesc);
    debugDesc.Usage = D3D11_USAGE_STAGING;
    debugDesc.BindFlags = 0;
    debugDesc.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    debugDesc.MiscFlags = 0;
    
    if (SUCCEEDED(ctx->device->CreateTexture2D(&debugDesc, nullptr, &debugTexture))) {
        ctx->context->CopyResource(debugTexture, ctx->d3dTextures[idx]);
        
        D3D11_MAPPED_SUBRESOURCE mapped;
        if (SUCCEEDED(ctx->context->Map(debugTexture, 0, D3D11_MAP_READ, 0, &mapped))) {
            
            uint8_t* data = (uint8_t*)mapped.pData;
            int width = ctx->width;
            int height = ctx->height;
            int pitch = mapped.RowPitch;
            
            // Sample grid: 5x5 points across the image
            uint32_t r_sum = 0, g_sum = 0, b_sum = 0;
            uint32_t r_min = 255, g_min = 255, b_min = 255;
            uint32_t r_max = 0, g_max = 0, b_max = 0;
            int samples = 0;
            
            for (int y = 0; y < height; y += height / 4) {
                for (int x = 0; x < width; x += width / 4) {
                    uint8_t* pixel = data + (y * pitch) + (x * 4);
                    uint8_t r = pixel[0];
                    uint8_t g = pixel[1];
                    uint8_t b = pixel[2];
                    
                    r_sum += r; g_sum += g; b_sum += b;
                    if (r < r_min) r_min = r;
                    if (g < g_min) g_min = g;
                    if (b < b_min) b_min = b;
                    if (r > r_max) r_max = r;
                    if (g > g_max) g_max = g;
                    if (b > b_max) b_max = b;
                    samples++;
                }
            }
            
            LogToFile("[CR] Pixel stats after TAB (25 samples):");
            LogToFile("[CR]   R: min=%u max=%u avg=%u", r_min, r_max, r_sum / samples);
            LogToFile("[CR]   G: min=%u max=%u avg=%u", g_min, g_max, g_sum / samples);
            LogToFile("[CR]   B: min=%u max=%u avg=%u", b_min, b_max, b_sum / samples);
            
            if (r_max == 0 && g_max == 0 && b_max == 0) {
                LogToFile("[CR] WARNING: All pixels are BLACK - shader outputting zeros");
            } else if (r_max < 10 && g_max < 10 && b_max < 10) {
                LogToFile("[CR] WARNING: Very dark output - possible black with noise");
            } else {
                LogToFile("[CR] OK: Valid scene colors detected in output");
            }
            
            ctx->context->Unmap(debugTexture, 0);
        } else {
            LogToFile("[CR] Failed to map debug texture for readback");
        }
        debugTexture->Release();
    } else {
        LogToFile("[CR] Failed to create debug staging texture");
    }
#endif

// GPU SYNC: Ensure TAB compute shader has finished writing before we proceed
    ID3D11Query* syncQuery = nullptr;
    D3D11_QUERY_DESC queryDesc = {};
    queryDesc.Query = D3D11_QUERY_EVENT;
    if (SUCCEEDED(ctx->device->CreateQuery(&queryDesc, &syncQuery))) {
        ctx->context->End(syncQuery);
        // Wait for GPU to finish all prior commands
        while (ctx->context->GetData(syncQuery, nullptr, 0, 0) == S_FALSE) {
            // Spin-wait for GPU completion (typically 0-1 iterations)
        }
        syncQuery->Release();
    }

    // 4. Unbind UAV (CRITICAL for encoder access)
    ID3D11UnorderedAccessView* nullUAV[1] = { nullptr };
    ctx->context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    
    // Unbind SRVs and shader (clean state)
    ID3D11ShaderResourceView* nullSRV[1] = { nullptr };
    ctx->context->CSSetShaderResources(0, 1, nullSRV);
    ctx->context->CSSetShader(nullptr, nullptr, 0);
    
    // 5. Ensure compute shader has finished writing to the texture
    ctx->context->Flush();
    
    // PING-PONG BUFFER FIX:
    // TAB writes to buffer[idx]. If Blue Noise follows, it must write to buffer[1-idx]
    // to avoid D3D11 resource hazard (binding same texture as SRV and UAV simultaneously).
    int outputIdx = idx;
    if (ctx->useBlueNoiseDither && ctx->ditherShader)
    {
        outputIdx = 1 - idx;  // Ping-pong to other buffer
    }

    // 6. Now proceed with encoding the result
    // The texture ctx->d3dTextures[idx] now contains the accumulated/averaged frame
    
    if (ctx->useBlueNoiseDither && ctx->ditherShader)
    {
        // Create SRV to read the TAB result
        ID3D11ShaderResourceView* tabResultSRV = nullptr;
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Texture2D.MipLevels = 1;
        
        HRESULT hr = ctx->device->CreateShaderResourceView(ctx->d3dTextures[idx], &srvDesc, &tabResultSRV);
        if (FAILED(hr)) {
            SetError("Failed to create SRV for Blue Noise input");
            if (createdTemporaryUav && uav) uav->Release();
            return -1;
        }
        
        // Apply Blue Noise dithering to the accumulated result
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
            params.flags = 0;  // TAB output is RGBA
            
            memcpy(mapped.pData, &params, sizeof(params));
            ctx->context->Unmap(ctx->constantBuffer, 0);
        }
        
        ctx->context->CSSetShader(ctx->ditherShader, nullptr, 0);
        ctx->context->CSSetConstantBuffers(0, 1, &ctx->constantBuffer);
        
        // Bind TAB result as first SRV, Blue Noise texture as second
        ID3D11ShaderResourceView* srvs[2] = { tabResultSRV, ctx->blueNoiseSRV };
        ctx->context->CSSetShaderResources(0, 2, srvs);
        
        // Bind UAV for Blue Noise output (ping-pong to other buffer to avoid SRV/UAV conflict)
        bnUav = ctx->encoderUAV[outputIdx];
        if (!bnUav)
        {
            D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
            uavDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
            uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
            uavDesc.Texture2D.MipSlice = 0;
            
            HRESULT hr = ctx->device->CreateUnorderedAccessView(ctx->d3dTextures[outputIdx], &uavDesc, &bnUav);
            if (FAILED(hr)) {
                SetError("Failed to create UAV for Blue Noise output");
                tabResultSRV->Release();
                if (createdTemporaryUav && uav) uav->Release();
                return -1;
            }
            createdBnUav = true;
        }
        ctx->context->CSSetUnorderedAccessViews(0, 1, &bnUav, nullptr);
        
        dispatchX = (ctx->width + 15) / 16;
        dispatchY = (ctx->height + 15) / 16;
        ctx->context->Dispatch(dispatchX, dispatchY, 1);
        
        // Cleanup Blue Noise resources
        ctx->context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
        ID3D11ShaderResourceView* nullSRVs[2] = { nullptr, nullptr };
        ctx->context->CSSetShaderResources(0, 2, nullSRVs);
        ctx->context->CSSetShader(nullptr, nullptr, 0);
        
        ctx->context->Flush();
        
        tabResultSRV->Release();  // Release the temporary SRV
    }
    
    // Release temporary UAV if we created one
    if (createdTemporaryUav && uav)
    {
        uav->Release();
    }
    
    // Release Blue Noise UAV if we created it
    if (createdBnUav && bnUav)
    {
        bnUav->Release();
    }
    
    // 7. Submit to AMF encoder - handle INPUT_FULL by draining until accepted
    AMF_RESULT res;
    do {
        res = ctx->encoder->SubmitInput(ctx->amfSurfaces[outputIdx]);
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
        SetError("AMF SubmitInput failed in TAB finalization");
        return -1;
    }

    // 8. Drain encoded packets (blocking until complete)
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
    
    // Reset sub-frame counter for next output frame
    ctx->currentSubFrame = 0;

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