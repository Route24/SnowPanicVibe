using UnityEngine;

/// <summary>見えている雪の正体を切り分けるデバッグトグル。ShowOnlyPieces/RoofLayer/GroundVisual</summary>
public class DebugSnowVisibility : MonoBehaviour
{
    public static bool ShowOnlyPieces { get; set; }
    public static bool ShowOnlyRoofLayer { get; set; }
    public static bool ShowOnlyGroundVisual { get; set; }
    /// <summary>When OFF (default): SnowPackPiece grid hidden. RoofSnowLayer is visual surface. When ON: grid visible for debug. Alias for GridVisualWatchdog.showSnowGridDebug.</summary>
    public static bool ShowSnowGrid { get => GridVisualWatchdog.showSnowGridDebug; set => GridVisualWatchdog.showSnowGridDebug = value; }

    public static bool DebugNonSymMesh { get; set; }

    Renderer[] _cachedPieceRenderers;
    Renderer _cachedRoofLayerRenderer;
    Renderer _cachedGroundLayerRenderer;
    float _nextCacheTime;
    const float CacheInterval = 0.5f;

    void Update()
    {
        if (Time.time >= _nextCacheTime)
        {
            _nextCacheTime = Time.time + CacheInterval;
            CacheRenderers();
        }
        ApplyVisibility();
    }

    void CacheRenderers()
    {
        _cachedPieceRenderers = null;
        _cachedRoofLayerRenderer = null;
        _cachedGroundLayerRenderer = null;

        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null)
        {
            var list = spawner.GetAllPieceRenderers();
            _cachedPieceRenderers = list != null && list.Count > 0 ? list.ToArray() : null;
        }

        var roof = FindFirstObjectByType<RoofSnowSystem>();
        if (roof != null)
            _cachedRoofLayerRenderer = roof.GetRoofLayerRenderer();

        var ground = FindFirstObjectByType<GroundSnowSystem>();
        if (ground != null)
            _cachedGroundLayerRenderer = ground.GetGroundLayerRenderer();
    }

    void ApplyVisibility()
    {
        bool onlyPieces = ShowOnlyPieces && !ShowOnlyRoofLayer && !ShowOnlyGroundVisual;
        bool onlyRoof = ShowOnlyRoofLayer && !ShowOnlyPieces && !ShowOnlyGroundVisual;
        bool onlyGround = ShowOnlyGroundVisual && !ShowOnlyPieces && !ShowOnlyRoofLayer;
        bool anyOverride = onlyPieces || onlyRoof || onlyGround;

        if (_cachedPieceRenderers != null)
        {
            bool show = GridVisualWatchdog.showSnowGridDebug && (!anyOverride || onlyPieces);
            foreach (var r in _cachedPieceRenderers)
            {
                if (r != null) r.enabled = show;
            }
        }
        if (_cachedRoofLayerRenderer != null)
        {
            bool show = !anyOverride || onlyRoof;
            _cachedRoofLayerRenderer.enabled = show;
        }
        if (_cachedGroundLayerRenderer != null)
        {
            bool show = !anyOverride || onlyGround;
            _cachedGroundLayerRenderer.enabled = show;
        }
    }

    /// <summary>レポート用: 現在の可視化状態</summary>
    public static void LogSceneObjectsVisible()
    {
        bool pieces = true;
        bool roofLayer = true;
        bool groundVisual = true;

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null)
        {
            var list = spawner.GetAllPieceRenderers();
            pieces = list != null && list.Count > 0 && list[0] != null && list[0].enabled;
        }
        var roof = Object.FindFirstObjectByType<RoofSnowSystem>();
        if (roof != null)
        {
            var r = roof.GetRoofLayerRenderer();
            roofLayer = r != null && r.enabled;
        }
        var ground = Object.FindFirstObjectByType<GroundSnowSystem>();
        if (ground != null)
        {
            var r = ground.GetGroundLayerRenderer();
            groundVisual = r != null && r.enabled;
        }

        string source = "All";
        if (ShowOnlyPieces && !ShowOnlyRoofLayer && !ShowOnlyGroundVisual) source = "Pieces";
        else if (ShowOnlyRoofLayer && !ShowOnlyPieces && !ShowOnlyGroundVisual) source = "RoofLayer";
        else if (ShowOnlyGroundVisual && !ShowOnlyPieces && !ShowOnlyRoofLayer) source = "Ground";

        Debug.Log($"[SceneObjectsVisible] showSnowGridDebug={GridVisualWatchdog.showSnowGridDebug} pieces={(pieces ? "ON" : "OFF")} roofLayer={(roofLayer ? "ON" : "OFF")} groundVisual={(groundVisual ? "ON" : "OFF")}");
        Debug.Log($"[VisibleSnowSource] {source}");
        Debug.Log($"[NonSymMesh] {(DebugNonSymMesh ? "ON" : "OFF")}");
    }

    static bool _rotationOverrideExecutedLogged;
    /// <summary>identity上書きが実行されたときに呼ぶ。2秒診断でNoneを出すため、未実行時はEmitAssiRequired4Blocksから呼ばれる。</summary>
    public static void LogRotationOverrideExecuted(string file, int line, string obj)
    {
        _rotationOverrideExecutedLogged = true;
        Debug.Log($"[RotationOverridesExecuted] file={file} line={line} obj={obj} reason=Quaternion.identity");
    }

    public static void EmitRotationOverridesExecutedIfNone()
    {
        if (!_rotationOverrideExecutedLogged)
            Debug.Log("[RotationOverridesExecuted] None");
    }
}
