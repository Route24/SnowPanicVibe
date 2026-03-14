#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

/// <summary>
/// Play 停止時（ExitingPlayMode）に Scene/Console/Inspector をキャプチャし、
/// Recordings/debug/ に保存。ASSI REPORT に出力。
/// </summary>
[InitializeOnLoad]
public static class DebugScreenshotEditor
{
    const string KindGameView = "gameview";
    const string KindScene = "sceneview";
    const string KindConsole = "console";
    const string KindInspector = "inspector";

    static string SaveDir => _saveDir ??= ResolveSaveDir();
    static string _saveDir;

    static DebugScreenshotEditor()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static string ResolveSaveDir()
    {
        string root = Application.dataPath;
        var dir = Path.Combine(Path.GetDirectoryName(root) ?? root, "Recordings", "debug");
        try { Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingPlayMode) return;
        // 同期実行: ExitingPlayMode 時点ではランタイムがまだ生きており AppendToAssiReport が使える
        CaptureEditorWindows();
    }

    static void CaptureEditorWindows()
    {
        try
        {
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            CaptureWindow(KindGameView, () => GetWindowByType("UnityEditor.GameView"), ts);
            CaptureWindow(KindScene, () => SceneView.lastActiveSceneView, ts);
            CaptureWindow(KindConsole, () => GetWindowByType("UnityEditor.ConsoleWindow"), ts);
            CaptureWindow(KindInspector, () => GetWindowByType("UnityEditor.InspectorWindow"), ts);
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[DebugScreenshotEditor] CaptureEditorWindows failed: {ex.Message}\n{ex.StackTrace}");
            AppendToLogFile("=== DEBUG SCREENSHOT [EDITOR] ===");
            AppendToLogFile("error=" + ex.Message);
        }
    }

    static EditorWindow GetWindowByType(string typeName)
    {
        var t = typeof(EditorWindow).Assembly.GetType(typeName);
        if (t == null) return null;
        return EditorWindow.GetWindow(t, false, null, false);
    }

    static void CaptureWindow(string kind, Func<EditorWindow> getWindow, string ts)
    {
        try
        {
            var win = getWindow();
            if (win == null)
            {
                EmitReport(kind, "", null, false, 0, "window_not_found");
                return;
            }

            var pos = win.position;
            int w = Mathf.Max(1, (int)pos.width);
            int h = Mathf.Max(1, (int)pos.height);

            Color[] pixels = null;
            try
            {
                pixels = InternalEditorUtility.ReadScreenPixel(pos.position, w, h);
            }
            catch (Exception ex)
            {
                EmitReport(kind, "", null, false, 0, $"ReadScreenPixel: {ex.Message}");
                return;
            }

            if (pixels == null || pixels.Length == 0)
            {
                EmitReport(kind, "", null, false, 0, "ReadScreenPixel_empty");
                return;
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);

            string pathLatest = Path.Combine(SaveDir, $"{kind}_latest.png");
            string pathSession = Path.Combine(SaveDir, $"{kind}_vp_play_{ts}.png");

            File.WriteAllBytes(pathLatest, png);
            File.WriteAllBytes(pathSession, png);

            EmitReport(kind, pathLatest, pathSession, false, png.Length, null);
        }
        catch (Exception ex)
        {
            EmitReport(kind, "", null, false, 0, ex.Message);
        }
    }

    static void EmitReport(string kind, string localPath, string sessionPath, bool driveAttempted, long sizeBytes, string err = null)
    {
        bool exists = !string.IsNullOrEmpty(localPath) && File.Exists(localPath);
        if (exists && sizeBytes == 0) try { sizeBytes = new FileInfo(localPath).Length; } catch { }
        string driveLink = driveAttempted ? "(not_implemented)" : "";
        bool driveOk = false;

        // ExitingPlayMode 時はランタイムが既に破棄されている可能性があるため直接ログファイルに追記
        AppendToLogFile($"=== DEBUG SCREENSHOT [{kind.ToUpper()}] ===");
        AppendToLogFile($"{kind}_local_path={localPath}");
        AppendToLogFile($"{kind}_exists={exists}");
        AppendToLogFile($"{kind}_size_bytes={sizeBytes}");
        AppendToLogFile($"{kind}_drive_link={driveLink}");
        AppendToLogFile($"{kind}_drive_upload_success={driveOk}");
        AppendToLogFile($"error={(err ?? "none")}");
        if (!string.IsNullOrEmpty(sessionPath))
            AppendToLogFile($"{kind}_session_path={sessionPath}");
    }

    static void AppendToLogFile(string line)
    {
        try
        {
            var path = Path.Combine(Application.dataPath, "Logs", "snowloop_latest.txt");
            if (!File.Exists(path)) return;
            string ts = DateTime.Now.ToString("HH:mm:ss");
            File.AppendAllText(path, $"[{ts}] [ASSI] {line}\n");
        }
        catch { }
    }
}
#endif
