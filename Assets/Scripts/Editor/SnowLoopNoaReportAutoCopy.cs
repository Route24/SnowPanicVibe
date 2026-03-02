#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SnowLoopNoaReportAutoCopy
{
    static readonly string LogPath = Path.GetFullPath(Path.Combine("Assets", "Logs", "snowloop_latest.txt"));
    static readonly Regex RunIdRegex = new Regex(@"runId=(\d+)", RegexOptions.Compiled);
    static bool _sawPlayMode;

    static SnowLoopNoaReportAutoCopy()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
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

    static void TryBuildAndCopyReport()
    {
        if (!File.Exists(LogPath))
        {
            Debug.LogWarning($"[NOAReportAuto] log file not found: {LogPath}");
            return;
        }

        string[] lines = File.ReadAllLines(LogPath);
        if (lines.Length == 0)
        {
            Debug.LogWarning("[NOAReportAuto] log file is empty.");
            return;
        }

        int latestRunId = DetectLatestRunId(lines);
        var picked = CollectTargetLines(lines, latestRunId);
        string report = BuildReport(lines, picked, latestRunId);

        EditorGUIUtility.systemCopyBuffer = report;
        Debug.Log("[NOAReportAuto] NOA REPORT copied to clipboard.");
    }

    static int DetectLatestRunId(string[] lines)
    {
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
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

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

            if (line.Contains("[SnowLoop")
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
        sb.AppendLine("【NOA REPORT / auto-generated on Play Stop】");
        sb.AppendLine();
        sb.AppendLine($"- started_at: {startedAt}");
        sb.AppendLine($"- unity_version: {unityVersion}");
        sb.AppendLine($"- scene: {scene}");
        sb.AppendLine($"- runId: {(latestRunId >= 0 ? latestRunId.ToString() : "N/A")}");
        sb.AppendLine();
        sb.AppendLine("【Target Logs】");
        for (int i = 0; i < picked.Count; i++)
            sb.AppendLine(picked[i]);
        sb.AppendLine();
        sb.AppendLine("【次のアシへの依頼テンプレ】");
        sb.AppendLine("- Play中は AssetDatabase.SaveAssets() を呼ばない（Editor限定・非Play時のみ）。");
        sb.AppendLine("- forceAvalancheNow=true のワンショットで分岐経路だけを先に検証。");
        sb.AppendLine("- [SnowLoop] の landingCount / addPerLanding / nextSpawnIn を維持して原因追跡。");
        sb.AppendLine("- 必要なら addPerLanding を一時的に上げて load>=threshold 到達を短縮。");
        sb.AppendLine("- 例外が出たら [ExceptionOrigin] と stack trace を最優先で確認。");
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
