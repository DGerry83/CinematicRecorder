// GTAO Compute Shader - XeGTAO-based implementation
// Optimized for Unity Deferred rendering

Texture2D<float> g_DepthTexture : register(t0);
Texture2D<float4> g_NormalTexture : register(t1);
RWTexture2D<float> g_AOTexture : register(u0);

SamplerState pointSampler : register(s0);  // Point sampler for Hi-Z depth sampling

cbuffer GTAOParams : register(b0)
{
    // float4 #1 (offset 0)
    float2 NDCToViewMul;        // tanHalfFOV * float2(2, -2)
    float2 NDCToViewAdd;        // tanHalfFOV * float2(-1, 1)
    // float4 #2 (offset 16)
    float2 DepthUnpackConsts;   // x = (far*near)/(far-near), y = -near/(far-near)
    float2 ScreenSize;
    // float4 #3 (offset 32)
    float2 InvScreenSize;
    float EffectRadius;
    float FalloffRange;
    // float4 #4 (offset 48)
    float Intensity;
    float SampleDistributionPower;
    int SliceCount;
    int StepsPerSlice;
    // float4 #5 (offset 64)
    int NoiseIndex;
    float DepthMIPSamplingOffset; // Offset for Hi-Z mip level calculation (typically 1.0-2.0)
    float __pad1;               // Padding
    float __pad2;               // Padding
    // float4 #6, #7, #8 (offset 80, 96, 112)
    float4 WorldToViewRow0;     // .xyz = row 0 of world-to-view matrix
    float4 WorldToViewRow1;     // .xyz = row 1 of world-to-view matrix
    float4 WorldToViewRow2;     // .xyz = row 2 of world-to-view matrix
};

// Simple R1 sequence for noise
float R1Noise(float idx)
{
    return frac(idx * 0.6180339887498948482);
}

// Stable reversed-Z linearization
// unpackConsts.x = near plane, unpackConsts.y = far plane
// Returns negative view-space Z (e.g., -0.21 to -750000)
float LinearizeDepth(float rawDepth, float2 nearFar)
{
    float n = nearFar.x;  // Near plane (positive)
    float f = nearFar.y;  // Far plane (positive)
    
    // For reversed Z: rawDepth 1.0 = near, 0.0 = far
    return -(n * f) / (rawDepth * (f - n) + n);
}

// Fast viewspace reconstruction
float3 ComputeViewspacePosition(float2 uv, float viewZ, float2 ndcMul, float2 ndcAdd)
{
    float3 pos;
    pos.xy = (ndcMul * uv + ndcAdd) * viewZ;
    pos.z = viewZ;
    return pos;
}

// Unpack normal from Deferred's custom format (WORLD SPACE)
// Deferred mod stores full XYZ world normal in RGB
float3 UnpackNormal(float4 normalData)
{
    return normalData.rgb * 2.0 - 1.0;
}

[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint2 coord = id.xy;
    uint width = (uint)ScreenSize.x;
    uint height = (uint)ScreenSize.y;
    
    if (coord.x >= width || coord.y >= height)
        return;
    
    float2 uv = (float2(coord) + 0.5) * InvScreenSize;
    float rawDepth = g_DepthTexture[coord];
    float4 normalData = g_NormalTexture[coord];
    float3 worldNormal = UnpackNormal(normalData);
    
    // Skip sky/invalid pixels - rely on Deferred's normal alpha for sky detection
    // (depth range check removed as it fails with reversed-Z and large far planes)
    if (length(worldNormal) < 0.001 || normalData.a < 0.1)
    {
        g_AOTexture[coord] = 1.0;
        return;
    }
    
    // 1. Linearize depth
    float viewZ = LinearizeDepth(rawDepth, DepthUnpackConsts);
    
    // 2. Reconstruct view position
    float3 pos = ComputeViewspacePosition(uv, viewZ, NDCToViewMul, NDCToViewAdd);
    float3 viewVec = normalize(-pos);
    
    // 3. Transform normal to VIEW SPACE using proper matrix
    float3x3 worldToView = float3x3(
        WorldToViewRow0.xyz,
        WorldToViewRow1.xyz,
        WorldToViewRow2.xyz
    );
    float3 viewNormal = mul(worldToView, worldNormal);
    
    // 4. Push toward camera (less negative Z) to avoid self-occlusion
    // Use 0.99999 for FP32 (0.01% offset = ~75 units at 750km far plane)
    viewZ *= 0.99999;
    pos = ComputeViewspacePosition(uv, viewZ, NDCToViewMul, NDCToViewAdd);
    
    // 5. Calculate screen-space radius (use abs to handle sign conventions)
    float2 pixelSizeAtViewZ = viewZ * NDCToViewMul * InvScreenSize;
    float screenSpaceRadius = EffectRadius / max(abs(pixelSizeAtViewZ.x), 0.0001);
    screenSpaceRadius = min(screenSpaceRadius, 50.0); // Max 50 pixels to prevent excessive sampling on distant grass
    
    // 6. Minimum sample distance (avoid self-sampling center pixel)
    const float pixelTooCloseThreshold = 1.3;
    float minS = pixelTooCloseThreshold / screenSpaceRadius;
    
    // 8. Noise for temporal stability
    float noiseSlice = R1Noise((float)NoiseIndex + (float)coord.x * 0.5 + (float)coord.y * 0.3);
    
    float visibility = 0;
    
    for (int s = 0; s < SliceCount; s++)
    {
        // Hemisphere only (PI radians, not 2*PI)
        float sliceK = ((float)s + noiseSlice) / (float)SliceCount;
        float phi = sliceK * 3.14159265359; // 180 degrees
        // XeGTAO: negate sin for Unity's coordinate system
        float2 omega = float2(cos(phi), -sin(phi));  // For sampling direction
        
        // Slice plane orientation - directionVec matches omega for consistency
        float3 directionVec = float3(omega.x, omega.y, 0.0);
        float3 orthoDirectionVec = directionVec - dot(directionVec, viewVec) * viewVec;
        float3 slicePlaneNormal = normalize(cross(orthoDirectionVec, viewVec));
        
        // Project normal to slice plane
        float3 projectedNormal = viewNormal - slicePlaneNormal * dot(viewNormal, slicePlaneNormal);
        float projectedNormalLength = length(projectedNormal);
        
        // XeGTAO normal angle calculation (reuse orthoDirectionVec from slice plane calc)
        float3 projectedNormalDir = projectedNormal / projectedNormalLength;
        float signNorm = sign(dot(orthoDirectionVec, projectedNormal));
        float cosNorm = saturate(dot(projectedNormalDir, viewVec));
        float n = signNorm * acos(cosNorm);
        float cosN = cos(n);
        float sinN = sin(n);
        
        // Initialize horizons based on normal angle
        float lowHorizonCos0 = cos(n + 1.570796); // n + PI/2
        float lowHorizonCos1 = cos(n - 1.570796); // n - PI/2
        // XeGTAO: horizonCos[0] = +omega direction, horizonCos[1] = -omega direction
        float2 horizonCos = float2(lowHorizonCos0, lowHorizonCos1);
        float2 lowHorizonCos = float2(lowHorizonCos0, lowHorizonCos1);
        
        // Sample both directions
        for (int dir = 0; dir < 2; dir++)
        {
            float2 direction = (dir == 0) ? omega : -omega;
            float lowHorizonCosDir = lowHorizonCos[dir];
            
            for (int step = 0; step < StepsPerSlice; step++)
            {
                // Distribution with minS offset to avoid self-sampling
                float stepBase = (float(step) + 0.5) / (float)StepsPerSlice;
                float stepNoise = R1Noise((float)NoiseIndex + (float)s * 7 + (float)step * 13);
                float t = pow(stepBase + stepNoise * 0.1, SampleDistributionPower);
                t += minS; // Add offset to ensure first sample is at least 1.3 pixels away
                
                float2 sampleUV = uv + direction * t * screenSpaceRadius * InvScreenSize;
                
                if (any(sampleUV < 0.0) || any(sampleUV > 1.0))
                    break;
                    
                int2 sampleCoord = int2(sampleUV * ScreenSize);
                sampleCoord = clamp(sampleCoord, int2(0, 0), int2(width - 1, height - 1));
                
                // Hi-Z sampling: calculate mip level based on sample distance (in pixels)
                float sampleOffsetLength = t * screenSpaceRadius; // Distance in pixels
                float mipLevel = clamp(log2(sampleOffsetLength) - DepthMIPSamplingOffset, 0.0, 8.0);
                
                float sampleRawDepth = g_DepthTexture.SampleLevel(pointSampler, sampleUV, mipLevel);
                if (sampleRawDepth < 0.001 || sampleRawDepth > 0.999)
                    continue;
                    
                float sampleViewZ = LinearizeDepth(sampleRawDepth, DepthUnpackConsts);
                
                float3 samplePos = ComputeViewspacePosition(sampleUV, sampleViewZ, NDCToViewMul, NDCToViewAdd);
                
                // REFERENCE-style falloff (inverse square, no thin occluder compensation)
                float3 delta = samplePos - pos;
                float distSq = dot(delta, delta);
                float3 deltaDir = normalize(delta);
                float elevationCos = dot(deltaDir, viewVec);
                
                // Inverse square falloff (REFERENCE style)
                float falloff = 1.0 / (1.0 + distSq / (EffectRadius * EffectRadius));
                elevationCos = lerp(lowHorizonCosDir, elevationCos, falloff);
                
                horizonCos[dir] = max(horizonCos[dir], elevationCos);
            }
        }
        
        // XeGTAO horizon integration - CRITICAL: h0 negative, h1 positive
        // horizonCos[0] is from +omega direction, horizonCos[1] from -omega
        // h0 represents angle below horizontal (negative), h1 above (positive)
        float h0 = -acos(clamp(horizonCos[1], -1.0, 1.0)); // Negative! From -omega dir
        float h1 =  acos(clamp(horizonCos[0], -1.0, 1.0)); // Positive! From +omega dir
        
        float iarc0 = (cosN + 2.0 * h0 * sinN - cos(2.0 * h0 - n)) / 4.0;
        float iarc1 = (cosN + 2.0 * h1 * sinN - cos(2.0 * h1 - n)) / 4.0;
        
        float localVisibility = projectedNormalLength * (iarc0 + iarc1);
        visibility += localVisibility;  // XeGTAO: no saturate per slice
    }
    
    visibility /= (float)SliceCount;
    
    // REFERENCE-style intensity application
    float ao = lerp(1.0, visibility, saturate(Intensity));
    if (Intensity > 1.0)
        ao = lerp(ao, ao * ao, saturate(Intensity - 1.0));
    
    g_AOTexture[coord] = saturate(ao);
}