using System;
using UnityEngine;

// Captura imágenes de la escena para que Claude pueda ver el resultado.
public static partial class BridgeServer
{
    [Serializable]
    class CaptureRequest
    {
        public float x;
        public float y;
        public float z;
        public float targetX;
        public float targetY;
        public float targetZ;
        public float fov;
        public float farClip;
        public int width;
        public int height;
    }

    static string ExecuteCapture(string command, string body)
    {
        switch (command)
        {
            case "capture_scene":
                return CaptureScene(JsonUtility.FromJson<CaptureRequest>(body));

            default:
                return ExecuteModules(command, body);
        }
    }

    static string CaptureScene(CaptureRequest req)
    {
        int w = req.width > 0 ? Mathf.Clamp(req.width, 256, 1600) : 960;
        int h = req.height > 0 ? Mathf.Clamp(req.height, 144, 900) : 540;
        float fov = req.fov > 0 ? Mathf.Clamp(req.fov, 10f, 120f) : 60f;
        float far = req.farClip > 0 ? req.farClip : 3000f;

        var origin = new Vector3(req.x, req.y, req.z);
        var target = new Vector3(req.targetX, req.targetY, req.targetZ);
        if ((target - origin).sqrMagnitude < 0.01f)
            return Json(false, "La cámara y el punto que mira están en el mismo lugar.");

        var camObject = new GameObject("MCP_CaptureCamera");
        camObject.hideFlags = HideFlags.HideAndDontSave;
        RenderTexture rt = null;
        Texture2D tex = null;
        RenderTexture previous = RenderTexture.active;

        try
        {
            var cam = camObject.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = far;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.transform.position = origin;
            cam.transform.LookAt(target);

            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            byte[] png = tex.EncodeToPNG();
            return Json(true, "IMG:" + Convert.ToBase64String(png));
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(camObject);
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
            if (tex != null)
                UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}