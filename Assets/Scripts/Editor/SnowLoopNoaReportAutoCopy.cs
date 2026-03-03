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
    static readonly string ReportPath = Path.GetFullPath(Path.Combine("Assets", "Logs", "noa_report_latest.txt"));
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

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            _sawPlayMode = true;
            return;
        }

        if (state != PlayModeStateChange.EnteredEditMode || !_sawPlayMode) return;
        _sawPlayMode = false;
        TryBuildAndCopyReport();
    }

    static bool TryBuildAndCopyReport()
    {
        if (!File.Exists(LogPath))
        {
            Debug.LogWarning($"[NOAReportAuto] log file not found: {LogPath}");
            return false;
        }

        string[] lines = File.ReadAllLines(LogPath);
        if (lines.Length == 0)
        {
            Debug.LogWarning("[NOAReportAuto] log file is empty.");
            return false;
        }

        int latestRunId = DetectLatestRunId(lines);
        var picked = CollectTargetLines(lines, latestRunId);
        string report = BuildReport(lines, picked, latestRunId);

        try
        {
            var dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(ReportPath, report);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NOAReportAuto] failed to write report file: {ex.Message}");
        }

        EditorGUIUtility.systemCopyBuffer = report;
        return true;
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

            // MVP + legacy tags (incl. [SnowPackSync] [SnowPackBasis] [SnowFallMax1s] [GroundVisual] [AvalancheVisual])
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

    static string BuildReport(string[] allLines, List<string> picked, int latestRunId)
    {
        string startedAt = ExtractHeaderValue(allLines, "started_at=");
        string unityVersion = ExtractHeaderValue(allLines, "unity_version=");
        string scene = ExtractHeaderValue(allLines, "scene=");

        var sb = new StringBuilder();
        sb.AppendLine("【ASSI REPORT】");
        sb.AppendLine();
        sb.AppendLine($"- started_at: {startedAt}");
        sb.AppendLine($"- unity_version: {unityVersion}");
        sb.AppendLine($"- scene: {scene}");
        sb.AppendLine($"- runId: {(latestRunId >= 0 ? latestRunId.ToString() : "N/A")}");
        sb.AppendLine();
        sb.AppendLine("=== ASSI CONSOLE LOG START ===");
        for (int i = 0; i < picked.Count; i++)
            sb.AppendLine(picked[i]);
        sb.AppendLine("=== ASSI CONSOLE LOG END ===");
        sb.AppendLine();
        sb.AppendLine("=== ASSI ANALYSIS ===");
        sb.AppendLine(BuildAssiAnalysis(picked));
        sb.AppendLine("=== ASSI ANALYSIS END ===");
        sb.AppendLine();
        sb.AppendLine("【次のアシへの依頼テンプレ】");
        sb.AppendLine("- Play中は AssetDatabase.SaveAssets() を呼ばない（Editor限定・非Play時のみ）。");
        sb.AppendLine("- SnowPack は RoofSnow.depth に同期して表示更新（DepthSync）。");
        sb.AppendLine("- 例外が出たら [ExceptionOrigin] と stack trace を最優先で確認。");
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
        sb.AppendLine($"- Clear回数: {clearCount}, REBUILD回数: {rebuildCount}");
        sb.AppendLine($"- 最初のreason: {firstReason}");
        var depthSample = depthVals.Count > 5 ? string.Join(",", depthVals.Skip(depthVals.Count - 5)) : string.Join(",", depthVals);
        var childSample = childVals.Count > 5 ? string.Join(",", childVals.Skip(childVals.Count - 5)) : string.Join(",", childVals);
        sb.AppendLine($"- DepthとchildCount: roofDepth={depthSample} | children={childSample}");
        sb.AppendLine();
        sb.AppendLine(BuildAutoVerification(picked));
        return sb.ToString();
    }

    static string BuildAutoVerification(List<string> picked)
    {
        const float movedMetersMin = 0.5f, movedMetersMax = 3.0f;
        const int addRemovePerSecLimit = 6;

        var results = new List<string>();
        results.Add("=== 自動判定 (PASS/FAIL) ===");
        var failReasons = new List<string>();

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
                failReasons.Add($"movedMeters={last:F3} (範囲外: 要0.5〜3.0)");
        }
        results.Add($"1) movedMeters範囲(0.5〜3.0m): {(movedMetersOk ? "PASS" : "FAIL")} 値={movedMetersVal}");

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
            failReasons.Add($"add+remove/秒={perSec:F1} (上限={addRemovePerSecLimit}) 合計={addRemoveTotal} 実行秒={runDuration:F1}");
        results.Add($"2) add+remove/秒<={addRemovePerSecLimit}: {(addRemoveOk ? "PASS" : "FAIL")} addCount={addCount} removeCount={removeCount} 合計/秒={perSec:F1} 実行秒={runDuration:F1}");

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
            failReasons.Add($"Grace: fired2s={firedInFirst2s} (要0) suppressed2s={suppressedInFirst2s} (要>0)");
        results.Add($"3) Grace抑止(2秒内fired=0,suppressed>0): {(graceOk ? "PASS" : "FAIL")} fired2s={firedInFirst2s} suppressed2s={suppressedInFirst2s}");

        bool allPass = movedMetersOk && addRemoveOk && graceOk;
        results.Add("");
        results.Add(allPass ? "判定: ALL PASS" : $"判定: FAIL ({string.Join(" / ", failReasons)})");

        return string.Join(Environment.NewLine, results);
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
