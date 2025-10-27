using System.Collections;
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
    public class VertexProfilerModeOnlyMeshRenderPass : VertexProfilerModeBaseRenderPass
    {
        private Material MeshPixelCalMat;
        private VertexProfilerJobs.J_Culling Job_Culling;

        private RTHandle m_RendererIdAndVertexCountRT;
        private RTHandle m_RendererIdAndVertexDepthCountRT;
        // 原生渲染所需属性
        List<ShaderTagId> URPMeshPixelCalShaderTagId;
        FilteringSettings m_FilteringSettings;
        RenderStateBlock m_renderStateBlock;
        ProfilingSampler m_ProfilingSampler;
        
        public VertexProfilerModeOnlyMeshRenderPass()
        {
            m_FilteringSettings = new FilteringSettings(RenderQueueRange.all, -1);
            m_renderStateBlock = new RenderStateBlock(RenderStateMask.Nothing);
            m_ProfilingSampler = new ProfilingSampler("PixelCalShader");
        }

        public override void Setup(VertexProfilerRendererFeature.Settings settings)
        {
	        base.Setup(settings);
            // 不能在构造函数初始化的部分在这创建
            URPMeshPixelCalShaderTagId = new List<ShaderTagId>() {new ("SRPDefaultUnlit"), new ("UniversalForward"), new ("UniversalForwardOnly")};
            if (vp != null)
            {
                vp.ProfilerMode = this;
               
                CheckColorRangeData(true);
                MeshPixelCalMat = m_Settings.m_FeatureData.materials.MeshPixelCalMat;
            };
        }

        public override void OnDisable()
        {
            base.OnDisable();
            ReleaseRTHandle(ref m_RendererIdAndVertexCountRT);
            ReleaseRTHandle(ref m_RendererIdAndVertexDepthCountRT);
            if (vp != null && vp.ProfilerMode == this)
            {
                vp.ProfilerMode = null;
            }

            ReleaseAllComputeBuffer();
        }

        public override bool CheckProfilerEnabled()
        {
            return vp != null
                   && MeshPixelCalMat != null;
        }

        public override void UseDefaultColorRangeSetting()
        {
            // 简单模式默认使用硬编码的阈值，不做处理
            m_Settings.OnlyMeshDensitySetting = new List<int>(VertexProfilerUtil.DefaultOnlyMeshDensitySetting);
            DensityList.Clear();
            NeedSyncColorRangeSetting = true;
            CheckColorRangeData();
        }

        public override void CheckColorRangeData(bool forceReload = false)
        {
	        if (DensityList.Count <= 0 || forceReload)
	        {
		        DensityList.Clear();

		        foreach (int v in m_Settings.OnlyMeshDensitySetting)
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
            base.SetupConstantBufferData(cmd, ref context, ref renderingData);

            var camera = renderingData.cameraData.camera;
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
            ReAllocTileProfilerRT(width, height, GraphicsFormat.R32G32_SFloat, 
                GraphicsFormat.None, FilterMode.Point, ref m_RendererIdAndVertexCountRT, "_RendererIdAndVertexCountRT");
            ReAllocTileProfilerRT(width, height, GraphicsFormat.None, GraphicsFormat.D24_UNorm, 
                FilterMode.Point, ref m_RendererIdAndVertexDepthCountRT, "_RendererIdAndVertexDepthCountRT", false);
            
            m_PixelCounterBuffer = new ComputeBuffer(m_RendererNum, Marshal.SizeOf(typeof(uint)));
            m_PixelCounterBuffer.SetData(new uint[m_RendererNum]);
            cmd.SetRandomWriteTarget(4, m_PixelCounterBuffer);
            cmd.SetGlobalBuffer(VertexProfilerUtil._PixelCounterBuffer, m_PixelCounterBuffer);
            cmd.SetGlobalInt(VertexProfilerUtil._ColorRangeSettingCount, m_ColorRangeSettings.Length);
            cmd.SetGlobalBuffer(VertexProfilerUtil._ColorRangeSetting, m_ColorRangeSettingBuffer);
            cmd.SetGlobalTexture(VertexProfilerUtil._RendererIdAndVertexCountRT, m_RendererIdAndVertexCountRT);
            
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public override void Dispatch(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // 先做一次前向渲染收集rendererId和顶点数信息，以及单独的深度信息,最终结果复制到支持随机读写的纹理中
            CoreUtils.SetRenderTarget(cmd, m_RendererIdAndVertexCountRT, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                m_RendererIdAndVertexDepthCountRT,
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                ClearFlag.Color | ClearFlag.Depth, Color.black);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            DrawObjects(cmd, ref URPMeshPixelCalShaderTagId, ref context, ref renderingData);
            
            // 还原原始的颜色缓冲
            CoreUtils.SetRenderTarget(cmd, renderingData.cameraData.renderer.cameraColorTarget, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                renderingData.cameraData.renderer.cameraDepthTarget, 
                RenderBufferLoadAction.Load, 
                RenderBufferStoreAction.Store, 
                ClearFlag.Color | ClearFlag.Depth, Color.black);
            
            cmd.ClearRandomWriteTargets();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        
        private void DrawObjects(CommandBuffer cmd, ref List<ShaderTagId> shaderTagIds, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags; 
                
                DrawingSettings drawSettings = CreateDrawingSettings(shaderTagIds, ref renderingData, sortFlags);
                drawSettings.overrideMaterial = MeshPixelCalMat;
                context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref m_FilteringSettings, ref m_renderStateBlock);
            }
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
    }
    
    [System.Serializable]
    public class VertexProfilerOnlyMeshLogRenderPass : VertexProfilerLogBaseRenderPass
    {
        // log 
        private bool pixelCountDataReady = false;
        uint[] pixelCountData = null;
        private RTHandle m_ScreenshotRT;

        public override void Setup(VertexProfilerRendererFeature.Settings settings)
        {
            if (vp == null) return;
			base.Setup(settings);
            vp.LogMode = this;
        }
        
        public override void OnDisable()
        {
            if (m_ScreenshotRT != null)
            {
                m_ScreenshotRT.Release();
                m_ScreenshotRT = null;
            }
            if (vp != null && vp.LogMode == this)
            {
                vp.LogMode = null;
            }
        }
        public override void DispatchScreenShotAndReadBack(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
	        if (vp == null) return;
	        if (vp.LogMode != this) return;
	        if (VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer == null
	            || !VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer.IsValid())
		        return;
            
            pixelCountDataReady = false;
            pixelCountData = null;
            
            var camera = renderingData.cameraData.camera;
            // 截图
            RenderTextureDescriptor desc = new RenderTextureDescriptor(camera.pixelWidth, camera.pixelHeight, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None, 0);
            desc.enableRandomWrite = true;
            VertexProfilerUtil.ReAllocRTIfNeeded(ref m_ScreenshotRT, desc, FilterMode.Point, TextureWrapMode.Clamp, false, name: "ScreenShot");
           
            cmd.Blit(colorAttachment, m_ScreenshotRT, m_Settings.m_FeatureData.materials.GammaCorrectionEffectMat);
            
            // 拉取数据，异步回读
            cmd.RequestAsyncReadback(VertexProfilerModeBaseRenderPass.m_PixelCounterBuffer,
                sizeof(uint) * VertexProfilerModeBaseRenderPass.rendererComponentDatas.Count, 0, 
                (data) =>
            {
                pixelCountDataReady = true;
                pixelCountData = data.GetData<uint>().ToArray();
                LogoutProfilerData();
            });
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }
        private void LogoutProfilerData()
        {
            // 数据准备完成后再开始
            if (!pixelCountDataReady) return;
            vp.StartCoroutineForProfiler(PullOnlyMeshProfilerData());
        }
        private IEnumerator PullOnlyMeshProfilerData()
        {
            logoutDataList.Clear();
            Mesh mesh;
            for (int i = 0; i < pixelCountData.Length; i++)
            {
                uint flag = VertexProfilerModeBaseRenderPass.VisibleFlag[i];
                if (flag == 0) continue;
                
                RendererComponentData data = VertexProfilerModeBaseRenderPass.rendererComponentDatas[i];
                var renderer = data.renderer;
                if(renderer == null)
                    continue;
                mesh = data.m;
                mesh = data.mf != null ? data.mf.sharedMesh : mesh;
                mesh = data.smr != null ? data.smr.sharedMesh : mesh;
                if(mesh == null) 
                    continue;

                int vertexCount = mesh.vertexCount;
                int pixelCount = (int)pixelCountData[i];
                if(vertexCount == 0) continue;
                float density = pixelCount > 0 ? (float)vertexCount / (float)pixelCount : float.MaxValue;
                string rendererHierarchyPath = VertexProfilerUtil.GetGameObjectNameFromRoots(renderer.transform);
                Color profilerColor = VertexProfilerModeBaseRenderPass.GetProfilerContentColor(density, out int thresholdLevel);
                ProfilerDataContents content = new ProfilerDataContents(
                    mesh.name,
                    vertexCount, 
                    pixelCount,
                    density,
                    rendererHierarchyPath,
                    thresholdLevel,
                    profilerColor);
                logoutDataList.Add(content);
            }
            // log一次就置false
            vp.NeedLogDataToProfilerWindow = false;
            vp.LastLogFrameCount = Time.frameCount;
            
            //截屏需要等待渲染线程结束
            yield return new WaitForEndOfFrame();
            
            // 如果需要输出到Excel才执行
            if (vp.NeedLogOutProfilerData)
            {
                vp.NeedLogOutProfilerData = false;
                //初始化Texture2D, 大小可以根据需求更改
                Texture2D screenShotWithPostEffect = new Texture2D(vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight, TextureFormat.RGB24, false);
                screenShotWithPostEffect.name = "screenShotWithPostEffect";
                //读取屏幕像素信息并存储为纹理数据
                screenShotWithPostEffect.ReadPixels(new Rect(0, 0, vp.MainCamera.pixelWidth, vp.MainCamera.pixelHeight), 0, 0);
                //应用
                screenShotWithPostEffect.Apply();
                // 写入Excel
                VertexProfilerEvent.CallLogoutToExcel(DisplayType.OnlyMesh, logoutDataList, m_ScreenshotRT.rt, screenShotWithPostEffect);
                // 释放资源
                Object.DestroyImmediate(screenShotWithPostEffect);
            }
        }
    }
}
