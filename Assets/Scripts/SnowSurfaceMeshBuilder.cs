using UnityEngine;

/// <summary>屋根雪の連続雪面メッシュを生成。キューブではなく「雪の塊」として上面を表示する。</summary>
public static class SnowSurfaceMeshBuilder
{
    static Mesh _shared;

    /// <summary>1x1 単位の雪面メッシュ。上面に Perlin でゆるい起伏。cube ではなく連続面。</summary>
    public static Mesh GetOrCreate()
    {
        if (_shared != null) return _shared;
        _shared = Build();
        return _shared;
    }

    static Mesh Build()
    {
        const int subdiv = 24;
        float inv = 1f / subdiv;
        var verts = new Vector3[(subdiv + 1) * (subdiv + 1)];
        var uvs = new Vector2[verts.Length];
        int idx = 0;
        for (int iz = 0; iz <= subdiv; iz++)
        {
            for (int ix = 0; ix <= subdiv; ix++)
            {
                float x = (ix * inv - 0.5f);
                float z = (iz * inv - 0.5f);
                float y = 0.5f + Mathf.PerlinNoise(ix * 0.15f + 37f, iz * 0.15f) * 0.08f - 0.04f;
                verts[idx] = new Vector3(x, y, z);
                uvs[idx] = new Vector2(ix * inv, iz * inv);
                idx++;
            }
        }
        var tris = new int[subdiv * subdiv * 6];
        idx = 0;
        for (int iz = 0; iz < subdiv; iz++)
        {
            for (int ix = 0; ix < subdiv; ix++)
            {
                int a = iz * (subdiv + 1) + ix;
                int b = a + 1;
                int c = a + (subdiv + 1);
                int d = c + 1;
                tris[idx++] = a; tris[idx++] = c; tris[idx++] = b;
                tris[idx++] = b; tris[idx++] = c; tris[idx++] = d;
            }
        }
        var m = new Mesh { name = "SnowSurfaceMesh" };
        m.vertices = verts;
        m.uv = uvs;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
