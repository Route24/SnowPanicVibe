using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Captures Unity console logs during play and writes them to a file.
/// Keeps last 400 lines in buffer for ASSI Report (FULL CONSOLE DUMP).
/// </summary>
public class SnowLoopLogCapture : MonoBehaviour
{
    static SnowLoopLogCapture _instance;
    static string _latestLogPath;
    static string _bufferPath;
    static bool _reportingWriteFailure;
    static int _runIdCounter;
    const int MaxBufferLines = 800;
    static readonly List<string> _consoleBuffer = new List<string>();

    static bool _assiBootEmitted;
    static bool _assiDiagnostic2sEmitted;

    /// <summary>ログCapture未配置でも必ず動く。BeforeSceneLoadで最優先生成。VideoPipeline SelfTest中は雪系を無効化。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (_instance != null) return;
        var go = new GameObject("SnowLoopLogCapture");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<SnowLoopLogCapture>();
        if (VideoPipelineSelfTestMode.IsActive)
        {
            _assiBootEmitted = true;
            _assiDiagnostic2sEmitted = true;
            return;
        }
        var systems = new GameObject("Systems");
        DontDestroyOnLoad(systems);
        systems.AddComponent<UIBootstrap>();
        go.AddComponent<AssiDebugUI>();
        go.AddComponent<DebugSnowVisibility>();
        go.AddComponent<GridVisualWatchdog>();
        go.AddComponent<CabinRoofForceHide>();
        go.AddComponent<RendererWatch>();
        _assiBootEmitted = false;
        _assiDiagnostic2sEmitted = false;
    }

    /// <summary>A) Play開始で必ず1回 [ASSI_BOOT] を出す。Start で確実に。SelfTest中はスキップ。</summary>
    void Start()
    {
        if (VideoPipelineSelfTestMode.IsActive) return;
        if (!_assiBootEmitted)
        {
            _assiBootEmitted = true;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            bool en = enabled;
            bool active = gameObject.activeInHierarchy;
            Debug.Log($"[ASSI_BOOT] scene={scene} enabled={en} active={active}");
        }
        SnowPhysicsScoreManager.EnsureBootstrapIfNeeded();
        UIBootstrap.EnsureUIRootAndScoreText();
        SnowScoreDisplayUI.EnsureBootstrap();
        DisableDebugGridVisuals();
        Invoke(nameof(InvokeEmitRenderVisibilitySnapshot), 0.02f);
    }

    static void DisableDebugGridVisuals()
    {
        var snowTest = GameObject.Find("SnowTest");
        if (snowTest != null && snowTest.activeSelf)
        {
            snowTest.SetActive(false);
            Debug.Log("[SnowLoopLogCapture] SnowTest disabled (grid/lattice prevention)");
        }
#pragma warning disable 0618
        foreach (var lr in UnityEngine.Object.FindObjectsOfType<LineRenderer>())
#pragma warning restore 0618
        {
            if (lr == null) continue;
            var t = lr.transform;
            if (t != null && (t.name.Contains("SnowTest") || t.name.Contains("Debug")))
            {
                lr.enabled = false;
                Debug.Log($"[SnowLoopLogCapture] LineRenderer disabled on {t.name}");
            }
        }
    }

    void InvokeEmitRenderVisibilitySnapshot()
    {
        EmitRenderVisibilitySnapshot();
    }

    /// <summary>茶色屋根一瞬表示の切り分け用。Play開始直後とRunAlign後で呼ぶ。</summary>
    public static void EmitRenderVisibilitySnapshot()
    {
        float t = Time.time;
        AppendToAssiReport("=== RENDER VISIBILITY SNAPSHOT ===");
        AppendToAssiReport($"time={t:F3}");

        var cabin = GameObject.Find("cabin-roof");
        if (cabin != null)
        {
            var mr = cabin.GetComponent<MeshRenderer>();
            string layerName = cabin.layer >= 0 ? LayerMask.LayerToName(cabin.layer) : "?";
            string matName = (mr != null && mr.sharedMaterial != null) ? mr.sharedMaterial.name : "null";
            AppendToAssiReport($"cabin-roof: enabled={(mr != null && mr.enabled)} active={cabin.activeInHierarchy} layer={layerName} mat={matName}");
        }
        else
            AppendToAssiReport("cabin-roof: (not found)");

        var roofProxy = GameObject.Find("RoofProxy");
        AppendToAssiReport(roofProxy == null ? "RoofProxy: (not found)" : "RoofProxy: (exists - DISABLED_MODE: should not exist)");

        foreach (var n in new[] { "RoofRoot", "RoofYaw", "RoofSlideCollider" })
        {
            var g = GameObject.Find(n);
            AppendToAssiReport($"{n}: active={(g != null && g.activeInHierarchy)}");
        }

        var candidates = new List<KeyValuePair<Renderer, float>>();
#pragma warning disable 0618
        var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
#pragma warning restore 0618
        foreach (var r in renderers)
        {
            if (r == null || r.sharedMaterial == null) continue;
            var c = MaterialColorHelper.GetColorSafe(r.sharedMaterial, Color.white);
            float brownScore = 0f;
            if (c.r > 0.3f && c.g > 0.2f && c.b < c.r && c.b < c.g) brownScore = c.r + c.g * 0.5f;
            if (r.sharedMaterial.name.IndexOf("roof", System.StringComparison.OrdinalIgnoreCase) >= 0) brownScore += 2f;
            if (r.sharedMaterial.name.IndexOf("brown", System.StringComparison.OrdinalIgnoreCase) >= 0) brownScore += 2f;
            if (r.sharedMaterial.name.IndexOf("wood", System.StringComparison.OrdinalIgnoreCase) >= 0) brownScore += 1.5f;
            if (brownScore > 0f) candidates.Add(new KeyValuePair<Renderer, float>(r, brownScore));
        }
        candidates.Sort((a, b) => b.Value.CompareTo(a.Value));
        AppendToAssiReport("TopBrownCandidates:");
        for (int i = 0; i < Mathf.Min(10, candidates.Count); i++)
        {
            var kv = candidates[i];
            var r = kv.Key;
            if (r == null) continue;
            float score = kv.Value;
            try
            {
                string path = r.transform != null ? GetTransformPath(r.transform) : "?";
                string matName = r.sharedMaterial != null ? r.sharedMaterial.name : "null";
                AppendToAssiReport($"  [{i}] name={r.name} path={path} enabled={r.enabled} active={r.gameObject.activeInHierarchy} mat={matName} score={score:F2}");
            }
            catch (System.Exception) { /* skip destroyed */ }
        }
        if (candidates.Count == 0) AppendToAssiReport("  (none)");
    }

    static string GetTransformPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        // 雪崩発火は AssiDebugUI [BTN] TriggerAvalanche を使用（F8はmacOSでMusic起動のため非推奨）
        // Play開始から2秒後に必ず [RUN_SNAPSHOT_FORCE][STACKTRACE_SELFTEST][LAST20_FORCE] を出す
        if (Time.time >= 2f && !_assiDiagnostic2sEmitted)
        {
            _assiDiagnostic2sEmitted = true;
            var spawner = UnityEngine.Object.FindFirstObjectByType<SnowPackSpawner>();
            if (spawner != null && spawner.isActiveAndEnabled)
                spawner.RunAssiDiagnostic2s();
            else
                EmitAssiDiagnosticFallback();
        }
    }

    static void EmitAssiDiagnosticFallback()
    {
        Debug.Log("[RUN_SNAPSHOT_FORCE] childCount=-1 transformCount=-1 pieceByNameCount=-1 rendererCount=-1 activePieces=-1 rootChildren=-1 pooled=-1");
        Debug.Log("[SNAPSHOT_INVALID] reason=NotInitialized SnowPackSpawnerが未配置または無効");
        Debug.Log("[SNAPSHOT_ROOT] findByName=SnowPackVisual findByTag=none found=No path=null");
        Debug.Log("[LAST20_FORCE] count=0");
        Debug.Log("[LAST20_EMPTY]");
        Debug.Log("[STACKTRACE_SELFTEST] ok=No fileLine=unknown method=unknown reason=SnowPackSpawnerNotFound");
        Debug.Log("[PiecePoseSample] N/A (SnowPackSpawner not found)");
        Debug.Log("[RotationOverrideFound] None");
        Debug.Log("[AutoAvalancheState] default=OFF current=" + (AssiDebugUI.AutoAvalancheOff ? "OFF" : "ON"));
        Debug.Log("[TapMarkerState] atStart visible=No lastTapValid=No (fallback)");
        Debug.Log("[SceneCodePath] scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " SnowPackSpawner=No roofCollider=N/A _piecesRoot=N/A RoofSnowSystem=N/A spawnFunc=N/A debugForcePieceRendererDirect=N/A");
    }

    void OnEnable()
    {
        _assiBootEmitted = false;
        _assiDiagnostic2sEmitted = false;
        Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.Full);
        Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.Full);
        Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.ScriptOnly);
        Debug.Log("[ASSISetup] StackTraceLogging: Error=Full Exception=Full Log=ScriptOnly (ConsoleUI要確認)");

        string logsDir = Path.Combine(Application.dataPath, "Logs");
        Directory.CreateDirectory(logsDir);
        _latestLogPath = Path.Combine(logsDir, "snowloop_latest.txt");
        _bufferPath = Path.Combine(logsDir, "console_buffer.txt");

        _runIdCounter++;
        _consoleBuffer.Clear();
        string header =
            $"# SnowLoop Play Log{Environment.NewLine}" +
            $"runId={_runIdCounter}{Environment.NewLine}" +
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
        try
        {
            if (!string.IsNullOrEmpty(_bufferPath) && _consoleBuffer.Count > 0)
            {
                var dir = Path.GetDirectoryName(_bufferPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(_bufferPath, _consoleBuffer);
            }
        }
        catch (Exception ex) { Debug.LogWarning($"[SnowLoopLogCapture] buffer write failed: {ex.Message}"); }
    }

    static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrEmpty(_latestLogPath)) return;
        try
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            string line = $"[{ts}] [{type}] {condition}";
            bool hasStackTrace = type == LogType.Exception || type == LogType.Error;
            if (hasStackTrace && !string.IsNullOrWhiteSpace(stackTrace))
            {
                string origin = ExtractOriginClassMethod(stackTrace);
                string exceptionBlock =
                    line + Environment.NewLine +
                    $"[{ts}] [StackTraceOrigin] classMethod={origin}" + Environment.NewLine +
                    stackTrace + Environment.NewLine +
                    $"[{ts}] [StackTraceEnd]";
                File.AppendAllText(_latestLogPath, exceptionBlock + Environment.NewLine);
                AddToBuffer(line);
                AddToBuffer($"[{ts}] [StackTraceOrigin] classMethod={origin}");
                foreach (var st in stackTrace.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    AddToBuffer(st.Trim());
                AddToBuffer($"[{ts}] [StackTraceEnd]");
            }
            else
            {
                File.AppendAllText(_latestLogPath, line + Environment.NewLine);
                AddToBuffer(line);
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

    static void AddToBuffer(string line)
    {
        _consoleBuffer.Add(line);
        while (_consoleBuffer.Count > MaxBufferLines)
            _consoleBuffer.RemoveAt(0);
    }

    /// <summary>Editor 用: 直近ログバッファのパス</summary>
    public static string BufferPath => _bufferPath;

    /// <summary>ASSI_BOOT 等で使用。現在の runId</summary>
    public static int RunId => _runIdCounter;

    /// <summary>Consoleに出さずASSIレポート用に追記。ログ量削減用。</summary>
    public static void AppendToAssiReport(string line)
    {
        if (VideoPipelineSelfTestMode.IsActive) return;
        if (string.IsNullOrEmpty(_latestLogPath)) return;
        try
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            string full = $"[{ts}] [ASSI] {line}";
            File.AppendAllText(_latestLogPath, full + Environment.NewLine);
            AddToBuffer(full);
        }
        catch { }
    }
}
