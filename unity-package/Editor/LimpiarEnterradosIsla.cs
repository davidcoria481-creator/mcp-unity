using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Borra de "Ciudad_Isla" lo que quedo enterrado por la montana (carreteras, autos, palmeras).
// Se ejecuta una vez al compilar. Informe en Assets/Editor/InformeLimpieza.cs. Un solo Ctrl+Z lo deshace.
[InitializeOnLoad]
public static class LimpiarEnterradosIsla
{
    const string Clave = "OseWorld_LimpiezaEnterrados_v1";
    static Terrain terr;

    static LimpiarEnterradosIsla() { EditorApplication.delayCall += Auto; }

    static void Auto()
    {
        if (EditorPrefs.GetBool(Clave, false)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Auto; return; }
        if (GameObject.Find("Ciudad_Isla") == null) return;
        Ejecutar();
    }

    static float H(float x, float z) { return terr.SampleHeight(new Vector3(x, 0f, z)) + terr.transform.position.y; }

    static void Ejecutar()
    {
        var sb = new StringBuilder();
        terr = Object.FindFirstObjectByType<Terrain>();
        var raiz = GameObject.Find("Ciudad_Isla");
        if (terr == null || raiz == null) sb.AppendLine("Falta Terrain o Ciudad_Isla.");
        else
        {
            try
            {
                var borrar = new List<GameObject>();
                int calles = 0, autos = 0, plantas = 0;
                foreach (Transform grupo in raiz.transform)
                {
                    string g = grupo.name;
                    if (g != "Carreteras" && g != "Vegetacion_Isla" && g != "Vehiculos_Isla" && g != "Playa") continue;
                    foreach (Transform t in grupo)
                    {
                        Vector3 p = t.position; float h = H(p.x, p.z); float d = p.y - h;
                        if (g == "Carreteras") { if (t.name.StartsWith("road_") && h > -1f && d < 0.1f) { borrar.Add(t.gameObject); calles++; sb.AppendLine("  baldosa en (" + p.x.ToString("0") + ", " + p.z.ToString("0") + ")"); } }
                        else if (g == "Vehiculos_Isla") { if (d < 0.1f) { borrar.Add(t.gameObject); autos++; sb.AppendLine("  " + t.name + " en (" + p.x.ToString("0") + ", " + p.z.ToString("0") + ")"); } }
                        else if (d < -1f && h > -1f) { borrar.Add(t.gameObject); plantas++; }
                    }
                }
                Undo.IncrementCurrentGroup();
                int ug = Undo.GetCurrentGroup();
                foreach (var go in borrar) Undo.DestroyObjectImmediate(go);
                Undo.CollapseUndoOperations(ug);
                EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                sb.Insert(0, "Borrados: " + calles + " baldosas, " + autos + " autos, " + plantas + " plantas enterradas (total " + borrar.Count + ").\n");
            }
            catch (System.Exception e) { sb.AppendLine("Error: " + e); }
        }
        EditorPrefs.SetBool(Clave, true);
        Debug.Log("[Limpieza] " + sb);
        File.WriteAllText(Application.dataPath + "/Editor/InformeLimpieza.cs", "/*\nINFORME DE LIMPIEZA\n" + sb.ToString().Replace("*/", "* /") + "\n*/\n");
        AssetDatabase.ImportAsset("Assets/Editor/InformeLimpieza.cs");
    }
}
