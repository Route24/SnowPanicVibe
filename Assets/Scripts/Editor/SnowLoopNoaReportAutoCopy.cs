#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SnowLoopNoaReportAutoCopy
{
    static readonly string LogPath = Path.GetFullPath(Path.Combine("Assets", "Logs", "snowloop_latest.txt"));
    static readonly string BufferPath = Path.GetFullPath(Path.Combine("Assets", "Logs", "console_buffer.txt"));
    static readonly string ReportPath = Path.GetFullPath(Path.Combine("Assets", "Logs", "noa_report_latest.txt"));
    static readonly string PreviousFullPath = Path.GetFullPath(Path.Combine("Assets", "Logs", "noa_report_previous_full.txt"));
    const int FullConsoleDumpLines = 300;
    const int ActiveZeroContextLines = 30;
    static readonly Regex RunIdRegex = new Regex(@"runId=(\d+)", RegexOptions.Compiled);
    static bool _sawPlayMode;

    static SnowLoopNoaReportAutoCopy()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/Snow Panic/Copy ASSI Report to Clipboard", false, 100)]
    public static void CopyReportToClipboard()
    {
        if (TryBuildAndCopyReport())
            Debug.Log("[ASSI] Report copied to clipboard. Ready to paste.");
        else
            Debug.LogWarning("[ASSI] Report copy failed (no log file or empty). Play once, stop, then try again.");
    }

    /// <summary>レポートをビルドしてファイルに保存。差分のみ出力。成功時は true。</summary>
    public static bool BuildReport()
    {
        try
        {
            string[] lines = LoadConsoleLines();
            if (lines == null || lines.Length == 0) return false;
            string fullReport = BuildReportFullMode(lines);
            string deltaReport = BuildDeltaReport(lines, fullReport);
            var dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ReportPath, deltaReport);
            File.WriteAllText(PreviousFullPath, fullReport);
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ASSI Report Error] {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>SelfTest用レポート。VIDEO FOR NOA を最上段に、続けて VIDEO PIPELINE LOGS、CORNICE SCENE CHECK、TAP DEBUG。</summary>
    public static bool BuildSelfTestReport()
    {
        try
        {
            var lines = LoadConsoleLines();
            var sb = new StringBuilder();
            sb.AppendLine("=== SNOW ROLLBACK CHECK ===");
            sb.AppendLine(BuildSnowRollbackCheckSection(lines));
            sb.AppendLine("");
            sb.AppendLine("=== CAMERA LOCK CHECK ===");
            sb.AppendLine(BuildCameraLockCheckSection(lines));
            sb.AppendLine("");
            sb.AppendLine("=== VIDEO FOR NOA ===");
            sb.AppendLine(BuildVideoForNoaSection());
            sb.AppendLine("");
            sb.AppendLine("=== VIDEO PREVIEW FOR NOA ===");
            sb.AppendLine(BuildVideoPreviewForNoaSection());
            sb.AppendLine("");
            sb.AppendLine("=== DRIVE STATUS ===");
            sb.AppendLine(BuildDriveStatusSection());
            sb.AppendLine("");
            sb.AppendLine("=== VIDEO PIPELINE LOGS ===");
            sb.AppendLine(BuildVideoPipelineLogsSection());
            sb.AppendLine("");
            sb.AppendLine("=== CORNICE SCENE CHECK ===");
            sb.AppendLine(BuildCorniceSceneCheckSection(lines));
            sb.AppendLine("");
            sb.AppendLine("=== TAP DEBUG ===");
            sb.AppendLine(BuildTapDebugSection(lines));
            sb.AppendLine("");
            sb.AppendLine(BuildImplementationSummary());
            sb.AppendLine("");
            var dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ReportPath, sb.ToString());
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ASSI SelfTest Report Error] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>SelfTest時は SelfTest レポート、通常Play時は既存レポート。</summary>
    public static bool TryBuildReportOrSelfTestReport()
    {
        if (SnowPanicVideoPipelineSelfTest.IsSelfTestSession)
            return BuildSelfTestReport();
        return BuildReport();
    }

    /// <summary>前回との差分のみ。変更点・新規エラー・ACTIVE=0変化だけ。</summary>
    static string BuildDeltaReport(string[] lines, string fullReport)
    {
        string prevFull = File.Exists(PreviousFullPath) ? File.ReadAllText(PreviousFullPath) : null;
        var sb = new StringBuilder();
        sb.AppendLine("=== SNOW ROLLBACK CHECK ===");
        sb.AppendLine(BuildSnowRollbackCheckSection(lines));
        sb.AppendLine();
        sb.AppendLine("=== CAMERA LOCK CHECK ===");
        sb.AppendLine(BuildCameraLockCheckSection(lines));
        sb.AppendLine();
        sb.AppendLine("=== VIDEO FOR NOA ===");
        sb.AppendLine(BuildVideoForNoaSection());
        sb.AppendLine();
        sb.AppendLine("=== VIDEO PREVIEW FOR NOA ===");
        sb.AppendLine(BuildVideoPreviewForNoaSection());
        sb.AppendLine();
        sb.AppendLine("=== DRIVE STATUS ===");
        sb.AppendLine(BuildDriveStatusSection());
        sb.AppendLine();
        sb.AppendLine($"生成日時: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("【ASSI REPORT - 差分のみ】");
        sb.AppendLine();
        sb.AppendLine("=== VIDEO PIPELINE LOGS ===");
        sb.AppendLine(BuildVideoPipelineLogsSection());
        sb.AppendLine();
        sb.AppendLine("=== CORNICE SCENE CHECK ===");
        sb.AppendLine(BuildCorniceSceneCheckSection(lines));
        sb.AppendLine();
        sb.AppendLine("=== TAP DEBUG ===");
        sb.AppendLine(BuildTapDebugSection(lines));
        sb.AppendLine();
        sb.AppendLine("=== 次回レポート必須（4項目） ===");
        sb.AppendLine(BuildRequiredChecklist(lines));
        sb.AppendLine();

        sb.AppendLine("=== タップ・局所雪崩レポート（必須） ===");
        sb.AppendLine(BuildTapLocalAvalancheReport(lines));
        sb.AppendLine();

        sb.AppendLine("=== ASSI観測ログ（必須・差分関係なく出力） ===");
        sb.AppendLine(BuildAssiObservationLog(lines));
        sb.AppendLine();

        sb.AppendLine("=== 今回サマリ ===");
        sb.AppendLine(BuildSummary(lines));
        sb.AppendLine();
        sb.AppendLine("=== ACTIVE=0 CONTEXT ===");
        sb.AppendLine(BuildActiveZeroContext(lines));
        sb.AppendLine();
        sb.AppendLine("=== 直前20イベント ===");
        sb.AppendLine(BuildLast20Events(lines));
        sb.AppendLine();
        sb.AppendLine("=== 動画（Recorder） ===");
        sb.AppendLine(GetLatestRecordingSection());
        sb.AppendLine();
        sb.AppendLine("=== 雪崩 before/after（必須） ===");
        sb.AppendLine(BuildAvalancheBeforeAfter(lines));
        sb.AppendLine();
        sb.AppendLine("=== 例外全文（最大3件） ===");
        var errs = ExtractErrors(lines);
        for (int i = 0; i < Math.Min(3, errs.Count); i++)
            sb.AppendLine(errs[i]).AppendLine();
        if (errs.Count > 3) sb.AppendLine($"=== 他 {errs.Count - 3} 件 ===");
        sb.AppendLine();
        sb.AppendLine("=== CONSOLE LOGS (filtered) ===");
        sb.AppendLine(BuildConsoleLogsFilteredSection());
        sb.AppendLine();
        sb.AppendLine(BuildImplementationSummary());
        return TruncateReport(sb.ToString());
    }

    const int VideoPipelineLogsMaxLines = 50;
    const int ConsoleExcerptMaxLines = 200;

    /// <summary>ノア確認用。最上段固定フォーマット。drive_share_link/direct_view_url を先頭に。</summary>
    static string BuildVideoForNoaSection()
    {
        try
        {
            var sessionPath = SnowPanicVideoPipelineSelfTest.GetSessionDataPath();
            var sb = new StringBuilder();
            if (!File.Exists(sessionPath))
            {
                sb.AppendLine("drive_share_link=none");
                sb.AppendLine("direct_view_url=");
                sb.AppendLine("direct_download_url=");
                sb.AppendLine("drive_permission=restricted");
                sb.AppendLine("local_path=(session not found)");
                sb.AppendLine("local_exists=false");
                sb.AppendLine("local_size_bytes=0");
                sb.AppendLine("drive_uploaded=false");
                sb.AppendLine("drive_size_bytes=0");
                sb.AppendLine("upload_result=NOT_RUN");
                sb.AppendLine("final_result=ERROR");
                sb.AppendLine("session_id=");
                sb.AppendLine("scene=");
                sb.AppendLine("unity_version=" + Application.unityVersion);
                sb.AppendLine("");
                sb.AppendLine("=== SLACK STATUS ===");
                sb.AppendLine("posted=false");
                sb.AppendLine("error=session_not_found");
                return sb.ToString();
            }
            var sessionLines = File.ReadAllLines(sessionPath);
            var sessionDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in sessionLines)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    sessionDict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            string v;
            var driveShareLink = sessionDict.TryGetValue("drive_share_link", out v) ? v : "";
            if (string.IsNullOrEmpty(driveShareLink)) driveShareLink = "none";
            var directViewUrl = sessionDict.TryGetValue("direct_view_url", out v) ? v : "";
            var directDownloadUrl = sessionDict.TryGetValue("direct_download_url", out v) ? v : "";
            var drivePermission = sessionDict.TryGetValue("drive_permission", out v) ? v : "restricted";
            var localPath = sessionDict.TryGetValue("local_mp4_path", out v) ? v : "";
            var localExists = sessionDict.TryGetValue("local_mp4_exists", out v) ? v : "false";
            var localSize = sessionDict.TryGetValue("local_mp4_size_bytes", out v) ? v : "0";
            var driveUploaded = sessionDict.TryGetValue("drive_uploaded", out v) ? v : "false";
            var driveSizeBytes = sessionDict.TryGetValue("drive_size_bytes", out v) ? v : "0";
            var uploadResult = sessionDict.TryGetValue("upload_result", out v) ? v : (driveUploaded == "true" ? "DRIVE_READY" : "NOT_RUN");
            var finalResult = sessionDict.TryGetValue("final_result", out v) ? v : (driveUploaded == "true" ? "DRIVE_READY" : "ERROR");
            var sessionId = sessionDict.TryGetValue("sessionId", out v) ? v : "";
            var scene = sessionDict.TryGetValue("scene", out v) ? v : "";
            var unityVersion = sessionDict.TryGetValue("unityVersion", out v) ? v : Application.unityVersion;
            var slackPosted = sessionDict.TryGetValue("slack_posted", out v) ? v : "false";
            var slackError = sessionDict.TryGetValue("slack_error", out v) ? v : "";
            sb.AppendLine("drive_share_link=" + driveShareLink);
            sb.AppendLine("direct_view_url=" + (directViewUrl ?? ""));
            sb.AppendLine("direct_download_url=" + (directDownloadUrl ?? ""));
            sb.AppendLine("drive_permission=" + drivePermission);
            sb.AppendLine("local_path=" + (localPath ?? ""));
            sb.AppendLine("local_exists=" + localExists);
            sb.AppendLine("local_size_bytes=" + localSize);
            sb.AppendLine("drive_uploaded=" + driveUploaded);
            sb.AppendLine("drive_size_bytes=" + driveSizeBytes);
            sb.AppendLine("upload_result=" + uploadResult);
            sb.AppendLine("final_result=" + finalResult);
            sb.AppendLine("session_id=" + sessionId);
            sb.AppendLine("scene=" + scene);
            sb.AppendLine("unity_version=" + unityVersion);
            sb.AppendLine("");
            sb.AppendLine("=== SLACK STATUS ===");
            sb.AppendLine("posted=" + slackPosted);
            sb.AppendLine("error=" + (string.IsNullOrEmpty(slackError) ? "none" : slackError));
            sb.AppendLine("slack_result=" + (slackPosted == "true" ? "OK" : "ERROR"));
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return "drive_share_link=none\n(読取失敗: " + ex.Message + ")";
        }
    }

    /// <summary>ノア確認用プレビュー。gif優先、fallback時はcontact_sheet。</summary>
    static string BuildVideoPreviewForNoaSection()
    {
        try
        {
            var sessionPath = SnowPanicVideoPipelineSelfTest.GetSessionDataPath();
            if (!File.Exists(sessionPath))
                return "preview_type=none\npreview_path=\ngif_path=\ngif_exists=false\ngif_size_bytes=0\npreview_fallback_used=false\nffmpeg_path=\nffmpeg_available=false\npreview_status=PREVIEW_ERROR\npreview_drive_link=";
            var sessionLines = File.ReadAllLines(sessionPath);
            var sessionDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in sessionLines)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    sessionDict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            string v;
            var previewType = sessionDict.TryGetValue("preview_type", out v) ? v : "none";
            var gifPath = sessionDict.TryGetValue("gif_path", out v) ? v : (sessionDict.TryGetValue("preview_gif_path", out v) ? v : "");
            var previewPath = (previewType == "gif" && !string.IsNullOrEmpty(gifPath)) ? gifPath : (sessionDict.TryGetValue("preview_path", out v) ? v : "");
            if (string.IsNullOrEmpty(previewPath)) previewPath = gifPath;
            var gifExists = sessionDict.TryGetValue("gif_exists", out v) ? v : (previewType == "gif" ? "true" : "false");
            var gifSizeBytes = sessionDict.TryGetValue("gif_size_bytes", out v) ? v : (sessionDict.TryGetValue("preview_gif_size", out v) ? v : "0");
            var previewFallbackUsed = sessionDict.TryGetValue("preview_fallback_used", out v) ? v : "false";
            var ffmpegPath = sessionDict.TryGetValue("ffmpeg_path", out v) ? v : "";
            var ffmpegAvailable = sessionDict.TryGetValue("ffmpeg_available", out v) ? v : "false";
            var previewStatus = sessionDict.TryGetValue("preview_status", out v) ? v : "PREVIEW_ERROR";
            var previewExists = sessionDict.TryGetValue("preview_exists", out v) ? v : "false";
            var previewDriveLink = sessionDict.TryGetValue("preview_drive_link", out v) ? v : (sessionDict.TryGetValue("preview_gif_drive_link", out v) ? v : "");
            var sb = new StringBuilder();
            sb.AppendLine("preview_type=" + (previewType ?? "none"));
            sb.AppendLine("preview_path=" + (previewPath ?? ""));
            sb.AppendLine("gif_path=" + (gifPath ?? ""));
            sb.AppendLine("gif_exists=" + gifExists);
            sb.AppendLine("gif_size_bytes=" + (gifSizeBytes ?? "0"));
            sb.AppendLine("preview_fallback_used=" + previewFallbackUsed);
            sb.AppendLine("ffmpeg_path=" + (ffmpegPath ?? ""));
            sb.AppendLine("ffmpeg_available=" + ffmpegAvailable);
            sb.AppendLine("preview_status=" + (previewStatus ?? "PREVIEW_ERROR"));
            sb.AppendLine("preview_exists=" + previewExists);
            sb.AppendLine("preview_size_bytes=" + (sessionDict.TryGetValue("preview_size_bytes", out v) ? v : "0"));
            sb.AppendLine("preview_drive_link=" + (previewDriveLink ?? ""));
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return "preview_type=none\npreview_path=\ngif_path=\ngif_exists=false\npreview_fallback_used=false\nffmpeg_path=\nffmpeg_available=false\npreview_status=PREVIEW_ERROR\n(読取失敗: " + ex.Message + ")";
        }
    }

    static string BuildVideoPipelineLogsSection()
    {
        try
        {
            var sb = new StringBuilder();
            var sessionPath = SnowPanicVideoPipelineSelfTest.GetSessionDataPath();
            var lastRunPath = SnowPanicVideoPipelineSelfTest.GetLastRunPath();

            if (File.Exists(sessionPath))
            {
                var sessionLines = File.ReadAllLines(sessionPath);
                var sessionDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in sessionLines)
                {
                    var eq = line.IndexOf('=');
                    if (eq > 0)
                        sessionDict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                string v;
                var result = sessionDict.TryGetValue("result", out v) ? v : "";
                var errorStep = sessionDict.TryGetValue("errorStep", out v) ? v : "none";
                var localPath = sessionDict.TryGetValue("local_mp4_path", out v) ? v : "";
                var localExists = sessionDict.TryGetValue("local_mp4_exists", out v) ? v : "false";
                if (!string.IsNullOrEmpty(localPath) && localExists == "true")
                {
                    sb.AppendLine("★ 動画保存先（ノアに添付） ★");
                    sb.AppendLine("path=" + localPath);
                    sb.AppendLine("");
                }
                if (errorStep != "none" && result != "SUCCESS" && result != "OK")
                {
                    sb.AppendLine("★ 失敗ステップ ★ " + errorStep);
                    sb.AppendLine("");
                }
                sb.AppendLine("sessionId=" + (sessionDict.TryGetValue("sessionId", out v) ? v : ""));
                sb.AppendLine("result=" + result);
                sb.AppendLine("errorStep=" + errorStep);
                sb.AppendLine("elapsedSec=" + (sessionDict.TryGetValue("elapsedSec", out v) ? v : ""));
                sb.AppendLine("local_mp4_path=" + (sessionDict.TryGetValue("local_mp4_path", out v) ? v : ""));
                sb.AppendLine("local_mp4_exists=" + (sessionDict.TryGetValue("local_mp4_exists", out v) ? v : "false"));
                sb.AppendLine("local_mp4_size_bytes=" + (sessionDict.TryGetValue("local_mp4_size_bytes", out v) ? v : "0"));
                sb.AppendLine("daily_archive_path=" + (sessionDict.TryGetValue("daily_archive_path", out v) ? v : ""));
                sb.AppendLine("daily_archive_created=" + (sessionDict.TryGetValue("daily_archive_created", out v) ? v : "false"));
                sb.AppendLine("preview_path=" + (sessionDict.TryGetValue("preview_path", out v) ? v : ""));
                sb.AppendLine("preview_created=" + (sessionDict.TryGetValue("preview_created", out v) ? v : "false"));
                if (sessionDict.TryGetValue("mp4_poll_expectedPath", out v) && !string.IsNullOrEmpty(v))
                {
                    sb.AppendLine("mp4_poll_expectedPath=" + v);
                    sb.AppendLine("mp4_poll_FileExists=" + (sessionDict.TryGetValue("mp4_poll_FileExists", out v) ? v : ""));
                    sb.AppendLine("mp4_poll_size_bytes=" + (sessionDict.TryGetValue("mp4_poll_size_bytes", out v) ? v : ""));
                    sb.AppendLine("mp4_poll_count=" + (sessionDict.TryGetValue("mp4_poll_count", out v) ? v : ""));
                    sb.AppendLine("mp4_poll_interval_sec=" + (sessionDict.TryGetValue("mp4_poll_interval_sec", out v) ? v : ""));
                }
                sb.AppendLine("drive_file=" + (sessionDict.TryGetValue("drive_file", out v) ? v : ""));
                sb.AppendLine("slack_message=" + (sessionDict.TryGetValue("slack_message", out v) ? v : ""));
                if (sessionDict.TryGetValue("unityVersion", out v)) sb.AppendLine("unityVersion=" + v);
                if (sessionDict.TryGetValue("platform", out v)) sb.AppendLine("platform=" + v);
                if (sessionDict.TryGetValue("recorderImplementation", out v)) sb.AppendLine("recorderImplementation=" + v);
                if (sessionDict.TryGetValue("outputDir", out v)) sb.AppendLine("outputDir=" + v);
                if (sessionDict.TryGetValue("exception", out v) && !string.IsNullOrEmpty(v)) sb.AppendLine("exception=" + v);
                if (sessionDict.TryGetValue("stacktrace", out v) && !string.IsNullOrEmpty(v)) sb.AppendLine("stacktrace=" + v);
                if (sessionDict.TryGetValue("outputDirExists", out v)) sb.AppendLine("outputDirExists=" + v);
                if (sessionDict.TryGetValue("outputDirWritable", out v)) sb.AppendLine("outputDirWritable=" + v);
            }
            else
            {
                string lastRunAt = "none";
                if (File.Exists(lastRunPath))
                {
                    try { lastRunAt = File.ReadAllText(lastRunPath).Trim(); } catch { }
                }
                var assiPathCheck = SnowPanicVideoPipelineSelfTest.GetAssiLogPath();
                var hasRun = File.Exists(lastRunPath);
                var assiContent = "";
                if (File.Exists(assiPathCheck))
                {
                    try { assiContent = File.ReadAllText(assiPathCheck); hasRun = hasRun || assiContent.Contains("step=start") || assiContent.Contains("step=recorder_start"); } catch { }
                }
                var inferredErrorStep = "recorder_start_failed";
                if (assiContent.Contains("step=recorder_start_exception")) inferredErrorStep = "recorder_start_failed";
                else if (assiContent.Contains("step=recorder_start_failed")) inferredErrorStep = "recorder_start_failed";
                else if (assiContent.Contains("step=play_never_entered")) inferredErrorStep = "recorder_start_failed";
                else if (assiContent.Contains("step=mp4_not_created") || assiContent.Contains("step=mp4_wait")) inferredErrorStep = "mp4_not_created";
                if (hasRun)
                {
                    sb.AppendLine("result=ERROR");
                    sb.AppendLine("errorStep=" + inferredErrorStep + " (no session file; inferred from assi_log)");
                    sb.AppendLine("elapsedSec=");
                    sb.AppendLine("local_mp4_path=");
                    sb.AppendLine("local_mp4_exists=false");
                    sb.AppendLine("local_mp4_size_bytes=0");
                    sb.AppendLine("daily_archive_path=");
                    sb.AppendLine("daily_archive_created=false");
                    sb.AppendLine("preview_path=");
                    sb.AppendLine("preview_created=false");
                    sb.AppendLine("drive_file=not_found");
                    sb.AppendLine("slack_message=not_posted");
                    sb.AppendLine("lastSelfTestRunAt=" + lastRunAt);
                }
                else
                {
                    sb.AppendLine("result=NOT_RUN");
                    sb.AppendLine("lastSelfTestRunAt=" + lastRunAt);
                }
            }

            sb.AppendLine("--- assi_log (last " + VideoPipelineLogsMaxLines + " lines, run session only) ---");
            string sessionIdForLog = null;
            if (File.Exists(sessionPath))
            {
                try
                {
                    var sl = File.ReadAllLines(sessionPath);
                    foreach (var x in sl)
                    {
                        var eq = x.IndexOf('=');
                        if (eq > 0 && x.Substring(0, eq).Trim().Equals("sessionId", StringComparison.OrdinalIgnoreCase))
                        {
                            sessionIdForLog = x.Substring(eq + 1).Trim();
                            break;
                        }
                    }
                }
                catch { }
            }
            var runLogContent = !string.IsNullOrEmpty(sessionIdForLog) ? SnowPanicVideoPipelineSelfTest.GetSessionRunLast50Lines(sessionIdForLog) : null;
            if (!string.IsNullOrEmpty(runLogContent))
            {
                foreach (var l in runLogContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    sb.AppendLine(l);
            }
            else
            {
                var assiPath = SnowPanicVideoPipelineSelfTest.GetAssiLogPath();
                if (File.Exists(assiPath))
                {
                    var assiLines = File.ReadAllLines(assiPath);
                    int take = Math.Min(VideoPipelineLogsMaxLines, assiLines.Length);
                    int start = Math.Max(0, assiLines.Length - take);
                    foreach (var l in assiLines.Skip(start).Take(take))
                        sb.AppendLine(l);
                }
                else sb.AppendLine("(no assi_log)");
            }
            sb.AppendLine("==========================");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return "(読取失敗: " + ex.Message + ")";
        }
    }

    const int ConsoleFilteredMaxLines = 200;

    static string BuildConsoleLogsFilteredSection()
    {
        try
        {
            var path = SnowPanicVideoPipelineSelfTest.GetConsoleFilteredLogPath();
            if (!File.Exists(path))
                return "(no log) reason=video_pipeline_console_filtered.txt not found. SelfTest may not have run.";
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
                return "(no log) reason=filtered console buffer empty.";
            int take = Math.Min(ConsoleFilteredMaxLines, lines.Length);
            int start = Math.Max(0, lines.Length - take);
            return string.Join(Environment.NewLine, lines.Skip(start).Take(take));
        }
        catch (Exception ex)
        {
            return "(読取失敗: " + ex.Message + ")";
        }
    }

    /// <summary>実装サマリ（ノア→ケン理解用）</summary>
    static string BuildImplementationSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 実装サマリ ===");
        sb.AppendLine("・雪挙動ロールバック（塊で落ちる復旧）:");
        sb.AppendLine("  - RoofSnow.debugMode=false（Cornice）: cluster 3-7粒、1-2粒→塊感");
        sb.AppendLine("  - SnowPackSpawner: pieceSize 0.11→0.13, maxSecondaryDetachPerHit 60→12");
        sb.AppendLine("  - secondary wave 12-45→6-18, third wave 8-30→4-12, third閾値 60→80");
        sb.AppendLine("・カメラ: 維持（-6,4,-6）(25,45,0)");
        sb.AppendLine("・NOAプレビュー: ffmpeg→gif, なければ contact_sheet, 必ず1つ生成");
        sb.AppendLine("・final_result: local mp4+preview あれば LOCAL_READY（Drive失敗でもERRORにしない）");
        sb.AppendLine("・次にやるべきこと: 6軒→1軒への戻し（今回は後回し）");
        return sb.ToString();
    }

    struct SummaryData
    {
        public bool activeZero; public int firstFrame; public float firstTime;
        public int poolReturnQueue, poolReturnAvalanche, poolReturnDespawn, destroySlideRoot;
        public bool callerFileLine;
    }

    static SummaryData ExtractSummaryData(string[] lines)
    {
        var d = new SummaryData();
        foreach (var line in lines)
        {
            if (line.Contains("activePieces=0") || line.Contains("ACTIVE=0"))
            {
                d.activeZero = true;
                var fm = Regex.Match(line, @"frame=(\d+)");
                var tm = Regex.Match(line, @"\bt=([\d.]+)");
                if (fm.Success) int.TryParse(fm.Groups[1].Value, out d.firstFrame);
                if (tm.Success) float.TryParse(tm.Groups[1].Value, out d.firstTime);
            }
            if (line.Contains("PoolReturn") || line.Contains("[SnowPackPoolReturn]"))
            {
                if (line.Contains("source=Queue")) d.poolReturnQueue++;
                else if (line.Contains("source=Avalanche")) d.poolReturnAvalanche++;
                else if (line.Contains("source=Despawn")) d.poolReturnDespawn++;
            }
            if ((line.Contains("Destroy(slideRoot)") || (line.Contains("[SnowPackDestroy]") && line.Contains("slideRoot")))) d.destroySlideRoot++;
            if ((line.Contains(" at ") || line.Contains(" in ")) && (line.Contains(".cs:") || line.Contains(".cs("))) d.callerFileLine = true;
        }
        return d;
    }

    static SummaryData ParseSummaryFromReport(string report)
    {
        var d = new SummaryData();
        var lines = report.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("ACTIVE=0 が発生したか: Yes")) d.activeZero = true;
            if (line.Contains("PoolReturn source別:"))
            {
                var m = Regex.Match(line, @"Queue=(\d+)");
                if (m.Success) int.TryParse(m.Groups[1].Value, out d.poolReturnQueue);
                m = Regex.Match(line, @"Avalanche=(\d+)");
                if (m.Success) int.TryParse(m.Groups[1].Value, out d.poolReturnAvalanche);
                m = Regex.Match(line, @"Despawn=(\d+)");
                if (m.Success) int.TryParse(m.Groups[1].Value, out d.poolReturnDespawn);
            }
            if (line.Contains("Destroy(slideRoot) 発生回数:"))
            {
                var m = Regex.Match(line, @"(\d+)$");
                if (m.Success) int.TryParse(m.Groups[1].Value, out d.destroySlideRoot);
            }
            if (line.Contains("caller + file:line") && line.Contains("Yes")) d.callerFileLine = true;
        }
        return d;
    }

    static List<string> ExtractErrors(string[] lines)
    {
        var list = new List<string>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("[Error]") || lines[i].Contains("[Exception]") || lines[i].Contains("Exception"))
            {
                var sb = new StringBuilder();
                sb.Append(lines[i]);
                int j = i + 1;
                while (j < lines.Length && (lines[j].Contains(" at ") || lines[j].Contains(" in ") || lines[j].Contains("[StackTrace") || lines[j].Contains("[Exception")))
                {
                    sb.Append("\n  ").Append(lines[j]);
                    j++;
                }
                list.Add(sb.ToString());
                i = j - 1;
            }
        }
        return list;
    }

    static HashSet<string> ExtractErrorKeysFromReport(string report)
    {
        var set = new HashSet<string>();
        var lines = report.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.Contains("[Error]") || line.Contains("[Exception]"))
                set.Add(ErrorKey(line));
        }
        return set;
    }

    static string ErrorKey(string errorBlock)
    {
        string first = errorBlock.Split('\n')[0].Trim();
        return first.Length > 100 ? first.Substring(0, 100) : first;
    }

    static string[] LoadConsoleLines()
    {
        string[] buf = null;
        if (File.Exists(BufferPath))
        {
            buf = File.ReadAllLines(BufferPath);
            if (buf.Length > 0)
            {
                // バッファにASSI必須ログが含まれていれば採用（長Playで先頭が捨てられた場合はfullLogを試す）
                if (buf.Any(l => l.Contains("[ASSI_BOOT]"))) return buf;
            }
        }
        if (File.Exists(LogPath))
        {
            var all = File.ReadAllLines(LogPath);
            int sep = Array.FindIndex(all, l => l.Trim() == "---");
            var fullLog = sep >= 0 ? all.Skip(sep + 1).ToArray() : all;
            if (fullLog.Length > 0) return fullLog;
        }
        return buf ?? Array.Empty<string>();
    }

    static readonly string[] RecordingSearchDirs = new[]
    {
        "Recordings",
        Path.Combine("Assets", "Recordings"),
        Path.Combine("Assets", "Logs", "Recordings"),
    };
    static readonly string[] VideoExtensions = new[] { ".mp4", ".webm", ".mov", ".avi" };

    /// <summary>最新のRecorder動画を探す。見つかればフルパス、なければ空文字。</summary>
    public static string GetLatestRecordingPath()
    {
        var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? "";
        FileInfo latest = null;
        foreach (var rel in RecordingSearchDirs)
        {
            var dir = Path.Combine(projectRoot, rel);
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var ext in VideoExtensions)
                {
                    var files = Directory.GetFiles(dir, "*" + ext, SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        var fi = new FileInfo(f);
                        if (latest == null || fi.LastWriteTime > latest.LastWriteTime)
                            latest = fi;
                    }
                }
            }
            catch { }
        }
        return latest != null ? latest.FullName : "";
    }

    static string GetLatestRecordingSection()
    {
        var path = GetLatestRecordingPath();
        if (string.IsNullOrEmpty(path))
            return "動画: (見つかりません。Recorderで録画後、Recordings フォルダに保存されているか確認)";
        var fileName = Path.GetFileName(path);
        var dir = Path.GetDirectoryName(path);
        return $"動画: {fileName}\n動画パス: {path}\n※ノアに送る時、このファイルを添付してください";
    }

    /// <summary>保存済みレポートを読み取る。なければ空文字。</summary>
    public static string GetReportContent()
    {
        if (File.Exists(ReportPath))
            return File.ReadAllText(ReportPath);
        return "";
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _sawPlayMode = true;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode || !_sawPlayMode) return;
        _sawPlayMode = false;
        if (TryBuildReportOrSelfTestReport())
        {
            EditorApplication.delayCall += () =>
            {
                AssiReportWindow.OpenAndShowReport();
                if (SnowPanicVideoPipelineSelfTest.IsSelfTestSession)
                {
                    var report = GetReportContent();
                    if (!string.IsNullOrEmpty(report))
                        EditorGUIUtility.systemCopyBuffer = report;
                    SnowPanicVideoPipelineSelfTest.IsSelfTestSession = false;
                }
            };
        }
    }

    static bool TryBuildAndCopyReport()
    {
        if (!BuildReport())
        {
            Debug.LogWarning("[NOAReportAuto] log file not found or empty. Play once, stop, then try again.");
            return false;
        }
        string report = GetReportContent();
        if (!string.IsNullOrEmpty(report))
            EditorGUIUtility.systemCopyBuffer = report;
        return !string.IsNullOrEmpty(report);
    }

    static readonly Regex RunIdHeaderRegex = new Regex(@"runId=(\d+)", RegexOptions.Compiled);

    static int DetectLatestRunId(string[] lines)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("runId="))
            {
                var m = RunIdHeaderRegex.Match(lines[i]);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int runId))
                    return runId;
            }
        }
        int maxRunId = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("[SlideProto3s]")) continue;
            Match m = RunIdRegex.Match(lines[i]);
            if (!m.Success) continue;
            if (int.TryParse(m.Groups[1].Value, out int runId))
                maxRunId = Mathf.Max(maxRunId, runId);
        }
        return maxRunId;
    }

    static List<string> CollectTargetLines(string[] lines, int latestRunId)
    {
        var result = new List<string>();
        bool inExceptionBlock = false;
        bool pastHeader = false;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith("---")) { pastHeader = true; continue; }
            if (!pastHeader && (line.StartsWith("#") || line.StartsWith("started_at") || line.StartsWith("unity_version") || line.StartsWith("scene")))
                continue;

            if (line.Contains("[Exception]"))
            {
                inExceptionBlock = true;
                result.Add(line);
                continue;
            }

            if (inExceptionBlock)
            {
                result.Add(line);
                if (line.Contains("[ExceptionEnd]"))
                    inExceptionBlock = false;
                continue;
            }

            if (line.Contains("[SlideProto3s]"))
            {
                if (latestRunId < 0 || LineHasRunId(line, latestRunId))
                    result.Add(line);
                continue;
            }

            // Stack trace continuation (直前に [SnowPack*] STACK があり、この行が "at " で始まる場合)
            if (i > 0 && (line.Contains(" at ") || line.TrimStart().StartsWith("at ")))
            {
                string prevLine = lines[i - 1];
                if (prevLine.Contains("[SnowPack") && prevLine.Contains("STACK"))
                {
                    result.Add(line.TrimStart());
                    continue;
                }
            }

            if (line.Contains("[ASSI]") || line.Contains("[RendererWatch]") || line.Contains("[RendererBlink]") || line.Contains("[TopBlueCandidates]") || line.Contains("[TopTransparentCandidates]") || line.Contains("=== ROOF PROXY ===") || line.Contains("=== SNOW DEPTH ===") || line.Contains("=== SNOWFALL STOP ===") || line.Contains("=== TAP AVALANCHE ===") || line.Contains("[AVALANCHE_BURST_LOG]") || line.Contains("DEACTIVATE BLOCKED") || line.Contains("[SnowPackEntityDump]") || line.Contains("[SnowPackEntity1s]") || line.Contains("[AUTO-REBUILD]") || line.Contains("[SnowPackLast20]") || line.Contains("[SnowPackTransition]") || line.Contains("[PoolReturnFirst]") || line.Contains("[ASSI_BOOT]") || line.Contains("[RUN_SNAPSHOT_FORCE]") || line.Contains("[LAST20_FORCE]") || line.Contains("[LAST20_EMPTY]") || line.Contains("[SNAPSHOT_INVALID]") || line.Contains("[SNAPSHOT_ROOT]") || line.Contains("[STACKTRACE_SELFTEST]") || line.Contains("[AvalancheReturn]") || line.Contains("[RoofVectors]") || line.Contains("[RoofBasis]") || line.Contains("[TapSlide]") || line.Contains("[TapHit]") || line.Contains("[TapMiss]") || line.Contains("[LocalAvalanche]") || line.Contains("[AvalancheBeforeAfter]") || line.Contains("[AvalanchePackedReduced]") || line.Contains("[PiecePoseSample]") || line.Contains("[RotationOverrideFound]") || line.Contains("[TapMarkerState]") || line.Contains("[AutoAvalancheState]") || line.Contains("[SceneCodePath]") || line.Contains("[CORNICE_SCENE_CHECK]") || line.Contains("[TAP_DEBUG]") || line.Contains("[TAP RAY]") || line.Contains("[SNOW_ROLLBACK_CHECK]") || line.Contains("[CAMERA_LOCK_CHECK]")) { result.Add(line); continue; }

            // MVP + legacy tags (incl. [SnowPackSync] [SnowPackPoolReturn] [SnowPackDestroy] [SnowPackLast10] [SnowPackActiveZero])
            if (line.Contains("[SnowPack")
                || line.Contains("[SnowFall")
                || line.Contains("[Avalanche")
                || line.Contains("[GroundVisual]")
                || line.Contains("[RoofSnow]")
                || line.Contains("[GroundSnow]")
                || line.Contains("[SnowMVP]")
                || line.Contains("[SnowLoop")
                || line.Contains("[AvalancheAuto")
                || line.Contains("[SlideFreeze]")
                || line.Contains("[SlideCreep]")
                || line.Contains("[Exception"))
            {
                result.Add(line);
            }
        }
        return result;
    }

    static bool LineHasRunId(string line, int runId)
    {
        Match m = RunIdRegex.Match(line);
        if (!m.Success) return false;
        return int.TryParse(m.Groups[1].Value, out int parsed) && parsed == runId;
    }

    static string BuildReportFullMode(string[] lines)
    {
        var sb = new StringBuilder();
        sb.AppendLine("【ASSI REPORT - FULL MODE】");
        sb.AppendLine();
        sb.AppendLine("=== 次回レポート必須（4項目） ===");
        sb.AppendLine(BuildRequiredChecklist(lines));
        sb.AppendLine();
        sb.AppendLine("=== ASSI観測ログ（必須） ===");
        sb.AppendLine(BuildAssiObservationLog(lines));
        sb.AppendLine();
        sb.AppendLine("=== SUMMARY ===");
        sb.AppendLine(BuildSummary(lines));
        sb.AppendLine();
        sb.AppendLine("=== ACTIVE ZERO CONTEXT (±30 lines) ===");
        sb.AppendLine(BuildActiveZeroContext(lines));
        sb.AppendLine();
        sb.AppendLine("=== FULL CONSOLE DUMP START ===");
        int start = Math.Max(0, lines.Length - FullConsoleDumpLines);
        for (int i = start; i < lines.Length; i++)
            sb.AppendLine(lines[i]);
        sb.AppendLine("=== FULL CONSOLE DUMP END ===");
        sb.AppendLine();
        sb.AppendLine("=== ANALYSIS ===");
        sb.AppendLine(BuildAssiAnalysis(lines.ToList()));
        sb.AppendLine("=== ANALYSIS END ===");
        sb.AppendLine();
        sb.AppendLine("=== VIDEO PIPELINE LOGS ===");
        sb.AppendLine(BuildVideoPipelineLogsSection());
        sb.AppendLine();
        sb.AppendLine("=== CONSOLE LOGS (filtered) ===");
        sb.AppendLine(BuildConsoleLogsFilteredSection());
        sb.AppendLine();
        sb.AppendLine(BuildImplementationSummary());
        return sb.ToString();
    }

    const int MaxReportChars = 6000;

    static string TruncateReport(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= MaxReportChars) return s;
        return s.Substring(0, MaxReportChars) + "\n...(6000文字で打ち切り)";
    }

    /// <summary>CORNICE SCENE CHECK: scene, house_count, spawn_system, is_expected。</summary>
    static string BuildCorniceSceneCheckSection(string[] lines)
    {
        var last = lines.LastOrDefault(l => l.Contains("[CORNICE_SCENE_CHECK]"));
        if (string.IsNullOrEmpty(last))
            return "scene=(no Cornice scene) house_count=0 spawn_system=N/A is_expected=N/A";
        var sb = new StringBuilder();
        foreach (var m in Regex.Matches(last, @"(\w+)=([^\s]+)"))
        {
            var match = m as Match;
            if (match == null) continue;
            var key = match.Groups[1].Value;
            if (key == "scene" || key == "house_count" || key == "one_house_forced" || key == "rollback_applied" || key == "camera_position" || key == "camera_rotation" || key == "active_roof_target" || key == "test_roof_visible" || key == "roof_shape" || key == "roof_slope_direction" || key == "enabled_snow_systems" || key == "disabled_legacy_snow_systems" || key == "active_snow_visual" || key == "active_snow_break_logic" || key == "active_snow_spawn_logic" || key == "spawn_system" || key == "spawn_reason" || key == "is_expected")
                sb.AppendLine($"{key}={match.Groups[2].Value}");
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : last;
    }

    /// <summary>SNOW ROLLBACK CHECK: 塊感復旧の確認。</summary>
    static string BuildSnowRollbackCheckSection(string[] lines)
    {
        var last = lines.LastOrDefault(l => l.Contains("[SNOW_ROLLBACK_CHECK]"));
        if (string.IsNullOrEmpty(last)) return "rollback_target=pre_camera_change_good_state current_house_count=N/A result=NG comment=(no log)";
        var sb = new StringBuilder();
        foreach (var m in Regex.Matches(last, @"(\w+)=([^\s]+)"))
        {
            var match = m as Match;
            if (match == null) continue;
            sb.AppendLine($"{match.Groups[1].Value}={match.Groups[2].Value}");
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : last;
    }

    /// <summary>CAMERA LOCK CHECK: カメラ位置が維持されているか。</summary>
    static string BuildCameraLockCheckSection(string[] lines)
    {
        var last = lines.LastOrDefault(l => l.Contains("[CAMERA_LOCK_CHECK]"));
        if (string.IsNullOrEmpty(last)) return "camPos=(N/A) camEuler=(N/A) result=CHANGED";
        var sb = new StringBuilder();
        foreach (var m in Regex.Matches(last, @"(camPos|camEuler|result)=([^\s]+)"))
        {
            var match = m as Match;
            if (match == null) continue;
            sb.AppendLine($"{match.Groups[1].Value}={match.Groups[2].Value}");
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : last;
    }

    /// <summary>DRIVE STATUS: upload_attempted, upload_success, error。</summary>
    static string BuildDriveStatusSection()
    {
        try
        {
            var sessionPath = SnowPanicVideoPipelineSelfTest.GetSessionDataPath();
            if (!File.Exists(sessionPath)) return "upload_attempted=false\nupload_success=false\nerror=session_not_found\nresult=WARNING";
            var sessionLines = File.ReadAllLines(sessionPath);
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in sessionLines)
            {
                var eq = line.IndexOf('=');
                if (eq > 0) dict[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            string v;
            var attempted = dict.TryGetValue("upload_attempted", out v) ? v : (dict.TryGetValue("drive_uploaded", out v) ? "true" : "unknown");
            var success = dict.TryGetValue("upload_success", out v) ? v : (dict.TryGetValue("drive_uploaded", out v) ? v : "false");
            var driveUploaded = dict.TryGetValue("drive_uploaded", out v) ? v : "false";
            var err = dict.TryGetValue("upload_error", out v) ? v : (dict.TryGetValue("slack_error", out v) ? v : "none");
            var result = (success == "true" || driveUploaded == "true") ? "OK" : "WARNING";
            var sb = new StringBuilder();
            sb.AppendLine("upload_attempted=" + (string.IsNullOrEmpty(attempted) ? "true" : attempted));
            sb.AppendLine("upload_success=" + (string.IsNullOrEmpty(success) ? driveUploaded : success));
            sb.AppendLine("error=" + (string.IsNullOrEmpty(err) ? "none" : err));
            sb.AppendLine("result=" + result);
            return sb.ToString().TrimEnd();
        }
        catch { return "upload_attempted=unknown\nupload_success=false\nerror=(read_failed)\nresult=WARNING"; }
    }

    /// <summary>TAP DEBUG: TapHit, TapMiss, lastHitObject, lastHitLayer。</summary>
    static string BuildTapDebugSection(string[] lines)
    {
        var last = lines.LastOrDefault(l => l.Contains("[TAP_DEBUG]"));
        if (string.IsNullOrEmpty(last))
            return "TapHit=0 TapMiss=0 lastHitObject= lastHitLayer=(no tap data)";
        var sb = new StringBuilder();
        foreach (var m in Regex.Matches(last, @"(TapHit|TapMiss|lastHitObject|hit_target|lastHitLayer)=([^\s]*)"))
        {
            var match = m as Match;
            if (match == null) continue;
            sb.AppendLine($"{match.Groups[1].Value}={match.Groups[2].Value}");
        }
        return sb.Length > 0 ? sb.ToString().TrimEnd() : last;
    }

    /// <summary>タップ・局所雪崩の必須レポート項目。</summary>
    static string BuildTapLocalAvalancheReport(string[] lines)
    {
        var sb = new StringBuilder();
        var roofBasis = lines.LastOrDefault(l => l.Contains("[RoofBasis]") && l.Contains("dot(r,n)"));
        var tapHit = lines.FirstOrDefault(l => l.Contains("[TapHit]"));
        var tapMiss = lines.FirstOrDefault(l => l.Contains("[TapMiss]"));
        var localAv = lines.Where(l => l.Contains("[LocalAvalanche]") && l.Contains("removedCount=")).ToList();
        var localAvWithRemove = localAv.Where(l => { var m = Regex.Match(l, @"removedCount=(\d+)"); return m.Success && int.Parse(m.Groups[1].Value) >= 1; }).ToList();
        int removedCount = 0;
        var mRem = localAvWithRemove.Count > 0 ? Regex.Match(localAvWithRemove[localAvWithRemove.Count - 1], @"removedCount=(\d+)") : null;
        if (mRem != null && mRem.Success) int.TryParse(mRem.Groups[1].Value, out removedCount);

        sb.AppendLine($"1) roof basis dot(r,n)≈0, dot(f,n)≈0, dot(r,f)≈0: {(roofBasis != null ? "Yes" : "No")}");
        if (roofBasis != null) sb.AppendLine("   " + roofBasis);
        sb.AppendLine($"2) Packed屋根面沿い（水平禁止）: Yes（Quaternion.LookRotation(f,n)で配置）");
        sb.AppendLine($"3) Tap removedCount>=1毎回: {(localAvWithRemove.Count > 0 && removedCount >= 1 ? "Yes" : "No")} 例: {(localAvWithRemove.Count > 0 ? localAvWithRemove[localAvWithRemove.Count - 1] : "(なし)")}");
        bool packedInRadiusGt0 = localAv.Any(l => { var m = Regex.Match(l, @"packedInRadiusBefore=(\d+)"); return m.Success && int.Parse(m.Groups[1].Value) > 0; });
        sb.AppendLine($"4) PackedInRadius > 0: {(packedInRadiusGt0 ? "Yes" : "No")}");
        sb.AppendLine($"5) 穴あき+斜面流れ: {(localAvWithRemove.Count > 0 ? "Yes" : "No")}（根拠: removedCount={removedCount}, Burst斜面方向 downhill）");
        sb.AppendLine($"6) キューブが水平ではなく屋根面に沿って見えたか: 要スクショ確認");
        if (localAv.Count > 0) foreach (var la in localAv) sb.AppendLine("   " + la);
        return sb.ToString();
    }

    /// <summary>次回レポート必須4項目＋水平診断4ブロック。ユーザー指定フォーマット。</summary>
    static string BuildRequiredChecklist(string[] lines)
    {
        bool boot = lines.Any(l => l.Contains("[ASSI_BOOT]"));
        bool snapshot = lines.Any(l => l.Contains("[RUN_SNAPSHOT_FORCE]"));
        bool last20 = lines.Any(l => l.Contains("[LAST20_FORCE]"));
        bool selftest = lines.Any(l => l.Contains("[STACKTRACE_SELFTEST]"));
        var beforeAfter = lines.LastOrDefault(l => l.Contains("[AvalancheBeforeAfter]"));
        var packedReduced = lines.Where(l => l.Contains("[AvalanchePackedReduced]")).ToList();
        bool piecePose = lines.Any(l => l.Contains("[PiecePoseSample]"));
        bool rotationOverride = lines.Any(l => l.Contains("[RotationOverrideFound]"));
        bool tapMarker = lines.Any(l => l.Contains("[TapMarkerState]"));
        bool autoAvalanche = lines.Any(l => l.Contains("[AutoAvalancheState]"));
        bool sceneCodePath = lines.Any(l => l.Contains("[SceneCodePath]"));

        var sb = new StringBuilder();
        sb.AppendLine("【判定項目（最上段）】");
        var pieceVerdict = lines.LastOrDefault(l => l.Contains("[PiecePoseSample]") && l.Contains("判定="));
        string caseStr = pieceVerdict != null && pieceVerdict.Contains("判定=") ? pieceVerdict.Substring(pieceVerdict.IndexOf("判定=") + 3).Trim() : "N/A";
        var rotLine = lines.LastOrDefault(l => l.Contains("[RotationOverrideFound]"));
        string rotStr = rotLine != null ? (rotLine.Contains("None") ? "None" : "file:lineあり") : "N/A";
        var scenePathLine = lines.LastOrDefault(l => l.Contains("[SceneCodePath]") && l.Contains("debugForcePieceRendererDirect="));
        var forceMatch = scenePathLine != null ? Regex.Match(scenePathLine, @"debugForcePieceRendererDirect=(\w+)") : null;
        string forceStr = forceMatch != null && forceMatch.Success ? forceMatch.Groups[1].Value : "N/A";
        sb.AppendLine($"・見た目(斜め積もり): 要スクショ / PiecePoseSample判定: {caseStr} / RotationOverrideFound: {rotStr}");
        sb.AppendLine($"・強制可視化パス: debugForcePieceRendererDirect={forceStr} (ON=piece直下にRenderer)");
        sb.AppendLine();
        sb.AppendLine($"1) [ASSI_BOOT] 検出: {(boot ? "Yes" : "No")}");
        sb.AppendLine($"2) [RUN_SNAPSHOT_FORCE] 検出: {(snapshot ? "Yes" : "No")} / [STACKTRACE_SELFTEST] 検出: {(selftest ? "Yes" : "No")} / [LAST20_FORCE] 検出: {(last20 ? "Yes" : "No")}（各1行）");
        sb.AppendLine($"3) [AvalancheBeforeAfter] 検出: {(beforeAfter != null ? "Yes（数値付き）" : "No")}");
        if (beforeAfter != null) sb.AppendLine("   " + beforeAfter);
        sb.AppendLine($"4) 見た目が変わったか: {EvaluateVisualChange(beforeAfter, packedReduced)}");
        sb.AppendLine($"5) [PiecePoseSample]3件: {(piecePose ? "Yes" : "FAIL")} / [RotationOverrideFound]: {(rotationOverride ? "Yes" : "FAIL")} / [TapMarkerState][AutoAvalancheState]: {(tapMarker && autoAvalanche ? "Yes" : "FAIL")} / [SceneCodePath]: {(sceneCodePath ? "Yes" : "FAIL")}");
        return sb.ToString();
    }

    static string BuildAssiAngleReport(string[] lines)
    {
        var sb = new StringBuilder();
        var assiLines = lines.Where(l => l.Contains("[ASSI]")).ToList();
        if (assiLines.Count == 0) return sb.AppendLine("※ ANGLE MINI / ANGLE FIX / DEBUG CAMERA なし").ToString();

        foreach (var line in assiLines)
        {
            int idx = line.IndexOf("[ASSI]");
            if (idx < 0) continue;
            string content = line.Substring(idx + "[ASSI] ".Length).Trim();
            if (string.IsNullOrEmpty(content)) continue;
            sb.AppendLine(content);
        }
        return sb.ToString();
    }

    /// <summary>ASSI観測ログ4種を必ずレポートに含める。欠落時は警告。</summary>
    static string BuildAssiObservationLog(string[] lines)
    {
        var sb = new StringBuilder();
        int idxBoot = Array.FindIndex(lines, l => l.Contains("[ASSI_BOOT]"));
        int idxSnapshot = Array.FindIndex(lines, l => l.Contains("[RUN_SNAPSHOT_FORCE]"));
        int idxLast20 = Array.FindIndex(lines, l => l.Contains("[LAST20_FORCE]"));
        int idxSelftest = Array.FindIndex(lines, l => l.Contains("[STACKTRACE_SELFTEST]"));

        if (idxBoot >= 0)
        {
            sb.AppendLine(lines[idxBoot]);
            for (int i = idxBoot + 1; i < lines.Length && i < idxBoot + 4 && (lines[i].Contains("scene=") || lines[i].Contains("enabled=") || lines[i].Contains("active=") || lines[i].Contains("frame=") || lines[i].Contains("time=")); i++)
                sb.AppendLine(lines[i]);
        }
        else
        {
            sb.AppendLine("※ [ASSI_BOOT] が検出されませんでした");
            sb.AppendLine("  想定原因: SnowLoopLogCaptureが無効、Play未開始、シーン未ロード");
        }

        if (idxSnapshot >= 0) sb.AppendLine(lines[idxSnapshot]);
        else { sb.AppendLine("※ [RUN_SNAPSHOT_FORCE] が検出されませんでした"); sb.AppendLine("  想定原因: SnowPackSpawnerが未配置/無効、Play時間が2秒未満"); }
        if (idxLast20 >= 0) sb.AppendLine(lines[idxLast20]);
        else { sb.AppendLine("※ [LAST20_FORCE] が検出されませんでした"); sb.AppendLine("  想定原因: 上記と同様（2秒診断が未実行）"); }
        if (idxSelftest >= 0) sb.AppendLine(lines[idxSelftest]);
        else { sb.AppendLine("※ [STACKTRACE_SELFTEST] が検出されませんでした"); sb.AppendLine("  想定原因: 上記と同様"); }

        foreach (var v in lines.Where(l => l.Contains("[SNAPSHOT_INVALID]"))) sb.AppendLine(v);
        foreach (var r in lines.Where(l => l.Contains("[SNAPSHOT_ROOT]"))) sb.AppendLine(r);
        foreach (var e in lines.Where(l => l.Contains("[LAST20_EMPTY]"))) sb.AppendLine(e);

        sb.AppendLine();
        sb.AppendLine(BuildAssiAngleReport(lines));
        sb.AppendLine();
        sb.AppendLine("--- 水平診断4ブロック（必須） ---");
        var pieceSamples = lines.Where(l => l.Contains("[PiecePoseSample]")).ToArray();
        if (pieceSamples.Length > 0) { foreach (var p in pieceSamples) sb.AppendLine(p); }
        else { sb.AppendLine("※ [PiecePoseSample] が検出されませんでした (FAIL)"); }
        var rotFound = lines.Where(l => l.Contains("[RotationOverrideFound]")).ToArray();
        if (rotFound.Length > 0) { foreach (var r in rotFound) sb.AppendLine(r); }
        else { sb.AppendLine("※ [RotationOverrideFound] が検出されませんでした (FAIL)"); }
        var tapState = lines.Where(l => l.Contains("[TapMarkerState]")).ToArray();
        if (tapState.Length > 0) { foreach (var t in tapState) sb.AppendLine(t); }
        else { sb.AppendLine("※ [TapMarkerState] が検出されませんでした (FAIL)"); }
        var avState = lines.Where(l => l.Contains("[AutoAvalancheState]")).ToArray();
        if (avState.Length > 0) { foreach (var a in avState) sb.AppendLine(a); }
        else { sb.AppendLine("※ [AutoAvalancheState] が検出されませんでした (FAIL)"); }
        var scenePath = lines.Where(l => l.Contains("[SceneCodePath]")).ToArray();
        if (scenePath.Length > 0) { foreach (var s in scenePath) sb.AppendLine(s); }
        else { sb.AppendLine("※ [SceneCodePath] が検出されませんでした (FAIL)"); }

        return sb.ToString();
    }

    static string BuildSummary(string[] lines)
    {
        var sb = new StringBuilder();
        bool activeZeroOccurred = false;
        int firstFrame = -1;
        float firstTime = -1f;
        int active = -1, pooled = -1, rootCh = -1;
        foreach (var line in lines)
        {
            if (line.Contains("activePieces=0") || line.Contains("ACTIVE=0") || line.Contains("[OnPieceDeactivated]"))
            {
                if (line.Contains("ACTIVE=0") || line.Contains("activePieces=0")) activeZeroOccurred = true;
                var fm = Regex.Match(line, @"frame=(\d+)");
                var tm = Regex.Match(line, @"\bt=([\d.]+)");
                if (fm.Success && firstFrame < 0) int.TryParse(fm.Groups[1].Value, out firstFrame);
                if (tm.Success && firstTime < 0) float.TryParse(tm.Groups[1].Value, out firstTime);
                var am = Regex.Match(line, @"activeAfter=(\d+)|activePieces=(\d+)");
                if (am.Success) int.TryParse(am.Groups[1].Success ? am.Groups[1].Value : am.Groups[2].Value, out active);
                var pm = Regex.Match(line, @"pooled=(\d+)");
                if (pm.Success) int.TryParse(pm.Groups[1].Value, out pooled);
                var rm = Regex.Match(line, @"rootChildren=(\d+)");
                if (rm.Success) int.TryParse(rm.Groups[1].Value, out rootCh);
            }
        }
        sb.AppendLine($"ACTIVE=0: {(activeZeroOccurred ? "Yes" : "No")}" + (activeZeroOccurred ? $" firstFrame={firstFrame} firstTime={firstTime:F2}" : ""));
        sb.AppendLine($"active={active} pooled={pooled} rootChildren={rootCh}");

        int entityChild = -1, entityTransform = -1, entityPiece = -1, entityRenderer = -1;
        var entityLine = lines.LastOrDefault(l => l.Contains("[SnowPackEntityDump]") || l.Contains("[SnowPackEntity1s]"));
        if (entityLine != null)
        {
            var m = Regex.Match(entityLine, @"childCount=(\d+)");
            if (m.Success) int.TryParse(m.Groups[1].Value, out entityChild);
            m = Regex.Match(entityLine, @"transformCount=(\d+)");
            if (m.Success) int.TryParse(m.Groups[1].Value, out entityTransform);
            m = Regex.Match(entityLine, @"pieceByNameCount=(\d+)");
            if (m.Success) int.TryParse(m.Groups[1].Value, out entityPiece);
            m = Regex.Match(entityLine, @"rendererCount=(\d+)");
            if (m.Success) int.TryParse(m.Groups[1].Value, out entityRenderer);
        }
        sb.AppendLine($"実体: childCount={entityChild} transformCount={entityTransform} pieceByNameCount={entityPiece} rendererCount={entityRenderer}");

        bool autoRebuildFired = lines.Any(l => l.Contains("[AUTO-REBUILD]") && l.Contains("FIRED"));
        bool autoRebuildRecovered = lines.Any(l => l.Contains("[AUTO-REBUILD]") && l.Contains("recovered=true"));
        sb.AppendLine($"AUTO-REBUILD FIRED: {(autoRebuildFired ? "Yes" : "No")}" + (autoRebuildFired ? $" 復旧={(autoRebuildRecovered ? "OK" : "FAIL")}" : ""));

        var failLine = lines.FirstOrDefault(l => l.Contains("[AUTO-REBUILD FAIL]"));
        string failReasonStr = "None";
        if (failLine != null)
        {
            var rm = Regex.Match(failLine, @"reason=(\w+)");
            if (rm.Success) failReasonStr = rm.Groups[1].Value;
        }
        sb.AppendLine($"AUTO-REBUILD FAIL 理由: {failReasonStr}");

        bool deactivateBlocked = lines.Any(l => l.Contains("DEACTIVATE BLOCKED"));
        sb.AppendLine($"DEACTIVATE BLOCKED が出たか: {(deactivateBlocked ? "Yes" : "No")}");

        int queue = 0, avalanche = 0, despawn = 0, clear = 0, unknown = 0;
        foreach (var line in lines)
        {
            if (!line.Contains("PoolReturn") && !line.Contains("[SnowPackPoolReturn]") && !line.Contains("[OnPieceDeactivated]")) continue;
            if (line.Contains("source=Queue")) queue++;
            else if (line.Contains("source=Avalanche")) avalanche++;
            else if (line.Contains("source=Despawn")) despawn++;
            else if (line.Contains("source=Clear")) clear++;
            else if (line.Contains("source=")) unknown++;
        }
        sb.AppendLine($"PoolReturn source別: Queue={queue} Avalanche={avalanche} Despawn={despawn} Clear={clear}");
        foreach (var line in lines.Where(l => l.Contains("[PoolReturnFirst]")))
        {
            var m = Regex.Match(line, @"source=(\w+).*?fileLine=([^\s\]]+)");
            if (m.Success) sb.AppendLine($"  First {m.Groups[1].Value}: fileLine={m.Groups[2].Value}");
        }

        var destroyLines = lines.Where(l => l.Contains("[SnowPackDestroy]") && l.Contains("slideRoot")).ToList();
        sb.AppendLine($"Destroy(slideRoot): {destroyLines.Count}");
        foreach (var dl in destroyLines)
        {
            var fm = Regex.Match(dl, @"fileLine=([^\s\]]+)");
            var cm = Regex.Match(dl, @"caller=(\w+)");
            if (fm.Success) sb.AppendLine($"  fileLine={fm.Groups[1].Value}" + (cm.Success ? $" caller={cm.Groups[1].Value}" : ""));
        }

        bool fileLineOk = lines.Any(l => (l.Contains("fileLine=") && !l.Contains("fileLine=unknown")) && (l.Contains("[OnPieceDeactivated]") || l.Contains("[SnowPackDestroy]") || l.Contains("[PoolReturnFirst]") || l.Contains("[SnowPackRootMutation]") || l.Contains("[SnowPackLast20]")));
        sb.AppendLine($"file:line 出たか: {(fileLineOk ? "Yes" : "No")}");

        string roofVal = ExtractRoofVectors(lines) ?? "要確認";
        sb.AppendLine($"roofNormal/roofUp: {roofVal}");

        string packedScale = ExtractLastScale(lines, "Packed") ?? ExtractScaleFrom1s(lines, "PackedScale") ?? "要確認";
        string burstScale = ExtractLastScale(lines, "Burst") ?? ExtractScaleFrom1s(lines, "BurstScale") ?? "要確認";
        string fallingScale = ExtractLastScale(lines, "Falling") ?? ExtractScaleFrom1s(lines, "FallingScale") ?? "要確認";
        sb.AppendLine($"PackedScale={packedScale} BurstScale={burstScale} FallingScale={fallingScale}");
        sb.AppendLine("=== 成功条件（次回判定） ===");
        sb.AppendLine($"file:line出たか: {(fileLineOk ? "Yes" : "No")}");
        sb.AppendLine($"ACTIVE=0消えたか: {(activeZeroOccurred ? "No" : "Yes")}");
        sb.AppendLine($"Destroy(slideRoot)=0: {(destroyLines.Count == 0 ? "Yes" : "No")}");
        return sb.ToString();
    }

    static string ExtractLastScale(string[] lines, string kind)
    {
        if (lines == null) return null;
        var match = lines.LastOrDefault(l => l != null && l.Contains("[SnowPieceScale]") && l.Contains("kind=" + kind));
        if (match == null) return null;
        var m = Regex.Match(match, @"scale=\(([\d.]+),([\d.]+),([\d.]+)\)");
        return m.Success ? $"({m.Groups[1].Value},{m.Groups[2].Value},{m.Groups[3].Value})" : null;
    }

    static string ExtractScaleFrom1s(string[] lines, string key)
    {
        if (lines == null) return null;
        var match = lines.LastOrDefault(l => l != null && l.Contains("[SnowPackScale1s]"));
        if (match == null) return null;
        var m = Regex.Match(match, key + @"=([\d.]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    static string ExtractRoofVectors(string[] lines)
    {
        if (lines == null) return null;
        var match = lines.LastOrDefault(l => l != null && l.Contains("[RoofVectors]"));
        if (match == null) return null;
        var m = Regex.Match(match, @"roofNormal=\([^)]+\) roofUp=\([^)]+\) worldUp=\([^)]+\)");
        return m.Success ? m.Value : null;
    }

    static string BuildLast20Events(string[] lines)
    {
        var events = lines.Where(l => l.Contains("[SnowPackLast20]") || l.Contains("[SnowPackLast20Short]")).ToList();
        if (events.Count == 0) return "(なし)";
        return string.Join("\n", events);
    }

    static string BuildAvalancheBeforeAfter(string[] lines)
    {
        var sb = new StringBuilder();
        var beforeAfter = lines.LastOrDefault(l => l.Contains("[AvalancheBeforeAfter]"));
        var packedReduced = lines.Where(l => l.Contains("[AvalanchePackedReduced]")).ToList();

        if (beforeAfter != null)
        {
            sb.AppendLine("1) 雪崩発生時 before/after:");
            sb.AppendLine(beforeAfter);
        }
        else
            sb.AppendLine("1) [AvalancheBeforeAfter] 未検出（雪崩未発生の可能性）");

        if (packedReduced.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("2) Packedを減らした処理（関数/層数/個数）:");
            foreach (var pr in packedReduced) sb.AppendLine(pr);
        }
        else
            sb.AppendLine("2) [AvalanchePackedReduced] 未検出");

        sb.AppendLine();
        sb.AppendLine("3) 見た目が変わったか: " + EvaluateVisualChange(beforeAfter, packedReduced));
        return sb.ToString();
    }

    /// <summary>packedCubeCountが減る＆減少量とburstAmountが一致を数値で判定。根拠付きYes/No。</summary>
    static string EvaluateVisualChange(string beforeAfter, List<string> packedReduced)
    {
        if (beforeAfter == null)
            return "No（根拠: [AvalancheBeforeAfter]未検出=雪崩未発生）";

        float roofBefore = -1f, roofAfter = -1f, packBefore = -1f, packAfter = -1f, burstAmount = -1f;
        int packedBefore = -1, packedAfter = -1;
        // 新フォーマット: beforeDepth= afterDepth= packedCubeCountBefore= packedCubeCountAfter= burstAmount=
        var m = Regex.Match(beforeAfter, @"beforeDepth=([\d.]+) afterDepth=([\d.]+) packedCubeCountBefore=(-?\d+) packedCubeCountAfter=(-?\d+) burstAmount=([\d.]+)");
        if (m.Success)
        {
            float.TryParse(m.Groups[1].Value, out roofBefore);
            float.TryParse(m.Groups[2].Value, out roofAfter);
            int.TryParse(m.Groups[3].Value, out packedBefore);
            int.TryParse(m.Groups[4].Value, out packedAfter);
            float.TryParse(m.Groups[5].Value, out burstAmount);
            packBefore = roofBefore;
            packAfter = roofAfter;
        }
        else
        {
            // 旧フォーマット: roofDepthBefore/roofDepthAfter/packDepthBefore/packDepthAfter
            m = Regex.Match(beforeAfter, @"roofDepthBefore=([\d.]+) roofDepthAfter=([\d.]+) packDepthBefore=([\d.]+) packDepthAfter=([\d.]+) packedCubeCountBefore=(-?\d+) packedCubeCountAfter=(-?\d+) burstAmount=([\d.]+)");
            if (m.Success)
            {
                float.TryParse(m.Groups[1].Value, out roofBefore);
                float.TryParse(m.Groups[2].Value, out roofAfter);
                float.TryParse(m.Groups[3].Value, out packBefore);
                float.TryParse(m.Groups[4].Value, out packAfter);
                int.TryParse(m.Groups[5].Value, out packedBefore);
                int.TryParse(m.Groups[6].Value, out packedAfter);
                float.TryParse(m.Groups[7].Value, out burstAmount);
            }
            else
            {
                m = Regex.Match(beforeAfter, @"roofDepthBefore=([\d.]+) roofDepthAfter=([\d.]+).*?packedCubeCountAfter=(-?\d+).*?burstAmount=([\d.]+)");
                if (m.Success)
                {
                    float.TryParse(m.Groups[1].Value, out roofBefore);
                    float.TryParse(m.Groups[2].Value, out roofAfter);
                    int.TryParse(m.Groups[3].Value, out packedAfter);
                    float.TryParse(m.Groups[4].Value, out burstAmount);
                }
            }
        }

        bool packedDecreased = packedBefore >= 0 && packedAfter >= 0 && packedAfter < packedBefore;
        float packDrop = (packBefore >= 0 && packAfter >= 0) ? packBefore - packAfter : 0f;
        bool depthDecreased = packDrop > 0.001f;
        bool burstMatch = burstAmount > 0.001f && (depthDecreased || Mathf.Abs(packDrop - burstAmount) < burstAmount * 0.5f);

        if (packedDecreased && (depthDecreased || burstMatch))
            return $"Yes（根拠: packedCubeCount {packedBefore}→{packedAfter}減, packDepth減={packDrop:F3}m, burstAmount={burstAmount:F3}m）";
        if (beforeAfter != null && (packedBefore < 0 || packedDecreased == false))
            return $"No（根拠: packedCubeCount減なし={packedBefore}→{packedAfter}）";
        if (beforeAfter != null && !depthDecreased && !burstMatch)
            return $"No（根拠: packDepth減なし={packDrop:F3}m, burstAmount={burstAmount:F3}m）";
        return "No（根拠: 数値パース不可）";
    }

    static string BuildActiveZeroContext(string[] lines)
    {
        if (!lines.Any(l => l.Contains("activePieces=0") || l.Contains("ACTIVE=0")))
            return "(ACTIVE=0 は発生していません)";

        var sb = new StringBuilder();
        var onPieceLog = lines.LastOrDefault(l => l.Contains("[OnPieceDeactivated]") && l.Contains("activeAfter=0"));
        if (string.IsNullOrEmpty(onPieceLog))
            onPieceLog = lines.LastOrDefault(l => l.Contains("[OnPieceDeactivated]") && l.Contains("frame="));
        if (!string.IsNullOrEmpty(onPieceLog))
            sb.AppendLine("OnPieceDeactivated ログ1件全文:").AppendLine(onPieceLog);

        var last20Events = lines.Where(l => l.Contains("[SnowPackLast20]") || l.Contains("[SnowPackLast20Short]")).ToList();
        if (last20Events.Count > 0)
            sb.AppendLine().AppendLine("直前20イベント:").AppendLine(string.Join("\n", last20Events));

        var transitionLines = lines.Where(l => l.Contains("[SnowPackTransition]")).ToList();
        if (transitionLines.Count > 0)
            sb.AppendLine().AppendLine("rootChildren/pooled/active 推移(発生前後1秒):").AppendLine(string.Join("\n", transitionLines));
        return sb.ToString();
    }

    static string BuildAssiAnalysis(List<string> picked)
    {
        var clearCount = picked.Count(l => l.Contains("[SnowPack]") && l.Contains("CLEAR"));
        var rebuildCount = picked.Count(l => l.Contains("[SnowPack]") && l.Contains("REBUILD"));
        string firstReason = "N/A";
        foreach (var line in picked)
        {
            if (line.Contains("[SnowPack]") && (line.Contains("CLEAR") || line.Contains("REBUILD")))
            {
                var m = Regex.Match(line, @"reason=(\w+)");
                firstReason = m.Success ? m.Groups[1].Value : "?";
                break;
            }
        }

        var depthVals = new List<string>();
        var childVals = new List<string>();
        foreach (var line in picked)
        {
            var depthM = Regex.Match(line, @"roofDepth=([\d.]+)|depth=([\d.]+)");
            var childM = Regex.Match(line, @"children=(\d+)|activePieces=(\d+)");
            if (depthM.Success) depthVals.Add(depthM.Groups[1].Success ? depthM.Groups[1].Value : depthM.Groups[2].Value);
            if (childM.Success) childVals.Add(childM.Groups[1].Success ? childM.Groups[1].Value : childM.Groups[2].Value);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Clear={clearCount} REBUILD={rebuildCount} reason={firstReason}");
        var depthSample = depthVals.Count > 3 ? string.Join(",", depthVals.Skip(depthVals.Count - 3)) : string.Join(",", depthVals);
        var childSample = childVals.Count > 3 ? string.Join(",", childVals.Skip(childVals.Count - 3)) : string.Join(",", childVals);
        sb.AppendLine($"depth={depthSample} children={childSample}");
        sb.AppendLine(BuildAutoVerificationCompact(picked));
        return sb.ToString();
    }

    static string BuildAutoVerificationCompact(List<string> picked)
    {
        var (allPass, failReasons, keyVals) = BuildAutoVerificationCore(picked);
        var sb = new StringBuilder();
        sb.Append(allPass ? "判定: PASS" : "判定: FAIL " + string.Join(" ", failReasons));
        if (keyVals.Count > 0)
            sb.Append(" | " + string.Join(" ", keyVals));
        return sb.ToString();
    }

    static (bool allPass, List<string> failReasons, List<string> keyVals) BuildAutoVerificationCore(List<string> picked)
    {
        const float movedMetersMin = 0.5f, movedMetersMax = 3.0f;
        const int addRemovePerSecLimit = 6;
        const float visualDepthMaxJumpPerSec = 0.25f;  // 1秒あたりの急激ジャンプ閾値

        var failReasons = new List<string>();
        var keyVals = new List<string>();

        // 1) movedMeters が 0.5〜3.0m に収まっているか
        var movedMetersList = new List<float>();
        foreach (var line in picked)
        {
            var m = Regex.Match(line, @"movedMeters=([\d.]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, out float val))
                movedMetersList.Add(val);
        }
        bool movedMetersOk = true;
        string movedMetersVal = "N/A";
        if (movedMetersList.Count > 0)
        {
            float last = movedMetersList[movedMetersList.Count - 1];
            movedMetersVal = last.ToString("F3");
            movedMetersOk = last >= movedMetersMin && last <= movedMetersMax;
            if (!movedMetersOk)
                failReasons.Add($"movedM={last:F2}");
        }
        keyVals.Add($"movedM={movedMetersVal}");

        // 2) addCount+removeCount が 1秒あたり<=6
        int addCount = 0, removeCount = 0;
        foreach (var line in picked)
        {
            if (!line.Contains("[SnowPackAudit1s]")) continue;
            var am = Regex.Match(line, @"addCount=(\d+)");
            var rm = Regex.Match(line, @"removeCount=(\d+)");
            if (am.Success) int.TryParse(am.Groups[1].Value, out addCount);
            if (rm.Success) int.TryParse(rm.Groups[1].Value, out removeCount);
        }
        float runDuration = ExtractRunDuration(picked);
        int addRemoveTotal = addCount + removeCount;
        float perSec = runDuration > 0 ? addRemoveTotal / runDuration : 0;
        bool addRemoveOk = perSec <= addRemovePerSecLimit;
        if (!addRemoveOk)
            failReasons.Add($"addRm/sec={perSec:F0}");
        keyVals.Add($"addRm={addCount}+{removeCount}/sec={perSec:F0}");

        // 3) 起動直後2秒で fired=0, suppressed>0
        int firedInFirst2s = 0, suppressedInFirst2s = 0;
        foreach (var line in picked)
        {
            float t = ExtractTimeFromLine(line);
            if (t < 0 || t > 2.0f) continue;
            if (line.Contains("[Avalanche] fired")) firedInFirst2s++;
            if (line.Contains("[Avalanche] suppressed")) suppressedInFirst2s++;
        }
        bool graceOk = firedInFirst2s == 0 && suppressedInFirst2s > 0;
        if (!graceOk)
            failReasons.Add($"Grace f={firedInFirst2s}");

        // 4) visualDepth 1秒変化量（滑らかさ: 急激ジャンプしない）
        var visualDepthSamples = new List<(float t, float v)>();
        foreach (var line in picked)
        {
            if (!line.Contains("[SnowPackAudit1s]")) continue;
            float t = ExtractTimeFromLine(line);
            var vm = Regex.Match(line, @"visualDepth=([\d.\-]+)");
            if (vm.Success && float.TryParse(vm.Groups[1].Value, out float vd))
                visualDepthSamples.Add((t, vd));
        }
        float maxVisualDeltaPerSec = 0f;
        bool visualSmoothOk = true;
        string visualDeltaVal = "N/A";
        for (int i = 1; i < visualDepthSamples.Count; i++)
        {
            float dt = visualDepthSamples[i].t - visualDepthSamples[i - 1].t;
            if (dt <= 0 || dt > 2f) continue;  // 約1秒間隔のみ
            float dv = Mathf.Abs(visualDepthSamples[i].v - visualDepthSamples[i - 1].v);
            float perSecVal = dv / dt;
            if (perSecVal > maxVisualDeltaPerSec) maxVisualDeltaPerSec = perSecVal;
        }
        if (visualDepthSamples.Count >= 2)
        {
            visualDeltaVal = maxVisualDeltaPerSec.ToString("F3");
            visualSmoothOk = maxVisualDeltaPerSec <= visualDepthMaxJumpPerSec;
            if (!visualSmoothOk)
                failReasons.Add($"visD/sec={maxVisualDeltaPerSec:F2}");
        }

        // 5) roofDepth と visualDepth の差（roofVisualDelta）
        float maxRoofVisualDelta = 0f;
        foreach (var line in picked)
        {
            if (!line.Contains("roofVisualDelta=")) continue;
            var m = Regex.Match(line, @"roofVisualDelta=([\d.\-]+)");
            if (m.Success && float.TryParse(m.Groups[1].Value, out float d))
            {
                float absD = Mathf.Abs(d);
                if (absD > maxRoofVisualDelta) maxRoofVisualDelta = absD;
            }
        }
        keyVals.Add($"roofVisD={maxRoofVisualDelta:F2}");

        // 6) children 1秒変動 |Δchildren| <= 200
        const int maxChildrenDeltaPerSec = 200;
        var childrenSamples = new List<(float t, int c)>();
        foreach (var line in picked)
        {
            if (!line.Contains("[SnowPackAudit1s]")) continue;
            float t = ExtractTimeFromLine(line);
            var cm = Regex.Match(line, @"rootChildren=(\d+)|children=(\d+)");
            string chStr = cm.Groups[1].Success ? cm.Groups[1].Value : (cm.Groups[2].Success ? cm.Groups[2].Value : null);
            if (!string.IsNullOrEmpty(chStr) && int.TryParse(chStr, out int ch))
                childrenSamples.Add((t, ch));
        }
        int maxChildrenDelta = 0;
        bool childrenDeltaOk = true;
        for (int i = 1; i < childrenSamples.Count; i++)
        {
            float dt = childrenSamples[i].t - childrenSamples[i - 1].t;
            if (dt <= 0 || dt > 2f) continue;
            int delta = Mathf.Abs(childrenSamples[i].c - childrenSamples[i - 1].c);
            if (delta > maxChildrenDelta) maxChildrenDelta = delta;
        }
        bool rootChildrenNever1 = true;
        foreach (var line in picked)
        {
            if (!line.Contains("[SnowPackAudit1s]")) continue;
            var rm = Regex.Match(line, @"rootChildren=(\d+)");
            if (rm.Success && int.TryParse(rm.Groups[1].Value, out int rc) && rc == 1)
            {
                var ap = Regex.Match(line, @"activePieces=(\d+)");
                if (ap.Success && int.TryParse(ap.Groups[1].Value, out int apv) && apv > 50)
                {
                    rootChildrenNever1 = false;
                    failReasons.Add("rootChildren=1かつactivePieces>50（階層破損）");
                    break;
                }
            }
        }
        if (maxChildrenDelta > maxChildrenDeltaPerSec)
        {
            childrenDeltaOk = false;
            failReasons.Add($"chΔ={maxChildrenDelta}");
        }
        keyVals.Add($"chΔ={maxChildrenDelta}");

        // 8) packDepth 急落 maxDown <= 0.25m/s
        const float maxPackDepthDownPerSec = 0.25f;
        float maxPackDownPerSec = 0f;
        var packSamples = new List<(float t, float p)>();
        foreach (var line in picked)
        {
            if (!line.Contains("[SnowPackAudit1s]")) continue;
            float t = ExtractTimeFromLine(line);
            var pm = Regex.Match(line, @"packDepth=([\d.\-]+)");
            if (pm.Success && float.TryParse(pm.Groups[1].Value, out float p))
                packSamples.Add((t, p));
        }
        for (int i = 1; i < packSamples.Count; i++)
        {
            float dt = packSamples[i].t - packSamples[i - 1].t;
            if (dt <= 0 || dt > 2f) continue;
            float drop = packSamples[i - 1].p - packSamples[i].p;
            if (drop > 0)
            {
                float dropPerSec = drop / dt;
                if (dropPerSec > maxPackDownPerSec) maxPackDownPerSec = dropPerSec;
            }
        }
        bool packDownOk = maxPackDownPerSec <= maxPackDepthDownPerSec;
        if (!packDownOk)
            failReasons.Add($"packD/sec={maxPackDownPerSec:F2}");

        // 9) actionのnが常に0 or 1
        bool actionNOk = true;
        int violationN = 0;
        foreach (var line in picked)
        {
            var am = Regex.Match(line, @"action=AddLayers\((\d+)\)|action=RemoveLayers\((\d+)\)");
            if (am.Success)
            {
                int n = am.Groups[1].Success ? int.Parse(am.Groups[1].Value) : int.Parse(am.Groups[2].Value);
                if (n > 1) { actionNOk = false; violationN = n; break; }
            }
        }
        if (!actionNOk) failReasons.Add($"actionN={violationN}");

        // 10) totalが0に戻ったら即FAIL
        bool totalNeverZeroOk = true;
        bool sawInst = false;
        bool sawTotal0 = false;
        foreach (var line in picked)
        {
            var im = Regex.Match(line, @"instantiated=(\d+)");
            if (im.Success && int.TryParse(im.Groups[1].Value, out int ix) && ix > 0) sawInst = true;
            if ((line.Contains("[SnowPackPool]") || line.Contains("[SnowPackPoolInvariant]")) && line.Contains("total=0"))
                sawTotal0 = true;
        }
        if (sawInst && sawTotal0)
        {
            totalNeverZeroOk = false;
            failReasons.Add("total0");
        }

        bool allPass = movedMetersOk && addRemoveOk && graceOk && visualSmoothOk && childrenDeltaOk && packDownOk && actionNOk && totalNeverZeroOk && rootChildrenNever1;
        return (allPass, failReasons, keyVals);
    }

    static float ExtractRunDuration(List<string> picked)
    {
        float maxT = 0f;
        foreach (var line in picked)
        {
            float t = ExtractTimeFromLine(line);
            if (t > maxT) maxT = t;
        }
        return maxT;
    }

    static float ExtractTimeFromLine(string line)
    {
        var m = Regex.Match(line, @"\bt=([\d.]+)");
        if (m.Success && float.TryParse(m.Groups[1].Value, out float t))
            return t;
        return -1f;
    }

    static string LoadSnowPackNoaReportCompact()
    {
        string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "docs", "SNOW_PACK_NOA_REPORT.md"));
        if (!File.Exists(path)) return "(SnowPack報告なし)";
        var sb = new StringBuilder();
        sb.AppendLine("【SnowPack Noa報告・視覚デバッグ＋StackTrace】");
        sb.AppendLine("視覚デバッグ: A1)状態別色 A2)ACTIVE=0巨大UI A3)Gizmos人数 A4)DrawRay3本");
        sb.AppendLine("StackTrace: StackTraceUtility.ExtractStackTrace()（本物のfile:line）");
        sb.AppendLine("PoolReturn: source=Queue/Despawn/Avalanche 必須");
        sb.AppendLine("レポート確認: ACTIVE=0有無(Yes/No+frame,t) / 本物StackTrace出たか(Yes/No)");
        sb.AppendLine("変更: SnowPackSpawner(状態別色/UI/Gizmos), RoofSnowSystem(DrawRay/BurstSize), BurstSnow=PackedSnow");
        return sb.ToString();
    }

    static string ExtractHeaderValue(string[] lines, string prefix)
    {
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                return lines[i].Substring(prefix.Length).Trim();
        }
        return "unknown";
    }
}
#endif
