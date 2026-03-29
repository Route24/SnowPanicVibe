using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 屋根雪の表示 ON/OFF 専用トグル。
///
/// V キー: 全 RoofSnowSystem の RoofSnowLayer + SnowPackPiecesRoot の
///         Renderer を一括 ON/OFF する。
///
/// L キーは RoofCalibrationController の calibration load 専用とし、
/// SnowVisibilityChecker の ForceSpawn フローとは独立して動作する。
///
/// 影響範囲: 屋根雪 Renderer のみ。地面雪・落下・UI には触らない。
/// </summary>
public class RoofSnowVisToggle : MonoBehaviour
{
    const KeyCode TOGGLE_KEY = KeyCode.V;

    bool _visible = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "SnowCore_AntiProtocol") return;
        var go = new GameObject("RoofSnowVisToggle");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<RoofSnowVisToggle>();
        Debug.Log("[ROOF_SNOW_VIS] ready key=V visible=true (default)");
    }

    void Update()
    {
        if (!Input.GetKeyDown(TOGGLE_KEY)) return;
        _visible = !_visible;
        ApplyVisibility(_visible);
    }

    void ApplyVisibility(bool visible)
    {
        int roofCount     = 0;
        int rendererCount = 0;

        var roofSystems = Object.FindObjectsByType<RoofSnowSystem>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var sys in roofSystems)
        {
            if (sys == null) continue;
            roofCount++;

            // ── RoofSnowLayer ──────────────────────────────────
            if (sys.roofSlideCollider != null)
            {
                var layer = sys.roofSlideCollider.transform.Find("RoofSnowLayer");
                if (layer != null)
                {
                    foreach (var r in layer.GetComponentsInChildren<Renderer>(true))
                    {
                        r.enabled = visible;
                        rendererCount++;
                    }
                }
            }

            // ── SnowPackVisual / SnowPackPiecesRoot ───────────
            Transform roofT = sys.roofSlideCollider != null
                ? sys.roofSlideCollider.transform
                : sys.transform;

            var visualRoot = roofT.Find("SnowPackVisual");
            if (visualRoot == null) visualRoot = roofT.Find("SnowPackPiecesRoot");

            if (visualRoot != null)
            {
                foreach (var r in visualRoot.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = visible;
                    rendererCount++;
                }
            }
        }

        // ── SnowPackSpawner 直下の SnowPackPiecesRoot も探す ──
        var spawners = Object.FindObjectsByType<SnowPackSpawner>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var sp in spawners)
        {
            if (sp == null) continue;
            var piecesRoot = sp.transform.Find("SnowPackPiecesRoot");
            if (piecesRoot == null)
            {
                var visual = sp.transform.Find("SnowPackVisual");
                if (visual != null)
                    piecesRoot = visual.Find("SnowPackPiecesRoot");
            }
            if (piecesRoot != null)
            {
                foreach (var r in piecesRoot.GetComponentsInChildren<Renderer>(true))
                {
                    r.enabled = visible;
                    rendererCount++;
                }
            }
        }

        string visStr = visible ? "YES" : "NO";
        Debug.Log(
            $"[ROOF_SNOW_VIS] key=V visible={visStr} " +
            $"roof_count={roofCount} renderer_count={rendererCount}");
    }
}
