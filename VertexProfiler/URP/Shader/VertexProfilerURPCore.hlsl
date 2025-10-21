#ifndef VERTEX_PROFILER_URP_CORE
#define VERTEX_PROFILER_URP_CORE

#include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "VertexProfilerModeInclude.hlsl"

int _RendererId;
int _VertexCount;

struct ColorRangeSetting
{
    /// <summary>
    /// 阈值下限(含)
    /// 当使用OnlyTile或TileBasedMesh时单位为【屏幕顶点数/1万屏幕像素】
    /// 当使用OnlyMesh时单位为【Mesh顶点数/Mesh占用像素】
    /// </summary>
    float threshold;
    float4 color;
};
StructuredBuffer<ColorRangeSetting> _ColorRangeSetting;
uniform int _ColorRangeSettingCount;
StructuredBuffer<uint> _TileVerticesCount;
uniform float4 _TileParams2;  // 分块数据（1.0 / width, 1.0 / height, 1.0 / tileNumX, 1.0 / tileNumY）
uniform int _TileWidth;
uniform int _TileNumX;

uniform int _EnableVertexProfiler;
// 0:Only Tile类型 1:Only Mesh类型 2:TIle Based Mesh类型
uniform int _DisplayType;

// Tile类型
TEXTURE2D(_TileProfilerRT);                     SAMPLER(sampler_TileProfilerRT);
TEXTURE2D(_RendererIdAndVertexCountRT);         SAMPLER(sampler_RendererIdAndVertexCountRT); // r:RendererId + 1(使用时需要-1) g:VertexCount
TEXTURE2D(_HeatMapTex);                         SAMPLER(sampler_HeatMapTex);

// OnlyTile or OnlyMesh类型时，格式[RendererId] = uint，用于统计该Renderer在渲染时使用了多少个像素
// TileBasedMesh类型时，格式为[RendererId * TileCount + TileIndex] = uint，用于逐棋盘格统计不同Mesh的像素占比情况
uniform StructuredBuffer<uint> _VertexCounterBuffer;
uniform StructuredBuffer<uint> _PixelCounterBuffer;

uniform int _TileCount;

half4 DisplayVertexProfilerByRT(float2 uv)
{
    return SAMPLE_TEXTURE2D_X(_TileProfilerRT, sampler_TileProfilerRT, uv.xy);
}

half4 DisplayVertexProfilerForOnlyMesh(int rendererId, int vertexCount)
{
    int pixelCount = _PixelCounterBuffer[rendererId];
    pixelCount = max(pixelCount, 1);
    // 颜色过滤
    float density = (float)vertexCount / (float)pixelCount;
    half4 color = half4(1, 1, 1, 1);
    for(int i = 0; i < _ColorRangeSettingCount; i++)
    {
        ColorRangeSetting setting = _ColorRangeSetting[i];
        if(setting.threshold <= density)
        {
            color = setting.color;
        }
        else
        {
            break;
        }
    }

    return color;
}

half4 DisplayVertexProfilerForMeshHeatMap(float2 uv)
{
    half vertexDensity = SAMPLE_TEXTURE2D_X(_TileProfilerRT, sampler_TileProfilerRT, uv.xy).r;
    return half4(SAMPLE_TEXTURE2D_X(_HeatMapTex, sampler_HeatMapTex, half2(saturate(vertexDensity), 0.5)).rgb, 1);
}

half4 DisplayVertexProfilerByTileBasedMesh(float2 uv, int rendererId)
{
    // 根据当前的屏幕空间坐标计算出当前处于哪个tile，根据全局数据计算得到当前Renderer mesh在这个区块的占用像素
    int2 texelPos = uv.xy * _ScreenParams.xy;
    int2 tilePos = texelPos.xy * _TileParams2.xy;
    // 根据写入的bufferId获取占用像素数
    int bufferId = rendererId * _TileCount + tilePos.y * _TileNumX + tilePos.x;
    int vertexCount = _VertexCounterBuffer[bufferId];
    int pixelCount = _PixelCounterBuffer[bufferId];
    half4 color = half4(1, 1, 1, 1);
    float density = (float)vertexCount / (float)pixelCount;
    for(int i = 0; i < _ColorRangeSettingCount; i++)
    {
        ColorRangeSetting setting = _ColorRangeSetting[i];
        if(setting.threshold <= density)
        {
            color = setting.color;
        }
        else
        {
            break;
        }
    }
    return color;
}

/**
 * \brief 
 * \param posWS 
 * \return 根据传递进来的屏幕控件坐标，单独获取profiler的结果。用于不使用后处理来显示profiler结果的情况，不建议使用。
 */
half4 DisplayVertexProfilerPS(float3 posWS)
{
    if(_EnableVertexProfiler == 1)
    {
        float4 posCS = mul(UNITY_MATRIX_VP, float4(posWS, 1.0));
        float4 screenPos = ComputeScreenPos(posCS);
        float3 posHCS = screenPos.xyz / screenPos.w;
        
        if(_DisplayType == ONLY_TILE_MODE || _DisplayType == OVERDRAW_MODE)
        {
            return DisplayVertexProfilerByRT(posHCS.xy);
        }
        if (_DisplayType == ONLY_MESH_MODE)
        {
            return DisplayVertexProfilerForOnlyMesh(_RendererId, _VertexCount);
        }
        if (_DisplayType == TILE_BASED_MESH_MODE)
        {
            return DisplayVertexProfilerByTileBasedMesh(posHCS.xy, _RendererId);
        }
        if (_DisplayType == MESH_HEAT_MAP_MODE)
        {
            return DisplayVertexProfilerForMeshHeatMap(posHCS.xy);
        }
    }
    return 1;
}

//------------- 替换渲染主要逻辑
RWTexture2D<uint> TileProfilerRTBuffer : register(u3);
struct VP_Arrtibutes
{
    float4 vertex : POSITION;
    float2 uv : TEXCOORD0;
};

struct VP_Varyings
{
    float2 uv : TEXCOORD0;
    float3 posWS : TEXCOORD1;
    float4 vertex : SV_POSITION;
};

VP_Varyings vert (VP_Arrtibutes v)
{
    VP_Varyings o;
    o.vertex = TransformObjectToHClip(v.vertex);
    o.uv = v.uv;
    o.posWS = TransformObjectToWorld(v.vertex);
    return o;
}
half4 frag (VP_Varyings i) : SV_Target
{
    // sample the texture
    half3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, i.uv).rgb;
    half4 profilerColor = DisplayVertexProfilerPS(i.posWS);
    col.rgb = lerp(col.rgb, col.rgb * profilerColor.rgb, profilerColor.a);

    return half4(col, 1);
}

#endif 