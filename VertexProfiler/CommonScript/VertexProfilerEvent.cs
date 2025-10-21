using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VertexProfilerTool
{
    public delegate void LogoutToExcel(DisplayType displayType, List<ProfilerDataContents> logoutDataList, RenderTexture screenShot = null, Texture2D screenShotWithGrids = null);
	
    public class VertexProfilerEvent
    {
        public static LogoutToExcel LogoutToExcelEvent;

        public static void CallLogoutToExcel(DisplayType displayType, List<ProfilerDataContents> logoutDataList,
            RenderTexture screenShot = null, Texture2D screenShotWithGrids = null)
        {
            if (LogoutToExcelEvent != null)
            {
                LogoutToExcelEvent.Invoke(displayType, logoutDataList, screenShot, screenShotWithGrids);
            }
        }
    }
}