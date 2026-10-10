using System;
using System.Collections.Concurrent;
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

    static HttpListener listener;
    static Thread thread;
    static readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

    [Serializable]
    class Request
    {
        public string command;
        public string name;
        public float x;
        public float y;
        public float z;
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

            default:
                return Json(false, "Comando desconocido: " + req.command);
        }
    }

    static string Json(bool ok, string message)
    {
        return JsonUtility.ToJson(new Response { ok = ok, message = message });
    }
}