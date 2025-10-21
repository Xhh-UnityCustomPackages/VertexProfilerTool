using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace VertexProfilerTool
{
    [System.Serializable]
    public static class RendererCuller
    {
        private static List<RendererComponentData> rendererComponentDataList = new List<RendererComponentData>();
        private static Dictionary<int, GraphicsBuffer> cacheMeshGraphicBufferDict = new Dictionary<int, GraphicsBuffer>();

        private static bool hasRevertRendererMat = false;
        
        /// <summary>
        /// 每次调度，统一收集当前场景内的带mesh组件的对象
        /// </summary>
        /// <returns></returns>
        public static List<RendererComponentData> GetAllRenderers(bool rawGet = false)
        {
            if (rawGet)
            {
                return rendererComponentDataList;
            }
            rendererComponentDataList.Clear();
            
            var mfs = GetAllMeshFilter();
            var smrs = GetAllSkinnedMeshRenderer();

            Renderer _renderer;

            foreach (var v in mfs)
            {
                _renderer = v.GetComponent<Renderer>();
                if(_renderer == null || !_renderer.enabled) continue;

                RendererComponentData data = new RendererComponentData();
                data.renderer = _renderer;
                data.mf = v;
                rendererComponentDataList.Add(data);
            }
            
            foreach (var v in smrs)
            {
                _renderer = v.GetComponent<Renderer>();
                if(_renderer == null || !_renderer.enabled) continue;

                RendererComponentData data = new RendererComponentData();
                data.renderer = _renderer;
                data.smr = v;
                rendererComponentDataList.Add(data);
            }

            return rendererComponentDataList;
        }

        public static MeshFilter[] GetAllMeshFilter()
        {
            return GameObject.FindObjectsOfType<MeshFilter>(false);
        }
        
        public static SkinnedMeshRenderer[] GetAllSkinnedMeshRenderer()
        {
            return GameObject.FindObjectsOfType<SkinnedMeshRenderer>(false);
        }
        

        public static GraphicsBuffer GetGraphicBufferByMesh(Mesh mesh)
        {
            int id = mesh.GetInstanceID();
            if (!cacheMeshGraphicBufferDict.ContainsKey(id))
            {
                mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
                GraphicsBuffer meshBuffer = mesh.GetVertexBuffer(0);
                cacheMeshGraphicBufferDict.Add(id, meshBuffer);
            }

            if (!cacheMeshGraphicBufferDict.ContainsKey(id))
            {
                return null;
            }

            return cacheMeshGraphicBufferDict[id];
        }
    }
}

