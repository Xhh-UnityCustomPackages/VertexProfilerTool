﻿Shader "VertexProfiler/URPApplyProfilerDataByPostEffect"
{
	SubShader
	{
		Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
		Pass
		{
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment frag

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
			#include "VertexProfilerURPCore.hlsl"

			TEXTURE2D_X(_SourceTex);
            SAMPLER(sampler_SourceTex);
			
			half4 frag (Varyings i) : SV_Target
			{
				half4 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.texcoord);
				half4 profilerColor = half4(1, 1, 1, 1);
				if(_DisplayType == ONLY_TILE_MODE || _DisplayType == OVERDRAW_MODE)
				{
					profilerColor = DisplayVertexProfilerByRT(i.texcoord);
				}
				if(_DisplayType == ONLY_MESH_MODE)
				{
					int2 rendererIdAndVertexCount = SAMPLE_TEXTURE2D_X(_RendererIdAndVertexCountRT, sampler_RendererIdAndVertexCountRT, i.texcoord).rg; // r:RendererId + 1(使用时需要-1) g:VertexCount
					profilerColor = DisplayVertexProfilerForOnlyMesh(rendererIdAndVertexCount.r - 1, rendererIdAndVertexCount.y);
				}
				if(_DisplayType == TILE_BASED_MESH_MODE)
				{
					int2 rendererIdAndVertexCount = SAMPLE_TEXTURE2D_X(_RendererIdAndVertexCountRT, sampler_RendererIdAndVertexCountRT, i.texcoord).rg; // r:RendererId + 1(使用时需要-1) g:VertexCount
					profilerColor = DisplayVertexProfilerByTileBasedMesh(i.texcoord, rendererIdAndVertexCount.r - 1);
				}
				if(_DisplayType == MESH_HEAT_MAP_MODE)
				{
					profilerColor = DisplayVertexProfilerForMeshHeatMap(i.texcoord);
				}
				
				col.rgb = lerp(col.rgb, col.rgb * profilerColor.rgb, profilerColor.a);
				return col;
			}
			ENDHLSL
		}
	}
}