using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// AI開発パイプライン用 共通診断モジュール。
/// Capture / Report を統合し、今後の調査で再利用する。
/// </summary>
public static class DebugDiagnostics
{
    /// <summary>Game View をキャプチャ。DebugScreenshotCapture に委譲。</summary>
    public static void CaptureGameView()
    {
        try { DebugScreenshotCapture.CaptureGameView(true, false); } catch (Exception ex) { Debug.LogWarning($"[DebugDiagnostics] CaptureGameView: {ex.Message}"); }
    }

    /// <summary>Console / Inspector は Editor 専用。ランタイムでは何もしない。</summary>
    public static void CaptureConsole() { /* Editor の DebugScreenshotEditor が担当 */ }

    /// <summary>Inspector キャプチャ。Editor 専用。</summary>
    public static void CaptureInspector() { /* Editor が担当 */ }

    /// <summary>テスト結果を ASSI Report に出力。</summary>
    public static void ReportTestResult(string testId, string expected, string result, params (string key, string value)[] values)
    {
        try
        {
            SnowLoopLogCapture.AppendToAssiReport($"=== TEST RESULT [{testId}] ===");
            SnowLoopLogCapture.AppendToAssiReport($"expected: {expected}");
            SnowLoopLogCapture.AppendToAssiReport($"result: {result}");
            foreach (var (k, v) in values) SnowLoopLogCapture.AppendToAssiReport($"value_{k}={v}");
        }
        catch (Exception ex) { Debug.LogWarning($"[DebugDiagnostics] ReportTestResult: {ex.Message}"); }
    }

    /// <summary>シーン状態を ASSI Report に出力。</summary>
    public static void ReportSceneInfo(string sceneName, int rootObjectCount, int activeSnowPieces, int scoreValue)
    {
        try
        {
            SnowLoopLogCapture.AppendToAssiReport("=== SCENE STATE ===");
            SnowLoopLogCapture.AppendToAssiReport($"scene_name={sceneName}");
            SnowLoopLogCapture.AppendToAssiReport($"root_object_count={rootObjectCount}");
            SnowLoopLogCapture.AppendToAssiReport($"active_snow_pieces={activeSnowPieces}");
            SnowLoopLogCapture.AppendToAssiReport($"score_value={scoreValue}");
        }
        catch (Exception ex) { Debug.LogWarning($"[DebugDiagnostics] ReportSceneInfo: {ex.Message}"); }
    }

    /// <summary>カメラ情報を ASSI Report に出力。</summary>
    public static void ReportCameraInfo(Vector3 pos, Vector3 euler)
    {
        try
        {
            SnowLoopLogCapture.AppendToAssiReport("=== CAMERA STATE ===");
            SnowLoopLogCapture.AppendToAssiReport($"camPos=({pos.x:F3},{pos.y:F3},{pos.z:F3})");
            SnowLoopLogCapture.AppendToAssiReport($"camEuler=({euler.x:F3},{euler.y:F3},{euler.z:F3})");
        }
        catch (Exception ex) { Debug.LogWarning($"[DebugDiagnostics] ReportCameraInfo: {ex.Message}"); }
    }

    /// <summary>active_snow_pieces を取得。SnowPackPiecesRoot の有効 Renderer 数をカウント。</summary>
    public static int GetActiveSnowPiecesCount()
    {
        var root = GameObject.Find("SnowPackPiecesRoot");
        if (root == null) return -1;
        var rnds = root.GetComponentsInChildren<Renderer>(true);
        int n = 0;
        for (int i = 0; i < rnds.Length; i++)
            if (rnds[i] != null && rnds[i].enabled && rnds[i].gameObject.activeInHierarchy) n++;
        return n;
    }

    /// <summary>ルートオブジェクト数を取得。</summary>
    public static int GetRootObjectCount()
    {
        try { return SceneManager.GetActiveScene().rootCount; } catch { return -1; }
    }
}
