using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace IsoMesh
{
    [System.Serializable]
    public struct SDFMaterial
    {
        public const float MIN_SMOOTHING = 0.0001f;

        [SerializeField]
        private MaterialType m_type;
        public MaterialType Type => m_type;

        [SerializeField]
        private Texture2D m_texture;
        public Texture2D Texture => m_texture;

        [SerializeField]
        [ColorUsage(showAlpha: false)]
        private Color m_colour;
        public Color Colour => m_colour;

        [SerializeField]
        [ColorUsage(showAlpha: false, hdr: true)]
        private Color m_emission;
        public Color Emission => m_emission;

        [SerializeField]
        private float m_materialSmoothing;
        public float MaterialSmoothing => m_materialSmoothing;

        [SerializeField]
        [Range(0f, 1f)]
        private float m_metallic;
        public float Metallic => m_metallic;

        [SerializeField]
        [Range(0f, 1f)]
        private float m_smoothness;
        public float Smoothness => m_smoothness;

        [SerializeField]
        [ColorUsage(showAlpha: false)]
        private Color m_subsurfaceColour;
        public Color SubsurfaceColour => m_subsurfaceColour;

        [SerializeField]
        [Min(0f)]
        private float m_subsurfaceScatteringPower;
        public float SubsurfaceScatteringPower => m_subsurfaceScatteringPower;

        // Layered material support for splatmapping
        [SerializeField]
        private Texture2D[] m_layerTextures;
        public Texture2D[] LayerTextures => m_layerTextures;

        [SerializeField]
        private Vector2[] m_layerScales;
        public Vector2[] LayerScales => m_layerScales;

        [SerializeField]
        private RenderTexture m_splatMap;
        public RenderTexture SplatMap => m_splatMap;

        [SerializeField]
        private Vector2 m_worldBounds;
        public Vector2 WorldBounds => m_worldBounds;

        // Note: UsePackedUVs moved to SDFMesh component for better visibility
        [SerializeField]
        private bool m_usePackedUVs;
        public bool UsePackedUVs 
        { 
            get => m_usePackedUVs; 
            set => m_usePackedUVs = value; 
        }

        // Surface Detail (Noise)
        [Header("Surface Detail")]
        [SerializeField]
        private bool m_useNoise;
        public bool UseNoise 
        { 
            get => m_useNoise; 
            set => m_useNoise = value; 
        }

        [SerializeField]
        private float m_noiseSeed;
        public float NoiseSeed 
        { 
            get => m_noiseSeed; 
            set => m_noiseSeed = value; 
        }

        [SerializeField]
        [Range(1, 4)]
        private int m_noiseOctaves;
        public int NoiseOctaves 
        { 
            get => m_noiseOctaves; 
            set => m_noiseOctaves = value; 
        }

        [SerializeField]
        [Min(0.1f)]
        private float m_noiseFrequency;
        public float NoiseFrequency 
        { 
            get => m_noiseFrequency; 
            set => m_noiseFrequency = value; 
        }

        [SerializeField]
        [Range(0f, 1f)]
        private float m_noiseAmplitude;
        public float NoiseAmplitude 
        { 
            get => m_noiseAmplitude; 
            set => m_noiseAmplitude = value; 
        }

        [SerializeField]
        [Tooltip("Animation speed (0 = static noise)")]
        private float m_noiseTimeScale;
        public float NoiseTimeScale 
        { 
            get => m_noiseTimeScale; 
            set => m_noiseTimeScale = value; 
        }

        public enum MaterialType
        {
            None,
            Colour,
            Texture,
            Layered
        }

        public SDFMaterial(Color mainCol, Color emission, float metallic, float smoothness, Color subsurfaceColour, float subsurfaceScatteringPower, float materialSmoothing)
        {
            m_type = MaterialType.Colour;
            m_texture = default;
            m_colour = mainCol;
            m_emission = emission;
            m_metallic = metallic;
            m_smoothness = smoothness;
            m_subsurfaceColour = subsurfaceColour;
            m_subsurfaceScatteringPower = subsurfaceScatteringPower;
            m_materialSmoothing = materialSmoothing;
            
            // Initialize layered material fields
            m_layerTextures = null;
            m_layerScales = null;
            m_splatMap = null;
            m_worldBounds = Vector2.one * 100f;
            m_usePackedUVs = false;
            
            // Initialize noise fields
            m_useNoise = false;
            m_noiseSeed = 12345f;
            m_noiseOctaves = 2;
            m_noiseFrequency = 1.0f;
            m_noiseAmplitude = 0.1f;
            m_noiseTimeScale = 0.0f;
        }

        // Constructor for layered materials
        public SDFMaterial(Texture2D[] layerTextures, Vector2[] layerScales, RenderTexture splatMap, Vector2 worldBounds, float materialSmoothing)
        {
            m_type = MaterialType.Layered;
            m_texture = default;
            m_colour = Color.white;
            m_emission = Color.black;
            m_metallic = 0f;
            m_smoothness = 0.5f;
            m_subsurfaceColour = Color.white;
            m_subsurfaceScatteringPower = 1f;
            m_materialSmoothing = materialSmoothing;
            
            m_layerTextures = layerTextures;
            m_layerScales = layerScales;
            m_splatMap = splatMap;
            m_worldBounds = worldBounds;
            m_usePackedUVs = false;
            
            // Initialize noise fields
            m_useNoise = false;
            m_noiseSeed = 12345f;
            m_noiseOctaves = 2;
            m_noiseFrequency = 1.0f;
            m_noiseAmplitude = 0.1f;
            m_noiseTimeScale = 0.0f;
        }
    }

    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct SDFMaterialGPU
    {
        public static int Stride => sizeof(float) * 18 + sizeof(int) * 5;

        public int MaterialType;
        public int TextureIndex;
        public Vector3 Colour;
        public Vector3 Emission;
        public float Metallic;
        public float Smoothness;
        public float Thickness;
        public Vector3 SubsurfaceColour;
        public float SubsurfaceScatteringPower;
        public float MaterialSmoothing;
        public int UsePackedUVs;
        
        // Noise fields
        public int UseNoise;
        public float NoiseSeed;
        public int NoiseOctaves;
        public float NoiseFrequency;
        public float NoiseAmplitude;
        public float NoiseTimeScale;

        public SDFMaterialGPU(SDFMaterial material)
        {
            MaterialType = (int)material.Type;
            TextureIndex = 0;
            Colour = (Vector4)material.Colour;
            Emission = (Vector4)material.Emission;
            Metallic = Mathf.Clamp01(material.Metallic);
            Smoothness = Mathf.Clamp01(material.Smoothness);
            Thickness = 0f;
            SubsurfaceColour = (Vector4)material.SubsurfaceColour;
            SubsurfaceScatteringPower = material.SubsurfaceScatteringPower;//Mathf.Lerp(5f, 0f, material.SubsurfaceScatteringPower);
            MaterialSmoothing = material.MaterialSmoothing;
            UsePackedUVs = material.UsePackedUVs ? 1 : 0;
            
            // Initialize noise fields
            UseNoise = material.UseNoise ? 1 : 0;
            NoiseSeed = material.NoiseSeed;
            NoiseOctaves = material.NoiseOctaves;
            NoiseFrequency = material.NoiseFrequency;
            NoiseAmplitude = material.NoiseAmplitude;
            NoiseTimeScale = material.NoiseTimeScale;
        }
        
        // Constructor for layered materials
        public SDFMaterialGPU(SDFMaterial material, int textureArrayIndex)
        {
            MaterialType = (int)material.Type;
            TextureIndex = textureArrayIndex;
            Colour = (Vector4)material.Colour;
            Emission = (Vector4)material.Emission;
            Metallic = Mathf.Clamp01(material.Metallic);
            Smoothness = Mathf.Clamp01(material.Smoothness);
            Thickness = 0f;
            SubsurfaceColour = (Vector4)material.SubsurfaceColour;
            SubsurfaceScatteringPower = material.SubsurfaceScatteringPower;
            MaterialSmoothing = material.MaterialSmoothing;
            UsePackedUVs = material.UsePackedUVs ? 1 : 0;
            
            // Initialize noise fields
            UseNoise = material.UseNoise ? 1 : 0;
            NoiseSeed = material.NoiseSeed;
            NoiseOctaves = material.NoiseOctaves;
            NoiseFrequency = material.NoiseFrequency;
            NoiseAmplitude = material.NoiseAmplitude;
            NoiseTimeScale = material.NoiseTimeScale;
        }
    }
}