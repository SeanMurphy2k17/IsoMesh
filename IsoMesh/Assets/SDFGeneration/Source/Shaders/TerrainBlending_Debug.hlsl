#ifndef TERRAIN_BLENDING_DEBUG_INCLUDED
#define TERRAIN_BLENDING_DEBUG_INCLUDED

void DebugTextureIndices_float(
    float4 vertexColor,
    out float4 debugColor
)
{
    float baseIndex = round(vertexColor.r * 7.0);
    float paintIndex = round(vertexColor.g * 7.0);
    float blendFactor = vertexColor.b;
    
    // Color-code texture indices for easy identification
    float3 indexColor;
    if (baseIndex == 0) indexColor = float3(1.0, 0.0, 0.0);      // Red
    else if (baseIndex == 1) indexColor = float3(0.0, 1.0, 0.0); // Green
    else if (baseIndex == 2) indexColor = float3(0.0, 0.0, 1.0); // Blue
    else if (baseIndex == 3) indexColor = float3(1.0, 1.0, 0.0); // Yellow
    else if (baseIndex == 4) indexColor = float3(1.0, 0.0, 1.0); // Magenta
    else if (baseIndex == 5) indexColor = float3(0.0, 1.0, 1.0); // Cyan
    else if (baseIndex == 6) indexColor = float3(1.0, 0.5, 0.0); // Orange
    else indexColor = float3(0.5, 0.5, 0.5);                    // Gray (index 7)
    
    debugColor = float4(indexColor, 1.0);
}

#endif // TERRAIN_BLENDING_DEBUG_INCLUDED
