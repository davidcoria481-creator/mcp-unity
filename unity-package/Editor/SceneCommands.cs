using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Herramientas de solo lectura para que Claude entienda la escena y los assets.
public static partial class BridgeServer
{
    [Serializable]
    class SceneRequest
    {
        public string filter;
        public string folder;
        public string path;
        public int maxResults;
    }

    static string ExecuteExtra(string command, string body)
    {
        var req = JsonUtility.FromJson<SceneRequest>(body);

        switch (command)
        {
            case "list_hierarchy":
                return Json(true, ListHierarchy(req));

            case "list_prefabs":
                return Json(true, ListPrefabs(req));

            default:
                return ExecuteEdit(command, body);
        }
    }

    // Números con punto decimal, sin importar el idioma de Windows
    static string Num(float v)
    {
        return v.ToString("0.##", CultureInfo.InvariantCulture);
    }

    static string Vec(Vector3 v)
    {
        return Num(v.x) + "," + Num(v.y) + "," + Num(v.z);
    }

    static string ListHierarchy(SceneRequest req)
    {
        int max = req.maxResults > 0 ? req.maxResults : 100;
        Scene scene = SceneManager.GetActiveScene();
        var items = new List<Transform>();
        string title;

        if (!string.IsNullOrEmpty(req.path))
        {
            Transform parent = FindByPath(scene, req.path);
            if (parent == null) return "No encontré el objeto: " + req.path;
            foreach (Transform child in parent) items.Add(child);
            title = "Hijos de " + req.path;
        }
        else
        {
            foreach (var root in scene.GetRootGameObjects()) items.Add(root.transform);
            title = "Objetos raíz";
        }

        string filter = (req.filter ?? "").ToLowerInvariant();
        var sb = new StringBuilder();
        sb.AppendLine("Escena: " + scene.name + " | " + title + " (" + items.Count + " en total)");

        int shown = 0;
        int matched = 0;
        foreach (var t in items)
        {
            if (filter.Length > 0 && t.name.ToLowerInvariant().IndexOf(filter, StringComparison.Ordinal) < 0)
                continue;

            matched++;
            if (shown >= max) continue;

            sb.AppendLine(Describe(t));
            shown++;
        }

        if (matched > shown)
            sb.AppendLine("... faltan " + (matched - shown) + " más. Usa un filtro o sube el máximo.");

        return sb.ToString();
    }

    static string Describe(Transform t)
    {
        var go = t.gameObject;
        var sb = new StringBuilder();
        sb.Append(t.name);
        if (!go.activeSelf) sb.Append(" (desactivado)");
        sb.Append(" | pos " + Vec(t.position));
        sb.Append(" | rotY " + Num(t.eulerAngles.y));
        sb.Append(" | escala " + Vec(t.localScale));

        Bounds b;
        if (TryGetBounds(go, out b))
            sb.Append(" | tamaño " + Vec(b.size));

        if (t.childCount > 0)
            sb.Append(" | hijos " + t.childCount);

        var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
        if (source != null)
            sb.Append(" | prefab " + AssetDatabase.GetAssetPath(source));

        return sb.ToString();
    }

    static bool TryGetBounds(GameObject go, out Bounds bounds)
    {
        bounds = new Bounds();
        bool found = false;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return found;
    }

    // Busca un objeto por ruta, por ejemplo "Calles/road_001"
    static Transform FindByPath(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        Transform current = null;

        foreach (var root in scene.GetRootGameObjects())
        {
            if (root.name == parts[0])
            {
                current = root.transform;
                break;
            }
        }

        for (int i = 1; current != null && i < parts.Length; i++)
            current = current.Find(parts[i]);

        return current;
    }

    static string ListPrefabs(SceneRequest req)
    {
        int max = req.maxResults > 0 ? req.maxResults : 60;
        string folder = string.IsNullOrEmpty(req.folder)
            ? "Assets"
            : req.folder.Replace('\\', '/').TrimEnd('/');

        if (!folder.StartsWith("Assets", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(folder))
            return "Carpeta no válida: " + folder + " (debe estar dentro de Assets)";

        var found = new HashSet<string>();
        foreach (var query in new[] { "t:Prefab", "t:Model" })
        {
            foreach (var guid in AssetDatabase.FindAssets(query, new[] { folder }))
                found.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        string filter = (req.filter ?? "").ToLowerInvariant();
        var paths = new List<string>();
        foreach (var p in found)
        {
            if (filter.Length == 0 || p.ToLowerInvariant().Contains(filter))
                paths.Add(p);
        }
        paths.Sort(StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("Prefabs y modelos en " + folder + " (" + paths.Count + " coincidencias):");

        for (int i = 0; i < paths.Count && i < max; i++)
            sb.AppendLine(paths[i]);

        if (paths.Count > max)
            sb.AppendLine("... faltan " + (paths.Count - max) + " más. Usa un filtro o sube el máximo.");

        return sb.ToString();
    }
}