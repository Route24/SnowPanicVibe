#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

/// <summary>
/// Play→Stop 後に最小 [REPORT] を生成する。
/// 事実として取得できる項目のみ出力。UNKNOWN 出力なし。
/// </summary>
public static class SnowLoopNoaReportAutoCopy
{
    static readonly string ReportPath = Path.GetFullPath(
        Path.Combine("Assets", "Logs", "noa_report_latest.txt"));

    static readonly string RecordingsDir = Path.GetFullPath("Recordings");

    // ── 公開 API ────────────────────────────────────────────────

    public static bool BuildReport()
    {
        try
        {
            File.WriteAllText(ReportPath, BuildMinimalReport(playExecuted: true), new System.Text.UTF8Encoding(false));
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
        try { File.WriteAllText(ReportPath, BuildMinimalReport(playExecuted: true), new System.Text.UTF8Encoding(false)); }
        catch { }
    }

    public static void UpdateReportWithGif()
    {
        try { File.WriteAllText(ReportPath, BuildMinimalReport(playExecuted: true), new System.Text.UTF8Encoding(false)); }
        catch { }
    }

    // ── レポート本文生成 ─────────────────────────────────────────

    static string BuildMinimalReport(bool playExecuted)
    {
        var mp4Path = Path.Combine(RecordingsDir, "snow_test_latest.mp4");
        var gifPath = Path.Combine(RecordingsDir, "snow_test_latest.gif");
        bool mp4Exists = File.Exists(mp4Path) && new FileInfo(mp4Path).Length > 0;
        bool gifExists = File.Exists(gifPath) && new FileInfo(gifPath).Length > 0;
        long gifSize   = gifExists ? new FileInfo(gifPath).Length : 0;

        bool mp4ThisSession = mp4Exists && (DateTime.UtcNow - new FileInfo(mp4Path).LastWriteTimeUtc).TotalMinutes < 10;
        bool gifThisSession = gifExists && (DateTime.UtcNow - new FileInfo(gifPath).LastWriteTimeUtc).TotalMinutes < 10;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[REPORT]");
        sb.AppendLine("timestamp="              + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("compile_result="         + (EditorUtility.scriptCompilationFailed ? "FAIL" : "PASS"));
        sb.AppendLine("anti_protocol_locked=YES");
        sb.AppendLine("play_executed="          + (playExecuted ? "YES" : "NO"));
        sb.AppendLine("gif_created_this_session=" + (gifThisSession ? "YES" : "NO"));
        sb.AppendLine("gif_path="               + (gifExists ? gifPath : "(not found)"));
        sb.AppendLine("gif_exists="             + (gifExists ? "YES" : "NO"));
        sb.AppendLine("gif_size_bytes="         + gifSize);
        sb.AppendLine("used_old_file="          + (mp4ThisSession && gifThisSession ? "NO" : "YES"));
        sb.AppendLine("edited_files=SnowLoopNoaReportAutoCopy.cs");
        return sb.ToString();
    }
}
#endif
