#pragma once
#include "../nvenc/nvEncodeAPI.h"
#include <windows.h>
#include <d3d11.h>
#include <cstdint>
#include <string>

// Settings struct (matches C# layout exactly)
struct NvencEncoderSettings {
    int RateControlMode;  // 0=CQP, 1=VBR
    int TargetBitrateKbps;
    int QpI;
    int QpP;
    int QpB;
    int QualityPreset;    // 0=P1(Speed), 1=P4(Balanced), 2=P7(Quality)
    int Codec;            // 0=H264, 1=HEVC
    int GopSize;
    int Reserved1;
    int Reserved2;
};

class NvencEncoder {
public:
    NvencEncoder();
    ~NvencEncoder();
    
    bool Initialize(ID3D11Device* unityDevice, ID3D11Texture2D* textureHint, 
                    int width, int height, int fps, const char* outputPath,
                    const NvencEncoderSettings& settings);
    bool EncodeFrame(ID3D11Texture2D* unityTexture, int64_t frameIndex);
    void Shutdown();
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
};