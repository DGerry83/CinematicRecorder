Texture2D<float4>  InputTexture : register(t0);
Texture2D<float>   BlueNoise    : register(t1);
RWTexture2D<float4> OutputTexture : register(u0);

cbuffer DitherParams : register(b0) {
    uint2 Resolution;    // Output width/height
    uint  FrameIndex;    // For temporal offset
    uint  Flags;         // Bit 0: BGRA swizzle
};

[numthreads(16, 16, 1)]
void main(uint3 id : SV_DispatchThreadID) {
    if (id.x >= Resolution.x || id.y >= Resolution.y) return;
    
    float4 color = InputTexture.Load(int3(id.xy, 0));
    
    // BGRA swizzle if needed (Bit 0 set)
    if (Flags & 1) {
        color = color.bgra;
    }
    
    // Blue noise sampling with temporal offset
    uint2 noiseCoord = (id.xy + uint2(FrameIndex * 13, FrameIndex * 37)) % 64;
    float noise = BlueNoise.Load(int3(noiseCoord, 0));
    
    // Apply dither: noise is 0-1, map to -0.5 to +0.5 of 1/255
    float ditherAmount = (noise - 0.5) / 255.0;
    color.rgb = saturate(color.rgb + ditherAmount);
    
    OutputTexture[id.xy] = float4(1.0, 0.0, 0.0, 1.0);
}