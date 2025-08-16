using System.Collections;
using UnityEngine;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine.Rendering;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IsoMesh
{
    /// <summary>
    /// This class passes SDF data to an isosurface extraction compute shader and returns a mesh.
    /// This mesh can be passed directly to a material as a triangle and index buffer in 'Procedural' mode,
    /// or transfered to the CPU and sent to a MeshFilter in 'Mesh' mode.
    /// </summary>
    [ExecuteInEditMode]
    public class SDFGroupMeshGenerator : MonoBehaviour, ISDFGroupComponent 
    {
        #region Fields and Properties

        private static class Properties
        {
            public static readonly int PointsPerSide_Int = Shader.PropertyToID("_PointsPerSide");
            public static readonly int CellSize_Float = Shader.PropertyToID("_CellSize");
            
            public static readonly int BinarySearchIterations_Int = Shader.PropertyToID("_BinarySearchIterations");
            public static readonly int IsosurfaceExtractionType_Int = Shader.PropertyToID("_IsosurfaceExtractionType");
            public static readonly int MaxAngleCosine_Float = Shader.PropertyToID("_MaxAngleCosine");
            public static readonly int VisualNormalSmoothing = Shader.PropertyToID("_VisualNormalSmoothing");
            public static readonly int GradientDescentIterations_Int = Shader.PropertyToID("_GradientDescentIterations");

            public static readonly int Settings_StructuredBuffer = Shader.PropertyToID("_Settings");
            public static readonly int Transform_Matrix4x4 = Shader.PropertyToID("_GroupTransform");

            public static readonly int SDFData_StructuredBuffer = Shader.PropertyToID("_SDFData");
            public static readonly int SDFMaterials_StructuredBuffer = Shader.PropertyToID("_SDFMaterials");
            public static readonly int SDFDataCount_Int = Shader.PropertyToID("_SDFDataCount");

            public static readonly int Samples_RWBuffer = Shader.PropertyToID("_Samples");
            public static readonly int VertexData_AppendBuffer = Shader.PropertyToID("_VertexDataPoints");
            public static readonly int CellData_RWBuffer = Shader.PropertyToID("_CellDataPoints");
            public static readonly int Counter_RWBuffer = Shader.PropertyToID("_Counter");
            public static readonly int TriangleData_AppendBuffer = Shader.PropertyToID("_TriangleDataPoints");
            public static readonly int VertexData_StructuredBuffer = Shader.PropertyToID("_VertexDataPoints_Structured");
            public static readonly int TriangleData_StructuredBuffer = Shader.PropertyToID("_TriangleDataPoints_Structured");

            public static readonly int MeshTransform_Matrix4x4 = Shader.PropertyToID("_MeshTransform");
            public static readonly int MeshVertices_RWBuffer = Shader.PropertyToID("_MeshVertices");
            public static readonly int MeshNormals_RWBuffer = Shader.PropertyToID("_MeshNormals");
            public static readonly int MeshTriangles_RWBuffer = Shader.PropertyToID("_MeshTriangles");
            //public static readonly int MeshUVs_RWBuffer = Shader.PropertyToID("_MeshUVs"); // PACKED INTO VERTEX COLORS
            public static readonly int MeshVertexColours_RWBuffer = Shader.PropertyToID("_MeshVertexColours");
            public static readonly int MeshVertexMaterials_RWBuffer = Shader.PropertyToID("_MeshVertexMaterials");

            public static readonly int IntermediateVertexBuffer_AppendBuffer = Shader.PropertyToID("_IntermediateVertexBuffer");
            public static readonly int IntermediateVertexBuffer_StructuredBuffer = Shader.PropertyToID("_IntermediateVertexBuffer_Structured");

            public static readonly int ProceduralArgs_RWBuffer = Shader.PropertyToID("_ProceduralArgs");
        }

        private struct Kernels
        {
            public const string MapKernelName = "Isosurface_Map";
            private const string GenerateVertices_KernelName = "Isosurface_GenerateVertices";
            private const string NumberVerticesKernelName = "Isosurface_NumberVertices";
            private const string GenerateTrianglesKernelName = "Isosurface_GenerateTriangles";
            public const string BuildIndexBufferKernelName = "Isosurface_BuildIndexBuffer";
            private const string AddIntermediateVerticesToIndexBufferKernelName = "Isosurface_AddIntermediateVerticesToIndexBuffer";

            public int Map { get; }
            public int GenerateVertices { get; }
            public int NumberVertices { get; }
            public int GenerateTriangles { get; }
            public int BuildIndexBuffer { get; }
            public int AddIntermediateVerticesToIndexBuffer { get; }

            public Kernels(ComputeShader shader)
            {
                Map = shader.FindKernel(MapKernelName);
                GenerateVertices = shader.FindKernel(GenerateVertices_KernelName);
                NumberVertices = shader.FindKernel(NumberVerticesKernelName);
                GenerateTriangles = shader.FindKernel(GenerateTrianglesKernelName);
                BuildIndexBuffer = shader.FindKernel(BuildIndexBufferKernelName);
                AddIntermediateVerticesToIndexBuffer = shader.FindKernel(AddIntermediateVerticesToIndexBufferKernelName);
            }
        }

        private static Kernels m_kernels;

        // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
        private readonly int[] m_counterArray = new int[] { 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1, 0, 1, 1 };
        private NativeArray<int> m_outputCounterNativeArray;
        private readonly int[] m_proceduralArgsArray = new int[] { 0, 1, 0, 0, 0 };

        private const int VERTEX_COUNTER = 0;
        private const int TRIANGLE_COUNTER = 3;
        private const int VERTEX_COUNTER_DIV_64 = 6;
        private const int TRIANGLE_COUNTER_DIV_64 = 9;
        private const int INTERMEDIATE_VERTEX_COUNTER = 12;
        private const int INTERMEDIATE_VERTEX_COUNTER_DIV_64 = 15;

        private const string ComputeShaderResourceName = "Compute_IsoSurfaceExtraction";

        private ComputeBuffer m_samplesBuffer;
        private ComputeBuffer m_cellDataBuffer;
        private ComputeBuffer m_vertexDataBuffer;
        private ComputeBuffer m_triangleDataBuffer;
        private ComputeBuffer m_meshVerticesBuffer;
        private ComputeBuffer m_meshNormalsBuffer;
        private ComputeBuffer m_meshTrianglesBuffer;
        //private ComputeBuffer m_meshUVsBuffer; // PACKED INTO VERTEX COLORS
        private ComputeBuffer m_meshVertexMaterialsBuffer;
        private ComputeBuffer m_meshVertexColoursBuffer;
        private ComputeBuffer m_intermediateVertexBuffer;
        private ComputeBuffer m_counterBuffer;
        private ComputeBuffer m_proceduralArgsBuffer;

        private NativeArray<Vector3> m_nativeArrayVertices;
        private NativeArray<Vector3> m_nativeArrayNormals;
        //private NativeArray<Vector2> m_nativeArrayUVs; // PACKED INTO VERTEX COLORS
        private NativeArray<Color> m_nativeArrayColours;
        private NativeArray<int> m_nativeArrayTriangles;

        private VertexData[] m_vertices;
        private TriangleData[] m_triangles;

        [SerializeField]
        private ComputeShader m_computeShader;
        private ComputeShader ComputeShader
        {
            get
            {
                if (m_computeShader)
                    return m_computeShader;

                Debug.Log("Attempting to load resource: " + ComputeShaderResourceName);

                m_computeShader = Resources.Load<ComputeShader>(ComputeShaderResourceName);

                if (!m_computeShader)
                    Debug.Log("Failed to load.");
                else
                    Debug.Log("Successfully loaded.");

                return m_computeShader;
            }
        }
        
        private const string UVComputeShaderResourceName = "Compute_UVGeneration";
        private ComputeShader m_uvComputeShader;
        private ComputeShader UVComputeShader
        {
            get
            {
                if (m_uvComputeShader)
                    return m_uvComputeShader;
                    
                m_uvComputeShader = Resources.Load<ComputeShader>(UVComputeShaderResourceName);
                return m_uvComputeShader;
            }
        }

        private ComputeShader m_computeShaderInstance;

        [SerializeField]
        private SDFGroup m_group;
        public SDFGroup Group
        {
            get
            {
                if (m_group)
                    return m_group;

                if (TryGetComponent(out m_group))
                    return m_group;

                if (transform.parent.TryGetComponent(out m_group))
                    return m_group;

                return null;

            }
        }

        [SerializeField]
        [UnityEngine.Serialization.FormerlySerializedAs("m_meshGameObject")]
        private GameObject m_meshGameObject;

        [SerializeField]
        private MeshFilter m_meshFilter;
        private MeshFilter MeshFilter
        {
            get
            {
                if (!m_meshFilter && TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
                {
                    m_meshFilter = meshGameObject.GetOrAddComponent<MeshFilter>();
                    return m_meshFilter;
                }

                return m_meshFilter;
            }
        }

        [SerializeField]
        private MeshCollider m_meshCollider;
        private MeshCollider MeshCollider
        {
            get
            {
                if (!m_meshCollider && TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
                {
                    m_meshCollider = meshGameObject.GetComponent<MeshCollider>();
                    return m_meshCollider;
                }

                return m_meshCollider;
            }
        }

        [SerializeField]
        private Material m_proceduralMeshMaterial;
        private Material ProceduralMeshMaterial
        {
            get
            {
                if (!m_proceduralMeshMaterial)
                    m_proceduralMeshMaterial = Resources.Load<Material>("Procedural_MeshMaterial");

                return m_proceduralMeshMaterial;
            }
        }

        [SerializeField]
        private MeshRenderer m_meshRenderer;
        public MeshRenderer MeshRenderer
        {
            get
            {
                if (!m_meshRenderer && TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
                {
                    m_meshRenderer = meshGameObject.GetOrAddComponent<MeshRenderer>();

                    if (!m_meshRenderer.sharedMaterial)
                        m_meshRenderer.sharedMaterial = ProceduralMeshMaterial;

                    return m_meshRenderer;
                }

                if (!m_meshRenderer.sharedMaterial)
                    m_meshRenderer.sharedMaterial = ProceduralMeshMaterial;

                return m_meshRenderer;
            }
        }

        private Mesh m_mesh;

        private Bounds m_bounds;

        private MaterialPropertyBlock m_propertyBlock;

        [SerializeField]
        private MainSettings m_mainSettings = new MainSettings();
        public MainSettings MainSettings => m_mainSettings;

        [SerializeField]
        private VoxelSettings m_voxelSettings = new VoxelSettings();
        public VoxelSettings VoxelSettings => m_voxelSettings;

        [SerializeField]
        private AlgorithmSettings m_algorithmSettings = new AlgorithmSettings();
        public AlgorithmSettings AlgorithmSettings => m_algorithmSettings;

        private bool m_initialized = false;

        [SerializeField]
        private bool m_showGrid = false;
        public bool ShowGrid => m_showGrid;
        
        [SerializeField]
        private bool m_showUVDebug = false;
        public bool ShowUVDebug => m_showUVDebug;

        private bool m_isEnabled = false;

        #endregion

        #region MonoBehaviour Callbacks

        private void Reset()
        {
            m_initialized = false;
            OnOutputModeChanged();
        }

        private void OnEnable()
        {
            m_isEnabled = true;
            m_initialized = false;

            if (Group.IsReady)
            {
                InitializeComputeShaderSettings();
                Group.RequestUpdate(onlySendBufferOnChange: false);
            }

#if UNITY_EDITOR
            Undo.undoRedoPerformed += OnUndo;
#endif
        }

        private void OnDisable()
        {
            m_isEnabled = false;
            ReleaseUnmanagedMemory();

#if UNITY_EDITOR
            Undo.undoRedoPerformed -= OnUndo;
#endif
        }

        private void OnUndo()
        {
            if (m_initialized)
            {
                m_initialized = false;
                InitializeComputeShaderSettings();
                m_group.RequestUpdate();
            }
        }

        #endregion

        #region UV Generation
        
        private enum UVGenerationMode { Simple, VoxelBased, LocalSpace, Triplanar, Spherical, TBN, TriplanarSeamless }
        
        private class UVGenerationResult
        {
            public NativeArray<Vector2> uvs;
            public int originalVertexCount;
            public int duplicateCount = 0;
            public bool needsMeshRebuild = false;
            
            // For seamless triplanar implementation
            public Vector3[] expandedVertices;
            public Vector3[] expandedNormals;
            public Vector2[] expandedUVs;
            public int[] remappedTriangles;
            public int[] duplicateSourceIndices;  // Source vertex index for each duplicate
            
            public int TotalVertexCount => originalVertexCount + duplicateCount;
            
            public void Dispose()
            {
                if (uvs.IsCreated) uvs.Dispose();
                // expandedVertices, expandedNormals, expandedUVs, and remappedTriangles are regular arrays - no disposal needed
            }
        }
        [SerializeField]
        private UVGenerationMode m_uvMode = UVGenerationMode.Triplanar;
        
        [SerializeField]
        [Range(0.1f, 10.0f)]
        private float m_uvScale = 1.0f;
        
        [SerializeField]
        [Range(0, 3)]
        private int m_debugUVMode = 0; // 0=off, 1=face selection, 2=UV coordinates, 3=blend weights
        
        [SerializeField]
        [Range(1.0f, 8.0f)]
        private float m_triplanarBlendSharpness = 2.0f;
        
        private UVGenerationResult GenerateUVsComputeShader(NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<int> triangles, int vertexCount, int triangleCount)
        {
            var result = new UVGenerationResult();
            result.originalVertexCount = vertexCount;
            
            // Ensure UV compute shader is loaded
            if (UVComputeShader == null)
            {
                Debug.LogError("UV Generation compute shader not found! Falling back to simple UVs.");
                result.uvs = new NativeArray<Vector2>(vertexCount, Allocator.Temp);
                for (int i = 0; i < vertexCount; i++)
                    result.uvs[i] = new Vector2(vertices[i].x * 0.1f, vertices[i].z * 0.1f);
                return result;
            }
            
            // Create buffers
            var vertexBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            var normalBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 3);
            var uvBuffer = new ComputeBuffer(vertexCount, sizeof(float) * 2);
            
            // Upload mesh data - only the exact vertex count we need
            var vertexArray = new Vector3[vertexCount];
            var normalArray = new Vector3[vertexCount];
            
            for (int i = 0; i < vertexCount; i++)
            {
                vertexArray[i] = vertices[i];
                normalArray[i] = normals[i];
            }
            
            vertexBuffer.SetData(vertexArray);
            normalBuffer.SetData(normalArray);
            
            // Select kernel based on mode
            int kernel = 0;
            switch (m_uvMode)
            {
                case UVGenerationMode.Simple:
                    kernel = UVComputeShader.FindKernel("GenerateUVs");
                    break;
                case UVGenerationMode.VoxelBased:
                    kernel = UVComputeShader.FindKernel("GenerateUVsVoxelBased");
                    break;
                case UVGenerationMode.LocalSpace:
                    kernel = UVComputeShader.FindKernel("GenerateUVsLocalSpace");
                    break;
                case UVGenerationMode.Triplanar:
                    kernel = UVComputeShader.FindKernel("GenerateUVsTriplanar");
                    break;
                case UVGenerationMode.Spherical:
                    kernel = UVComputeShader.FindKernel("GenerateUVsSpherical");
                    break;
                case UVGenerationMode.TBN:
                    kernel = UVComputeShader.FindKernel("GenerateUVsTBN");
                    break;
                case UVGenerationMode.TriplanarSeamless:
                    // Multi-pass seamless triplanar requires special handling
                    return GenerateSeamlessTriplanarUVs(vertices, normals, triangles, vertexCount, triangleCount, 
                                                      vertexBuffer, normalBuffer);
            }
            
            // Set compute shader parameters
            UVComputeShader.SetBuffer(kernel, "_MeshVertices", vertexBuffer);
            UVComputeShader.SetBuffer(kernel, "_MeshNormals", normalBuffer);
            UVComputeShader.SetBuffer(kernel, "_GeneratedUVs", uvBuffer);
            UVComputeShader.SetInt("_VertexCount", vertexCount);
            UVComputeShader.SetFloat("_UVScale", m_uvScale);
            // Match the transform used in mesh generation - vertices are in mesh local space
            if (TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
                UVComputeShader.SetMatrix("_MeshTransform", meshGameObject.transform.worldToLocalMatrix);
            else
                UVComputeShader.SetMatrix("_MeshTransform", Matrix4x4.identity);
            UVComputeShader.SetFloat("_CellSize", m_voxelSettings.CellSize);
            UVComputeShader.SetInt("_PointsPerSide", m_voxelSettings.SamplesPerSide);
            UVComputeShader.SetInt("_DebugMode", m_debugUVMode);
            UVComputeShader.SetFloat("_BlendSharpness", m_triplanarBlendSharpness);
            
            // Dispatch compute shader
            int threadGroups = Mathf.CeilToInt(vertexCount / 64.0f);
            UVComputeShader.Dispatch(kernel, threadGroups, 1, 1);
            
            // Read back UVs
            var tempUVArray = new Vector2[vertexCount];
            uvBuffer.GetData(tempUVArray);
            result.uvs = new NativeArray<Vector2>(vertexCount, Allocator.Temp);
            result.uvs.CopyFrom(tempUVArray);
            
            // Cleanup
            vertexBuffer.Dispose();
            normalBuffer.Dispose();
            uvBuffer.Dispose();
            
            return result;
        }
        
        private UVGenerationResult GenerateSeamlessTriplanarUVs(NativeArray<Vector3> vertices, NativeArray<Vector3> normals, 
                                                                   NativeArray<int> triangles, int vertexCount, int triangleCount,
                                                                   ComputeBuffer vertexBuffer, ComputeBuffer normalBuffer)
        {
            var result = new UVGenerationResult();
            result.originalVertexCount = vertexCount;
            // Create all necessary buffers for multi-pass processing
            var triangleBuffer = new ComputeBuffer(triangleCount * 3, sizeof(int));
            var projectionMaskBuffer = new ComputeBuffer(vertexCount, sizeof(int));
            var duplicateCountBuffer = new ComputeBuffer(vertexCount, sizeof(int));
            var duplicateOffsetBuffer = new ComputeBuffer(vertexCount, sizeof(int));
            var totalDuplicatesBuffer = new ComputeBuffer(1, sizeof(int));
            
            // Initialize buffers - only copy the exact triangle data we need
            // Ensure we don't read beyond the triangles array bounds
            int triangleDataSize = Mathf.Min(triangleCount * 3, triangles.Length);
            var triangleArray = new int[triangleDataSize];
            for (int i = 0; i < triangleDataSize; i++)
                triangleArray[i] = triangles[i];
                
            // If the data is smaller than expected, pad with zeros
            if (triangleDataSize < triangleCount * 3)
            {
                Debug.LogWarning($"[UV] Triangle data size mismatch: expected {triangleCount * 3}, got {triangleDataSize}");
                // Create properly sized array
                var paddedArray = new int[triangleCount * 3];
                System.Array.Copy(triangleArray, paddedArray, triangleDataSize);
                triangleBuffer.SetData(paddedArray);
            }
            else
            {
                triangleBuffer.SetData(triangleArray);
            }
            
            // Clear projection masks and counters
            var clearArray = new int[vertexCount];
            projectionMaskBuffer.SetData(clearArray);
            duplicateCountBuffer.SetData(clearArray);
            duplicateOffsetBuffer.SetData(clearArray);
            totalDuplicatesBuffer.SetData(new int[] { 0 });
            
            // Common parameters for all passes
            if (TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
                UVComputeShader.SetMatrix("_MeshTransform", meshGameObject.transform.worldToLocalMatrix);
            else
                UVComputeShader.SetMatrix("_MeshTransform", Matrix4x4.identity);
            UVComputeShader.SetFloat("_UVScale", m_uvScale);
            UVComputeShader.SetInt("_VertexCount", vertexCount);
            UVComputeShader.SetInt("_TriangleCount", triangleCount);
            
            // PASS 1: Analyze vertex projections
            int kernel = UVComputeShader.FindKernel("AnalyzeVertexProjections");
            UVComputeShader.SetBuffer(kernel, "_MeshVertices", vertexBuffer);
            UVComputeShader.SetBuffer(kernel, "_MeshTriangles", triangleBuffer);
            UVComputeShader.SetBuffer(kernel, "_VertexProjectionMask", projectionMaskBuffer);
            int threadGroups = Mathf.CeilToInt(triangleCount / 64.0f);
            UVComputeShader.Dispatch(kernel, threadGroups, 1, 1);
            
            // PASS 2: Count duplicates needed
            kernel = UVComputeShader.FindKernel("CountDuplicatesNeeded");
            UVComputeShader.SetBuffer(kernel, "_VertexProjectionMask", projectionMaskBuffer);
            UVComputeShader.SetBuffer(kernel, "_VertexDuplicateCount", duplicateCountBuffer);
            UVComputeShader.SetBuffer(kernel, "_VertexDuplicateOffset", duplicateOffsetBuffer);
            UVComputeShader.SetBuffer(kernel, "_TotalDuplicatesCounter", totalDuplicatesBuffer);
            threadGroups = Mathf.CeilToInt(vertexCount / 64.0f);
            UVComputeShader.Dispatch(kernel, threadGroups, 1, 1);
            
            // Read back total duplicates needed
            var totalDuplicates = new int[1];
            totalDuplicatesBuffer.GetData(totalDuplicates);
            int duplicateCount = totalDuplicates[0];
            
            // Debug logging
            //Debug.Log($"[UV] Seamless Triplanar Analysis:");
            //Debug.Log($"[UV] - Original vertices: {vertexCount}");
            //Debug.Log($"[UV] - Duplicates needed: {duplicateCount}");
            //Debug.Log($"[UV] - Total vertices after duplication: {vertexCount + duplicateCount}");
            
            // Store duplicate info in result
            result.duplicateCount = duplicateCount;
            result.needsMeshRebuild = duplicateCount > 0;
            
            // Create buffers for duplicates (only if needed)
            ComputeBuffer duplicateVertexDataBuffer = null;
            ComputeBuffer remappedTriangleBuffer = null;
            
            if (duplicateCount > 0)
            {
                // Packed structure size: float3 + float3 + float2 + int + int = 12 + 12 + 8 + 4 + 4 = 40 bytes
                duplicateVertexDataBuffer = new ComputeBuffer(duplicateCount, 40);
                remappedTriangleBuffer = new ComputeBuffer(triangleCount * 3, sizeof(int));
            }
            
            // Create UV buffer for all vertices (original + duplicates)
            var totalVertexCount = vertexCount + duplicateCount;
            var uvBuffer = new ComputeBuffer(totalVertexCount, sizeof(float) * 2);
            
            // PASS 3A: Generate UVs for original vertices
            kernel = UVComputeShader.FindKernel("GenerateOriginalVertexUVs");
            UVComputeShader.SetBuffer(kernel, "_MeshVertices", vertexBuffer);
            UVComputeShader.SetBuffer(kernel, "_VertexProjectionMask", projectionMaskBuffer);
            UVComputeShader.SetBuffer(kernel, "_GeneratedUVs", uvBuffer);
            threadGroups = Mathf.CeilToInt(vertexCount / 64.0f);
            UVComputeShader.Dispatch(kernel, threadGroups, 1, 1);
            
            // PASS 3B: Create duplicate vertices (only if needed)
            if (duplicateCount > 0)
            {
                kernel = UVComputeShader.FindKernel("CreateDuplicateVertices");
                UVComputeShader.SetBuffer(kernel, "_MeshVertices", vertexBuffer);
                UVComputeShader.SetBuffer(kernel, "_MeshNormals", normalBuffer);
                UVComputeShader.SetBuffer(kernel, "_VertexProjectionMask", projectionMaskBuffer);
                UVComputeShader.SetBuffer(kernel, "_VertexDuplicateCount", duplicateCountBuffer);
                UVComputeShader.SetBuffer(kernel, "_VertexDuplicateOffset", duplicateOffsetBuffer);
                UVComputeShader.SetBuffer(kernel, "_DuplicateVertexData", duplicateVertexDataBuffer);
                threadGroups = Mathf.CeilToInt(vertexCount / 64.0f);
                UVComputeShader.Dispatch(kernel, threadGroups, 1, 1);
            }
            
            // If we have duplicates, we need to update the mesh
            if (duplicateCount > 0)
            {
                // PASS 4: Remap triangle indices
                kernel = UVComputeShader.FindKernel("RemapTriangleIndices");
                UVComputeShader.SetBuffer(kernel, "_MeshVertices", vertexBuffer);
                UVComputeShader.SetBuffer(kernel, "_MeshTriangles", triangleBuffer);
                UVComputeShader.SetBuffer(kernel, "_VertexProjectionMask", projectionMaskBuffer);
                UVComputeShader.SetBuffer(kernel, "_VertexDuplicateCount", duplicateCountBuffer);
                UVComputeShader.SetBuffer(kernel, "_VertexDuplicateOffset", duplicateOffsetBuffer);
                UVComputeShader.SetBuffer(kernel, "_DuplicateVertexData", duplicateVertexDataBuffer);
                UVComputeShader.SetBuffer(kernel, "_RemappedTriangles", remappedTriangleBuffer);
                threadGroups = Mathf.CeilToInt(triangleCount / 64.0f);
                UVComputeShader.Dispatch(kernel, threadGroups, 1, 1);
                
                // TODO: Update the actual mesh with new vertices and remapped triangles
                // This would require modifying the mesh after UV generation
                //Debug.LogWarning($"Seamless triplanar created {duplicateCount} duplicate vertices for UV seams. Full mesh update not yet implemented.");
            }
            
            // For now, just return the original vertex UVs
            // In a full implementation, we'd need to rebuild the entire mesh
            var tempUVArray = new Vector2[vertexCount];
            uvBuffer.GetData(tempUVArray, 0, 0, vertexCount);
            result.uvs = new NativeArray<Vector2>(vertexCount, Allocator.Temp);
            result.uvs.CopyFrom(tempUVArray);
            
            // Step 5: If we have duplicates, read back and create expanded mesh data
            if (duplicateCount > 0)
            {
                //Debug.Log($"[UV] Reading expanded mesh data from GPU...");
                
                // Read original UVs
                var originalUVs = new Vector2[vertexCount];
                uvBuffer.GetData(originalUVs);
                
                // Read duplicate vertex data as raw floats (matching HLSL struct layout)
                // DuplicateVertexData: float3 vertex, float3 normal, float2 uv, int sourceIndex, int projection
                // = 3 + 3 + 2 + 1 + 1 = 10 floats per duplicate
                var duplicateDataFloats = new float[duplicateCount * 10];
                duplicateVertexDataBuffer.GetData(duplicateDataFloats);
                
                // Read remapped triangles
                var remappedTriangles = new int[triangleCount * 3];
                remappedTriangleBuffer.GetData(remappedTriangles);
                
                // Create expanded arrays
                int expandedVertexCount = vertexCount + duplicateCount;
                result.expandedVertices = new Vector3[expandedVertexCount];
                result.expandedNormals = new Vector3[expandedVertexCount];
                result.expandedUVs = new Vector2[expandedVertexCount];
                result.remappedTriangles = remappedTriangles;
                result.duplicateSourceIndices = new int[duplicateCount];  // Store source indices
                result.duplicateCount = duplicateCount;
                result.needsMeshRebuild = true;
                
                // Copy original vertices
                var verticesArray = new Vector3[vertexCount];
                var normalsArray = new Vector3[vertexCount];
                vertexBuffer.GetData(verticesArray);
                normalBuffer.GetData(normalsArray);
                
                for (int i = 0; i < vertexCount; i++)
                {
                    result.expandedVertices[i] = verticesArray[i];
                    result.expandedNormals[i] = normalsArray[i];
                    result.expandedUVs[i] = originalUVs[i];
                }
                
                // Parse and add duplicate vertices
                for (int i = 0; i < duplicateCount; i++)
                {
                    int baseIndex = i * 10;
                    int destIndex = vertexCount + i;
                    
                    // Extract data from float array
                    result.expandedVertices[destIndex] = new Vector3(
                        duplicateDataFloats[baseIndex + 0],
                        duplicateDataFloats[baseIndex + 1],
                        duplicateDataFloats[baseIndex + 2]
                    );
                    result.expandedNormals[destIndex] = new Vector3(
                        duplicateDataFloats[baseIndex + 3],
                        duplicateDataFloats[baseIndex + 4],
                        duplicateDataFloats[baseIndex + 5]
                    );
                    result.expandedUVs[destIndex] = new Vector2(
                        duplicateDataFloats[baseIndex + 6],
                        duplicateDataFloats[baseIndex + 7]
                    );
                    
                    // Extract and store source index for vertex color mapping
                    result.duplicateSourceIndices[i] = (int)duplicateDataFloats[baseIndex + 8];
                    // projection at baseIndex + 9 (not needed for mesh creation)
                }
                
                //Debug.Log($"[UV] Seamless Triplanar Complete:");
                //Debug.Log($"[UV] - Original vertices: {vertexCount}");
                //Debug.Log($"[UV] - Duplicate vertices: {duplicateCount}");
                //Debug.Log($"[UV] - Total vertices: {expandedVertexCount}");
                //Debug.Log($"[UV] - Triangles: {triangleCount}");
            }
            
            // Cleanup
            triangleBuffer.Release();
            projectionMaskBuffer.Release();
            duplicateCountBuffer.Release();
            duplicateOffsetBuffer.Release();
            totalDuplicatesBuffer.Release();
            uvBuffer.Release();
            
            if (duplicateCount > 0)
            {
                duplicateVertexDataBuffer?.Release();
                remappedTriangleBuffer?.Release();
            }
            
            return result;
        }
        
        #endregion

        #region Mesh Stuff

        private void Update()
        {
            if ((transform.hasChanged || (m_mainSettings.OutputMode == OutputMode.MeshFilter && TryGetOrCreateMeshGameObject(out GameObject meshGameObject) && meshGameObject.transform.hasChanged)) && Group.IsReady && !Group.IsEmpty && Group.IsRunning)
            {
                if (TryGetOrCreateMeshGameObject(out meshGameObject))
                    meshGameObject.transform.hasChanged = false;

                SendTransformToGPU();
                UpdateMesh();
            }

            transform.hasChanged = false;

            if (m_meshGameObject)
                m_meshGameObject.transform.hasChanged = false;
        }

        private void LateUpdate()
        {
            if (!m_initialized || !Group.IsReady || Group.IsEmpty)
                return;

            if (m_mainSettings.OutputMode == OutputMode.Procedural && m_mainSettings.ProceduralMaterial && m_proceduralArgsBuffer != null && m_proceduralArgsBuffer.IsValid() && m_mainSettings.AutoUpdate)
                Graphics.DrawProceduralIndirect(m_mainSettings.ProceduralMaterial, m_bounds, MeshTopology.Triangles, m_proceduralArgsBuffer, properties: m_propertyBlock);
        }

        public void UpdateMesh()
        {
            if (!m_initialized || !Group.IsReady || Group.IsEmpty)
                return;

            if (m_mainSettings.OutputMode == OutputMode.MeshFilter)
            {
                if (m_mainSettings.IsAsynchronous)
                {
                    if (!m_isCoroutineRunning)
                        StartCoroutine(Cr_GetMeshDataFromGPUAsync());
                }
                else
                {
                    GetMeshDataFromGPU();
                }
            }
            else
            {
                Dispatch();
            }
        }

        private void ReallocateNativeArrays(int vertexCount, int triangleCount, ref NativeArray<Vector3> vertices, ref NativeArray<Vector3> normals, ref NativeArray<Color> colours/*, ref NativeArray<Vector2> uvs*/, ref NativeArray<int> indices)
        {
            // to avoid lots of allocations here, i only create new arrays when
            // 1) there's no array to begin with
            // 2) the number of items to store is greater than the size of the current array
            // 3) the size of the current array is greater than the size of the entire buffer
            void ReallocateArrayIfNeeded<T>(ref NativeArray<T> array, int count) where T : struct
            {
                if (array == null || !array.IsCreated || array.Length < count)
                {
                    if (array != null && array.IsCreated)
                        array.Dispose();

                    array = new NativeArray<T>(count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                }
            }

            ReallocateArrayIfNeeded(ref vertices, vertexCount);
            ReallocateArrayIfNeeded(ref normals, vertexCount);
            //ReallocateArrayIfNeeded(ref m_nativeArrayUVs, vertexCount); // PACKED INTO VERTEX COLORS
            ReallocateArrayIfNeeded(ref colours, vertexCount);
            ReallocateArrayIfNeeded(ref indices, triangleCount * 3);
        }

        private void GetMeshDataFromGPU()
        {
            Dispatch();

            if (m_outputCounterNativeArray == null || !m_outputCounterNativeArray.IsCreated)
                m_outputCounterNativeArray = new NativeArray<int>(m_counterBuffer.count, Allocator.Persistent);

            AsyncGPUReadbackRequest counterRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_outputCounterNativeArray, m_counterBuffer);

            if (counterRequest.hasError)
            {
                Debug.LogError("AsyncGPUReadbackRequest encountered an error.");
                return;
            }

            counterRequest.WaitForCompletion();

            if (counterRequest.hasError)
            {
                Debug.LogError("AsyncGPUReadbackRequest encountered an error.");
                return;
            }

            GetCounts(m_outputCounterNativeArray, out int vertexCount, out int triangleCount);

            if (triangleCount > 0)
            {
                ReallocateNativeArrays(vertexCount, triangleCount, ref m_nativeArrayVertices, ref m_nativeArrayNormals/*, ref m_nativeArrayUVs*/, ref m_nativeArrayColours, ref m_nativeArrayTriangles);

                int vertexRequestSize = Mathf.Min(m_nativeArrayVertices.Length, m_meshVerticesBuffer.count, vertexCount);
                int triangleRequestSize = Mathf.Min(m_nativeArrayTriangles.Length, m_meshTrianglesBuffer.count, triangleCount * 3);

                AsyncGPUReadbackRequest vertexRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayVertices, m_meshVerticesBuffer, vertexRequestSize * sizeof(float) * 3, 0);
                AsyncGPUReadbackRequest normalRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayNormals, m_meshNormalsBuffer, vertexRequestSize * sizeof(float) * 3, 0);
                //AsyncGPUReadbackRequest uvRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayUVs, m_meshUVsBuffer, vertexRequestSize * sizeof(float) * 2, 0); // PACKED INTO VERTEX COLORS
                AsyncGPUReadbackRequest colourRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayColours, m_meshVertexColoursBuffer, vertexRequestSize * sizeof(float) * 4, 0);
                AsyncGPUReadbackRequest triangleRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayTriangles, m_meshTrianglesBuffer, triangleRequestSize * sizeof(int), 0);

                if (vertexRequest.hasError || normalRequest.hasError || colourRequest.hasError || triangleRequest.hasError)
                {
                    Debug.LogError("AsyncGPUReadbackRequest encountered an error.");
                    return;
                }

                AsyncGPUReadback.WaitAllRequests();

                if (vertexRequest.hasError || normalRequest.hasError || colourRequest.hasError || triangleRequest.hasError)
                {
                    Debug.LogError("AsyncGPUReadbackRequest encountered an error.");
                    return;
                }

                SetMeshData(m_nativeArrayVertices, m_nativeArrayNormals, m_nativeArrayColours, m_nativeArrayTriangles, vertexCount, triangleCount);
            }
            else
            {
                if (MeshRenderer)
                    MeshRenderer.enabled = false;

                if (MeshCollider)
                    MeshCollider.enabled = false;
            }
        }

        private bool m_isCoroutineRunning = false;

        /// <summary>
        /// This is the asynchronous version of <see cref="GetMeshDataFromGPU"/>. Use it as a coroutine. It uses a member variable to prevent duplicates from running at the same time.
        /// </summary>
        private IEnumerator Cr_GetMeshDataFromGPUAsync()
        {
            if (m_isCoroutineRunning)
                yield break;

            m_isCoroutineRunning = true;

            Dispatch();

            if (m_outputCounterNativeArray == null || !m_outputCounterNativeArray.IsCreated)
                m_outputCounterNativeArray = new NativeArray<int>(m_counterBuffer.count, Allocator.Persistent);

            AsyncGPUReadbackRequest counterRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_outputCounterNativeArray, m_counterBuffer);

            while (!counterRequest.done)
                yield return null;

            GetCounts(m_outputCounterNativeArray, out int vertexCount, out int triangleCount);

            if (triangleCount > 0)
            {
                ReallocateNativeArrays(vertexCount, triangleCount, ref m_nativeArrayVertices, ref m_nativeArrayNormals, ref m_nativeArrayColours/*m_nativeArrayUVs*/, ref m_nativeArrayTriangles);

                int vertexRequestSize = Mathf.Min(m_nativeArrayVertices.Length, m_meshVerticesBuffer.count, vertexCount);
                int triangleRequestSize = Mathf.Min(m_nativeArrayTriangles.Length, m_meshTrianglesBuffer.count, triangleCount * 3);

                AsyncGPUReadbackRequest vertexRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayVertices, m_meshVerticesBuffer, vertexRequestSize * sizeof(float) * 3, 0);
                AsyncGPUReadbackRequest normalRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayNormals, m_meshNormalsBuffer, vertexRequestSize * sizeof(float) * 3, 0);
                //AsyncGPUReadbackRequest uvRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayUVs, m_meshUVsBuffer, vertexRequestSize * sizeof(float) * 2, 0); // PACKED INTO VERTEX COLORS
                AsyncGPUReadbackRequest colourRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayColours, m_meshVertexColoursBuffer, vertexRequestSize * sizeof(float) * 4, 0);
                AsyncGPUReadbackRequest triangleRequest = AsyncGPUReadback.RequestIntoNativeArray(ref m_nativeArrayTriangles, m_meshTrianglesBuffer, triangleRequestSize * sizeof(int), 0);

                // Check for initial errors
                if (vertexRequest.hasError || normalRequest.hasError || colourRequest.hasError || triangleRequest.hasError)
                {
                    Debug.LogError("AsyncGPUReadbackRequest encountered an error during initialization.");
                    yield break;
                }

                while (!vertexRequest.done && !normalRequest.done && !colourRequest.done && !triangleRequest.done)
                    yield return null;

                // Check for errors after completion
                if (vertexRequest.hasError || normalRequest.hasError || colourRequest.hasError || triangleRequest.hasError)
                {
                    Debug.LogError("AsyncGPUReadbackRequest encountered an error during execution.");
                    yield break;
                }

                // Validate NativeArrays before use
                if (!m_nativeArrayVertices.IsCreated || !m_nativeArrayNormals.IsCreated || 
                    !m_nativeArrayColours.IsCreated || !m_nativeArrayTriangles.IsCreated)
                {
                    Debug.LogError("One or more NativeArrays are not properly created.");
                    yield break;
                }

                SetMeshData(m_nativeArrayVertices, m_nativeArrayNormals, m_nativeArrayColours, m_nativeArrayTriangles, vertexCount, triangleCount);
            }
            else
            {
                if (MeshRenderer)
                    MeshRenderer.enabled = false;

                if (MeshCollider)
                    MeshCollider.enabled = false;
            }

            m_isCoroutineRunning = false;
        }

        private void SetMeshData(NativeArray<Vector3> vertices, NativeArray<Vector3> normals, NativeArray<Color> colours, NativeArray<int> indices, int vertexCount, int triangleCount)
        {
            if (MeshRenderer)
                MeshRenderer.enabled = true;

            if (MeshCollider)
                MeshCollider.enabled = true;

            if (m_mesh == null)
            {
                m_mesh = new Mesh()
                {
                    indexFormat = IndexFormat.UInt32
                };
            }
            else
            {
                m_mesh.Clear();
            }

            m_mesh.SetVertices(vertices, 0, vertexCount);
            m_mesh.SetNormals(normals, 0, vertexCount);
            
            // Generate UVs using second compute shader pass
            Debug.Log($"🔧 [VERTEX COLOR DEBUG] UV Mode: {m_uvMode}, Debug Mode: {m_debugUVMode}");
            var uvResult = GenerateUVsComputeShader(vertices, normals, indices, vertexCount, triangleCount);
            
            // Now vertex colors are just material colors
            var cleanColors = new NativeArray<Color>(vertexCount, Allocator.Temp);
            
            // Check if mesh rebuild is needed for seamless UVs
            if (uvResult.needsMeshRebuild)
            {
                Debug.Log($"🔍 [VERTEX COLOR DEBUG] Seamless UV rebuild needed. Duplicates: {uvResult.duplicateCount}");
                
                // Step 7: Simple test case - double all vertices when debug mode = 3
                if (m_debugUVMode == 3)
                {
                    //Debug.Log($"[UV] TEST MODE: Doubling all vertices to test expanded mesh handling");
                    
                    // Create expanded arrays with double the vertices
                    var testVertexCount = vertexCount * 2;
                    var testVertices = new NativeArray<Vector3>(testVertexCount, Allocator.Temp);
                    var testNormals = new NativeArray<Vector3>(testVertexCount, Allocator.Temp);
                    var testUVs = new NativeArray<Vector2>(testVertexCount, Allocator.Temp);
                    var testColors = new NativeArray<Color>(testVertexCount, Allocator.Temp);
                    
                    // Copy original vertices twice
                    for (int i = 0; i < vertexCount; i++)
                    {
                        testVertices[i] = vertices[i];
                        testVertices[vertexCount + i] = vertices[i];
                        testNormals[i] = normals[i];
                        testNormals[vertexCount + i] = normals[i];
                        testUVs[i] = uvResult.uvs[i];
                        testUVs[vertexCount + i] = uvResult.uvs[i];
                        testColors[i] = colours[i];
                        testColors[vertexCount + i] = colours[i];
                    }
                    
                    // Use the test data
                    m_mesh.SetVertices(testVertices, 0, testVertexCount);
                    m_mesh.SetNormals(testNormals, 0, testVertexCount);
                    m_mesh.SetUVs(0, testUVs, 0, testVertexCount);
                    m_mesh.SetColors(testColors, 0, testVertexCount);
                    
                    // Cleanup test arrays
                    testVertices.Dispose();
                    testNormals.Dispose();
                    testUVs.Dispose();
                    testColors.Dispose();
                    
                    Debug.Log($"[UV] TEST MODE: Successfully created mesh with {testVertexCount} vertices (doubled from {vertexCount})");
                    
                    // Skip normal UV processing
                    uvResult.Dispose();
                    cleanColors.Dispose();
                    m_mesh.SetIndices(indices, 0, triangleCount * 3, MeshTopology.Triangles, 0, calculateBounds: true);
                    MeshFilter.mesh = m_mesh;
                    if (MeshCollider) MeshCollider.sharedMesh = m_mesh;
                    return;
                }
                
                                // Implement full mesh rebuild with expanded vertices from seamless triplanar
                if (uvResult.expandedVertices != null && uvResult.expandedNormals != null && 
uvResult.expandedUVs != null && uvResult.remappedTriangles != null)
                {
                    Debug.Log($"🎯 [VERTEX COLOR DEBUG] SEAMLESS MODE: Rebuilding mesh with {uvResult.expandedVertices.Length} vertices (expanded from {vertexCount})");
                    
                    int expandedVertexCount = uvResult.expandedVertices.Length;
                    
                    // Create expanded color array
                    var expandedColors = new Color[expandedVertexCount];
                    
                    // Copy original colors for original vertices
                    for (int i = 0; i < vertexCount; i++)
                    {
                        expandedColors[i] = colours[i];
                        // Debug first few vertex colors
                        if (i < 3)
                            Debug.Log($"🎨 [SEAMLESS] Original vertex {i} color: {colours[i]}");
                    }
                    
                    // For duplicate vertices, use the same color as their source vertex
                    // Use the source indices stored during UV generation
                    Debug.Log($"🔄 [SEAMLESS] Copying colors for {uvResult.duplicateCount} duplicate vertices");
                    for (int i = 0; i < uvResult.duplicateCount; i++)
                    {
                        int destIndex = vertexCount + i;
                        int sourceIndex = uvResult.duplicateSourceIndices[i];
                        
                        // Copy vertex color from the source vertex
                        expandedColors[destIndex] = colours[sourceIndex];
                        
                        // Debug first few duplicates
                        if (i < 3)
                            Debug.Log($"🎨 [SEAMLESS] Duplicate vertex {destIndex} copied from {sourceIndex}: {colours[sourceIndex]}");
                    }
                    
                    // Set the expanded mesh data
                    m_mesh.SetVertices(uvResult.expandedVertices);
                    m_mesh.SetNormals(uvResult.expandedNormals);
                    m_mesh.SetUVs(0, uvResult.expandedUVs);
                    m_mesh.SetColors(expandedColors);
                    
                    // Use the remapped triangles
                    m_mesh.SetIndices(uvResult.remappedTriangles, MeshTopology.Triangles, 0, calculateBounds: true);
                    
                    //Debug.Log($"[UV] SEAMLESS MODE: Successfully created seamless mesh with {expandedVertexCount} vertices");
                    
                    // Cleanup and return early
                    uvResult.Dispose();
                    cleanColors.Dispose();
                    MeshFilter.mesh = m_mesh;
                    if (MeshCollider) MeshCollider.sharedMesh = m_mesh;
                    return;
                }
            }
            
            // Fill vertex colors
            Debug.Log($"🟢 [VERTEX COLOR DEBUG] REGULAR MODE: Setting {vertexCount} vertex colors (SDF material colors)");
            for (int i = 0; i < vertexCount; i++)
            {
                cleanColors[i] = colours[i]; // Direct material colors
                
                // DEBUG: Visualize UVs as colors
                if (m_showUVDebug)
                    cleanColors[i] = new Color(uvResult.uvs[i].x, uvResult.uvs[i].y, 0, 1);
                    
                // Debug first few vertex colors
                if (i < 3)
                    Debug.Log($"🎨 Vertex {i} color: {colours[i]} -> {cleanColors[i]}");
            }
            
            m_mesh.SetUVs(0, uvResult.uvs, 0, vertexCount);
            m_mesh.SetColors(cleanColors, 0, vertexCount);
            m_mesh.SetIndices(indices, 0, triangleCount * 3, MeshTopology.Triangles, 0, calculateBounds: true);
            
            uvResult.Dispose();
            cleanColors.Dispose();

            MeshFilter.mesh = m_mesh;

            if (MeshCollider)
                MeshCollider.sharedMesh = m_mesh;
        }

        private bool TryGetOrCreateMeshGameObject(out GameObject meshGameObject)
        {
            meshGameObject = null;

            // this looks weird, but remember that unity overrides the behaviour of 
            // implicit boolean casting to mean "check whether the underlying c++ object is null"
            if (!this)
                return false;

            meshGameObject = m_meshGameObject;

            if (meshGameObject)
                return true;

            meshGameObject = new GameObject(name + " Generated Mesh");
            meshGameObject.transform.SetParent(transform);
            meshGameObject.transform.Reset();

            m_meshGameObject = meshGameObject;

            return true;
        }

        public void DereferenceMeshObject()
        {
            m_meshGameObject = null;
            m_meshFilter = null;
            m_meshRenderer = null;
        }

        /// <summary>
        /// Read the mesh counter buffer output and convert it into a simple vertex and triangle count.
        /// </summary>
        private void GetCounts(NativeArray<int> counter, out int vertexCount, out int triangleCount)
        {
            vertexCount = counter[VERTEX_COUNTER] + counter[INTERMEDIATE_VERTEX_COUNTER];
            triangleCount = counter[TRIANGLE_COUNTER];
        }

        #endregion

        #region Internal Compute Shader Stuff + Other Boring Boilerplate Methods

        private bool m_isInitializing = false;

        /// <summary>
        /// Do all the initial setup. This function should only be called once per 'session' because it does a lot of
        /// setup for buffers of constant size.
        /// </summary>
        private void InitializeComputeShaderSettings()
        {
            if (m_initialized || !m_isEnabled)
                return;

            ReleaseUnmanagedMemory();
            
            m_isInitializing = true;
            m_initialized = true;

            m_computeShaderInstance = Instantiate(ComputeShader);

            SendTransformToGPU();
            
            m_kernels = new Kernels(ComputeShader);

            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            m_counterBuffer = new ComputeBuffer(m_counterArray.Length, sizeof(int), ComputeBufferType.IndirectArguments);
            m_proceduralArgsBuffer = new ComputeBuffer(m_proceduralArgsArray.Length, sizeof(int), ComputeBufferType.IndirectArguments);

            m_computeShaderInstance.SetBuffer(m_kernels.NumberVertices, Properties.Counter_RWBuffer, m_counterBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.Counter_RWBuffer, m_counterBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateVertices, Properties.Counter_RWBuffer, m_counterBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.NumberVertices, Properties.Counter_RWBuffer, m_counterBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.BuildIndexBuffer, Properties.Counter_RWBuffer, m_counterBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.Counter_RWBuffer, m_counterBuffer);

            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.ProceduralArgs_RWBuffer, m_proceduralArgsBuffer);

            CreateVariableBuffers();

            // ensuring all these setting variables are sent to the gpu.
            OnCellSizeChanged();
            OnBinarySearchIterationsChanged();
            OnIsosurfaceExtractionTypeChanged();
            OnVisualNormalSmoothingChanged();
            OnMaxAngleToleranceChanged();
            OnGradientDescentIterationsChanged();
            OnOutputModeChanged();

            m_isInitializing = false;
        }

        /// <summary>
        /// Create the buffers which will need to be recreated and reset if certain settings change, such as cell count.
        /// </summary>
        private void CreateVariableBuffers()
        {
            int countCubed = m_voxelSettings.TotalSampleCount;

            m_computeShaderInstance.SetInt(Properties.PointsPerSide_Int, m_voxelSettings.SamplesPerSide);

            if (m_vertices.IsNullOrEmpty() || m_vertices.Length != countCubed)
                m_vertices = new VertexData[countCubed];

            if (m_triangles.IsNullOrEmpty() || m_triangles.Length != countCubed)
                m_triangles = new TriangleData[countCubed];

            m_samplesBuffer?.Dispose();
            m_cellDataBuffer?.Dispose();
            m_vertexDataBuffer?.Dispose();
            m_triangleDataBuffer?.Dispose();

            m_meshVerticesBuffer?.Dispose();
            m_meshNormalsBuffer?.Dispose();
            m_meshTrianglesBuffer?.Dispose();
            //m_meshUVsBuffer?.Dispose(); // PACKED INTO VERTEX COLORS
            m_meshVertexColoursBuffer?.Dispose();
            m_meshVertexMaterialsBuffer?.Dispose();

            m_intermediateVertexBuffer?.Dispose();

            m_samplesBuffer = new ComputeBuffer(countCubed, sizeof(float), ComputeBufferType.Structured);
            m_cellDataBuffer = new ComputeBuffer(countCubed, CellData.Stride, ComputeBufferType.Structured);
            m_vertexDataBuffer = new ComputeBuffer(countCubed, VertexData.Stride, ComputeBufferType.Append);
            m_triangleDataBuffer = new ComputeBuffer(countCubed, TriangleData.Stride, ComputeBufferType.Append);

            m_meshVerticesBuffer = new ComputeBuffer(countCubed * 3, sizeof(float) * 3, ComputeBufferType.Structured);
            m_meshNormalsBuffer = new ComputeBuffer(countCubed * 3, sizeof(float) * 3, ComputeBufferType.Structured);
            m_meshTrianglesBuffer = new ComputeBuffer(countCubed * 3, sizeof(int), ComputeBufferType.Structured);
            //m_meshUVsBuffer = new ComputeBuffer(countCubed * 3, sizeof(float) * 2, ComputeBufferType.Structured); // PACKED INTO VERTEX COLORS
            m_meshVertexColoursBuffer = new ComputeBuffer(countCubed * 3, sizeof(float) * 4, ComputeBufferType.Structured);
            m_meshVertexMaterialsBuffer = new ComputeBuffer(countCubed * 3, SDFMaterialGPU.Stride, ComputeBufferType.Structured);

            m_intermediateVertexBuffer = new ComputeBuffer(countCubed * 3, NewVertexData.Stride, ComputeBufferType.Append);

            if (m_mainSettings.ProceduralMaterial)
            {
                if (m_propertyBlock == null)
                    m_propertyBlock = new MaterialPropertyBlock();

                m_propertyBlock.SetBuffer(Properties.MeshVertices_RWBuffer, m_meshVerticesBuffer);
                m_propertyBlock.SetBuffer(Properties.MeshTriangles_RWBuffer, m_meshTrianglesBuffer);
                m_propertyBlock.SetBuffer(Properties.MeshNormals_RWBuffer, m_meshNormalsBuffer);
                //m_propertyBlock.SetBuffer(Properties.MeshUVs_RWBuffer, m_meshUVsBuffer); // PACKED INTO VERTEX COLORS
                m_propertyBlock.SetBuffer(Properties.MeshVertexMaterials_RWBuffer, m_meshVertexMaterialsBuffer);
            }

            UpdateMapKernels(Properties.Samples_RWBuffer, m_samplesBuffer);

            m_computeShaderInstance.SetBuffer(m_kernels.GenerateVertices, Properties.CellData_RWBuffer, m_cellDataBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.NumberVertices, Properties.CellData_RWBuffer, m_cellDataBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.CellData_RWBuffer, m_cellDataBuffer);

            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.MeshVertices_RWBuffer, m_meshVerticesBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.MeshNormals_RWBuffer, m_meshNormalsBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.MeshTriangles_RWBuffer, m_meshTrianglesBuffer);
            //m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.MeshUVs_RWBuffer, m_meshUVsBuffer); // PACKED INTO VERTEX COLORS
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.MeshVertexMaterials_RWBuffer, m_meshVertexMaterialsBuffer);

            m_computeShaderInstance.SetBuffer(m_kernels.BuildIndexBuffer, Properties.MeshTriangles_RWBuffer, m_meshTrianglesBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.BuildIndexBuffer, Properties.IntermediateVertexBuffer_AppendBuffer, m_intermediateVertexBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.BuildIndexBuffer, Properties.MeshVertexColours_RWBuffer, m_meshVertexColoursBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.BuildIndexBuffer, Properties.MeshVertexMaterials_RWBuffer, m_meshVertexMaterialsBuffer);

            m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.MeshVertices_RWBuffer, m_meshVerticesBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.MeshNormals_RWBuffer, m_meshNormalsBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.MeshTriangles_RWBuffer, m_meshTrianglesBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.MeshVertexMaterials_RWBuffer, m_meshVertexMaterialsBuffer);
            //m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.MeshUVs_RWBuffer, m_meshUVsBuffer); // PACKED INTO VERTEX COLORS
            m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.IntermediateVertexBuffer_StructuredBuffer, m_intermediateVertexBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, Properties.MeshVertexColours_RWBuffer, m_meshVertexColoursBuffer);

            m_bounds = new Bounds { extents = m_voxelSettings.Extents };

            ResetCounters();
            SetVertexData();
            SetTriangleData();
        }

        /// <summary>
        /// Buffers and NativeArrays are unmanaged and Unity will cry if we don't do this.
        /// </summary>
        private void ReleaseUnmanagedMemory()
        {
            StopAllCoroutines();
            m_isCoroutineRunning = false;

            m_counterBuffer?.Dispose();
            m_proceduralArgsBuffer?.Dispose();

            m_samplesBuffer?.Dispose();
            m_cellDataBuffer?.Dispose();
            m_vertexDataBuffer?.Dispose();
            m_triangleDataBuffer?.Dispose();

            m_meshVerticesBuffer?.Dispose();
            m_meshNormalsBuffer?.Dispose();
            m_meshTrianglesBuffer?.Dispose();
            //m_meshUVsBuffer?.Dispose(); // PACKED INTO VERTEX COLORS
            m_meshVertexColoursBuffer?.Dispose();
            m_meshVertexMaterialsBuffer?.Dispose();

            m_intermediateVertexBuffer?.Dispose();

            // need to do this because some of the below native arrays might be 'in use' by requests
            AsyncGPUReadback.WaitAllRequests();

            if (m_outputCounterNativeArray != null && m_outputCounterNativeArray.IsCreated)
                m_outputCounterNativeArray.Dispose();

            if (m_nativeArrayVertices != null && m_nativeArrayVertices.IsCreated)
                m_nativeArrayVertices.Dispose();

            if (m_nativeArrayNormals != null && m_nativeArrayNormals.IsCreated)
                m_nativeArrayNormals.Dispose();

            //if (m_nativeArrayUVs != null && m_nativeArrayUVs.IsCreated)
            //    m_nativeArrayUVs.Dispose(); // PACKED INTO VERTEX COLORS

            if (m_nativeArrayColours != null && m_nativeArrayColours.IsCreated)
                m_nativeArrayColours.Dispose();

            if (m_nativeArrayTriangles != null && m_nativeArrayTriangles.IsCreated)
                m_nativeArrayTriangles.Dispose();

            m_initialized = false;

            if (m_computeShaderInstance)
                DestroyImmediate(m_computeShaderInstance);
        }

        /// <summary>
        /// Send a buffer to all kernels which use the map function.
        /// </summary>
        private void UpdateMapKernels(int id, ComputeBuffer buffer)
        {
            if (!m_initialized || !m_isEnabled)
                return;

            if (buffer == null || !buffer.IsValid())
            {
                Debug.Log("Attempting to pass null buffer to map kernels.");
                return;
            }

            m_computeShaderInstance.SetBuffer(m_kernels.Map, id, buffer);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateVertices, id, buffer);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, id, buffer);
            m_computeShaderInstance.SetBuffer(m_kernels.BuildIndexBuffer, id, buffer);
            m_computeShaderInstance.SetBuffer(m_kernels.AddIntermediateVerticesToIndexBuffer, id, buffer);
        }

        /// <summary>
        /// Sets the vertex data as empty and then sends it back to all three kernels? TODO: get rid of this
        /// </summary>
        private void SetVertexData()
        {
            m_vertexDataBuffer.SetData(m_vertices);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateVertices, Properties.VertexData_AppendBuffer, m_vertexDataBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.NumberVertices, Properties.VertexData_StructuredBuffer, m_vertexDataBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.VertexData_StructuredBuffer, m_vertexDataBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.BuildIndexBuffer, Properties.VertexData_StructuredBuffer, m_vertexDataBuffer);
        }

        /// <summary>
        /// Sets the triangle data as empty and then sends it back? TODO: get rid of this
        /// </summary>
        private void SetTriangleData()
        {
            m_triangleDataBuffer.SetData(m_triangles);
            m_computeShaderInstance.SetBuffer(m_kernels.GenerateTriangles, Properties.TriangleData_AppendBuffer, m_triangleDataBuffer);
            m_computeShaderInstance.SetBuffer(m_kernels.BuildIndexBuffer, Properties.TriangleData_StructuredBuffer, m_triangleDataBuffer);
        }

        public void Run()
        {
            // this only happens when unity is recompiling. this is annoying and hacky but it works.
            if (m_counterBuffer == null || !m_counterBuffer.IsValid())
                return;

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
            {
                if (!m_initialized)
                    InitializeComputeShaderSettings();

                UpdateMesh();
            }
        }
        
        /// <summary>
        /// Dispatch all the compute kernels in the correct order. Basically... do the thing.
        /// </summary>
        private void Dispatch()
        {
            // this only happens when unity is recompiling. this is annoying and hacky but it works.
            if (m_counterBuffer == null || !m_counterBuffer.IsValid())
                return;

            ResetCounters();

            DispatchMap();
            DispatchGenerateVertices();
            DispatchNumberVertices();
            DispatchGenerateTriangles();
            DispatchBuildIndexBuffer();
            DispatchAddIntermediateVerticesToIndexBuffer();
        }

        /// <summary>
        /// Reset count of append buffers.
        /// </summary>
        private void ResetCounters()
        {
            m_counterBuffer.SetData(m_counterArray);

            m_vertexDataBuffer?.SetCounterValue(0);
            m_triangleDataBuffer?.SetCounterValue(0);

            m_meshVerticesBuffer?.SetCounterValue(0);
            m_meshNormalsBuffer?.SetCounterValue(0);
            m_meshTrianglesBuffer?.SetCounterValue(0);

            m_intermediateVertexBuffer?.SetCounterValue(0);

            m_proceduralArgsBuffer?.SetData(m_proceduralArgsArray);
        }

        private void DispatchMap()
        {
            UpdateMapKernels(Properties.Settings_StructuredBuffer, Group.SettingsBuffer);

            m_computeShaderInstance.GetKernelThreadGroupSizes(m_kernels.Map, out uint x, out uint y, out uint z);
            m_computeShaderInstance.Dispatch(m_kernels.Map, Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)x), Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)y), Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)z));
        }

        private void DispatchGenerateVertices()
        {
            m_computeShaderInstance.GetKernelThreadGroupSizes(m_kernels.GenerateVertices, out uint x, out uint y, out uint z);
            m_computeShaderInstance.Dispatch(m_kernels.GenerateVertices, Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)x), Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)y), Mathf.CeilToInt(m_voxelSettings.SamplesPerSide / (float)z));
        }

        private void DispatchNumberVertices()
        {
            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            m_computeShaderInstance.DispatchIndirect(m_kernels.NumberVertices, m_counterBuffer, VERTEX_COUNTER_DIV_64 * sizeof(int));
        }

        private void DispatchGenerateTriangles()
        {
            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            m_computeShaderInstance.DispatchIndirect(m_kernels.GenerateTriangles, m_counterBuffer, VERTEX_COUNTER_DIV_64 * sizeof(int));
        }

        private void DispatchBuildIndexBuffer()
        {
            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            m_computeShaderInstance.DispatchIndirect(m_kernels.BuildIndexBuffer, m_counterBuffer, TRIANGLE_COUNTER_DIV_64 * sizeof(int));
        }

        private void DispatchAddIntermediateVerticesToIndexBuffer()
        {
            // counter buffer has 18 integers: [vertex count, 1, 1, triangle count, 1, 1, vertex count / 64, 1, 1, triangle count / 64, 1, 1, intermediate vertex count, 1, 1, intermediate vertex count / 64, 1, 1]
            m_computeShaderInstance.DispatchIndirect(m_kernels.AddIntermediateVerticesToIndexBuffer, m_counterBuffer, INTERMEDIATE_VERTEX_COUNTER_DIV_64 * sizeof(int));
        }

        private void SendTransformToGPU()
        {
            if (!m_initialized || !m_isEnabled)
                return;

            m_computeShaderInstance.SetMatrix(Properties.Transform_Matrix4x4, transform.localToWorldMatrix);

            if (m_mainSettings.OutputMode == OutputMode.MeshFilter && TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
                m_computeShaderInstance.SetMatrix(Properties.MeshTransform_Matrix4x4, meshGameObject.transform.worldToLocalMatrix);
            else if (m_mainSettings.OutputMode == OutputMode.Procedural)
                m_computeShaderInstance.SetMatrix(Properties.MeshTransform_Matrix4x4, Matrix4x4.identity);
        }

        public void SetVoxelSettings(VoxelSettings voxelSettings)
        {
            m_isInitializing = true;
            m_voxelSettings.CopySettings(voxelSettings);

            OnCellCountChanged();
            OnCellSizeChanged();

            m_isInitializing = false;

            if (m_mainSettings.AutoUpdate)
                UpdateMesh();
        }

        public void SetMainSettings(MainSettings mainSettings)
        {
            m_isInitializing = true;
            m_mainSettings.CopySettings(mainSettings);

            OnOutputModeChanged();

            m_isInitializing = false;

            if (m_mainSettings.AutoUpdate)
                UpdateMesh();
        }

        public void SetAlgorithmSettings(AlgorithmSettings algorithmSettings)
        {
            m_isInitializing = true;
            m_algorithmSettings.CopySettings(algorithmSettings);
            
            OnVisualNormalSmoothingChanged();
            OnMaxAngleToleranceChanged();
            OnGradientDescentIterationsChanged();
            OnBinarySearchIterationsChanged();
            OnIsosurfaceExtractionTypeChanged();
            OnOutputModeChanged();

            m_isInitializing = false;

            if (m_mainSettings.AutoUpdate)
                UpdateMesh();
        }

        public void OnCellCountChanged()
        {
            m_bounds = new Bounds { extents = m_voxelSettings.Extents };

            if (!m_initialized || !m_isEnabled)
                return;

            CreateVariableBuffers();

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnCellSizeChanged()
        {
            m_bounds = new Bounds { extents = m_voxelSettings.Extents };

            if (!m_initialized || !m_isEnabled)
                return;

            m_computeShaderInstance.SetFloat(Properties.CellSize_Float, m_voxelSettings.CellSize);

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }
        
        public void OnVisualNormalSmoothingChanged()
        {
            if (!m_initialized || !m_isEnabled)
                return;

            m_computeShaderInstance.SetFloat(Properties.VisualNormalSmoothing, m_algorithmSettings.VisualNormalSmoothing);

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnMaxAngleToleranceChanged()
        {
            if (!m_initialized || !m_isEnabled)
                return;

            m_computeShaderInstance.SetFloat(Properties.MaxAngleCosine_Float, Mathf.Cos(m_algorithmSettings.MaxAngleTolerance * Mathf.Deg2Rad));

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnGradientDescentIterationsChanged()
        {
            if (!m_initialized || !m_isEnabled)
                return;
            
            m_computeShaderInstance.SetInt(Properties.GradientDescentIterations_Int, m_algorithmSettings.GradientDescentIterations);

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnBinarySearchIterationsChanged()
        {
            if (!m_initialized || !m_isEnabled)
                return;

            m_computeShaderInstance.SetInt(Properties.BinarySearchIterations_Int, m_algorithmSettings.BinarySearchIterations);

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnIsosurfaceExtractionTypeChanged()
        {
            if (!m_initialized || !m_isEnabled)
                return;

            m_computeShaderInstance.SetInt(Properties.IsosurfaceExtractionType_Int, (int)m_algorithmSettings.IsosurfaceExtractionType);

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }
        
        public void OnOutputModeChanged()
        {
            if (TryGetOrCreateMeshGameObject(out GameObject meshGameObject))
            {
                meshGameObject.SetActive(true);

                if (MeshRenderer)
                    MeshRenderer.enabled = !Group.IsEmpty;
            }
            else if (m_mainSettings.OutputMode == OutputMode.Procedural)
            {
                if (meshGameObject)
                    meshGameObject.SetActive(false);
            }

            SendTransformToGPU();
            Group.RequestUpdate(onlySendBufferOnChange: false);
        }

        public void OnDensitySettingChanged()
        {
            OnCellSizeChanged();
            CreateVariableBuffers();
        }

        #endregion

        #region SDF Group Methods

        public void UpdateDataBuffer(ComputeBuffer computeBuffer, ComputeBuffer materialBuffer, int count)
        {
            if (!m_isEnabled)
                return;

            if (!m_initialized)
                InitializeComputeShaderSettings();

            UpdateMapKernels(Properties.SDFData_StructuredBuffer, computeBuffer);
            UpdateMapKernels(Properties.SDFMaterials_StructuredBuffer, materialBuffer);
            m_computeShaderInstance.SetInt(Properties.SDFDataCount_Int, count);

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void UpdateSettingsBuffer(ComputeBuffer computeBuffer)
        {
            if (!m_isEnabled)
                return;

            if (!m_initialized)
                InitializeComputeShaderSettings();

            UpdateMapKernels(Properties.Settings_StructuredBuffer, computeBuffer);

            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        public void OnEmpty()
        {
            if (MeshRenderer)
                MeshRenderer.enabled = false;
        }

        public void OnNotEmpty()
        {
            if (MeshRenderer)
                MeshRenderer.enabled = m_mainSettings.OutputMode == OutputMode.MeshFilter;
        }

        public void OnPrimitivesChanged()
        {
            if (m_mainSettings.AutoUpdate && !m_isInitializing)
                UpdateMesh();
        }

        #endregion

        #region Grid Helper Functions

        public Vector3 CellCoordinateToVertex(int x, int y, int z)
        {
            float gridSize = (float)(m_voxelSettings.SamplesPerSide - 1f);
            float bound = (gridSize / 2f) * m_voxelSettings.CellSize;

            float xPos = Mathf.LerpUnclamped(-bound, bound, x / gridSize);
            float yPos = Mathf.LerpUnclamped(-bound, bound, y / gridSize);
            float zPos = Mathf.LerpUnclamped(-bound, bound, z / gridSize);

            return new Vector3(xPos, yPos, zPos);
        }

        public Vector3 CellCoordinateToVertex(Vector3Int vec) =>
            CellCoordinateToVertex(vec.x, vec.y, vec.z);

        public Vector3Int IndexToCellCoordinate(int index)
        {
            int samplesPerSide = m_voxelSettings.SamplesPerSide;

            int z = index / (samplesPerSide * samplesPerSide);
            index -= (z * samplesPerSide * samplesPerSide);
            int y = index / samplesPerSide;
            int x = index % samplesPerSide;

            return new Vector3Int(x, y, z);
        }

        public Vector3 IndexToVertex(int index)
        {
            Vector3Int coords = IndexToCellCoordinate(index);
            return CellCoordinateToVertex(coords.x, coords.y, coords.z);
        }

        public int CellCoordinateToIndex(int x, int y, int z) =>
            (x + y * m_voxelSettings.SamplesPerSide + z * m_voxelSettings.SamplesPerSide * m_voxelSettings.SamplesPerSide);

        public int CellCoordinateToIndex(Vector3Int vec) =>
            CellCoordinateToIndex(vec.x, vec.y, vec.z);

        #endregion

        #region Chunk + Editor Methods

        [SerializeField]
        private bool m_settingsControlledByGrid = false;

        public void SetSettingsControlledByGrid(bool settingsControlledByGrid) =>
            m_settingsControlledByGrid = settingsControlledByGrid;

        public static void CloneSettings(SDFGroupMeshGenerator target, Transform parent, SDFGroup group, MainSettings mainSettings, AlgorithmSettings algorithmSettings, VoxelSettings voxelSettings, bool addMeshRenderer = false, bool addMeshCollider = false, Material meshRendererMaterial = null)
        {
            target.TryGetOrCreateMeshGameObject(out GameObject meshGameObject);

            if (addMeshRenderer)
            {
                MeshRenderer clonedMeshRenderer = meshGameObject.GetOrAddComponent<MeshRenderer>();

                if (meshRendererMaterial)
                    clonedMeshRenderer.sharedMaterial = meshRendererMaterial;
            }

            if (addMeshCollider)
            {
                meshGameObject.GetOrAddComponent<MeshCollider>();
            }

            target.m_group = group;
            target.m_mainSettings.CopySettings(mainSettings);
            target.m_algorithmSettings.CopySettings(algorithmSettings);
            target.m_voxelSettings.CopySettings(voxelSettings);
        }

        #endregion

        #region Structs

        [StructLayout(LayoutKind.Sequential)]
        [System.Serializable]
        public struct CellData
        {
            public static int Stride => sizeof(int) + sizeof(float) * 3;

            public int VertexID;
            public Vector3 SurfacePoint;

            public bool HasSurfacePoint => VertexID >= 0;

            public override string ToString() => $"HasSurfacePoint = {HasSurfacePoint}" + (HasSurfacePoint ? $", SurfacePoint = {SurfacePoint}, VertexID = {VertexID}" : "");
        };

        [StructLayout(LayoutKind.Sequential)]
        [System.Serializable]
        public struct VertexData
        {
            public static int Stride => sizeof(int) * 2 + sizeof(float) * 6;

            public int Index;
            public int CellID;
            public Vector3 Vertex;
            public Vector3 Normal;

            public override string ToString() => $"Index = {Index}, CellID = {CellID}, Vertex = {Vertex}, Normal = {Normal}";
        }

        [StructLayout(LayoutKind.Sequential)]
        [System.Serializable]
        public struct TriangleData
        {
            public static int Stride => sizeof(int) * 3;

            public int P_1;
            public int P_2;
            public int P_3;

            public override string ToString() => $"P_1 = {P_1}, P_2 = {P_2}, P_3 = {P_3}";
        }

        [StructLayout(LayoutKind.Sequential)]
        [System.Serializable]
        public struct NewVertexData
        {
            public static int Stride => sizeof(int) + sizeof(float) * 6;

            public int Index;
            public Vector3 Vertex;
            public Vector3 Normal;

            public override string ToString() => $"Index = {Index}, Vertex = {Vertex}, Normal = {Normal}";
        }

        #endregion

    }

    public enum IsosurfaceExtractionType { SurfaceNets, DualContouring };
    public enum EdgeIntersectionType { Interpolation, BinarySearch };

    public enum CellSizeMode { Fixed, Density };
    public enum OutputMode { MeshFilter, Procedural };
}