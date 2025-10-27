using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
namespace VertexProfilerTool
{
    public class VertexProfilerBase : MonoBehaviour
    {
        public Camera MainCamera;
        [Range(32, 128)]
        public int TileWidth = 100;
        [Range(32, 128)]
        public int TileHeight = 100;
		
        public bool NeedRecollectRenderers = true;
        
        
        public int TileNumX = 1;
        public int TileNumY = 1;
        
       
        
        // log 
        public GameObject GoUITile;
        public bool NeedLogOutProfilerData = false;
        public bool NeedUpdateUITileGrid = true;
        [HideInInspector]public bool NeedLogDataToProfilerWindow = false;
        [HideInInspector]public int LastLogFrameCount = 0;

        // public bool HideGoTUITile = false;
        // public bool HideTileNum = false;
        internal List<UITile> GoUITileList = new List<UITile>();
        internal Canvas tileCanvas;
        
        #if UNITY_EDITOR
        /// <summary>
        /// 创建Canvas和EventSystem
        /// </summary>
        internal void InitUITile()
        {
            #if UNITY_EDITOR
            // 尝试查找场景tile grid画布对象
            GameObject go = GameObject.Find("TileGridCanvas");
            if (go == null)
            {
                var AllCanvas = FindObjectsOfType<Canvas>(true);
                foreach (var canvas in AllCanvas)
                {
                    if (canvas.name.Equals("TileGridCanvas"))
                    {
                        go = canvas.gameObject;
                        break;
                    }
                }
            }
            if (go == null)
            {
                go = new GameObject("TileGridCanvas");
                go.hideFlags = HideFlags.None;
                go.layer = LayerMask.NameToLayer("UI");
            }

            tileCanvas = go.GetComponent<Canvas>();
            if (tileCanvas == null)
            {
                tileCanvas = go.AddComponent<Canvas>();
                tileCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }
            if (go.GetComponent<CanvasScaler>() == null)
            {
                go.AddComponent<CanvasScaler>();
            }
            if (go.GetComponent<GraphicRaycaster>() == null)
            {
                go.AddComponent<GraphicRaycaster>();
            }
            var esys = FindObjectOfType<EventSystem>();
            if (esys == null)
            {
                var eventSystem = new GameObject("EventSystem");
                GameObjectUtility.SetParentAndAlign(eventSystem, null);
                esys = eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<StandaloneInputModule>();
            }
            #endif
        }
        #endif
        
     
        internal void InitCamera()
        {
            if (MainCamera == null)
            {
                MainCamera = Camera.main;
            }
        }
        internal void Update()
        {
            // 同步UI网格
            if (NeedUpdateUITileGrid)
            {
                UpdateGoTileGrid();
            }
        }
        
        #region UI
        public void CheckShowUIGrid(DisplayType displayType, bool hideGoTUITile)
        {
            // 检查是否需要显示uiTile
            bool showCanvas = displayType != DisplayType.OnlyMesh
                              && displayType != DisplayType.MeshHeatMap 
                              && displayType != DisplayType.Overdraw 
                              && !hideGoTUITile;
            if (tileCanvas != null && tileCanvas.gameObject.activeSelf != showCanvas)
            {
                tileCanvas.gameObject.SetActive(showCanvas);
            }
        }

        public void SetTileNumShow(bool hideTileNum)
        {
            for (int i = 0; i < GoUITileList.Count; i++)
            {
                var uiTile = GoUITileList[i];
                if (uiTile)
                {
                    uiTile.SetTileNumActive(!hideTileNum);
                }
            }
        }
        internal void UpdateGoTileGrid()
        {
            // if (EDisplayType == DisplayType.OnlyMesh) return;
            
            TileNumX = Mathf.CeilToInt((float)MainCamera.pixelWidth / (float)TileWidth);
            TileNumY = Mathf.CeilToInt((float)MainCamera.pixelHeight / (float)TileHeight);
            
            int needNum = TileNumX * TileNumY;
            if (GoUITileList.Count < needNum)
            {
                for (int i = GoUITileList.Count; i < needNum; i++)
                {
                    // 先尝试获取，再实例化
                    var trans = tileCanvas.transform.Find("UITile" + i);
                    GameObject go = trans != null ? trans.gameObject : Instantiate(GoUITile, Vector3.zero, Quaternion.identity, tileCanvas.transform);
                    UITile uitile = go.GetComponent<UITile>();
                    GoUITileList.Add(uitile);
                }
            }

            for (int i = 0; i < GoUITileList.Count; i++)
            {
                var uitile = GoUITileList[i];
                if (i < needNum)
                {
                    uitile.SetData(TileWidth, TileHeight, TileNumX, i);
                }
                uitile.SetActive(i < needNum);
                uitile.SetTileNumActive(false);
            }

            NeedUpdateUITileGrid = false;
        }
        #endregion
        #region Recycle
        internal void OnDestroy()
        {
            for (int i = GoUITileList.Count - 1; i >= 0; i--)
            {
                DestroyImmediate(GoUITileList[i]);
            }
            GoUITileList.Clear();
        }
        #endregion
    }
}