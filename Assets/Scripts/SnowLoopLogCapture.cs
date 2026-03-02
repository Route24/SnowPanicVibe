using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Captures Unity console logs during play and writes them to a file.
/// This allows report generation without screenshots.
/// </summary>
public class SnowLoopLogCapture : MonoBehaviour
{
    static SnowLoopLogCapture _instance;
    static string _latestLogPath;
    static bool _reportingWriteFailure;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("SnowLoopLogCapture");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SnowLoopLogCapture>();
    }

    void OnEnable()
    {
        string logsDir = Path.Combine(Application.dataPath, "Logs");
        Directory.CreateDirectory(logsDir);
        _latestLogPath = Path.Combine(logsDir, "snowloop_latest.txt");

        string header =
            $"# SnowLoop Play Log{Environment.NewLine}" +
            $"started_at={DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}" +
            $"unity_version={Application.unityVersion}{Environment.NewLine}" +
            $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}{Environment.NewLine}" +
            $"---{Environment.NewLine}";
        File.WriteAllText(_latestLogPath, header);

        Application.logMessageReceived += OnLogMessageReceived;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= OnLogMessageReceived;
    }

    static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrEmpty(_latestLogPath)) return;
        try
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            string line = $"[{ts}] [{type}] {condition}{Environment.NewLine}";
            if (type == LogType.Exception)
            {
                string origin = ExtractOriginClassMethod(stackTrace);
                string exceptionBlock =
                    line +
                    $"[{ts}] [ExceptionOrigin] classMethod={origin}{Environment.NewLine}" +
                    $"{stackTrace}{Environment.NewLine}" +
                    $"[{ts}] [ExceptionEnd]{Environment.NewLine}";
                File.AppendAllText(_latestLogPath, exceptionBlock);
                Debug.LogError($"[SnowLoopException] classMethod={origin}{Environment.NewLine}{condition}{Environment.NewLine}{stackTrace}");
            }
            else
            {
                File.AppendAllText(_latestLogPath, line);
            }
        }
        catch (Exception ex)
        {
            if (_reportingWriteFailure) return;
            _reportingWriteFailure = true;
            Application.logMessageReceived -= OnLogMessageReceived;
            Debug.LogError($"[SnowLoopLogCaptureError] failed to write log file: {ex.Message}");
            Application.logMessageReceived += OnLogMessageReceived;
            _reportingWriteFailure = false;
        }
    }

    static string ExtractOriginClassMethod(string stackTrace)
    {
        if (string.IsNullOrWhiteSpace(stackTrace)) return "Unknown";
        var lines = stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            string l = lines[i].Trim();
            if (string.IsNullOrEmpty(l)) continue;
            int atIdx = l.IndexOf(" at ");
            if (atIdx >= 0)
            {
                string tail = l.Substring(atIdx + 4);
                int paren = tail.IndexOf('(');
                return paren > 0 ? tail.Substring(0, paren).Trim() : tail.Trim();
            }
            int methodParen = l.IndexOf('(');
            if (methodParen > 0)
                return l.Substring(0, methodParen).Trim();
            return l;
        }
        return "Unknown";
    }
}
