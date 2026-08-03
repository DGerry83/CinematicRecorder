#pragma once
#include "../nvenc/nvEncodeAPI.h"
#include <windows.h>
#include <d3d11.h>
#include <cstdint>
#include <string>
#include <mutex>
#include <vector>
#include <utility>

// Settings struct (matches C# layout exactly) - 56 bytes total
struct NvencEncoderSettings {
    int RateControlMode;      // 0=CQP, 1=VBR, 2=CBR
    int TargetBitrateKbps;    // Target bitrate in kbps for VBR/CBR
    int QpI;                  // QP for I-frames (0-51)
    int QpP;                  // QP for P-frames (0-51)
    int QpB;                  // QP for B-frames (0-51)
    int QualityPreset;        // 0=P1(Speed), 1=P4(Balanced), 2=P7(Quality)
    int Codec;                // 0=H264, 1=HEVC
    int GopSize;              // Group of Pictures size (keyframe interval)
    int EnableTAB;            // Enable Temporal Accumulation Buffer (0=off, 1=on)
    int EnableCAS;            // Enable Contrast Adaptive Sharpening (0=off, 1=on)
    int EnableDither;         // Enable Blue Noise Dithering (0=off, 1=on)
    int TABSubFrameCount;     // Number of sub-frames for TAB (typically 8)
    float CASSharpness;       // Sharpening strength (0.0 to 0.5)
    int _padding;             // Padding to align to 8-byte boundary
};

class NvencEncoder {
public:
    NvencEncoder();
    ~NvencEncoder();
    
    bool Initialize(ID3D11Device* unityDevice, ID3D11Texture2D* textureHint, 
                    int width, int height, int fps, const char* outputPath,
                    const NvencEncoderSettings& settings);
    bool EncodeFrame(ID3D11Texture2D* unityTexture, int64_t frameIndex, bool enableCAS = false, float sharpness = 0.0f);
    void Shutdown();
    const char* GetError() const { return m_errorBuffer; }
    const char* GetLastError() const { return m_errorBuffer; }

private:
    bool LoadNvencLibrary();
    bool ValidateOrCreateDevice(ID3D11Device* unityDevice, ID3D11Texture2D* textureHint);
    bool InitializeEncoder(const NvencEncoderSettings& settings);
    bool InitializeFFmpeg(const char* outputPath);
    // F8: fetch SPS/PPS (and VPS for HEVC) from NVENC and build avcC/hvcC extradata
    // for the matroska muxer (without extradata, avformat_write_header fails with
    // INVALIDDATA). avStream is an AVStream*, kept opaque to avoid pulling FFmpeg
    // headers into this header.
    bool SetExtradataFromNvenc(void* avStream);
    bool BuildH264Extradata(void* avStream,
                            const std::vector<std::pair<const uint8_t*, uint32_t>>& sps,
                            const std::vector<std::pair<const uint8_t*, uint32_t>>& pps);
    bool BuildHevcExtradata(void* avStream,
                            const std::vector<std::pair<const uint8_t*, uint32_t>>& vps,
                            const std::vector<std::pair<const uint8_t*, uint32_t>>& sps,
                            const std::vector<std::pair<const uint8_t*, uint32_t>>& pps);
    void LogDebug(const char* fmt, ...);
    void SetError(const char* fmt, ...);
    const char* NvencStatusToString(NVENCSTATUS status);
    bool ProcessOutput();
    // F9: like ProcessOutput but reports whether a packet was actually written, so
    // the shutdown drain can stop instead of spinning on LOCK_BUSY.
    bool ProcessOutput(bool* wrotePacket);
    
    // NVENC handles
    void* m_hEncoder;
    HMODULE m_hNvencLib;
    NV_ENCODE_API_FUNCTION_LIST m_nvencFunctions;
    
    // D3D11
    ID3D11Device* m_device;
    ID3D11DeviceContext* m_context;
    ID3D11Device* m_unityDevice;
    void* m_multithread;             // ID3D11Multithread*, stored as void* to avoid SDK header dependency
    BOOL m_prevMultithreadProtected;
    bool m_multithreadProtectionActive;
    
    // Texture resources
    ID3D11Texture2D* m_encodeTextures[2];
    int m_bufferIndex;
    // F2/F3: encode textures are created in the source texture's own DXGI format so
    // CopyResource is legal by construction; the matching NVENC format is declared
    // (B8G8R8A8_UNORM -> ARGB, R8G8B8A8_UNORM -> ABGR). Detected from the source
    // texture at init.
    DXGI_FORMAT m_encodeTextureFormat;
    NV_ENC_BUFFER_FORMAT m_encodeBufferFormat;
    
    // NVENC resources
    NV_ENC_REGISTERED_PTR m_registeredResources[2];
    NV_ENC_INPUT_PTR m_mappedInputs[2];
    NV_ENC_OUTPUT_PTR m_bitstreamBuffer;
    
    // FFmpeg
    void* m_formatContext;
    void* m_videoStream;
    bool m_headerWritten; // avformat_write_header succeeded; trailer safe to write
    int64_t m_frameCount;
    int m_deferredFrames; // frames NVENC buffered (NEED_MORE_INPUT); drained at EOS
    int m_width, m_height, m_fps;
    bool m_initialized;
    bool m_isHEVC;
    
    // Debug/Error
    char m_errorBuffer[1024];
    char m_debugBuffer[2048];
    
    // Thread safety
    std::mutex m_encodeMutex;
    std::mutex m_writeMutex; // serializes av_interleaved_write_frame (F16: was a
                             // function-static shared across encoder instances)
    std::mutex m_tabMutex;   // serializes TAB SubmitSubFrame/FinalizeTemporalFrame
    
    // Compute shaders (created from embedded bytecode)
    ID3D11ComputeShader* m_tabComputeShader;
    ID3D11ComputeShader* m_casComputeShader;

    // GPU Synchronization: fresh event query per sync (see HardSyncGPU in .cpp)

    // Intermediate textures for shader pipeline (ping-pong)
    ID3D11Texture2D* m_intermediateTextures[2];
    ID3D11ShaderResourceView* m_intermediateSRV[2];
    ID3D11UnorderedAccessView* m_intermediateUAV[2];

    // Blue noise texture
    ID3D11Texture2D* m_blueNoiseTexture;
    ID3D11ShaderResourceView* m_blueNoiseSRV;

    // Constant buffers
    ID3D11Buffer* m_casParamsBuffer;
    
    // TAB state
    bool m_isTabMode;
    int m_currentAccumBuffer;
    int m_currentSubFrame;
    int m_tabSubFrameCount;
    int m_tabFinalizeCount;        // output frames completed (rate-limits TAB breadcrumbs)
    bool m_tabFirstSliceReceived;  // true once slice 0 of the first TAB batch is received
    int m_syncDiagCount;           // rate-limits HardSyncGPU resolution-latency logs

    // Accumulation array (8-slice texture array for sub-frames)
    ID3D11Texture2D* m_accumulationArray[2];
    ID3D11ShaderResourceView* m_accumulationSRV[2];

    // TAB weight buffer (constant buffer)
    ID3D11Buffer* m_tabWeightBuffer;
    
    bool InitializeComputeShaders();
    bool CreateIntermediateTextures(int width, int height);
    bool CreateBlueNoiseTexture();
    bool CreateConstantBuffers();
    
    // GPU Synchronization
    bool HardSyncGPU(const char* stageName, DWORD timeoutMs = 5000);
    
    // Preprocessing encode paths
    bool EncodeFrameWithCAS(ID3D11Texture2D* unityTexture, int64_t frameIndex, float sharpness);
    bool EncodeNVENC(int idx, int64_t frameIndex);
    
    // TAB methods
    bool CreateAccumulationArray(int width, int height);
    
public:
    // Public TAB API
    bool SubmitSubFrame(ID3D11Texture2D* unityTexture, int sliceIndex);
    bool FinalizeTemporalFrame(int64_t frameIndex, float sharpness);
    void SetTabMode(bool enabled, int subFrameCount);
};