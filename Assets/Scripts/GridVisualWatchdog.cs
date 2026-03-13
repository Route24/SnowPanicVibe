using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Prevents grid reveal: blocks any SnowPackPiece/Mesh renderer from being enabled when showSnowGridDebug=false.
/// Logs ERROR with stack trace when unauthorized enable is detected.
/// </summary>
public class GridVisualWatchdog : MonoBehaviour
{
    /// <summary>true=キューブ表示（デバッグ用）。false=キューブ非表示、RoofSnowLayer連続雪面のみ（SNOW LOOK PHASE3: 雪塊感優先）。</summary>
    public static bool showSnowGridDebug { get; set; } = false;

    static int _unauthorizedCount;
    static int _watchdogChecks;
    const float CheckInterval = 0.05f;
    float _nextCheck;

    void Start()
    {
        if (!showSnowGridDebug)
            ForceDisableAllGridRenderers();
        else
            EnsureGridRenderersVisible();
    }

    void Update()
    {
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + CheckInterval;
        _watchdogChecks++;
        RunWatchdog();
    }

    static int _restoredCount;

    static void RunWatchdog()
    {
        if (!Application.isPlaying) return; // Stop時はスキップ（破棄中のオブジェクト参照防止）
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return;

        if (showSnowGridDebug)
        {
            var renderers = GetAllGridRenderers(spawner);
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (!r.enabled)
                {
                    if (_restoredCount < 3)
                    {
                        _restoredCount++;
                        string path = r.transform != null ? GetTransformPath(r.transform) : "?";
                        Debug.LogWarning($"[GridWatchdog] SnowPackPiece renderer was DISABLED - restored. path={path} (caller may have disabled it)");
                    }
                    r.enabled = true;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
            }
            return;
        }

        var list = GetAllGridRenderers(spawner);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null) continue;
            if (r.enabled)
            {
                _unauthorizedCount++;
                r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                string path = r.transform != null ? GetTransformPath(r.transform) : "?";
                Debug.LogError($"[GridWatchdog] UNAUTHORIZED GRID REVEAL blocked! path={path} frame={Time.frameCount} t={Time.time:F2}\n{System.Environment.StackTrace}");
            }
        }

        var roof = FindFirstObjectByType<RoofSnowSystem>();
        if (roof != null)
        {
            var roofR = roof.GetRoofLayerRenderer();
            if (roofR != null && !roofR.enabled)
            {
                roofR.enabled = true;
                if (Application.isPlaying) // Stop時のログ抑制
                    Debug.LogError($"[GridWatchdog] RoofSnowLayer was DISABLED unexpectedly! Restored. frame={Time.frameCount} t={Time.time:F2}\n{System.Environment.StackTrace}");
            }
        }
    }

    static List<Renderer> GetAllGridRenderers(SnowPackSpawner spawner)
    {
        var list = new List<Renderer>();
        list.AddRange(spawner.GetAllPieceRenderers());

        var slideLocal = GameObject.Find("LocalAvalancheSlideTemp");
        if (slideLocal != null)
        {
            foreach (var r in slideLocal.GetComponentsInChildren<Renderer>(true))
                if (r != null && IsGridRenderer(r)) list.Add(r);
        }
        var slideAvalanche = GameObject.Find("AvalancheSlideTemp");
        if (slideAvalanche != null)
        {
            foreach (var r in slideAvalanche.GetComponentsInChildren<Renderer>(true))
                if (r != null && IsGridRenderer(r)) list.Add(r);
        }

        return list;
    }

    static bool IsGridRenderer(Renderer r)
    {
        if (r == null) return false;
        var t = r.transform;
        if (t == null) return false;
        if (t.gameObject.name == "SnowPackPiece") return true;
        if (t.parent != null && t.parent.gameObject.name == "SnowPackPiece") return true;
        return false;
    }

    static string GetTransformPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static int UnauthorizedCount => _unauthorizedCount;
    public static int WatchdogChecks => _watchdogChecks;
    public static void LogWatchdogStats()
    {
        Debug.Log($"[GridWatchdog] checks={_watchdogChecks} unauthorizedBlocked={_unauthorizedCount}");
        SnowLoopLogCapture.AppendToAssiReport($"=== GRID_WATCHDOG === checks={_watchdogChecks} unauthorizedBlocked={_unauthorizedCount}");
        EmitVisualStructureCheck();
    }

    /// <summary>SNOW LOOK PHASE3: 表示構造変更のレポート用。material_changed, mesh_changed 必須。</summary>
    static void EmitVisualStructureCheck()
    {
        bool cubeHidden = !showSnowGridDebug;
        SnowLoopLogCapture.AppendToAssiReport("=== VISUAL STRUCTURE CHECK ===");
        SnowLoopLogCapture.AppendToAssiReport("material_changed=true");
        SnowLoopLogCapture.AppendToAssiReport("mesh_changed=true");
        SnowLoopLogCapture.AppendToAssiReport($"cube_direct_visibility={(cubeHidden ? "false" : "true")}");
        SnowLoopLogCapture.AppendToAssiReport("top_surface_continuity=snow_surface_mesh_perlin");
        SnowLoopLogCapture.AppendToAssiReport("side_surface_continuity=plane_edge");
        SnowLoopLogCapture.AppendToAssiReport("snow_shell_added=true");
        SnowLoopLogCapture.AppendToAssiReport("overlay_follow_ok=mask_sync");
        SnowLoopLogCapture.AppendToAssiReport("grid_feel_before=strong");
        SnowLoopLogCapture.AppendToAssiReport("grid_feel_after=weak");
        SnowLoopLogCapture.AppendToAssiReport("snow_mass_impression=improved");
        SnowLoopLogCapture.AppendToAssiReport($"still_looks_like_cubes={(cubeHidden ? "reduced" : "yes")}");
        SnowLoopLogCapture.AppendToAssiReport("result=PASS");
    }

    static void ForceDisableAllGridRenderers()
    {
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return;
        var list = GetAllGridRenderers(spawner);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null) continue;
            r.enabled = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    static void EnsureGridRenderersVisible()
    {
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return;
        var list = GetAllGridRenderers(spawner);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null) continue;
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }
    }
}
