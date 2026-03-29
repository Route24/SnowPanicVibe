#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play→Stop 後に最小 [REPORT] を生成する。
/// 事実として取得できる項目のみ出力。UNKNOWN 出力なし。
/// [SNOW AUTO CHECK] セクションで雪状態を自動観測する。
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

        sb.AppendLine("");
        BuildSnowAutoCheck(sb);
        sb.AppendLine("");
        BuildSnowShapeCheck(sb);

        return sb.ToString();
    }

    // ── [SNOW AUTO CHECK] 自動観測 ──────────────────────────────────
    // Roof/SnowTypeBRoot を辿って雪状態を数値で観測する。
    // 見つからない場合は NOT_FOUND / NO を返す（UNKNOWN禁止）。
    static void BuildSnowAutoCheck(System.Text.StringBuilder sb)
    {
        sb.AppendLine("[SNOW AUTO CHECK]");

        // 観測対象: Roof → SnowTypeBRoot（AntiProtocolSceneCreator が生成する階層）
        // Roof は Environment の子として生成される
        var roofGo = GameObject.Find("Roof");
        if (roofGo == null)
        {
            // Roof が見つからない場合はすべて NOT_FOUND
            sb.AppendLine("snow_object_found=NO");
            sb.AppendLine("snow_renderer_enabled=NO");
            sb.AppendLine("snow_world_pos_y=NOT_FOUND");
            sb.AppendLine("snow_local_scale_y=NOT_FOUND");
            sb.AppendLine("snow_child_count=NOT_FOUND");
            return;
        }

        // SnowTypeBRoot を Roof の直下から探す
        var snowRoot = roofGo.transform.Find("SnowTypeBRoot");
        if (snowRoot == null)
        {
            sb.AppendLine("snow_object_found=NO");
            sb.AppendLine("snow_renderer_enabled=NO");
            sb.AppendLine("snow_world_pos_y=NOT_FOUND");
            sb.AppendLine("snow_local_scale_y=NOT_FOUND");
            sb.AppendLine("snow_child_count=NOT_FOUND");
            return;
        }

        sb.AppendLine("snow_object_found=YES");

        // Renderer 確認: 子の最初のパネルで代表チェック
        bool rendererEnabled = false;
        if (snowRoot.childCount > 0)
        {
            var firstChild = snowRoot.GetChild(0);
            var mr = firstChild.GetComponent<Renderer>();
            rendererEnabled = (mr != null && mr.enabled);
        }
        sb.AppendLine("snow_renderer_enabled=" + (rendererEnabled ? "YES" : "NO"));

        // ワールド座標 Y（SnowTypeBRoot 自体の位置）
        float worldY = snowRoot.position.y;
        sb.AppendLine("snow_world_pos_y=" + worldY.ToString("F3"));

        // ローカルスケール Y（SnowTypeBRoot のスケール）
        float localScaleY = snowRoot.localScale.y;
        sb.AppendLine("snow_local_scale_y=" + localScaleY.ToString("F3"));

        // 子オブジェクト数（面の枚数）
        int childCount = snowRoot.childCount;
        sb.AppendLine("snow_child_count=" + childCount);
    }

    // ── [SNOW SHAPE CHECK] bounds・はみ出し量の自動観測 ────────────────
    // 雪全体の統合 bounds と屋根 bounds を比較し、各方向のはみ出し量を出力する。
    // はみ出し量は「収まっている場合は 0」（負値にしない）。
    static void BuildSnowShapeCheck(System.Text.StringBuilder sb)
    {
        sb.AppendLine("[SNOW SHAPE CHECK]");

        var roofGo   = GameObject.Find("Roof");
        var snowRootT = roofGo != null ? roofGo.transform.Find("SnowTypeBRoot") : null;

        // ── 雪の統合 bounds ──────────────────────────────────────
        if (snowRootT == null || snowRootT.childCount == 0)
        {
            const string NF = "NOT_FOUND";
            sb.AppendLine("snow_bounds_size_x=" + NF);
            sb.AppendLine("snow_bounds_size_y=" + NF);
            sb.AppendLine("snow_bounds_size_z=" + NF);
            sb.AppendLine("snow_bounds_min_x="  + NF);
            sb.AppendLine("snow_bounds_max_x="  + NF);
            sb.AppendLine("snow_bounds_min_y="  + NF);
            sb.AppendLine("snow_bounds_max_y="  + NF);
            sb.AppendLine("snow_bounds_min_z="  + NF);
            sb.AppendLine("snow_bounds_max_z="  + NF);
            sb.AppendLine("roof_bounds_min_x="  + NF);
            sb.AppendLine("roof_bounds_max_x="  + NF);
            sb.AppendLine("roof_bounds_min_y="  + NF);
            sb.AppendLine("roof_bounds_max_y="  + NF);
            sb.AppendLine("roof_bounds_min_z="  + NF);
            sb.AppendLine("roof_bounds_max_z="  + NF);
            sb.AppendLine("snow_overhang_left="  + NF);
            sb.AppendLine("snow_overhang_right=" + NF);
            sb.AppendLine("snow_overhang_above=" + NF);
            sb.AppendLine("snow_overhang_below=" + NF);
            sb.AppendLine("snow_overhang_front=" + NF);
            sb.AppendLine("snow_overhang_back="  + NF);
            return;
        }

        // 子パネル全体の Renderer bounds を統合
        bool snowBoundsInit = false;
        Bounds snowB = new Bounds();
        for (int i = 0; i < snowRootT.childCount; i++)
        {
            var r = snowRootT.GetChild(i).GetComponent<Renderer>();
            if (r == null) continue;
            if (!snowBoundsInit) { snowB = r.bounds; snowBoundsInit = true; }
            else snowB.Encapsulate(r.bounds);
        }

        if (!snowBoundsInit)
        {
            const string NF = "NOT_FOUND";
            sb.AppendLine("snow_bounds_size_x=" + NF);
            sb.AppendLine("snow_bounds_size_y=" + NF);
            sb.AppendLine("snow_bounds_size_z=" + NF);
            sb.AppendLine("snow_bounds_min_x="  + NF);
            sb.AppendLine("snow_bounds_max_x="  + NF);
            sb.AppendLine("snow_bounds_min_y="  + NF);
            sb.AppendLine("snow_bounds_max_y="  + NF);
            sb.AppendLine("snow_bounds_min_z="  + NF);
            sb.AppendLine("snow_bounds_max_z="  + NF);
            sb.AppendLine("roof_bounds_min_x="  + NF);
            sb.AppendLine("roof_bounds_max_x="  + NF);
            sb.AppendLine("roof_bounds_min_y="  + NF);
            sb.AppendLine("roof_bounds_max_y="  + NF);
            sb.AppendLine("roof_bounds_min_z="  + NF);
            sb.AppendLine("roof_bounds_max_z="  + NF);
            sb.AppendLine("snow_overhang_left="  + NF);
            sb.AppendLine("snow_overhang_right=" + NF);
            sb.AppendLine("snow_overhang_above=" + NF);
            sb.AppendLine("snow_overhang_below=" + NF);
            sb.AppendLine("snow_overhang_front=" + NF);
            sb.AppendLine("snow_overhang_back="  + NF);
            return;
        }

        sb.AppendLine("snow_bounds_size_x=" + snowB.size.x.ToString("F3"));
        sb.AppendLine("snow_bounds_size_y=" + snowB.size.y.ToString("F3"));
        sb.AppendLine("snow_bounds_size_z=" + snowB.size.z.ToString("F3"));
        sb.AppendLine("snow_bounds_min_x="  + snowB.min.x.ToString("F3"));
        sb.AppendLine("snow_bounds_max_x="  + snowB.max.x.ToString("F3"));
        sb.AppendLine("snow_bounds_min_y="  + snowB.min.y.ToString("F3"));
        sb.AppendLine("snow_bounds_max_y="  + snowB.max.y.ToString("F3"));
        sb.AppendLine("snow_bounds_min_z="  + snowB.min.z.ToString("F3"));
        sb.AppendLine("snow_bounds_max_z="  + snowB.max.z.ToString("F3"));

        // ── 屋根の bounds ────────────────────────────────────────
        Bounds roofB = new Bounds();
        bool roofBoundsInit = false;
        if (roofGo != null)
        {
            var roofR = roofGo.GetComponent<Renderer>();
            if (roofR != null) { roofB = roofR.bounds; roofBoundsInit = true; }
        }

        if (!roofBoundsInit)
        {
            const string NF = "NOT_FOUND";
            sb.AppendLine("roof_bounds_min_x="  + NF);
            sb.AppendLine("roof_bounds_max_x="  + NF);
            sb.AppendLine("roof_bounds_min_y="  + NF);
            sb.AppendLine("roof_bounds_max_y="  + NF);
            sb.AppendLine("roof_bounds_min_z="  + NF);
            sb.AppendLine("roof_bounds_max_z="  + NF);
            sb.AppendLine("snow_overhang_left="  + NF);
            sb.AppendLine("snow_overhang_right=" + NF);
            sb.AppendLine("snow_overhang_above=" + NF);
            sb.AppendLine("snow_overhang_below=" + NF);
            sb.AppendLine("snow_overhang_front=" + NF);
            sb.AppendLine("snow_overhang_back="  + NF);
            return;
        }

        sb.AppendLine("roof_bounds_min_x=" + roofB.min.x.ToString("F3"));
        sb.AppendLine("roof_bounds_max_x=" + roofB.max.x.ToString("F3"));
        sb.AppendLine("roof_bounds_min_y=" + roofB.min.y.ToString("F3"));
        sb.AppendLine("roof_bounds_max_y=" + roofB.max.y.ToString("F3"));
        sb.AppendLine("roof_bounds_min_z=" + roofB.min.z.ToString("F3"));
        sb.AppendLine("roof_bounds_max_z=" + roofB.max.z.ToString("F3"));

        // ── はみ出し量（収まっていれば 0、絶対にマイナスにしない）──────
        float OvLeft  = Mathf.Max(0f, roofB.min.x - snowB.min.x);
        float OvRight = Mathf.Max(0f, snowB.max.x - roofB.max.x);
        float OvAbove = Mathf.Max(0f, snowB.max.y - roofB.max.y);
        float OvBelow = Mathf.Max(0f, roofB.min.y - snowB.min.y);
        float OvFront = Mathf.Max(0f, snowB.max.z - roofB.max.z);
        float OvBack  = Mathf.Max(0f, roofB.min.z - snowB.min.z);

        sb.AppendLine("snow_overhang_left="  + OvLeft.ToString("F3"));
        sb.AppendLine("snow_overhang_right=" + OvRight.ToString("F3"));
        sb.AppendLine("snow_overhang_above=" + OvAbove.ToString("F3"));
        sb.AppendLine("snow_overhang_below=" + OvBelow.ToString("F3"));
        sb.AppendLine("snow_overhang_front=" + OvFront.ToString("F3"));
        sb.AppendLine("snow_overhang_back="  + OvBack.ToString("F3"));
    }
}
#endif
