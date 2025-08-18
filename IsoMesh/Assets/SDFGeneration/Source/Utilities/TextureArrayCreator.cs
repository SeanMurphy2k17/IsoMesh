using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace IsoMesh
{
    /// <summary>
    /// Simple utility to create Texture2DArray assets from a list of textures.
    /// </summary>
    public class TextureArrayCreator : EditorWindow
    {
        [SerializeField] private List<Texture2D> sourceTextures = new List<Texture2D>();
        [SerializeField] private string assetName = "MyTextureArray";
        [SerializeField] private bool generateMipmaps = true;
        [SerializeField] private bool linear = false;
        
        [MenuItem("Tools/IsoMesh/Texture Array Creator")]
        public static void ShowWindow()
        {
            GetWindow<TextureArrayCreator>("Texture Array Creator");
        }
        
        void OnGUI()
        {
            GUILayout.Label("Texture Array Creator", EditorStyles.boldLabel);
            GUILayout.Space(10);
            
            // Asset name
            assetName = EditorGUILayout.TextField("Asset Name", assetName);
            
            // Settings
            generateMipmaps = EditorGUILayout.Toggle("Generate Mipmaps", generateMipmaps);
            linear = EditorGUILayout.Toggle("Linear Color Space", linear);
            
            GUILayout.Space(10);
            
            // Texture list
            GUILayout.Label("Source Textures:", EditorStyles.boldLabel);
            
            // Add/Remove buttons
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Slot"))
                sourceTextures.Add(null);
            
            if (GUILayout.Button("Remove Last") && sourceTextures.Count > 0)
                sourceTextures.RemoveAt(sourceTextures.Count - 1);
            GUILayout.EndHorizontal();
            
            // Texture slots
            for (int i = 0; i < sourceTextures.Count; i++)
            {
                sourceTextures[i] = (Texture2D)EditorGUILayout.ObjectField($"Texture {i}", sourceTextures[i], typeof(Texture2D), false);
            }
            
            GUILayout.Space(20);
            
            // Validation
            bool canCreate = sourceTextures.Count > 0 && !string.IsNullOrEmpty(assetName);
            bool allValid = true;
            
            if (canCreate)
            {
                // Check if all textures are assigned and same size
                Texture2D firstTex = null;
                foreach (var tex in sourceTextures)
                {
                    if (tex == null)
                    {
                        allValid = false;
                        break;
                    }
                    
                    if (firstTex == null)
                        firstTex = tex;
                    else if (tex.width != firstTex.width || tex.height != firstTex.height)
                    {
                        allValid = false;
                        EditorGUILayout.HelpBox($"All textures must be the same size! Expected: {firstTex.width}x{firstTex.height}", MessageType.Error);
                        break;
                    }
                }
                
                if (allValid && firstTex != null)
                {
                    EditorGUILayout.HelpBox($"Ready to create: {sourceTextures.Count} textures at {firstTex.width}x{firstTex.height}", MessageType.Info);
                }
            }
            
            // Create button
            GUI.enabled = canCreate && allValid;
            if (GUILayout.Button("Create Texture Array Asset", GUILayout.Height(30)))
            {
                CreateTextureArrayAsset();
            }
            GUI.enabled = true;
        }
        
        private void CreateTextureArrayAsset()
        {
            if (sourceTextures.Count == 0)
            {
                EditorUtility.DisplayDialog("Error", "No textures provided!", "OK");
                return;
            }
            
            // Validate all textures
            var firstTexture = sourceTextures[0];
            if (firstTexture == null)
            {
                EditorUtility.DisplayDialog("Error", "First texture is null!", "OK");
                return;
            }
            
            int width = firstTexture.width;
            int height = firstTexture.height;
            
            // Check all textures are same size
            foreach (var tex in sourceTextures)
            {
                if (tex == null)
                {
                    EditorUtility.DisplayDialog("Error", "One or more textures are null!", "OK");
                    return;
                }
                
                if (tex.width != width || tex.height != height)
                {
                    EditorUtility.DisplayDialog("Error", $"All textures must be the same size!\nExpected: {width}x{height}, Got: {tex.width}x{tex.height}", "OK");
                    return;
                }
            }
            
            // Create texture array
            var textureArray = new Texture2DArray(width, height, sourceTextures.Count, firstTexture.format, generateMipmaps)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Repeat
            };
            
            // Copy textures
            for (int i = 0; i < sourceTextures.Count; i++)
            {
                Graphics.CopyTexture(sourceTextures[i], 0, 0, textureArray, i, 0);
            }
            
            // Apply changes
            textureArray.Apply(updateMipmaps: generateMipmaps);
            
            // Save as asset
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Texture Array",
                assetName + ".asset",
                "asset",
                "Choose where to save the texture array asset"
            );
            
            if (!string.IsNullOrEmpty(path))
            {
                AssetDatabase.CreateAsset(textureArray, path);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                
                // Select the created asset
                Selection.activeObject = textureArray;
                EditorGUIUtility.PingObject(textureArray);
                
                Debug.Log($"✅ Texture array created: {sourceTextures.Count} textures at {width}x{height} -> {path}");
                
                EditorUtility.DisplayDialog("Success", $"Texture array created successfully!\n\nPath: {path}\nTextures: {sourceTextures.Count}\nSize: {width}x{height}", "OK");
            }
        }
    }
    
    // Alternative: Simple component-based creator
    [CreateAssetMenu(fileName = "TextureArrayAsset", menuName = "IsoMesh/Texture Array Asset")]
    [System.Serializable]
    public class SimpleTextureArrayAsset : ScriptableObject
    {
        [SerializeField] public Texture2D[] sourceTextures;
        [SerializeField] public Texture2DArray textureArray;
        
        [ContextMenu("Create Array")]
        public void CreateArray()
        {
            if (sourceTextures == null || sourceTextures.Length == 0)
            {
                Debug.LogError("No source textures assigned!");
                return;
            }
            
            var firstTex = sourceTextures[0];
            textureArray = new Texture2DArray(firstTex.width, firstTex.height, sourceTextures.Length, firstTex.format, true);
            
            for (int i = 0; i < sourceTextures.Length; i++)
            {
                Graphics.CopyTexture(sourceTextures[i], 0, 0, textureArray, i, 0);
            }
            
            textureArray.Apply();
            Debug.Log($"Texture array created with {sourceTextures.Length} textures!");
        }
    }
}
