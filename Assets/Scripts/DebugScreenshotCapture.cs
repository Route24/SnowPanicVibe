using System;
using System.IO;
using UnityEngine;

/// <summary>
/// デバッグ用スクショ自動保存モジュール。
/// Game View / Scene View / Console / Inspector を固定保存先に保存し、
/// ASSI REPORT にパス・Drive共有リンクを出力。
/// </summary>
public class DebugScreenshotCapture : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("保存先ルート。空なら Recordings/debug")]
    public string saveRoot = "";
    [Tooltip("Drive アップロードを試行する")]
    public bool tryDriveUpload = false;
    [Tooltip("セッション別にタイムスタンプ付きで保存")]
    public bool saveSessionCopy = true;
    [Tooltip("Play 開始後何秒でキャプチャ")]
    public float captureDelayOnPlay = 2f;
    [Tooltip("Stop 直前にキャプチャ（OnApplicationQuit）")]
    public bool captureOnQuit = true;

    public static string SaveDir => _saveDir ??= ResolveSaveDir();
    static string _saveDir;
    static bool _capturedThisSession;
    static string _ts;

    static string ResolveSaveDir()
    {
        string root = Application.dataPath;
        var dir = Path.Combine(Path.GetDirectoryName(root) ?? root, "Recordings", "debug");
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DebugScreenshot] CreateDirectory failed: {ex.Message}");
        }
        return dir;
    }

    void Start()
    {
        if (string.IsNullOrEmpty(_ts)) _ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Invoke(nameof(DoCaptureGameView), captureDelayOnPlay);
    }

    void OnApplicationQuit()
    {
        if (captureOnQuit) DoCaptureGameView();
    }

    void DoCaptureGameView()
    {
        CaptureGameView(saveSessionCopy, tryDriveUpload);
    }

    /// <summary>Game View をキャプチャ。ランタイムで呼べる。</summary>
    public static void CaptureGameView(bool saveSession = true, bool driveUpload = false)
    {
        try
        {
            if (string.IsNullOrEmpty(_ts)) _ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(SaveDir, "gameview_latest.png");
            string pathSession = saveSession ? Path.Combine(SaveDir, $"gameview_vp_play_{_ts}.png") : null;

            var cam = Camera.main;
            if (cam != null)
            {
                int w = Screen.width;
                int h = Screen.height;
                var rt = new RenderTexture(w, h, 24);
                var prev = cam.targetTexture;
                cam.targetTexture = rt;
                cam.Render();
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                cam.targetTexture = prev;
                rt.Release();

                byte[] png = tex.EncodeToPNG();
                File.WriteAllBytes(path, png);
                if (!string.IsNullOrEmpty(pathSession)) File.WriteAllBytes(pathSession, png);
                UnityEngine.Object.Destroy(tex);
                UnityEngine.Object.Destroy(rt);

                _capturedThisSession = true;
                EmitReport("gameview", path, pathSession, driveUpload, png.Length, null);
            }
            else
            {
                ScreenCapture.CaptureScreenshot(path);
                if (!string.IsNullOrEmpty(pathSession)) ScreenCapture.CaptureScreenshot(pathSession);
                long size = 0;
                if (File.Exists(path)) try { size = new FileInfo(path).Length; } catch { }
                EmitReport("gameview", path, pathSession, driveUpload, size, null);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[DebugScreenshot] GameView capture failed: {ex.Message}");
            EmitReport("gameview", "", null, false, 0, ex.Message);
        }
    }

    static void EmitReport(string kind, string localPath, string sessionPath, bool driveAttempted, long sizeBytes, string err = null)
    {
        bool exists = !string.IsNullOrEmpty(localPath) && File.Exists(localPath);
        if (exists && sizeBytes == 0) try { sizeBytes = new FileInfo(localPath).Length; } catch { }
        string driveLink = "";
        bool driveOk = false;
        if (driveAttempted)
        {
            driveLink = "(not_implemented)";
            driveOk = false;
        }

        Debug.Log($"[DebugScreenshot] {kind} local_path={localPath} exists={exists} size_bytes={sizeBytes}");
        SnowLoopLogCapture.AppendToAssiReport($"=== DEBUG SCREENSHOT [{kind.ToUpper()}] ===");
        SnowLoopLogCapture.AppendToAssiReport($"{kind}_local_path={localPath}");
        SnowLoopLogCapture.AppendToAssiReport($"{kind}_exists={exists}");
        SnowLoopLogCapture.AppendToAssiReport($"{kind}_size_bytes={sizeBytes}");
        SnowLoopLogCapture.AppendToAssiReport($"{kind}_drive_link={driveLink}");
        SnowLoopLogCapture.AppendToAssiReport($"{kind}_drive_upload_success={driveOk}");
        SnowLoopLogCapture.AppendToAssiReport($"error={(err ?? "none")}");
        if (!string.IsNullOrEmpty(sessionPath))
            SnowLoopLogCapture.AppendToAssiReport($"{kind}_session_path={sessionPath}");
    }
}
