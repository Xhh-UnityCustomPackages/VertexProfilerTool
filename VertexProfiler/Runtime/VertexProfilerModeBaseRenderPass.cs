using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VertexProfilerTool
{
    [System.Serializable]
    public abstract class VertexProfilerModeBaseRenderPass : ScriptableRenderPass
    {
        public static VertexProfilerURP vp;
        public CullMode ECullMode = CullMode.Back;
        public List<int> DensityList = new List<int>();
        public bool NeedSyncColorRangeSetting = true;
        
        public static ComputeBuffer m_VertexCounterBuffer;
        public static ComputeBuffer m_PixelCounterBuffer;
        public static ComputeBuffer m_TileVerticesCountBuffer;
        public static List<RendererComponentData> rendererComponentDatas;
        
        public static List<uint> VisibleFlag;
        public static ColorRangeSetting[] m_ColorRangeSettings;
        // Tile Type Buffers
        internal ComputeBuffer m_ColorRangeSettingBuffer;
        internal int m_RendererNum;
        internal List<RendererBoundsData> m_RendererBoundsData = new List<RendererBoundsData>();
        internal List<Matrix4x4> m_RendererLocalToWorldMatrix = new List<Matrix4x4>();
    
        protected VertexProfilerRendererFeature.Settings m_Settings;

        public virtual void Setup(VertexProfilerRendererFeature.Settings settings)
        {
	        m_Settings = settings;
        }

        public virtual void OnDisable()
        {
            ReleaseAllComputeBuffer();
        }
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            
            ReleaseAllComputeBuffer();
            if (!CheckProfilerEnabled())
            {
                Shader.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 0);
                return;
            }

            InitRenderers();
            if (m_RendererNum <= 0)
            {
                Shader.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 0);
                return;
            }
        
            // 更新颜色阈值到GPU
            CheckColorRangeData();
            CommandBuffer cmd = CommandBufferPool.Get();
            // 设置静态Buffer到GPU
            SetupConstantBufferData(cmd, ref context, ref renderingData);
            // 调度预渲染统计信息
            Dispatch(cmd, ref context, ref renderingData);
            CommandBufferPool.Release(cmd);
        }


        public override void OnCameraCleanup(CommandBuffer cmd)
        {
        }
        
        public virtual bool CheckProfilerEnabled()
        {
            return false;
        }
        public virtual void UseDefaultColorRangeSetting()
        {
            
        }

        public virtual void CheckColorRangeData(bool forceReload = false)
        {
            
        }

        public virtual void InitRenderers()
        {
            
        }

        public virtual void SetupConstantBufferData(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            cmd.SetGlobalInt(VertexProfilerUtil._EnableVertexProfiler, 1);
            cmd.SetGlobalInt(VertexProfilerUtil._DisplayType, (int)m_Settings.displayType);
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            if (m_ColorRangeSettings != null && m_ColorRangeSettings.Length > 0)
            {
                m_ColorRangeSettingBuffer = new ComputeBuffer(m_ColorRangeSettings.Length, Marshal.SizeOf(typeof(ColorRangeSetting)));
                m_ColorRangeSettingBuffer.SetData(m_ColorRangeSettings);
            }
        }

        public virtual void Dispatch(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            
        }
        
        public virtual void ReleaseAllComputeBuffer()
        {
            ReleaseComputeBuffer(ref m_ColorRangeSettingBuffer);
            
            ReleaseComputeBuffer(ref m_VertexCounterBuffer);
            ReleaseComputeBuffer(ref m_PixelCounterBuffer);
            ReleaseComputeBuffer(ref m_TileVerticesCountBuffer);
        }

        internal static void ReleaseComputeBuffer(ref ComputeBuffer _buffer)
        {
            if (_buffer != null)
            {
                _buffer.Release();
                _buffer = null;
            }
        }

        internal static void ReleaseRTHandle(ref RTHandle handle)
        {
            if (handle != null)
            {
                handle.Release();
                handle = null;
            }
        }
        
        internal void ReAllocTileProfilerRT(int width, int height, GraphicsFormat colorFormat, GraphicsFormat depthFormat, FilterMode filterMode, ref RTHandle handle, string handleName = "", bool randomWrite = true)
        {
            RenderTextureDescriptor desc = new RenderTextureDescriptor(width, height, colorFormat, depthFormat, 0);
            desc.enableRandomWrite = randomWrite;
            VertexProfilerUtil.ReAllocRTIfNeeded(ref handle, desc, filterMode, TextureWrapMode.Clamp, false, name: handleName);
        }
        
        public static Color GetProfilerContentColor(float content, out int level)
        {
            Color color = Color.white;
            level = 0;
            if (m_ColorRangeSettings != null && m_ColorRangeSettings.Length > 0)
            {
                for (int i = 0; i < m_ColorRangeSettings.Length; i++)
                {
                    var setting = m_ColorRangeSettings[i];
                    if (setting.threshold < content)
                    {
                        color = setting.color;
                        level = i;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            return color;
        }
    }
    
    // 用于处理后处理
    [System.Serializable]
    public class VertexProfilerPostEffectRenderPass : ScriptableRenderPass
    {
        private VertexProfilerURP vp;

        private VertexProfilerRendererFeature.Settings m_Settings;
        
        string[] m_ShaderKeywords = new string[1];
        private Material m_ProfileMaterial;
        public void Setup(VertexProfilerRendererFeature.Settings settings)
        {
	        m_Settings = settings;
	        if (m_ProfileMaterial == null)
		        m_ProfileMaterial = Material.Instantiate(m_Settings.m_FeatureData.materials.ApplyProfilerDataByPostEffectMat);
        }
        
        public static string GetDebugKeyword(DisplayType debugMode)
        {
	        switch (debugMode)
	        {
		        case DisplayType.OnlyTile:
			        return "_DEBUG_ONLY_TILE_MODE";
		        case DisplayType.OnlyMesh:
			        return "_DEBUG_ONLY_MESH_MODE";
		        case DisplayType.Overdraw:
			        return "_DEBUG_OVERDRAW_MODE";
		        case DisplayType.TileBasedMesh:
			        return "_DEBUG_TILE_BASED_MESH_MODE";
		        case DisplayType.MeshHeatMap:
			        return "_DEBUG_MESH_HEAT_MAP_MODE";
		        case DisplayType.None:
		        default:
			        return "_";
	        }
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType != CameraType.Game) return;
            // 进入后处理阶段，使用m_TileProfilerRT之前需要执行一次释放
            CommandBuffer cmd = CommandBufferPool.Get();
            cmd.ClearRandomWriteTargets();
            context.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            
            m_ShaderKeywords[0] = GetDebugKeyword(m_Settings.displayType);
            m_ProfileMaterial.shaderKeywords = m_ShaderKeywords;
            Blit(cmd, ref renderingData, m_ProfileMaterial);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
        
        public override void OnCameraCleanup(CommandBuffer cmd)
        {

        }

        public void OnDisable()
        {
	        CoreUtils.Destroy(m_ProfileMaterial);
        }
    }

    [System.Serializable]
    public class VertexProfilerLogBaseRenderPass : ScriptableRenderPass
    {
        public static VertexProfilerURP vp;
        public List<ProfilerDataContents> logoutDataList = new List<ProfilerDataContents>();

        protected VertexProfilerRendererFeature.Settings m_Settings;
        public virtual void Setup(VertexProfilerRendererFeature.Settings settings)
        {
	        m_Settings = settings;
        }

        public virtual void OnDisable()
        {
            
        }
        
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (vp == null) return;
            
            CommandBuffer cmd = CommandBufferPool.Get();
            // 处理log操作
            if (vp.NeedLogOutProfilerData || vp.NeedLogDataToProfilerWindow)
            {
                DispatchScreenShotAndReadBack(cmd, ref context, ref renderingData);
            }
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public virtual void DispatchScreenShotAndReadBack(CommandBuffer cmd, ref ScriptableRenderContext context, ref RenderingData renderingData)
        {
            
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            
        }
    }
}


