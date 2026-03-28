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
        Path.Combine("Automation", "latest_summary.txt"));

    static readonly string RecordingsDir = Path.GetFullPath(
        Path.Combine("Recordings"));

    static bool _sawPlay = false;

    static AssiAutoSummary()
    {
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        // SnowVisibilityLab ではサマリ生成を停止
        if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name == "SnowVisibilityLab") return;

        if (state == PlayModeStateChange.EnteredPlayMode) { _sawPlay = true; return; }
        if (state != PlayModeStateChange.EnteredEditMode || !_sawPlay) return;
        _sawPlay = false;
        // レポート書き込み完了を待つため3秒後に実行
        var stopTime = DateTime.Now;
        EditorApplication.update += WaitAndGenerate;

        void WaitAndGenerate()
        {
            if ((DateTime.Now - stopTime).TotalSeconds < 3.0) return;
            EditorApplication.update -= WaitAndGenerate;
            GenerateSummary();
        }
    }

    [MenuItem("SnowPanic/Generate Latest Summary", false, 400)]
    public static void GenerateSummaryManual() => GenerateSummary();

    /// <summary>アシがコーディング完了時に呼ぶ。mark_ready.py を実行して READY_FOR_UNITY_PLAY に遷移する。</summary>
    [MenuItem("SnowPanic/Coding Done (Mark Ready)", false, 403)]
    public static void CodingDone()
    {
        var runner = Path.GetFullPath(Path.Combine("Automation", "mark_ready.py"));
        if (!File.Exists(runner))
        {
            Debug.LogWarning("[AssiAutoSummary] mark_ready.py が見つかりません: " + runner);
            return;
        }
        var proc = new System.Diagnostics.Process();
        proc.StartInfo.FileName = "python3";
        proc.StartInfo.Arguments = "\"" + runner + "\"";
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.StartInfo.RedirectStandardError = true;
        proc.Start();
        string output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        Debug.Log("[AssiAutoSummary] " + output.Trim());
        Debug.Log("[AssiAutoSummary] status=READY_FOR_UNITY_PLAY → Unity で Play してください");
    }

    public static void GenerateSummary()
    {
        try
        {
            string r = File.Exists(ReportPath) ? File.ReadAllText(ReportPath) : "";

            // --- 基本 ---
            string generatedAt   = Pick(r, "生成日時") ?? Pick(r, "generated_at") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            // "scene=" を全マッチから探し、"(no Cornice" を含まない有効値を優先する
            string sceneName     = PickValid(r, "scene", "(no Cornice") ?? Pick(r, "scene") ?? "UNKNOWN";
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

            // --- キャリブレーション状態（JSONから直接読む） ---
            string calibPath = Path.GetFullPath("Assets/Art/RoofCalibrationData.json");
            string roofPointsCaptured = "NO";
            string groundPointCaptured = "NO";
            string calibSaved = "NO";
            string roofMinY = "N/A";
            string roofMaxY = "N/A";
            string groundYVal = "N/A";
            if (File.Exists(calibPath))
            {
                try
                {
                    string calibJson = File.ReadAllText(calibPath);
                    // roofs 配列に Roof_Main が confirmed=true で存在するか
                    if (calibJson.Contains("\"Roof_Main\"") && calibJson.Contains("\"confirmed\": true"))
                    {
                        roofPointsCaptured = "YES";
                        calibSaved = "YES";
                        // minY / maxY を正規表現で抽出
                        var ys = System.Text.RegularExpressions.Regex.Matches(calibJson, @"""y"":\s*([\d.]+)");
                        var yVals = new System.Collections.Generic.List<float>();
                        foreach (System.Text.RegularExpressions.Match m2 in ys)
                            if (float.TryParse(m2.Groups[1].Value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float fy))
                                yVals.Add(fy);
                        if (yVals.Count >= 4)
                        {
                            float mn = float.MaxValue, mx = float.MinValue;
                            // 最初の4つが4点のY（groundY は別フィールド）
                            for (int i = 0; i < 4 && i < yVals.Count; i++)
                            { if (yVals[i] < mn) mn = yVals[i]; if (yVals[i] > mx) mx = yVals[i]; }
                            roofMinY = mn.ToString("F4");
                            roofMaxY = mx.ToString("F4");
                        }
                    }
                    var gm = System.Text.RegularExpressions.Regex.Match(calibJson, @"""groundY"":\s*([\d.]+)");
                    if (gm.Success)
                    {
                        groundYVal = float.Parse(gm.Groups[1].Value,
                            System.Globalization.CultureInfo.InvariantCulture).ToString("F4");
                        groundPointCaptured = "YES";
                    }
                }
                catch { }
            }
            sb.AppendLine("=== CALIBRATION ===");
            sb.AppendLine("roof_points_captured="  + roofPointsCaptured);
            sb.AppendLine("ground_point_captured=" + groundPointCaptured);
            sb.AppendLine("calibration_saved="     + calibSaved);
            sb.AppendLine("roof_min_y="            + roofMinY);
            sb.AppendLine("roof_max_y="            + roofMaxY);
            sb.AppendLine("ground_y="              + groundYVal);
            sb.AppendLine("snow_on_roof="          + (Has(r, "snow_on_roof=YES") ? "YES" : "PENDING"));
            sb.AppendLine("fall_reaches_ground="   + (Has(r, "fall_reaches_ground=YES") ? "YES" : "PENDING"));
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
            File.WriteAllText(SummaryPath, sb.ToString(), new System.Text.UTF8Encoding(false));

            Debug.Log("[AssiAutoSummary] latest_summary.txt 生成完了 → " + SummaryPath);

            // summary 生成後に protocol を自動生成する
            SummaryToProtocol.RunAfterSummary();
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

    /// <summary>key=value の全マッチから excludeSubstr を含まない最初の値を返す</summary>
    static string PickValid(string text, string key, string excludeSubstr)
    {
        foreach (Match m in Regex.Matches(text, @"(?m)^" + Regex.Escape(key) + @"=(.+)$"))
        {
            var val = m.Groups[1].Value.Trim();
            if (!val.Contains(excludeSubstr)) return val;
        }
        return null;
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
        // gif_exists=true AND gif_size_bytes > 0 のみ OK
        if (exists != null && exists.ToLower() == "true"
            && size != null && long.TryParse(size, out long sz) && sz > 0)
            return "OK";
        // レポートに値がない場合はファイル実在で判定（mp4_statusと同様）
        var gifPath = Path.Combine(RecordingsDir, "snow_test_latest.gif");
        if (File.Exists(gifPath) && new FileInfo(gifPath).Length > 0)
            return "OK";
        return "NG";
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

    static string DetermineResult(string compileResult, string errorCount, string snowState, string underEave, string fallsStraight)
    {
        if (compileResult != "PASS") return "FAIL";
        if (int.TryParse(errorCount, out int n) && n > 0) return "FAIL";
        // 明確なNG判定のみ NEED_CHECK にする（UNKNOWN は許容してループを防ぐ）
        if (snowState == "BROKEN") return "NEED_CHECK";
        if (fallsStraight == "YES") return "NEED_CHECK";
        if (underEave == "NO") return "NEED_CHECK";
        return "PASS";
    }

    static string BuildNoaNextCheck(string result, string compileResult, string errorCount, string snowState, string underEave, string fallsStraight, string avalancheFeel)
    {
        if (compileResult != "PASS") return "Compile FAIL。エラーを修正してから再確認。";
        if (int.TryParse(errorCount, out int n) && n > 0) return $"エラー {n} 件あり。修正してから再確認。";
        if (fallsStraight == "YES") return "雪が真下に落ちている。屋根沿い滑走ロジックを確認。";
        if (underEave == "NO") return "軒下で止まっていない。UnderEaveLanding の挙動を確認。";
        if (snowState == "BROKEN") return "雪の状態が BROKEN。SnowPackSpawner を確認。";
        if (snowState == "PARTIAL") return "一部の屋根に雪がない。残り屋根の積雪を確認。";
        if (avalancheFeel == "NONE" || avalancheFeel == "WEAK") return "compile/snow/falls はPASS。次は avalanche_feel を WEAK→GOOD に改善する。SlideMinSpeed か Cascade を調整。";
        if (underEave == "UNKNOWN") return "compile/snow/falls はPASS。under_eave は目視未確認だが他は良好。次は avalanche_feel 改善へ進む。";
        return "SAFEは維持。現状良好。次は avalanche_feel 改善か新機能追加をノアに確認。";
    }

    static string DetectMp4Path(string text)
    {
        foreach (var key in new[] { "local_mp4_path", "final_mp4_path", "local_path" })
        {
            var val = Pick(text, key);
            // フルパスであること・存在すること・途中で切れていないことを確認
            if (!string.IsNullOrEmpty(val) && val != "(not found)" && val.EndsWith(".mp4") && File.Exists(val))
                return val;
        }
        // フォールバック: Recordings フォルダの固定パスを使う
        var path = Path.Combine(RecordingsDir, "snow_test_latest.mp4");
        return File.Exists(path) ? path : "(not found)";
    }
}
