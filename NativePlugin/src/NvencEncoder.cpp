#include "NvencEncoder.h"
#include <cstdio>
#include <cstdarg>
#include <cstring>
#include <mutex>

extern "C" {
#include <libavformat/avformat.h>
#include <libavcodec/avcodec.h>
#include <libavutil/opt.h>
}

#include <dxgi1_2.h>
#pragma comment(lib, "dxgi.lib")

// Typedef for the create instance function pointer (not defined in header)
typedef NVENCSTATUS (NVENCAPI *NVENCAPICREATEINSTANCEPROC)(NV_ENCODE_API_FUNCTION_LIST *);

NvencEncoder::NvencEncoder()
    : m_hEncoder(nullptr), m_hNvencLib(nullptr), m_device(nullptr), m_context(nullptr),
      m_unityDevice(nullptr), m_usingSharedDevice(false), m_bufferIndex(0),
      m_bitstreamBuffer(nullptr), m_formatContext(nullptr), m_videoStream(nullptr),
      m_frameCount(0), m_width(0), m_height(0), m_fps(0), m_initialized(false), m_isHEVC(false) {
    
    memset(m_encodeTextures, 0, sizeof(m_encodeTextures));
    memset(m_sharedHandles, 0, sizeof(m_sharedHandles));
    memset(m_registeredResources, 0, sizeof(m_registeredResources));
    memset(m_mappedInputs, 0, sizeof(m_mappedInputs));
    memset(m_errorBuffer, 0, sizeof(m_errorBuffer));
    memset(&m_nvencFunctions, 0, sizeof(m_nvencFunctions));
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
    
    m_hNvencLib = LoadLibraryA("nvEncodeAPI64.dll");
    if (!m_hNvencLib) {
        SetError("Failed to load nvEncodeAPI64.dll (Error: %lu)", GetLastError());
        return false;
    }
    
    auto createInstance = (NVENCAPICREATEINSTANCEPROC)GetProcAddress(m_hNvencLib, "NvEncodeAPICreateInstance");
    if (!createInstance) {
        SetError("Failed to get NvEncodeAPICreateInstance");
        return false;
    }
    
    m_nvencFunctions.version = NV_ENCODE_API_FUNCTION_LIST_VER;
    NVENCSTATUS status = createInstance(&m_nvencFunctions);
    
    if (status != NV_ENC_SUCCESS) {
        SetError("NvEncodeAPICreateInstance failed: %s", NvencStatusToString(status));
        return false;
    }
    
    LogDebug("NVENC API loaded successfully, version %u", m_nvencFunctions.version);
    return true;
}

bool NvencEncoder::ValidateOrCreateDevice(ID3D11Device* unityDevice, ID3D11Texture2D* textureHint) {
    if (!unityDevice) {
        SetError("Unity device is null");
        return false;
    }
    
    m_unityDevice = unityDevice;
    m_unityDevice->AddRef();
    
    // Try direct device usage first by attempting to create a session
    {
        NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS params = {};
        params.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
        params.deviceType = NV_ENC_DEVICE_TYPE_DIRECTX;
        params.device = unityDevice;
        params.apiVersion = NVENCAPI_VERSION;
        
        void* testEncoder = nullptr;
        NVENCSTATUS status = m_nvencFunctions.nvEncOpenEncodeSessionEx(&params, &testEncoder);
        if (status == NV_ENC_SUCCESS) {
            LogDebug("Direct Unity device usage successful");
            m_nvencFunctions.nvEncDestroyEncoder(testEncoder);
            
            m_device = unityDevice;
            m_device->AddRef();
            m_device->GetImmediateContext(&m_context);
            m_usingSharedDevice = false;
            return true;
        }
        LogDebug("Direct device failed (%s), creating secondary device", NvencStatusToString(status));
    }
    
    // Create secondary device on same adapter
    IDXGIDevice* dxgiDevice = nullptr;
    HRESULT hr = unityDevice->QueryInterface(__uuidof(IDXGIDevice), (void**)&dxgiDevice);
    if (FAILED(hr)) {
        SetError("Failed to get IDXGIDevice from Unity device");
        return false;
    }
    
    IDXGIAdapter* adapter = nullptr;
    hr = dxgiDevice->GetAdapter(&adapter);
    dxgiDevice->Release();
    
    if (FAILED(hr)) {
        SetError("Failed to get adapter from Unity device");
        return false;
    }
    
    DXGI_ADAPTER_DESC adapterDesc;
    adapter->GetDesc(&adapterDesc);
    LogDebug("Creating secondary device on adapter: %S", adapterDesc.Description);
    
    D3D11_CREATE_DEVICE_FLAG createFlags = (D3D11_CREATE_DEVICE_FLAG)(
        D3D11_CREATE_DEVICE_VIDEO_SUPPORT | 
        D3D11_CREATE_DEVICE_BGRA_SUPPORT
    );
    
    D3D_FEATURE_LEVEL featureLevels[] = { D3D_FEATURE_LEVEL_11_0 };
    D3D_FEATURE_LEVEL level;
    
    hr = D3D11CreateDevice(
        adapter,
        D3D_DRIVER_TYPE_UNKNOWN,
        nullptr,
        createFlags,
        featureLevels,
        1,
        D3D11_SDK_VERSION,
        &m_device,
        &level,
        &m_context
    );
    
    adapter->Release();
    
    if (FAILED(hr)) {
        SetError("Failed to create video-support device (0x%08X)", hr);
        return false;
    }
    
    LogDebug("Secondary device created with VIDEO_SUPPORT, Feature Level: %d", level);
    m_usingSharedDevice = true;
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
    LogDebug("Encode session opened");
    
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
    
    // Configure
    NV_ENC_CONFIG encodeConfig = {};
    encodeConfig.version = NV_ENC_CONFIG_VER;
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
        encodeConfig.rcParams.rateControlMode = NV_ENC_PARAMS_RC_VBR;
        encodeConfig.rcParams.averageBitRate = settings.TargetBitrateKbps * 1000;
        encodeConfig.rcParams.maxBitRate = settings.TargetBitrateKbps * 1000;
        encodeConfig.rcParams.vbvBufferSize = (settings.TargetBitrateKbps * 1000) / m_fps;
        LogDebug("VBR Settings: %d kbps", settings.TargetBitrateKbps);
    }
    
    // Codec specific settings - NOTE: These structs don't have version fields
    if (m_isHEVC) {
        encodeConfig.encodeCodecConfig.hevcConfig.idrPeriod = settings.GopSize;
        encodeConfig.encodeCodecConfig.hevcConfig.chromaFormatIDC = 1;
    } else {
        encodeConfig.encodeCodecConfig.h264Config.idrPeriod = settings.GopSize;
        encodeConfig.encodeCodecConfig.h264Config.repeatSPSPPS = 1;
    }
    
    initParams.encodeConfig = &encodeConfig;
    
    status = m_nvencFunctions.nvEncInitializeEncoder(m_hEncoder, &initParams);
    if (status != NV_ENC_SUCCESS) {
        SetError("nvEncInitializeEncoder failed: %s", NvencStatusToString(status));
        return false;
    }
    LogDebug("Encoder initialized successfully");
    
    // Create bitstream buffer
    NV_ENC_CREATE_BITSTREAM_BUFFER bitstreamBuffer = {};
    bitstreamBuffer.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
    status = m_nvencFunctions.nvEncCreateBitstreamBuffer(m_hEncoder, &bitstreamBuffer);
    if (status != NV_ENC_SUCCESS) {
        SetError("nvEncCreateBitstreamBuffer failed: %s", NvencStatusToString(status));
        return false;
    }
    m_bitstreamBuffer = bitstreamBuffer.bitstreamBuffer;
    LogDebug("Bitstream buffer created");
    
    // Create encode textures
    D3D11_TEXTURE2D_DESC texDesc = {};
    texDesc.Width = m_width;
    texDesc.Height = m_height;
    texDesc.MipLevels = 1;
    texDesc.ArraySize = 1;
    texDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    texDesc.SampleDesc.Count = 1;
    texDesc.Usage = D3D11_USAGE_DEFAULT;
    texDesc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
    
    if (m_usingSharedDevice) {
        texDesc.MiscFlags = D3D11_RESOURCE_MISC_SHARED;
    }
    
    for (int i = 0; i < 2; i++) {
        HRESULT hr = m_device->CreateTexture2D(&texDesc, nullptr, &m_encodeTextures[i]);
        if (FAILED(hr)) {
            SetError("Failed to create encode texture %d (0x%08X)", i, hr);
            return false;
        }
        
        if (m_usingSharedDevice) {
            IDXGIResource* dxgiRes = nullptr;
            hr = m_encodeTextures[i]->QueryInterface(__uuidof(IDXGIResource), (void**)&dxgiRes);
            if (SUCCEEDED(hr)) {
                dxgiRes->GetSharedHandle(&m_sharedHandles[i]);
                dxgiRes->Release();
                LogDebug("Texture %d shared handle: %p", i, m_sharedHandles[i]);
            }
        }
        
        // Register with NVENC
        NV_ENC_REGISTER_RESOURCE regRes = {};
        regRes.version = NV_ENC_REGISTER_RESOURCE_VER;
        regRes.resourceType = NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX;
        regRes.width = m_width;
        regRes.height = m_height;
        regRes.pitch = 0;
        regRes.subResourceIndex = 0;
        regRes.resourceToRegister = m_encodeTextures[i];
        regRes.bufferFormat = NV_ENC_BUFFER_FORMAT_ARGB;
        regRes.bufferUsage = NV_ENC_INPUT_IMAGE;
        
        status = m_nvencFunctions.nvEncRegisterResource(m_hEncoder, &regRes);
        if (status != NV_ENC_SUCCESS) {
            SetError("nvEncRegisterResource failed for buffer %d: %s", i, NvencStatusToString(status));
            return false;
        }
        m_registeredResources[i] = regRes.registeredResource;
        LogDebug("Registered resource %d: %p", i, m_registeredResources[i]);
    }
    
    return true;
}

bool NvencEncoder::InitializeFFmpeg(const char* outputPath) {
    LogDebug("Initializing FFmpeg muxer: %s", outputPath);
    
    const AVOutputFormat* fmt = av_guess_format("matroska", nullptr, nullptr);
    if (!fmt) {
        SetError("MKV muxer not found");
        return false;
    }
    
    AVFormatContext* ctx = nullptr;
    if (avformat_alloc_output_context2(&ctx, fmt, nullptr, outputPath) < 0) {
        SetError("avformat_alloc_output_context2 failed");
        return false;
    }
    m_formatContext = ctx;
    
    AVStream* stream = avformat_new_stream(ctx, nullptr);
    if (!stream) {
        SetError("avformat_new_stream failed");
        return false;
    }
    m_videoStream = stream;
    
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
        if (avio_open(&ctx->pb, outputPath, AVIO_FLAG_WRITE) < 0) {
            SetError("avio_open failed for %s", outputPath);
            return false;
        }
    }
    
    if (avformat_write_header(ctx, nullptr) < 0) {
        SetError("avformat_write_header failed");
        return false;
    }
    
    LogDebug("FFmpeg muxer ready");
    return true;
}

bool NvencEncoder::Initialize(ID3D11Device* unityDevice, ID3D11Texture2D* textureHint,
                              int width, int height, int fps, const char* outputPath,
                              const NvencEncoderSettings& settings) {
    m_width = width;
    m_height = height;
    m_fps = fps;
    
    if (!LoadNvencLibrary()) return false;
    if (!ValidateOrCreateDevice(unityDevice, textureHint)) return false;
    if (!InitializeEncoder(settings)) return false;
    if (!InitializeFFmpeg(outputPath)) return false;
    
    m_initialized = true;
    LogDebug("NVENC Encoder fully initialized and ready");
    return true;
}

bool NvencEncoder::EncodeFrame(ID3D11Texture2D* unityTexture, int64_t frameIndex) {
    if (!m_initialized) return false;
    
    int idx = m_bufferIndex;
    m_bufferIndex = 1 - idx;
    
    LogDebug("Frame %lld - Processing buffer %d", frameIndex, idx);
    
    // Copy from Unity texture to our encode texture
    if (m_usingSharedDevice && m_unityDevice && unityTexture) {
        ID3D11Texture2D* sharedTex = nullptr;
        HRESULT hr = m_unityDevice->OpenSharedResource(m_sharedHandles[idx], 
                                                      __uuidof(ID3D11Texture2D), 
                                                      (void**)&sharedTex);
        if (SUCCEEDED(hr) && sharedTex) {
            ID3D11DeviceContext* unityCtx = nullptr;
            m_unityDevice->GetImmediateContext(&unityCtx);
            if (unityCtx) {
                unityCtx->CopyResource(sharedTex, unityTexture);
                unityCtx->Flush();
                unityCtx->Release();
            }
            sharedTex->Release();
            
            // GPU-GPU copy from shared to encode texture on NVENC context
            // FIX: Copy from sharedTex (which we just copied Unity content to) to our encode texture
            // Actually, sharedTex is the same as m_encodeTextures[idx] since we opened the shared handle
            // So we don't need to copy here - the data is already in m_encodeTextures[idx] via the shared handle
            // But we need to ensure the copy from Unity to sharedTex is synced, which we did with Flush()
        } else {
            LogDebug("Failed to open shared resource, falling back to CPU copy (slow)");
            SetError("Shared resource open failed");
            return false;
        }
    } else if (unityTexture) {
        m_context->CopyResource(m_encodeTextures[idx], unityTexture);
        m_context->Flush();
        LogDebug("Frame %lld - CopyResource completed", frameIndex);
    }
    
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
    LogDebug("Frame %lld - Mapped input: %p, Format: %d", frameIndex, mapRes.mappedResource, mapRes.mappedBufferFmt);
    
    // Encode picture
    NV_ENC_PIC_PARAMS picParams = {};
    picParams.version = NV_ENC_PIC_PARAMS_VER;
    picParams.inputBuffer = m_mappedInputs[idx];
    picParams.outputBitstream = m_bitstreamBuffer;
    picParams.inputWidth = m_width;
    picParams.inputHeight = m_height;
    picParams.bufferFmt = NV_ENC_BUFFER_FORMAT_ARGB;
    picParams.frameIdx = (uint32_t)frameIndex;
    
    status = m_nvencFunctions.nvEncEncodePicture(m_hEncoder, &picParams);
    
    // Unmap immediately after encode
    m_nvencFunctions.nvEncUnmapInputResource(m_hEncoder, m_mappedInputs[idx]);
    m_mappedInputs[idx] = nullptr;
    
    if (status != NV_ENC_SUCCESS && status != NV_ENC_ERR_NEED_MORE_INPUT) {
        SetError("nvEncEncodePicture failed: %s", NvencStatusToString(status));
        return false;
    }
    LogDebug("Frame %lld - EncodePicture submitted", frameIndex);
    
    return ProcessOutput();
}

bool NvencEncoder::ProcessOutput() {
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
    
    LogDebug("Bitstream locked: %u bytes, Frame %d", lockBS.bitstreamSizeInBytes, lockBS.frameIdx);
    
    AVPacket pkt = {};
    av_init_packet(&pkt);
    pkt.data = (uint8_t*)lockBS.bitstreamBufferPtr;
    pkt.size = lockBS.bitstreamSizeInBytes;
    pkt.pts = m_frameCount;
    pkt.dts = m_frameCount;
    pkt.duration = 1;
    pkt.stream_index = ((AVStream*)m_videoStream)->index;
    
    // Keyframe detection
    if (pkt.size > 4) {
        uint8_t* data = pkt.data;
        int offset = 0;
        while (offset < pkt.size - 4) {
            if (data[offset] == 0 && data[offset+1] == 0 && 
                ((data[offset+2] == 1) || (data[offset+2] == 0 && data[offset+3] == 1))) {
                int start = (data[offset+2] == 1) ? offset+3 : offset+4;
                uint8_t nalType = data[start] & (m_isHEVC ? 0x7E : 0x1F);
                if (m_isHEVC) {
                    if (nalType == 19 || nalType == 20 || nalType == 21)
                        pkt.flags |= AV_PKT_FLAG_KEY;
                } else {
                    if (nalType == 5)
                        pkt.flags |= AV_PKT_FLAG_KEY;
                }
                break;
            }
            offset++;
        }
    }
    
    AVRational tb = { 1, m_fps };
    av_packet_rescale_ts(&pkt, tb, ((AVStream*)m_videoStream)->time_base);
    
    {
        static std::mutex writeMutex;
        std::lock_guard<std::mutex> lock(writeMutex);
        av_interleaved_write_frame((AVFormatContext*)m_formatContext, &pkt);
    }
    
    m_frameCount++;
    
    m_nvencFunctions.nvEncUnlockBitstream(m_hEncoder, m_bitstreamBuffer);
    return true;
}

void NvencEncoder::Shutdown() {
    LogDebug("Shutting down NVENC encoder");
    
    if (m_hEncoder) {
        // Flush encoder
        NV_ENC_PIC_PARAMS picParams = {};
        picParams.version = NV_ENC_PIC_PARAMS_VER;
        picParams.encodePicFlags = NV_ENC_PIC_FLAG_EOS;
        m_nvencFunctions.nvEncEncodePicture(m_hEncoder, &picParams);
        
        // Process remaining output
        while (ProcessOutput()) {}
        
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
        av_write_trailer((AVFormatContext*)m_formatContext);
        if (!(((AVFormatContext*)m_formatContext)->oformat->flags & AVFMT_NOFILE))
            avio_closep(&((AVFormatContext*)m_formatContext)->pb);
        avformat_free_context((AVFormatContext*)m_formatContext);
        m_formatContext = nullptr;
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