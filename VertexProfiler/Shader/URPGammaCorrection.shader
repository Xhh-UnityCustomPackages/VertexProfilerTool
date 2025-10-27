/*
* @aAuthor: SaberGodLY
* @Description: 截屏（无UI），此shader用于做输出前的Gamma映射
*/
Shader "VertexProfiler/URPGammaCorrection"
{
	SubShader
	{
		// No culling or depth
		Cull Off ZWrite Off ZTest Always

		Pass
		{
			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment frag
			
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

			half4 frag (Varyings i) : SV_Target
			{
				half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, i.texcoord);
				col.rgb = pow(col.rgb, 0.454545); // 1.0 / 2.2
				return col;
			}
			ENDHLSL
		}
	}
}