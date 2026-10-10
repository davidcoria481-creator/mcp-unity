using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BridgeServer
{
    const string Prefix = "http://127.0.0.1:8090/";
    const int MaxLogs = 500;

    static HttpListener listener;
    static Thread thread;
    static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();
    static readonly List<string> logs = new List<string>();
    static readonly object logLock = new object();
    static volatile bool refreshRequested;

    [Serializable]
    class Request
    {
        public string command;
        public string name;
        public float x;
        public float y;
        public float z;
        public string path;
        public string content;
        public bool overwrite;
        public int count;
    }

    [Serializable]
    class Response
    {
        public bool ok;
        public string message;
    }

    static BridgeServer()
    {
        Start();
        EditorApplication.update += Update;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.quitting += Stop;
        Application.logMessageReceivedThreaded += OnLog;
    }

    static void OnLog(string condition, string stackTrace, LogType type)
    {
        lock (logLock)
        {
            logs.Add("[" + type + "] " + condition);
            if (logs.Count > MaxLogs)
                logs.RemoveRange(0, logs.Count - MaxLogs);
        }
    }

    static void Start()
    {
        if (listener != null) return;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add(Prefix);
            listener.Start();
            thread = new Thread(Loop) { IsBackground = true };
            thread.Start();
            Debug.Log("[MCP Bridge] Escuchando en " + Prefix);
        }
        catch (Exception e)
        {
            Debug.LogError("[MCP Bridge] No se pudo iniciar: " + e.Message);
            listener = null;
        }
    }

    static void Stop()
    {
        try
        {
            if (listener != null)
            {
                listener.Stop();
                listener.Close();
            }
        }
        catch (Exception) { }
        listener = null;
    }

    static void Loop()
    {
        while (listener != null && listener.IsListening)
        {
            try
            {
                var ctx = listener.GetContext();
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    static void Handle(HttpListenerContext ctx)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();

            string result = null;
            var done = new ManualResetEventSlim(false);

            // Unity solo permite tocar sus objetos desde el hilo principal,
            // así que dejamos la orden en una cola y Update() la ejecuta.
            queue.Enqueue(() =>
            {
                try { result = Execute(body); }
                catch (Exception e) { result = Json(false, e.Message); }
                finally { done.Set(); }
            });

            if (!done.Wait(10000))
                result = Json(false, "Unity no respondió a tiempo");

            byte[] bytes = Encoding.UTF8.GetBytes(result);
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.Close();

            // Refrescamos los assets DESPUÉS de responder, porque al
            // recompilar Unity reinicia este servidor.
            if (refreshRequested)
            {
                refreshRequested = false;
                queue.Enqueue(() => AssetDatabase.Refresh());
            }
        }
        catch (Exception) { }
    }

    static void Update()
    {
        while (queue.TryDequeue(out var action))
            action();
    }

    static string Execute(string body)
    {
        var req = JsonUtility.FromJson<Request>(body);

        switch (req.command)
        {
            case "ping":
                return Json(true, "pong desde Unity " + Application.unityVersion);

            case "create_gameobject":
                var go = new GameObject(req.name);
                go.transform.position = new Vector3(req.x, req.y, req.z);
                Undo.RegisterCreatedObjectUndo(go, "MCP crear " + req.name);
                Selection.activeGameObject = go;
                EditorSceneManager.MarkSceneDirty(go.scene);
                return Json(true, "Creado el objeto " + req.name);

            case "read_console":
                return Json(true, ReadConsole(req.count));

            case "read_script":
                return ReadScript(req.path);

            case "create_script":
                return CreateScript(req.path, req.content, req.overwrite);

            default:
                return Json(false, "Comando desconocido: " + req.command);
        }
    }

    static string ReadConsole(int count)
    {
        if (count <= 0) count = 30;
        lock (logLock)
        {
            if (logs.Count == 0)
                return "(no hay mensajes capturados desde la última recompilación)";
            int start = Math.Max(0, logs.Count - count);
            return string.Join("\n", logs.GetRange(start, logs.Count - start).ToArray());
        }
    }

    // Convierte una ruta tipo "Scripts/Player.cs" en una ruta completa,
    // verificando que quede dentro de Assets y termine en .cs
    static string ResolveScriptPath(string relative, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(relative))
        {
            error = "Falta la ruta (path).";
            return null;
        }

        relative = relative.Replace('\\', '/').TrimStart('/');
        if (relative.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            relative = relative.Substring("Assets/".Length);

        if (!relative.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            error = "La ruta debe terminar en .cs";
            return null;
        }

        string assetsFull = Path.GetFullPath(Application.dataPath);
        string full = Path.GetFullPath(Path.Combine(assetsFull, relative));

        if (!full.StartsWith(assetsFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            error = "La ruta debe quedar dentro de la carpeta Assets.";
            return null;
        }
        return full;
    }

    static string ReadScript(string path)
    {
        string error;
        string full = ResolveScriptPath(path, out error);
        if (full == null) return Json(false, error);
        if (!File.Exists(full)) return Json(false, "No existe: " + path);
        return Json(true, File.ReadAllText(full));
    }

    static string CreateScript(string path, string content, bool overwrite)
    {
        if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
            return Json(false, "Sal del modo Play antes de crear o modificar scripts.");

        string error;
        string full = ResolveScriptPath(path, out error);
        if (full == null) return Json(false, error);

        if (Path.GetFileName(full).Equals("BridgeServer.cs", StringComparison.OrdinalIgnoreCase))
            return Json(false, "No se permite modificar BridgeServer.cs.");

        if (content == null)
            return Json(false, "Falta el contenido (content).");

        bool existed = File.Exists(full);
        if (existed && !overwrite)
            return Json(false, "Ya existe " + path + ". Usa overwrite=true para reemplazarlo.");

        if (existed)
        {
            // Copia de seguridad fuera de Assets antes de reemplazar
            string backupDir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "McpBackups");
            Directory.CreateDirectory(backupDir);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            File.Copy(full, Path.Combine(backupDir, stamp + "_" + Path.GetFileName(full) + ".bak"));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, content, new UTF8Encoding(false));
        refreshRequested = true;

        return Json(true, (existed ? "Reemplazado " : "Creado ") + path +
            ". Unity va a recompilar; revisa la consola en unos segundos.");
    }

    static string Json(bool ok, string message)
    {
        return JsonUtility.ToJson(new Response { ok = ok, message = message });
    }
}