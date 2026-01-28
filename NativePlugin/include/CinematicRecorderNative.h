#pragma once

#include <Windows.h>
#include <d3d11.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef void* CREncoderHandle;

// Encoder settings struct - must match C# layout exactly
typedef struct {
    int RateControlMode;      // 0=CQP, 1=VBR, 2=CBR
    int TargetBitrateKbps;    // Kilobits per second (for VBR/CBR)
    int QpI;                  // QP for I-frames (CQP mode, 0-51)
    int QpP;                  // QP for P-frames
    int QpB;                  // QP for B-frames
    int QualityPreset;        // 0=Speed, 1=Balanced, 2=Quality
    int Codec;                // 0=H264, 1=HEVC
    int GopSize;              // Keyframe interval in frames
    int Reserved1;            // Padding for alignment/future use
    int Reserved2;            // Padding for alignment/future use
} AmfEncoderSettings;

// NEW: Set the Unity D3D11 device once (optional, can also use InitFromTexture)
__declspec(dllexport)
void CR_SetUnityD3D11Device(ID3D11Device* device);

// MODIFIED: Added settings parameter
__declspec(dllexport)
CREncoderHandle CR_InitEncoder(
    int width,
    int height,
    int fps,
    const char* outputPath,
    const AmfEncoderSettings* settings  // NEW: Encoder configuration
);

// MODIFIED: Added settings parameter
__declspec(dllexport)
CREncoderHandle CR_InitEncoderFromTexture(
    ID3D11Texture2D* d3d11Texture,
    int width,
    int height,
    int fps,
    const char* outputPath,
    const AmfEncoderSettings* settings  // NEW: Encoder configuration
);

__declspec(dllexport)
int CR_EncodeFrame(
    CREncoderHandle encoder,
    ID3D11Texture2D* texture,
    long long frameIndex
);

__declspec(dllexport)
int CR_ShutdownEncoder(CREncoderHandle encoder);

__declspec(dllexport)
const char* CR_GetLastError();

#ifdef __cplusplus
}
#endif