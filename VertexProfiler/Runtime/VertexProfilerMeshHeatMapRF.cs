using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using ProfilingScope = UnityEngine.Rendering.ProfilingScope;

namespace VertexProfilerTool
{
    [System.Serializable]
    public class VertexProfilerModeMeshHeatMapRenderPass : VertexProfilerModeBaseRenderPass
    {
        public ComputeShader CalculateVertexByTilesCS;
        public ComputeShader GenerateProfilerRTCS;
        
        private Material OutputRendererIdMat;
        private VertexProfilerJobs.J_Culling Job_Culling;

        private int CalculateVertexKernel = 2;
        private int CalculateVertexKernel2 = 3;
        private int GenerateProfilerKernel = 1;
        private int CalculateVertexByTilesCSGroupX = 256;
        
        private RTHandle m_TileProfilerRT;
        private RTHandle m_OutputRenderIdDepthRT;
        private RTHandle m_OutputRenderIdRT;
        private RTHandle m_TileProfilerUIntRT;
        private RTHandle m_TileProfilerUInt2RT;

        // 原生渲染所需属性
        List<ShaderTagId> URPOutputRendererTagId;
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_renderStateBlock;
        ProfilingSampler m_ProfilingSamplerOutputRenderer;

        public VertexProfilerModeMeshHeatMapRenderPass()
        {
	        m_FilteringSettings = new FilteringSettings(RenderQueueRange.all);
	        m_renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
	        m_ProfilingSamplerOutputRenderer = new ProfilingSampler("Output Renderer Data");
        }

        public override void Setup(VertexProfilerRendererFeature.Settings settings)
        {
	        base.Setup(settings);
            // 不能在构造函数初始化的部分在这创建
            URPOutputRendererTagId = new List<ShaderTagId>() {new ("SRPDefaultUnlit"), new ("UniversalForward"), new ("UniversalForwardOnly")};
            if (vp != null)
            {
                vp.ProfilerMode = this;
                
                CalculateVertexByTilesCS = m_Settings.m_FeatureData.shaders.calculateVertexByTilesCS;
                GenerateProfilerRTCS = m_Settings.m_FeatureData.shaders.generateProfilerRTCS;
                OutputRendererIdMat = new Material(Shader.Find("VertexProfiler/URPOutputRendererIdShader"));
            };
        }

        public override void OnDisable()
        {
            base.OnDisable();
            ReleaseRTHandle(ref m_TileProfilerRT);
            ReleaseRTHandle(ref m_OutputRenderIdDepthRT);
            ReleaseRTHandle(ref m_OutputRenderIdRT);
            ReleaseRTHandle(ref m_TileProfilerUIntRT);
            ReleaseRTHandle(ref m_TileProfilerUInt2RT);
            
            if (vp != null && vp.ProfilerMode == this)
            {
                vp.ProfilerMode = null;
            }

            ReleaseAllComputeBuffer();
        }

        public override bool CheckProfilerEnabled()
        {
            return vp != null
                   && CalculateVertexByTilesCS != null 
                   && GenerateProfilerRTCS != null 
                   && OutputRendererIdMat != null;
        }

        public override void UseDefaultColorRangeSetting()
        {
            VertexProfilerUtil.MeshHeatMapSetting = new List<int>(VertexProfilerUtil.DefaultMeshHeatMapSetting);
            DensityList.Clear();
            NeedSyncColorRangeSetting = true;
            CheckColorRangeData();
        }
        
        public override void CheckColorRangeData(bool forceReload = false)
        {
	        if (DensityList.Count <= 0 || forceReload)
	        {
		        DensityList.Clear();

		        foreach (int v in VertexProfilerUtil.MeshHeatMapSetting)
		        {
			        DensityList.Add(v);
		        }

		        NeedSyncColorRangeSetting = true;
	        }
            // 检查是否要同步设置
            if (NeedSyncColorRangeSetting)
            {
                m_ColorRangeSettings = new ColorRangeSetting[DensityList.Count];
                for (int i = 0; i < DensityList.Count; i++)
                {
                    float threshold = DensityList[i] * 0.0001f;
                    Color color = VertexProfilerUtil.GetProfilerColor(i);
                    ColorRangeSetting setting = new ColorRangeSetting();
                    setting.threshold = threshold;
                    setting.color = color;
                    m_ColorRangeSettings[i] = setting;
                }
                NeedSyncColorRangeSetting = false;
            }
        }
        
        public override void InitRenderers()
        {
            // 初始化
            if (vp.NeedRecollectRenderers)
            {
                // 收集场景内的显示中的渲染器，并收集这些渲染器的包围盒数据
                rendererComponentDatas = RendererCuller.GetAllRenderers();
                
                m_RendererNum = 0;
                m_RendererBoundsData.Clear();
                m_RendererLocalToWorldMatrix.Clear();
                
                Mesh mesh;
                for (int i = 0; i < rendererComponentDatas.Count; i++)
                {
                    RendererComponentData data = rendererComponentDatas[i];
                    Renderer renderer = data.renderer;
                
                    if(data.renderer == null)
                        continue;
                    mesh = data.m;
                    mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                    mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                    if(mesh == null) 
                        continue;
                
                    Bounds bound = renderer.bounds;
                    RendererBoundsData boundsData = new RendererBoundsData()
                    {
                        center = bound.center,
                        extends = bound.extents
                    };
                
                    m_RendererBoundsData.Add(boundsData);
                    m_RendererLocalToWorldMatrix.Add(renderer.localToWorldMatrix);

                    // SetPropertyBlock不指定index的话，多材质renderer会无法生效
                    var smats = renderer.sharedMaterials;
                    for (int k = 0; k < smats.Length; k++)
                    {
                        MaterialPropertyBlock block = new MaterialPropertyBlock();
                        renderer.GetPropertyBlock(block, k);
                        block.SetInt(VertexProfilerUtil._RendererId, i);
                        block.SetInt(VertexProfilerUtil._VertexCount, mesh.vertexCount);
                        renderer.SetPropertyBlock(block, k);
                    }
                
                    m_RendererNum++;
                }
            }
        }

        public override void SetupConstantBufferData(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            base.SetupConstantBufferData(cmd, ref context,ref renderingData);

            var camera = renderingData.cameraData.camera;
            // 相机矩阵
            Matrix4x4 m_v = camera.worldToCameraMatrix;
            Matrix4x4 m_p = GL.GetGPUProjectionMatrix(camera.projectionMatrix, SystemInfo.graphicsUVStartsAtTop);
            Matrix4x4 m_vp = m_p * m_v;
            
            // 外部处理
            vp.TileNumX = Mathf.CeilToInt(camera.pixelWidth / (float)m_Settings.TileWidth);
            vp.TileNumY = Mathf.CeilToInt(camera.pixelHeight / (float)m_Settings.TileHeight);
            
            // 在这里使用JobSystem调度视锥剔除计算
            var frustumPlanes = GeometryUtility.CalculateFrustumPlanes(camera);
            NativeArray<RendererBoundsData> m_RendererBoundsNA = VertexProfilerUtil.ConvertToNativeArray(m_RendererBoundsData, Allocator.TempJob);
            NativeArray<uint> VisibleFlagNA = new NativeArray<uint>(m_RendererNum, Allocator.TempJob);
            NativeArray<Plane> frustumPlanesNA = VertexProfilerUtil.ConvertToNativeArray(frustumPlanes, Allocator.TempJob);
            Job_Culling = new VertexProfilerJobs.J_Culling()
            {
                RendererBoundsData = m_RendererBoundsNA,
                CameraFrustumPlanes = frustumPlanesNA,
                _VisibleFlagList = VisibleFlagNA
            };
            Job_Culling.Run();
            VisibleFlag = VisibleFlagNA.ToList();
            frustumPlanesNA.Dispose();
            m_RendererBoundsNA.Dispose();
            VisibleFlagNA.Dispose();
            
            int width = camera.pixelWidth;
            int height = camera.pixelHeight;
            ReAllocTileProfilerRT(width, height, GraphicsFormat.None, GraphicsFormat.D24_UNorm, FilterMode.Point, ref m_OutputRenderIdDepthRT, "m_OutputRenderIdDepthRT", false);
            ReAllocTileProfilerRT(width, height, GraphicsFormat.R32G32_SFloat, GraphicsFormat.None, FilterMode.Point, ref m_OutputRenderIdRT, "m_OutputRenderIdRT");
            ReAllocTileProfilerRT(width, height, GraphicsFormat.R32_UInt, GraphicsFormat.None, FilterMode.Point, ref m_TileProfilerUIntRT, "m_TileProfilerUIntRT");
            ReAllocTileProfilerRT(width, height, GraphicsFormat.R32G32_UInt, GraphicsFormat.None, FilterMode.Point, ref m_TileProfilerUInt2RT, "m_TileProfilerUInt2RT");
            ReAllocTileProfilerRT(width, height, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, FilterMode.Point, ref m_TileProfilerRT, "m_TileProfilerRT");
            
            cmd.SetComputeMatrixParam(CalculateVertexByTilesCS, VertexProfilerUtil._UNITY_MATRIX_VP, m_vp);
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._UNITY_REVERSED_Z, SystemInfo.usesReversedZBuffer ? 1 : 0);
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._CullMode, (int)ECullMode);
            cmd.SetComputeVectorParam(CalculateVertexByTilesCS, VertexProfilerUtil._ScreenParams, new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight));
            cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._UNITY_UV_STARTS_AT_TOP, SystemInfo.graphicsUVStartsAtTop ? 1 : 0);
            cmd.SetComputeTextureParam(CalculateVertexByTilesCS, CalculateVertexKernel, VertexProfilerUtil._RenderIdAndDepthRT, m_OutputRenderIdRT);
            cmd.SetComputeTextureParam(CalculateVertexByTilesCS, CalculateVertexKernel, VertexProfilerUtil._TileProfilerRTUint, m_TileProfilerUIntRT);
            
            cmd.SetComputeTextureParam(CalculateVertexByTilesCS, CalculateVertexKernel2, VertexProfilerUtil._RenderIdAndDepthRT, m_OutputRenderIdRT);
            cmd.SetComputeTextureParam(CalculateVertexByTilesCS, CalculateVertexKernel2, VertexProfilerUtil._TileProfilerRTUint, m_TileProfilerUIntRT);
            cmd.SetComputeTextureParam(CalculateVertexByTilesCS, CalculateVertexKernel2, VertexProfilerUtil._TileProfilerRTUint2, m_TileProfilerUInt2RT);

            cmd.SetComputeVectorParam(GenerateProfilerRTCS, VertexProfilerUtil._ScreenParams, new Vector4(camera.pixelWidth, camera.pixelHeight, 1.0f / camera.pixelWidth, 1.0f / camera.pixelHeight));
            cmd.SetComputeIntParam(GenerateProfilerRTCS, VertexProfilerUtil._HeatMapRange, m_Settings.HeatMapRange);
            cmd.SetComputeIntParam(GenerateProfilerRTCS, VertexProfilerUtil._HeatMapStep, m_Settings.HeatMapStep);
            cmd.SetComputeIntParam(GenerateProfilerRTCS, VertexProfilerUtil._HeatMapOffsetCount, m_Settings.HeatMapOffsetCount);
            cmd.SetComputeIntParam(GenerateProfilerRTCS, VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            cmd.SetComputeBufferParam(GenerateProfilerRTCS, GenerateProfilerKernel, VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            cmd.SetComputeVectorParam(GenerateProfilerRTCS, VertexProfilerUtil._HeatMapRampRange, new Vector2(m_Settings.HeatMapRampMin, m_Settings.HeatMapRampMax));
            cmd.SetComputeTextureParam(GenerateProfilerRTCS, GenerateProfilerKernel, VertexProfilerUtil._TileProfilerRTUint2, m_TileProfilerUInt2RT);
            cmd.SetComputeTextureParam(GenerateProfilerRTCS, GenerateProfilerKernel, VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);

            cmd.SetGlobalTexture(VertexProfilerUtil._TileProfilerRT, m_TileProfilerRT);
            cmd.SetGlobalTexture(VertexProfilerUtil._HeatMapTex, m_Settings.m_FeatureData.textures.heatMapTex);
            
            cmd.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            cmd.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public override void Dispatch(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 先做一次前向渲染收集深度信息和rendererId信息,最终结果复制到支持随机读写的纹理中
            CoreUtils.SetRenderTarget(cmd, m_OutputRenderIdRT, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                m_OutputRenderIdDepthRT,
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                ClearFlag.Color | ClearFlag.Depth, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            DrawObjects(cmd, m_ProfilingSamplerOutputRenderer, OutputRendererIdMat, ref URPOutputRendererTagId, ref context, ref renderingData);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            // 还原原始的颜色缓冲
            CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTarget, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                renderingData.cameraData.renderer.cameraDepthTarget, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                ClearFlag.Color | ClearFlag.Depth, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            using (new ProfilingScope(cmd, new ProfilingSampler("Calculate Vertex By Tiles")))
            {
                Mesh mesh;
                for (int k = 0; k < m_RendererNum; k++)
                {
                    uint flag = VisibleFlag[k];
                    if (flag > 0)
                    {
                        Matrix4x4 localToWorld = m_RendererLocalToWorldMatrix[k];
                        RendererComponentData data = rendererComponentDatas[k];
                        if(!data.renderer.enabled)
                            continue;

                        mesh = data.m;
                        mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                        mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                        if(mesh == null)
                            continue;
                        
                        var meshBuffer = RendererCuller.GetGraphicBufferByMesh(mesh);

                        if(meshBuffer == null) continue;
                        
                        int count = mesh.vertexCount;
                        cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._VertexNum, count);
                        cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._RendererId, k);
                        cmd.SetComputeIntParam(CalculateVertexByTilesCS, VertexProfilerUtil._VertexDataSize, meshBuffer.stride / sizeof(float));
                        cmd.SetComputeMatrixParam(CalculateVertexByTilesCS, VertexProfilerUtil._LocalToWorld, localToWorld);
                        cmd.SetComputeBufferParam(CalculateVertexByTilesCS, CalculateVertexKernel, VertexProfilerUtil._VertexData, meshBuffer);
                        cmd.DispatchCompute(CalculateVertexByTilesCS, CalculateVertexKernel, Mathf.CeilToInt((float)count / CalculateVertexByTilesCSGroupX), 1, 1);
                    }
                }
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            using (new ProfilingScope(cmd, new ProfilingSampler("Merge Data RT")))
            {
                cmd.DispatchCompute(CalculateVertexByTilesCS, CalculateVertexKernel2, Mathf.CeilToInt((float)m_TileProfilerUInt2RT.rt.width / 16), Mathf.CeilToInt((float)m_TileProfilerUInt2RT.rt.height / 16), 1);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            using (new ProfilingScope(cmd, new ProfilingSampler("Generate Profiler RT")))
            {
                cmd.DispatchCompute(GenerateProfilerRTCS, GenerateProfilerKernel, Mathf.CeilToInt((float)m_TileProfilerRT.rt.width / 16), Mathf.CeilToInt((float)m_TileProfilerRT.rt.height / 16), 1);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        
        private void DrawObjects(CommandBuffer cmd, ProfilingSampler profilerSampler, 
            Material overrideMat, ref List<ShaderTagId> shaderTagIds, 
            ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            using (new ProfilingScope(cmd, profilerSampler))
            {
                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags; 
                
                DrawingSettings drawSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortFlags);
                
                if(overrideMat != null)
                    drawSettings.overrideMaterial = overrideMat;
                
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings, ref m_renderStateBlock);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }
}