#pragma once

#include <Windows.h>
#include <d3d11.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef void* CREncoderHandle;

// NEW: Set the Unity D3D11 device once (optional, can also use InitFromTexture)
__declspec(dllexport)
void CR_SetUnityD3D11Device(ID3D11Device* device);

// MODIFIED: No longer takes device parameter (uses global set via above or InitFromTexture)
__declspec(dllexport)
CREncoderHandle CR_InitEncoder(
    int width,
    int height,
    int fps,
    const char* outputPath
);

// NEW: Convenience wrapper that extracts device from texture then calls CR_InitEncoder
__declspec(dllexport)
CREncoderHandle CR_InitEncoderFromTexture(
    ID3D11Texture2D* d3d11Texture,
    int width,
    int height,
    int fps,
    const char* outputPath
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