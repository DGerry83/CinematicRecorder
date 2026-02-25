// SimpleAO.hlsl - Basic depth-based ambient occlusion for verification
// This is a simplified placeholder before full GTAO

Texture2D<float> DepthTexture : register(t0);
Texture2D<float4> NormalTexture : register(t1);
RWTexture2D<float> AOOutput : register(u0);

cbuffer AOParams : register(b0)
{
    float4x4 InvViewProj;    // Inverse view-projection matrix
    float2 Resolution;       // Width, Height
    float2 InvResolution;    // 1/Width, 1/Height
    float Radius;            // AO sampling radius in world units
    float Intensity;         // AO intensity multiplier
    float2 Padding;
};

// Reconstruct world position from depth
float3 ReconstructWorldPosition(float2 uv, float depth)
{
    float4 clipPos = float4(uv * 2.0 - 1.0, depth, 1.0);
    clipPos.y = -clipPos.y; // Flip Y for DX
    float4 worldPos = mul(clipPos, InvViewProj);
    return worldPos.xyz / worldPos.w;
}

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)Resolution.x || id.y >= (uint)Resolution.y)
        return;
    
    float2 uv = (float2(id.xy) + 0.5) * InvResolution;
    
    // Sample center depth and normal
    float centerDepth = DepthTexture.Load(int3(id.xy, 0));
    float3 centerNormal = NormalTexture.Load(int3(id.xy, 0)).xyz;
    
    // Reconstruct world position
    float3 centerPos = ReconstructWorldPosition(uv, centerDepth);
    
    // Simple hemisphere sampling AO
    float ao = 0.0;
    int sampleCount = 8;
    
    for (int i = 0; i < sampleCount; i++)
    {
        // Fibonacci sphere distribution
        float phi = (float)i * 2.39996; // golden angle
        float cosTheta = 1.0 - (float)(i + 0.5) / float(sampleCount);
        float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
        
        // Sample direction in tangent space
        float3 sampleDir;
        sampleDir.x = cos(phi) * sinTheta;
        sampleDir.y = sin(phi) * sinTheta;
        sampleDir.z = cosTheta;
        
        // Build tangent basis from normal
        float3 up = abs(centerNormal.z) < 0.999 ? float3(0, 0, 1) : float3(1, 0, 0);
        float3 tangent = normalize(cross(up, centerNormal));
        float3 bitangent = cross(centerNormal, tangent);
        
        // Transform to world space
        float3 worldDir = tangent * sampleDir.x + bitangent * sampleDir.y + centerNormal * sampleDir.z;
        
        // Sample position
        float3 samplePos = centerPos + worldDir * Radius;
        
        // Project back to screen
        float4 clipPos = mul(float4(samplePos, 1.0), InvViewProj);
        clipPos.xyz /= clipPos.w;
        clipPos.y = -clipPos.y;
        float2 sampleUV = clipPos.xy * 0.5 + 0.5;
        
        // Check if sample is within screen bounds
        if (all(sampleUV >= 0.0) && all(sampleUV <= 1.0))
        {
            // Sample depth at projected position
            float2 sampleCoord = sampleUV * Resolution;
            float sampleDepth = DepthTexture.Load(int3(sampleCoord, 0));
            float3 sampleWorldPos = ReconstructWorldPosition(sampleUV, sampleDepth);
            
            // Distance check
            float dist = length(sampleWorldPos - centerPos);
            float rangeCheck = smoothstep(Radius, 0.0, dist);
            
            // AO contribution: occluded if sample depth is closer than expected
            float expectedDepth = samplePos.z;
            float actualDepth = sampleWorldPos.z;
            float occluded = (actualDepth < expectedDepth - 0.01) ? 1.0 : 0.0;
            
            ao += occluded * rangeCheck;
        }
    }
    
    ao /= float(sampleCount);
    ao = 1.0 - saturate(ao * Intensity);
    
    AOOutput[id.xy] = ao;
}
