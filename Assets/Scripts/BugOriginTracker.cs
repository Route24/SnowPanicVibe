using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 不具合原因の自動特定。イベントトレース・状態スナップショット・原典解析。
/// 「犯人をログで特定する」方式。
/// </summary>
public static class BugOriginTracker
{
    public const string EventSnowHit = "SnowHit";
    public const string EventSnowDetach = "SnowDetach";
    public const string EventSnowAvalanche = "SnowAvalanche";
    public const string EventScoreUpdate = "ScoreUpdate";
    public const string EventObjectSpawn = "ObjectSpawn";
    public const string EventObjectDestroy = "ObjectDestroy";
    public const string EventSceneLoad = "SceneLoad";

    const int EventBufferSize = 64;
    static readonly List<TracedEvent> _events = new List<TracedEvent>();
    static readonly object _lock = new object();
    static int _snowPiecesDestroyedCount;
    static Vector3 _lastCameraPos;
    static bool _cameraPosKnown;
    static bool _snowPiecesZeroTriggered;

    struct TracedEvent
    {
        public float Time;
        public string EventType;
        public string ObjectName;
        public string Script;
        public string Position;
    }

    /// <summary>イベントを記録。</summary>
    public static void RecordEvent(string eventType, string objectName = "", string script = "", Vector3? position = null)
    {
        lock (_lock)
        {
            var pos = position ?? Vector3.zero;
            _events.Add(new TracedEvent
            {
                Time = Time.time,
                EventType = eventType,
                ObjectName = objectName ?? "",
                Script = script ?? "",
                Position = $"({pos.x:F2},{pos.y:F2},{pos.z:F2})"
            });
            while (_events.Count > EventBufferSize) _events.RemoveAt(0);
            if (eventType == EventObjectDestroy && (objectName ?? "").IndexOf("Snow", StringComparison.OrdinalIgnoreCase) >= 0)
                _snowPiecesDestroyedCount++;
        }
    }

    /// <summary>スコア更新を記録。</summary>
    public static void RecordScoreUpdate(int scoreBefore, int scoreAfter)
    {
        RecordEvent(EventScoreUpdate, $"score_{scoreAfter}", "SnowPhysicsScoreManager.cs");
    }

    /// <summary>例外発生時に呼ぶ。状態キャプチャと原典解析を実行。</summary>
    public static void OnException(string condition, string stackTrace)
    {
        if (condition.IndexOf("NullReference", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("NullRef", StringComparison.OrdinalIgnoreCase) >= 0 ||
            condition.IndexOf("MissingReference", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            TriggerBugAnalysis("null_reference_exception", condition);
        }
    }

    /// <summary>active_pieces=0 検出時に呼ぶ。</summary>
    public static void OnSnowPiecesZero()
    {
        if (_snowPiecesZeroTriggered) return;
        _snowPiecesZeroTriggered = true;
        TriggerBugAnalysis("SnowPiecesMissing", "snow_piece_count=0");
    }

    /// <summary>シーンロードを記録。</summary>
    public static void RecordSceneLoad(string sceneName)
    {
        RecordEvent(EventSceneLoad, sceneName, "SceneManager");
    }

    static void TriggerBugAnalysis(string detectedError, string detail)
    {
        try
        {
            var snapshot = CaptureStateSnapshot();
            var lastEvts = GetLastEvents(10);
            var originScripts = GetPossibleOriginScripts(lastEvts);

            SnowLoopLogCapture.AppendToAssiReport("=== BUG ORIGIN ANALYSIS ===");
            SnowLoopLogCapture.AppendToAssiReport($"detected_error={detectedError}");
            SnowLoopLogCapture.AppendToAssiReport($"detail={detail}");
            SnowLoopLogCapture.AppendToAssiReport("last_events=");
            foreach (var e in lastEvts) SnowLoopLogCapture.AppendToAssiReport(e.EventType);
            SnowLoopLogCapture.AppendToAssiReport("possible_origin_script=");
            foreach (var s in originScripts) SnowLoopLogCapture.AppendToAssiReport(s);

            SnowLoopLogCapture.AppendToAssiReport("=== STATE SNAPSHOT ===");
            foreach (var line in snapshot) SnowLoopLogCapture.AppendToAssiReport(line);

            UnityEngine.Debug.Log($"[BugOriginTracker] Triggered: {detectedError} last_events={string.Join(",", lastEvts.Select(x => x.EventType))}");
        }
        catch (Exception ex) { UnityEngine.Debug.LogWarning($"[BugOriginTracker] TriggerBugAnalysis failed: {ex.Message}"); }
    }

    static List<TracedEvent> GetLastEvents(int n)
    {
        lock (_lock)
        {
            int start = Mathf.Max(0, _events.Count - n);
            return _events.Skip(start).ToList();
        }
    }

    static HashSet<string> GetPossibleOriginScripts(List<TracedEvent> evts)
    {
        var scripts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in evts)
            if (!string.IsNullOrEmpty(e.Script)) scripts.Add(e.Script);
        return scripts;
    }

    static List<string> CaptureStateSnapshot()
    {
        var lines = new List<string>();
        try
        {
            string scene = SceneManager.GetActiveScene().name;
            int activePieces = DebugDiagnostics.GetActiveSnowPiecesCount();
            int rootChildren = GetSnowRootChildren();
            int score = SnowPhysicsScoreManager.Instance != null ? SnowPhysicsScoreManager.Instance.Score : 0;
            var cam = Camera.main;
            string camPos = cam != null ? $"({cam.transform.position.x:F2},{cam.transform.position.y:F2},{cam.transform.position.z:F2})" : "(0,0,0)";

            lines.Add($"scene={scene}");
            lines.Add($"active_snow_pieces={activePieces}");
            lines.Add($"snow_root_children={rootChildren}");
            lines.Add($"score={score}");
            lines.Add($"cameraPos={camPos}");
            lines.Add($"snow_pieces_destroyed={_snowPiecesDestroyedCount}");
        }
        catch { }
        return lines;
    }

    static int GetSnowRootChildren()
    {
        var root = GameObject.Find("SnowPackPiecesRoot");
        return root != null ? root.transform.childCount : -1;
    }

    /// <summary>EVENT TRACE を ASSI Report に出力。</summary>
    public static void EmitEventTraceToReport()
    {
        try
        {
            lock (_lock)
            {
                SnowLoopLogCapture.AppendToAssiReport("=== EVENT TRACE ===");
                int start = Mathf.Max(0, _events.Count - 30);
                foreach (var e in _events.Skip(start))
                {
                    SnowLoopLogCapture.AppendToAssiReport($"time={e.Time:F2}");
                    SnowLoopLogCapture.AppendToAssiReport($"event={e.EventType}");
                    if (!string.IsNullOrEmpty(e.ObjectName)) SnowLoopLogCapture.AppendToAssiReport($"object={e.ObjectName}");
                    if (!string.IsNullOrEmpty(e.Script)) SnowLoopLogCapture.AppendToAssiReport($"script={e.Script}");
                    SnowLoopLogCapture.AppendToAssiReport($"position={e.Position}");
                }
            }
        }
        catch { }
    }

    /// <summary>OBJECT TRACKING を ASSI Report に出力。</summary>
    public static void EmitObjectTrackingToReport()
    {
        try
        {
            int rootCh = GetSnowRootChildren();
            int active = DebugDiagnostics.GetActiveSnowPiecesCount();
            SnowLoopLogCapture.AppendToAssiReport("=== OBJECT TRACKING ===");
            SnowLoopLogCapture.AppendToAssiReport($"snow_root_children={rootCh}");
            SnowLoopLogCapture.AppendToAssiReport($"snow_pieces_active={active}");
            SnowLoopLogCapture.AppendToAssiReport($"snow_pieces_destroyed={_snowPiecesDestroyedCount}");
        }
        catch { }
    }
}
