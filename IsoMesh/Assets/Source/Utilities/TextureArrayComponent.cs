using UnityEngine;

namespace IsoMesh
{
    /// <summary>
    /// Simple component that creates a texture array from assigned textures.
    /// Just drag textures in and click the button!
    /// </summary>
    public class TextureArrayComponent : MonoBehaviour
    {
        [Header("Source Textures")]
        [SerializeField] private Texture2D[] sourceTextures = new Texture2D[4];
        
        [Header("Generated Texture Array")]
        [SerializeField] private Texture2DArray textureArray;
        public Texture2DArray TextureArray => textureArray;
        
        [Header("Settings")]
        [SerializeField] private bool generateMipmaps = true;
        [SerializeField] private Material targetMaterial;
        
        [ContextMenu("Create Texture Array")]
        public void CreateTextureArray()
        {
            if (sourceTextures == null || sourceTextures.Length == 0)
            {
                Debug.LogError("No source textures assigned!");
                return;
            }
            
            // Find first valid texture
            Texture2D firstTex = null;
            int validCount = 0;
            
            foreach (var tex in sourceTextures)
            {
                if (tex != null)
                {
                    if (firstTex == null) firstTex = tex;
                    validCount++;
                }
            }
            
            if (firstTex == null)
            {
                Debug.LogError("No valid textures found!");
                return;
            }
            
            // Check if compressed
            bool isCompressed = IsCompressed(firstTex.format);
            bool useMipmaps = generateMipmaps && !isCompressed;
            
            if (isCompressed && generateMipmaps)
            {
                Debug.LogWarning("Compressed textures detected - disabling mipmaps");
            }
            
            // Create the array
            textureArray = new Texture2DArray(
                firstTex.width, 
                firstTex.height, 
                validCount, 
                firstTex.format, 
                useMipmaps
            );
            
            textureArray.filterMode = FilterMode.Bilinear;
            textureArray.wrapMode = TextureWrapMode.Repeat;
            
            // Copy textures
            int arrayIndex = 0;
            for (int i = 0; i < sourceTextures.Length; i++)
            {
                if (sourceTextures[i] != null)
                {
                    Graphics.CopyTexture(sourceTextures[i], 0, 0, textureArray, arrayIndex, 0);
                    Debug.Log($"✅ Added {sourceTextures[i].name} to array index {arrayIndex}");
                    arrayIndex++;
                }
            }
            
            // Apply if not compressed
            if (!isCompressed)
            {
                textureArray.Apply(useMipmaps);
            }
            
            // Auto-assign to material if available
            if (targetMaterial != null)
            {
                targetMaterial.SetTexture("_TextureArray", textureArray);
                Debug.Log($"✅ Applied texture array to material: {targetMaterial.name}");
            }
            
            Debug.Log($"🎉 Texture array created successfully! {validCount} textures at {firstTex.width}x{firstTex.height}");
        }
        
        [ContextMenu("Clear Texture Array")]
        public void ClearTextureArray()
        {
            if (textureArray != null)
            {
                DestroyImmediate(textureArray);
                textureArray = null;
            }
            
            if (targetMaterial != null)
            {
                targetMaterial.SetTexture("_TextureArray", null);
            }
            
            Debug.Log("Texture array cleared");
        }
        
        private bool IsCompressed(TextureFormat format)
        {
            switch (format)
            {
                case TextureFormat.DXT1:
                case TextureFormat.DXT5:
                case TextureFormat.BC7:
                case TextureFormat.ETC2_RGB:
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_6x6:
                case TextureFormat.ASTC_8x8:
                    return true;
                default:
                    return false;
            }
        }
        
        void OnDestroy()
        {
            if (textureArray != null)
            {
                DestroyImmediate(textureArray);
            }
        }
    }
    
    #if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(TextureArrayComponent))]
    public class TextureArrayComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            GUILayout.Space(15);
            
            TextureArrayComponent component = (TextureArrayComponent)target;
            
            // Create button
            if (GUILayout.Button("CREATE TEXTURE ARRAY", GUILayout.Height(35)))
            {
                component.CreateTextureArray();
            }
            
            GUILayout.Space(5);
            
            // Clear button
            if (GUILayout.Button("Clear Array"))
            {
                component.ClearTextureArray();
            }
            
            GUILayout.Space(10);
            
            // Status
            if (component.TextureArray != null)
            {
                UnityEditor.EditorGUILayout.HelpBox(
                    $"✅ Texture Array Ready!\n" +
                    $"Size: {component.TextureArray.width}x{component.TextureArray.height}\n" +
                    $"Textures: {component.TextureArray.depth}\n" +
                    $"Format: {component.TextureArray.format}", 
                    UnityEditor.MessageType.Info);
            }
            else
            {
                UnityEditor.EditorGUILayout.HelpBox(
                    "Add textures above and click 'CREATE TEXTURE ARRAY'", 
                    UnityEditor.MessageType.Warning);
            }
        }
    }
    #endif
}

