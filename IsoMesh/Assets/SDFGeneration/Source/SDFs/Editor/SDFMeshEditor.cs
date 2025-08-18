using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace IsoMesh.Editor
{
    [CustomEditor(typeof(SDFMesh))]
    [CanEditMultipleObjects]
    public class SDFMeshEditor : UnityEditor.Editor
    {
        private static class Labels
        {
            public static GUIContent MeshAsset = new GUIContent("Mesh Asset", "An SDFMeshAsset ScriptableObject. You can create these in 'Tools/Mesh to SDF'");
            public static GUIContent Operation = new GUIContent("Operation", "How this primitive is combined with the previous SDF objects in the hierarchy.");
            public static GUIContent Flip = new GUIContent("Flip", "Turn this object inside out.");
            public static GUIContent Smoothing = new GUIContent("Smoothing", "How smoothly this sdf blends with the previous SDFs.");
            public static GUIContent UsePackedUVs = new GUIContent("Use Packed UVs", "Use original mesh UVs instead of procedural triplanar mapping");

            public static GUIContent Material = new GUIContent("Material", "The visual properties of this SDF object.");
            public static GUIContent MaterialType = new GUIContent("Type", "Whether this object has no effect on the group colours, just a pure colour, or is fully textured.");
            public static GUIContent MaterialTexture = new GUIContent("Texture", "The texture which will be applied to this object via the UVs of the original mesh.");
            public static GUIContent Colour = new GUIContent("Colour", "Colour of this SDF object.");
            public static GUIContent Emission = new GUIContent("Emission", "Emission of this primitive, must be used alongside post processing (bloom).");
            public static GUIContent MaterialSmoothing = new GUIContent("Material Smoothing", "How sharply this material is combined with other SDF objects.");
            public static GUIContent Metallic = new GUIContent("Metallic", "Metallicity of this object's material.");
            public static GUIContent Smoothness = new GUIContent("Smoothness", "Smoothness of this object's material.");
            public static GUIContent SubsurfaceColour = new GUIContent("Subsurface Colour", "Colour of the inside of this SDF object, used by subsurface scattering.");
            public static GUIContent SubsurfaceScatteringPower = new GUIContent("Subsurface Scattering Power", "Strength of the subsurface scattering effect.");
            
            // Noise fields
            public static GUIContent UseNoise = new GUIContent("Use Noise", "Enable Perlin noise displacement for surface detail.");
            public static GUIContent NoiseSeed = new GUIContent("Noise Seed", "Seed value for deterministic noise generation.");
            public static GUIContent NoiseOctaves = new GUIContent("Noise Octaves", "Number of noise layers for complexity.");
            public static GUIContent NoiseFrequency = new GUIContent("Noise Frequency", "Frequency/scale of the noise pattern.");
            public static GUIContent NoiseAmplitude = new GUIContent("Noise Amplitude", "Strength/displacement amount of the noise.");
            public static GUIContent NoiseTimeScale = new GUIContent("Noise Time Scale", "Speed of noise animation (0 = static).");
            
            public static string MeshAssetRequiredMessage = "SDF Mesh objects must have a reference to an SDFMeshAsset ScriptableObject. You can create these in 'Tools/Mesh to SDF'";
        }

        private class SerializedProperties
        {
            public SerializedProperty MeshAsset { get; }
            public SerializedProperty Operation { get; }
            public SerializedProperty Flip { get; }
            public SerializedProperty Smoothing { get; }
            public SerializedProperty UsePackedUVs { get; }

            public SerializedProperty Material { get; }
            public SerializedProperty MaterialType { get; }
            public SerializedProperty MaterialTexture { get; }
            public SerializedProperty Colour { get; }
            public SerializedProperty Emission { get; }
            public SerializedProperty MaterialSmoothing { get; }
            public SerializedProperty Metallic { get; }
            public SerializedProperty Smoothness { get; }
            public SerializedProperty SubsurfaceColour { get; }
            public SerializedProperty SubsurfaceScatteringPower { get; }
            
            // Noise properties
            public SerializedProperty UseNoise { get; }
            public SerializedProperty NoiseSeed { get; }
            public SerializedProperty NoiseOctaves { get; }
            public SerializedProperty NoiseFrequency { get; }
            public SerializedProperty NoiseAmplitude { get; }
            public SerializedProperty NoiseTimeScale { get; }

            public SerializedProperties(SerializedObject serializedObject)
            {
                MeshAsset = serializedObject.FindProperty("m_asset");
                Operation = serializedObject.FindProperty("m_operation");
                Flip = serializedObject.FindProperty("m_flip");
                Smoothing = serializedObject.FindProperty("m_smoothing");
                UsePackedUVs = serializedObject.FindProperty("m_usePackedUVs");

                Material = serializedObject.FindProperty("m_material");
                MaterialType = Material.FindPropertyRelative("m_type");
                MaterialTexture = Material.FindPropertyRelative("m_texture");
                Colour = Material.FindPropertyRelative("m_colour");
                Emission = Material.FindPropertyRelative("m_emission");
                MaterialSmoothing = Material.FindPropertyRelative("m_materialSmoothing");
                Metallic = Material.FindPropertyRelative("m_metallic");
                Smoothness = Material.FindPropertyRelative("m_smoothness");
                SubsurfaceColour = Material.FindPropertyRelative("m_subsurfaceColour");
                SubsurfaceScatteringPower = Material.FindPropertyRelative("m_subsurfaceScatteringPower");
                
                // Noise properties
                UseNoise = Material.FindPropertyRelative("m_useNoise");
                NoiseSeed = Material.FindPropertyRelative("m_noiseSeed");
                NoiseOctaves = Material.FindPropertyRelative("m_noiseOctaves");
                NoiseFrequency = Material.FindPropertyRelative("m_noiseFrequency");
                NoiseAmplitude = Material.FindPropertyRelative("m_noiseAmplitude");
                NoiseTimeScale = Material.FindPropertyRelative("m_noiseTimeScale");
            }
        }


        private SerializedProperties m_serializedProperties;
        private SDFMesh m_sdfMesh;
        private SerializedPropertySetter m_setter;

        private bool m_isMaterialOpen = true;

        private void OnEnable()
        {
            m_serializedProperties = new SerializedProperties(serializedObject);
            m_sdfMesh = target as SDFMesh;
            m_setter = new SerializedPropertySetter(serializedObject);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.DrawScript();
            m_setter.Clear();
            
            m_setter.DrawProperty(Labels.MeshAsset, m_serializedProperties.MeshAsset);

            bool hasMeshAsset = m_serializedProperties.MeshAsset.objectReferenceValue;

            if (!hasMeshAsset)
                EditorGUILayout.HelpBox(Labels.MeshAssetRequiredMessage, MessageType.Warning);

            GUI.enabled = hasMeshAsset;

            m_setter.DrawProperty(Labels.Operation, m_serializedProperties.Operation);
            m_setter.DrawProperty(Labels.Flip, m_serializedProperties.Flip);
            m_setter.DrawProperty(Labels.UsePackedUVs, m_serializedProperties.UsePackedUVs);
            m_setter.DrawFloatSetting(Labels.Smoothing, m_serializedProperties.Smoothing, min: 0f);
            
            if (m_isMaterialOpen = EditorGUILayout.Foldout(m_isMaterialOpen, Labels.Material, true))
            {
                using (EditorGUILayout.VerticalScope box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (EditorGUI.IndentLevelScope indent = new EditorGUI.IndentLevelScope())
                    {
                        m_setter.DrawProperty(Labels.MaterialType, m_serializedProperties.MaterialType);

                        if (m_sdfMesh.Material.Type != SDFMaterial.MaterialType.None)
                        {
                            if (m_sdfMesh.Material.Type == SDFMaterial.MaterialType.Texture)
                                m_setter.DrawProperty(Labels.MaterialTexture, m_serializedProperties.MaterialTexture);

                            m_setter.DrawProperty(Labels.Colour, m_serializedProperties.Colour);
                            m_setter.DrawFloatSetting(Labels.MaterialSmoothing, m_serializedProperties.MaterialSmoothing, min: 0f);
                            m_setter.DrawProperty(Labels.Emission, m_serializedProperties.Emission);
                            m_setter.DrawProperty(Labels.Metallic, m_serializedProperties.Metallic);
                            m_setter.DrawProperty(Labels.Smoothness, m_serializedProperties.Smoothness);
                            m_setter.DrawProperty(Labels.SubsurfaceColour, m_serializedProperties.SubsurfaceColour);
                            m_setter.DrawProperty(Labels.SubsurfaceScatteringPower, m_serializedProperties.SubsurfaceScatteringPower);
                            
                            // Noise settings
                            EditorGUILayout.Space();
                            EditorGUILayout.LabelField("Surface Detail", EditorStyles.boldLabel);
                            m_setter.DrawProperty(Labels.UseNoise, m_serializedProperties.UseNoise);
                            
                            if (m_serializedProperties.UseNoise.boolValue)
                            {
                                using (EditorGUI.IndentLevelScope indent2 = new EditorGUI.IndentLevelScope())
                                {
                                    m_setter.DrawProperty(Labels.NoiseSeed, m_serializedProperties.NoiseSeed);
                                    m_setter.DrawIntSetting(Labels.NoiseOctaves, m_serializedProperties.NoiseOctaves, min: 1, max: 8);
                                    m_setter.DrawFloatSetting(Labels.NoiseFrequency, m_serializedProperties.NoiseFrequency, min: 0.001f);
                                    m_setter.DrawFloatSetting(Labels.NoiseAmplitude, m_serializedProperties.NoiseAmplitude, min: 0f);
                                    m_setter.DrawFloatSetting(Labels.NoiseTimeScale, m_serializedProperties.NoiseTimeScale, min: 0f);
                                }
                            }
                        }
                    }
                }
            }

            m_setter.Update();

            GUI.enabled = true;
        }
    }
}