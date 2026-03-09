using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 診断専用: 修正禁止。roof_logic / roof_visual / snow_spawn / snow_visual の4つを色分けして可視化し、
/// 実際に使われているオブジェクトをログで確定する。
/// 使い方: 空のGameObjectにアタッチ → Play。Quad overlayでGame viewに色付き半透明表示。Scene viewはGizmos。
/// </summary>
public class SnowPanicTargetDiagnostic : MonoBehaviour
{
    [Header("可視化色（変更禁止）")]
    public Color roofLogicColor = new Color(0.2f, 1f, 0.2f);
    public Color roofVisualColor = new Color(1f, 1f, 0.2f);
    public Color snowSpawnColor = new Color(1f, 0.2f, 0.2f);
    public Color snowVisualColor = new Color(0.2f, 0.5f, 1f);

    [Header("診断")]
    public float logIntervalSec = 3f;
    public bool drawOverlay = true;
    [Range(0.5f, 1f)] public float overlayAlpha = 0.75f;
    [Tooltip("ON: 各ターゲットのRendererに診断色を強制適用（Game viewで確実に見える）")]
    public bool forceTintRenderers = false;

    Transform _roofLogic;
    Transform _roofVisual;
    Transform _snowSpawn;
    Transform _snowVisual;
    float _nextLogTime = -10f;
    Transform[] _quadOverlays;
    Transform _overlaysRoot;
    int _activeOverlayCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureDiagnosticInScene()
    {
        if (FindFirstObjectByType<SnowPanicTargetDiagnostic>() != null) return;
        var go = new GameObject("SnowPanicTargetDiagnostic");
        go.AddComponent<SnowPanicTargetDiagnostic>();
        Debug.Log("[TARGET_DIAG] Auto-added SnowPanicTargetDiagnostic. Play中に色付きオーバーレイ表示。");
    }

    void Start()
    {
        ResolveTargets();
        if (drawOverlay) UpdateQuadOverlays();
        LogTargets();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        ResolveTargets();
        if (forceTintRenderers) ApplyDiagnosticTints();
        if (drawOverlay) UpdateQuadOverlays();
        if (Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + logIntervalSec;
            LogTargets();
        }
    }

    void EnsureQuadOverlays()
    {
        if (_overlaysRoot != null) return;
        _overlaysRoot = new GameObject("TargetDiagnostic_Overlays").transform;
        _overlaysRoot.SetParent(transform);
        _overlaysRoot.localPosition = Vector3.zero;
        _quadOverlays = new Transform[4];
        Color[] colors = { roofLogicColor, roofVisualColor, snowSpawnColor, snowVisualColor };
        string[] names = { "roof_logic", "roof_visual", "snow_spawn", "snow_visual" };
        var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Universal Render Pipeline/Unlit");
        for (int i = 0; i < 4; i++)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "Overlay_" + names[i];
            go.transform.SetParent(_overlaysRoot);
            Object.Destroy(go.GetComponent<Collider>());
            var c = colors[i];
            c.a = overlayAlpha;
            var mat = shader != null ? new Material(shader) : null;
            if (mat != null)
            {
                mat.color = c;
                mat.renderQueue = 3000;
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                go.GetComponent<Renderer>().material = mat;
            }
            go.SetActive(false);
            _quadOverlays[i] = go.transform;
        }
    }

    void UpdateQuadOverlays()
    {
        Transform[] targets = { _roofLogic, _roofVisual, _snowSpawn, _snowVisual };
        EnsureQuadOverlays();
        _activeOverlayCount = 0;
        if (_quadOverlays == null) return;
        var cam = Camera.main;
        for (int i = 0; i < 4; i++)
        {
            var quad = _quadOverlays[i];
            if (targets[i] == null || !targets[i].gameObject.activeInHierarchy)
            {
                quad.gameObject.SetActive(false);
                continue;
            }
            Bounds b = ComputeBounds(targets[i]);
            Vector3 center = b.center;
            if (cam != null)
            {
                Vector3 toCam = (cam.transform.position - center).normalized;
                center += toCam * 0.015f;
            }
            quad.position = center;
            quad.localScale = new Vector3(Mathf.Max(0.1f, b.size.x), Mathf.Max(0.1f, b.size.z), 1f);
            if (cam != null)
                quad.rotation = Quaternion.LookRotation(center - cam.transform.position);
            else
                quad.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var r = quad.GetComponent<Renderer>();
            if (r != null && r.material != null)
            {
                Color c = i == 0 ? roofLogicColor : (i == 1 ? roofVisualColor : (i == 2 ? snowSpawnColor : snowVisualColor));
                c.a = overlayAlpha;
                r.material.color = c;
            }
            quad.gameObject.SetActive(true);
            _activeOverlayCount++;
        }
    }

    void ApplyDiagnosticTints()
    {
        TintRenderer(_roofLogic, roofLogicColor);
        TintRenderer(_roofVisual, roofVisualColor);
        TintRenderer(_snowSpawn, snowSpawnColor);
        TintRenderer(_snowVisual, snowVisualColor);
    }

    void TintRenderer(Transform t, Color c)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return;
        var r = t.GetComponent<Renderer>();
        if (r == null) r = t.GetComponentInChildren<Renderer>(true);
        if (r == null) return;
        if (r.sharedMaterial == null) return;
        var mat = r.material;
        if (mat == null) return;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        else if (mat.HasProperty("_Color")) mat.SetColor("_Color", c);
    }

    void ResolveTargets()
    {
        var roofSys = FindFirstObjectByType<RoofSnowSystem>();
        var spawner = FindFirstObjectByType<SnowPackSpawner>();

        _roofLogic = roofSys != null && roofSys.roofSlideCollider != null ? roofSys.roofSlideCollider.transform : null;
        _roofVisual = _roofLogic != null ? _roofLogic.Find("RoofSnowLayer") : null;
        if (_roofVisual == null && roofSys != null)
        {
            var layer = roofSys.GetRoofLayerRenderer();
            if (layer != null) _roofVisual = layer.transform;
        }
        _snowSpawn = spawner != null && spawner.roofCollider != null ? spawner.roofCollider.transform : null;
        _snowVisual = spawner != null ? GetSnowVisualRoot(spawner) : null;
    }

    static Transform GetSnowVisualRoot(SnowPackSpawner s)
    {
        if (s == null) return null;
        try
        {
            var t = s.GetType().GetField("_visualRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (t != null) { var v = t.GetValue(s) as Transform; if (v != null) return v; }
            var p = s.GetType().GetField("_piecesRoot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (p != null) { var x = p.GetValue(s) as Transform; if (x != null) return x; }
        }
        catch { }
        var found = GameObject.Find("SnowPackVisual");
        return found != null ? found.transform : null;
    }

    void LogTargets()
    {
        string N(Transform t) => t != null ? t.gameObject.name : "null";
        int ID(Transform t) => t != null ? t.GetInstanceID() : -1;
        string ACT(Transform t) => t != null ? t.gameObject.activeSelf.ToString().ToLower() : "null";
        string BND(Transform t)
        {
            if (t == null) return "null";
            var b = ComputeBounds(t);
            return $"({b.size.x:F3},{b.size.y:F3},{b.size.z:F3})";
        }
        string TR(Transform t)
        {
            if (t == null) return "null";
            var p = t.position;
            var s = t.lossyScale;
            return $"pos=({p.x:F2},{p.y:F2},{p.z:F2}) scale=({s.x:F2},{s.y:F2},{s.z:F2})";
        }
        bool sameLogicVisual = _roofLogic != null && _roofVisual != null && _roofLogic == _roofVisual;
        bool sameLogicSpawn = _roofLogic != null && _snowSpawn != null && _roofLogic == _snowSpawn;
        bool sameSpawnVisual = _snowSpawn != null && _snowVisual != null && _snowSpawn == _snowVisual;
        bool overlayVisible = drawOverlay && _activeOverlayCount > 0 && Camera.main != null;
        string msg = $"[TARGET_DIAG] roof_logic_target_name={N(_roofLogic)} roof_visual_target_name={N(_roofVisual)} snow_spawn_target_name={N(_snowSpawn)} snow_visual_target_name={N(_snowVisual)} roof_logic_bounds_size={BND(_roofLogic)} roof_visual_bounds_size={BND(_roofVisual)} snow_spawn_bounds_size={BND(_snowSpawn)} snow_visual_bounds_size={BND(_snowVisual)} roof_logic_transform={TR(_roofLogic)} roof_visual_transform={TR(_roofVisual)} snow_spawn_transform={TR(_snowSpawn)} snow_visual_transform={TR(_snowVisual)} roof_logic_active={ACT(_roofLogic)} roof_visual_active={ACT(_roofVisual)} snow_spawn_active={ACT(_snowSpawn)} snow_visual_active={ACT(_snowVisual)} same_logic_and_visual={sameLogicVisual.ToString().ToLower()} same_logic_and_spawn={sameLogicSpawn.ToString().ToLower()} same_spawn_and_visual={sameSpawnVisual.ToString().ToLower()} diagnostic_overlay_visible={overlayVisible.ToString().ToLower()}";
        UnityEngine.Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport("=== TARGET_DIAGNOSTIC ===");
        SnowLoopLogCapture.AppendToAssiReport($"roof_logic_target_name={N(_roofLogic)} roof_visual_target_name={N(_roofVisual)} snow_spawn_target_name={N(_snowSpawn)} snow_visual_target_name={N(_snowVisual)}");
        SnowLoopLogCapture.AppendToAssiReport($"roof_logic_bounds_size={BND(_roofLogic)} roof_visual_bounds_size={BND(_roofVisual)} snow_spawn_bounds_size={BND(_snowSpawn)} snow_visual_bounds_size={BND(_snowVisual)}");
        SnowLoopLogCapture.AppendToAssiReport($"same_logic_and_visual={sameLogicVisual.ToString().ToLower()} same_logic_and_spawn={sameLogicSpawn.ToString().ToLower()} same_spawn_and_visual={sameSpawnVisual.ToString().ToLower()} diagnostic_overlay_visible={overlayVisible.ToString().ToLower()}");
    }

    Bounds ComputeBounds(Transform t)
    {
        var col = t.GetComponent<Collider>();
        if (col != null) return col.bounds;
        var r = t.GetComponent<Renderer>();
        if (r != null) return r.bounds;
        var rnds = t.GetComponentsInChildren<Renderer>(true);
        if (rnds != null && rnds.Length > 0)
        {
            Bounds b = rnds[0].bounds;
            for (int i = 1; i < rnds.Length; i++)
                if (rnds[i] != null && rnds[i].enabled) b.Encapsulate(rnds[i].bounds);
            return b;
        }
        return new Bounds(t.position, Vector3.one * 0.5f);
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        ResolveTargets();
        Gizmos.color = roofLogicColor;
        DrawGizmoBounds(_roofLogic);
        Gizmos.color = roofVisualColor;
        DrawGizmoBounds(_roofVisual);
        Gizmos.color = snowSpawnColor;
        DrawGizmoBounds(_snowSpawn);
        Gizmos.color = snowVisualColor;
        DrawGizmoBounds(_snowVisual);
    }

    void DrawGizmoBounds(Transform t)
    {
        if (t == null || !t.gameObject.activeInHierarchy) return;
        Bounds b = ComputeBounds(t);
        Gizmos.DrawWireCube(b.center, b.size);
    }

#if UNITY_EDITOR
    [MenuItem("SnowPanicVibe/Add Target Diagnostic")]
    static void MenuAddDiagnostic()
    {
        var go = new GameObject("SnowPanicTargetDiagnostic");
        go.AddComponent<SnowPanicTargetDiagnostic>();
        Selection.activeGameObject = go;
        Debug.Log("[TARGET_DIAG] SnowPanicTargetDiagnostic を追加しました。Playして確認。");
    }
#endif
}
