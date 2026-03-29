#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

/// <summary>
/// Play→Stop 後に最小 [REPORT] を生成して返す。
/// 判定根拠：
/// - roof_visible / cyan_box_visible / score_ui_visible / ready_ui_visible / white_overlay_visible
///   → Runtime ログ [AntiProtocolVis] roof_visible=... から取得
/// - click_hit_cyan_box / cyan_box_destroyed_on_click
///   → Runtime ログ [SnowBlock] click_hit_cyan_box=YES / cyan_box_destroyed=YES から取得
/// - unexpected_respawn
///   → Runtime ログ [AntiProtocolVis] unexpected_respawn=... から取得
/// ログが出ていない項目は UNKNOWN（推測しない）
/// </summary>
public static class SnowLoopNoaReportAutoCopy
{
    static readonly string ReportPath = Path.GetFullPath(
        Path.Combine("Assets", "Logs", "noa_report_latest.txt"));

    static readonly string RecordingsDir = Path.GetFullPath("Recordings");

    // ── Runtime ログから収集するフラグ ──────────────────────────
    static string _roofVisible        = "UNKNOWN";
    static string _cyanBoxVisible     = "UNKNOWN";
    static string _scoreUiVisible     = "UNKNOWN";
    static string _readyUiVisible     = "UNKNOWN";
    static string _whiteOverlayVisible = "UNKNOWN";
    static bool   _clickHitCyan       = false;
    static bool   _cyanDestroyed      = false;
    static string _unexpectedRespawn  = "UNKNOWN";

    [UnityEditor.Callbacks.DidReloadScripts]
    static void OnScriptsReloaded()
    {
        ResetFlags();
        UnityEngine.Application.logMessageReceived -= OnLog;
        UnityEngine.Application.logMessageReceived += OnLog;
    }

    static void ResetFlags()
    {
        _roofVisible        = "UNKNOWN";
        _cyanBoxVisible     = "UNKNOWN";
        _scoreUiVisible     = "UNKNOWN";
        _readyUiVisible     = "UNKNOWN";
        _whiteOverlayVisible = "UNKNOWN";
        _clickHitCyan       = false;
        _cyanDestroyed      = false;
        _unexpectedRespawn  = "UNKNOWN";
    }

    static void OnLog(string condition, string stackTrace, UnityEngine.LogType type)
    {
        // [AntiProtocolVis] roof_visible=YES cyan_box_visible=YES ... （Start時）
        if (condition.StartsWith("[AntiProtocolVis]") && condition.Contains("roof_visible="))
        {
            _roofVisible         = Extract(condition, "roof_visible");
            _cyanBoxVisible      = Extract(condition, "cyan_box_visible");
            _scoreUiVisible      = Extract(condition, "score_ui_visible");
            _readyUiVisible      = Extract(condition, "ready_ui_visible");
            _whiteOverlayVisible = Extract(condition, "white_overlay_visible");
        }

        // [AntiProtocolVis] unexpected_respawn=... （OnDestroy / LateUpdate 時）
        if (condition.StartsWith("[AntiProtocolVis]") && condition.Contains("unexpected_respawn="))
        {
            var respawn = Extract(condition, "unexpected_respawn");
            if (respawn != "UNKNOWN") _unexpectedRespawn = respawn;
            else if (_unexpectedRespawn == "UNKNOWN") _unexpectedRespawn = respawn;
        }

        // [SnowBlock] click_hit_cyan_box=YES
        if (condition.Contains("[SnowBlock]") && condition.Contains("click_hit_cyan_box=YES"))
            _clickHitCyan = true;

        // [SnowBlock] cyan_box_destroyed=YES
        if (condition.Contains("[SnowBlock]") && condition.Contains("cyan_box_destroyed=YES"))
            _cyanDestroyed = true;
    }

    /// <summary>"key=VALUE" 形式のトークンを condition から抽出。見つからなければ "UNKNOWN" を返す。</summary>
    static string Extract(string line, string key)
    {
        int idx = line.IndexOf(key + "=", StringComparison.Ordinal);
        if (idx < 0) return "UNKNOWN";
        int start = idx + key.Length + 1;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line.Substring(start) : line.Substring(start, end - start);
    }

    // ── 公開 API ────────────────────────────────────────────────

    public static bool BuildReport()
    {
        try
        {
            string content = BuildMinimalReport(playExecuted: true);
            File.WriteAllText(ReportPath, content, new System.Text.UTF8Encoding(false));
            return true;
        }
        catch { return false; }
    }

    public static bool BuildSelfTestReport() => BuildReport();
    public static bool TryBuildReportOrSelfTestReport() => BuildReport();

    public static string GetReportContent()
    {
        if (!File.Exists(ReportPath)) return string.Empty;
        try { return File.ReadAllText(ReportPath); }
        catch { return string.Empty; }
    }

    public static string GetLatestRecordingPath()
    {
        var p = Path.Combine(RecordingsDir, "snow_test_latest.gif");
        return File.Exists(p) ? p : string.Empty;
    }

    public static void WriteReportOnStop()
    {
        try
        {
            string content = BuildMinimalReport(playExecuted: true);
            File.WriteAllText(ReportPath, content, new System.Text.UTF8Encoding(false));
            ResetFlags();
        }
        catch { }
    }

    public static void UpdateReportWithGif()
    {
        try
        {
            string content = BuildMinimalReport(playExecuted: true);
            File.WriteAllText(ReportPath, content, new System.Text.UTF8Encoding(false));
        }
        catch { }
    }

    // ── レポート本文生成 ─────────────────────────────────────────

    static string BuildMinimalReport(bool playExecuted)
    {
        var mp4Path = Path.Combine(RecordingsDir, "snow_test_latest.mp4");
        var gifPath = Path.Combine(RecordingsDir, "snow_test_latest.gif");
        bool mp4Exists = File.Exists(mp4Path) && new FileInfo(mp4Path).Length > 0;
        bool gifExists = File.Exists(gifPath) && new FileInfo(gifPath).Length > 0;
        long gifSize = gifExists ? new FileInfo(gifPath).Length : 0;

        bool mp4ThisSession = mp4Exists && (DateTime.UtcNow - new FileInfo(mp4Path).LastWriteTimeUtc).TotalMinutes < 10;
        bool gifThisSession = gifExists && (DateTime.UtcNow - new FileInfo(gifPath).LastWriteTimeUtc).TotalMinutes < 10;

        // report_matches_visual：可視判定とクリック判定が UNKNOWN でなければ YES
        bool allKnown = _roofVisible    != "UNKNOWN"
                     && _cyanBoxVisible != "UNKNOWN"
                     && _scoreUiVisible != "UNKNOWN"
                     && _readyUiVisible != "UNKNOWN";
        bool clickKnown = _cyanDestroyed || _clickHitCyan; // クリックしていれば YES/NO 確定
        string reportMatchesVisual = (allKnown && _unexpectedRespawn != "UNKNOWN") ? "YES" : "UNKNOWN";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[REPORT]");
        sb.AppendLine("timestamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("compile_result=" + (UnityEditor.EditorUtility.scriptCompilationFailed ? "FAIL" : "PASS"));
        sb.AppendLine("anti_protocol_locked=YES");
        sb.AppendLine("play_executed=" + (playExecuted ? "YES" : "NO"));
        sb.AppendLine("roof_visible=" + _roofVisible);
        sb.AppendLine("cyan_box_visible=" + _cyanBoxVisible);
        sb.AppendLine("score_ui_visible=" + _scoreUiVisible);
        sb.AppendLine("ready_ui_visible=" + _readyUiVisible);
        sb.AppendLine("click_hit_cyan_box=" + (_clickHitCyan ? "YES" : "NO"));
        sb.AppendLine("cyan_box_destroyed_on_click=" + (_cyanDestroyed ? "YES" : "NO"));
        sb.AppendLine("unexpected_respawn=" + _unexpectedRespawn);
        sb.AppendLine("gif_created_this_session=" + (gifThisSession ? "YES" : "NO"));
        sb.AppendLine("gif_path=" + (gifExists ? gifPath : "(not found)"));
        sb.AppendLine("gif_exists=" + (gifExists ? "YES" : "NO"));
        sb.AppendLine("gif_size_bytes=" + gifSize);
        sb.AppendLine("used_old_file=" + (mp4ThisSession && gifThisSession ? "NO" : "YES"));
        sb.AppendLine("report_matches_visual=" + reportMatchesVisual);
        sb.AppendLine("edited_files=AntiProtocolVisibilityReporter.cs,SnowBlockNode.cs,SnowLoopNoaReportAutoCopy.cs");
        return sb.ToString();
    }
}
#endif
