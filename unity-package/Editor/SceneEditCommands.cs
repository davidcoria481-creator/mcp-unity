using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Herramientas que MODIFICAN la escena activa.
// Todo queda registrado en el historial de Unity: Ctrl + Z lo deshace.
public static partial class BridgeServer
{
    [Serializable]
    class EditRequest
    {
        public string name;
        public string path;
        public string parent;
        public string filter;
        public string[] names;
        public string prefab;
        public bool force;
        public bool setPosition;
        public bool setRotation;
        public bool setScale;
        public float x;
        public float y;
        public float z;
        public float rotY;
        public float scale;
    }

    static string ExecuteEdit(string command, string body)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return Json(false, "Sal del modo Play antes de modificar la escena.");

        var req = JsonUtility.FromJson<EditRequest>(body);

        switch (command)
        {
            case "create_group":
                return CreateGroup(req);

            case "reparent":
                return Reparent(req);

            case "rename_object":
                return RenameObject(req);

            case "set_transform":
                return SetTransform(req);

            case "delete_object":
                return DeleteObject(req);

            case "instantiate_prefab":
                return InstantiateAsset(req);

            case "save_scene":
                return SaveActiveScene();

            default:
                return Json(false, "Comando desconocido: " + command);
        }
    }

    static string CreateGroup(EditRequest req)
    {
        if (string.IsNullOrEmpty(req.name))
            return Json(false, "Falta el nombre del grupo.");

        Scene scene = SceneManager.GetActiveScene();

        Transform parent = null;
        if (!string.IsNullOrEmpty(req.parent))
        {
            parent = FindByPath(scene, req.parent);
            if (parent == null)
                return Json(false, "No existe el grupo padre: " + req.parent);
        }

        string fullPath = parent == null ? req.name : req.parent + "/" + req.name;
        if (FindByPath(scene, fullPath) != null)
            return Json(true, "El grupo ya existía: " + fullPath);

        var go = new GameObject(req.name);
        Undo.RegisterCreatedObjectUndo(go, "MCP crear grupo " + req.name);
        if (parent != null)
            Undo.SetTransformParent(go.transform, parent, "MCP crear grupo " + req.name);

        go.transform.position = new Vector3(req.x, req.y, req.z);
        EditorSceneManager.MarkSceneDirty(scene);

        return Json(true, "Grupo creado: " + fullPath + " en " + Vec(go.transform.position));
    }

    static string Reparent(EditRequest req)
    {
        Scene scene = SceneManager.GetActiveScene();

        if (string.IsNullOrEmpty(req.parent))
            return Json(false, "Falta el grupo destino (parent).");

        Transform target = FindByPath(scene, req.parent);
        if (target == null)
            return Json(false, "No existe el grupo destino: " + req.parent);

        bool hasNames = req.names != null && req.names.Length > 0;
        bool hasFilter = !string.IsNullOrEmpty(req.filter);
        if (!hasNames && !hasFilter)
            return Json(false, "Indica names o filter para elegir qué mover.");

        // De dónde se toman los objetos: raíz de la escena o hijos de un objeto
        var sources = new List<Transform>();
        if (!string.IsNullOrEmpty(req.path))
        {
            Transform src = FindByPath(scene, req.path);
            if (src == null)
                return Json(false, "No existe el origen: " + req.path);
            foreach (Transform c in src) sources.Add(c);
        }
        else
        {
            foreach (var root in scene.GetRootGameObjects()) sources.Add(root.transform);
        }

        string filter = hasFilter ? req.filter.ToLowerInvariant() : null;
        var toMove = new List<Transform>();
        foreach (var t in sources)
        {
            if (target.IsChildOf(t)) continue;   // no mover el destino ni sus ancestros
            if (t.parent == target) continue;    // ya está dentro

            bool match = hasNames
                ? Array.IndexOf(req.names, t.name) >= 0
                : t.name.ToLowerInvariant().Contains(filter);

            if (match) toMove.Add(t);
        }

        if (toMove.Count == 0)
            return Json(false, "Ningún objeto coincide.");

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        foreach (var t in toMove)
            Undo.SetTransformParent(t, target, "MCP agrupar");
        Undo.CollapseUndoOperations(undoGroup);

        EditorSceneManager.MarkSceneDirty(scene);

        var sb = new StringBuilder();
        sb.Append("Movidos " + toMove.Count + " objetos a " + req.parent + ": ");
        for (int i = 0; i < toMove.Count && i < 30; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(toMove[i].name);
        }
        if (toMove.Count > 30)
            sb.Append(" ... y " + (toMove.Count - 30) + " más");
        sb.Append(". Ctrl+Z los devuelve de un solo paso.");

        return Json(true, sb.ToString());
    }

    static string RenameObject(EditRequest req)
    {
        Scene scene = SceneManager.GetActiveScene();

        if (string.IsNullOrEmpty(req.path))
            return Json(false, "Falta la ruta del objeto (path).");
        if (string.IsNullOrEmpty(req.name))
            return Json(false, "Falta el nombre nuevo (name).");

        Transform t = FindByPath(scene, req.path);
        if (t == null)
            return Json(false, "No encontré el objeto: " + req.path);

        string oldName = t.name;
        Undo.RecordObject(t.gameObject, "MCP renombrar " + oldName);
        t.name = req.name;
        EditorSceneManager.MarkSceneDirty(scene);

        return Json(true, "Renombrado: " + oldName + " -> " + req.name);
    }

    static string SetTransform(EditRequest req)
    {
        Scene scene = SceneManager.GetActiveScene();

        if (string.IsNullOrEmpty(req.path))
            return Json(false, "Falta la ruta del objeto (path).");

        Transform t = FindByPath(scene, req.path);
        if (t == null)
            return Json(false, "No encontré el objeto: " + req.path);

        if (!req.setPosition && !req.setRotation && !req.setScale)
            return Json(false, "No se pidió ningún cambio.");

        Undo.RecordObject(t, "MCP transformar " + t.name);
        ApplyTransform(t, req);
        EditorSceneManager.MarkSceneDirty(scene);

        return Json(true, "Listo: " + Describe(t));
    }

    // Posición en el mundo, giro solo en el eje Y y escala uniforme
    static void ApplyTransform(Transform t, EditRequest req)
    {
        if (req.setPosition)
            t.position = new Vector3(req.x, req.y, req.z);

        if (req.setRotation)
        {
            Vector3 e = t.eulerAngles;
            t.rotation = Quaternion.Euler(e.x, req.rotY, e.z);
        }

        if (req.setScale)
            t.localScale = Vector3.one * req.scale;
    }

    static string DeleteObject(EditRequest req)
    {
        Scene scene = SceneManager.GetActiveScene();

        if (string.IsNullOrEmpty(req.path))
            return Json(false, "Falta la ruta del objeto (path).");

        Transform t = FindByPath(scene, req.path);
        if (t == null)
            return Json(false, "No encontré el objeto: " + req.path);

        int descendants = t.GetComponentsInChildren<Transform>(true).Length - 1;
        if (descendants > 10 && !req.force)
            return Json(false, req.path + " tiene " + descendants +
                " objetos adentro. Si de verdad quieres borrarlo, repite con force=true.");

        string deletedName = t.name;
        Undo.DestroyObjectImmediate(t.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);

        return Json(true, "Borrado " + deletedName + " (" + descendants +
            " hijos). Se puede deshacer con Ctrl+Z.");
    }

    static string InstantiateAsset(EditRequest req)
    {
        Scene scene = SceneManager.GetActiveScene();

        if (string.IsNullOrEmpty(req.prefab) || !req.prefab.StartsWith("Assets/", StringComparison.Ordinal))
            return Json(false, "La ruta del prefab debe empezar con Assets/");

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(req.prefab);
        if (asset == null)
            return Json(false, "No encontré el prefab o modelo: " + req.prefab);

        Transform parent = null;
        if (!string.IsNullOrEmpty(req.parent))
        {
            parent = FindByPath(scene, req.parent);
            if (parent == null)
                return Json(false, "No existe el grupo padre: " + req.parent);
        }

        var go = PrefabUtility.InstantiatePrefab(asset, scene) as GameObject;
        if (go == null)
            return Json(false, "No se pudo instanciar " + req.prefab);

        Undo.RegisterCreatedObjectUndo(go, "MCP instanciar " + asset.name);
        if (!string.IsNullOrEmpty(req.name))
            go.name = req.name;
        if (parent != null)
            Undo.SetTransformParent(go.transform, parent, "MCP instanciar " + asset.name);

        ApplyTransform(go.transform, req);
        EditorSceneManager.MarkSceneDirty(scene);

        return Json(true, "Creado: " + Describe(go.transform));
    }

    static string SaveActiveScene()
    {
        if (EditorApplication.isPlaying)
            return Json(false, "Sal del modo Play antes de guardar.");

        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
            return Json(false, "La escena no tiene archivo todavía; guárdala una vez a mano con File > Save As.");

        bool saved = EditorSceneManager.SaveScene(scene);
        return saved
            ? Json(true, "Escena guardada: " + scene.path)
            : Json(false, "No se pudo guardar la escena.");
    }
}