Here's the complete technical breakdown:
Architecture Overview
The pipeline uses 3 synchronous stages with explicit barriers:
Scatter Pass: Reorder depth/normal buffers into 2×2 or 4×4 tiles
Compute Pass: Execute GTAO on the tiled layout (cache-coherent)
Gather Pass: Reassemble AO result to full-screen output
No asynchronous compute, no ping-ponging mid-pass. Each pass holds exclusive access to its resources.
Stage 1: Scatter (Deinterleave)
Transform row-major buffers into tile-major order. For a 4×4 tiling pattern:
plain
Copy
Input (Row-Major):          Output (Tile-Major):
[ 0  1  2  3  4  5...]      [T0 T0 T0 T0 T1 T1...]
[16 17 18 19 20 21...]  →   [T0 T0 T0 T0 T1 T1...]
[32 33 34 35 36 37...]      [T2 T2 T2 T2 T3 T3...]
[48 49 50 51 52 53...]      [T2 T2 T2 T2 T3 T3...]
Coordinate Transformation:
hlsl
Copy
// Map screen pixel (x,y) to deinterleaved position
uint2 DeinterleavePos(uint2 screen_pos, uint2 grid_size, uint tile_count) {
    uint2 tile_size = (grid_size + tile_count - 1) / tile_count; // Ceiling division
    uint2 tile_idx = screen_pos % tile_count;                    // Which tile (0-3)
    uint2 local_pos = screen_pos / tile_count;                   // Position within tile
    return tile_idx * tile_size + local_pos;
}

// Inverse for sampling original UVs during scatter
float2 TileToSourceUV(uint2 tile_pos, uint2 grid_size, uint tile_count) {
    uint2 tile_size = (grid_size + tile_count - 1) / tile_count;
    uint2 tile_idx = tile_pos / tile_size;
    uint2 local_pos = tile_pos % tile_size;
    uint2 screen_pos = local_pos * tile_count + tile_idx;
    return (float2(screen_pos) + 0.5) / float2(grid_size);
}
Implementation:
Use a Compute Shader with thread group size 16×16 or 32×32
Each thread fetches from SourceDepth/Normal at TileToSourceUV(), writes to TiledDepth/Normal at DispatchThreadID.xy
Explicit UAV barrier after this pass before Compute stage
Stage 2: GTAO Compute (Tiled Space)
Now execute the horizon-based occlusion on the tiled buffers. The critical difference: samples that were far apart in screen space are now neighbors in memory.
View-Space Setup:
hlsl
Copy
float3 GetViewPos(uint2 tile_pos, float depth, float4x4 inv_proj) {
    float2 uv = (float2(tile_pos) + 0.5) / float2(tile_buffer_size);
    float2 ndc = uv * 2.0 - 1.0;
    // Adjust for your specific projection matrix
    float4 view = mul(inv_proj, float4(ndc, depth, 1.0));
    return view.xyz / view.w;
}
The GTAO Kernel:
hlsl
Copy
// Constants
#define SLICE_COUNT 4       // 4-8 slices
#define STEPS_PER_SLICE 8   // 8-16 steps per direction
#define TILE_COUNT 4        // 2 or 4

float ComputeAO(uint2 tile_pos, float2 tile_uv, 
                Texture2D<float> tiled_depth,
                Texture2D<float3> tiled_normal,
                float4x4 inv_proj, float2 buffer_size) {
    
    float depth = tiled_depth.Load(int3(tile_pos, 0));
    float3 pos = GetViewPos(tile_pos, depth, inv_proj);
    float3 normal = tiled_normal.Load(int3(tile_pos, 0));
    float3 view_vec = normalize(-pos);
    
    // World-space radius adapted to view depth
    float radius_ws = SAMPLE_RADIUS;
    float radius_vs = radius_ws / abs(pos.z);  // View-space scaling
    
    // Precompute GTAO terms
    float ndotv = dot(normal, view_vec);
    float2 vcrossn = float2(
        view_vec.y * normal.z - view_vec.z * normal.y,
        view_vec.z * normal.x - view_vec.x * normal.z
    );
    
    float ao_accum = 0;
    float slice_weight_accum = 0;
    
    // Slice rotation setup
    float slice_angle_step = 3.14159 / SLICE_COUNT;
    float2x2 slice_rot;
    sincos(slice_angle_step, slice_rot._21, slice_rot._11);
    slice_rot._12 = -slice_rot._21;
    slice_rot._22 = slice_rot._11;
    
    float2 slice_dir = float2(1, 0);
    
    for (int s = 0; s < SLICE_COUNT; s++) {
        slice_dir = mul(slice_dir, slice_rot);
        
        // Project normal onto slice plane for weighting
        float sdotv = dot(slice_dir, view_vec.xy);
        float sdotn = dot(slice_dir, normal.xy);
        float ndotns = dot(slice_dir, vcrossn) * rsqrt(saturate(1 - sdotv * sdotv));
        float slice_weight = sqrt(saturate(1 - ndotns * ndotns));
        
        if (slice_weight < 0.001) continue;
        
        // Normal angle relative to slice
        float cos_n = saturate(ndotv / slice_weight);
        float normal_angle = acos(cos_n);
        normal_angle = (sdotn < sdotv * ndotv) ? -normal_angle : normal_angle;
        
        // Track horizons for both directions (+slice_dir and -slice_dir)
        float2 max_horizon_cos = float2(sin(normal_angle), -sin(normal_angle));
        
        [unroll]
        for (int side = 0; side < 2; side++) {
            float horizon_cos = max_horizon_cos.x;
            
            [loop]
            for (int step = 0; step < STEPS_PER_SLICE; step++) {
                // Quadratic distribution (dense near center, sparse at radius)
                float t = (step + 0.5) / STEPS_PER_SLICE;
                t *= t;
                
                // Sample in tiled space (cache coherent!)
                float2 sample_tile_uv = tile_uv + slice_dir * t * radius_vs * (side ? -1 : 1);
                int2 sample_pos = int2(sample_tile_uv * buffer_size);
                
                // Bounds check against tiled buffer
                if (any(sample_pos < 0) || any(sample_pos >= buffer_size)) break;
                
                float sample_depth = tiled_depth.Load(int3(sample_pos, 0));
                float3 sample_pos_view = GetViewPos(sample_pos, sample_depth, inv_proj);
                float3 delta = sample_pos_view - pos;
                
                float dist_sq = dot(delta, delta);
                float3 delta_dir = delta * rsqrt(dist_sq);
                
                // Horizon angle cosine
                float cos_h = dot(delta_dir, view_vec);
                
                // Distance falloff (GTAO "thickness" heuristic)
                float falloff = saturate(1.0 - dist_sq / (radius_ws * radius_ws));
                cos_h = lerp(horizon_cos, cos_h, falloff);
                
                horizon_cos = max(horizon_cos, cos_h);
            }
            
            max_horizon_cos.x = horizon_cos;
            max_horizon_cos = max_horizon_cos.yx;  // Swap for other side
        }
        
        // Analytical integration over the visible hemisphere slice
        float2 h_angles = float2(-acos(max_horizon_cos.x), acos(max_horizon_cos.y));
        float2 sin_h, cos_h;
        sincos(h_angles, sin_h, cos_h);
        
        // GTAO integral: ∫(cos(θ) - cos(θ_max))cos(θ - θ_n)dθ
        float2 integral = cos_n + 2.0 * h_angles * sin(normal_angle) 
                         - cos(2.0 * h_angles - normal_angle);
        
        ao_accum += (integral.x + integral.y) * slice_weight;
        slice_weight_accum += 1.0;
    }
    
    return saturate(ao_accum / (slice_weight_accum * 4.0));
}
Key Cache Benefits:
tiled_depth.Load(int3(sample_pos, 0)) fetches from a 2D region where sample_pos is spatially local to neighboring threads
A 64-byte cache line holds ~16 depth values (R16_FLOAT), covering a 4×4 neighborhood
When Thread A samples at distance 5 and Thread B samples at distance 5 in the same slice, they hit the same cache line
Stage 3: Gather (Reinterleave)
Reverse the mapping to write AO back to standard render target format for compositing:
hlsl
Copy
uint2 ReinterleavePos(uint2 tile_pos, uint2 grid_size, uint tile_count) {
    uint2 tile_size = (grid_size + tile_count - 1) / tile_count;
    uint2 tile_idx = tile_pos / tile_size;        // Which tile
    uint2 local_pos = tile_pos % tile_size;       // Where in tile
    return local_pos * tile_count + tile_idx;     // Original screen position
}

// In pixel shader or compute:
uint2 screen_pos = ReinterleavePos(dispatch_id, screen_size, TILE_COUNT);
OutputAO[screen_pos] = ComputedAO;
Pipeline Integration for "Bucket Brigade"
Given your synchronization constraints:
Resource Flow:
Input: DepthBuffer + NormalBuffer (from your native hook)
Temp: TiledDepth + TiledNormal (R16_FLOAT + RGBA8/16F, transient)
Temp: TiledAO (R8_UNORM or R16_FLOAT)
Output: FinalAO (matches screen resolution)
Synchronization Points:
plain
Copy
[Depth/Normal In] → Barrier(UAV) → [Scatter CS] → Barrier(UAV) 
→ [GTAO CS] → Barrier(UAV) → [Gather CS/PS] → Barrier(SRV)
→ [Composite/Encode]
Memory Pressure Mitigation:
Since you're encoding simultaneously, the tiled buffers should be small:
For 1920×1080 with 4×4 tiles: Tiled buffers are still 1920×1080, but the access pattern is swizzled
Alternatively, process in screen-space tiles (e.g., 256×256 chunks) to keep working set small:
Scatter 256×256 region → Compute AO → Gather 256×256 region → Repeat
This fits entirely in L2 cache and prevents eviction of encoder data
No Temporal Jitter:
Since your Halton sequence drives the camera subpixel offset, remove all framecount dependencies from the AO shader. The AO should be deterministic for a given camera position so that your 8 temporal samples blend correctly.
No Variable Rate:
Process every tile every frame. Remove any if (tile_idx % 2) return; logic.

The mathematical details of GTAO are subtle and easy to get wrong. Here is the rigorous mathematical breakdown you need for a correct implementation.
View-Space Coordinate System
All calculations happen in view space (camera-relative):
Origin: Camera position
+Z: Forward (into the screen, negative in right-handed systems—verify your matrix)
XY: Image plane aligned
Reconstruction from UV + Depth:
hlsl
Copy
float3 UVToView(float2 uv, float depth, float4x4 inv_proj) {
    float2 ndc = uv * 2.0 - 1.0;
    // Handle projection: if your depth is linear 0..1, adjust accordingly
    float4 view = mul(inv_proj, float4(ndc.x, ndc.y, depth, 1.0));
    return view.xyz / view.w;
}
Critical: The original code uses Camera::z_to_depth and Camera::depth_to_z, suggesting it handles non-linear depth. Ensure your depth input is linear view-space Z before reconstruction.
The GTAO Integral (Type 0)
GTAO analytically integrates the visibility function over the hemisphere, weighted by the cosine lobe (Lambertian BRDF).
For a single slice direction ω 
s
​
  , the visibility V  is:
V=∫ 
−π/2
π/2
​
 ρ(θ)⋅max(0,cos(θ−θ 
n
​
 )−cos(θ 
h
​
 ))dθ 
Where:
θ  is the angle along the slice (0 = forward, perpendicular to view)
θ 
n
​
   is the angle of the surface normal projected onto the slice plane
θ 
h
​
   is the horizon angle (occlusion boundary)
ρ(θ)  is the slice weight (sin of angle between slice plane and normal)
Analytical Solution (derived in Jimenez et al.):
∫cos(θ−θ 
n
​
 )dθ=sin(θ−θ 
n
​
 ) 
∫cos(θ 
h
​
 )dθ=θ⋅cos(θ 
h
​
 ) 
For horizons at angles h 
1
​
   and h 
2
​
   (negative and positive from normal):
Visibility= 
2
1
​
 ∑ 
i=1
2
​
 [sin(h 
i
​
 −θ 
n
​
 )−h 
i
​
 cos(h 
i
​
 −θ 
n
​
 )] 
In the shader code, this appears as:
hlsl
Copy
float2 h = float2(-acos(max_horizon_cos.x), acos(max_horizon_cos.y));
// This is the integral result:
float2 integral = cos_n + 2.0 * h * sin(normal_angle) - cos(2.0 * h - normal_angle);
Where cos_n = cos(θ_n) and normal_angle = θ_n.
The multiplication by sliceweight accounts for the projection of the hemisphere onto the slice plane.
Slice Weighting (Critical Detail)
The slice weight accounts for the fact that slices tangent to the normal contribute less to the final AO than slices aligned with the normal.
hlsl
Copy
// v = view vector (0,0,-1 ideally, but normalize(-position))
// n = surface normal
// slice_dir = (cos(φ), sin(φ)) for slice angle φ

float sdotv = dot(slice_dir, v.xy);
float sdotn = dot(slice_dir, n.xy);

// Cross product of view and normal, projected to determine slice plane alignment
float2 vcrossn_xy = float2(v.y * n.z - v.z * n.y, 
                           v.z * n.x - v.x * n.z);
float ndotns = dot(slice_dir, vcrossn_xy) * rsqrt(saturate(1 - sdotv * sdotv));

// This is sin(γ) where γ is the angle between the slice plane and the normal
float sliceweight = sqrt(saturate(1 - ndotns * ndotns));
Why this matters: If you omit this weighting, concave surfaces (like the inside of a cylinder) will have incorrect occlusion falloff.
Horizon Angle Tracking
For each step along the slice:
hlsl
Copy
// delta = sample_position - current_position
// v = view vector (normalize(-current_position))
float3 delta = sample_view_pos - current_view_pos;
float dist_sq = dot(delta, delta);
float3 delta_dir = delta * rsqrt(dist_sq);

// Cosine of the horizon angle
float cos_horizon = dot(delta_dir, v);

// Distance falloff (GTAO "thickness" heuristic)
// Original uses: falloff = 1 / (1 + distance² / radius²)
float falloff = rcp(1.0 + dist_sq * falloff_factor);
falloff_factor = 1.0 / (radius_ws * radius_ws);

// Attenuate horizon by distance (occluders far away contribute less)
cos_horizon = lerp(lowest_possible, cos_horizon, falloff);
The "Thickness" Heuristic:
GTAO assumes occluders have finite thickness. The falloff effectively models the probability that a distant occluder is actually solid vs. a thin surface. Without this, distant geometry creates "halos" of occlusion.
Normal Angle Bias
The horizon search must be biased by the surface normal. The horizon angles are calculated relative to the view vector, then shifted by the normal angle:
hlsl
Copy
float ndotv = dot(n, v);
float cos_n = saturate(ndotv / sliceweight);  // Cosine of angle between normal and view
float normal_angle = acos(cos_n);

// Determine sign based on which side of the view vector the normal lies
normal_angle = (sdotn < sdotv * ndotv) ? -normal_angle : normal_angle;

// Initialize horizons relative to normal
// cos(normal_angle - π/2) = sin(normal_angle)
// cos(normal_angle + π/2) = -sin(normal_angle)
float2 max_horizon_cos = float2(sin(normal_angle), -sin(normal_angle));
Sample Distribution
Samples must be distributed quadratically along the ray to place more samples near the origin where curvature matters most:
hlsl
Copy
for (int step = 0; step < STEPS_PER_SLICE; step++) {
    // Linear step 0..1
    float t_linear = (step + 0.5) / STEPS_PER_SLICE;
    
    // Quadratic distribution (concentrate near center)
    float t = t_linear * t_linear;
    
    float2 sample_uv = current_uv + slice_dir * t * radius_ss;
}
This is crucial for capturing contact shadows (occlusion very close to the pixel).
Integration Accumulation
The final AO value aggregates all slices:
hlsl
Copy
// For each slice:
float2 h_angles = float2(-acos(max_horizon_cos.x), acos(max_horizon_cos.y));
float2 sin_h, cos_h;
sincos(h_angles, sin_h, cos_h);

// Analytical integration of cosine lobe over visible sector
float slice_ao = 0.5 * dot(
    float2(1,1), 
    sin_h - h_angles * cos_h  // Simplified form of the integral
);

// Or the expanded form from the original code:
float2 integral = cos_n + 2.0 * h_angles * sin(normal_angle) - cos(2.0 * h_angles - normal_angle);
float slice_ao = 0.25 * (integral.x + integral.y);

// Weight by how much this slice aligns with the normal
total_ao += slice_ao * sliceweight;
total_weight += sliceweight;
Final visibility:
hlsl
Copy
float visibility = saturate(total_ao / total_weight);
Solid Angle vs. GTAO (Type 0 vs Type 1)
If you want the Solid Angle approximation (faster, slightly less accurate):
Instead of the analytical integral, use:
hlsl
Copy
// Just the angular width of the visible sector
float2 h_angles = acos(max_horizon_cos);
float slice_ao = dot(h_angles, float2(1,1)) / PI;  // Normalize by PI
This approximates the integral as the solid angle of the visible sky, ignoring the cosine lobe weighting. It's cheaper (no sin, cos of horizons) but darkens flat surfaces too much compared to GTAO.
Summary of Critical Implementation Details
View-space reconstruction: Must be accurate to within floating point epsilon at all depths
Slice weighting: The sqrt(1 - ndotns²) term is essential for correct normal dependence
Quadratic sample distribution: Linear distribution misses contact shadows
Falloff factor: The 1/(1+d²) attenuation prevents distant geometry artifacts
Normal angle sign: The conditional sdotn < sdotv * ndotv determines which side of the view vector the normal projects to—getting this wrong inverts the occlusion on back-facing geometry
Horizon initialization: Start with ±sin(normal_angle), not 0, to clamp the search to the hemisphere above the surface