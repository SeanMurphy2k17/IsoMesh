using UnityEngine;

namespace IsoMesh
{
    [CreateAssetMenu(fileName = "TextureArrayCreator", menuName = "IsoMesh/Simple Texture Array Creator")]
    public class SimpleTextureArrayCreator : ScriptableObject
    {
        [Header("Source Textures")]
        public Texture2D[] sourceTextures = new Texture2D[4];
        
        [Header("Generated Array")]
        public Texture2DArray textureArray;
        
        [Header("Settings")]
        public bool generateMipmaps = true;
        
        // This creates the context menu option when you right-click the asset
        [ContextMenu("Generate Texture Array")]
        public void GenerateTextureArray()
        {
            if (sourceTextures == null || sourceTextures.Length == 0)
            {
                Debug.LogError("No source textures assigned!");
                return;
            }
            
            // Find first valid texture for size reference
            Texture2D firstTex = null;
            foreach (var tex in sourceTextures)
            {
                if (tex != null)
                {
                    firstTex = tex;
                    break;
                }
            }
            
            if (firstTex == null)
            {
                Debug.LogError("No valid textures found!");
                return;
            }
            
            // Count valid textures
            int validCount = 0;
            foreach (var tex in sourceTextures)
            {
                if (tex != null) validCount++;
            }
            
            // Check if textures are compressed
            bool isCompressed = IsTextureCompressed(firstTex.format);
            bool useMipmaps = generateMipmaps && !isCompressed;
            
            if (isCompressed && generateMipmaps)
            {
                Debug.LogWarning("Source textures are compressed. Disabling mipmaps to avoid errors.");
            }
            
            // Create texture array
            textureArray = new Texture2DArray(firstTex.width, firstTex.height, validCount, firstTex.format, useMipmaps);
            
            // Copy valid textures
            int arrayIndex = 0;
            for (int i = 0; i < sourceTextures.Length; i++)
            {
                if (sourceTextures[i] != null)
                {
                    Graphics.CopyTexture(sourceTextures[i], 0, 0, textureArray, arrayIndex, 0);
                    arrayIndex++;
                    Debug.Log($"Added texture {i}: {sourceTextures[i].name} → Array index {arrayIndex - 1}");
                }
            }
            
            // Only apply if we're not using mipmaps or textures aren't compressed
            if (!isCompressed)
            {
                textureArray.Apply(updateMipmaps: useMipmaps);
            }
            
            Debug.Log($"✅ Texture array created! {validCount} textures at {firstTex.width}x{firstTex.height}");
            
            // Mark the asset as dirty so it saves
            #if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
            #endif
        }
        
        public bool IsTextureCompressed(TextureFormat format)
        {
            // Check for common compressed formats
            switch (format)
            {
                case TextureFormat.DXT1:
                case TextureFormat.DXT5:
                case TextureFormat.BC4:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC2_RGB:
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_5x5:
                case TextureFormat.ASTC_6x6:
                case TextureFormat.ASTC_8x8:
                case TextureFormat.ASTC_10x10:
                case TextureFormat.ASTC_12x12:
                case TextureFormat.PVRTC_RGB2:
                case TextureFormat.PVRTC_RGBA2:
                case TextureFormat.PVRTC_RGB4:
                case TextureFormat.PVRTC_RGBA4:
                    return true;
                default:
                    return false;
            }
        }
    }
    
    #if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(SimpleTextureArrayCreator))]
    public class SimpleTextureArrayCreatorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            GUILayout.Space(20);
            
            SimpleTextureArrayCreator creator = (SimpleTextureArrayCreator)target;
            
            // Big obvious button
            if (GUILayout.Button("GENERATE TEXTURE ARRAY", GUILayout.Height(40)))
            {
                creator.GenerateTextureArray();
            }
            
            GUILayout.Space(10);
            
            // Status info
            if (creator.textureArray != null)
            {
                UnityEditor.EditorGUILayout.HelpBox(
                    $"✅ Texture Array Generated!\n" +
                    $"Size: {creator.textureArray.width}x{creator.textureArray.height}\n" +
                    $"Textures: {creator.textureArray.depth}\n" +
                    $"Format: {creator.textureArray.format}", 
                    UnityEditor.MessageType.Info);
                    
                GUILayout.Space(5);
                UnityEditor.EditorGUILayout.LabelField("Drag the 'Texture Array' field above into your Shader Graph!", UnityEditor.EditorStyles.helpBox);
            }
            else
            {
                // Count valid textures
                int validCount = 0;
                if (creator.sourceTextures != null)
                {
                    foreach (var tex in creator.sourceTextures)
                        if (tex != null) validCount++;
                }
                
                if (validCount > 0)
                {
                    // Check if any textures are compressed
                    bool hasCompressed = false;
                    foreach (var tex in creator.sourceTextures)
                    {
                        if (tex != null && creator.IsTextureCompressed(tex.format))
                        {
                            hasCompressed = true;
                            break;
                        }
                    }
                    
                    string message = $"Ready to generate! Found {validCount} valid textures.\nClick the button above to create the array.";
                    
                    if (hasCompressed && creator.generateMipmaps)
                    {
                        message += "\n\n⚠️ Compressed textures detected. Mipmaps will be disabled automatically.";
                    }
                    
                    UnityEditor.EditorGUILayout.HelpBox(message, UnityEditor.MessageType.Warning);
                }
                else
                {
                    UnityEditor.EditorGUILayout.HelpBox(
                        "Add some textures to the 'Source Textures' array above, then click Generate.", 
                        UnityEditor.MessageType.Error);
                }
            }
        }
    }
    #endif
}
