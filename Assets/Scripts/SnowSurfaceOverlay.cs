using UnityEngine;

/// <summary>
/// 最小実装: セルロジックは維持し、見た目だけ雪面にする。
/// キューブ感を消し、屋根上に1枚のSnowSurfaceを表示。崩壊時の粒演出は維持。
/// </summary>
public class SnowSurfaceOverlay : MonoBehaviour
{
    [Header("Snow Surface")]
    public bool useSnowSurface = true;
    public Color surfaceColor = new Color(0.95f, 0.97f, 1f);
    public float surfaceThickness = 0.06f;
    public float surfaceOffsetY = 0.002f;

    [Header("Log")]
    public float logIntervalSec = 3f;

    GameObject _surfaceRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInScene()
    {
        if (FindFirstObjectByType<SnowSurfaceOverlay>() != null) return;
        var go = new GameObject("SnowSurfaceOverlay");
        go.AddComponent<SnowSurfaceOverlay>();
    }

    Transform _surfaceTransform;
    Renderer _surfaceRenderer;
    SnowPackSpawner _spawner;
    float _nextLogTime = -10f;

    void Start()
    {
        _spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (_spawner == null) return;
        GridVisualWatchdog.UseSnowSurfaceMode = useSnowSurface;
        if (useSnowSurface)
            GridVisualWatchdog.showSnowGridDebug = false;
        EnsureSurface();
    }

    void OnDestroy()
    {
        if (!useSnowSurface) return;
        GridVisualWatchdog.UseSnowSurfaceMode = false;
        GridVisualWatchdog.showSnowGridDebug = true;
    }

    void LateUpdate()
    {
        if (_spawner == null) _spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (_spawner == null || !useSnowSurface) return;
        if (_spawner.roofCollider == null) return;

        EnsureSurface();
        UpdateSurfaceTransform();
        if (Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + logIntervalSec;
            LogState();
        }
    }

    void EnsureSurface()
    {
        if (_surfaceRoot != null) return;
        if (_spawner == null || _spawner.roofCollider == null) return;
        if (_spawner.RoofWidth <= 0f || _spawner.RoofLength <= 0f) return;

        var roof = _spawner.roofCollider.transform;
        var existing = roof.Find("SnowSurface");
        if (existing != null)
        {
            _surfaceRoot = existing.gameObject;
        }
        else
        {
            _surfaceRoot = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _surfaceRoot.name = "SnowSurface";
            _surfaceRoot.transform.SetParent(roof, false);
            Object.Destroy(_surfaceRoot.GetComponent<Collider>());
        }
        _surfaceTransform = _surfaceRoot.transform;
        _surfaceRenderer = _surfaceRoot.GetComponent<Renderer>();
        if (_surfaceRenderer != null && _surfaceRenderer.material != null)
        {
            _surfaceRenderer.material.color = surfaceColor;
            _surfaceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _surfaceRenderer.receiveShadows = true;
        }
        _surfaceRoot.SetActive(true);
    }

    void UpdateSurfaceTransform()
    {
        if (_surfaceTransform == null || _spawner == null) return;
        float w = _spawner.RoofWidth;
        float l = _spawner.RoofLength;
        if (w <= 0f || l <= 0f) return;

        Vector3 center = _spawner.RoofCenter + _spawner.RoofUp * (surfaceThickness * 0.5f + surfaceOffsetY);
        _surfaceTransform.position = center;
        _surfaceTransform.rotation = Quaternion.LookRotation(_spawner.RoofUp, _spawner.RoofF);
        _surfaceTransform.localScale = new Vector3(w, l, 1f);
    }

    void LogState()
    {
        bool cellLogicActive = _spawner != null && _spawner.GetPackedCubeCountRealtime() > 0;
        bool cellRendererVisible = GridVisualWatchdog.showSnowGridDebug;
        bool snowSurfaceActive = useSnowSurface && _surfaceRoot != null && _surfaceRoot.activeSelf;
        Vector3 surfaceSize = _surfaceTransform != null ? new Vector3(_spawner.RoofWidth, surfaceThickness, _spawner.RoofLength) : Vector3.zero;
        bool surfaceMatchesLogic = _spawner != null && _surfaceTransform != null &&
            Mathf.Abs(_surfaceTransform.lossyScale.x - _spawner.RoofWidth) < 0.1f &&
            Mathf.Abs(_surfaceTransform.lossyScale.y - _spawner.RoofLength) < 0.1f;
        string visibleStyle = useSnowSurface && !cellRendererVisible ? "surface" : (cellRendererVisible && !useSnowSurface ? "cubes" : "mixed");

        UnityEngine.Debug.Log($"[SNOW_SURFACE] cell_logic_active={cellLogicActive.ToString().ToLower()} cell_renderer_visible={cellRendererVisible.ToString().ToLower()} snow_surface_active={snowSurfaceActive.ToString().ToLower()} snow_surface_bounds_size=({surfaceSize.x:F3},{surfaceSize.y:F3},{surfaceSize.z:F3}) snow_surface_matches_snow_logic={surfaceMatchesLogic.ToString().ToLower()} visible_snow_style={visibleStyle}");
        SnowLoopLogCapture.AppendToAssiReport($"=== SNOW_SURFACE === cell_logic_active={cellLogicActive} cell_renderer_visible={cellRendererVisible} snow_surface_active={snowSurfaceActive} visible_snow_style={visibleStyle}");
    }
}
