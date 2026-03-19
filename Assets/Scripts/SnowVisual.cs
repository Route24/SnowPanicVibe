using UnityEngine;

/// <summary>
/// Snow Visual Module – 雪の見た目専用。
/// ゲームプレイロジックは変更しない。
/// ・丸みのあるブロック形状
/// ・頂点ノイズによる表面の不揃い
/// ・柔らかい雪シェーダー（白・青味・ソフトライティング）
/// ・雪崩時の雪粉パーティクル
/// ・ソフトシャドウ（屋根への投影）
/// </summary>
[DefaultExecutionOrder(-150)]
public class SnowVisual : MonoBehaviour
{
    public static SnowVisual Instance { get; private set; }

    [Header("Snow Visual Settings")]
    [Tooltip("false で従来の立方体に戻る")]
    public bool visualModuleEnabled = true;
    [Tooltip("丸みの強さ。0=立方体、0.5=丸い。ASSI雪塊感: 0.48=白い板感をさらに減らす")]
    [Range(0f, 0.5f)] public float roundness = 0.48f;
    [Tooltip("頂点ノイズの強さ。表面の不揃いでブロック断面感を減らす")]
    [Range(0f, 0.1f)] public float vertexNoiseStrength = 0.06f;
    [Tooltip("シード（同じ値で再現可能）")]
    public int noiseSeed = 42;

    [Header("Snow Powder (Avalanche)")]
    [Tooltip("雪崩時の雪粉パーティクル数")]
    public int powderParticleCount = 60;
    [Tooltip("雪粉の持続時間")]
    public float powderLifetime = 0.25f;

    static Mesh _roundedMesh;
    static Material _snowMaterial;
    static bool _initialized;

    void Awake()
    {
        Instance = this;
        _initialized = false;
        EnsureSoftShadows();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>SnowPackSpawner が使用するメッシュを取得。丸み・頂点ノイズ適用。無効時は null。</summary>
    public static Mesh GetPieceMesh()
    {
        EnsureInitialized();
        return _roundedMesh;
    }

    /// <summary>SnowVisual Instance がない場合に runtime 生成して production mesh を確保する。</summary>
    public static void ForceEnsureInstance()
    {
        if (Instance != null) return;
        var found = Object.FindFirstObjectByType<SnowVisual>();
        if (found != null) { Instance = found; }
        else
        {
            var go = new GameObject("SnowVisual_AutoCreated") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<SnowVisual>();
            UnityEngine.Debug.Log("[FALLING_MESH_ROUTE] SnowVisual_instance_was_null -> auto_created production mesh now available");
        }
        EnsureInitialized();
    }

    /// <summary>SnowPackSpawner が使用するマテリアルを取得。無効時は null。</summary>
    public static Material GetSnowMaterial(Color baseColor)
    {
        var inst = Instance ?? Object.FindFirstObjectByType<SnowVisual>();
        if (inst != null && !inst.visualModuleEnabled) return null;
        EnsureInitialized();
        if (_snowMaterial != null)
            MaterialColorHelper.SetColorSafe(_snowMaterial, baseColor);
        return _snowMaterial;
    }

    internal static void EnsureInitialized()
    {
        if (_initialized && _roundedMesh != null && _snowMaterial != null) return;
        var inst = Instance ?? Object.FindFirstObjectByType<SnowVisual>();
        if (inst == null)
        {
            var go = new GameObject("SnowVisual") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            inst = go.AddComponent<SnowVisual>();
        }
        float roundness = inst != null ? inst.roundness : 0.48f;
        float noiseStr = inst != null ? inst.vertexNoiseStrength : 0.03f;
        int seed = inst != null ? inst.noiseSeed : 42;
        _roundedMesh = BuildRoundedMesh(roundness, noiseStr, seed);
        _snowMaterial = CreateSnowMaterial();
        _initialized = true;
    }

    static Mesh BuildRoundedMesh(float roundness, float noiseStrength, int seed)
    {
        // 常に球状雪塊メッシュを使う（キューブ感を排除）
        return BuildSnowClumpMesh(noiseStrength, seed);
    }

    /// <summary>
    /// 球状の雪塊メッシュ。上面が少し平らで下面が丸い不定形の雪の塊。
    /// UV球（縦8・横8）ベースに頂点ノイズを加えた低ポリ雪塊。
    /// </summary>
    static Mesh BuildSnowClumpMesh(float noiseStrength, int seed)
    {
        var m = new Mesh { name = "SnowVisualRoundedMesh" };
        var rand = new System.Random(seed);
        float Noise() => (float)(rand.NextDouble() * 2 - 1) * noiseStrength;

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris  = new System.Collections.Generic.List<int>();

        // UV球パラメータ（ローポリ感を残す）
        int latDiv = 7;   // 縦分割（少ないほどローポリ）
        int lonDiv = 8;   // 横分割

        // 上端（頂点）
        verts.Add(new Vector3(Noise(), 0.5f + Noise() * 0.5f, Noise()));

        // 中間リング
        for (int lat = 1; lat < latDiv; lat++)
        {
            float phi = Mathf.PI * lat / latDiv; // 0→π
            float sinP = Mathf.Sin(phi);
            float cosP = Mathf.Cos(phi);

            // 上半球は少し縦に伸ばし、下半球は平らにして「雪の塊」らしく
            float yScale = cosP > 0 ? 1.1f : 0.7f;
            float xzScale = 0.5f;

            for (int lon = 0; lon < lonDiv; lon++)
            {
                float theta = 2f * Mathf.PI * lon / lonDiv;
                float x = sinP * Mathf.Cos(theta) * xzScale;
                float y = cosP * 0.5f * yScale;
                float z = sinP * Mathf.Sin(theta) * xzScale;
                verts.Add(new Vector3(x + Noise(), y + Noise() * 0.5f, z + Noise()));
            }
        }

        // 下端（底点）- 少し上に寄せて底を平らに見せる
        verts.Add(new Vector3(Noise(), -0.38f + Noise() * 0.3f, Noise()));

        // トライアングル：上端 → 最初のリング
        for (int lon = 0; lon < lonDiv; lon++)
        {
            int a = 1 + lon;
            int b = 1 + (lon + 1) % lonDiv;
            tris.Add(0); tris.Add(b); tris.Add(a);
        }

        // 中間リング同士
        for (int lat = 0; lat < latDiv - 2; lat++)
        {
            int ringStart = 1 + lat * lonDiv;
            int nextStart = 1 + (lat + 1) * lonDiv;
            for (int lon = 0; lon < lonDiv; lon++)
            {
                int a = ringStart + lon;
                int b = ringStart + (lon + 1) % lonDiv;
                int c = nextStart + lon;
                int d = nextStart + (lon + 1) % lonDiv;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        // 最後のリング → 下端
        int lastRingStart = 1 + (latDiv - 2) * lonDiv;
        int bottomIdx = verts.Count - 1;
        for (int lon = 0; lon < lonDiv; lon++)
        {
            int a = lastRingStart + lon;
            int b = lastRingStart + (lon + 1) % lonDiv;
            tris.Add(a); tris.Add(bottomIdx); tris.Add(b);
        }

        m.SetVertices(verts);
        m.SetTriangles(tris, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Mesh BuildCubeMesh(float roundness, float noiseStrength, int seed)
    {
        var m = new Mesh { name = "SnowVisualRoundedMesh" };
        var rand = new System.Random(seed);

        var v = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
        };
        for (int i = 0; i < 24; i++)
        {
            var p = v[i];
            var n = p.normalized;
            v[i] = Vector3.Lerp(p, n * 0.5f, roundness)
                + new Vector3((float)(rand.NextDouble() * 2 - 1) * noiseStrength,
                              (float)(rand.NextDouble() * 2 - 1) * noiseStrength,
                              (float)(rand.NextDouble() * 2 - 1) * noiseStrength);
        }
        int[] trisArr = { 0,2,1,0,3,2,4,6,5,4,7,6,8,10,9,8,11,10,12,14,13,12,15,14,16,18,17,16,19,18,20,22,21,20,23,22 };
        m.vertices = v;
        m.triangles = trisArr;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    static Material CreateSnowMaterial()
    {
        var sh = Shader.Find("SnowVisual/SoftSnow");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var mat = new Material(sh);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", new Color(0.92f, 0.95f, 1f)); // 雪白（わずかに青みがかった白）
        if (mat.HasProperty("_BlueTint"))
            mat.SetFloat("_BlueTint", 0.08f);
        if (mat.HasProperty("_Softness"))
            mat.SetFloat("_Softness", 1.2f);
        mat.enableInstancing = true;
        return mat;
    }

    /// <summary>雪崩時に呼ばれる。雪粉パーティクルをスポーン。</summary>
    public static void SpawnPowderAt(Vector3 worldPos, int count = -1)
    {
        var inst = Instance ?? Object.FindFirstObjectByType<SnowVisual>();
        if (inst != null && !inst.visualModuleEnabled) return;
        int n = (inst != null && count < 0) ? inst.powderParticleCount : (count > 0 ? count : 60);
        float life = inst != null ? inst.powderLifetime : 0.25f;
        DoSpawnPowder(worldPos, n, life);
    }

    static void DoSpawnPowder(Vector3 worldPos, int count, float lifetime)
    {
        var go = new GameObject("SnowVisualPowder");
        go.transform.position = worldPos + Vector3.up * 0.1f;
        var ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false; // 明示 Play まで待機（duration 再生中変更エラー回避）
        // duration は変更しない（再生中変更で Unity エラー回避）。ワンショットバーストは startLifetime で制御
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.5f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new Color(0.92f, 0.95f, 1f, 0.6f);
        main.maxParticles = Mathf.Min(count, 120);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.05f;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Min(count, short.MaxValue), (short)Mathf.Min(count, short.MaxValue)) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;

        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        UnityEngine.Debug.Log("[SnowVisual] particle_error_source=SnowVisualPowder particle_velocity_mode_fixed=true");

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(0.94f, 0.96f, 1f), 0f), new GradientColorKey(new Color(0.94f, 0.96f, 1f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.2f), new GradientAlphaKey(0.2f, 0.6f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (sh != null)
                rend.sharedMaterial = new Material(sh) { color = new Color(0.94f, 0.96f, 1f, 0.5f) };
        }

        ps.Play();
        Object.Destroy(go, lifetime + 0.5f);
    }

    /// <summary>ソフトシャドウを有効化。Directional Light に適用。</summary>
    public static void EnsureSoftShadows()
    {
        var light = Object.FindFirstObjectByType<Light>();
        if (light != null && light.type == LightType.Directional && light.shadows == LightShadows.None)
        {
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.75f;
        }
    }
}
