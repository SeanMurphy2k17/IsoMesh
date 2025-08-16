#ifndef TERRAIN_BLENDING_MAIN_INCLUDED
#define TERRAIN_BLENDING_MAIN_INCLUDED

void SimpleTerrainTexture_float(
    float4 vertexColor,
    float2 uv,
    float uvScale,
    float arrayLength,
    out float4 finalColor
)
{
    // R channel = texture index (0 to arrayLength-1)
    int textureIndex = round(vertexColor.r * (arrayLength - 1.0));
    
    // Scale UV coordinates  
    float2 scaledUV = uv * uvScale;
    
    // For now, just return vertex color for testing
    // TODO: Replace with actual texture sampling once we figure out Shader Graph texture inputs
    finalColor = float4(textureIndex / (arrayLength - 1.0), vertexColor.g, vertexColor.b, 1.0);
    
    // G channel available for future effects (wetness, snow, dirt, etc.)
    // B channel available for other effects
}

#endif // TERRAIN_BLENDING_MAIN_INCLUDED
