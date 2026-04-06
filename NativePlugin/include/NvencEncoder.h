#pragma once
#include "../nvenc/nvEncodeAPI.h"
#include <windows.h>
#include <d3d11.h>
#include <cstdint>
#include <string>
#include <mutex>

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
    void LogDebug(const char* fmt, ...);
    void SetError(const char* fmt, ...);
    const char* NvencStatusToString(NVENCSTATUS status);
    bool ProcessOutput();
    
    // NVENC handles
    void* m_hEncoder;
    HMODULE m_hNvencLib;
    NV_ENCODE_API_FUNCTION_LIST m_nvencFunctions;
    
    // D3D11
    ID3D11Device* m_device;
    ID3D11DeviceContext* m_context;
    ID3D11Device* m_unityDevice;
    bool m_usingSharedDevice;
    
    // Texture resources
    ID3D11Texture2D* m_encodeTextures[2];
    HANDLE m_sharedHandles[2];
    int m_bufferIndex;
    
    // NVENC resources
    NV_ENC_REGISTERED_PTR m_registeredResources[2];
    NV_ENC_INPUT_PTR m_mappedInputs[2];
    NV_ENC_OUTPUT_PTR m_bitstreamBuffer;
    
    // FFmpeg
    void* m_formatContext;
    void* m_videoStream;
    int64_t m_frameCount;
    int m_width, m_height, m_fps;
    bool m_initialized;
    bool m_isHEVC;
    
    // Debug/Error
    char m_errorBuffer[1024];
    char m_debugBuffer[2048];
    
    // Thread safety
    std::mutex m_encodeMutex;
    
    // Compute shaders (created from embedded bytecode)
    ID3D11ComputeShader* m_tabComputeShader;
    ID3D11ComputeShader* m_casComputeShader;
    ID3D11ComputeShader* m_ditherComputeShader;

    // GPU Synchronization
    ID3D11Query* m_preComputeQuery;
    ID3D11Query* m_postComputeQuery;
    
    // Intermediate textures for shader pipeline (ping-pong)
    ID3D11Texture2D* m_intermediateTextures[2];
    ID3D11ShaderResourceView* m_intermediateSRV[2];
    ID3D11UnorderedAccessView* m_intermediateUAV[2];

    // Blue noise texture
    ID3D11Texture2D* m_blueNoiseTexture;
    ID3D11ShaderResourceView* m_blueNoiseSRV;

    // Constant buffers
    ID3D11Buffer* m_casParamsBuffer;
    ID3D11Buffer* m_ditherParamsBuffer;
    
    // TAB state
    bool m_isTabMode;
    int m_currentAccumBuffer;
    int m_currentSubFrame;
    int m_tabSubFrameCount;

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
    void HardSyncGPU(ID3D11Query* query, const char* stageName);
    
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