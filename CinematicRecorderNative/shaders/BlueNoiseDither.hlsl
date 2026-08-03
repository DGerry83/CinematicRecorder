Texture2D<float4>  InputTexture : register(t0);
Texture2D<float>   BlueNoise    : register(t1);
RWTexture2D<float4> OutputTexture : register(u0);

cbuffer DitherParams : register(b0) {
    uint2 Resolution;
    uint  FrameIndex;
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
    
    // 256x256 blue noise with temporal offset
    uint2 noiseCoord = (id.xy + uint2(FrameIndex * 17, FrameIndex * 29)) & 255;
    float noise = BlueNoise.Load(int3(noiseCoord, 0));
    
    // Base dither amplitude: ±1.0 LSB (slightly stronger than before since we weight it down)
    float ditherAmount = (noise - 0.5) * (8.0 / 255.0); // 8.0 kills all banding in 8-bit
    
    // LUMA-WEIGHTED: Stronger dither in dark areas, none in bright
    // Rec.709 luma coefficients
    float luma = dot(color.rgb, float3(0.2126, 0.7152, 0.0722));
    float weight = saturate(1.0 - luma * 1.50); // Multiplier 2.0 makes cutoff steeper (adjustable)
    
    color.rgb = saturate(color.rgb + ditherAmount * weight);
    
    OutputTexture[id.xy] = color;
}