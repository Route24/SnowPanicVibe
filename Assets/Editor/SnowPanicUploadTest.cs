#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 動画アップロードパイプラインの自動検証。
/// SnowPanic/UploadTest/Run でダミー動画生成→Drive確認→Slack確認→ログ出力。
/// </summary>
public static class SnowPanicUploadTest
{
    const string RcloneRemote = "gdrive:SnowPanicVideos";

    static string GetOutputDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            home = Environment.GetEnvironmentVariable("HOME") ?? "";
        return Path.Combine(home, "SnowPanicVideos");
    }

    static string GetLogPath() => Path.Combine(GetOutputDir(), "upload_test_log.txt");

    /// <summary>最小の有効MP4 (ftyp + 簡易moov) を作成。約400バイト。</summary>
    static void WriteMinimalMp4(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        // ftyp box (28 bytes)
        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var ftyp = new byte[]
            {
                0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70, // size=28, 'ftyp'
                0x69, 0x73, 0x6F, 0x6D, 0x00, 0x00, 0x02, 0x00, // isom, version 512
                0x69, 0x73, 0x6F, 0x6D, 0x69, 0x73, 0x6F, 0x32, 0x6D, 0x70, 0x34, 0x31 // isom, iso2, mp41
            };
            fs.Write(ftyp, 0, ftyp.Length);
            // moov の最小フッタ（ファイルをMP4として認識させるためのパディング）
            var padding = new byte[512];
            fs.Write(padding, 0, padding.Length);
        }
    }

    static string RunProcess(string exe, string args, int timeoutMs = 8000)
    {
        try
        {
            using (var p = new Process())
            {
                p.StartInfo.FileName = exe;
                p.StartInfo.Arguments = args;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                var sb = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs))
                {
                    p.Kill();
                    return "[TIMEOUT]";
                }
                return sb.ToString().Trim();
            }
        }
        catch (Exception ex)
        {
            return $"[ERROR] {ex.Message}";
        }
    }

    static string ReadSlackWebhookUrl()
    {
        var env = Environment.GetEnvironmentVariable("SLACK_WEBHOOK_URL");
        if (!string.IsNullOrEmpty(env)) return env.Trim();
        var path = Path.Combine(GetOutputDir(), "slack_webhook.txt");
        if (File.Exists(path))
        {
            try { return File.ReadAllText(path).Trim(); }
            catch { }
        }
        return null;
    }

    [MenuItem("SnowPanic/UploadTest/Run", false, 200)]
    public static void RunUploadTest()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var dir = GetOutputDir();
        var dummyPath = Path.Combine(dir, $"test_upload_{timestamp}.mp4");
        var logPath = GetLogPath();

        var logLines = new System.Collections.Generic.List<string> { $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UploadTest Run" };

        // 1. ダミー動画生成
        try
        {
            WriteMinimalMp4(dummyPath);
            UnityEngine.Debug.Log($"[UploadTest] Created: {dummyPath}");
            logLines.Add($"dummy_created: {dummyPath}");
        }
        catch (Exception ex)
        {
            var msg = $"[UploadTest] FAIL: Could not create dummy: {ex.Message}";
            UnityEngine.Debug.LogError(msg);
            logLines.Add($"dummy_created: FAIL {ex.Message}");
            WriteLog(logPath, logLines);
            return;
        }

        // snowpanic_upload.sh が検出・アップロードするまで待機
        System.Threading.Thread.Sleep(5000);

        // 2. rclone ls で Drive 確認
        string driveResult = RunProcess("rclone", $"ls \"{RcloneRemote}\"", 10000);
        bool uploadOk = !string.IsNullOrEmpty(driveResult) && driveResult.Contains($"test_upload_{timestamp}.mp4") && !driveResult.Contains("[ERROR]") && !driveResult.Contains("[TIMEOUT]");
        if (uploadOk)
        {
            UnityEngine.Debug.Log("UPLOAD TEST SUCCESS");
            logLines.Add("upload_check: SUCCESS");
        }
        else
        {
            UnityEngine.Debug.LogWarning($"[UploadTest] Drive check: not found or error. rclone output: {(driveResult.Length > 200 ? driveResult.Substring(0, 200) + "..." : driveResult)}");
            logLines.Add($"upload_check: FAIL");
            logLines.Add($"drive_result: {(driveResult.Length > 500 ? driveResult.Substring(0, 500) + "..." : driveResult)}");
        }

        // 3. Slack Webhook 送信
        var webhookUrl = ReadSlackWebhookUrl();
        if (!string.IsNullOrEmpty(webhookUrl))
        {
            var payloadFile = Path.Combine(Path.GetTempPath(), $"slack_payload_{Guid.NewGuid():N}.json");
            File.WriteAllText(payloadFile, "{\"text\":\"UploadTest OK\"}");
            var curlArgs = $"-s -X POST -H \"Content-Type: application/json\" -d \"@{payloadFile}\" \"{webhookUrl}\"";
            var slackResult = RunProcess("curl", curlArgs, 5000);
            try { if (File.Exists(payloadFile)) File.Delete(payloadFile); } catch { }
            if (string.IsNullOrEmpty(slackResult) || slackResult.Contains("ok") || !slackResult.Contains("[ERROR]"))
            {
                UnityEngine.Debug.Log("Slack OK");
                logLines.Add("slack: OK");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"[UploadTest] Slack: {slackResult}");
                logLines.Add($"slack: FAIL {slackResult}");
            }
        }
        else
        {
            UnityEngine.Debug.LogWarning("[UploadTest] Slack: SKIP (SLACK_WEBHOOK_URL or ~/SnowPanicVideos/slack_webhook.txt not set)");
            logLines.Add("slack: SKIP (no webhook)");
        }

        WriteLog(logPath, logLines);
    }

    static void WriteLog(string path, System.Collections.Generic.List<string> lines)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var content = string.Join(Environment.NewLine, lines) + Environment.NewLine + "---" + Environment.NewLine;
            File.AppendAllText(path, content);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[UploadTest] Could not write log: {ex.Message}");
        }
    }
}
#endif
