using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Información del proyecto y actualización de assets.
public static partial class BridgeServer
{
    static string ExecuteProject(string command, string body)
    {
        switch (command)
        {
            case "project_info":
                return Json(true, ProjectInfo());

            case "refresh_assets":
                // El refresco real se hace después de responder (ver Handle)
                refreshRequested = true;
                return Json(true, "Unity está importando los archivos nuevos.");

            default:
                return Json(false, "Comando desconocido: " + command);
        }
    }

    static string ProjectInfo()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Unity: " + Application.unityVersion);

        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline == null)
            sb.AppendLine("Pipeline de render: Built-in (Standard)");
        else
            sb.AppendLine("Pipeline de render: " + pipeline.GetType().Name + " (" + pipeline.name + ")");

        sb.AppendLine("Espacio de color: " + PlayerSettings.colorSpace);
        sb.AppendLine("Assets: " + Application.dataPath);
        return sb.ToString();
    }
}