using System;
using UnityEngine;

// Cordilleras extra para el terreno de la isla.
public static partial class BridgeServer
{
    [Serializable]
    class MountainRange
    {
        public float angle;      // grados: 0 = este, 90 = norte, 180 = oeste, 270 = sur
        public float distance;   // fracción del radio de la isla (0.6 a 0.85)
        public float radius;     // fracción del radio de la isla (0.15 a 0.25)
        public float height;     // metros
    }

    static MountainRange[] extraRanges;

    static float ExtraRangesHeight(float wx, float wz, float cx, float cz, float islandR, float seedOffset)
    {
        if (extraRanges == null) return 0f;

        float best = 0f;
        foreach (var r in extraRanges)
        {
            if (r.height <= 0f || r.radius <= 0f) continue;

            float a = r.angle * Mathf.Deg2Rad;
            float mcx = cx + Mathf.Cos(a) * islandR * r.distance;
            float mcz = cz + Mathf.Sin(a) * islandR * r.distance;
            float mr = islandR * r.radius;

            float dx = wx - mcx;
            float dz = wz - mcz;
            float md = Mathf.Sqrt(dx * dx + dz * dz);
            if (md >= mr) continue;

            float m = 1f - Smooth(md, 0f, mr);
            float ridge = 1f - Mathf.Abs(2f * Fbm(wx / 130f, wz / 130f, 4, seedOffset + 120f + r.angle) - 1f);
            float rough = 0.2f + 0.8f * Mathf.Pow(ridge, 2f);

            best = Mathf.Max(best, r.height * Mathf.Pow(m, 1.1f) * rough);
        }
        return best;
    }
}