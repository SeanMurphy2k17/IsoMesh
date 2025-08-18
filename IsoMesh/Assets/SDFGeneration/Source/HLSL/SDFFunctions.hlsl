#ifndef SDF_FUNCTIONS_INCLUDED
#define SDF_FUNCTIONS_INCLUDED

#include "Common.hlsl"
#include "./Compute_IsoSurfaceExtraction_Structs.hlsl"

float3 sdf_op_translate(float3 p, float3 translation)
{
    return p - translation;
}

float3 sdf_op_rotate(float3 p, float3 axis, float degrees)
{
    return mul(AngleAxis3x3(degrees * DEGREES_TO_RADIANS, axis), p);
}

float3 sdf_op_rotate(float3 p, float3 eulerAngles)
{
    return mul(p, Euler3x3(eulerAngles));
}

float sdf_cylinder(float3 p, float h, float r)
{
    float2 d = abs(float2(length(p.xz), p.y)) - float2(h, r);
    return min(max(d.x, d.y), 0.0) + length(max(d, 0.0));
}

float sdf_box(float3 p, float3 b)
{
    float3 q = abs(p) - b;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

float sdf_roundedBox(float3 p, float3 b, float r)
{
    float3 q = abs(p) - b;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0) - r;
}

float3 sdf_box_normal(float3 p, float3 b)
{
    float3 d = abs(p) - b;
    float3 s = sign(p);
    float g = max(d.x, max(d.y, d.z));
    return s * ((g > 0.0) ? normalize(max(d, 0.0)) : step(d.yzx, d.xyz) * step(d.zxy, d.xyz));
}

float sdf_boxFrame(float3 p, float3 b, float e)
{
    p = abs(p) - b;
    float3 q = abs(p + e) - e;
    return min(min(
      length(max(float3(p.x, q.y, q.z), 0.0)) + min(max(p.x, max(q.y, q.z)), 0.0),
      length(max(float3(q.x, p.y, q.z), 0.0)) + min(max(q.x, max(p.y, q.z)), 0.0)),
      length(max(float3(q.x, q.y, p.z), 0.0)) + min(max(q.x, max(q.y, p.z)), 0.0));
}


float sdf_torus(float3 p, float2 t)
{
    float2 q = float2(length(p.xz) - t.x, p.y);
    return length(q) - t.y;
}

float sdf_link(float3 p, float le, float r1, float r2)
{
    float3 q = float3(p.x, max(abs(p.y) - le, 0.0), p.z);
    return length(float2(length(q.xy) - r1, q.z)) - r2;
}

float2 sdf_uv_planarX(float3 p, float3 b)
{
    float3 q = abs(p) - b;
    float dist = length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
    
    float normalizedX = remap(-b.z, b.z, 0, 1, p.z);
    float normalizedY = remap(-b.y, b.y, 0, 1, p.y);
    return float2(normalizedX, normalizedY);
}

float2 sdf_uv_planarY(float3 p, float3 b)
{
    float3 q = abs(p) - b;
    float dist = length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
    
    float normalizedX = remap(-b.x, b.x, 0, 1, p.x);
    float normalizedY = remap(-b.z, b.z, 0, 1, p.z);
    return float2(normalizedX, normalizedY);
}

float2 sdf_uv_planarZ(float3 p, float3 b)
{
    float3 q = abs(p) - b;
    float dist = length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
    
    float normalizedX = remap(-b.x, b.x, 0, 1, p.x);
    float normalizedY = remap(-b.y, b.y, 0, 1, p.y);
    return float2(normalizedX, normalizedY);
}

float2 sdf_uv_triplanar(float3 p, float3 b, float3 normal)
{
    float2 yzPlane = sdf_uv_planarX(p, b);
    float2 xzPlane = sdf_uv_planarY(p, b);
    float2 xyPlane = sdf_uv_planarZ(p, b);

    return yzPlane * normal.x + xzPlane * normal.y + xyPlane * normal.z;
}

float sdf_plane(float3 p, float3 n, float h)
{
    // n must be normalized
    return dot(p, n) + h;
}

float sdf_roundedCone(float3 p, float r1, float r2, float h)
{
    h = max(h, 0.00001);
    float2 q = float2(length(p.xz), p.y);
    
    float b = (r1 - r2) / h;
    float a = sqrt(1.0 - b * b);
    float k = dot(q, float2(-b, a));
    
    if (k < 0.0)
        return length(q) - r1;
    
    if (k > a * h)
        return length(q - float2(0.0, h)) - r2;
        
    return dot(q, float2(a, b)) - r1;
}

float sdf_sphere(float3 p, float radius)
{
    return length(p) - radius;
}

float2 sdf_uv_sphere(float3 p, float radius)
{
    float len = length(p);
    float distance = len - radius;
    float3 normalized = p / len;
    float verticalness = dot(normalized, float3(0, 1, 0));
    float normalizedHeight = (verticalness + 1.0) * 0.5;
    
    float rotatiness = (atan2(normalized.z, normalized.x) + PI) / (PI * 2);
    
    return float2(rotatiness, normalizedHeight);
}

// SUPER SIMPLE PLANAR UV PROJECTIONS - Perfect for terrain!
float2 sdf_uv_world_xz(float3 worldPos, float tileSize)
{
    // Simple XZ plane projection (top-down view)
    return worldPos.xz / tileSize;
}

float2 sdf_uv_world_xy(float3 worldPos, float tileSize)
{
    // Front-facing projection 
    return worldPos.xy / tileSize;
}

float2 sdf_uv_world_yz(float3 worldPos, float tileSize)
{
    // Side-facing projection
    return worldPos.yz / tileSize;
}

float2 sdf_uv_local_xz(float3 localPos, float tileSize)
{
    // Local space XZ projection (relative to object)
    return localPos.xz / tileSize;
}

// ADAPTIVE NORMAL-BASED PROJECTION - picks best plane based on surface normal!
float2 sdf_uv_adaptive_planar(float3 localPos, float3 normal, float tileSize)
{
    // Find the dominant axis of the normal
    float3 absNormal = abs(normal);
    
    // Project onto the plane most aligned with the surface normal
    if (absNormal.y > absNormal.x && absNormal.y > absNormal.z)
    {
        // Y is dominant - use XZ plane (top/bottom faces)
        return localPos.xz / tileSize;
    }
    else if (absNormal.x > absNormal.z)
    {
        // X is dominant - use YZ plane (left/right faces)
        return localPos.yz / tileSize;
    }
    else
    {
        // Z is dominant - use XY plane (front/back faces)
        return localPos.xy / tileSize;
    }
}

// TRIPLANAR WITH NORMAL WEIGHTING - blends all three projections based on normal
float2 sdf_uv_smart_triplanar(float3 localPos, float3 normal, float tileSize)
{
    // Calculate all three projections
    float2 yzPlane = localPos.yz / tileSize;  // X-facing surfaces
    float2 xzPlane = localPos.xz / tileSize;  // Y-facing surfaces  
    float2 xyPlane = localPos.xy / tileSize;  // Z-facing surfaces
    
    // Use normal to weight the projections
    float3 blendWeights = abs(normal);
    blendWeights = blendWeights / (blendWeights.x + blendWeights.y + blendWeights.z);
    
    return yzPlane * blendWeights.x + xzPlane * blendWeights.y + xyPlane * blendWeights.z;
}

// ULTRA SIMPLE UV PROJECTION - guaranteed to work, might have seams but no skewing!
float2 sdf_uv_ultra_simple(float3 localPos, float tileSize)
{
    // Just use XZ coordinates, super simple
    return localPos.xz / tileSize;
}

// VOXEL-BASED UVs - Use the SDF voxel grid coordinates directly as UVs (GENIUS!)
float2 sdf_uv_voxel_based(float3 worldPos, float cellSize, int samplesPerSide)
{
    // Convert world position back to voxel grid coordinates
    float gridBounds = ((float)samplesPerSide - 1.0) * cellSize * 0.5;
    float3 normalizedGridPos = (worldPos + gridBounds) / (gridBounds * 2.0); // [0,1] range
    
    // Use XZ plane of the voxel grid as UV coordinates
    return normalizedGridPos.xz; // Perfect 0-1 UV coordinates from the grid!
}

// LAZY GRID-BASED UVs - Use voxel grid coordinates as UVs (ZERO math, guaranteed to work!)
float2 sdf_uv_grid_based(float3 worldPos, float gridSize, float uvScale)
{
    // Convert world position to normalized grid coordinates [0,1]
    float3 gridPos = (worldPos + gridSize * 0.5) / gridSize; // Center around origin
    return gridPos.xz * uvScale; // Use XZ plane of grid as UV
}

// DISTANCE-BASED UVs - Use distance from origin (great for radial patterns!)
float2 sdf_uv_distance_based(float3 localPos, float scale)
{
    float dist = length(localPos.xz);
    float angle = atan2(localPos.z, localPos.x) / (2.0 * 3.14159) + 0.5; // Normalize to [0,1]
    return float2(angle, dist / scale);
}

// Note: For world-space UVs, transform the object instead of the UVs!

// polynomial smooth min (k = 0.1);
float sdf_op_smin(float a, float b, float k)
{
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * (1.0 / 4.0);
}

//// smooth min but also smoothly combines associated float3s (e.g. colours)
//float sdf_op_smin_colour(float d1, float d2, float3 v1, float3 v2, float k, float vSmoothing, out float3 vResult)
//{
//    float h = saturate(0.5 + 0.5 * (d2 - d1) / k);
//    float vH = saturate(0.5 + 0.5 * (d2 - d1) / vSmoothing);
    
//    vResult = lerp(v2, v1, vH);
//    return lerp(d2, d1, h) - k * h * (1.0 - h);
//}

// smooth min but also smoothly combines associated material
float sdf_op_smin_material(float d1, float d2, SDFMaterialGPU v1, SDFMaterialGPU v2, float k, float vSmoothing, out SDFMaterialGPU vResult)
{
    float h = saturate(0.5 + 0.5 * (d2 - d1) / k);
    float vH = saturate(0.5 + 0.5 * (d2 - d1) / vSmoothing);
    
    vResult = lerpMaterial(v2, v1, vH);
    return lerp(d2, d1, h) - k * h * (1.0 - h);
}

float sdf_op_smoothIntersection(float d1, float d2, float k)
{
    float h = saturate(0.5 - 0.5 * (d2 - d1) / k);
    return lerp(d2, d1, h) + k * h * (1.0 - h);
}

//// smooth intersection but also smoothly intersects associated float3s (e.g. colours)
//float sdf_op_smoothIntersection_colour(float d1, float d2, float3 v1, float3 v2, float k, float vSmoothing, out float3 vResult)
//{
//    float h = saturate(0.5 - 0.5 * (d2 - d1) / k);
//    float vH = saturate(0.5 - 0.5 * (d2 - d1) / vSmoothing);
    
//    vResult = lerp(v2, v1, vH);
//    return lerp(d2, d1, h) + k * h * (1.0 - h);
//}


// smooth intersection but also smoothly intersects associated materials
float sdf_op_smoothIntersection_material(float d1, float d2, SDFMaterialGPU v1, SDFMaterialGPU v2, float k, float vSmoothing, out SDFMaterialGPU vResult)
{
    //d2 = -abs(d2);
    //vSmoothing = -max(0.00001, vSmoothing);
    
    float h = saturate(0.5 - 0.5 * (d2 - d1) / k);
    float vH = saturate(0.5 - 0.5 * (d1 - d2) / vSmoothing);
    
    vResult = lerpMaterial(v2, v1, vH);
    return lerp(d2, d1, h) + k * h * (1.0 - h);
}


float sdf_op_smoothSubtraction(float d1, float d2, float k)
{
    float h = saturate(0.5 - 0.5 * (d2 + d1) / k);
    return lerp(d2, -d1, h) + k * h * (1.0 - h);
}

// smooth subtraction but also smoothly subtracts associated float3s (e.g. colours)
float sdf_op_smoothSubtraction_material(float d1, float d2, SDFMaterialGPU v1, SDFMaterialGPU v2, float k, float vSmoothing, out SDFMaterialGPU vResult)
{
    d1 = abs(d1);
    float h = saturate(0.5 + 0.5 * (d2 - d1) / k);
    float vH = saturate(0.5 + 0.5 * (d2 - d1) / vSmoothing);
    
    vResult = lerpMaterial(v2, v1, vH);
    return lerp(d2, d1, h) - k * h * (1.0 - h);
}

float3 sdf_op_twist(float3 p, float k)
{
    float c = cos(k * p.y);
    float s = sin(k * p.y);
    float2x2 m = float2x2(c, -s, s, c);
    return float3(mul(p.xz, m), p.y);
}

float3 sdf_op_elongate(float3 p, float3 h, float4x4 transform)
{
    float3 translation = float3(transform._m03, transform._m13, transform._m23);
    p = mul(transform, float4(p, 0.0)).xyz;
    p = p - clamp(p + translation, -h, h);
    return mul(float4(p, 0.0), transform).xyz;
}

float3 sdf_op_round(float3 p, float rad)
{
    return p - rad;
}

float sdf_op_onion(float dist, float thickness, int count)
{
    //count = clamp(count, 0, 16);
    
    //[fastopt]
    //for (int iter = 0; iter < count; iter++)
    //{
    //    dist = abs(dist) - thickness;
    //    thickness /= 2;
    //}
    
    return dist;
    
}

float3 sdf_op_bendX(float3 p, float angle)
{
    angle *= DEGREES_TO_RADIANS;
    
    float c = cos(angle * p.y);
    float s = sin(angle * p.y);
    float2x2 m = float2x2(c, -s, s, c);
    return float3(p.x, mul(m, p.yz));
}

float3 sdf_op_bendY(float3 p, float angle)
{
    angle *= DEGREES_TO_RADIANS;
    
    float c = cos(angle * p.x);
    float s = sin(angle * p.x);
    float2x2 m = float2x2(c, -s, s, c);
    
    float2 xz = mul(m, p.xz);
    return float3(xz.x, p.y, xz.y);
}

float3 sdf_op_bendZ(float3 p, float angle)
{
    angle *= DEGREES_TO_RADIANS;
    
    float c = cos(angle * p.y);
    float s = sin(angle * p.y);
    float2x2 m = float2x2(c, -s, s, c);
    return float3(mul(m, p.xy), p.z);
}

#endif // SDF_FUNCTIONS_INCLUDED