using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace VertexProfilerTool
{
    [ExecuteInEditMode]
    public class VertexProfilerURP : VertexProfilerBase
    {
        public VertexProfilerURP()
        {
            VertexProfilerModeBaseRenderPass.vp = this;
            VertexProfilerLogBaseRenderPass.vp = this;
        }
        
        public VertexProfilerModeBaseRenderPass ProfilerMode;
        public VertexProfilerLogBaseRenderPass LogMode;
    
        
        [NonReorderable]
        public List<int> DensityList = new List<int>();
        public bool NeedSyncColorRangeSetting = true;
        public CullMode ECullMode = CullMode.Back;
        
        private void Awake()
        {
            InitUITile();
        }

        void Start()
        {
            NeedUpdateUITileGrid = true;
            InitCamera();
        }

        #region public function
        
        public void ChangeProfilerType(int index)
        {
            EProfilerType = (ProfilerType)index;
            ProfilerMode?.ChangeProfilerType(EProfilerType);
        }
        
        #endregion
        
        #region Log out
        public void StartCoroutineForProfiler(IEnumerator routine)
        {
            StartCoroutine(routine);
        }
        #endregion

        #region Recycle

        private new void OnDestroy()
        {
            base.OnDestroy();
            VertexProfilerModeBaseRenderPass.vp = null;
            VertexProfilerLogBaseRenderPass.vp = null;
        }

        #endregion
    }
}
