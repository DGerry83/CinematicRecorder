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
    int EnableVbaq;           // 0=Off, 1=On (Variance-Based Adaptive Quantization)
    int UseBlueNoiseDither;   // 0 = CopyResource, 1 = Compute Dither
    int Reserved2;            // Reserved (remains at end for padding/compatibility)
} AmfEncoderSettings;

// NEW: Temporal Accumulation Blur settings struct
typedef struct {
    int Enabled;              // 0=Off, 1=On
    int SubFrameCount;        // Number of sub-frames to accumulate (typically 8)
    float Sigma;              // Gaussian blur sigma (typically 1.5)
} TabSettings;

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

// NEW: Temporal Accumulation Blur API

// Configure TAB mode. Must be called after CR_InitEncoder/FromTexture but before first frame.
// If enabled, encoder switches to accumulation mode with specified sub-frame count and Gaussian sigma.
__declspec(dllexport)
int CR_SetTemporalAccumulation(CREncoderHandle encoder, const TabSettings* settings);

// Submit a single sub-frame for accumulation. 
// 'subFrameIndex' must be 0 to (SubFrameCount-1).
// Copies from unityTexture to internal accumulation array slice [subFrameIndex].
// Returns immediately (non-blocking GPU copy).
__declspec(dllexport)
int CR_SubmitSubFrame(CREncoderHandle encoder, ID3D11Texture2D* unityTexture, int subFrameIndex);

// Finalize accumulated sub-frames and encode the result.
// Dispatches compute shader to weighted-average accumulation array into encoder texture,
// then encodes the frame and blocks until complete.
// 'outputFrameIndex' is the frame number for the encoded output (passed to encoder).
__declspec(dllexport)
int CR_FinalizeTemporalFrame(CREncoderHandle encoder, long long outputFrameIndex);

// GTAO Debug Test - Minimal verification functions
__declspec(dllexport)
void CR_GTAODebugSetInput(ID3D11Texture2D* depthTex, ID3D11Texture2D* normalTex, int width, int height,
                          const float* invProj, const float* worldToView, float nearPlane, float farPlane);

__declspec(dllexport)
int CR_GTAODebugExecute(const char* outputDirectory);

#ifdef __cplusplus
}
#endif