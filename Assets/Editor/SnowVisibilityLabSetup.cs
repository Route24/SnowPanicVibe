#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// SnowVisibilityLab に RoofSnowSystem + SnowPackSpawner + Gate2SnowChecker を配置する。
/// メニュー: SnowPanic/Setup SnowVisibilityLab Snow Systems
/// </summary>
public static class SnowVisibilityLabSetup
{
    [MenuItem("SnowPanic/Setup SnowVisibilityLab Snow Systems", false, 3)]
    public static void Setup()
    {
        // ── RoofQuad を探す ──────────────────────────────────
        var roofQuadGo = GameObject.Find("RoofQuad");
        if (roofQuadGo == null)
        {
            EditorUtility.DisplayDialog("Setup", "RoofQuad が見つかりません。\nSnowVisibilityLab シーンを開いてから実行してください。", "OK");
            return;
        }

        // RoofQuad に BoxCollider を追加（なければ）
        var boxCol = roofQuadGo.GetComponent<BoxCollider>();
        if (boxCol == null)
        {
            boxCol = roofQuadGo.AddComponent<BoxCollider>();
            boxCol.size   = new Vector3(1f, 0.05f, 1f);
            boxCol.center = Vector3.zero;
            EditorUtility.SetDirty(roofQuadGo);
            Debug.Log("[SnowVisibilityLabSetup] BoxCollider added to RoofQuad");
        }

        // ── SnowTest GO を探す or 作成 ───────────────────────
        var snowTestGo = GameObject.Find("SnowTest");
        if (snowTestGo == null)
        {
            snowTestGo = new GameObject("SnowTest");
            Debug.Log("[SnowVisibilityLabSetup] Created SnowTest GO");
        }

        // ── RoofSnowSystem ────────────────────────────────────
        var rss = snowTestGo.GetComponent<RoofSnowSystem>();
        if (rss == null) rss = snowTestGo.AddComponent<RoofSnowSystem>();
        rss.roofSlideCollider    = boxCol;
        rss.heightmap_mode_enabled = true;
        rss.roofSnowDepthMeters  = 0.5f;
        EditorUtility.SetDirty(rss);
        Debug.Log("[SnowVisibilityLabSetup] RoofSnowSystem configured");

        // ── SnowPackSpawner ───────────────────────────────────
        var sps = snowTestGo.GetComponent<SnowPackSpawner>();
        if (sps == null) sps = snowTestGo.AddComponent<SnowPackSpawner>();
        sps.roofCollider = boxCol;

        // roofSnowSystem フィールドを Reflection で設定
        var f = typeof(SnowPackSpawner).GetField(
            "roofSnowSystem",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (f != null) f.SetValue(sps, rss);

        EditorUtility.SetDirty(sps);
        Debug.Log("[SnowVisibilityLabSetup] SnowPackSpawner configured");

        // RoofSnowSystem の snowPackSpawner も接続
        rss.snowPackSpawner = sps;
        EditorUtility.SetDirty(rss);

        // ── Gate2SnowChecker ──────────────────────────────────
        if (snowTestGo.GetComponent<Gate2SnowChecker>() == null)
        {
            snowTestGo.AddComponent<Gate2SnowChecker>();
            Debug.Log("[SnowVisibilityLabSetup] Gate2SnowChecker added");
        }

        // ── シーン保存 ─────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log("[SnowVisibilityLabSetup] DONE. Ctrl+S → Play → Console で [GATE2_SNOW_CHECK] を確認してください。");
        EditorUtility.DisplayDialog("Setup Complete",
            "SnowTest GO に以下を配置しました:\n" +
            "  ・RoofSnowSystem\n" +
            "  ・SnowPackSpawner\n" +
            "  ・Gate2SnowChecker\n\n" +
            "Ctrl+S でシーン保存 → Play → Console の [GATE2_SNOW_CHECK] を確認してください。",
            "OK");
    }
}
#endif
