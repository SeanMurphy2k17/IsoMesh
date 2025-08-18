#ifndef TERRAIN_BLENDING_TRIPLANAR_INCLUDED
#define TERRAIN_BLENDING_TRIPLANAR_INCLUDED

void ProcessTerrainVertexTriplanar_float(
    float4 vertexColor,
    float3 worldPosition,
    float3 worldNormal,
    UnityTexture2DArray textureArray,
    UnitySamplerState samplerState,
    float uvScale,
    bool showVertexColors,
    bool useTriplanar,
    out float4 finalColor
)
{
    // Decode vertex colors
    float baseIndex = round(vertexColor.r * 7.0);
    float paintIndex = round(vertexColor.g * 7.0);
    float blendFactor = vertexColor.b;
    
    float4 baseColor, paintColor;
    
    if (useTriplanar)
    {
        // Triplanar mapping for complex 3D surfaces
        float3 blendWeights = abs(worldNormal);
        blendWeights = blendWeights / (blendWeights.x + blendWeights.y + blendWeights.z);
        
        // Sample each plane
        float2 uvX = worldPosition.yz * uvScale;
        float2 uvY = worldPosition.xz * uvScale;
        float2 uvZ = worldPosition.xy * uvScale;
        
        // Base texture triplanar sampling
        float4 baseX = SAMPLE_TEXTURE2D_ARRAY(textureArray.tex, samplerState.samplerstate, uvX, baseIndex);
        float4 baseY = SAMPLE_TEXTURE2D_ARRAY(textureArray.tex, samplerState.samplerstate, uvY, baseIndex);
        float4 baseZ = SAMPLE_TEXTURE2D_ARRAY(textureArray.tex, samplerState.samplerstate, uvZ, baseIndex);
        baseColor = baseX * blendWeights.x + baseY * blendWeights.y + baseZ * blendWeights.z;
        
        // Paint texture triplanar sampling
        float4 paintX = SAMPLE_TEXTURE2D_ARRAY(textureArray.tex, samplerState.samplerstate, uvX, paintIndex);
        float4 paintY = SAMPLE_TEXTURE2D_ARRAY(textureArray.tex, samplerState.samplerstate, uvY, paintIndex);
        float4 paintZ = SAMPLE_TEXTURE2D_ARRAY(textureArray.tex, samplerState.samplerstate, uvZ, paintIndex);
        paintColor = paintX * blendWeights.x + paintY * blendWeights.y + paintZ * blendWeights.z;
    }
    else
    {
        // Standard UV mapping
        float2 scaledUV = worldPosition.xz * uvScale; // Top-down projection
        baseColor = SAMPLE_TEXTURE2D_ARRAY(textureArray.tex, samplerState.samplerstate, scaledUV, baseIndex);
        paintColor = SAMPLE_TEXTURE2D_ARRAY(textureArray.tex, samplerState.samplerstate, scaledUV, paintIndex);
    }
    
    // Blend and output
    float4 blendedColor = lerp(baseColor, paintColor, blendFactor);
    finalColor = showVertexColors ? vertexColor : blendedColor;
}

#endif // TERRAIN_BLENDING_TRIPLANAR_INCLUDED
