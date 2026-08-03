#include "NvencEncoder.h"
#include "CinematicRecorderNative.h"
#include "TemporalAccumulation.h"
#include "CASSharpen.h"
#include "../shaders/BlueNoiseDitherBytecode.h"
#include "EmbeddedResources.h"
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <mutex>
#include <vector>

extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/opt.h>
}

#include <dxgi1_2.h>
#pragma comment(lib, "dxgi.lib")

// Minimal local definition of ID3D11Multithread so we don't depend on d3d11_1.h,
// which is absent from some installed Windows SDKs. Layout matches the SDK interface:
// Enter, Leave, SetMultithreadProtected, GetMultithreadProtected (documented order).
static const IID IID_ID3D11Multithread = { 0x9B7E4E00, 0x342C, 0x4106, { 0xA1, 0x9F, 0x4F, 0x27, 0x04, 0xF6, 0x89, 0xF0 } };
struct ID3D11MultithreadLocal : public IUnknown {
    virtual void STDMETHODCALLTYPE Enter(void) = 0;
    virtual void STDMETHODCALLTYPE Leave(void) = 0;
    virtual BOOL STDMETHODCALLTYPE SetMultithreadProtected(BOOL bProtected) = 0;
    virtual BOOL STDMETHODCALLTYPE GetMultithreadProtected(void) = 0;
};

// Typedef for the create instance function pointer (not defined in header)
typedef NVENCSTATUS (NVENCAPI *NVENCAPICREATEINSTANCEPROC)(NV_ENCODE_API_FUNCTION_LIST *);

NvencEncoder::NvencEncoder()
    : m_hEncoder(nullptr), m_hNvencLib(nullptr), m_device(nullptr), m_context(nullptr),
      m_unityDevice(nullptr), m_multithread(nullptr), m_prevMultithreadProtected(FALSE),
      m_multithreadProtectionActive(false), m_bufferIndex(0),
      m_encodeTextureFormat(DXGI_FORMAT_UNKNOWN), m_encodeBufferFormat(NV_ENC_BUFFER_FORMAT_UNDEFINED),
      m_bitstreamBuffer(nullptr), m_formatContext(nullptr), m_videoStream(nullptr),
      m_headerWritten(false),
      m_deferredFrames(0),
      m_frameCount(0), m_width(0), m_height(0), m_fps(0), m_initialized(false), m_isHEVC(false),
      m_tabComputeShader(nullptr), m_casComputeShader(nullptr),
      m_blueNoiseTexture(nullptr), m_blueNoiseSRV(nullptr),
      m_casParamsBuffer(nullptr),
      m_isTabMode(false), m_currentAccumBuffer(0), m_currentSubFrame(0), m_tabSubFrameCount(8),
      m_tabWeightBuffer(nullptr), m_tabFinalizeCount(0), m_tabFirstSliceReceived(false),
      m_syncDiagCount(0) {
    
    memset(m_encodeTextures, 0, sizeof(m_encodeTextures));
    memset(m_accumulationArray, 0, sizeof(m_accumulationArray));
    memset(m_accumulationSRV, 0, sizeof(m_accumulationSRV));
    memset(m_registeredResources, 0, sizeof(m_registeredResources));
    memset(m_mappedInputs, 0, sizeof(m_mappedInputs));
    memset(m_errorBuffer, 0, sizeof(m_errorBuffer));
    memset(&m_nvencFunctions, 0, sizeof(m_nvencFunctions));
    memset(m_intermediateTextures, 0, sizeof(m_intermediateTextures));
    memset(m_intermediateSRV, 0, sizeof(m_intermediateSRV));
    memset(m_intermediateUAV, 0, sizeof(m_intermediateUAV));
}

NvencEncoder::~NvencEncoder() {
    Shutdown();
}

void NvencEncoder::LogDebug(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    vsnprintf(m_debugBuffer, sizeof(m_debugBuffer), fmt, args);
    va_end(args);
    
    char fullMsg[2300];
    snprintf(fullMsg, sizeof(fullMsg), "[NVENC][DEBUG] %s\n", m_debugBuffer);
    OutputDebugStringA(fullMsg);
    CRNativeLog("[NVENC][DEBUG] %s", m_debugBuffer);
}

void NvencEncoder::SetError(const char* fmt, ...) {
    va_list args;
    va_start(args, fmt);
    vsnprintf(m_errorBuffer, sizeof(m_errorBuffer), fmt, args);
    va_end(args);
    
    char fullMsg[1100];
    snprintf(fullMsg, sizeof(fullMsg), "[NVENC][ERROR] %s\n", m_errorBuffer);
    OutputDebugStringA(fullMsg);
}

const char* NvencEncoder::NvencStatusToString(NVENCSTATUS status) {
    switch(status) {
        case NV_ENC_SUCCESS: return "NV_ENC_SUCCESS";
        case NV_ENC_ERR_NO_ENCODE_DEVICE: return "NV_ENC_ERR_NO_ENCODE_DEVICE";
        case NV_ENC_ERR_UNSUPPORTED_DEVICE: return "NV_ENC_ERR_UNSUPPORTED_DEVICE";
        case NV_ENC_ERR_INVALID_ENCODERDEVICE: return "NV_ENC_ERR_INVALID_ENCODERDEVICE";
        case NV_ENC_ERR_INVALID_DEVICE: return "NV_ENC_ERR_INVALID_DEVICE";
        case NV_ENC_ERR_DEVICE_NOT_EXIST: return "NV_ENC_ERR_DEVICE_NOT_EXIST";
        case NV_ENC_ERR_INVALID_PTR: return "NV_ENC_ERR_INVALID_PTR";
        case NV_ENC_ERR_INVALID_PARAM: return "NV_ENC_ERR_INVALID_PARAM";
        case NV_ENC_ERR_OUT_OF_MEMORY: return "NV_ENC_ERR_OUT_OF_MEMORY";
        case NV_ENC_ERR_ENCODER_NOT_INITIALIZED: return "NV_ENC_ERR_ENCODER_NOT_INITIALIZED";
        case NV_ENC_ERR_UNSUPPORTED_PARAM: return "NV_ENC_ERR_UNSUPPORTED_PARAM";
        case NV_ENC_ERR_LOCK_BUSY: return "NV_ENC_ERR_LOCK_BUSY";
        case NV_ENC_ERR_RESOURCE_REGISTER_FAILED: return "NV_ENC_ERR_RESOURCE_REGISTER_FAILED";
        case NV_ENC_ERR_RESOURCE_NOT_REGISTERED: return "NV_ENC_ERR_RESOURCE_NOT_REGISTERED";
        case NV_ENC_ERR_RESOURCE_NOT_MAPPED: return "NV_ENC_ERR_RESOURCE_NOT_MAPPED";
        case NV_ENC_ERR_GENERIC: return "NV_ENC_ERR_GENERIC";
        default: return "UNKNOWN_ERROR";
    }
}

bool NvencEncoder::LoadNvencLibrary() {
    LogDebug("Loading nvEncodeAPI64.dll...");
    
    // Add detailed logging before load
    m_hNvencLib = LoadLibraryA("nvEncodeAPI64.dll");
    if (!m_hNvencLib) {
        DWORD lastError = ::GetLastError();
        LogDebug("nvEncodeAPI64.dll not found (expected on non-NVIDIA systems), error: %lu", lastError);
        SetError("nvEncodeAPI64.dll not found (expected on non-NVIDIA systems), error: %lu", lastError);
        return false;
    }
    
    LogDebug("nvEncodeAPI64.dll loaded successfully at %p", m_hNvencLib);
    
    LogDebug("Attempting GetProcAddress for NvEncodeAPICreateInstance");
    auto createInstance = (NVENCAPICREATEINSTANCEPROC)GetProcAddress(m_hNvencLib, "NvEncodeAPICreateInstance");
    if (!createInstance) {
        DWORD lastError = ::GetLastError();
        SetError("Failed to get NvEncodeAPICreateInstance, error: %lu", lastError);
        return false;
    }
    
    LogDebug("NvEncodeAPICreateInstance found at %p", createInstance);
    
    m_nvencFunctions.version = NV_ENCODE_API_FUNCTION_LIST_VER;
    LogDebug("Calling NvEncodeAPICreateInstance with version %u", m_nvencFunctions.version);
    NVENCSTATUS status = createInstance(&m_nvencFunctions);
    
    if (status != NV_ENC_SUCCESS) {
        SetError("NvEncodeAPICreateInstance failed: %s", NvencStatusToString(status));
        return false;
    }
    
    LogDebug("NVENC API loaded successfully, version %u", m_nvencFunctions.version);
    return true;
}

bool NvencEncoder::ValidateOrCreateDevice(ID3D11Device* unityDevice, ID3D11Texture2D* textureHint) {
    LogDebug("ValidateOrCreateDevice called");
    
    if (!unityDevice) {
        SetError("Unity device is null");
        return false;
    }
    
    m_unityDevice = unityDevice;
    m_unityDevice->AddRef();
    LogDebug("Unity device retained");
    
    // Direct device usage only: probe by opening a session on the Unity device.
    // (The cross-device shared-texture fallback was removed in Phase 3 - it was racy
    // by design (no keyed mutex), never taken on the test machine, and untestable.
    // If this probe ever fails, init fails loudly and a proper keyed-mutex rework
    // becomes its own scope, per NVENC_ZEROCOPY_PLAN Phase 1.)
    LogDebug("Attempting direct Unity device usage...");
    NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS params = {};
    params.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
    params.deviceType = NV_ENC_DEVICE_TYPE_DIRECTX;
    params.device = unityDevice;
    params.apiVersion = NVENCAPI_VERSION;
    
    void* testEncoder = nullptr;
    NVENCSTATUS status = m_nvencFunctions.nvEncOpenEncodeSessionEx(&params, &testEncoder);
    if (status != NV_ENC_SUCCESS) {
        SetError("NVENC session probe on the render device failed: %s", NvencStatusToString(status));
        return false;
    }
    m_nvencFunctions.nvEncDestroyEncoder(testEncoder);
    
    m_device = unityDevice;
    m_device->AddRef();
    m_device->GetImmediateContext(&m_context);
    LogDebug("Direct Unity device usage successful");
    
    // Enable D3D11 multithread protection on Unity's immediate context so concurrent
    // access from Unity render thread, our main thread, and NVENC's worker is
    // serialized by the runtime. Restore original state in Shutdown.
    ID3D11MultithreadLocal* mt = nullptr;
    if (m_context && SUCCEEDED(m_context->QueryInterface(IID_ID3D11Multithread, (void**)&mt))) {
        m_multithread = mt;
        m_prevMultithreadProtected = mt->GetMultithreadProtected();
        mt->SetMultithreadProtected(TRUE);
        m_multithreadProtectionActive = true;
        LogDebug("D3D11 multithread protection enabled (was %s)", m_prevMultithreadProtected ? "TRUE" : "FALSE");
    } else {
        LogDebug("D3D11Multithread not available, proceeding without runtime protection");
    }
    
    return true;
}

bool NvencEncoder::InitializeEncoder(const NvencEncoderSettings& settings) {
    LogDebug("Initializing encoder: %dx%d @ %d fps, Codec=%s, RC=%s",
             m_width, m_height, m_fps,
             settings.Codec == 1 ? "HEVC" : "H264",
             settings.RateControlMode == 0 ? "CQP" : "VBR");
    
    // Open session
    NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS sessionParams = {};
    sessionParams.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
    sessionParams.deviceType = NV_ENC_DEVICE_TYPE_DIRECTX;
    sessionParams.device = m_device;
    sessionParams.apiVersion = NVENCAPI_VERSION;
    
    NVENCSTATUS status = m_nvencFunctions.nvEncOpenEncodeSessionEx(&sessionParams, &m_hEncoder);
    if (status != NV_ENC_SUCCESS) {
        SetError("nvEncOpenEncodeSessionEx failed: %s", NvencStatusToString(status));
        return false;
    }
    LogDebug("Encode session opened, encoder handle: %p", m_hEncoder);
    
    // Initialize params
    NV_ENC_INITIALIZE_PARAMS initParams = {};
    initParams.version = NV_ENC_INITIALIZE_PARAMS_VER;
    initParams.encodeWidth = m_width;
    initParams.encodeHeight = m_height;
    initParams.darWidth = m_width;
    initParams.darHeight = m_height;
    initParams.frameRateNum = m_fps;
    initParams.frameRateDen = 1;
    initParams.enableEncodeAsync = 0;
    initParams.enablePTD = 1;
    initParams.bufferFormat = NV_ENC_BUFFER_FORMAT_ARGB;
    initParams.tuningInfo = NV_ENC_TUNING_INFO_HIGH_QUALITY;
    
    // Select codec
    if (settings.Codec == 1) {
        initParams.encodeGUID = NV_ENC_CODEC_HEVC_GUID;
        m_isHEVC = true;
    } else {
        initParams.encodeGUID = NV_ENC_CODEC_H264_GUID;
        m_isHEVC = false;
    }
    
    // Select preset (P1=Speed, P4=Balanced, P7=Quality)
    if (settings.QualityPreset == 0)
        initParams.presetGUID = NV_ENC_PRESET_P1_GUID;
    else if (settings.QualityPreset == 2)
        initParams.presetGUID = NV_ENC_PRESET_P7_GUID;
    else
        initParams.presetGUID = NV_ENC_PRESET_P4_GUID;
    
    LogDebug("Using Preset: %s", settings.QualityPreset == 0 ? "P1(Speed)" : 
             (settings.QualityPreset == 2 ? "P7(Quality)" : "P4(Balanced)"));
    
    // Configure: start from the driver's preset config (the header-recommended flow)
    // and apply our overrides on top. A zero-built NV_ENC_CONFIG leaves fields like
    // frameFieldMode=0, which nvEncInitializeEncoder tolerates but nvEncEncodePicture
    // rejects with UNSUPPORTED_PARAM for RGB input (probe-verified, RTX 3050,
    // driver 595.97, 2026-07-28); a zero chromaFormatIDC additionally fails init with
    // INVALID_PARAM (F1).
    NV_ENC_PRESET_CONFIG presetConfig = {};
    presetConfig.version = NV_ENC_PRESET_CONFIG_VER;
    presetConfig.presetCfg.version = NV_ENC_CONFIG_VER;
    status = m_nvencFunctions.nvEncGetEncodePresetConfigEx(m_hEncoder, initParams.encodeGUID,
                                                           initParams.presetGUID,
                                                           initParams.tuningInfo, &presetConfig);
    if (status != NV_ENC_SUCCESS) {
        SetError("nvEncGetEncodePresetConfigEx failed: %s", NvencStatusToString(status));
        return false;
    }
    NV_ENC_CONFIG encodeConfig = presetConfig.presetCfg;
    encodeConfig.version = NV_ENC_CONFIG_VER;
    encodeConfig.profileGUID = NV_ENC_CODEC_PROFILE_AUTOSELECT_GUID;
    encodeConfig.gopLength = settings.GopSize;
    encodeConfig.frameIntervalP = 1;
    encodeConfig.mvPrecision = NV_ENC_MV_PRECISION_QUARTER_PEL;
    
    // Rate control
    encodeConfig.rcParams.version = NV_ENC_RC_PARAMS_VER;
    if (settings.RateControlMode == 0) {
        encodeConfig.rcParams.rateControlMode = NV_ENC_PARAMS_RC_CONSTQP;
        encodeConfig.rcParams.constQP.qpIntra = settings.QpI;
        encodeConfig.rcParams.constQP.qpInterP = settings.QpP;
        encodeConfig.rcParams.constQP.qpInterB = settings.QpB;
        LogDebug("CQP Settings: I=%d, P=%d, B=%d", settings.QpI, settings.QpP, settings.QpB);
    } else {
        // F14: honest VBR - previously maxBitRate == averageBitRate (effectively CBR)
        // and vbvBufferSize was one frame of average rate. Now: 2x peak headroom and
        // one second of peak rate in the VBV buffer.
        encodeConfig.rcParams.rateControlMode = NV_ENC_PARAMS_RC_VBR;
        encodeConfig.rcParams.averageBitRate = settings.TargetBitrateKbps * 1000;
        encodeConfig.rcParams.maxBitRate = settings.TargetBitrateKbps * 2000;
        encodeConfig.rcParams.vbvBufferSize = settings.TargetBitrateKbps * 2000;
        LogDebug("VBR Settings: %d kbps avg, %d kbps peak", settings.TargetBitrateKbps,
                 settings.TargetBitrateKbps * 2);
    }
    
    // Codec specific settings - NOTE: These structs don't have version fields
    if (m_isHEVC) {
        encodeConfig.encodeCodecConfig.hevcConfig.idrPeriod = settings.GopSize;
        encodeConfig.encodeCodecConfig.hevcConfig.repeatSPSPPS = 1;
        encodeConfig.encodeCodecConfig.hevcConfig.chromaFormatIDC = 1;
    } else {
        encodeConfig.encodeCodecConfig.h264Config.idrPeriod = settings.GopSize;
        encodeConfig.encodeCodecConfig.h264Config.repeatSPSPPS = 1;
        // F1: chromaFormatIDC=0 (4:0:0 monochrome) is rejected by the driver at
        // nvEncInitializeEncoder with INVALID_PARAM; 1 = YUV 4:2:0. Hardware-proven
        // root cause (nvprobe bisection, RTX 3050, driver 595.97, 2026-07-28). The
        // Ex preset config already carries 1; set it explicitly for clarity.
        encodeConfig.encodeCodecConfig.h264Config.chromaFormatIDC = 1;
    }
    
    initParams.encodeConfig = &encodeConfig;
    
    status = m_nvencFunctions.nvEncInitializeEncoder(m_hEncoder, &initParams);
    if (status != NV_ENC_SUCCESS) {
        SetError("nvEncInitializeEncoder failed: %s", NvencStatusToString(status));
        return false;
    }
    LogDebug("Encoder initialized successfully (codec: %s)", m_isHEVC ? "HEVC" : "H264");
    
    // Create bitstream buffer
    NV_ENC_CREATE_BITSTREAM_BUFFER bitstreamBuffer = {};
    bitstreamBuffer.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
    status = m_nvencFunctions.nvEncCreateBitstreamBuffer(m_hEncoder, &bitstreamBuffer);
    if (status != NV_ENC_SUCCESS) {
        SetError("nvEncCreateBitstreamBuffer failed: %s", NvencStatusToString(status));
        return false;
    }
    m_bitstreamBuffer = bitstreamBuffer.bitstreamBuffer;
    LogDebug("Bitstream buffer created, handle: %p", m_bitstreamBuffer);
    
    // Initialize compute shaders (non-fatal)
    LogDebug("Initializing compute shaders...");
    InitializeComputeShaders();
    LogDebug("Compute shaders initialized");
    
    // Create intermediate textures for shader pipeline
    LogDebug("Creating intermediate textures...");
    if (!CreateIntermediateTextures(m_width, m_height)) {
        SetError("Failed to create intermediate textures");
        return false;
    }
    LogDebug("Intermediate textures created");
    
    // Create blue noise texture (non-fatal)
    LogDebug("Creating blue noise texture...");
    CreateBlueNoiseTexture();
    
    // Create constant buffers (non-fatal)
    LogDebug("Creating constant buffers...");
    CreateConstantBuffers();
    
    // Create accumulation array (non-fatal)
    LogDebug("Creating accumulation array...");
    CreateAccumulationArray(m_width, m_height);
    
    // Create encode textures
    LogDebug("Creating encode textures...");
    D3D11_TEXTURE2D_DESC texDesc = {};
    texDesc.Width = m_width;
    texDesc.Height = m_height;
    texDesc.MipLevels = 1;
    texDesc.ArraySize = 1;
    texDesc.Format = m_encodeTextureFormat; // F2/F3: match the source format exactly
    texDesc.SampleDesc.Count = 1;
    texDesc.Usage = D3D11_USAGE_DEFAULT;
    texDesc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    
    for (int i = 0; i < 2; i++) {
        HRESULT hr = m_device->CreateTexture2D(&texDesc, nullptr, &m_encodeTextures[i]);
        if (FAILED(hr)) {
            SetError("Failed to create encode texture %d (0x%08X)", i, hr);
            return false;
        }
        LogDebug("Encode texture %d created", i);
        
        // Register with NVENC
        NV_ENC_REGISTER_RESOURCE regRes = {};
        regRes.version = NV_ENC_REGISTER_RESOURCE_VER;
        regRes.resourceType = NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;
        regRes.width = m_width;
        regRes.height = m_height;
        regRes.pitch = 0;
        regRes.subResourceIndex = 0;
        regRes.resourceToRegister = m_encodeTextures[i];
        regRes.bufferFormat = m_encodeBufferFormat; // F2: declare the actual texture layout
        regRes.bufferUsage = NV_ENC_INPUT_IMAGE;
        
        status = m_nvencFunctions.nvEncRegisterResource(m_hEncoder, &regRes);
        if (status != NV_ENC_SUCCESS) {
            SetError("nvEncRegisterResource failed for buffer %d: %s", i, NvencStatusToString(status));
            return false;
        }
        m_registeredResources[i] = regRes.registeredResource;
        LogDebug("Registered resource %d: %p", i, m_registeredResources[i]);
    }
    
    LogDebug("InitializeEncoder complete");
    return true;
}

bool NvencEncoder::InitializeFFmpeg(const char* outputPath) {
    LogDebug("InitializeFFmpeg called");
    LogDebug("Initializing FFmpeg muxer: %s", outputPath ? outputPath : "(null)");
    
    if (!outputPath) {
        SetError("Output path is null");
        return false;
    }
    
    LogDebug("Guessing output format (matroska)...");
    const AVOutputFormat* fmt = av_guess_format("matroska", nullptr, nullptr);
    if (!fmt) {
        SetError("MKV muxer not found");
        return false;
    }
    LogDebug("MKV muxer found: %s", fmt->name);
    
    AVFormatContext* ctx = nullptr;
    LogDebug("Allocating output context...");
    if (avformat_alloc_output_context2(&ctx, fmt, nullptr, outputPath) < 0) {
        SetError("avformat_alloc_output_context2 failed");
        return false;
    }
    m_formatContext = ctx;
    LogDebug("Output context allocated");
    
    LogDebug("Creating video stream...");
    AVStream* stream = avformat_new_stream(ctx, nullptr);
    if (!stream) {
        SetError("avformat_new_stream failed");
        return false;
    }
    m_videoStream = stream;
    LogDebug("Video stream created, index: %d", stream->index);
    
    AVCodecParameters* par = stream->codecpar;
    par->codec_type = AVMEDIA_TYPE_VIDEO;
    par->codec_id = m_isHEVC ? AV_CODEC_ID_HEVC : AV_CODEC_ID_H264;
    par->width = m_width;
    par->height = m_height;
    par->format = AV_PIX_FMT_YUV420P;
    par->color_range = AVCOL_RANGE_MPEG;
    par->color_primaries = AVCOL_PRI_BT709;
    par->color_trc = AVCOL_TRC_BT709;
    par->color_space = AVCOL_SPC_BT709;
    
    stream->time_base = { 1, m_fps };
    stream->avg_frame_rate = { m_fps, 1 };
    
    if (!(ctx->oformat->flags & AVFMT_NOFILE)) {
        LogDebug("Opening output file...");
        if (avio_open(&ctx->pb, outputPath, AVIO_FLAG_WRITE) < 0) {
            SetError("avio_open failed for %s", outputPath);
            return false;
        }
        LogDebug("Output file opened");
    }
    
    // F8: the matroska muxer requires extradata (avcC for H264, hvcC for HEVC) in
    // CodecPrivate - without it avformat_write_header fails with INVALIDDATA
    // (probe-verified 2026-07-28).
    LogDebug("Fetching sequence-header extradata from NVENC...");
    if (!SetExtradataFromNvenc(stream)) {
        return false; // SetError already called
    }
    LogDebug("Extradata set (%d bytes)", par->extradata_size);
    
    LogDebug("Writing format header...");
    if (avformat_write_header(ctx, nullptr) < 0) {
        SetError("avformat_write_header failed");
        return false;
    }
    m_headerWritten = true;
    
    LogDebug("FFmpeg muxer ready");
    return true;
}

// F8: pull sequence headers out-of-band from NVENC (Annex-B) and repackage as
// extradata in the format the matroska muxer expects in CodecPrivate:
// avcC for H264 (SPS/PPS), hvcC for HEVC (VPS/SPS/PPS arrays).
// Mirrors the AMF path's "extradata before header" ordering (CinematicRecorderNative.cpp:807-837).
bool NvencEncoder::SetExtradataFromNvenc(void* avStream) {
    AVStream* stream = (AVStream*)avStream;
    
    uint8_t payload[1024];
    uint32_t payloadSize = 0;
    NV_ENC_SEQUENCE_PARAM_PAYLOAD spspps = {};
    spspps.version = NV_ENC_SEQUENCE_PARAM_PAYLOAD_VER;
    spspps.spsppsBuffer = payload;
    spspps.inBufferSize = sizeof(payload);
    spspps.outSPSPPSPayloadSize = &payloadSize;
    
    NVENCSTATUS status = m_nvencFunctions.nvEncGetSequenceParams(m_hEncoder, &spspps);
    if (status != NV_ENC_SUCCESS) {
        SetError("nvEncGetSequenceParams failed: %s", NvencStatusToString(status));
        return false;
    }
    uint32_t size = payloadSize;
    
    // Locate Annex-B start codes (00 00 01; 4-byte form contains the 3-byte form at +1)
    std::vector<uint32_t> sc;
    for (uint32_t i = 0; i + 3 < size; i++) {
        if (payload[i] == 0 && payload[i+1] == 0 && payload[i+2] == 1) {
            sc.push_back(i);
            i += 2;
        }
    }
    
    // Split into NALs by type. H264: 1-byte header, type = b & 0x1F (SPS=7, PPS=8).
    // HEVC: 2-byte header, type = (b >> 1) & 0x3F (VPS=32, SPS=33, PPS=34).
    std::vector<std::pair<const uint8_t*, uint32_t>> vps, sps, pps;
    for (size_t k = 0; k < sc.size(); k++) {
        uint32_t start = sc[k] + 3;
        uint32_t end = (k + 1 < sc.size()) ? sc[k + 1] : size;
        while (end > start && payload[end - 1] == 0) end--; // trailing zeros belong to next prefix
        if (end <= start) continue;
        if (m_isHEVC) {
            uint8_t nalType = (payload[start] >> 1) & 0x3F;
            if (nalType == 32) vps.push_back({ payload + start, end - start });
            else if (nalType == 33) sps.push_back({ payload + start, end - start });
            else if (nalType == 34) pps.push_back({ payload + start, end - start });
        } else {
            uint8_t nalType = payload[start] & 0x1F;
            if (nalType == 7) sps.push_back({ payload + start, end - start });
            else if (nalType == 8) pps.push_back({ payload + start, end - start });
        }
    }
    
    if (m_isHEVC)
        return BuildHevcExtradata(stream, vps, sps, pps);
    return BuildH264Extradata(stream, sps, pps);
}

// H264: avcC - version, profile/compat/level from SPS, 4-byte NAL lengths, SPS/PPS lists.
bool NvencEncoder::BuildH264Extradata(void* avStream,
                                      const std::vector<std::pair<const uint8_t*, uint32_t>>& sps,
                                      const std::vector<std::pair<const uint8_t*, uint32_t>>& pps) {
    AVStream* stream = (AVStream*)avStream;
    
    if (sps.empty() || pps.empty() || sps[0].second < 4) {
        SetError("nvEncGetSequenceParams returned no usable SPS/PPS (%zu SPS, %zu PPS)",
                 sps.size(), pps.size());
        return false;
    }
    
    size_t total = 7;
    for (auto& n : sps) total += 2 + n.second;
    total += 1;
    for (auto& n : pps) total += 2 + n.second;
    
    uint8_t* avcc = (uint8_t*)av_mallocz(total + AV_INPUT_BUFFER_PADDING_SIZE);
    if (!avcc) {
        SetError("av_mallocz failed for extradata");
        return false;
    }
    
    uint8_t* p = avcc;
    *p++ = 1;             // configurationVersion
    *p++ = sps[0].first[1]; // AVCProfileIndication
    *p++ = sps[0].first[2]; // profile_compatibility
    *p++ = sps[0].first[3]; // AVCLevelIndication
    *p++ = 0xFF;          // 6 reserved bits + lengthSizeMinusOne = 3 (4-byte lengths)
    *p++ = (uint8_t)(0xE0 | sps.size());
    for (auto& n : sps) {
        *p++ = (uint8_t)(n.second >> 8);
        *p++ = (uint8_t)(n.second & 0xFF);
        memcpy(p, n.first, n.second);
        p += n.second;
    }
    *p++ = (uint8_t)pps.size();
    for (auto& n : pps) {
        *p++ = (uint8_t)(n.second >> 8);
        *p++ = (uint8_t)(n.second & 0xFF);
        memcpy(p, n.first, n.second);
        p += n.second;
    }
    
    stream->codecpar->extradata = avcc;
    stream->codecpar->extradata_size = (int)total;
    return true;
}

// HEVC: hvcC (HEVCDecoderConfigurationRecord, ISO/IEC 14496-15) - 23-byte header
// with the 12 general profile_tier_level bytes lifted from the SPS, then one
// array each for VPS/SPS/PPS with 4-byte... (2-byte per spec) NAL lengths.
bool NvencEncoder::BuildHevcExtradata(void* avStream,
                                      const std::vector<std::pair<const uint8_t*, uint32_t>>& vps,
                                      const std::vector<std::pair<const uint8_t*, uint32_t>>& sps,
                                      const std::vector<std::pair<const uint8_t*, uint32_t>>& pps) {
    AVStream* stream = (AVStream*)avStream;
    
    // SPS layout: 2-byte NAL header, 1 byte (vps id + max sub layers + nesting),
    // then 12 bytes of general profile_tier_level - need at least 15 bytes.
    if (vps.empty() || sps.empty() || pps.empty() || sps[0].second < 15) {
        SetError("nvEncGetSequenceParams returned no usable VPS/SPS/PPS (%zu/%zu/%zu)",
                 vps.size(), sps.size(), pps.size());
        return false;
    }
    
    size_t total = 23;
    total += 3 + 2 * vps.size();
    for (auto& n : vps) total += n.second;
    total += 3 + 2 * sps.size();
    for (auto& n : sps) total += n.second;
    total += 3 + 2 * pps.size();
    for (auto& n : pps) total += n.second;
    
    uint8_t* hvcc = (uint8_t*)av_mallocz(total + AV_INPUT_BUFFER_PADDING_SIZE);
    if (!hvcc) {
        SetError("av_mallocz failed for extradata");
        return false;
    }
    
    uint8_t* p = hvcc;
    *p++ = 1;                        // configurationVersion
    memcpy(p, sps[0].first + 3, 12); // general profile_tier_level (12 bytes)
    p += 12;
    *p++ = 0xF0;                     // 4 reserved + min_spatial_segmentation_idc hi (0 = unknown)
    *p++ = 0xF0;                     // 4 reserved + min_spatial_segmentation_idc lo
    *p++ = 0xFC;                     // 6 reserved + parallelismType (0)
    *p++ = 0xFC | 1;                 // 6 reserved + chromaFormat (1 = 4:2:0)
    *p++ = 0xF8;                     // 5 reserved + bitDepthLumaMinus8 (0)
    *p++ = 0xF8;                     // 5 reserved + bitDepthChromaMinus8 (0)
    *p++ = 0; *p++ = 0;              // avgFrameRate (0 = unspecified)
    *p++ = 0x0F;                     // constantFrameRate=0, numTemporalLayers=1, temporalIdNested=1, lengthSizeMinusOne=3
    *p++ = 3;                        // numOfArrays (VPS, SPS, PPS)
    
    const struct { const std::vector<std::pair<const uint8_t*, uint32_t>>* nals; uint8_t type; } arrays[] = {
        { &vps, 32 }, { &sps, 33 }, { &pps, 34 }
    };
    for (auto& a : arrays) {
        *p++ = (uint8_t)(0x80 | a.type); // array_completeness=1 + NAL_unit_type
        *p++ = (uint8_t)(a.nals->size() >> 8);
        *p++ = (uint8_t)(a.nals->size() & 0xFF);
        for (auto& n : *a.nals) {
            *p++ = (uint8_t)(n.second >> 8);
            *p++ = (uint8_t)(n.second & 0xFF);
            memcpy(p, n.first, n.second);
            p += n.second;
        }
    }
    
    stream->codecpar->extradata = hvcc;
    stream->codecpar->extradata_size = (int)total;
    return true;
}

bool NvencEncoder::Initialize(ID3D11Device* unityDevice, ID3D11Texture2D* textureHint,
                              int width, int height, int fps, const char* outputPath,
                              const NvencEncoderSettings& settings) {
    LogDebug("NVENC Initialize called");
    LogDebug("Parameters: %dx%d @ %d fps, output=%s", width, height, fps, outputPath ? outputPath : "(null)");
    
    m_width = width;
    m_height = height;
    m_fps = fps;
    
    LogDebug("Phase 1: Loading NVENC library...");
    if (!LoadNvencLibrary()) {
        LogDebug("Phase 1 FAILED: LoadNvencLibrary returned false");
        return false;
    }
    LogDebug("Phase 1 complete: NVENC library loaded");
    
    LogDebug("Phase 2: Validating/Creating device...");
    if (!ValidateOrCreateDevice(unityDevice, textureHint)) {
        LogDebug("Phase 2 FAILED: ValidateOrCreateDevice returned false");
        return false;
    }
    LogDebug("Phase 2 complete: Device ready");
    
    // F2/F3: determine the source texture format up front. Encode textures are created
    // in the matching typed UNORM format so CopyResource is legal by construction, and
    // the matching NVENC buffer format is declared (B8G8R8A8 -> ARGB, R8G8B8A8 -> ABGR).
    // An unexpected format fails init loudly instead of silently no-op copying later.
    //
    // Unity render textures are often TYPELESS (smoke test, 2026-07-29: format 27 =
    // R8G8B8A8_TYPELESS) or SRGB - same 32-bit RGBA memory layout as their UNORM
    // twins, so the whole family is accepted; our encode textures are always created
    // as the typed UNORM twin (typeless->typed copies of the same size are
    // CopyResource-compatible, and NVENC registration gets a plain typed texture).
    if (!textureHint) {
        SetError("Source texture hint is null; cannot determine encode format");
        return false;
    }
    D3D11_TEXTURE2D_DESC srcDesc = {};
    textureHint->GetDesc(&srcDesc);
    LogDebug("Source texture: format=%d, %ux%u", (int)srcDesc.Format, srcDesc.Width, srcDesc.Height);
    switch (srcDesc.Format) {
        case DXGI_FORMAT_B8G8R8A8_UNORM:
        case DXGI_FORMAT_B8G8R8A8_TYPELESS:
        case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
            m_encodeTextureFormat = DXGI_FORMAT_B8G8R8A8_UNORM;
            m_encodeBufferFormat = NV_ENC_BUFFER_FORMAT_ARGB;
            break;
        case DXGI_FORMAT_R8G8B8A8_UNORM:
        case DXGI_FORMAT_R8G8B8A8_TYPELESS:
        case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            m_encodeTextureFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
            m_encodeBufferFormat = NV_ENC_BUFFER_FORMAT_ABGR;
            break;
        default:
            SetError("Unsupported source texture format %d (expected an R8G8B8A8/B8G8R8A8 variant)",
                     (int)srcDesc.Format);
            return false;
    }
    LogDebug("Encode format selected: DXGI=%d, NVENC=%s", (int)m_encodeTextureFormat,
             m_encodeBufferFormat == NV_ENC_BUFFER_FORMAT_ARGB ? "ARGB" : "ABGR");
    
    LogDebug("Phase 3: Initializing encoder...");
    if (!InitializeEncoder(settings)) {
        LogDebug("Phase 3 FAILED: InitializeEncoder returned false");
        return false;
    }
    LogDebug("Phase 3 complete: Encoder initialized");
    
    LogDebug("Phase 4: Initializing FFmpeg...");
    if (!InitializeFFmpeg(outputPath)) {
        LogDebug("Phase 4 FAILED: InitializeFFmpeg returned false");
        return false;
    }
    LogDebug("Phase 4 complete: FFmpeg ready");
    
    m_initialized = true;
    LogDebug("NVENC Encoder fully initialized and ready");
    return true;
}

bool NvencEncoder::EncodeFrame(ID3D11Texture2D* unityTexture, int64_t frameIndex, bool enableCAS, float sharpness) {
    std::lock_guard<std::mutex> lock(m_encodeMutex);
    
    if (!m_initialized) return false;
    
    // Use CAS preprocessing path if enabled and shader is available
    if (enableCAS && m_casComputeShader) {
        return EncodeFrameWithCAS(unityTexture, frameIndex, sharpness);
    }
    
    // Otherwise use original direct path
    int idx = m_bufferIndex;
    m_bufferIndex = 1 - idx;
    
    LogDebug("Frame %lld - Processing buffer %d", frameIndex, idx);
    
    // Copy from Unity texture to our encode texture
    if (unityTexture) {
        // F3: CopyResource silently no-ops on format mismatch - verify instead.
        // Typeless/SRGB twins share the encode format's 32-bit layout and are
        // copy-compatible (Unity hands us R8G8B8A8_TYPELESS).
        D3D11_TEXTURE2D_DESC frameDesc = {};
        unityTexture->GetDesc(&frameDesc);
        DXGI_FORMAT f = frameDesc.Format;
        bool sameFamily =
            (m_encodeTextureFormat == DXGI_FORMAT_B8G8R8A8_UNORM &&
             (f == DXGI_FORMAT_B8G8R8A8_UNORM || f == DXGI_FORMAT_B8G8R8A8_TYPELESS ||
              f == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB)) ||
            (m_encodeTextureFormat == DXGI_FORMAT_R8G8B8A8_UNORM &&
             (f == DXGI_FORMAT_R8G8B8A8_UNORM || f == DXGI_FORMAT_R8G8B8A8_TYPELESS ||
              f == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB));
        if (!sameFamily) {
            SetError("Frame texture format %d incompatible with encode format %d; refusing blind CopyResource",
                     (int)f, (int)m_encodeTextureFormat);
            return false;
        }
        m_context->CopyResource(m_encodeTextures[idx], unityTexture);
        m_context->Flush();
        LogDebug("Frame %lld - CopyResource completed", frameIndex);
    }
    
    // Use the extracted EncodeNVENC method
    return EncodeNVENC(idx, frameIndex);
}

bool NvencEncoder::ProcessOutput() {
    bool ignored;
    return ProcessOutput(&ignored);
}

bool NvencEncoder::ProcessOutput(bool* wrotePacket) {
    *wrotePacket = false;
    NV_ENC_LOCK_BITSTREAM lockBS = {};
    lockBS.version = NV_ENC_LOCK_BITSTREAM_VER;
    lockBS.outputBitstream = m_bitstreamBuffer;
    
    NVENCSTATUS status = m_nvencFunctions.nvEncLockBitstream(m_hEncoder, &lockBS);
    if (status != NV_ENC_SUCCESS) {
        if (status == NV_ENC_ERR_LOCK_BUSY) {
            LogDebug("Lock busy, will retry next frame");
            return true;
        }
        SetError("nvEncLockBitstream failed: %s", NvencStatusToString(status));
        return false;
    }
    *wrotePacket = true;
    
    LogDebug("Bitstream locked: %u bytes, Frame %d", lockBS.bitstreamSizeInBytes, lockBS.frameIdx);
    
    AVPacket pkt = {};
    av_init_packet(&pkt);
    pkt.data = (uint8_t*)lockBS.bitstreamBufferPtr;
    pkt.size = lockBS.bitstreamSizeInBytes;
    pkt.pts = m_frameCount;
    pkt.dts = m_frameCount;
    pkt.duration = 1;
    pkt.stream_index = ((AVStream*)m_videoStream)->index;
    
    // Keyframe detection: walk ALL NALUs in the access unit - SPS/PPS may precede
    // the IDR slice, so the first start code is not authoritative. H264 IDR = type 5
    // (b & 0x1F); HEVC IDR = types 19/20/21 ((b >> 1) & 0x3F, 2-byte NAL header).
    if (pkt.size > 4) {
        uint8_t* data = pkt.data;
        int offset = 0;
        while (offset < pkt.size - 4 && !(pkt.flags & AV_PKT_FLAG_KEY)) {
            if (data[offset] == 0 && data[offset+1] == 0 && 
                ((data[offset+2] == 1) || (data[offset+2] == 0 && data[offset+3] == 1))) {
                int start = (data[offset+2] == 1) ? offset+3 : offset+4;
                if (start >= pkt.size) break;
                if (m_isHEVC) {
                    uint8_t nalType = (data[start] >> 1) & 0x3F;
                    if (nalType == 19 || nalType == 20 || nalType == 21)
                        pkt.flags |= AV_PKT_FLAG_KEY;
                } else {
                    uint8_t nalType = data[start] & 0x1F;
                    if (nalType == 5)
                        pkt.flags |= AV_PKT_FLAG_KEY;
                }
            }
            offset++;
        }
    }
    
    // Matroska + hvcC expects length-prefixed NAL units, but NVENC emits Annex-B;
    // convert by replacing each start code with a 4-byte big-endian NAL length
    // (matches lengthSizeMinusOne=3 in our hvcC). The H264 path relies on the
    // muxer's automatic Annex-B->avcC conversion, hardware-verified in Phase 1 -
    // do not disturb it.
    std::vector<uint8_t> lpBuffer;
    if (m_isHEVC && pkt.size > 4) {
        uint8_t* data = pkt.data;
        int n = pkt.size;
        std::vector<std::pair<int,int>> spans; // NAL [start,end) within data
        int i = 0;
        while (i < n - 3) {
            bool sc4 = data[i] == 0 && data[i+1] == 0 && data[i+2] == 0 && data[i+3] == 1;
            bool sc3 = !sc4 && data[i] == 0 && data[i+1] == 0 && data[i+2] == 1;
            if (sc3 || sc4) {
                int start = sc3 ? i + 3 : i + 4;
                if (!spans.empty() && spans.back().second < 0) spans.back().second = i;
                spans.push_back({ start, -1 });
                i = start;
            } else {
                i++;
            }
        }
        if (!spans.empty() && spans.back().second < 0) spans.back().second = n;
        
        if (!spans.empty()) {
            for (auto& s : spans)
                while (s.second > s.first && data[s.second - 1] == 0) s.second--; // trim trailing zeros
            for (auto& s : spans) {
                uint32_t len = (uint32_t)(s.second - s.first);
                if (len == 0) continue;
                lpBuffer.push_back((uint8_t)(len >> 24));
                lpBuffer.push_back((uint8_t)(len >> 16));
                lpBuffer.push_back((uint8_t)(len >> 8));
                lpBuffer.push_back((uint8_t)(len & 0xFF));
                lpBuffer.insert(lpBuffer.end(), data + s.first, data + s.second);
            }
            pkt.data = lpBuffer.data();
            pkt.size = (int)lpBuffer.size();
        }
    }
    
    AVRational tb = { 1, m_fps };
    av_packet_rescale_ts(&pkt, tb, ((AVStream*)m_videoStream)->time_base);
    
    {
        // F16: member mutex (was a function-static shared across encoder instances;
        // matches the AMF module's per-context writeMutex)
        std::lock_guard<std::mutex> lock(m_writeMutex);
        av_interleaved_write_frame((AVFormatContext*)m_formatContext, &pkt);
    }
    
    m_frameCount++;
    
    m_nvencFunctions.nvEncUnlockBitstream(m_hEncoder, m_bitstreamBuffer);
    return true;
}

bool NvencEncoder::InitializeComputeShaders() {
    HRESULT hr;
    
    // TAB shader
    hr = m_device->CreateComputeShader(
        g_TemporalAccumulationCS, 
        sizeof(g_TemporalAccumulationCS),
        nullptr, 
        &m_tabComputeShader
    );
    if (FAILED(hr)) {
        LogDebug("Failed to create TAB compute shader: 0x%08X", hr);
        // Non-fatal - can still encode without preprocessing
    }
    
    // CAS shader
    hr = m_device->CreateComputeShader(
        g_CASSharpenCS,
        sizeof(g_CASSharpenCS),
        nullptr,
        &m_casComputeShader
    );
    if (FAILED(hr)) {
        LogDebug("Failed to create CAS compute shader: 0x%08X", hr);
    }
    
    return true;  // Non-fatal - can encode without shaders
}

bool NvencEncoder::CreateIntermediateTextures(int width, int height) {
    for (int i = 0; i < 2; i++) {
        D3D11_TEXTURE2D_DESC desc = {};
        desc.Width = width;
        desc.Height = height;
        desc.MipLevels = 1;
        desc.ArraySize = 1;
        desc.Format = m_encodeTextureFormat; // F2/F3: match the source format exactly
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
        
        HRESULT hr = m_device->CreateTexture2D(&desc, nullptr, &m_intermediateTextures[i]);
        if (FAILED(hr)) {
            SetError("Failed to create intermediate texture %d", i);
            return false;
        }
        
        // Create SRV
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format = m_encodeTextureFormat;
        srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Texture2D.MipLevels = 1;
        
        hr = m_device->CreateShaderResourceView(m_intermediateTextures[i], &srvDesc, &m_intermediateSRV[i]);
        if (FAILED(hr)) return false;
        
        // Create UAV
        D3D11_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
        uavDesc.Format = m_encodeTextureFormat;
        uavDesc.ViewDimension = D3D11_UAV_DIMENSION_TEXTURE2D;
        uavDesc.Texture2D.MipSlice = 0;
        
        hr = m_device->CreateUnorderedAccessView(m_intermediateTextures[i], &uavDesc, &m_intermediateUAV[i]);
        if (FAILED(hr)) return false;
    }
    
    return true;
}

bool NvencEncoder::CreateBlueNoiseTexture() {
    // g_BlueNoise256x256R8 is defined in EmbeddedResources.h (65536 bytes = 256x256 R8)
    
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
    initData.SysMemPitch = 256;
    
    HRESULT hr = m_device->CreateTexture2D(&desc, &initData, &m_blueNoiseTexture);
    if (FAILED(hr)) {
        LogDebug("Failed to create blue noise texture: 0x%08X", hr);
        return false;
    }
    
    D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
    srvDesc.Format = DXGI_FORMAT_R8_UNORM;
    srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D;
    srvDesc.Texture2D.MipLevels = 1;
    
    hr = m_device->CreateShaderResourceView(m_blueNoiseTexture, &srvDesc, &m_blueNoiseSRV);
    if (FAILED(hr)) {
        LogDebug("Failed to create blue noise SRV: 0x%08X", hr);
        return false;
    }
    
    LogDebug("Blue noise texture created successfully (256x256 R8)");
    return true;
}

bool NvencEncoder::CreateConstantBuffers() {
    D3D11_BUFFER_DESC desc = {};
    desc.Usage = D3D11_USAGE_DYNAMIC;
    desc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    
    // CAS params (16 bytes)
    desc.ByteWidth = 16;
    if (FAILED(m_device->CreateBuffer(&desc, nullptr, &m_casParamsBuffer))) {
        return false;
    }
    
    return true;
}

bool NvencEncoder::HardSyncGPU(const char* stageName, DWORD timeoutMs) {
    // Fresh event query per sync: the wait semantics are "all GPU work issued so
    // far", which a new query preserves exactly, and per-use creation sidesteps
    // event-query re-issue behavior that wedged the DONOTFLUSH poll on driver
    // 595.97 (harness-verified: a re-issued query never resolved; fresh did).
    D3D11_QUERY_DESC queryDesc = {};
    queryDesc.Query = D3D11_QUERY_EVENT;
    ID3D11Query* query = nullptr;
    if (FAILED(m_device->CreateQuery(&queryDesc, &query)) || !query) {
        LogDebug("[NVENC] Failed to create sync query for %s", stageName);
        return false;
    }
    m_context->End(query);
    // Explicitly submit the command buffer: the DONOTFLUSH poll below never
    // flushes, and without a submission trigger the End() can sit in an
    // unsubmitted buffer indefinitely.
    m_context->Flush();

    DWORD startTime = GetTickCount();
    bool ok = true;
    while (S_FALSE == m_context->GetData(query, nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH)) {
        Sleep(1);
        if (GetTickCount() - startTime > timeoutMs) {
            LogDebug("[NVENC] GPU sync timeout in %s", stageName);
            ok = false;
            break;
        }
    }
    // Diagnostic (H-A vs H-B): resolution latency tells apart "event queries are
    // just slow in this quiet harness context" (H-A: harness-measured ~4.1s per
    // query on driver 595.97) from "queries stop resolving after EncodeNVENC"
    // (H-B: refuted — post-encode probe resolved at the same latency). Rate-limited
    // to the first 40 syncs so KSP captures (expected µs-latency) aren't log-spammed;
    // timeouts always log above.
    if (ok && m_syncDiagCount < 40) {
        m_syncDiagCount++;
        LogDebug("[NVENC] GPU sync %s resolved in %lu ms", stageName, (unsigned long)(GetTickCount() - startTime));
    }
    query->Release();
    return ok;
}

bool NvencEncoder::EncodeNVENC(int idx, int64_t frameIndex) {
    // Map input resource
    NV_ENC_MAP_INPUT_RESOURCE mapRes = {};
    mapRes.version = NV_ENC_MAP_INPUT_RESOURCE_VER;
    mapRes.registeredResource = m_registeredResources[idx];
    
    NVENCSTATUS status = m_nvencFunctions.nvEncMapInputResource(m_hEncoder, &mapRes);
    if (status != NV_ENC_SUCCESS) {
        SetError("nvEncMapInputResource failed: %s", NvencStatusToString(status));
        return false;
    }
    m_mappedInputs[idx] = mapRes.mappedResource;
    
    // Encode picture
    NV_ENC_PIC_PARAMS picParams = {};
    picParams.version = NV_ENC_PIC_PARAMS_VER;
    picParams.inputBuffer = m_mappedInputs[idx];
    picParams.outputBitstream = m_bitstreamBuffer;
    picParams.inputWidth = m_width;
    picParams.inputHeight = m_height;
    picParams.inputPitch = m_width; // F6: header prescribes inputWidth when pitch unknown
    picParams.bufferFmt = mapRes.mappedBufferFmt; // F2: use the mapped format, per header
    picParams.pictureStruct = NV_ENC_PIC_STRUCT_FRAME; // F5
    picParams.frameIdx = (uint32_t)frameIndex;
    
    status = m_nvencFunctions.nvEncEncodePicture(m_hEncoder, &picParams);
    
    // F7: only consume the bitstream when the encode actually produced output;
    // NEED_MORE_INPUT means the frame was buffered by the encoder (counted so the
    // EOS drain knows exactly how many packets remain - see F9 note in Shutdown).
    bool encodeOk;
    if (status == NV_ENC_SUCCESS) {
        encodeOk = ProcessOutput();
    } else if (status == NV_ENC_ERR_NEED_MORE_INPUT) {
        m_deferredFrames++;
        encodeOk = true;
    } else {
        SetError("nvEncEncodePicture failed: %s", NvencStatusToString(status));
        encodeOk = false;
    }
    
    // F4: unmap only after ProcessOutput has locked/consumed this frame's bitstream,
    // matching the header contract and all reference implementations.
    m_nvencFunctions.nvEncUnmapInputResource(m_hEncoder, m_mappedInputs[idx]);
    m_mappedInputs[idx] = nullptr;
    
    return encodeOk;
}

bool NvencEncoder::EncodeFrameWithCAS(ID3D11Texture2D* unityTexture, int64_t frameIndex, float sharpness) {
    int idx = m_bufferIndex;
    m_bufferIndex = 1 - idx;
    
    // Step 1: Copy Unity texture to intermediate[0]
    m_context->CopyResource(m_intermediateTextures[0], unityTexture);
    m_context->Flush();
    
    // Step 2: Run CAS shader
    // Update constant buffer
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(m_context->Map(m_casParamsBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        struct CASParams {
            float sharpness;
            float padding[3];
        } params = { sharpness, {0,0,0} };
        memcpy(mapped.pData, &params, sizeof(params));
        m_context->Unmap(m_casParamsBuffer, 0);
    }
    
    // Bind shader
    m_context->CSSetShader(m_casComputeShader, nullptr, 0);
    m_context->CSSetConstantBuffers(0, 1, &m_casParamsBuffer);
    m_context->CSSetShaderResources(0, 1, &m_intermediateSRV[0]);
    m_context->CSSetUnorderedAccessViews(0, 1, &m_intermediateUAV[1], nullptr);
    
    // Dispatch
    UINT dispatchX = (m_width + 15) / 16;
    UINT dispatchY = (m_height + 15) / 16;
    m_context->Dispatch(dispatchX, dispatchY, 1);
    
    // Hard sync
    if (!HardSyncGPU("CAS")) {
        SetError("CAS GPU sync timeout");
        LogDebug("EncodeFrameWithCAS frame %lld CAS GPU sync timeout", frameIndex);
        return false;
    }
    
    // Unbind (MANDATORY - prevents D3D11 resource hazards)
    ID3D11UnorderedAccessView* nullUAV[1] = { nullptr };
    ID3D11ShaderResourceView* nullSRV[1] = { nullptr };
    ID3D11Buffer* nullCB[1] = { nullptr };
    m_context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    m_context->CSSetShaderResources(0, 1, nullSRV);
    m_context->CSSetConstantBuffers(0, 1, nullCB);
    m_context->CSSetShader(nullptr, nullptr, 0);
    m_context->Flush();
    
    // Step 3: Copy result to encode texture
    m_context->CopyResource(m_encodeTextures[idx], m_intermediateTextures[1]);
    m_context->Flush();
    
    // Step 4: NVENC encode
    return EncodeNVENC(idx, frameIndex);
}

bool NvencEncoder::CreateAccumulationArray(int width, int height) {
    for (int i = 0; i < 2; i++) {
        D3D11_TEXTURE2D_DESC desc = {};
        desc.Width = width;
        desc.Height = height;
        desc.MipLevels = 1;
        desc.ArraySize = 8;  // 8 slices for sub-frames
        desc.Format = m_encodeTextureFormat; // F2/F3: match the source format exactly
        desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_DEFAULT;
        desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        
        HRESULT hr = m_device->CreateTexture2D(&desc, nullptr, &m_accumulationArray[i]);
        if (FAILED(hr)) {
            SetError("Failed to create accumulation array %d", i);
            return false;
        }
        
        D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Format = m_encodeTextureFormat;
        srvDesc.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2DARRAY;
        srvDesc.Texture2DArray.MipLevels = 1;
        srvDesc.Texture2DArray.ArraySize = 8;
        
        hr = m_device->CreateShaderResourceView(m_accumulationArray[i], &srvDesc, &m_accumulationSRV[i]);
        if (FAILED(hr)) {
            SetError("Failed to create accumulation SRV %d", i);
            return false;
        }
    }
    
    // Create weight buffer
    D3D11_BUFFER_DESC cbDesc = {};
    cbDesc.ByteWidth = 48;  // 8 floats weights + 1 float total + padding
    cbDesc.Usage = D3D11_USAGE_DYNAMIC;
    cbDesc.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
    cbDesc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
    
    if (FAILED(m_device->CreateBuffer(&cbDesc, nullptr, &m_tabWeightBuffer))) {
        SetError("Failed to create TAB weight buffer");
        return false;
    }
    
    return true;
}

bool NvencEncoder::SubmitSubFrame(ID3D11Texture2D* unityTexture, int sliceIndex) {
    if (!m_initialized || !unityTexture) return false;
    if (sliceIndex < 0 || sliceIndex >= 8) return false;
    if (!m_accumulationArray[m_currentAccumBuffer]) {
        SetError("TAB accumulation array not available (creation failed or device lost)");
        return false;
    }
    
    // CRITICAL: acquire TAB mutex FIRST (mirrors AMF path)
    std::lock_guard<std::mutex> lock(m_tabMutex);
    
    // Breadcrumb: first-slice received once ever
    if (sliceIndex == 0 && !m_tabFirstSliceReceived) {
        m_tabFirstSliceReceived = true;
        LogDebug("TAB first sub-frame slice 0 received");
    }
    
    // Frame-over-frame throttle: wait until previous frame's compute is done
    // before overwriting this accumulation buffer. Skip on the very first frame
    // (no prior compute exists to wait for).
    if (sliceIndex == 0 && m_tabFinalizeCount > 0) {
        if (!HardSyncGPU("TAB submit throttle", 5000)) {
            SetError("TAB submit throttle GPU sync timeout");
            return false;
        }
        if (m_tabFinalizeCount < 3) {
            LogDebug("SubmitSubFrame slice 0 frame-over-frame throttle complete");
        }
    }
    
    // Validate sub-frame order (prevents gaps in accumulation)
    if (sliceIndex != m_currentSubFrame) {
        SetError("Out-of-order sub-frame submission. Expected %d, got %d", m_currentSubFrame, sliceIndex);
        return false;
    }
    
    // Validate dimensions
    D3D11_TEXTURE2D_DESC srcDesc;
    unityTexture->GetDesc(&srcDesc);
    if (srcDesc.Width != (UINT)m_width || srcDesc.Height != (UINT)m_height) {
        SetError("Sub-frame dimension mismatch: expected %dx%d, got %ux%u",
                 m_width, m_height, srcDesc.Width, srcDesc.Height);
        return false;
    }
    
    // Copy to specific slice of current accumulation buffer
    UINT subResource = D3D11CalcSubresource(0, sliceIndex, 1);
    m_context->CopySubresourceRegion(
        m_accumulationArray[m_currentAccumBuffer],
        subResource,
        0, 0, 0,
        unityTexture,
        0,
        nullptr
    );
    
    // Only flush on last sub-frame to reduce driver overhead
    if (sliceIndex == m_tabSubFrameCount - 1) {
        m_context->Flush();
    }
    
    m_currentSubFrame++;
    return true;
}

void NvencEncoder::SetTabMode(bool enabled, int subFrameCount) {
    m_isTabMode = enabled;
    // I4: the accumulation array and the TAB shader are hardcoded to 8 slices;
    // clamp so CR_NvencSetTabMode cannot over-index either.
    if (subFrameCount < 1) subFrameCount = 1;
    if (subFrameCount > 8) subFrameCount = 8;
    m_tabSubFrameCount = subFrameCount;
    m_currentAccumBuffer = 0;
    m_currentSubFrame = 0;
}

bool NvencEncoder::FinalizeTemporalFrame(int64_t frameIndex, float sharpness) {
    // I3: all TAB resources are non-fatal creations - fail loudly if any are
    // missing instead of dereferencing null.
    if (!m_tabWeightBuffer || !m_tabComputeShader ||
        !m_accumulationArray[m_currentAccumBuffer] || !m_accumulationSRV[m_currentAccumBuffer] ||
        !m_intermediateUAV[0] || !m_intermediateSRV[0]) {
        SetError("TAB resources not available (creation failed or device lost)");
        return false;
    }

    // CRITICAL: acquire TAB mutex FIRST (mirrors AMF path)
    std::lock_guard<std::mutex> lock(m_tabMutex);

    int idx = m_bufferIndex;
    m_bufferIndex = 1 - idx;
    
    if (m_tabFinalizeCount < 3) {
        LogDebug("FinalizeTemporalFrame entry frame %lld", frameIndex);
    }
    
    // Validate sub-frame count and reset immediately so a failure can't poison the next frame
    int submittedSubFrames = m_currentSubFrame;
    m_currentSubFrame = 0;
    if (submittedSubFrames != m_tabSubFrameCount) {
        SetError("Finalize called with %d/%d sub-frames", submittedSubFrames, m_tabSubFrameCount);
        return false;
    }
    
    // HARD SYNC: ensure all sub-frame copies are complete before compute reads the array
    if (!HardSyncGPU("TAB pre-dispatch", 5000)) {
        SetError("TAB pre-dispatch GPU sync timeout");
        LogDebug("FinalizeTemporalFrame frame %lld TAB pre-dispatch sync timeout", frameIndex);
        return false;
    }
    if (m_tabFinalizeCount < 3) {
        LogDebug("FinalizeTemporalFrame frame %lld post-pre-dispatch-sync", frameIndex);
    }
    
    // Step 1: Run TAB shader to average accumulated sub-frames
    // Update weight buffer with Gaussian weights
    D3D11_MAPPED_SUBRESOURCE mapped;
    if (SUCCEEDED(m_context->Map(m_tabWeightBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
        // Simple uniform weights for now (can be made Gaussian later)
        float* weights = (float*)mapped.pData;
        int count = m_tabSubFrameCount > 0 ? m_tabSubFrameCount : 8;
        float weight = 1.0f / count;
        for (int i = 0; i < 8; i++) {
            weights[i] = (i < count) ? weight : 0.0f;
        }
        weights[8] = 1.0f;  // Total weight
        m_context->Unmap(m_tabWeightBuffer, 0);
    }
    
    // Bind TAB shader
    m_context->CSSetShader(m_tabComputeShader, nullptr, 0);
    m_context->CSSetConstantBuffers(0, 1, &m_tabWeightBuffer);
    m_context->CSSetShaderResources(0, 1, &m_accumulationSRV[m_currentAccumBuffer]);
    m_context->CSSetUnorderedAccessViews(0, 1, &m_intermediateUAV[0], nullptr);
    
    // Dispatch
    UINT dispatchX = (m_width + 15) / 16;
    UINT dispatchY = (m_height + 15) / 16;
    m_context->Dispatch(dispatchX, dispatchY, 1);
    
    if (m_tabFinalizeCount < 3) {
        LogDebug("FinalizeTemporalFrame frame %lld post-TAB-dispatch", frameIndex);
    }
    
    // Hard sync
    if (!HardSyncGPU("TAB")) {
        SetError("TAB GPU sync timeout");
        LogDebug("FinalizeTemporalFrame frame %lld TAB GPU sync timeout", frameIndex);
        return false;
    }
    if (m_tabFinalizeCount < 3) {
        LogDebug("FinalizeTemporalFrame frame %lld post-TAB-sync", frameIndex);
    }
    
    // Unbind TAB resources
    ID3D11UnorderedAccessView* nullUAV[1] = { nullptr };
    ID3D11ShaderResourceView* nullSRV[1] = { nullptr };
    ID3D11Buffer* nullCB[1] = { nullptr };
    m_context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
    m_context->CSSetShaderResources(0, 1, nullSRV);
    m_context->CSSetConstantBuffers(0, 1, nullCB);
    
    // Step 2: Run CAS if enabled
    int outputIdx = 0;
    if (sharpness > 0.0f && m_casComputeShader) {
        if (!m_casParamsBuffer) {
            SetError("CAS params buffer not available (creation failed or device lost)");
            return false;
        }
        // Update CAS params
        if (SUCCEEDED(m_context->Map(m_casParamsBuffer, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) {
            struct CASParams {
                float sharpness;
                float padding[3];
            } params = { sharpness, {0,0,0} };
            memcpy(mapped.pData, &params, sizeof(params));
            m_context->Unmap(m_casParamsBuffer, 0);
        }
        
        // Bind CAS shader
        m_context->CSSetShader(m_casComputeShader, nullptr, 0);
        m_context->CSSetConstantBuffers(0, 1, &m_casParamsBuffer);
        m_context->CSSetShaderResources(0, 1, &m_intermediateSRV[0]);
        m_context->CSSetUnorderedAccessViews(0, 1, &m_intermediateUAV[1], nullptr);
        
        m_context->Dispatch(dispatchX, dispatchY, 1);
        if (m_tabFinalizeCount < 3) {
            LogDebug("FinalizeTemporalFrame frame %lld post-CAS-dispatch", frameIndex);
        }
        
        if (!HardSyncGPU("CAS")) {
            SetError("CAS GPU sync timeout");
            LogDebug("FinalizeTemporalFrame frame %lld CAS GPU sync timeout", frameIndex);
            return false;
        }
        if (m_tabFinalizeCount < 3) {
            LogDebug("FinalizeTemporalFrame frame %lld post-CAS-sync", frameIndex);
        }
        
        // Unbind CAS resources
        m_context->CSSetUnorderedAccessViews(0, 1, nullUAV, nullptr);
        m_context->CSSetShaderResources(0, 1, nullSRV);
        m_context->CSSetConstantBuffers(0, 1, nullCB);
        
        outputIdx = 1;  // CAS output is in intermediate[1]
    }
    
    // Unbind shader
    m_context->CSSetShader(nullptr, nullptr, 0);
    m_context->Flush();
    
    // Step 3: Copy result to encode texture
    m_context->CopyResource(m_encodeTextures[idx], m_intermediateTextures[outputIdx]);
    m_context->Flush();
    
    if (m_tabFinalizeCount < 3) {
        LogDebug("FinalizeTemporalFrame frame %lld post-copy-to-encode-texture", frameIndex);
    }
    
    // Step 4: NVENC encode
    bool encodeOk = EncodeNVENC(idx, frameIndex);
    if (!encodeOk || m_tabFinalizeCount < 3) {
        LogDebug("FinalizeTemporalFrame frame %lld EncodeNVENC %s", frameIndex, encodeOk ? "success" : "failed");
    }
    // DIAGNOSTIC (H-B probe): does an event query issued immediately after
    // EncodeNVENC still resolve? Non-fatal, first 3 frames only — a stall here
    // pinpoints the encode as what poisons subsequent syncs on this queue.
    if (encodeOk && m_tabFinalizeCount < 3) {
        if (!HardSyncGPU("post-encode probe", 5000)) {
            LogDebug("FinalizeTemporalFrame frame %lld post-encode probe sync timeout (H-B signature)", frameIndex);
        }
    }
    if (encodeOk) {
        // CRITICAL: toggle accumulation buffer for next frame (prevents resource hazards)
        m_currentAccumBuffer = 1 - m_currentAccumBuffer;
        
        m_tabFinalizeCount++;
        if (m_tabFinalizeCount == 3) {
            LogDebug("breadcrumb logging off, TAB steady-state");
        }
    }
    return encodeOk;
}

void NvencEncoder::Shutdown() {
    // F16: idempotent - safe for the explicit + destructor double-call
    if (!m_initialized && !m_hEncoder && !m_formatContext && !m_device) return;
    LogDebug("Shutting down NVENC encoder");
    
    // Restore D3D11 multithread protection to its original state if we changed it.
    // Do this early while m_context is still valid.
    if (m_multithreadProtectionActive && m_multithread) {
        ID3D11MultithreadLocal* mt = static_cast<ID3D11MultithreadLocal*>(m_multithread);
        LogDebug("D3D11 multithread protection restoring to %s", m_prevMultithreadProtected ? "TRUE" : "FALSE");
        mt->SetMultithreadProtected(m_prevMultithreadProtected);
        mt->Release();
        m_multithread = nullptr;
        m_multithreadProtectionActive = false;
    }
    
    // Cleanup compute shaders
    if (m_tabComputeShader) { 
        m_tabComputeShader->Release(); 
        m_tabComputeShader = nullptr; 
    }
    if (m_casComputeShader) {
        m_casComputeShader->Release();
        m_casComputeShader = nullptr;
    }

    // Cleanup intermediate textures
    for (int i = 0; i < 2; i++) {
        if (m_intermediateUAV[i]) { m_intermediateUAV[i]->Release(); m_intermediateUAV[i] = nullptr; }
        if (m_intermediateSRV[i]) { m_intermediateSRV[i]->Release(); m_intermediateSRV[i] = nullptr; }
        if (m_intermediateTextures[i]) { m_intermediateTextures[i]->Release(); m_intermediateTextures[i] = nullptr; }
    }

    // Cleanup blue noise
    if (m_blueNoiseSRV) { m_blueNoiseSRV->Release(); m_blueNoiseSRV = nullptr; }
    if (m_blueNoiseTexture) { m_blueNoiseTexture->Release(); m_blueNoiseTexture = nullptr; }

    // Cleanup constant buffers
    if (m_casParamsBuffer) { m_casParamsBuffer->Release(); m_casParamsBuffer = nullptr; }
    
    // Cleanup accumulation array
    for (int i = 0; i < 2; i++) {
        if (m_accumulationSRV[i]) { m_accumulationSRV[i]->Release(); m_accumulationSRV[i] = nullptr; }
        if (m_accumulationArray[i]) { m_accumulationArray[i]->Release(); m_accumulationArray[i] = nullptr; }
    }
    if (m_tabWeightBuffer) { m_tabWeightBuffer->Release(); m_tabWeightBuffer = nullptr; }
    
    if (m_hEncoder) {
        // Flush encoder
        NV_ENC_PIC_PARAMS picParams = {};
        picParams.version = NV_ENC_PIC_PARAMS_VER;
        picParams.encodePicFlags = NV_ENC_PIC_FLAG_EOS;
        m_nvencFunctions.nvEncEncodePicture(m_hEncoder, &picParams);
        
        // Drain deferred frames. F9: nvEncLockBitstream SPINS (100% CPU) when called
        // with no output pending (probe-verified on RTX 3050, driver 595.97 -
        // nvprobe3 D2/D6), so the drain must lock exactly once per deferred
        // (NEED_MORE_INPUT) frame and never more. In the current sync,
        // no-B-frame config the count is 0 and this loop is a no-op: every frame's
        // packet was already consumed per-frame, and EOS produces no new output.
        for (int i = 0; i < m_deferredFrames; i++) {
            bool wrote = false;
            if (!ProcessOutput(&wrote) || !wrote) break; // LOCK_BUSY/error: stop, never spin
        }
        if (m_deferredFrames > 0)
            LogDebug("EOS drain: %d deferred frames flushed", m_deferredFrames);
        m_deferredFrames = 0;
        
        // Unregister resources
        for (int i = 0; i < 2; i++) {
            if (m_registeredResources[i]) {
                m_nvencFunctions.nvEncUnregisterResource(m_hEncoder, m_registeredResources[i]);
                m_registeredResources[i] = nullptr;
            }
            if (m_encodeTextures[i]) {
                m_encodeTextures[i]->Release();
                m_encodeTextures[i] = nullptr;
            }
        }
        
        // Destroy bitstream buffer - NOTE: This takes the pointer directly, not a struct
        if (m_bitstreamBuffer) {
            m_nvencFunctions.nvEncDestroyBitstreamBuffer(m_hEncoder, m_bitstreamBuffer);
            m_bitstreamBuffer = nullptr;
        }
        
        m_nvencFunctions.nvEncDestroyEncoder(m_hEncoder);
        m_hEncoder = nullptr;
    }
    
    if (m_formatContext) {
        // Only write the trailer when the header was written - av_write_trailer on a
        // context whose avformat_write_header failed (or never ran) dereferences
        // uninitialized muxer state and crashes (WER: AV in avformat-59.dll).
        if (m_headerWritten)
            av_write_trailer((AVFormatContext*)m_formatContext);
        if (!(((AVFormatContext*)m_formatContext)->oformat->flags & AVFMT_NOFILE))
            avio_closep(&((AVFormatContext*)m_formatContext)->pb);
        avformat_free_context((AVFormatContext*)m_formatContext);
        m_formatContext = nullptr;
        m_headerWritten = false;
    }
    
    if (m_context) {
        m_context->Release();
        m_context = nullptr;
    }
    if (m_device) {
        m_device->Release();
        m_device = nullptr;
    }
    if (m_unityDevice) {
        m_unityDevice->Release();
        m_unityDevice = nullptr;
    }
    
    if (m_hNvencLib) {
        FreeLibrary(m_hNvencLib);
        m_hNvencLib = nullptr;
    }
    
    m_initialized = false;
    LogDebug("Shutdown complete");
}

// =============================================================================
// C Exports for C# Interop (NVENC)
// =============================================================================

// Link to global error buffer defined in CinematicRecorderNative.cpp (AMF module)
extern thread_local char g_errorBuffer[1024];

extern "C" {

__declspec(dllexport) void* CR_InitNvencEncoder(
    ID3D11Device* unityDevice,
    ID3D11Texture2D* textureHint,
    int width,
    int height,
    int fps,
    const char* outputPath,
    const NvencEncoderSettings* settings)
{
    if (!settings) {
        strncpy_s(g_errorBuffer, sizeof(g_errorBuffer), "[NVENC] Null settings pointer", _TRUNCATE);
        return nullptr;
    }
    
    NvencEncoder* encoder = new NvencEncoder();
    if (!encoder->Initialize(unityDevice, textureHint, width, height, fps, outputPath, *settings)) {
        strcpy_s(g_errorBuffer, sizeof(g_errorBuffer), encoder->GetError());
        delete encoder;
        return nullptr;
    }
    
    return encoder;
}

__declspec(dllexport) void* CR_InitNvencEncoderFromTexture(
    ID3D11Texture2D* texture,
    int width,
    int height,
    int fps,
    const char* outputPath,
    const NvencEncoderSettings* settings)
{
    if (!texture) {
        strncpy_s(g_errorBuffer, sizeof(g_errorBuffer), "[NVENC] Null D3D11 texture", _TRUNCATE);
        return nullptr;
    }
    
    ID3D11Device* device = nullptr;
    texture->GetDevice(&device);
    if (!device) {
        strncpy_s(g_errorBuffer, sizeof(g_errorBuffer), "[NVENC] Failed to get device from texture", _TRUNCATE);
        return nullptr;
    }
    
    void* result = CR_InitNvencEncoder(device, texture, width, height, fps, outputPath, settings);
    device->Release();
    return result;
}

__declspec(dllexport) int CR_EncodeNvencFrame(
    void* encoderHandle,
    ID3D11Texture2D* texture,
    long long frameIndex)
{
    if (!encoderHandle) {
        strncpy_s(g_errorBuffer, sizeof(g_errorBuffer), "[NVENC] Null encoder handle", _TRUNCATE);
        return -1;
    }
    
    NvencEncoder* encoder = static_cast<NvencEncoder*>(encoderHandle);
    if (!encoder->EncodeFrame(texture, frameIndex)) {
        strcpy_s(g_errorBuffer, sizeof(g_errorBuffer), encoder->GetError());
        return -1;
    }
    
    return 0;
}

__declspec(dllexport) int CR_ShutdownNvencEncoder(void* encoderHandle)
{
    if (!encoderHandle)
        return 0;
        
    NvencEncoder* encoder = static_cast<NvencEncoder*>(encoderHandle);
    encoder->Shutdown();
    delete encoder;
    return 0;
}

__declspec(dllexport) int CR_NvencSubmitSubFrame(
    void* encoderHandle,
    ID3D11Texture2D* texture,
    int sliceIndex)
{
    if (!encoderHandle) {
        strncpy_s(g_errorBuffer, sizeof(g_errorBuffer), "[NVENC] Null encoder handle", _TRUNCATE);
        return -1;
    }
    NvencEncoder* encoder = static_cast<NvencEncoder*>(encoderHandle);
    if (!encoder->SubmitSubFrame(texture, sliceIndex)) {
        strcpy_s(g_errorBuffer, sizeof(g_errorBuffer), encoder->GetError());
        return -1;
    }
    return 0;
}

__declspec(dllexport) int CR_NvencFinalizeTemporalFrame(
    void* encoderHandle,
    long long frameIndex,
    float sharpness)
{
    if (!encoderHandle) {
        strncpy_s(g_errorBuffer, sizeof(g_errorBuffer), "[NVENC] Null encoder handle", _TRUNCATE);
        return -1;
    }
    NvencEncoder* encoder = static_cast<NvencEncoder*>(encoderHandle);
    if (!encoder->FinalizeTemporalFrame(frameIndex, sharpness)) {
        strcpy_s(g_errorBuffer, sizeof(g_errorBuffer), encoder->GetError());
        return -1;
    }
    return 0;
}

__declspec(dllexport) int CR_NvencSetTabMode(
    void* encoderHandle,
    int enabled,
    int subFrameCount)
{
    if (!encoderHandle) {
        strncpy_s(g_errorBuffer, sizeof(g_errorBuffer), "[NVENC] Null encoder handle", _TRUNCATE);
        return -1;
    }
    NvencEncoder* encoder = static_cast<NvencEncoder*>(encoderHandle);
    encoder->SetTabMode(enabled != 0, subFrameCount);
    return 0;
}

} // extern "C"