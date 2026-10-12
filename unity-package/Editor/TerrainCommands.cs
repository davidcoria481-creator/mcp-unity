using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Crea el terreno de una isla: suelo, colinas, montañas, playa, mar y texturas.
public static partial class BridgeServer
{
    [Serializable]
    class TerrainRequest
    {
        public string name;
        public string parent;
        public float centerX;
        public float centerZ;
        public float size;
        public float islandRadius;
        public float mountainHeight;
        public float mountainAngle;
        public float hillHeight;
        public float waterLevel;
        public float seaDepth;
        public bool createWater;
        public int resolution;
        public int seed;
    }

    const string TerrainFolder = "Assets/Terreno";

    [Command("create_island_terrain")]
    static string CreateIslandTerrain(string body)
    {
        var req = JsonUtility.FromJson<TerrainRequest>(body);

        string baseName = string.IsNullOrEmpty(req.name) ? "Isla" : req.name;
        foreach (char c in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(c, '_');
        string goName = baseName + "_Terrain";

        foreach (var existing in Terrain.activeTerrains)
        {
            if (existing.name == goName)
                return Json(false, "Ya existe " + goName + ". Bórralo primero con borrar_objeto o usa otro nombre.");
        }

        float size = req.size > 100f ? req.size : 1400f;
        float islandR = req.islandRadius > 50f ? req.islandRadius : 560f;
        float mountainH = Mathf.Max(0f, req.mountainHeight);
        float hillH = Mathf.Max(0f, req.hillHeight);
        float seaDepth = req.seaDepth > 1f ? req.seaDepth : 30f;
        float waterLevel = req.waterLevel;
        if (waterLevel >= 0f)
            return Json(false, "El nivel del agua debe ser menor que 0 (la altura de la ciudad).");

        int res = req.resolution >= 400 ? 513 : 257;
        int seed = req.seed != 0 ? req.seed : UnityEngine.Random.Range(1, 99999);
        float seedOffset = 1000f + (seed % 1000) * 3.17f;

        float sizeY = mountainH + hillH + seaDepth + 20f;
        float baseY = -seaDepth;
        float originX = req.centerX - size * 0.5f;
        float originZ = req.centerZ - size * 0.5f;

        Scene scene = SceneManager.GetActiveScene();

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();

        EnsureAssetFolder("Assets", "Terreno");
        EnsureAssetFolder(TerrainFolder, "Texturas");

        // 1) Alturas
        var data = new TerrainData();
        data.heightmapResolution = res;
        data.size = new Vector3(size, sizeY, size);

        var heights = new float[res, res];
        for (int zi = 0; zi < res; zi++)
        {
            float wz = originZ + size * zi / (res - 1);
            for (int xi = 0; xi < res; xi++)
            {
                float wx = originX + size * xi / (res - 1);
                float h = IslandHeight(wx, wz, req.centerX, req.centerZ, islandR, mountainH,
                    req.mountainAngle, hillH, waterLevel, seaDepth, seedOffset);
                heights[zi, xi] = Mathf.Clamp01((h - baseY) / sizeY);
            }
        }
        data.SetHeights(0, 0, heights);

        // 2) Texturas y capas
        var sand = MakeLayer(baseName + "_Arena", new Color(0.86f, 0.78f, 0.58f), 0.06f, seed + 1);
        var grass = MakeLayer(baseName + "_Pasto", new Color(0.30f, 0.55f, 0.20f), 0.08f, seed + 2);
        var rock = MakeLayer(baseName + "_Roca", new Color(0.46f, 0.43f, 0.40f), 0.10f, seed + 3);

        string dataPath = TerrainFolder + "/" + baseName + "_TerrainData.asset";
        if (AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath) != null)
            AssetDatabase.DeleteAsset(dataPath);
        AssetDatabase.CreateAsset(data, dataPath);

        data.terrainLayers = new[] { sand, grass, rock };

        // 3) Pintar: arena en la costa, roca en lo empinado y alto, pasto en el resto
        int aRes = res == 513 ? 512 : 256;
        data.alphamapResolution = aRes;
        var alpha = new float[aRes, aRes, 3];
        for (int zi = 0; zi < aRes; zi++)
        {
            float ny = (float)zi / (aRes - 1);
            for (int xi = 0; xi < aRes; xi++)
            {
                float nx = (float)xi / (aRes - 1);
                float hWorld = baseY + data.GetInterpolatedHeight(nx, ny);
                float steep = data.GetSteepness(nx, ny);

                float rockW = Mathf.Max(Smooth(steep, 24f, 38f), Smooth(hWorld, 70f, 100f));
                float sandW = (1f - Smooth(hWorld, waterLevel - 0.2f, waterLevel + 0.9f)) * (1f - rockW);
                float grassW = Mathf.Max(0f, 1f - rockW - sandW);

                alpha[zi, xi, 0] = sandW;
                alpha[zi, xi, 1] = grassW;
                alpha[zi, xi, 2] = rockW;
            }
        }
        data.SetAlphamaps(0, 0, alpha);

        // 4) Objeto de terreno
        GameObject terrainGo = Terrain.CreateTerrainGameObject(data);
        terrainGo.name = goName;
        Undo.RegisterCreatedObjectUndo(terrainGo, "MCP crear terreno");

        Transform parent = EnsureGroup(scene, string.IsNullOrEmpty(req.parent) ? "Terreno" : req.parent);
        terrainGo.transform.SetParent(parent, true);
        terrainGo.transform.position = new Vector3(originX, baseY, originZ);

        var terrain = terrainGo.GetComponent<Terrain>();
        var pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null && pipeline.defaultTerrainMaterial != null)
            terrain.materialTemplate = pipeline.defaultTerrainMaterial;
        terrain.heightmapPixelError = 10f;

        // 5) Agua
        if (req.createWater)
        {
            var water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = baseName + "_Agua";
            UnityEngine.Object.DestroyImmediate(water.GetComponent<Collider>());
            Undo.RegisterCreatedObjectUndo(water, "MCP crear agua");
            water.transform.SetParent(parent, true);
            water.transform.position = new Vector3(req.centerX, waterLevel, req.centerZ);
            float ws = size * 3f / 10f;
            water.transform.localScale = new Vector3(ws, 1f, ws);

            var rend = water.GetComponent<MeshRenderer>();
            rend.shadowCastingMode = ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.sharedMaterial = MakeWaterMaterial(baseName);
        }

        AssetDatabase.SaveAssets();
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);

        return Json(true, "Terreno " + goName + " creado: " + size + " x " + size + " m (resolución " + res +
            "), centro (" + req.centerX + ", " + req.centerZ + "), isla de radio ~" + islandR +
            " con costa irregular, ciudad plana a altura 0, montaña de hasta ~" + mountainH +
            " m hacia el ángulo " + req.mountainAngle + ", agua a y=" + waterLevel +
            ". Semilla " + seed + ". Archivos en " + TerrainFolder +
            ". Ctrl+Z quita los objetos pero no borra esos archivos.");
    }

    static void EnsureAssetFolder(string parentFolder, string name)
    {
        if (!AssetDatabase.IsValidFolder(parentFolder + "/" + name))
            AssetDatabase.CreateFolder(parentFolder, name);
    }

    static float Smooth(float v, float e0, float e1)
    {
        float t = Mathf.Clamp01((v - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    // Ruido suave entre 0 y 1
    static float Fbm(float x, float z, int octaves, float seedOffset)
    {
        float sum = 0f;
        float amp = 1f;
        float freq = 1f;
        float norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Mathf.PerlinNoise(x * freq + seedOffset, z * freq + seedOffset * 0.7f);
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }

    // Altura del mundo (en metros) en un punto
    static float IslandHeight(float wx, float wz, float cx, float cz, float islandR,
        float mountainH, float mountainAngleDeg, float hillH, float waterLevel,
        float seaDepth, float seedOffset)
    {
        float dx = wx - cx;
        float dz = wz - cz;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        float angle = Mathf.Atan2(dz, dx);

        // Costa irregular
        float coast = Fbm(Mathf.Cos(angle) * 1.4f + 10f, Mathf.Sin(angle) * 1.4f + 10f, 3, seedOffset);
        float shoreR = islandR * (0.85f + 0.3f * coast);
        float beachStart = shoreR - 110f;

        float h;
        if (d < beachStart)
            h = 0f;
        else if (d < shoreR)
            h = Mathf.Lerp(0f, waterLevel - 0.3f, Smooth(d, beachStart, shoreR));
        else
            h = Mathf.Lerp(waterLevel - 0.3f, -seaDepth, Smooth(d, shoreR, shoreR + 220f));

        float landFactor = 1f - Smooth(d, beachStart, shoreR);
        float coreFree = Smooth(d, 140f, 260f);   // la zona de la ciudad queda plana

        // Colinas suaves
        float hills = Mathf.Clamp01((Fbm(wx / 160f, wz / 160f, 4, seedOffset + 50f) - 0.35f) * 1.6f);
        h += hills * hillH * coreFree * landFactor;

        // Montaña
        if (mountainH > 0f)
        {
            float a = mountainAngleDeg * Mathf.Deg2Rad;
            float mcx = cx + Mathf.Cos(a) * islandR * 0.7f;
            float mcz = cz + Mathf.Sin(a) * islandR * 0.7f;
            float mr = islandR * 0.3f;
            float mdx = wx - mcx;
            float mdz = wz - mcz;
            float md = Mathf.Sqrt(mdx * mdx + mdz * mdz);

            float m = 1f - Smooth(md, 0f, mr);
            float rough = Mathf.Clamp(0.75f + 1.1f * (Fbm(wx / 90f, wz / 90f, 5, seedOffset + 120f) - 0.5f), 0.45f, 1f);
            h += mountainH * Mathf.Pow(m, 1.4f) * rough * coreFree * landFactor;
        }

        return h;
    }

    static TerrainLayer MakeLayer(string name, Color baseColor, float variation, int seed)
    {
        string texPath = TerrainFolder + "/Texturas/" + name + ".png";
        string fullPath = Path.Combine(Path.GetDirectoryName(Application.dataPath), texPath);

        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGB24, false);
        var rng = new System.Random(seed);
        float off = 100f + (seed % 100);

        for (int y = 0; y < n; y++)
        {
            for (int x = 0; x < n; x++)
            {
                float coarse = Mathf.PerlinNoise(off + x * 0.05f, off + y * 0.05f) - 0.5f;
                float fine = (float)rng.NextDouble() - 0.5f;
                float v = coarse * variation * 2f + fine * variation * 0.6f;
                tex.SetPixel(x, y, new Color(
                    Mathf.Clamp01(baseColor.r + v),
                    Mathf.Clamp01(baseColor.g + v),
                    Mathf.Clamp01(baseColor.b + v)));
            }
        }
        tex.Apply();
        File.WriteAllBytes(fullPath, tex.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceSynchronousImport);
        var imported = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);

        string layerPath = TerrainFolder + "/" + name + ".terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        bool isNew = layer == null;
        if (isNew) layer = new TerrainLayer();

        layer.diffuseTexture = imported;
        layer.tileSize = new Vector2(25f, 25f);

        if (isNew) AssetDatabase.CreateAsset(layer, layerPath);
        else EditorUtility.SetDirty(layer);

        return layer;
    }

    static Material MakeWaterMaterial(string baseName)
    {
        string path = TerrainFolder + "/" + baseName + "_Agua.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        bool urp = shader != null;
        if (!urp) shader = Shader.Find("Standard");

        var mat = new Material(shader);
        var color = new Color(0.10f, 0.45f, 0.62f, 1f);
        if (urp)
        {
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", 0.9f);
        }
        else
        {
            mat.color = color;
            mat.SetFloat("_Glossiness", 0.9f);
        }

        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}