// CAS (Contrast Adaptive Sharpening) - Second Pass
// Samples from g_InputTexture (accumulation result) and writes sharpened output

Texture2D<float4> g_InputTexture : register(t0);
RWTexture2D<float4> g_OutputTexture : register(u0);

cbuffer CASSharpnessBuffer : register(b0)
{
    float Sharpness;      // 0.0 to 0.5 (user slider 0-50%)
    float3 Padding;       // Align to 16-byte boundary
};

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{   
    uint2 coord = id.xy;
    uint width, height;
    g_OutputTexture.GetDimensions(width, height);
    
    // Bounds check
    if (coord.x >= width || coord.y >= height)
        return;
    
    // Sample center and 4 cross neighbors
    float3 a = g_InputTexture[uint2(coord.x, max(coord.y - 1, 0))].rgb;          // Up
    float3 b = g_InputTexture[uint2(max(coord.x - 1, 0), coord.y)].rgb;          // Left
    float3 c = g_InputTexture[coord].rgb;                                         // Center
    float3 d = g_InputTexture[uint2(min(coord.x + 1, width - 1), coord.y)].rgb;  // Right
    float3 e = g_InputTexture[uint2(coord.x, min(coord.y + 1, height - 1))].rgb; // Down
    
    // Compute local contrast (min/max of cross pattern)
    float3 minRGB = min(min(min(a, b), min(c, d)), e);
    float3 maxRGB = max(max(max(a, b), max(c, d)), e);
    float3 contrast = maxRGB - minRGB;
    
    // Adaptive sharpening amount: less on high-contrast edges
    float avgContrast = dot(contrast, float3(0.299, 0.587, 0.114));
    float edgeFactor = 1.0 / (1.0 + avgContrast * 4.0);
    float amount = Sharpness * edgeFactor;
    
    // Energy-preserving sharpening kernel
    // Sum of all weights equals 1.0 (no brightness change)
    // Center: 1.0 + amount, Neighbors: -amount/4 each
    float3 sharpened = c * (1.0 + amount) - (a + b + d + e) * amount * 0.25;
    
    // Clamp to valid range
    g_OutputTexture[coord] = float4(saturate(sharpened), 1.0);
}
