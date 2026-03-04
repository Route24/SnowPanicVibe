using UnityEngine;

/// <summary>画面上のDebug UIボタン。macOSショートカット回避。F8非依存。</summary>
public class AssiDebugUI : MonoBehaviour
{
    void Start()
    {
        ClearTapMarkerState();
    }

    static void ClearTapMarkerState()
    {
        SnowPackSpawner.LastTapTime = -10f;
        SnowPackSpawner.LastRemovedCount = 0;
        SnowPackSpawner.LastPackedInRadiusBefore = 0;
        var g = GameObject.Find("TapHitGizmo");
        if (g != null) Object.Destroy(g);
        g = GameObject.Find("BurstMarker");
        if (g != null) Object.Destroy(g);
        Debug.Log("[TapMarkerState] atStart visible=No lastTapValid=No cleared");
    }
    const float ButtonW = 180f;
    const float ButtonH = 36f;
    const float Margin = 8f;
    const float Pad = 12f;
    const float HudX = 12f;
    const float HudY = 240f;

    static bool _snowFallRateReduced;
    static bool _autoAvalancheOff = true;
    static float _originalSpawnInterval = -1f;

    void OnGUI()
    {
        float x = Pad;
        float y = Pad;

        if (Button(x, y, "[BTN] TriggerAvalanche"))
        {
            var roof = Object.FindFirstObjectByType<RoofSnowSystem>();
            if (roof != null && roof.isActiveAndEnabled)
            {
                roof.ForceAvalancheNow();
                Debug.Log("[AssiDebugUI] TriggerAvalanche pressed");
            }
            else
                Debug.LogWarning("[AssiDebugUI] RoofSnowSystem not found");
        }
        y += ButtonH + Margin;

        if (Button(x, y, "[BTN] AddSnow"))
        {
            var roof = Object.FindFirstObjectByType<RoofSnowSystem>();
            if (roof != null)
            {
                roof.AddRoofSnow(0.08f);
                Debug.Log("[AssiDebugUI] AddSnow +0.08m pressed");
            }
        }
        y += ButtonH + Margin;

        if (Button(x, y, "[BTN] StopSnow"))
        {
            var fall = Object.FindFirstObjectByType<SnowFallSystem>();
            if (fall != null)
            {
                fall.enabled = !fall.enabled;
                Debug.Log($"[AssiDebugUI] StopSnow: SnowFallSystem.enabled={fall.enabled}");
            }
        }
        y += ButtonH + Margin;

        // 5) 自動ループ抑制トグル
        if (Button(x, y, _snowFallRateReduced ? "[BTN] SnowFallRate x1 (復帰)" : "[BTN] SnowFallRate x0.1"))
        {
            var fall = Object.FindFirstObjectByType<SnowFallSystem>();
            if (fall != null)
            {
                _snowFallRateReduced = !_snowFallRateReduced;
                if (_originalSpawnInterval < 0f) _originalSpawnInterval = fall.spawnIntervalSeconds;
                fall.spawnIntervalSeconds = _snowFallRateReduced ? _originalSpawnInterval * 10f : _originalSpawnInterval;
                Debug.Log($"[AssiDebugUI] SnowFallRate {( _snowFallRateReduced ? "x0.1" : "x1")} interval={fall.spawnIntervalSeconds:F3}");
            }
        }
        y += ButtonH + Margin;

        if (Button(x, y, _autoAvalancheOff ? "[BTN] AutoAvalanche ON" : "[BTN] AutoAvalanche OFF"))
        {
            _autoAvalancheOff = !_autoAvalancheOff;
            Debug.Log($"[AssiDebugUI] AutoAvalanche {(_autoAvalancheOff ? "OFF" : "ON")}");
        }
        y += ButtonH + Margin;

        if (Button(x, y, _freezeSpawn ? "[BTN] FreezeSpawn OFF" : "[BTN] FreezeSpawn ON"))
        {
            _freezeSpawn = !_freezeSpawn;
            Debug.Log($"[AssiDebugUI] FreezeSpawn={_freezeSpawn}");
        }
        y += ButtonH + Margin;

        if (Button(x, y, "[BTN] ResetRoof"))
        {
            var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
            if (spawner != null) { spawner.ClearNow(); spawner.Rebuild(); Debug.Log("[AssiDebugUI] ResetRoof: Clear+Rebuild"); }
        }
        y += ButtonH + Margin;

        if (Button(x, y, _showGridGizmos ? "[BTN] ShowGridGizmos OFF" : "[BTN] ShowGridGizmos ON"))
        {
            _showGridGizmos = !_showGridGizmos;
            Debug.Log($"[AssiDebugUI] ShowGridGizmos={_showGridGizmos}");
        }
        y += ButtonH + Margin;

        if (Button(x, y, DebugSnowVisibility.ShowOnlyPieces ? "[BTN] ShowOnlyPieces OFF" : "[BTN] ShowOnlyPieces ON"))
        {
            DebugSnowVisibility.ShowOnlyPieces = !DebugSnowVisibility.ShowOnlyPieces;
            if (DebugSnowVisibility.ShowOnlyPieces) { DebugSnowVisibility.ShowOnlyRoofLayer = false; DebugSnowVisibility.ShowOnlyGroundVisual = false; }
            Debug.Log($"[AssiDebugUI] ShowOnlyPieces={DebugSnowVisibility.ShowOnlyPieces}");
        }
        y += ButtonH + Margin;

        if (Button(x, y, DebugSnowVisibility.ShowOnlyRoofLayer ? "[BTN] ShowOnlyRoofLayer OFF" : "[BTN] ShowOnlyRoofLayer ON"))
        {
            DebugSnowVisibility.ShowOnlyRoofLayer = !DebugSnowVisibility.ShowOnlyRoofLayer;
            if (DebugSnowVisibility.ShowOnlyRoofLayer) { DebugSnowVisibility.ShowOnlyPieces = false; DebugSnowVisibility.ShowOnlyGroundVisual = false; }
            Debug.Log($"[AssiDebugUI] ShowOnlyRoofLayer={DebugSnowVisibility.ShowOnlyRoofLayer}");
        }
        y += ButtonH + Margin;

        if (Button(x, y, DebugSnowVisibility.ShowOnlyGroundVisual ? "[BTN] ShowOnlyGroundVisual OFF" : "[BTN] ShowOnlyGroundVisual ON"))
        {
            DebugSnowVisibility.ShowOnlyGroundVisual = !DebugSnowVisibility.ShowOnlyGroundVisual;
            if (DebugSnowVisibility.ShowOnlyGroundVisual) { DebugSnowVisibility.ShowOnlyPieces = false; DebugSnowVisibility.ShowOnlyRoofLayer = false; }
            Debug.Log($"[AssiDebugUI] ShowOnlyGroundVisual={DebugSnowVisibility.ShowOnlyGroundVisual}");
        }
        y += ButtonH + Margin;

        if (Button(x, y, DebugSnowVisibility.DebugNonSymMesh ? "[BTN] DebugNonSymMesh OFF" : "[BTN] DebugNonSymMesh ON"))
        {
            DebugSnowVisibility.DebugNonSymMesh = !DebugSnowVisibility.DebugNonSymMesh;
            var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
            if (spawner != null) spawner.RefreshPieceMeshesForDebug();
            Debug.Log($"[AssiDebugUI] DebugNonSymMesh={DebugSnowVisibility.DebugNonSymMesh}");
        }

        DrawHud();
    }

    static bool _freezeSpawn;
    static bool _showGridGizmos;

    void DrawHud()
    {
        var roof = Object.FindFirstObjectByType<RoofSnowSystem>();
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        var fall = Object.FindFirstObjectByType<SnowFallSystem>();
        float depth = roof != null ? roof.roofSnowDepthMeters : 0f;
        int packedTotal = spawner != null ? spawner.GetPackedCubeCountRealtime() : 0;
        int packedInRadius = SnowPackSpawner.LastPackedInRadiusBefore;
        int removedLast = SnowPackSpawner.LastRemovedCount;
        float slopeDeg = roof != null ? roof.AngleDeg : 0f;
        float rate = fall != null ? fall.spawnIntervalSeconds : 0f;
        var uv = SnowPackSpawner.LastTapRoofLocal;
        var downhill = spawner != null ? spawner.RoofDownhill : Vector3.zero;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 10 };
        float yy = HudY;
        GUI.Label(new Rect(HudX, yy, 450, 18), $"PackedTotal={packedTotal} PackedInRadius={packedInRadius} RemovedLastTap={removedLast}", style); yy += 14;
        GUI.Label(new Rect(HudX, yy, 450, 18), $"RoofSlopeDeg={slopeDeg:F1} downhill=({downhill.x:F2},{downhill.y:F2},{downhill.z:F2}) tap(u,v)=({uv.x:F2},{uv.y:F2})", style); yy += 14;
        GUI.Label(new Rect(HudX, yy, 450, 18), $"Depth={depth:F3} AutoAvalanche={(_autoAvalancheOff ? "OFF" : "ON")} SnowFallRate={rate:F3} Freeze={(spawner != null && spawner.IsSpawnFrozen)} DebugFreeze={DebugFreezeSpawn}", style);
        if (SnowPackSpawner.LastRemovedCount > 0 && SnowPackSpawner.LastTapTime > 0)
        {
            yy += 14;
            GUI.Label(new Rect(HudX, yy, 450, 18), $"LastTap: Before={SnowPackSpawner.LastPackedTotalBefore} After={SnowPackSpawner.LastPackedTotalAfter}", style);
        }
    }

    bool Button(float x, float y, string label)
    {
        return GUI.Button(new Rect(x, y, ButtonW, ButtonH), label);
    }

    /// <summary>自動雪崩をOFFにするか。RoofSnowSystem.Updateで参照。</summary>
    public static bool AutoAvalancheOff => _autoAvalancheOff;
    /// <summary>デバッグ用: Spawn/MinFill/Sync追加を手動停止。</summary>
    public static bool DebugFreezeSpawn => _freezeSpawn;
    /// <summary>デバッグ用: グリッドGizmos表示。</summary>
    public static bool ShowGridGizmos => _showGridGizmos;
}
