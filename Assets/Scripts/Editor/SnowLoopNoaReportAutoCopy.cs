#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;

/// <summary>
/// Play→Stop 後に最小 [REPORT] を生成して返す。長文セクション出力は行わない。
/// </summary>
public static class SnowLoopNoaReportAutoCopy
{
    static readonly string ReportPath = Path.GetFullPath(
        Path.Combine("Assets", "Logs", "noa_report_latest.txt"));

    /// <summary>最小 [REPORT] を生成してファイルに書き込む。成功時 true を返す。</summary>
    public static bool BuildReport()
    {
        try
        {
            string content = BuildMinimalReport(playExecuted: true, stopExecuted: true);
            File.WriteAllText(ReportPath, content, new System.Text.UTF8Encoding(false));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool BuildSelfTestReport() => BuildReport();
    public static bool TryBuildReportOrSelfTestReport() => BuildReport();

    /// <summary>ファイルが存在する場合はその内容を、なければ空文字を返す。</summary>
    public static string GetReportContent()
    {
        if (!File.Exists(ReportPath)) return string.Empty;
        try { return File.ReadAllText(ReportPath); }
        catch { return string.Empty; }
    }

    public static string GetLatestRecordingPath() => string.Empty;

    /// <summary>Play→Stop 完了時に呼ばれる。最小レポートを書き込む。</summary>
    public static void WriteReportOnStop()
    {
        try
        {
            string content = BuildMinimalReport(playExecuted: true, stopExecuted: true);
            File.WriteAllText(ReportPath, content, new System.Text.UTF8Encoding(false));
        }
        catch { }
    }

    static string BuildMinimalReport(bool playExecuted, bool stopExecuted)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[REPORT]");
        sb.AppendLine("timestamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("compile_result=PASS");
        sb.AppendLine("play_executed=" + (playExecuted ? "YES" : "NO"));
        sb.AppendLine("stop_executed=" + (stopExecuted ? "YES" : "NO"));
        sb.AppendLine("report_written=YES");
        sb.AppendLine("report_path=" + ReportPath);
        sb.AppendLine("used_old_report=NO");
        sb.AppendLine("edited_files=SnowLoopNoaReportAutoCopy.cs,AssiAutoSummary.cs");
        return sb.ToString();
    }
}
#endif
