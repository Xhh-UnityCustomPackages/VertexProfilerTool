using UnityEngine;
using UnityEngine.Rendering.Universal;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VertexProfilerTool
{
	[DisallowMultipleRendererFeature]
	public class VertexProfilerRendererFeature : ScriptableRendererFeature
	{
		[Serializable]
		public class Settings
		{
			[SerializeField] public VertexProfilerRendererFeatureData m_FeatureData;
			public DisplayType displayType = DisplayType.None;
		}
		
		[SerializeField] 
		Settings m_Settings = new();

		public DisplayType displayType
		{
			get { return m_Settings.displayType; }
			set { m_Settings.displayType = value; }
		}

		public bool EnableProfiler;

		private DisplayType m_LastDisplayType = DisplayType.None;
		VertexProfilerModeBaseRenderPass m_ScriptablePass;
		VertexProfilerLogBaseRenderPass m_LogPass;
		VertexProfilerPostEffectRenderPass m_PostEffectPass;
		
		public override void Create()
		{
			CreatePass();
		}

		void CreatePass()
		{
			m_ScriptablePass?.OnDisable();
			m_LogPass?.OnDisable();
			
			switch (m_LastDisplayType)
			{
				case DisplayType.MeshHeatMap:
					m_ScriptablePass = new VertexProfilerModeMeshHeatMapRenderPass();
					break;
				case DisplayType.OnlyMesh:
					m_ScriptablePass = new VertexProfilerModeOnlyMeshRenderPass();
					m_LogPass = new VertexProfilerOnlyMeshLogRenderPass();
					break;
				case DisplayType.OnlyTile:
					m_ScriptablePass = new VertexProfilerModeOnlyTileRenderPass();
					m_LogPass = new VertexProfilerOnlyTileLogRenderPass();
					break;
				case DisplayType.Overdraw:
					m_ScriptablePass = new VertexProfilerModeOverdrawRenderPass();
					break;
				case DisplayType.TileBasedMesh:
					m_ScriptablePass = new VertexProfilerModeTileBasedMeshRenderPass();
					m_LogPass = new VertexProfilerTileBasedMeshLogRenderPass();
					break;
			}

			if (m_PostEffectPass == null) m_PostEffectPass = new VertexProfilerPostEffectRenderPass();
			m_PostEffectPass.renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

			if (m_ScriptablePass != null) m_ScriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingGbuffer;
			if (m_LogPass != null) m_LogPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (m_Settings.m_FeatureData == null)
			{
#if UNITY_EDITOR
				m_Settings.m_FeatureData = AssetDatabase.LoadAssetAtPath<VertexProfilerRendererFeatureData>("Assets/VertexProfiler/URP/Script/Data/VertexProfilerRendererFeatureData.asset");
#endif
				return;
			}

			if (!EnableProfiler) return;
			if (renderingData.cameraData.cameraType != CameraType.Game) return;

			if (m_LastDisplayType != m_Settings.displayType)
			{
				m_LastDisplayType = m_Settings.displayType;
				CreatePass();
			}

			m_ScriptablePass?.Setup(m_Settings);
			m_LogPass?.Setup(m_Settings);
			m_PostEffectPass.Setup(m_Settings);
			
			switch (m_Settings.displayType)
			{
				case DisplayType.MeshHeatMap:
					renderer.EnqueuePass(m_ScriptablePass);
					renderer.EnqueuePass(m_PostEffectPass);
					break;
				case DisplayType.OnlyMesh:
					renderer.EnqueuePass(m_ScriptablePass);
					renderer.EnqueuePass(m_LogPass);
					renderer.EnqueuePass(m_PostEffectPass);
					break;
				case DisplayType.OnlyTile:
					renderer.EnqueuePass(m_ScriptablePass);
					renderer.EnqueuePass(m_LogPass);
					renderer.EnqueuePass(m_PostEffectPass);
					break;
				case DisplayType.Overdraw:
					renderer.EnqueuePass(m_ScriptablePass);
					renderer.EnqueuePass(m_PostEffectPass);
					break;
				case DisplayType.TileBasedMesh:
					renderer.EnqueuePass(m_ScriptablePass);
					renderer.EnqueuePass(m_LogPass);
					renderer.EnqueuePass(m_PostEffectPass);
					break;
			}
		}
		
		private void OnDisable()
		{
			m_ScriptablePass?.OnDisable();
			m_LogPass?.OnDisable();
		}
		
		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				m_ScriptablePass?.OnDisable();
				m_LogPass?.OnDisable();

				m_PostEffectPass = null;
				m_ScriptablePass = null;
				m_LogPass = null;
			}
		}
	}
}