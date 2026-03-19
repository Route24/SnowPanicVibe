using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// 閉じた立体の「雪塊メッシュ」を生成するエディタ拡張。
/// 天面・側面・底面を持つ closed mesh を生成し、
/// Scene 上にプレビュー GameObject も配置する。
/// Menu: Tools > SnowPanic > Generate Snow Clump Mesh
/// </summary>
public static class SnowClumpMeshGenerator
{
    // --- パラメータ ---
    const int   SeedDefault   = 42;
    const float SizeX         = 1.0f;   // 横幅
    const float SizeZ         = 0.7f;   // 奥行き
    const float HeightTop     = 0.55f;  // 天面の最大高さ（ドームの高さ）
    const float HeightSide    = 0.30f;  // 側面の厚み（底面からの高さ）
    const int   RingCount     = 14;     // 側面の輪郭点数
    const int   TopRadialDiv  = 6;      // 天面の放射分割数（未使用）
    const int   TopRingDiv    = 10;     // 天面の同心円分割数（多いほど滑らか）

    [MenuItem("Tools/SnowPanic/Generate Snow Clump Mesh")]
    public static void GenerateMesh()
    {
        var mesh = BuildClosedMesh(SeedDefault);

        // --- アセット保存 ---
        string dir = "Assets/Art/Meshes";
        if (!AssetDatabase.IsValidFolder(dir))
        {
            System.IO.Directory.CreateDirectory(Application.dataPath + "/Art/Meshes");
            AssetDatabase.Refresh();
        }
        string assetPath = dir + "/SnowClumpMesh.asset";
        // 既存があれば上書き
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            AssetDatabase.SaveAssets();
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, assetPath);
            AssetDatabase.SaveAssets();
        }

        // --- Scene にプレビュー配置 ---
        PlacePreview(assetPath);

        // --- レポート ---
        bool isClosed = CheckClosed(mesh);
        float maxY = 0f;
        foreach (var v in mesh.vertices) if (v.y > maxY) maxY = v.y;
        float minY = 0f;
        foreach (var v in mesh.vertices) if (v.y < minY) minY = v.y;
        float thickness = maxY - minY;

        Debug.Log(
            $"[SnowClumpMeshGenerator] 生成完了\n" +
            $"asset_path={assetPath}\n" +
            $"vertex_count={mesh.vertexCount}\n" +
            $"triangle_count={mesh.triangles.Length / 3}\n" +
            $"closed_mesh={isClosed}\n" +
            $"thickness_y={thickness:F3}（板状でないこと確認: {(thickness > 0.1f ? "OK" : "NG")}）\n" +
            $"snow_clump_self_eval={(isClosed && thickness > 0.1f ? "雪塊らしい立体形状" : "要調整")}"
        );

        EditorUtility.DisplayDialog(
            "Snow Clump Mesh 生成完了",
            $"asset: {assetPath}\n" +
            $"vertices: {mesh.vertexCount}\n" +
            $"triangles: {mesh.triangles.Length / 3}\n" +
            $"closed: {isClosed}\n" +
            $"thickness: {thickness:F3}m\n\n" +
            "Scene に SnowClumpPreview を配置しました。\n横から見て雪塊に見えることを確認してください。",
            "OK"
        );
    }

    // ---------------------------------------------------------------
    // Closed Mesh 生成
    // ---------------------------------------------------------------
    static Mesh BuildClosedMesh(int seed)
    {
        var rng = new System.Random(seed);
        float Rnd() => (float)rng.NextDouble();

        var verts  = new List<Vector3>();
        var tris   = new List<int>();
        var uvs    = new List<Vector2>();

        // ---- 1. 輪郭リング（側面の底辺・上辺） ----
        // 底辺リング（y=0、楕円）
        var bottomRing = new Vector3[RingCount];
        // 上辺リング（y=HeightSide、楕円＋ランダムえぐれ）
        var topRing    = new Vector3[RingCount];

        for (int i = 0; i < RingCount; i++)
        {
            float angle = (float)i / RingCount * Mathf.PI * 2f;
            float rx = Mathf.Cos(angle);
            float rz = Mathf.Sin(angle);

            // えぐれを控えめに（0.88〜1.0）→ 丸い輪郭を維持
            float gouge = 0.88f + 0.12f * (float)rng.NextDouble();
            float bx = rx * SizeX * 0.5f * gouge;
            float bz = rz * SizeZ * 0.5f * gouge;

            bottomRing[i] = new Vector3(bx, 0f, bz);
            // 上辺の高さも揃える（あまりバラつかせない）
            topRing[i]    = new Vector3(bx, HeightSide * (0.92f + 0.08f * (float)rng.NextDouble()), bz);
        }

        // ---- 2. 側面クワッド（底辺リング → 上辺リング） ----
        int sideBase = verts.Count;
        for (int i = 0; i < RingCount; i++)
        {
            verts.Add(bottomRing[i]);
            verts.Add(topRing[i]);
            float u = (float)i / RingCount;
            uvs.Add(new Vector2(u, 0f));
            uvs.Add(new Vector2(u, 1f));
        }
        // 閉じるために最初の点を再追加
        verts.Add(bottomRing[0]);
        verts.Add(topRing[0]);
        uvs.Add(new Vector2(1f, 0f));
        uvs.Add(new Vector2(1f, 1f));

        for (int i = 0; i < RingCount; i++)
        {
            int b0 = sideBase + i * 2;
            int t0 = sideBase + i * 2 + 1;
            int b1 = sideBase + (i + 1) * 2;
            int t1 = sideBase + (i + 1) * 2 + 1;
            tris.Add(b0); tris.Add(t0); tris.Add(b1);
            tris.Add(b1); tris.Add(t0); tris.Add(t1);
        }

        // ---- 3. 底面（底辺リングを fan で塞ぐ） ----
        int bottomCenterIdx = verts.Count;
        verts.Add(Vector3.zero);
        uvs.Add(new Vector2(0.5f, 0.5f));

        int bottomRingBase = verts.Count;
        for (int i = 0; i < RingCount; i++)
        {
            verts.Add(bottomRing[i]);
            uvs.Add(new Vector2(
                0.5f + Mathf.Cos((float)i / RingCount * Mathf.PI * 2f) * 0.5f,
                0.5f + Mathf.Sin((float)i / RingCount * Mathf.PI * 2f) * 0.5f
            ));
        }
        for (int i = 0; i < RingCount; i++)
        {
            int a = bottomRingBase + i;
            int b = bottomRingBase + (i + 1) % RingCount;
            // 底面は下向き法線なので逆順
            tris.Add(bottomCenterIdx); tris.Add(b); tris.Add(a);
        }

        // ---- 4. 天面（放射状ドーム、上辺リングから頂点へ） ----
        // 上辺リングの頂点を天面用に再登録
        int topRingBase = verts.Count;
        for (int i = 0; i < RingCount; i++)
        {
            verts.Add(topRing[i]);
            uvs.Add(new Vector2(
                0.5f + Mathf.Cos((float)i / RingCount * Mathf.PI * 2f) * 0.5f,
                0.5f + Mathf.Sin((float)i / RingCount * Mathf.PI * 2f) * 0.5f
            ));
        }

        // 天面の内側リング（同心円状に分割してドーム形状を作る）
        // 外側から中心へ向かって収縮しながら高さを上げる
        var topInnerRings = new List<int[]>();
        for (int ring = 1; ring <= TopRingDiv; ring++)
        {
            float t01 = (float)ring / TopRingDiv;
            // 外側(t01=0)→ HeightSide、中心(t01=1)→ HeightSide+HeightTop
            // SmoothStep で滑らかなドーム
            float h = HeightSide + HeightTop * Mathf.SmoothStep(0f, 1f, t01);
            // 輪郭は中心に向かって収縮（楕円を維持）
            float scale = 1f - t01 * 0.97f;

            var ringIndices = new int[RingCount];
            for (int i = 0; i < RingCount; i++)
            {
                float angle = (float)i / RingCount * Mathf.PI * 2f;
                float nx = Mathf.Cos(angle) * scale;
                float nz = Mathf.Sin(angle) * scale;
                // 天面の凹凸は控えめに（雪の丸みを優先）
                float noise = Mathf.PerlinNoise(nx * 2.5f + seed * 0.1f, nz * 2.5f + seed * 0.1f) - 0.5f;
                float hNoise = h + noise * 0.04f * (1f - t01);
                ringIndices[i] = verts.Count;
                verts.Add(new Vector3(nx * SizeX * 0.5f, Mathf.Max(HeightSide, hNoise), nz * SizeZ * 0.5f));
                uvs.Add(new Vector2(0.5f + nx * 0.5f, 0.5f + nz * 0.5f));
            }
            topInnerRings.Add(ringIndices);
        }

        // 天面頂点（中心）- 最高点
        int topCenterIdx = verts.Count;
        verts.Add(new Vector3(0f, HeightSide + HeightTop, 0f));
        uvs.Add(new Vector2(0.5f, 0.5f));

        // 天面トライアングル：外側リング → 内側リング → 中心
        // 外側リング（topRingBase）→ 最初の内側リング
        var prevRing = new int[RingCount];
        for (int i = 0; i < RingCount; i++) prevRing[i] = topRingBase + i;

        foreach (var curRing in topInnerRings)
        {
            for (int i = 0; i < RingCount; i++)
            {
                int next = (i + 1) % RingCount;
                tris.Add(prevRing[i]);    tris.Add(curRing[i]);    tris.Add(prevRing[next]);
                tris.Add(prevRing[next]); tris.Add(curRing[i]);    tris.Add(curRing[next]);
            }
            prevRing = curRing;
        }
        // 最内リング → 中心
        for (int i = 0; i < RingCount; i++)
        {
            int next = (i + 1) % RingCount;
            tris.Add(prevRing[i]); tris.Add(topCenterIdx); tris.Add(prevRing[next]);
        }

        // ---- メッシュ組み立て ----
        var mesh = new Mesh();
        mesh.name = "SnowClump_Organic";
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------
    // Closed mesh チェック（境界エッジがないこと）
    // ---------------------------------------------------------------
    static bool CheckClosed(Mesh mesh)
    {
        var edgeCount = new Dictionary<(int, int), int>();
        var tris = mesh.triangles;
        for (int i = 0; i < tris.Length; i += 3)
        {
            int a = tris[i], b = tris[i + 1], c = tris[i + 2];
            AddEdge(edgeCount, a, b);
            AddEdge(edgeCount, b, c);
            AddEdge(edgeCount, c, a);
        }
        foreach (var kv in edgeCount)
            if (kv.Value != 2) return false;
        return true;
    }

    static void AddEdge(Dictionary<(int, int), int> d, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        d[key] = d.TryGetValue(key, out int v) ? v + 1 : 1;
    }

    // ---------------------------------------------------------------
    // Scene プレビュー配置
    // ---------------------------------------------------------------
    static void PlacePreview(string assetPath)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (mesh == null) return;

        // 既存プレビューを削除
        var existing = GameObject.Find("SnowClumpPreview");
        if (existing != null) Object.DestroyImmediate(existing);

        var go = new GameObject("SnowClumpPreview");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();

        // マテリアル：白いデフォルト
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.color = new Color(0.9f, 0.95f, 1f);
        mr.sharedMaterial = mat;

        // 見やすい位置・スケールに配置
        go.transform.position = new Vector3(0f, 1f, 0f);
        go.transform.localScale = Vector3.one * 2f; // 2倍スケールで見やすく

        // Scene ビューをフォーカス
        Selection.activeGameObject = go;
        SceneView.lastActiveSceneView?.FrameSelected();
    }
}
