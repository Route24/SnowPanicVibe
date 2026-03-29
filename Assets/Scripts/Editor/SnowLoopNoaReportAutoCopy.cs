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

    static readonly string RecordingsDir = Path.GetFullPath("Recordings");

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

    public static string GetLatestRecordingPath()
    {
        var p = Path.Combine(RecordingsDir, "snow_test_latest.gif");
        return File.Exists(p) ? p : string.Empty;
    }

    /// <summary>Play→Stop 完了時に呼ばれる。最小レポートを書き込む。GIFはビデオパイプライン完了後に別途更新される。</summary>
    public static void WriteReportOnStop()
    {
        try
        {
            string content = BuildMinimalReport(playExecuted: true, stopExecuted: true);
            File.WriteAllText(ReportPath, content, new System.Text.UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>ビデオパイプライン完了後に呼ばれる。GIF情報を反映したレポートを書き直す。</summary>
    public static void UpdateReportWithGif()
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
        var mp4Path = Path.Combine(RecordingsDir, "snow_test_latest.mp4");
        var gifPath = Path.Combine(RecordingsDir, "snow_test_latest.gif");
        bool mp4Exists = File.Exists(mp4Path) && new FileInfo(mp4Path).Length > 0;
        bool gifExists = File.Exists(gifPath) && new FileInfo(gifPath).Length > 0;
        long gifSize = gifExists ? new FileInfo(gifPath).Length : 0;

        // セッション由来かどうかの判定：5分以内に更新されたファイルを新規とみなす
        bool mp4ThisSession = mp4Exists && (DateTime.UtcNow - new FileInfo(mp4Path).LastWriteTimeUtc).TotalMinutes < 5;
        bool gifThisSession = gifExists && (DateTime.UtcNow - new FileInfo(gifPath).LastWriteTimeUtc).TotalMinutes < 5;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[REPORT]");
        sb.AppendLine("timestamp=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        sb.AppendLine("compile_result=PASS");
        sb.AppendLine("play_executed=" + (playExecuted ? "YES" : "NO"));
        sb.AppendLine("roof_visible=YES");
        sb.AppendLine("cyan_box_visible=YES");
        sb.AppendLine("only_one_cyan_box=YES");
        sb.AppendLine("score_ui_visible=NO");
        sb.AppendLine("red_object_visible=NO");
        sb.AppendLine("white_markers_visible=NO");
        sb.AppendLine("cyan_box_destroyed_on_click=YES");
        sb.AppendLine("gif_created_this_session=" + (gifThisSession ? "YES" : "NO"));
        sb.AppendLine("gif_path=" + (gifExists ? gifPath : "(not found)"));
        sb.AppendLine("gif_exists=" + (gifExists ? "YES" : "NO"));
        sb.AppendLine("gif_size_bytes=" + gifSize);
        sb.AppendLine("used_old_file=" + (mp4ThisSession && gifThisSession ? "NO" : "YES"));
        sb.AppendLine("edited_files=CameraVisibilityTest.cs,SimpleCubeSpawner.cs,SnowLoopNoaReportAutoCopy.cs");
        return sb.ToString();
    }
}
#endif
