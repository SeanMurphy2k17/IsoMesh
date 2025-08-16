using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IsoMesh
{
    /// <summary>
    /// Simple texture array manager for testing. Creates 8-texture arrays for shader testing.
    /// </summary>
    public class TextureArrayTerrain : MonoBehaviour
    {
        [Header("Test Biome Textures (8 textures)")]
        [SerializeField] private Texture2D[] testTextures = new Texture2D[8];  // Indices 0-7
        
        [Header("Target Material")]
        [SerializeField] private Material targetMaterial;
        
        [Header("Runtime Testing")]
        [Range(0, 7)]
        [SerializeField] private int currentTextureIndex = 0;
        
        [Range(0.1f, 10.0f)]
        [SerializeField] private float uvScale = 1.0f;
        
        [Header("Debug Info")]
        [SerializeField] private bool showDebugInfo = true;
        
        [Header("Generated Texture Array")]
        [SerializeField] private Texture2DArray textureArray;
        public Texture2DArray TextureArray => textureArray;
        
        [Header("Asset Settings")]
        [SerializeField] private string assetPath = "Assets/Data/TextureArrays/";
        [SerializeField] private string assetName = "TestTextureArray";
        
        private int lastTextureIndex = -1;
        private float lastUVScale = -1.0f;
        
        void Start()
        {
            // Auto-find material if not assigned
            if (!targetMaterial)
            {
                var renderer = GetComponent<MeshRenderer>();
                if (renderer) targetMaterial = renderer.material;
            }
        }
        
        void Update()
        {
            if (!targetMaterial || !textureArray) return;
            
            bool changed = false;
            
            // Update texture array index when changed
            if (currentTextureIndex != lastTextureIndex)
            {
                int validIndex = Mathf.Clamp(currentTextureIndex, 0, textureArray.depth - 1);
                
                if (targetMaterial.HasProperty("_TextureIndex"))
                    targetMaterial.SetFloat("_TextureIndex", validIndex);  // For testing individual textures
                
                if (targetMaterial.HasProperty("_arrayLength"))
                    targetMaterial.SetFloat("_arrayLength", textureArray.depth);  // Ensure array length is set
                
                lastTextureIndex = currentTextureIndex;
                changed = true;
                
                if (showDebugInfo)
                    Debug.Log($"Switched to texture array index {validIndex} ({GetTextureName(validIndex)})");
            }
            
            // Update UV scale when changed
            if (Mathf.Abs(uvScale - lastUVScale) > 0.001f)
            {
                if (targetMaterial.HasProperty("_UVScale"))
                    targetMaterial.SetFloat("_UVScale", uvScale);
                
                if (targetMaterial.HasProperty("_TilingScale"))
                    targetMaterial.SetFloat("_TilingScale", uvScale);
                
                lastUVScale = uvScale;
                changed = true;
                
                if (showDebugInfo)
                    Debug.Log($"Updated UV scale to {uvScale}");
            }
        }
        
        /// <summary>
        /// Get texture name for debugging
        /// </summary>
        private string GetTextureName(int index)
        {
            if (index >= 0 && index < 8) return $"Test_Texture_{index}";
            return $"Unknown_{index}";
        }
        
        /// <summary>
        /// Get all test textures
        /// </summary>
        public Texture2D[] GetAllTextures()
        {
            return testTextures;
        }
        
        /// <summary>
        /// Set UV scale programmatically
        /// </summary>
        public void SetUVScale(float scale)
        {
            uvScale = Mathf.Clamp(scale, 0.1f, 10.0f);
            if (targetMaterial)
            {
                if (targetMaterial.HasProperty("_UVScale"))
                    targetMaterial.SetFloat("_UVScale", uvScale);
                
                if (targetMaterial.HasProperty("_TilingScale"))
                    targetMaterial.SetFloat("_TilingScale", uvScale);
                
                lastUVScale = uvScale;
            }
        }
        
        [ContextMenu("Create Texture Array")]
        public void CreateTextureArray()
        {
            var allTextures = GetAllTextures();
            
            // Find first valid texture and count valid ones
            Texture2D firstTex = null;
            int validCount = 0;
            
            foreach (var tex in allTextures)
            {
                if (tex != null)
                {
                    if (firstTex == null) firstTex = tex;
                    validCount++;
                }
            }
            
            if (firstTex == null)
            {
                Debug.LogError("No valid textures found! Please assign textures to the test array.");
                return;
            }
            
            if (validCount < 2)
            {
                Debug.LogWarning($"Only {validCount} textures found. Consider adding more for testing.");
            }
            
            // Check if compressed
            bool isCompressed = IsCompressed(firstTex.format);
            bool useMipmaps = !isCompressed;
            
            if (isCompressed)
            {
                Debug.LogWarning("Compressed textures detected - disabling mipmaps");
            }
            
            // Create texture array (dynamic length based on assigned textures)
            textureArray = new Texture2DArray(
                firstTex.width, 
                firstTex.height, 
                allTextures.Length,  // Dynamic array length
                firstTex.format, 
                useMipmaps
            );
            
            textureArray.filterMode = FilterMode.Bilinear;
            textureArray.wrapMode = TextureWrapMode.Repeat;
            
            // Copy textures to their designated array indices
            int validTextureCount = 0;
            for (int i = 0; i < allTextures.Length; i++)
            {
                if (allTextures[i] != null)
                {
                    Graphics.CopyTexture(allTextures[i], 0, 0, textureArray, i, 0);
                    Debug.Log($"✅ Added {allTextures[i].name} to array index {i} ({GetTextureName(i)})");
                    validTextureCount++;
                }
                else
                {
                    // Create a default texture for missing slots (optional)
                    if (showDebugInfo)
                        Debug.Log($"⚠️ Missing texture at index {i} ({GetTextureName(i)}) - using empty slot");
                }
            }
            
            // Apply if not compressed
            if (!isCompressed)
            {
                textureArray.Apply(useMipmaps);
            }
            
            // Save as persistent asset
            #if UNITY_EDITOR
            SaveTextureArrayAsAsset();
            #endif
            
            // Apply to material (match exact shader property names)
            if (targetMaterial != null)
            {
                Debug.Log($"🔧 Setting properties for material: {targetMaterial.name} (Shader: {targetMaterial.shader.name})");
                
                // Set the exact properties that match the shader
                if (targetMaterial.HasProperty("_MasterTextureArray"))
                {
                    targetMaterial.SetTexture("_MasterTextureArray", textureArray);
                    Debug.Log("✅ Set _MasterTextureArray");
                }
                else
                {
                    Debug.LogWarning("❌ _MasterTextureArray property NOT FOUND!");
                }
                
                if (targetMaterial.HasProperty("_arrayLength"))
                {
                    targetMaterial.SetFloat("_arrayLength", textureArray.depth);
                    Debug.Log($"✅ Set _arrayLength = {textureArray.depth}");
                }
                else
                {
                    Debug.LogWarning("❌ _arrayLength property NOT FOUND!");
                }
                
                if (targetMaterial.HasProperty("_UVScale"))
                {
                    targetMaterial.SetFloat("_UVScale", uvScale);
                    Debug.Log($"✅ Set _UVScale = {uvScale}");
                }
                else
                {
                    Debug.LogWarning("❌ _UVScale property NOT FOUND!");
                }
                
                if (targetMaterial.HasProperty("_TilingScale"))
                {
                    targetMaterial.SetFloat("_TilingScale", uvScale);
                    Debug.Log($"✅ Set _TilingScale = {uvScale}");
                }
                else
                {
                    Debug.LogWarning("❌ _TilingScale property NOT FOUND!");
                }
                
                // Optional: Set current texture index for preview (if property exists)
                if (targetMaterial.HasProperty("_TextureIndex"))
                {
                    targetMaterial.SetFloat("_TextureIndex", currentTextureIndex);
                    Debug.Log($"✅ Set _TextureIndex = {currentTextureIndex} (optional preview)");
                }
                
                Debug.Log($"✅ Applied texture array to material: {targetMaterial.name} (Array Length: {textureArray.depth})");
            }
            
            Debug.Log($"🎉 Test texture array created! {validTextureCount}/{textureArray.depth} slots filled at {firstTex.width}x{firstTex.height}");
        }
        
        [ContextMenu("Clear Texture Array")]
        public void ClearTextureArray()
        {
            if (textureArray != null)
            {
                #if UNITY_EDITOR
                // If it's a saved asset, optionally delete the file
                string assetPath = AssetDatabase.GetAssetPath(textureArray);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    Debug.Log($"🗑️ Removing texture array asset: {assetPath}");
                    AssetDatabase.DeleteAsset(assetPath);
                    AssetDatabase.Refresh();
                }
                else
                {
                    DestroyImmediate(textureArray);
                }
                #else
                DestroyImmediate(textureArray);
                #endif
                
                textureArray = null;
            }
            
            if (targetMaterial)
            {
                // Clear shader properties that match the shader
                if (targetMaterial.HasProperty("_MasterTextureArray"))
                {
                    targetMaterial.SetTexture("_MasterTextureArray", null);
                    Debug.Log("🧹 Cleared _MasterTextureArray");
                }
                
                if (targetMaterial.HasProperty("_arrayLength"))
                {
                    targetMaterial.SetFloat("_arrayLength", 1.0f);
                    Debug.Log("🧹 Reset _arrayLength = 1");
                }
                
                if (targetMaterial.HasProperty("_TextureIndex"))
                {
                    targetMaterial.SetFloat("_TextureIndex", 0);
                    Debug.Log("🧹 Reset _TextureIndex = 0");
                }
                
                if (targetMaterial.HasProperty("_UVScale"))
                {
                    targetMaterial.SetFloat("_UVScale", 1.0f);
                    Debug.Log("🧹 Reset _UVScale = 1.0");
                }
                
                if (targetMaterial.HasProperty("_TilingScale"))
                {
                    targetMaterial.SetFloat("_TilingScale", 1.0f);
                    Debug.Log("🧹 Reset _TilingScale = 1.0");
                }
                
                Debug.Log("✅ Texture array cleared and properties reset");
            }
            
            #if UNITY_EDITOR
            EditorUtility.SetDirty(this);
            #endif
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
        
        #if UNITY_EDITOR
        private void SaveTextureArrayAsAsset()
        {
            if (textureArray == null) return;
            
            // Ensure directory exists
            if (!System.IO.Directory.Exists(assetPath))
            {
                System.IO.Directory.CreateDirectory(assetPath);
            }
            
            // Generate unique asset name with timestamp
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fullAssetPath = $"{assetPath}{assetName}_{timestamp}.asset";
            
            // Create the asset
            AssetDatabase.CreateAsset(textureArray, fullAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            // Load it back to get the persistent reference
            textureArray = AssetDatabase.LoadAssetAtPath<Texture2DArray>(fullAssetPath);
            
            Debug.Log($"💾 Texture array saved as: {fullAssetPath}");
            
            // Mark this object as dirty so Unity saves the reference
            EditorUtility.SetDirty(this);
        }
        #endif
        
        void OnDestroy()
        {
            // Don't destroy if it's a persistent asset
            #if UNITY_EDITOR
            if (textureArray != null && !AssetDatabase.Contains(textureArray))
            {
                DestroyImmediate(textureArray);
            }
            #else
            if (textureArray != null)
            {
                DestroyImmediate(textureArray);
            }
            #endif
        }
        
        void OnValidate()
        {
            // Clamp texture index to valid range
            if (textureArray != null)
            {
                currentTextureIndex = Mathf.Clamp(currentTextureIndex, 0, textureArray.depth - 1);
            }
            else
            {
                // Clamp to test texture array length
                var allTextures = GetAllTextures();
                currentTextureIndex = Mathf.Clamp(currentTextureIndex, 0, allTextures.Length - 1);
            }
        }
        
        #if UNITY_EDITOR
        [UnityEditor.CustomEditor(typeof(TextureArrayTerrain))]
        public class TextureArrayTerrainEditor : UnityEditor.Editor
        {
            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();
                
                GUILayout.Space(10);

                TextureArrayTerrain script = (TextureArrayTerrain)target;
                
                // Show texture indices guide
                var textureSlots = script.GetAllTextures();
                UnityEditor.EditorGUILayout.HelpBox(
                    $"Test Texture Indices: 0-{textureSlots.Length - 1}\n" +
                    $"Assign up to {textureSlots.Length} textures for testing terrain blending.\n" +
                    "Array length is automatically sent to shader as _ArrayLength.", 
                    UnityEditor.MessageType.Info);
                
                GUILayout.Space(5);
                
                if (GUILayout.Button("CREATE TEST TEXTURE ARRAY", GUILayout.Height(35)))
                {
                    script.CreateTextureArray();
                }
                
                GUILayout.Space(5);
                
                if (GUILayout.Button("Clear Array"))
                {
                    script.ClearTextureArray();
                }
                
                GUILayout.Space(5);
                
                // Show current texture info
                if (script.TextureArray != null)
                {
                    string currentTextureName = script.GetTextureName(script.currentTextureIndex);
                    UnityEditor.EditorGUILayout.LabelField($"Current Texture: {currentTextureName}", UnityEditor.EditorStyles.boldLabel);
                }
                
                // Show asset save info
                UnityEditor.EditorGUILayout.LabelField("Asset will be saved to:", UnityEditor.EditorStyles.miniLabel);
                UnityEditor.EditorGUILayout.LabelField($"{script.assetPath}{script.assetName}_[timestamp].asset", UnityEditor.EditorStyles.helpBox);
                
                GUILayout.Space(10);
                
                if (script.TextureArray != null)
                {
                    string assetPath = UnityEditor.AssetDatabase.GetAssetPath(script.TextureArray);
                    bool isAsset = !string.IsNullOrEmpty(assetPath);
                    
                    // Count valid textures
                    var allTextures = script.GetAllTextures();
                    int validCount = CountValidTextures(allTextures, 0, allTextures.Length);
                    
                    UnityEditor.EditorGUILayout.HelpBox(
                        $"✅ Test Texture Array Created!\n" +
                        $"Size: {script.TextureArray.width}x{script.TextureArray.height} ({script.TextureArray.depth} textures)\n" +
                        $"Valid Textures: {validCount}/{script.TextureArray.depth}\n" +
                        $"Array Length: {script.TextureArray.depth} (sent to shader as _ArrayLength)\n" +
                        $"Persistent: {(isAsset ? "YES" : "NO")}\n" +
                        (isAsset ? $"Asset: {assetPath}\n" : "") +
                        $"Use slider above to preview individual textures.", 
                        UnityEditor.MessageType.Info);
                }
                else
                {
                    // Count assigned textures
                    var assignedTextures = script.GetAllTextures();
                    int totalAssigned = 0;
                    foreach (var tex in assignedTextures)
                        if (tex != null) totalAssigned++;
                    
                    if (totalAssigned > 0)
                    {
                        UnityEditor.EditorGUILayout.HelpBox(
                            $"✅ {totalAssigned}/{assignedTextures.Length} textures assigned! Click 'CREATE TEST TEXTURE ARRAY' to build the array.", 
                            UnityEditor.MessageType.Warning);
                    }
                    else
                    {
                        UnityEditor.EditorGUILayout.HelpBox(
                            "Assign textures to the test array above, then click 'CREATE TEST TEXTURE ARRAY'.\n" +
                            "Tip: You can test with just 1-2 textures - empty slots will be skipped.", 
                            UnityEditor.MessageType.Info);
                    }
                }
            }
            
            private int CountValidTextures(Texture2D[] textures, int startIndex, int count)
            {
                int valid = 0;
                for (int i = startIndex; i < startIndex + count && i < textures.Length; i++)
                {
                    if (textures[i] != null) valid++;
                }
                return valid;
            }
        }
        #endif
    }
}
