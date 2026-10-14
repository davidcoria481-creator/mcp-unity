using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Genera la isla de Ose World: carreteras, zonas con edificios, puerto, aeropuerto,
// playa, vegetacion y autos. Se ejecuta UNA vez al compilar (si no existe "Ciudad_Isla").
// Todo queda dentro del grupo "Ciudad_Isla" y se deshace con un solo Ctrl+Z.
// Menu para repetir: Ose > Regenerar isla.
[InitializeOnLoad]
public static class GenerarIslaOse
{
    const string Clave = "OseWorld_IslaGenerada_v3";
    const string K = "Assets/Importados/Kenney_CityKit_Commercial/Models/FBX format/";
    const string C = "Assets/ithappy/Cartoon_City_Free/Prefabs/";
    static readonly Vector2 Centro = new Vector2(520f, -12f);
    static readonly Vector2 Montana = new Vector2(520f, 400f);
    // x, z, radio de cada zona (industrial, residencial, puerto, aeropuerto)
    static readonly Vector3[] zonas = { new Vector3(250, 200, 90), new Vector3(800, 60, 100), new Vector3(470, -490, 40), new Vector3(790, -270, 110) };

    struct Mod { public string ruta; public float esc; public Mod(string r, float e) { ruta = r; esc = e; } }

    static Terrain terr;
    static System.Random rng;
    static List<Vector2[]> calles, callesAuto, pistas;
    static List<Vector2> ocupados, bloques;
    static int total;

    static GenerarIslaOse() { EditorApplication.delayCall += Auto; }

    static void Auto()
    {
        if (EditorPrefs.GetBool(Clave, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Auto; return; }
        if (GameObject.Find("Ciudad_Isla") != null) { EditorPrefs.SetBool(Clave, true); return; }
        Generar();
    }

    [MenuItem("Ose/Regenerar isla")]
    static void Regenerar()
    {
        var viejo = GameObject.Find("Ciudad_Isla");
        if (viejo != null) Undo.DestroyObjectImmediate(viejo);
        Generar();
    }

    static void Generar()
    {
        terr = Object.FindFirstObjectByType<Terrain>();
        if (terr == null) { Debug.LogWarning("[Isla] No hay Terrain en la escena; no se genero nada."); return; }
        rng = new System.Random(72135);
        calles = new List<Vector2[]>(); callesAuto = new List<Vector2[]>(); pistas = new List<Vector2[]>();
        ocupados = new List<Vector2>(); bloques = new List<Vector2>();
        total = 0;
        Undo.IncrementCurrentGroup();
        int grupo = Undo.GetCurrentGroup();
        try
        {
            var raiz = Nuevo("Ciudad_Isla", null);
            var tCalles = Nuevo("Carreteras", raiz);
            var tPuerto = Nuevo("Puerto", raiz);
            var tAero = Nuevo("Aeropuerto", raiz);
            var tInd = Nuevo("Zona_Industrial", raiz);
            var tRes = Nuevo("Barrio_Residencial", raiz);
            var tSky = Nuevo("Rascacielos", raiz);
            var tPlaya = Nuevo("Playa", raiz);
            var tVeg = Nuevo("Vegetacion_Isla", raiz);
            var tAutos = Nuevo("Vehiculos_Isla", raiz);

            // ---- Carreteras principales (spline suave, baldosas de 15 m) ----
            Tiles(new[] { V(488, 66), V(500, 130), V(515, 190), V(530, 235) }, 1f, tCalles, true, false, true, false);
            Tiles(new[] { V(441, 20), V(390, 75), V(320, 140), V(250, 200), V(195, 235) }, 1f, tCalles, true, false, true, false);
            Tiles(new[] { V(552, 66), V(640, 75), V(730, 75), V(800, 60), V(870, 40) }, 1f, tCalles, true, false, true, false);
            Tiles(new[] { V(552, -90), V(615, -145), V(676, -190) }, 1f, tCalles, true, false, true, false);
            Tiles(new[] { V(487, -90), V(495, -250), V(485, -400), V(473, -532) }, 1f, tCalles, true, false, true, false);
            Tiles(new[] { V(441, -44), V(300, -40), V(160, -40), V(90, -40) }, 1f, tCalles, true, false, true, false);
            Tiles(new[] { V(800, 60), V(840, -60), V(850, -130), V(832, -178) }, 1f, tCalles, true, false, true, false);

            // ---- Puerto: muelle de la costa y 3 muelles al mar ----
            Tiles(new[] { V(425, -532), V(535, -532) }, 1f, tPuerto, false, false, false, false);
            Tiles(new[] { V(450, -548), V(450, -630) }, 0.7f, tPuerto, false, true, false, false);
            Tiles(new[] { V(482, -548), V(482, -630) }, 0.7f, tPuerto, false, true, false, false);
            Tiles(new[] { V(514, -548), V(514, -630) }, 0.7f, tPuerto, false, true, false, false);

            // ---- Aeropuerto: pista de 30 m de ancho hacia el sureste (sin palmeras encima) ----
            Vector2 a = V(690, -200), b = V(860, -335);
            Tiles(new[] { a, b }, 2f, tAero, false, false, false, true);
            Vector2 u = (b - a).normalized, n = new Vector2(-u.y, u.x);
            Vector2 baseT = (a + b) / 2f + n * 65f;
            float rotT = Mathf.Atan2(-n.x, -n.y) * Mathf.Rad2Deg;
            Edificio(K + "low-detail-building-wide-b.fbx", baseT - u * 50f, rotT, 26f, tAero);
            Edificio(K + "low-detail-building-wide-a.fbx", baseT, rotT, 32f, tAero);
            Edificio(K + "low-detail-building-wide-b.fbx", baseT + u * 50f, rotT, 26f, tAero);
            Edificio(K + "building-skyscraper-d.fbx", baseT - u * 95f, rotT, 9f, tAero);

            // ---- Edificios por zona ----
            Zona(Centro, 160f, 200f, 12, Mods(K, "building-skyscraper-", new[] { "a", "b", "c", "d", "e" }, 18f), 40f, 24f, false, true, tSky);
            var ind = Mods(K, "low-detail-building-", new[] { "wide-a", "wide-b" }, 26f);
            Zona(V(250, 200), 0f, 90f, 10, ind, 40f, 26f, false, false, tInd);
            var res = new List<Mod>(Mods(K, "building-", new[] { "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n" }, 20f));
            res.Add(new Mod(C + "Buildings/Eco_Building_Terrace.prefab", 0.7f));
            Zona(V(800, 60), 0f, 100f, 16, res.ToArray(), 34f, 26f, false, false, tRes);
            var port = Mods(K, "low-detail-building-", new[] { "wide-a", "wide-b", "e", "f" }, 20f);
            Zona(V(470, -490), 0f, 40f, 4, port, 30f, 24f, true, false, tPuerto);

            // ---- Vegetacion, playa y autos ----
            PalmerasCalle(tVeg);
            Costa(tVeg, tPlaya);
            Arboledas(tVeg);
            Autos(tAutos);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorPrefs.SetBool(Clave, true);
            Debug.Log("[Isla] Generada: " + total + " objetos en 'Ciudad_Isla' (escena sin guardar).");
        }
        catch (System.Exception e) { Debug.LogError("[Isla] Error: " + e); }
        Undo.CollapseUndoOperations(grupo);
    }

    // ---------------- utilidades ----------------
    static Vector2 V(float x, float z) { return new Vector2(x, z); }

    static Transform Nuevo(string nombre, Transform padre)
    {
        var go = new GameObject(nombre);
        if (padre != null) go.transform.SetParent(padre, false);
        Undo.RegisterCreatedObjectUndo(go, "Isla");
        return go.transform;
    }

    static GameObject Cargar(string ruta)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
        if (p == null) Debug.LogWarning("[Isla] Falta " + ruta);
        return p;
    }

    static GameObject Colocar(GameObject prefab, Vector3 pos, Quaternion rot, float escala, Transform padre)
    {
        if (prefab == null) return null;
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, padre);
        go.transform.SetPositionAndRotation(pos, rot);
        go.transform.localScale = prefab.transform.localScale * escala;
        Undo.RegisterCreatedObjectUndo(go, "Isla");
        total++;
        return go;
    }

    static Mod[] Mods(string dir, string prefijo, string[] sufijos, float esc)
    {
        var r = new Mod[sufijos.Length];
        for (int i = 0; i < sufijos.Length; i++) r[i] = new Mod(dir + prefijo + sufijos[i] + ".fbx", esc);
        return r;
    }

    static float H(float x, float z) { return terr.SampleHeight(new Vector3(x, 0f, z)) + terr.transform.position.y; }
    static float H(Vector2 p) { return H(p.x, p.y); }

    static bool CercaAgua(Vector2 p, float d)
    {
        for (int i = 0; i < 8; i++)
        {
            float ang = i * Mathf.PI / 4f;
            if (H(p.x + Mathf.Cos(ang) * d, p.y + Mathf.Sin(ang) * d) < -1.7f) return true;
        }
        return false;
    }

    static Vector3 Normal(Vector2 p)
    {
        float hx = H(p.x + 6f, p.y) - H(p.x - 6f, p.y);
        float hz = H(p.x, p.y + 6f) - H(p.x, p.y - 6f);
        return new Vector3(-hx / 12f, 1f, -hz / 12f).normalized;
    }

    static Vector2 CalleCercana(Vector2 p, out float dist)
    {
        float m = float.MaxValue; Vector2 mejor = p;
        foreach (var c in calles)
            for (int i = 0; i < c.Length; i += 2)
            {
                float d = (c[i] - p).sqrMagnitude;
                if (d < m) { m = d; mejor = c[i]; }
            }
        dist = Mathf.Sqrt(m);
        return mejor;
    }

    static float DistCalles(Vector2 p) { float d; CalleCercana(p, out d); return d; }

    static float DistPistas(Vector2 p)
    {
        float m = float.MaxValue;
        foreach (var c in pistas)
            for (int i = 0; i < c.Length; i += 2)
            {
                float d = (c[i] - p).sqrMagnitude;
                if (d < m) m = d;
            }
        return Mathf.Sqrt(m);
    }

    static bool Cerca(List<Vector2> lista, Vector2 p, float d)
    {
        float d2 = d * d;
        for (int i = 0; i < lista.Count; i++) if ((lista[i] - p).sqrMagnitude < d2) return true;
        return false;
    }

    static bool EnZona(Vector2 p, float margen)
    {
        foreach (var z in zonas) if (Vector2.Distance(p, V(z.x, z.y)) < z.z + margen) return true;
        return Vector2.Distance(p, Montana) < 230f;
    }

    static float AlturaMin(Vector2 p, float r, out float rango)
    {
        float mn = H(p), mx = mn;
        Vector2[] o = { V(r, 0), V(-r, 0), V(0, r), V(0, -r) };
        foreach (var d in o) { float h = H(p + d); if (h < mn) mn = h; if (h > mx) mx = h; }
        rango = mx - mn;
        return mn;
    }

    // ---------------- carreteras ----------------
    static Vector2 CR(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t2 + (-a + 3f * b - 3f * c + d) * t3);
    }

    static List<Vector2> Spline(Vector2[] p)
    {
        var r = new List<Vector2>();
        for (int i = 0; i < p.Length - 1; i++)
        {
            Vector2 p0 = p[Mathf.Max(i - 1, 0)], p1 = p[i], p2 = p[i + 1], p3 = p[Mathf.Min(i + 2, p.Length - 1)];
            int n = Mathf.Max(2, (int)Vector2.Distance(p1, p2));
            for (int k = 0; k < n; k++) r.Add(CR(p0, p1, p2, p3, k / (float)n));
        }
        r.Add(p[p.Length - 1]);
        return r;
    }

    static List<Vector2> Densificar(Vector2[] p)
    {
        var r = new List<Vector2>();
        for (int i = 0; i < p.Length - 1; i++)
        {
            int n = Mathf.Max(1, (int)Vector2.Distance(p[i], p[i + 1]));
            for (int k = 0; k < n; k++) r.Add(Vector2.Lerp(p[i], p[i + 1], k / (float)n));
        }
        r.Add(p[p.Length - 1]);
        return r;
    }

    static void Tiles(Vector2[] ctrl, float escala, Transform padre, bool spline, bool cubierta, bool autos, bool pista)
    {
        var tile = Cargar(C + "Roads/road_001.prefab");
        if (tile == null) return;
        List<Vector2> dense = spline ? Spline(ctrl) : Densificar(ctrl);
        if (pista) pistas.Add(dense.ToArray());
        else if (!cubierta)
        {
            calles.Add(dense.ToArray());
            if (autos) callesAuto.Add(dense.ToArray());
        }
        float paso = 14f * escala, siguiente = 0f, recorrido = 0f;
        for (int i = 0; i < dense.Count - 1; i++)
        {
            float seg = Vector2.Distance(dense[i], dense[i + 1]);
            while (siguiente <= recorrido + seg)
            {
                float t = seg > 0.0001f ? (siguiente - recorrido) / seg : 0f;
                Vector2 pos = Vector2.Lerp(dense[i], dense[i + 1], t);
                Vector2 dir = dense[Mathf.Min(i + 2, dense.Count - 1)] - dense[Mathf.Max(i - 1, 0)];
                if (dir.sqrMagnitude < 0.01f) dir = dense[i + 1] - dense[i];
                Baldosa(tile, pos, dir.normalized, escala, padre, cubierta);
                siguiente += paso;
            }
            recorrido += seg;
        }
    }

    static void Baldosa(GameObject tile, Vector2 pos, Vector2 dir, float escala, Transform padre, bool cubierta)
    {
        Vector3 n = cubierta ? Vector3.up : Normal(pos);
        float y;
        if (cubierta) y = 0.35f;
        else
        {
            // Se apoya sobre el punto MAS ALTO bajo la baldosa para que ninguna esquina quede enterrada
            Vector2 der = new Vector2(dir.y, -dir.x);
            float l = 7f * escala, w = 6f * escala;
            float h = H(pos);
            h = Mathf.Max(h, H(pos + dir * l));
            h = Mathf.Max(h, H(pos - dir * l));
            h = Mathf.Max(h, H(pos + der * w));
            h = Mathf.Max(h, H(pos - der * w));
            y = Mathf.Max(h, -0.2f) + 0.45f;
        }
        Vector3 fwd = Vector3.ProjectOnPlane(new Vector3(dir.x, 0f, dir.y), n).normalized;
        Colocar(tile, new Vector3(pos.x, y, pos.y), Quaternion.LookRotation(fwd, n), escala, padre);
    }

    // ---------------- edificios ----------------
    static void Edificio(string ruta, Vector2 p, float rotY, float esc, Transform padre)
    {
        var prefab = Cargar(ruta);
        float rango;
        float y = AlturaMin(p, 9f, out rango) - 0.2f;
        Colocar(prefab, new Vector3(p.x, y, p.y), Quaternion.Euler(0f, rotY, 0f), esc, padre);
        bloques.Add(p);
    }

    static void Zona(Vector2 c, float rMin, float rMax, int cantidad, Mod[] mods, float sep, float distCalle,
                     bool costaOk, bool mirarCentro, Transform padre)
    {
        var prefabs = new GameObject[mods.Length];
        for (int i = 0; i < mods.Length; i++) prefabs[i] = Cargar(mods[i].ruta);
        int puestos = 0;
        for (int intento = 0; intento < cantidad * 80 && puestos < cantidad; intento++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float rad = Mathf.Sqrt(Mathf.Lerp(rMin * rMin, rMax * rMax, (float)rng.NextDouble()));
            Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
            if (Vector2.Distance(p, Centro) < 128f) continue;
            if (Vector2.Distance(p, Montana) < 200f) continue;
            if (H(p) <= 0f) continue;
            if (!costaOk && CercaAgua(p, 30f)) continue;
            if (DistCalles(p) < distCalle) continue;
            if (DistPistas(p) < 45f) continue;
            if (Cerca(bloques, p, sep)) continue;
            float rango;
            float y = AlturaMin(p, 9f, out rango) - 0.2f;
            if (rango > 3.5f) continue;
            int k = rng.Next(mods.Length);
            if (prefabs[k] == null) continue;
            float esc = mods[k].esc * Mathf.Lerp(0.9f, 1.1f, (float)rng.NextDouble());
            float rotY;
            float dc; Vector2 q = CalleCercana(p, out dc);
            if (mirarCentro) rotY = Mathf.Atan2(Centro.x - p.x, Centro.y - p.y) * Mathf.Rad2Deg;
            else if (dc < 70f) rotY = Mathf.Atan2(q.x - p.x, q.y - p.y) * Mathf.Rad2Deg;
            else rotY = rng.Next(4) * 90f;
            Colocar(prefabs[k], new Vector3(p.x, y, p.y), Quaternion.Euler(0f, rotY, 0f), esc, padre);
            bloques.Add(p);
            puestos++;
        }
    }

    // ---------------- vegetacion ----------------
    static bool AptoVeg(Vector2 p, float hMin)
    {
        if (H(p) < hMin) return false;
        if (Vector2.Distance(p, Centro) < 135f) return false;
        if (DistPistas(p) < 24f) return false;
        if (Cerca(bloques, p, 22f)) return false;
        if (Cerca(ocupados, p, 9f)) return false;
        return true;
    }

    static void PonerPalma(GameObject palm, Vector2 p, Transform padre)
    {
        Colocar(palm, new Vector3(p.x, H(p) - 0.05f, p.y), Quaternion.Euler(0f, rng.Next(360), 0f),
                Mathf.Lerp(1.3f, 1.7f, (float)rng.NextDouble()), padre);
        ocupados.Add(p);
    }

    static void PalmerasCalle(Transform padre)
    {
        var palm = Cargar(C + "Vegetation/Palm_03.prefab");
        foreach (var c in calles)
        {
            float acc = 0f;
            for (int i = 2; i < c.Length - 2; i++)
            {
                acc += (c[i] - c[i - 1]).magnitude;
                if (acc < 24f) continue;
                acc = 0f;
                Vector2 dir = (c[i + 2] - c[i - 2]).normalized;
                Vector2 der = new Vector2(dir.y, -dir.x);
                for (int lado = -1; lado <= 1; lado += 2)
                {
                    if (rng.NextDouble() < 0.3) continue;
                    Vector2 p = c[i] + der * lado * (11.5f + (float)rng.NextDouble() * 2f);
                    if (!AptoVeg(p, 0f)) continue;
                    if (DistCalles(p) < 9.5f) continue;
                    PonerPalma(palm, p, padre);
                }
            }
        }
    }

    static void Costa(Transform tVeg, Transform tPlaya)
    {
        var palm = Cargar(C + "Vegetation/Palm_03.prefab");
        var bush = Cargar(C + "Vegetation/Bush_07.prefab");
        var parA = Cargar(K + "detail-parasol-a.fbx");
        var parB = Cargar(K + "detail-parasol-b.fbx");
        int palmas = 0, sombrillas = 0;
        for (int i = 0; i < 20000 && (palmas < 130 || sombrillas < 14); i++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float rad = Mathf.Sqrt((float)rng.NextDouble()) * 600f;
            Vector2 p = Centro + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
            float h = H(p);
            if (h < -0.8f || h > 2.2f) continue;
            if (!CercaAgua(p, 45f)) continue;
            if (DistCalles(p) < 12f || DistPistas(p) < 24f) continue;
            if (Cerca(bloques, p, 22f) || Cerca(ocupados, p, 8f)) continue;
            if (p.x < 260f && sombrillas < 14 && h < 1.0f && rng.NextDouble() < 0.5)
            {
                Colocar(rng.Next(2) == 0 ? parA : parB, new Vector3(p.x, Mathf.Max(h, -0.3f), p.y), Quaternion.Euler(0f, rng.Next(360), 0f), 10f, tPlaya);
                ocupados.Add(p);
                sombrillas++;
            }
            else if (palmas < 130)
            {
                if (rng.NextDouble() < 0.25) { Colocar(bush, new Vector3(p.x, h - 0.05f, p.y), Quaternion.Euler(0f, rng.Next(360), 0f), 1.5f, tVeg); ocupados.Add(p); }
                else { PonerPalma(palm, p, tVeg); palmas++; }
            }
        }
    }

    static void Arboledas(Transform padre)
    {
        var palm = Cargar(C + "Vegetation/Palm_03.prefab");
        string[] arb = { "Bush_06", "Bush_07", "Bush_10" };
        var bushes = new GameObject[arb.Length];
        for (int i = 0; i < arb.Length; i++) bushes[i] = Cargar(C + "Vegetation/" + arb[i] + ".prefab");
        int grupos = 0;
        for (int intento = 0; intento < 400 && grupos < 14; intento++)
        {
            float ang = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float rad = Mathf.Lerp(200f, 480f, (float)rng.NextDouble());
            Vector2 c = Centro + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rad;
            float h = H(c);
            if (h < 0.3f || h > 4.5f) continue;
            if (CercaAgua(c, 40f) || EnZona(c, 30f) || DistCalles(c) < 25f) continue;
            for (int k = 0; k < 16; k++)
            {
                float a2 = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                float r2 = Mathf.Sqrt((float)rng.NextDouble()) * 25f;
                Vector2 p = c + new Vector2(Mathf.Cos(a2), Mathf.Sin(a2)) * r2;
                if (!AptoVeg(p, 0.1f) || DistCalles(p) < 9f) continue;
                if (k < 10) PonerPalma(palm, p, padre);
                else
                {
                    Colocar(bushes[rng.Next(bushes.Length)], new Vector3(p.x, H(p) - 0.05f, p.y),
                            Quaternion.Euler(0f, rng.Next(360), 0f), Mathf.Lerp(1.3f, 1.8f, (float)rng.NextDouble()), padre);
                    ocupados.Add(p);
                }
            }
            grupos++;
        }
    }

    // ---------------- autos ----------------
    static void Autos(Transform padre)
    {
        string[] nombres = { "Car_06", "Car_13", "Car_16", "Car_19", "Van", "Futuristic_Car_1" };
        var autos = new GameObject[nombres.Length];
        for (int i = 0; i < nombres.Length; i++) autos[i] = Cargar(C + "Cars/" + nombres[i] + ".prefab");
        int puestos = 0;
        for (int intento = 0; intento < 200 && puestos < 24; intento++)
        {
            var c = callesAuto[rng.Next(callesAuto.Count)];
            if (c.Length < 12) continue;
            int i = 3 + rng.Next(c.Length - 6);
            Vector2 dir = (c[i + 2] - c[i - 2]).normalized;
            Vector2 der = new Vector2(dir.y, -dir.x);
            int lado = rng.Next(2) == 0 ? 1 : -1;
            Vector2 p = c[i] + der * lado * 3.3f;
            if (Vector2.Distance(p, Centro) < 90f || H(p) < -0.2f) continue;
            Vector2 avance = lado > 0 ? dir : -dir;
            Vector3 n = Normal(p);
            Vector3 fwd = Vector3.ProjectOnPlane(new Vector3(avance.x, 0f, avance.y), n).normalized;
            var prefab = autos[rng.Next(autos.Length)];
            float hc = Mathf.Max(H(p), H(c[i]));
            Colocar(prefab, new Vector3(p.x, hc + 0.6f, p.y), Quaternion.LookRotation(fwd, n), 1f, padre);
            puestos++;
        }
    }
}
