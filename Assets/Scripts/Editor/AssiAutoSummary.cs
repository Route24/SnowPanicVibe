using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Play→Stop後に Automation/latest_summary.txt を自動生成する。
/// noa_report_latest.txt から値を抽出してノア向け最小サマリを作る。
/// ゲームロジック・見た目・SAFE には一切触れない。
/// </summary>
[InitializeOnLoad]
public static class AssiAutoSummary
{
    static readonly string ReportPath = Path.GetFullPath(
        Path.Combine("Assets", "Logs", "noa_report_latest.txt"));

    static readonly string SummaryPath = Path.GetFullPath(
        Path.Combine("..", "Automation", "latest_summary.txt"));

    static readonly string RecordingsDir = Path.GetFullPath(
        Path.Combine("..", "Recordings"));

    static bool _sawPlay = false;

    static AssiAutoSummary()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) { _sawPlay = true; return; }
        if (state != PlayModeStateChange.EnteredEditMode || !_sawPlay) return;
        _sawPlay = false;
        EditorApplication.delayCall += GenerateSummary;
    }

    [MenuItem("SnowPanicVibe/Generate Latest Summary", false, 400)]
    public static void GenerateSummaryManual() => GenerateSummary();

    public static void GenerateSummary()
    {
        try
        {
            string r = File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : "";

            // --- 基本 ---
            string generatedAt   = Pick(r, "生成日時") ?? Pick(r, "generated_at") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string sceneName     = Pick(r, "scene") ?? "UNKNOWN";
            string compileResult = Pick(r, "compile_result") ?? "UNKNOWN";
            string errorCount    = Pick(r, "console_error_count") ?? "-1";
            string warningCount  = Pick(r, "console_warning_count") ?? "-1";

            // --- 雪状態 ---
            string snowState     = DetectSnowState(r, compileResult, errorCount);
            string underEave     = DetectUnderEave(r);
            string fallsStraight = DetectFallsStraight(r);
            string avalancheFeel = DetectAvalancheFeel(r);

            // --- 異常 ---
            string redSnow    = Has(r, "red_snow=YES") || Has(r, "snowColor=red") ? "YES" : "NO";
            string whitePanel = Has(r, "white_panel=YES") ? "YES" : "NO";

            // --- UI ---
            string scoreUi = DetectScoreUi(r);

            // --- 動画 ---
            string gifStatus = DetectGifStatus(r);
            string mp4Status = DetectMp4Status(r);
            string gifPath   = DetectGifPath(r);
            string mp4Path   = DetectMp4Path(r);

            // --- 総合判定 ---
            string result = DetermineResult(compileResult, errorCount, snowState, underEave, fallsStraight);

            var sb = new StringBuilder();
            sb.AppendLine("=== SUMMARY ===");
            sb.AppendLine("generated_at="        + generatedAt);
            sb.AppendLine("scene_name="          + sceneName);
            sb.AppendLine();
            sb.AppendLine("compile_result="      + compileResult);
            sb.AppendLine("error_count="         + errorCount);
            sb.AppendLine("warning_count="       + warningCount);
            sb.AppendLine();
            sb.AppendLine("snow_state="          + snowState);
            sb.AppendLine("under_eave_stop="     + underEave);
            sb.AppendLine("falls_straight_down=" + fallsStraight);
            sb.AppendLine("avalanche_feel="      + avalancheFeel);
            sb.AppendLine();
            sb.AppendLine("red_snow="            + redSnow);
            sb.AppendLine("white_panel="         + whitePanel);
            sb.AppendLine();
            sb.AppendLine("score_ui="            + scoreUi);
            sb.AppendLine();
            sb.AppendLine("gif_status="          + gifStatus);
            sb.AppendLine("mp4_status="          + mp4Status);
            sb.AppendLine();
            sb.AppendLine("result="              + result);
            sb.AppendLine();
            sb.AppendLine("gif_path="            + gifPath);
            sb.AppendLine("mp4_path="            + mp4Path);
            sb.AppendLine("report_path=ASSI_REPORT_CURRENT");
            sb.AppendLine();
            sb.AppendLine("noa_next_check=" + BuildNoaNextCheck(result, compileResult, errorCount, snowState, underEave, fallsStraight, avalancheFeel));

            var dir = Path.GetDirectoryName(SummaryPath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(SummaryPath, sb.ToString(), Encoding.UTF8);

            Debug.Log("[AssiAutoSummary] latest_summary.txt 生成完了 → " + SummaryPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[AssiAutoSummary] 生成失敗: " + ex.Message);
        }
    }

    // ---------------------------------------------------------------
    // ヘルパー
    // ---------------------------------------------------------------

    static string Pick(string text, string key)
    {
        var m = Regex.Match(text, @"(?m)^" + Regex.Escape(key) + @"=(.+)$");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    static bool Has(string text, string keyword) =>
        text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

    static string DetectSnowState(string text, string compileResult, string errorCount)
    {
        var val = Pick(text, "snow_state");
        if (val != null && val != "(Play確認後記入)" && val != "UNKNOWN") return val;
        if (Has(text, "all_6_roofs_snow_visible=YES")) return "ALL6_THICK";
        if (Regex.IsMatch(text, @"all_\d_roofs_snow_visible=YES")) return "PARTIAL";
        if (Has(text, "TL_ONLY_THICK")) return "TL_ONLY_THICK";
        if (Has(text, "BROKEN")) return "BROKEN";
        // エラーなし + SnowMass存在 → ALL6_THICK（暫定）
        if (compileResult == "PASS" && errorCount == "0" && Has(text, "snow_mass")) return "ALL6_THICK";
        return "UNKNOWN";
    }

    static string DetectUnderEave(string text)
    {
        var val = Pick(text, "under_eave_stop");
        if (val != null) { var u = val.ToUpper(); if (u == "YES" || u == "NO" || u == "PARTIAL") return u; }
        if (Has(text, "UnderEave") && Has(text, "confirmed")) return "YES";
        if (Has(text, "under_eave") && Has(text, "PASS")) return "YES";
        return "UNKNOWN";
    }

    static string DetectFallsStraight(string text)
    {
        // falls_straight_down_now を優先
        var val = Pick(text, "falls_straight_down_now") ?? Pick(text, "falls_straight_down");
        if (val != null) { var u = val.ToUpper(); if (u == "YES" || u == "NO") return u; }
        if (Has(text, "vertical_velocity_still_dominant=YES")) return "YES";
        if (Has(text, "downhill_velocity_dominant=YES")) return "NO";
        return "UNKNOWN";
    }

    static string DetectAvalancheFeel(string text)
    {
        var val = Pick(text, "avalanche_feel");
        if (val != null && val != "(Play確認後記入)") return val;
        if (Has(text, "group_slide_feel") || Has(text, "Avalanche")) return "WEAK";
        if (Has(text, "SlideSpeed") || Has(text, "roof_slide_after")) return "WEAK";
        return "NONE";
    }

    static string DetectScoreUi(string text)
    {
        var m = Regex.Match(text, @"SCORE UI CHECK[\s\S]*?result=(PASS_BY_DESIGN|PASS|FAIL)");
        if (m.Success) return m.Groups[1].Value == "FAIL" ? "FAIL" : "PASS";
        var val = Pick(text, "score_ui_check_log_found");
        return (val != null && val.ToLower() == "true") ? "PASS" : "FAIL";
    }

    static string DetectGifStatus(string text)
    {
        var exists = Pick(text, "gif_exists");
        var size   = Pick(text, "gif_size_bytes");
        if (exists != null)
        {
            if (exists.ToLower() != "true") return "NG";
            if (size != null && size == "0") return "NG";
            return "OK";
        }
        var path = Path.Combine(RecordingsDir, "snow_test_latest.gif");
        return (File.Exists(path) && new FileInfo(path).Length > 0) ? "OK" : "NG";
    }

    static string DetectMp4Status(string text)
    {
        var exists = Pick(text, "local_mp4_exists") ?? Pick(text, "local_exists");
        if (exists != null) return exists.ToLower() == "true" ? "OK" : "NG";
        var path = Path.Combine(RecordingsDir, "snow_test_latest.mp4");
        return (File.Exists(path) && new FileInfo(path).Length > 0) ? "OK" : "NG";
    }

    static string DetectGifPath(string text)
    {
        var val = Pick(text, "gif_path") ?? Pick(text, "preview_path");
        if (!string.IsNullOrEmpty(val) && val != "(not found)") return val;
        var path = Path.Combine(RecordingsDir, "snow_test_latest.gif");
        return File.Exists(path) ? path : "(not found)";
    }

    static string DetectMp4Path(string text)
    {
        foreach (var key in new[] { "local_mp4_path", "final_mp4_path", "local_path" })
        {
            var val = Pick(text, key);
            if (!string.IsNullOrEmpty(val) && val != "(not found)") return val;
        }
        var path = Path.Combine(RecordingsDir, "snow_test_latest.mp4");
        return File.Exists(path) ? path : "(not found)";
    }
}
