#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 変更ファイル検出と protected_systems ガード。
/// git status / git diff で変更を検出し、ASSI REPORT に FILE CHANGE CHECK を出力。
/// </summary>
public static class FileChangeChecker
{
    /// <summary>protected_system -> 対象ファイル名（部分一致）</summary>
    static readonly Dictionary<string, string[]> ProtectedFilesBySystem = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        { "SnowPhysics", new[] { "SnowPackFallingPiece", "SnowClump", "SnowPieceAutoSettle", "SnowPhysicsScoreManager", "GroundSnow", "MvpSnowChunkMotion", "SnowFallSystem", "SnowfallEventBurst", "SnowDespawnLogger" } },
        { "SnowSpawner", new[] { "SnowPackSpawner" } },
        { "SnowAvalanche", new[] { "RoofSnowSystem", "TapToSlideOnRoof", "RoofSnow", "AvalancheFeedback", "RoofSnowCleanup", "RoofSnowMaskController", "RoofAlignToSnow" } },
        { "CameraController", new[] { "CameraOrbit", "CameraMatchAndSnowConfig" } },
        { "ParticleSystem", new[] { "SnowVisual", "RoofSnow" } },
    };

    /// <summary>FILE CHANGE CHECK セクションを生成。呼び出し元で ASSI Report に追加。</summary>
    public static string BuildFileChangeCheckSection(string allowedFilesCsv, string protectedSystemsCsv)
    {
        var sb = new StringBuilder();
        var allowed = ParseCsv(allowedFilesCsv);
        var changed = GetChangedFiles();
        var unexpected = new List<string>();
        var changedList = new List<string>();

        foreach (var f in changed)
        {
            string fileName = Path.GetFileName(f) ?? f;
            changedList.Add(fileName);
            if (IsProtectedFile(fileName) && !IsAllowed(fileName, allowed))
                unexpected.Add(fileName);
        }

        sb.AppendLine($"changed_files={string.Join(",", changedList)}");
        sb.AppendLine($"unexpected_changes={string.Join(",", unexpected)}");
        sb.AppendLine($"result={(unexpected.Count > 0 ? "FAIL" : "PASS")}");

        if (unexpected.Count > 0)
            UnityEngine.Debug.LogWarning($"[FileChangeChecker] PROTECTED SYSTEM MODIFIED: {string.Join(", ", unexpected)}");

        return sb.ToString();
    }

    /// <summary>CODE DIFF セクションを生成。</summary>
    public static string BuildCodeDiffSection(int maxLinesPerFile = 50)
    {
        var sb = new StringBuilder();
        var changed = GetChangedFiles();
        foreach (var f in changed.Take(10))
        {
            string diff = GetFileDiff(f, maxLinesPerFile);
            if (string.IsNullOrEmpty(diff)) continue;
            string fileName = Path.GetFileName(f) ?? f;
            sb.AppendLine($"file={fileName}");
            sb.AppendLine(diff);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    static HashSet<string> ParseCsv(string csv)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(csv)) return set;
        foreach (var s in csv.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = s.Trim();
            if (t.Length > 0) set.Add(Path.GetFileName(t));
        }
        return set;
    }

    static bool IsAllowed(string fileName, HashSet<string> allowed)
    {
        if (allowed.Count == 0) return false;
        return allowed.Contains(fileName) || allowed.Any(a => fileName.Contains(a) || a.Contains(fileName));
    }

    static bool IsProtectedFile(string fileName)
    {
        foreach (var kv in ProtectedFilesBySystem)
        {
            foreach (var pattern in kv.Value)
                if (fileName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        }
        return false;
    }

    static List<string> GetChangedFiles()
    {
        var list = new List<string>();
        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            string output = RunCommand("git", "status --short --porcelain", projectRoot);
            if (string.IsNullOrEmpty(output)) return list;
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length < 4) continue;
                string path = line.Substring(3).Trim();
                if (path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    list.Add(path);
            }
            list = list.Distinct().OrderBy(x => x).ToList();
        }
        catch (Exception ex) { UnityEngine.Debug.LogWarning($"[FileChangeChecker] GetChangedFiles: {ex.Message}"); }
        return list;
    }

    static string GetFileDiff(string relativePath, int maxLines)
    {
        try
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath) ?? Application.dataPath;
            string output = RunCommand("git", $"diff --no-color -- {relativePath}", projectRoot);
            if (string.IsNullOrEmpty(output)) return "";
            var lines = output.Split(new[] { '\r', '\n' });
            var added = new List<string>();
            foreach (var l in lines)
            {
                if (l.StartsWith("+") && !l.StartsWith("+++")) added.Add(l);
                if (added.Count >= maxLines) break;
            }
            return string.Join("\n", added.Take(maxLines));
        }
        catch { return ""; }
    }

    static string RunCommand(string cmd, string args, string workingDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd,
                Arguments = args,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (var p = Process.Start(psi))
            {
                if (p == null) return "";
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);
                return stdout?.Trim() ?? "";
            }
        }
        catch { return ""; }
    }
}
#endif
