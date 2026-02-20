Texture2DArray<float4> g_AccumulationArray : register(t0);
RWTexture2D<float4> g_OutputTexture : register(u0);

cbuffer TabWeightsBuffer : register(b0)
{
    float4 Weights[2];    // Weights[0] = w0,w1,w2,w3 | Weights[1] = w4,w5,w6,w7
    float TotalWeight;    // Sum of all weights for normalization
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
    
    float4 accumulated = float4(0.0, 0.0, 0.0, 0.0);
    
    // Unpack weights from constant buffer (8 weights total)
    float w[8];
    w[0] = Weights[0].x;
    w[1] = Weights[0].y;
    w[2] = Weights[0].z;
    w[3] = Weights[0].w;
    w[4] = Weights[1].x;
    w[5] = Weights[1].y;
    w[6] = Weights[1].z;
    w[7] = Weights[1].w;
    
    // Accumulate all sub-frames with Gaussian weights
    [unroll]
    for (int i = 0; i < 8; i++)
    {
        accumulated += g_AccumulationArray[uint3(coord, i)] * w[i];
    }
    
    // Normalize
    accumulated /= TotalWeight;
    
    // Saturate for UNORM output (R8G8B8A8_UNORM)
    // The encoder texture expects 0-1 range, R16G16B16A16_FLOAT input may have values outside this range
    g_OutputTexture[coord] = saturate(accumulated);
}