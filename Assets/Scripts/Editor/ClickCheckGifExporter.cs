#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Diagnostics;

/// <summary>
/// SnowVisibilityLab 専用: Play → Stop 後に
/// Recordings/snow_test_latest.mp4 から
/// Recordings/latest_click_check.gif を生成する。
/// </summary>
[InitializeOnLoad]
static class ClickCheckGifExporter
{
    static readonly string Mp4Path = Path.GetFullPath("Recordings/snow_test_latest.mp4");
    static readonly string GifPath = Path.GetFullPath("Recordings/latest_click_check.gif");
    static bool _sawPlay;

    static ClickCheckGifExporter()
    {
        EditorApplication.playModeStateChanged += OnPlayModeState;
    }

    static void OnPlayModeState(PlayModeStateChange state)
    {
        // TASK3G: play_view_match=NO のため GIF 書き出しを停止
        // 理由: snow_test_latest.mp4 は現 Play 画面の録画ではなく旧セッションの mp4
        return;

        if (state == PlayModeStateChange.EnteredPlayMode) { _sawPlay = true; return; }
        if (state != PlayModeStateChange.EnteredEditMode || !_sawPlay) return;
        _sawPlay = false;

        EditorApplication.delayCall += ExportGif;
    }

    static void ExportGif()
    {
        if (!File.Exists(Mp4Path))
        {
            UnityEngine.Debug.Log($"[ClickCheckGif] mp4 not found: {Mp4Path}");
            return;
        }

        // ffmpeg で mp4 → gif（最初の8秒、320px幅、10fps）
        string ffmpeg = "/opt/homebrew/bin/ffmpeg";
        if (!File.Exists(ffmpeg)) ffmpeg = "ffmpeg";

        string args = $"-y -i \"{Mp4Path}\" -t 8 -vf \"fps=10,scale=320:-1:flags=lanczos\" \"{GifPath}\"";
        var psi = new ProcessStartInfo(ffmpeg, args)
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var proc = Process.Start(psi);
            proc.WaitForExit(15000);
            bool exists = File.Exists(GifPath);
            UnityEngine.Debug.Log($"[ClickCheckGif] gif_export={(exists ? "YES" : "NO")} gif_path={GifPath} gif_exists={exists}");
        }
        catch (System.Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[ClickCheckGif] ffmpeg failed: {ex.Message}");
        }
    }
}
#endif
