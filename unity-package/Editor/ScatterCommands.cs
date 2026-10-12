using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Pone muchos objetos de una sola vez dentro de una zona.
public static partial class BridgeServer
{
    [Serializable]
    class ScatterRequest
    {
        public string[] prefabs;
        public string parent;
        public string shape;
        public float centerX;
        public float centerZ;
        public float rMin;
        public float rMax;
        public float sizeX;
        public float sizeZ;
        public int count;
        public float minDistance;
        public float margin;
        public float scaleMin;
        public float scaleMax;
        public bool randomRotation;
        public float yBase;
        public bool snapToGround;
        public bool avoidObjects;
        public int seed;
    }

    [Command("scatter_prefabs")]
    static string ScatterPrefabs(string body)
    {
        var req = JsonUtility.FromJson<ScatterRequest>(body);

        if (req.prefabs == null || req.prefabs.Length == 0)
            return Json(false, "Indica al menos un prefab.");

        int count = Mathf.Clamp(req.count, 1, 200);

        var assets = new List<GameObject>();
        foreach (var path in req.prefabs)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
                return Json(false, "No encontré el prefab: " + path);
            assets.Add(asset);
        }

        bool ring = req.shape != "rectangulo";
        if (ring && req.rMax <= req.rMin)
            return Json(false, "radio_max debe ser mayor que radio_min.");
        if (!ring && (req.sizeX <= 0f || req.sizeZ <= 0f))
            return Json(false, "Indica el ancho y el largo de la zona.");

        Scene scene = SceneManager.GetActiveScene();

        // Todo lo que sigue queda en un solo paso de deshacer
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        Transform parent = null;
        if (!string.IsNullOrEmpty(req.parent))
            parent = EnsureGroup(scene, req.parent);

        int seed = req.seed != 0 ? req.seed : Environment.TickCount;
        var rng = new System.Random(seed);

        float scaleMin = req.scaleMin > 0f ? req.scaleMin : 1f;
        float scaleMax = req.scaleMax >= scaleMin ? req.scaleMax : scaleMin;
        float minDist = Mathf.Max(0f, req.minDistance);
        float margin = Mathf.Max(0f, req.margin);

        // Objetos altos que ya existen (lo plano, como calles y veredas, no cuenta)
        var obstacles = new List<Bounds>();
        if (req.avoidObjects)
        {
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                Bounds b = r.bounds;
                if (b.size.y >= 0.8f)
                    obstacles.Add(b);
            }
        }

        Physics.SyncTransforms();

        var placed = new List<Vector2>();
        var perPrefab = new Dictionary<string, int>();
        int created = 0;
        int attempts = 0;
        int maxAttempts = count * 40;

        while (created < count && attempts < maxAttempts)
        {
            attempts++;

            float x;
            float z;
            if (ring)
            {
                float u = (float)rng.NextDouble();
                float radius = Mathf.Sqrt(Mathf.Lerp(req.rMin * req.rMin, req.rMax * req.rMax, u));
                float angle = (float)(rng.NextDouble() * Math.PI * 2.0);
                x = req.centerX + radius * Mathf.Cos(angle);
                z = req.centerZ + radius * Mathf.Sin(angle);
            }
            else
            {
                x = req.centerX + ((float)rng.NextDouble() - 0.5f) * req.sizeX;
                z = req.centerZ + ((float)rng.NextDouble() - 0.5f) * req.sizeZ;
            }

            if (TooClose(placed, x, z, minDist)) continue;
            if (InsideObstacle(obstacles, x, z, margin)) continue;

            float y = req.snapToGround ? GroundHeight(x, z, req.yBase) : req.yBase;

            var source = assets[rng.Next(assets.Count)];
            var go = PrefabUtility.InstantiatePrefab(source, scene) as GameObject;
            if (go == null) continue;

            Undo.RegisterCreatedObjectUndo(go, "MCP esparcir " + source.name);
            if (parent != null)
                go.transform.SetParent(parent, true);

            go.transform.position = new Vector3(x, y, z);

            if (req.randomRotation)
            {
                float turn = (float)(rng.NextDouble() * 360.0);
                go.transform.rotation = Quaternion.Euler(0f, turn, 0f) * go.transform.rotation;
            }

            float s = Mathf.Lerp(scaleMin, scaleMax, (float)rng.NextDouble());
            go.transform.localScale = go.transform.localScale * s;
            go.name = source.name + "_" + created;

            placed.Add(new Vector2(x, z));

            if (req.avoidObjects)
            {
                Bounds nb;
                if (TryGetBounds(go, out nb) && nb.size.y >= 0.8f)
                    obstacles.Add(nb);
            }

            int n;
            perPrefab.TryGetValue(source.name, out n);
            perPrefab[source.name] = n + 1;
            created++;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);

        var sb = new StringBuilder();
        sb.Append("Colocados " + created + " de " + count + " (semilla " + seed + ")");
        if (parent != null)
            sb.Append(" en " + req.parent);
        sb.Append(". ");

        bool first = true;
        foreach (var pair in perPrefab)
        {
            sb.Append(first ? "Por prefab: " : ", ");
            sb.Append(pair.Key + " " + pair.Value);
            first = false;
        }

        if (created < count)
            sb.Append(". No se pudo colocar el resto: la zona está llena o hay demasiados obstáculos. " +
                      "Reduce la distancia mínima o amplía la zona");

        sb.Append(". Un Ctrl+Z deshace todo.");
        return Json(true, sb.ToString());
    }

    static bool TooClose(List<Vector2> placed, float x, float z, float minDist)
    {
        if (minDist <= 0f) return false;
        float sq = minDist * minDist;
        foreach (var p in placed)
        {
            float dx = p.x - x;
            float dz = p.y - z;
            if (dx * dx + dz * dz < sq) return true;
        }
        return false;
    }

    static bool InsideObstacle(List<Bounds> obstacles, float x, float z, float margin)
    {
        foreach (var b in obstacles)
        {
            if (x > b.min.x - margin && x < b.max.x + margin &&
                z > b.min.z - margin && z < b.max.z + margin)
                return true;
        }
        return false;
    }

    // Altura del punto más bajo con collider; si no hay ninguno, usa 'fallback'
    static float GroundHeight(float x, float z, float fallback)
    {
        var hits = Physics.RaycastAll(new Vector3(x, 2000f, z), Vector3.down, 4000f);
        if (hits.Length == 0) return fallback;

        float lowest = hits[0].point.y;
        foreach (var h in hits)
        {
            if (h.point.y < lowest) lowest = h.point.y;
        }
        return lowest;
    }

    // Devuelve el grupo de esa ruta, creando los que falten
    static Transform EnsureGroup(Scene scene, string path)
    {
        string[] parts = path.Split('/');
        Transform current = null;
        string built = "";

        foreach (var part in parts)
        {
            built = built.Length == 0 ? part : built + "/" + part;
            Transform next = FindByPath(scene, built);

            if (next == null)
            {
                var go = new GameObject(part);
                Undo.RegisterCreatedObjectUndo(go, "MCP crear grupo " + part);
                if (current != null)
                    go.transform.SetParent(current, false);
                next = go.transform;
            }
            current = next;
        }
        return current;
    }
}