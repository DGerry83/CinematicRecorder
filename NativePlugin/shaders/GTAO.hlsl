// GTAO (Ground-Truth Ambient Occlusion) Compute Shader
// Full implementation with horizon-based sampling
// For debug dump: single-pass screen-space GTAO without tiling
// Input: ARGB32 depth (depth in red channel) and ARGB32 normals

Texture2D<float4> g_DepthTexture : register(t0);  // ARGB32, depth in R channel
Texture2D<float4> g_NormalTexture : register(t1); // ARGB32 normals
RWTexture2D<float> g_AOTexture : register(u0);

cbuffer GTAOParams : register(b0)
{
    float4x4 InvProj;        // Inverse projection matrix
    float2 ScreenSize;       // Width, Height
    float2 InvScreenSize;    // 1/Width, 1/Height
    float Radius;            // World-space sampling radius
    float Intensity;         // AO intensity multiplier
    int SliceCount;          // Number of slices (4-8)
    int StepsPerSlice;       // Steps per direction (8-16)
};

// Reconstruct view-space position from UV and depth
float3 UVToView(float2 uv, float depth, float4x4 invProj)
{
    float2 ndc = uv * 2.0 - 1.0;
    float4 view = mul(invProj, float4(ndc.x, -ndc.y, depth, 1.0));
    return view.xyz / view.w;
}

[numthreads(16, 16, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint2 coord = id.xy;
    uint width = (uint)ScreenSize.x;
    uint height = (uint)ScreenSize.y;
    
    // Bounds check
    if (coord.x >= width || coord.y >= height)
        return;
    
    float2 uv = (float2(coord) + 0.5) * InvScreenSize;
    
    // Sample depth and normal (ARGB32 format)
    float depth = g_DepthTexture[coord].r / 255.0;  // Convert from 8-bit to float
    float3 normal = g_NormalTexture[coord].rgb / 255.0;  // Convert from 8-bit to float
    normal = normal * 2.0 - 1.0;  // Map from [0,1] to [-1,1]
    
    // Skip invalid pixels
    if (depth < 0.001 || depth > 0.999 || length(normal) < 0.001)
    {
        g_AOTexture[coord] = 1.0;
        return;
    }
    
    // Reconstruct view-space position
    float3 pos = UVToView(uv, depth, InvProj);
    float3 viewVec = normalize(-pos);
    
    // World-space radius adapted to view depth
    float radiusVS = Radius / abs(pos.z);
    float radiusSS = radiusVS * InvProj._m11; // Approximate screen-space radius
    
    // Precompute GTAO terms
    float ndotv = dot(normal, viewVec);
    
    // Cross product of view and normal (for slice weighting)
    float2 vcrossn = float2(
        viewVec.y * normal.z - viewVec.z * normal.y,
        viewVec.z * normal.x - viewVec.x * normal.z
    );
    
    float aoAccum = 0.0;
    float sliceWeightAccum = 0.0;
    
    // Slice rotation setup
    float sliceAngleStep = 3.14159 / float(SliceCount);
    float2x2 sliceRot;
    sincos(sliceAngleStep, sliceRot._21, sliceRot._11);
    sliceRot._12 = -sliceRot._21;
    sliceRot._22 = sliceRot._11;
    
    float2 sliceDir = float2(1, 0);
    
    for (int s = 0; s < SliceCount; s++)
    {
        sliceDir = mul(sliceDir, sliceRot);
        
        // Project normal onto slice plane for weighting
        float sdotv = dot(sliceDir, viewVec.xy);
        float sdotn = dot(sliceDir, normal.xy);
        float ndotns = dot(sliceDir, vcrossn) * rsqrt(saturate(1.0 - sdotv * sdotv));
        float sliceWeight = sqrt(saturate(1.0 - ndotns * ndotns));
        
        if (sliceWeight < 0.001)
            continue;
        
        // Normal angle relative to slice
        float cosN = saturate(ndotv / sliceWeight);
        float normalAngle = acos(cosN);
        normalAngle = (sdotn < sdotv * ndotv) ? -normalAngle : normalAngle;
        
        // Track horizons for both directions
        float2 maxHorizonCos = float2(sin(normalAngle), -sin(normalAngle));
        
        // Sample in both directions along slice
        for (int side = 0; side < 2; side++)
        {
            float horizonCos = maxHorizonCos.x;
            
            for (int step = 0; step < StepsPerSlice; step++)
            {
                // Quadratic distribution (dense near center)
                float t = (float(step) + 0.5) / float(StepsPerSlice);
                t *= t;
                
                // Sample position in screen space
                float2 sampleUV = uv + sliceDir * t * radiusSS * (side ? -1.0 : 1.0);
                
                // Bounds check
                if (any(sampleUV < 0.0) || any(sampleUV > 1.0))
                    break;
                
                int2 sampleCoord = int2(sampleUV * ScreenSize);
                sampleCoord = clamp(sampleCoord, int2(0, 0), int2(width - 1, height - 1));
                
                float sampleDepth = g_DepthTexture[sampleCoord].r / 255.0;
                
                // Skip invalid samples
                if (sampleDepth < 0.001 || sampleDepth > 0.999)
                    continue;
                
                // Reconstruct sample position in view space
                float3 samplePos = UVToView(sampleUV, sampleDepth, InvProj);
                float3 delta = samplePos - pos;
                
                float distSq = dot(delta, delta);
                float3 deltaDir = delta * rsqrt(distSq);
                
                // Horizon angle cosine
                float cosH = dot(deltaDir, viewVec);
                
                // Distance falloff (thickness heuristic)
                float falloff = saturate(1.0 - distSq / (Radius * Radius));
                cosH = lerp(horizonCos, cosH, falloff);
                
                horizonCos = max(horizonCos, cosH);
            }
            
            maxHorizonCos.x = horizonCos;
            maxHorizonCos = maxHorizonCos.yx; // Swap for other side
        }
        
        // Analytical integration over the visible hemisphere slice
        float2 hAngles = float2(-acos(maxHorizonCos.x), acos(maxHorizonCos.y));
        float2 sinH, cosH;
        sincos(hAngles, sinH, cosH);
        
        // GTAO integral: simplified form
        float2 integral = sinH - hAngles * cosH;
        float sliceAO = 0.5 * dot(integral, float2(1, 1));
        
        aoAccum += sliceAO * sliceWeight;
        sliceWeightAccum += sliceWeight;
    }
    
    // Final AO value
    float visibility = sliceWeightAccum > 0.001 ? (aoAccum / sliceWeightAccum) : 1.0;
    visibility = saturate(visibility);
    
    // Apply intensity
    float ao = lerp(1.0, visibility, Intensity);
    
    g_AOTexture[coord] = ao;
}
