using UnityEngine;
using System;

namespace VertexProfilerTool
{
	// [CreateAssetMenu(fileName = "VertexProfilerRendererFeatureData", menuName = "VertexProfilerRendererFeatureData", order = 0)]
	public class VertexProfilerRendererFeatureData : ScriptableObject
	{
		public ShaderResources shaders;
		public TextureResources textures;
		public MaterialResources materials;
	}
	
	[Serializable]
	public sealed class ShaderResources
	{
		public ComputeShader calculateVertexByTilesCS;
		public ComputeShader generateProfilerRTCS;

		public Shader vertexProfilerReplaceShader;
		public Shader applyProfilerDataByPostEffectShader;
		public Shader gammaCorrectionShader;
		public Shader meshPixelCalShader;
	}
	
	[Serializable]
	public sealed class TextureResources
	{
		public Texture heatMapTex;
	}

	[Serializable]
	public sealed class MaterialResources
	{
		public Material ApplyProfilerDataByPostEffectMat;
		public Material MeshPixelCalMat;
		public Material GammaCorrectionEffectMat;
	}
}