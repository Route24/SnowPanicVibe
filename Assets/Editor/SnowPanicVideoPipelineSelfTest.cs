#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;
using UnityEngine.SceneManagement;
using Process = System.Diagnostics.Process;

/// <summary>
/// 動画パイプライン SelfTest。10秒で必ず停止。ログは必ずASSI REPORTに載り、クリップボードへコピー。
/// </summary>
[InitializeOnLoad]
public static class SnowPanicVideoPipelineSelfTest
{
    const int RecordDurationSeconds = 10;
    const int RecWidth = 1280;
    const int RecHeight = 720;
    const float FrameRate = 30f;
    const string RcloneRemote = "gdrive:SnowPanicVideos";
    const float WatchdogSeconds = 20f;
    const float RecordTimeoutSeconds = 10f;
    const int Mp4WaitMaxSec = 10;
    const double Mp4PollIntervalSec = 0.2;
    const long Mp4MinSizeBytes = 1;
    const float RecorderStartTimeoutSec = 5f;
    const float RecorderFlushDelaySec = 3f;
    const double Mp4SizeStableDelaySec = 0.3;
    const int RcloneTimeoutMs = 6000;
    const int CurlTimeoutMs = 3000;
    const string RclonePath = "/opt/homebrew/bin/rclone";
    const int ConsoleRingBufferSize = 200;

    enum State { Idle, Recording, PostStopPolling, WaitingEdit, PostRecord, Done }
    enum PostPhase { WaitMp4, Rclone, Slack, Complete }
    static State _state = State.Idle;
    static PostPhase _postPhase = PostPhase.WaitMp4;
    static double _timerStart;
    static DateTime _postPhaseStart;
    static int _lastMp4PollCount = -1;
    static string _mp4CandidatePath;
    static long _mp4CandidateSize = -1;
    static DateTime _mp4CandidateFirstSeenUtc;
    static bool _exitRoutineRan;
    static RecorderController _controller;
    static RecorderControllerSettings _controllerSettings;
    static MovieRecorderSettings _movieSettings;
    static string _outputPathBase;
    static string _filename;
    static string _mp4Path;
    static string _lastStep = "none";
    static readonly List<string> _consoleRingBuffer = new List<string>();
    static readonly List<string> _consoleRingBufferFull = new List<string>();
    static Application.LogCallback _logHandler;
    static bool _recorderStartOk;
    static bool _recorderStopRequested;
    static DateTime _recorderStopRequestedAt;
    static DateTime _recordingStartedAt;
    static string _lastRecorderException;
    static string _lastRecorderExceptionMessage;
    static string _lastRecorderExceptionStackTrace;

    static string _sessionId;
    static DateTime _startedAt;
    static DateTime? _endedAt;
    static string _result;
    static string _errorStep;
    static string _localPath;
    static long _localSizeBytes;
    static string _drivePath;
    static long _driveSizeBytes;
    static string _driveShareLink;
    static string _slackMessageTsOrLink;
    static string _slackError;
    static bool _localMp4Exists;
    static string _driveFileStatus;
    static string _slackMessageStatus;
    static string _uploadStatus;
    static bool _outputDirWritable;
    static string _latestMp4Path;
    static string _dailyArchivePath;
    static bool _dailyArchiveCreated;
    static string _previewPath;
    static bool _previewCreated;
    static long _previewGifSize;
    static string _previewGifDriveLink;
    static string _previewType;
    static long _previewSizeBytes;
    static bool _previewFallbackUsed;
    static string _gifPath;
    static string _ffmpegPathUsed;
    static bool _ffmpegAvailable;
    static string _previewStatus;
    static bool _uploadCopySuccess;
    static string _uploadVerifyWarning;
    static bool _flushWaiting;
    static DateTime _flushWaitStart;
    static string _tempMp4Path;
    static string _sessionRunLogPath;
    const float FlushWaitMaxSec = 10f;
    const float PostStopPollMaxSec = 30f;
    const double PostStopPollIntervalSec = 0.25;
    static bool _postStopPollEntered;
    static bool _finalizeEntered;
    static string _finalizeReason = "";
    static bool _manualStopCalledByUser;
    static bool _autoStopBypassed;
    static DateTime _postStopPollStart;
    static int _postStopPollCount;
    static long _lastPostStopPollBytes = -1;
    static bool _backgroundTasksPending;
    static bool _backgroundTasksRequested;
    static string _executionMode = "play";
    static bool _startHookCalled;
    static bool _stopHookCalled;
    static bool _previewStartCalled;
    static bool _previewDoneCalled;
    static string _previewErrorReason = "";
    static bool _uploadStarted;
    static bool _uploadFinished;
    static bool _earlyReportEmitted;
    static long _reportCopyReadyTimeMs;
    static bool _finalResultEmitted;
    static bool _previewStartedAsync;
    static bool _uploadStartedAsync;
    static bool _slackStartedAsync;
    static bool _backgroundWorkComplete;
    static bool _backgroundFallbackToMainThread;
    static DateTime _earlyReportEmittedAt;
    static string _roofSurfaceSize;
    static string _snowCoverSize;
    static bool _snowCoverMatchesRoof;

    /// <summary>SelfTestセッション中ならtrue。レポート生成後にクリップボードコピーする。</summary>
    public static bool IsSelfTestSession;

    /// <summary>SelfTestを開始可能か。Idleのときtrue。</summary>
    public static bool IsIdleForSelfTest() => _state == State.Idle;

    /// <summary>早出しレポート中か（後追い未完了）。</summary>
    public static bool IsEarlyReportPhase => _earlyReportEmitted && _backgroundTasksPending;
    /// <summary>後追い完了済みか。</summary>
    public static bool IsFinalResultAvailable => _earlyReportEmitted && !_backgroundTasksPending;

    /// <summary>屋根全面積雪レポート用。Runtime から Play 中に設定。</summary>
    public static void SetRoofSnowReport(string roofSurfaceSize, string snowCoverSize, bool matchesRoof)
    {
        _roofSurfaceSize = roofSurfaceSize;
        _snowCoverSize = snowCoverSize;
        _snowCoverMatchesRoof = matchesRoof;
    }

    const string PrefAutoRecordOnPlay = "SnowPanic.AutoRecordOnPlay";
    /// <summary>通常Playでも自動録画するか。false=通常Playはゲーム画面のみ。true=Play押下で録画→mp4→レポート（オーバーレイ表示）。</summary>
    public static bool AutoRecordOnPlay
    {
        get { return EditorPrefs.GetBool(PrefAutoRecordOnPlay, false); }
        set { EditorPrefs.SetBool(PrefAutoRecordOnPlay, value); }
    }

    /// <summary>セッション開始からの経過秒数。Editor起動時間ではなく _startedAt 基準。sessionIdごとにリセット。</summary>
    static double GetSessionElapsedSeconds()
    {
        if (_startedAt == default) return 0;
        var sec = (DateTime.Now - _startedAt).TotalSeconds;
        if (sec < 0 || double.IsNaN(sec) || double.IsInfinity(sec)) return 0;
        return sec;
    }

    /// <summary>録画中か。Stopボタン表示用。セッションマーカーがあれば true（ドメイン再読込後も復旧）。</summary>
    public static bool IsRecordingForSelfTest()
    {
        if (_state == State.Recording) return true;
        string sid, tmp;
        return TryReadSessionActive(out sid, out tmp);
    }

    static SnowPanicVideoPipelineSelfTest()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += OnUpdate;
    }

    static string GetProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    static string GetHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home)) home = Environment.GetEnvironmentVariable("HOME") ?? "";
        return home;
    }

    /// <summary>_tempMp4Path を必ず返す。null の場合は outputDir + sessionId から生成。</summary>
    static string EnsureTempMp4Path()
    {
        if (!string.IsNullOrEmpty(_tempMp4Path)) return _tempMp4Path;
        if (string.IsNullOrEmpty(_sessionId)) return "";
        var outDir = GetOutputDir();
        if (string.IsNullOrEmpty(outDir)) return "";
        var path = Path.Combine(outDir, "snow_test_tmp_" + _sessionId + ".mp4");
        _tempMp4Path = Path.GetFullPath(path);
        return _tempMp4Path;
    }

    /// <summary>プロジェクトルート/Recordings を確定で返す。_outputPathBase→セッションMarker→ResolveOutputDir の順でフォールバック。</summary>
    static string GetOutputDir()
    {
        if (!string.IsNullOrEmpty(_outputPathBase))
        {
            var dir = Path.GetDirectoryName(_outputPathBase);
            if (!string.IsNullOrEmpty(dir)) return dir;
        }
        string sid, tmp, od;
        if (TryReadSessionActive(out sid, out tmp, out od) && !string.IsNullOrEmpty(od))
            return od;
        var outDir = ResolveOutputDir();
        if (!string.IsNullOrEmpty(outDir)) return outDir;
        return Path.Combine(Environment.CurrentDirectory ?? ".", "Recordings");
    }

    /// <summary>temp → latest の順で検索。FileExists && size>0 なら返す。無ければ最新 mp4 を探索。</summary>
    static FileInfo FindExpectedOrNewestMp4(long minSizeBytes = 1)
    {
        if (!string.IsNullOrEmpty(_tempMp4Path) && File.Exists(_tempMp4Path))
        {
            try { var fi = new FileInfo(_tempMp4Path); if (fi.Length >= minSizeBytes) return fi; } catch { }
        }
        var dir = GetOutputDir();
        var latestPath = Path.Combine(dir, "snow_test_latest.mp4");
        if (File.Exists(latestPath))
        {
            try { var fi = new FileInfo(latestPath); if (fi.Length >= minSizeBytes) return fi; } catch { }
        }
        return FindNewestMp4InRecordings(minSizeBytes);
    }

    /// <summary>Recordings 内の最新 mp4 を検出。*.mp4 を更新日時でソートし、size>=minSizeBytes の最新を返す。</summary>
    static FileInfo FindNewestMp4InRecordings(long minSizeBytes = 1)
    {
        var dir = GetOutputDir();
        if (!Directory.Exists(dir)) return null;
        string[] files;
        try { files = Directory.GetFiles(dir, "*.mp4"); }
        catch { return null; }
        if (files == null || files.Length == 0) return null;
        FileInfo newest = null;
        foreach (var p in files)
        {
            try
            {
                var fi = new FileInfo(p);
                if (fi.Length < minSizeBytes) continue;
                if (newest == null || fi.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                    newest = fi;
            }
            catch { }
        }
        return newest;
    }

    public static string GetAssiLogPath() => Path.Combine(GetOutputDir(), "video_pipeline_assi_log.txt");
    public static string GetEarlySnapshotPath() => Path.Combine(GetOutputDir(), "video_pipeline_early_snapshot.txt");
    public static string GetConsoleFilteredLogPath() => Path.Combine(GetOutputDir(), "video_pipeline_console_filtered.txt");
    public static string GetSessionDataPath() => Path.Combine(GetOutputDir(), "video_pipeline_session.txt");
    public static string GetLastRunPath() => Path.Combine(GetOutputDir(), "video_pipeline_last_run.txt");
    public static string GetConsoleVpOnlyPath() => Path.Combine(GetOutputDir(), "video_pipeline_console_vp_only.txt");
    public static string GetConsoleLastNPath() => Path.Combine(GetOutputDir(), "video_pipeline_console_last_n.txt");

    static void AssiLog(string msg)
    {
        var line = "[VideoPipeline] " + msg;
        try
        {
            var path = GetAssiLogPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(path, line + Environment.NewLine);
        }
        catch { }
        try
        {
            if (!string.IsNullOrEmpty(_sessionRunLogPath))
                File.AppendAllText(_sessionRunLogPath, line + Environment.NewLine);
        }
        catch { }
        UnityEngine.Debug.Log(line);
    }

    static string GetSnowPanicVideosDir()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SnowPanicVideos");
        try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
        return dir;
    }

    public static string GetSessionRunLogPath(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        return Path.Combine(GetSnowPanicVideosDir(), "video_pipeline_run_" + sessionId + ".txt");
    }

    public static string GetSessionRunLast50Lines(string sessionId)
    {
        var path = GetSessionRunLogPath(sessionId);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var lines = File.ReadAllLines(path);
            var take = Math.Min(50, lines.Length);
            var start = Math.Max(0, lines.Length - take);
            return string.Join(Environment.NewLine, lines.Skip(start).Take(take));
        }
        catch { return null; }
    }

    static void AssiError(string step, string reason)
    {
        AssiLog("VIDEO PIPELINE ERROR step=" + step + " reason=" + reason);
    }

    static readonly string[] VpKeywords = { "VIDEO", "PIPELINE", "Recorder", "MP4", "rclone", "gdrive", "Slack" };
    static bool MatchesVpFilter(string condition)
    {
        if (string.IsNullOrEmpty(condition)) return false;
        var upper = condition.ToUpperInvariant();
        foreach (var kw in VpKeywords)
            if (upper.Contains(kw.ToUpperInvariant())) return true;
        return false;
    }

    static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (string.IsNullOrEmpty(condition)) return;
        var line = $"[{DateTime.Now:HH:mm:ss}] [{type}] {condition}";
        lock (_consoleRingBufferFull)
        {
            _consoleRingBufferFull.Add(line);
            while (_consoleRingBufferFull.Count > ConsoleRingBufferSize)
                _consoleRingBufferFull.RemoveAt(0);
        }
        if (MatchesVpFilter(condition))
        {
            lock (_consoleRingBuffer)
            {
                _consoleRingBuffer.Add(line);
                while (_consoleRingBuffer.Count > ConsoleRingBufferSize)
                    _consoleRingBuffer.RemoveAt(0);
            }
        }
    }

    static void SubscribeLogHandler()
    {
        if (_logHandler != null) return;
        _logHandler = OnLogMessageReceived;
        Application.logMessageReceived += _logHandler;
        Application.logMessageReceivedThreaded += _logHandler;
    }

    static void UnsubscribeLogHandler()
    {
        if (_logHandler == null) return;
        Application.logMessageReceived -= _logHandler;
        Application.logMessageReceivedThreaded -= _logHandler;
        _logHandler = null;
    }

    static void WriteConsoleRingBuffer()
    {
        try
        {
            var dir = Path.GetDirectoryName(GetConsoleFilteredLogPath());
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string[] vpCopy, fullCopy;
            lock (_consoleRingBuffer) { vpCopy = _consoleRingBuffer.ToArray(); }
            lock (_consoleRingBufferFull) { fullCopy = _consoleRingBufferFull.ToArray(); }
            File.WriteAllLines(GetConsoleFilteredLogPath(), vpCopy);
            File.WriteAllLines(GetConsoleVpOnlyPath(), vpCopy);
            File.WriteAllLines(GetConsoleLastNPath(), fullCopy);
        }
        catch { }
    }

    static void WriteLastRunTimestamp()
    {
        try
        {
            var path = GetLastRunPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, _startedAt.ToString("o"));
        }
        catch { }
    }

    static void WriteSessionDataAtStart()
    {
        try
        {
            var path = GetSessionDataPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.AppendLine("sessionId=" + (_sessionId ?? ""));
            sb.AppendLine("result=IN_PROGRESS");
            sb.AppendLine("errorStep=none");
            sb.AppendLine("outputDir=" + (Path.GetDirectoryName(_outputPathBase) ?? ""));
            sb.AppendLine("outputDirExists=" + (!string.IsNullOrEmpty(_outputPathBase) && Directory.Exists(Path.GetDirectoryName(_outputPathBase))));
            sb.AppendLine("outputFileExpected=" + (_outputPathBase ?? ""));
            sb.AppendLine("local_mp4_path=" + (_mp4Path ?? ""));
            sb.AppendLine("local_mp4_exists=false");
            sb.AppendLine("local_mp4_size_bytes=0");
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex) { AssiLog("WriteSessionDataAtStart failed ex=" + ex.ToString().Replace("\r", " ").Replace("\n", " | ")); }
    }

    static void LoadRoofSnowReportIfExists()
    {
        try
        {
            var dir = GetOutputDir();
            if (string.IsNullOrEmpty(dir)) return;
            var reportPath = Path.Combine(dir, "roof_snow_report.txt");
            if (!File.Exists(reportPath)) return;
            var lines = File.ReadAllLines(reportPath);
            foreach (var line in lines)
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();
                if (key.Equals("roof_surface_size", System.StringComparison.OrdinalIgnoreCase)) _roofSurfaceSize = val;
                else if (key.Equals("snow_cover_size", System.StringComparison.OrdinalIgnoreCase)) _snowCoverSize = val;
                else if (key.Equals("snow_cover_matches_roof", System.StringComparison.OrdinalIgnoreCase))
                {
                    bool b;
                    if (bool.TryParse(val, out b)) _snowCoverMatchesRoof = b;
                }
            }
        }
        catch { }
    }

    static void WriteSessionData()
    {
        try
        {
            LoadRoofSnowReportIfExists();
            var outDir = GetOutputDir();
            if (string.IsNullOrEmpty(outDir)) outDir = Path.Combine(Environment.CurrentDirectory ?? ".", "Recordings");
            var reportGeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var currSessionId = "report_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var mp4PathCurr = !string.IsNullOrEmpty(currSessionId)
                ? Path.GetFullPath(Path.Combine(outDir, "snow_test_tmp_" + currSessionId + ".mp4"))
                : "";
            var gifPathCurr = !string.IsNullOrEmpty(currSessionId)
                ? Path.GetFullPath(Path.Combine(outDir, "snow_test_tmp_" + currSessionId + ".gif"))
                : "";
            var mp4ExistsForSession = !string.IsNullOrEmpty(mp4PathCurr) && File.Exists(mp4PathCurr);
            var gifExistsForSession = !string.IsNullOrEmpty(gifPathCurr) && File.Exists(gifPathCurr);
            var latestMp4Path = Path.Combine(outDir, "snow_test_latest.mp4");
            var latestMp4Exists = File.Exists(latestMp4Path);
            var latestMp4Modified = latestMp4Exists ? (DateTime?)null : null;
            if (latestMp4Exists) try { latestMp4Modified = new FileInfo(latestMp4Path).LastWriteTimeUtc; } catch { }
            var runStartedUtc = _startedAt != default ? _startedAt.ToUniversalTime() : DateTime.MinValue;
            var latestMp4UpdatedThisRun = (_recorderStartOk || _startHookCalled) && latestMp4Modified.HasValue && runStartedUtc != DateTime.MinValue && latestMp4Modified.Value >= runStartedUtc.AddSeconds(-5);
            var latestGifPath = Path.Combine(outDir, "snow_test_latest.gif");
            if (!File.Exists(latestGifPath)) latestGifPath = Path.Combine(outDir, "snow_test_tmp_" + currSessionId + ".gif");
            var gifExists = File.Exists(latestGifPath) || File.Exists(gifPathCurr);
            var gifPathToCheck = File.Exists(gifPathCurr) ? gifPathCurr : latestGifPath;
            var gifModified = (DateTime?)null;
            if (File.Exists(gifPathToCheck)) try { gifModified = new FileInfo(gifPathToCheck).LastWriteTimeUtc; } catch { }
            var gifUpdatedThisRun = (_recorderStartOk || _startHookCalled) && gifModified.HasValue && runStartedUtc != DateTime.MinValue && gifModified.Value >= runStartedUtc.AddSeconds(-5);
            var sessDataPath = GetSessionDataPath();
            var cutoffUtc = runStartedUtc != DateTime.MinValue ? runStartedUtc.AddSeconds(-10) : DateTime.UtcNow.AddMinutes(-5);
            var txtLogsUpdated = !string.IsNullOrEmpty(sessDataPath) && File.Exists(sessDataPath) && new FileInfo(sessDataPath).LastWriteTimeUtc >= cutoffUtc;
            var debugDir = Path.Combine(outDir, "debug");
            var debugPngPath = Path.Combine(debugDir, "gameview_latest.png");
            var debugPngUpdated = File.Exists(debugPngPath) && new FileInfo(debugPngPath).LastWriteTimeUtc >= cutoffUtc;
            var path = GetSessionDataPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            var sessTempPath = EnsureTempMp4Path();
            var sessTempExists = !string.IsNullOrEmpty(sessTempPath) && File.Exists(sessTempPath);
            long sessTempBytes = 0;
            DateTime? sessTempModified = null;
            if (sessTempExists) try { var fi = new FileInfo(sessTempPath); sessTempBytes = fi.Length; sessTempModified = fi.LastWriteTimeUtc; } catch { }
            var sessPlayKeptAliveSec = _endedAt.HasValue ? (_endedAt.Value - _startedAt).TotalSeconds : 0;
            sb.AppendLine("=== VIDEO PIPELINE STATUS ===");
            sb.AppendLine("execution_mode=" + (_executionMode ?? "play"));
            sb.AppendLine("report_emitted_early=" + _earlyReportEmitted.ToString().ToLower());
            sb.AppendLine("report_copy_ready_time_ms=" + _reportCopyReadyTimeMs);
            sb.AppendLine("final_result_emitted=" + _finalResultEmitted.ToString().ToLower());
            sb.AppendLine("start_hook_called=" + _startHookCalled.ToString().ToLower());
            sb.AppendLine("stop_hook_called=" + _stopHookCalled.ToString().ToLower());
            sb.AppendLine("background_tasks_started=" + _backgroundTasksRequested.ToString().ToLower());
            sb.AppendLine("preview_start_called=" + _previewStartCalled.ToString().ToLower());
            sb.AppendLine("preview_done_called=" + _previewDoneCalled.ToString().ToLower());
            sb.AppendLine("preview_error_reason=" + (_previewErrorReason ?? ""));
            sb.AppendLine("upload_started=" + _uploadStarted.ToString().ToLower());
            sb.AppendLine("upload_finished=" + _uploadFinished.ToString().ToLower());
            sb.AppendLine("final_pipeline_state=" + GetFinalResult());
            sb.AppendLine("selftest_auto_stop_disabled: " + VideoPipelineSelfTestMode.ManualStopOnly.ToString().ToLower());
            sb.AppendLine("auto_stop_bypassed: " + _autoStopBypassed.ToString().ToLower());
            sb.AppendLine("manual_stop_available: true");
            sb.AppendLine("manual_stop_called_by_user: " + _manualStopCalledByUser.ToString().ToLower());
            sb.AppendLine("play_kept_alive_seconds: " + sessPlayKeptAliveSec.ToString("F1"));
            sb.AppendLine("start hook: " + (_recorderStartOk ? "OK" : "NG"));
            sb.AppendLine("stop hook: " + ((_lastStep == "recorder_stop" || _lastStep == "local_file") ? "OK" : "NG"));
            sb.AppendLine("output dir: " + (Path.GetDirectoryName(_outputPathBase) ?? GetOutputDir() ?? "(null)"));
            sb.AppendLine("writable: " + _outputDirWritable.ToString().ToLower());
            sb.AppendLine("temp exists: " + sessTempExists.ToString().ToLower());
            sb.AppendLine("temp bytes: " + sessTempBytes);
            sb.AppendLine("post stop poll entered: " + _postStopPollEntered.ToString().ToLower());
            sb.AppendLine("finalize entered: " + _finalizeEntered.ToString().ToLower());
            sb.AppendLine("finalize reason: " + (_finalizeReason ?? "none"));
            sb.AppendLine("movie file created: " + _localMp4Exists.ToString().ToLower());
            sb.AppendLine("movie path: " + (_localPath ?? _mp4Path ?? "(none)"));
            sb.AppendLine("fail step: " + (_errorStep ?? "none"));
            sb.AppendLine("");
            var elapsedSec = _endedAt.HasValue ? (_endedAt.Value - _startedAt).TotalSeconds : 0;
            var sessionIdForReport = (runStartedUtc != DateTime.MinValue && _recorderStartOk && !string.IsNullOrEmpty(_sessionId)) ? _sessionId : currSessionId;
            sb.AppendLine("sessionId=" + (sessionIdForReport ?? ""));
            sb.AppendLine("result=" + (string.IsNullOrEmpty(_result) ? GetFinalResult() : _result));
            sb.AppendLine("errorStep=" + (_errorStep ?? "none"));
            sb.AppendLine("unityVersion=" + Application.unityVersion);
            sb.AppendLine("platform=" + Application.platform);
            sb.AppendLine("editorOrPlayer=Editor");
            sb.AppendLine("recorderImplementation=Unity Recorder MovieRecorder");
            sb.AppendLine("outputDir=" + (Path.GetDirectoryName(_outputPathBase) ?? ""));
            sb.AppendLine("outputFileExpected=" + (_outputPathBase ?? ""));
            sb.AppendLine("outputDirExists=" + (!string.IsNullOrEmpty(_outputPathBase) && Directory.Exists(Path.GetDirectoryName(_outputPathBase))));
            sb.AppendLine("outputDirWritable=" + _outputDirWritable);
            if (Application.platform == RuntimePlatform.OSXEditor)
                sb.AppendLine("macOS_ScreenRecording=(check System Preferences > Security & Privacy > Privacy)");
            sb.AppendLine("elapsedSec=" + elapsedSec.ToString("F1"));
            var localMp4PathForReport = _localPath ?? _mp4Path ?? (latestMp4UpdatedThisRun ? latestMp4Path : null) ?? mp4PathCurr ?? "";
            var localMp4ExistsForReport = _localMp4Exists || latestMp4UpdatedThisRun;
            sb.AppendLine("local_mp4_path=" + (localMp4PathForReport ?? ""));
            sb.AppendLine("local_mp4_exists=" + localMp4ExistsForReport.ToString().ToLower());
            sb.AppendLine("local_mp4_size_bytes=" + _localSizeBytes);
            sb.AppendLine("latest_mp4_path=" + (_latestMp4Path ?? ""));
            sb.AppendLine("daily_archive_path=" + (_dailyArchivePath ?? ""));
            sb.AppendLine("daily_archive_created=" + _dailyArchiveCreated.ToString().ToLower());
            sb.AppendLine("upload_copy_success=" + _uploadCopySuccess.ToString().ToLower());
            sb.AppendLine("upload_verify_warning=" + (_uploadVerifyWarning ?? ""));
            sb.AppendLine("final_result=" + GetFinalResult());
            sb.AppendLine("preview_path=" + (_previewPath ?? ""));
            sb.AppendLine("preview_created=" + _previewCreated.ToString().ToLower());
            sb.AppendLine("preview_type=" + (_previewType ?? "none"));
            sb.AppendLine("preview_path=" + (_previewPath ?? ""));
            sb.AppendLine("preview_exists=" + _previewCreated.ToString().ToLower());
            sb.AppendLine("preview_size_bytes=" + _previewSizeBytes);
            sb.AppendLine("preview_drive_link=" + (_previewGifDriveLink ?? ""));
            var gifPathForReport = gifUpdatedThisRun ? gifPathToCheck : (_gifPath ?? (_previewType == "gif" ? _previewPath : "") ?? gifPathCurr ?? "");
            sb.AppendLine("gif_path=" + (gifPathForReport ?? ""));
            sb.AppendLine("gif_exists=" + gifUpdatedThisRun.ToString().ToLower());
            sb.AppendLine("gif_size_bytes=" + (_previewType == "gif" ? _previewGifSize : 0));
            sb.AppendLine("preview_fallback_used=" + _previewFallbackUsed.ToString().ToLower());
            sb.AppendLine("ffmpeg_path=" + (_ffmpegPathUsed ?? ""));
            sb.AppendLine("ffmpeg_available=" + _ffmpegAvailable.ToString().ToLower());
            string ffmpegStatus = "PENDING";
            if (_previewStatus != "PENDING" && _previewStatus != null && !_previewStatus.StartsWith("PENDING"))
                ffmpegStatus = _ffmpegAvailable ? "READY" : "ERROR";
            sb.AppendLine("ffmpeg_status=" + ffmpegStatus);
            sb.AppendLine("preview_status=" + (_previewStatus ?? "PENDING"));
            sb.AppendLine("preview_gif_path=" + (_previewPath != null && _previewPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? _previewPath : (_gifPath ?? "")));
            sb.AppendLine("preview_gif_size=" + (_previewPath != null && _previewPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? _previewGifSize : 0));
            sb.AppendLine("preview_gif_drive_link=" + (_previewGifDriveLink ?? ""));
            if (_errorStep == "mp4_not_created" || _errorStep == "recorder_start_failed")
            {
                var exists = !string.IsNullOrEmpty(_mp4Path) && File.Exists(_mp4Path);
                long sz = 0;
                if (exists) try { sz = new FileInfo(_mp4Path).Length; } catch { }
                sb.AppendLine("mp4_poll_detection=newest_in_Recordings");
                sb.AppendLine("mp4_poll_expectedPath=" + (_mp4Path ?? "(none)"));
                sb.AppendLine("mp4_poll_actualPath=" + (_localPath ?? _mp4Path ?? "(none)"));
                sb.AppendLine("mp4_poll_FileExists=" + exists.ToString().ToLower());
                sb.AppendLine("mp4_poll_size_bytes=" + sz);
                sb.AppendLine("mp4_poll_count=" + _lastMp4PollCount);
                sb.AppendLine("mp4_poll_interval_sec=" + Mp4PollIntervalSec);
            }
            sb.AppendLine("upload_attempted=" + (_lastStep == "rclone" || _lastStep == "upload" || _errorStep == "upload" ? "true" : "false"));
            sb.AppendLine("upload_success=" + (_driveFileStatus != null && _driveFileStatus.StartsWith("found") ? "true" : "false"));
            sb.AppendLine("upload_error=" + (_driveFileStatus == "not_found" ? "UPLOAD_TIMEOUT_OR_FAILED" : "none"));
            sb.AppendLine("drive_file=" + (_driveFileStatus ?? ""));
            sb.AppendLine("drive_share_link=" + (_driveShareLink ?? ""));
            sb.AppendLine("drive_size_bytes=" + _driveSizeBytes);
            sb.AppendLine("drive_uploaded=" + (_driveFileStatus != null && _driveFileStatus.StartsWith("found") ? "true" : "false"));
            var driveId = ExtractDriveFileId(_driveShareLink);
            var directView = !string.IsNullOrEmpty(driveId) ? "https://drive.google.com/file/d/" + driveId + "/view?usp=sharing" : "";
            var directDownload = !string.IsNullOrEmpty(driveId) ? "https://drive.google.com/uc?export=download&id=" + driveId : "";
            var drivePermission = !string.IsNullOrEmpty(directView) ? "public" : "restricted";
            sb.AppendLine("direct_view_url=" + (directView ?? ""));
            sb.AppendLine("direct_download_url=" + (directDownload ?? ""));
            sb.AppendLine("drive_permission=" + drivePermission);
            sb.AppendLine("upload_result=" + GetUploadResult());
            sb.AppendLine("upload_status=" + (_driveFileStatus == "pending" ? "PENDING" : (_driveFileStatus != null && _driveFileStatus.StartsWith("found") ? "DONE" : (_driveFileStatus == "not_found" ? "ERROR" : "PENDING"))));
            sb.AppendLine("scene=" + (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? ""));
            sb.AppendLine("slack_message=" + (_slackMessageStatus ?? ""));
            sb.AppendLine("slack_posted=" + (_slackMessageStatus != null && _slackMessageStatus.StartsWith("posted") ? "true" : "false"));
            sb.AppendLine("slack_error=" + (_slackError ?? ""));
            sb.AppendLine("roof_surface_size=" + (_roofSurfaceSize ?? "(pending)"));
            sb.AppendLine("snow_cover_size=" + (_snowCoverSize ?? "(pending)"));
            sb.AppendLine("snow_cover_matches_roof=" + _snowCoverMatchesRoof.ToString().ToLower());
            sb.AppendLine("");
            sb.AppendLine("=== ASSI REPORT - VIDEO PIPELINE RECOVERY HARD MODE ===");
            var currentSessionIdForRecovery = (runStartedUtc != DateTime.MinValue && _recorderStartOk && !string.IsNullOrEmpty(_sessionId)) ? _sessionId : currSessionId;
            sb.AppendLine("current_session_id=" + (currentSessionIdForRecovery ?? ""));
            sb.AppendLine("report_generated_at=" + reportGeneratedAt);
            var tempMp4CreatedThisSession = sessTempExists && (_recorderStartOk || _startHookCalled) && runStartedUtc != DateTime.MinValue && sessTempModified.HasValue && sessTempModified.Value >= runStartedUtc.AddSeconds(-5);
            sb.AppendLine("temp_mp4_created_this_session=" + (tempMp4CreatedThisSession ? "YES" : "NO"));
            sb.AppendLine("temp_mp4_path=" + (sessTempPath ?? ""));
            sb.AppendLine("temp_mp4_modified_time=" + (sessTempModified.HasValue ? sessTempModified.Value.ToString("o") : "(n/a)"));
            sb.AppendLine("final_mp4_created_this_session=" + (latestMp4UpdatedThisRun ? "YES" : "NO"));
            sb.AppendLine("final_mp4_path=" + (latestMp4Exists ? latestMp4Path : ""));
            sb.AppendLine("final_mp4_modified_time=" + (latestMp4Modified.HasValue ? latestMp4Modified.Value.ToString("o") : "(n/a)"));
            sb.AppendLine("gif_created_this_session=" + (gifUpdatedThisRun ? "YES" : "NO"));
            sb.AppendLine("gif_path=" + (gifExists ? gifPathToCheck : ""));
            sb.AppendLine("gif_modified_time=" + (gifModified.HasValue ? gifModified.Value.ToString("o") : "(n/a)"));
            var rootCause = "ok";
            if (!latestMp4UpdatedThisRun && !gifUpdatedThisRun)
            {
                if (!_recorderStartOk)
                    rootCause = !AutoRecordOnPlay ? "AutoRecordOnPlay_off_enable_SnowPanic_VideoPipeline_Auto_record_on_Play_or_run_SelfTest" : "recorder_not_started";
                else if (!_outputDirWritable) rootCause = "outputDir_not_writable";
                else if (!_postStopPollEntered) rootCause = "post_stop_poll_not_entered";
                else if (!_finalizeEntered) rootCause = "finalize_not_run";
                else rootCause = "temp_mp4_not_created_or_not_promoted";
            }
            else if (latestMp4UpdatedThisRun && !gifUpdatedThisRun)
                rootCause = !string.IsNullOrEmpty(_previewErrorReason) ? _previewErrorReason : "gif_generation_failed";
            sb.AppendLine("root_cause=" + rootCause);
            var whatWasFixed = "stale_sessionId_cleared_at_ExitingEditMode;gif_reverted_to_snow_test_latest;no_old_success_log_in_report";
            sb.AppendLine("what_was_fixed=" + whatWasFixed);
            sb.AppendLine("regression_guard_added=YES");
            sb.AppendLine("old_success_log_suppressed=YES");
            var recoveryResult = (latestMp4UpdatedThisRun && gifUpdatedThisRun) ? "PASS" : "FAIL";
            sb.AppendLine("recovery_result=" + recoveryResult);
            sb.AppendLine("");
            if (!string.IsNullOrEmpty(_lastRecorderExceptionMessage))
                sb.AppendLine("exception=" + (_lastRecorderExceptionMessage ?? "").Replace("\r", " ").Replace("\n", " | "));
            if (!string.IsNullOrEmpty(_lastRecorderExceptionStackTrace))
                sb.AppendLine("stacktrace=" + (_lastRecorderExceptionStackTrace ?? "").Replace("\r", " ").Replace("\n", " | "));
            File.WriteAllText(path, sb.ToString());
        }
        catch { }
    }

    /// <summary>mp4確定時点で最小レポートを即出力し、コピー可能にする。</summary>
    static void EmitEarlyReportAndCopy()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        _earlyReportEmitted = true;
        _earlyReportEmittedAt = DateTime.Now;
        WriteSessionData();
        if (SnowLoopNoaReportAutoCopy.TryBuildReportOrSelfTestReport())
        {
            AssiReportWindow.OpenAndShowReport();
            var report = SnowLoopNoaReportAutoCopy.GetReportContent();
            if (!string.IsNullOrEmpty(report))
                EditorGUIUtility.systemCopyBuffer = report;
        }
        sw.Stop();
        _reportCopyReadyTimeMs = sw.ElapsedMilliseconds;
        AssiLog("report_emitted_early=true");
        AssiLog("report_copy_ready_time_ms=" + _reportCopyReadyTimeMs);
        AssiLog("preview_started_async=" + _previewStartedAsync.ToString().ToLower());
        AssiLog("upload_started_async=" + _uploadStartedAsync.ToString().ToLower());
        AssiLog("slack_started_async=" + _slackStartedAsync.ToString().ToLower());
    }

    static string GetFinalResult()
    {
        if (_localMp4Exists && (_driveFileStatus != null && _driveFileStatus.StartsWith("found")))
            return "DRIVE_READY";
        if (_localMp4Exists && _previewCreated) return "LOCAL_READY";
        if (_localMp4Exists) return "LOCAL_READY";
        if (_result == "SUCCESS" || _result == "DRIVE_READY") return "DRIVE_READY";
        if (_driveFileStatus != null && _driveFileStatus.StartsWith("found")) return "DRIVE_READY";
        return _result ?? "ERROR";
    }

    static string GetUploadResult()
    {
        if (_driveFileStatus != null && _driveFileStatus.StartsWith("found")) return "DRIVE_READY";
        if (_localMp4Exists) return "LOCAL_READY";
        if (_lastStep == "rclone" || _lastStep == "upload" || _errorStep == "upload")
            return (_driveFileStatus == "not_found" || !(_driveFileStatus != null && _driveFileStatus.StartsWith("found"))) ? "ERROR" : "DRIVE_READY";
        return "NOT_RUN";
    }

    static string ExtractDriveFileId(string shareLink)
    {
        if (string.IsNullOrEmpty(shareLink) || !shareLink.StartsWith("http")) return "";
        var m = Regex.Match(shareLink, @"/d/([a-zA-Z0-9_-]+)");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(shareLink, @"[?&]id=([a-zA-Z0-9_-]+)");
        if (m.Success) return m.Groups[1].Value;
        return "";
    }

    static string MapLastStepToErrorStep()
    {
        if (_lastStep == "start" || _lastStep == "recorder_start") return "recorder";
        if (_lastStep == "recorder_start_exception" || _lastStep == "recorder_start_timeout" || _lastStep == "play_never_entered" || _lastStep == "recorder_start_failed") return "recorder";
        if (_lastStep == "recorder_stop" || _lastStep == "rec_done" || _lastStep == "rec_timeout" || _lastStep == "local_file" || _lastStep == "recording_running") return "none";
        if (_lastStep == "rclone" || _lastStep == "upload") return "upload";
        if (_lastStep == "slack") return "slack_post";
        if (_lastStep.Contains("mp4") || _lastStep.Contains("file")) return "mp4_not_created";
        if (_lastStep.Contains("verify") || _lastStep.Contains("drive")) return "drive_verify";
        return _lastStep ?? "unknown";
    }

    static void RunExitRoutine(bool timedOut)
    {
        if (_exitRoutineRan) return;
        _exitRoutineRan = true;

        UnsubscribeLogHandler();
        _endedAt = DateTime.Now;

        if (timedOut)
        {
            _result = "TIMEOUT";
            _errorStep = _lastStep ?? "unknown";
            var timeoutElapsed = _endedAt.HasValue ? (_endedAt.Value - _startedAt).TotalSeconds : 0;
            AssiLog("step=timeout after=" + timeoutElapsed.ToString("F1") + "s lastStep=" + _lastStep);
        }
        else if (string.IsNullOrEmpty(_result) || _result == "IN_PROGRESS")
            _result = "ERROR";
        if (string.IsNullOrEmpty(_errorStep))
            _errorStep = MapLastStepToErrorStep();

        WriteConsoleRingBuffer();
        var tempPath = EnsureTempMp4Path();
        var tempExists = !string.IsNullOrEmpty(tempPath) && File.Exists(tempPath);
        long tempBytes = 0;
        if (tempExists) try { tempBytes = new FileInfo(tempPath).Length; } catch { }
        if (!_localMp4Exists && tempExists && tempBytes > 0)
        {
            if (TryFinalizeFromTemp())
            {
                AssiLog("step=finalize_from_temp (RunExitRoutine last-chance)");
                if (_errorStep == "mp4_not_created") _errorStep = "none";
                _result = "LOCAL_READY";
                _previewStatus = "PENDING";
                _driveFileStatus = "pending";
                _backgroundTasksPending = true;
                _backgroundTasksRequested = true;
                AssiLog("[VideoPipeline] background_tasks_started=true");
                AssiLog("background_tasks_started=true");
                _previewStartedAsync = true;
                _uploadStartedAsync = true;
                _slackStartedAsync = true;
                WriteSessionData();
                EmitEarlyReportAndCopy();
                Task.Run(() =>
                {
                    try
                    {
                        RunUploadPhaseFromLatest();
                    }
                    catch (Exception ex)
                    {
                        AssiLog("step=background_exception ex=" + (ex.Message ?? "").Replace("\r", " ").Replace("\n", " | ") + " -> fallback_to_main_thread");
                        _backgroundFallbackToMainThread = true;
                        return;
                    }
                    _backgroundTasksPending = false;
                    _backgroundWorkComplete = true;
                });
            }
            else if (_errorStep == "mp4_not_created")
            {
                _errorStep = "finalize_not_run";
            }
        }
        else if (tempBytes > 0 && _errorStep == "mp4_not_created")
        {
            _errorStep = "finalize_not_run";
        }
        var playKeptAliveSec = _endedAt.HasValue ? (_endedAt.Value - _startedAt).TotalSeconds : (DateTime.Now - _startedAt).TotalSeconds;
        AssiLog("=== VIDEO PIPELINE STATUS ===");
        AssiLog("selftest_auto_stop_disabled: " + VideoPipelineSelfTestMode.ManualStopOnly.ToString().ToLower());
        AssiLog("auto_stop_bypassed: " + _autoStopBypassed.ToString().ToLower());
        AssiLog("manual_stop_available: true");
        AssiLog("manual_stop_called_by_user: " + _manualStopCalledByUser.ToString().ToLower());
        AssiLog("play_kept_alive_seconds: " + playKeptAliveSec.ToString("F1"));
        AssiLog("start hook: " + (_recorderStartOk ? "OK" : "NG"));
        AssiLog("stop hook: " + ((_lastStep == "recorder_stop" || _lastStep == "local_file") ? "OK" : "NG"));
        AssiLog("output dir: " + (Path.GetDirectoryName(_outputPathBase) ?? GetOutputDir() ?? "(null)"));
        AssiLog("writable: " + _outputDirWritable.ToString().ToLower());
        AssiLog("temp exists: " + tempExists.ToString().ToLower());
        AssiLog("temp bytes: " + tempBytes);
        AssiLog("post stop poll entered: " + _postStopPollEntered.ToString().ToLower());
        AssiLog("finalize entered: " + _finalizeEntered.ToString().ToLower());
        AssiLog("finalize reason: " + (_finalizeReason ?? "none"));
        AssiLog("movie file created: " + _localMp4Exists.ToString().ToLower());
        AssiLog("movie path: " + (_localPath ?? _mp4Path ?? "(none)"));
        AssiLog("fail step: " + (_errorStep ?? "none"));
        AssiLog("execution_mode=" + (_executionMode ?? "unknown") + " start_hook_called=" + _startHookCalled.ToString().ToLower() + " stop_hook_called=" + _stopHookCalled.ToString().ToLower() + " background_tasks_started=" + _backgroundTasksRequested.ToString().ToLower() + " preview_start_called=" + _previewStartCalled.ToString().ToLower() + " preview_done_called=" + _previewDoneCalled.ToString().ToLower() + " preview_error_reason=" + (_previewErrorReason ?? "") + " upload_started=" + _uploadStarted.ToString().ToLower() + " upload_finished=" + _uploadFinished.ToString().ToLower() + " final_pipeline_state=" + GetFinalResult());
        WriteSessionData();
        IsSelfTestSession = true;
        VideoPipelineSelfTestMode.SetActive(false);
        DeleteSessionActive();
        _sessionRunLogPath = null;

        if (EditorApplication.isPlaying)
            EditorApplication.ExitPlaymode();

        EditorApplication.delayCall += () =>
        {
            if (!_earlyReportEmitted && SnowLoopNoaReportAutoCopy.TryBuildReportOrSelfTestReport())
            {
                AssiReportWindow.OpenAndShowReport();
                if (IsSelfTestSession)
                {
                    var report = SnowLoopNoaReportAutoCopy.GetReportContent();
                    if (!string.IsNullOrEmpty(report))
                        EditorGUIUtility.systemCopyBuffer = report;
                    IsSelfTestSession = false;
                }
            }
            else if (_earlyReportEmitted)
            {
                IsSelfTestSession = false;
            }
            if (_state == State.Done) _state = State.Idle;
        };
    }

    static string GetWebhookPath() => Path.Combine(GetHome(), ".snowpanic_slack_webhook");
    static string ReadWebhookUrl()
    {
        var p = GetWebhookPath();
        if (!File.Exists(p)) return null;
        try { return File.ReadAllText(p).Trim(); }
        catch { return null; }
    }

    const int FfmpegTimeoutMs = 15000;

    static string ResolveFfmpegPath()
    {
        foreach (var p in new[] { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg" })
        { if (File.Exists(p)) return p; }
        var which = RunZsh("which ffmpeg 2>/dev/null || true", 3000);
        if (string.IsNullOrEmpty(which) || which.Contains("[ERROR]")) return null;
        var lines = which.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        var first = lines[0].Trim();
        return File.Exists(first) ? first : null;
    }

    /// <summary>_latestMp4Path / _localPath から archive → preview → rclone → slack を実行。TryFinalizeFromTemp 後の last-chance 用。</summary>
    static void RunUploadPhaseFromLatest()
    {
        // Drive upload を完全停止
        AssiLog("step=upload_disabled");
        _uploadStatus = "DISABLED";
    }

    /// <summary>mp4から軽量プレビューを必ず生成。gif→png_sequence→contact_sheet→qlmanage→placeholder。</summary>
    static void GeneratePreview(string mp4Path)
    {
    }

    static void SetPreviewResult(string path, string type, long sizeBytes)
    {
        _previewPath = Path.GetFullPath(path);
        _previewCreated = true;
        _previewType = type;
        _previewSizeBytes = sizeBytes;
        if (type == "gif") _previewGifSize = sizeBytes;
    }

    static bool TryCreateContactSheet(string framesDir, string outputPath)
    {
        var files = Directory.GetFiles(framesDir, "*.png").OrderBy(f => f).ToArray();
        if (files.Length == 0) return false;
        UnityEngine.Texture2D[] texs = new UnityEngine.Texture2D[files.Length];
        try
        {
            for (int i = 0; i < files.Length; i++)
            {
                var bytes = File.ReadAllBytes(files[i]);
                var t = new UnityEngine.Texture2D(2, 2);
                if (!t.LoadImage(bytes)) { t = null; continue; }
                texs[i] = t;
            }
            var w = texs.Where(t => t != null).Select(t => t.width).DefaultIfEmpty(0).Max();
            var h = texs.Where(t => t != null).Select(t => t.height).DefaultIfEmpty(0).Max();
            if (w <= 0 || h <= 0) return false;
            var outW = w * files.Length;
            var outTex = new UnityEngine.Texture2D(outW, h);
            for (int oy = 0; oy < h; oy++) for (int ox = 0; ox < outW; ox++) outTex.SetPixel(ox, oy, UnityEngine.Color.clear);
            int x = 0;
            for (int i = 0; i < texs.Length; i++)
            {
                if (texs[i] == null) continue;
                var tw = texs[i].width;
                var th = texs[i].height;
                for (int py = 0; py < th && py < h; py++)
                    for (int px = 0; px < tw && px < w; px++)
                        outTex.SetPixel(x + px, h - 1 - py, texs[i].GetPixel(px, th - 1 - py));
                x += w;
                UnityEngine.Object.DestroyImmediate(texs[i]);
            }
            var png = outTex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(outTex);
            if (png == null || png.Length == 0) return false;
            File.WriteAllBytes(outputPath, png);
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }
        finally
        {
            foreach (var t in texs) { if (t != null) UnityEngine.Object.DestroyImmediate(t); }
        }
    }

    static bool TryImageMagickPngToGif(string framesDir, string[] pngPaths, string outputGifPath)
    {
        var fileList = string.Join(" ", pngPaths.Select(p => "\"" + p.Replace("\"", "\\\"") + "\""));
        foreach (var pair in new[] { ("magick", "convert"), ("convert", "") })
        {
            var which = RunZsh("which " + pair.Item1 + " 2>/dev/null || true", 2000);
            if (string.IsNullOrEmpty(which) || which.Contains("[ERROR]")) continue;
            var lines = which.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) continue;
            var exe = lines[0].Trim();
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) continue;
            var cmd = string.IsNullOrEmpty(pair.Item2)
                ? "\"" + exe + "\" -delay 10 -loop 0 " + fileList + " \"" + outputGifPath + "\" 2>&1"
                : "\"" + exe + "\" " + pair.Item2 + " -delay 10 -loop 0 " + fileList + " \"" + outputGifPath + "\" 2>&1";
            int exitCode;
            RunZshWithExitCode(cmd, 10000, out exitCode);
            if (exitCode == 0 && File.Exists(outputGifPath) && new FileInfo(outputGifPath).Length > 0)
                return true;
        }
        return false;
    }

    static bool CreatePlaceholderPreview(string outputPath)
    {
        var w = 640; var h = 360;
        var tex = new UnityEngine.Texture2D(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, y, new UnityEngine.Color(0.2f, 0.3f, 0.5f));
        var png = tex.EncodeToPNG();
        UnityEngine.Object.DestroyImmediate(tex);
        if (png == null || png.Length == 0) return false;
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(outputPath, png);
        return File.Exists(outputPath);
    }

    static string ResolveRclonePath()
    {
        if (File.Exists(RclonePath)) return RclonePath;
        var which = RunZsh("which rclone 2>/dev/null || true", 3000);
        if (string.IsNullOrEmpty(which) || which.Contains("[ERROR]")) return null;
        var lines = which.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;
        var first = lines[0].Trim();
        return File.Exists(first) ? first : null;
    }

    static string RunZsh(string command, int timeoutMs)
    {
        int _;
        return RunZshWithExitCode(command, timeoutMs, out _);
    }

    static string RunZshWithExitCode(string command, int timeoutMs, out int exitCode)
    {
        exitCode = -1;
        try
        {
            using (var p = new Process())
            {
                p.StartInfo.FileName = "/bin/zsh";
                p.StartInfo.Arguments = "-lc \"" + command.Replace("\"", "\\\"") + "\"";
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.Start();
                var sb = new StringBuilder();
                p.OutputDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (_, e) => { if (e.Data != null) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { p.Kill(); return "[TIMEOUT]"; }
                exitCode = p.ExitCode;
                return sb.ToString().Trim();
            }
        }
        catch (Exception ex) { return "[ERROR] " + ex.Message; }
    }

    /// <summary>rclone copy を実行し、lsjson で Drive 上のサイズ確認。copy exit=0 なら成功扱い。verify 失敗は WARNING のみ。</summary>
    static bool RunRcloneCopyAndVerify(string localPath, long localSizeBytes, string filename, out long driveSizeBytes, out string shareLink)
    {
        driveSizeBytes = 0;
        shareLink = "";
        _uploadCopySuccess = false;
        _uploadVerifyWarning = "";
        var rclone = ResolveRclonePath();
        if (string.IsNullOrEmpty(rclone))
        {
            AssiLog("rclone not found. which=" + RunZsh("which rclone 2>/dev/null", 2000));
            return false;
        }
        _lastStep = "upload";
        AssiLog("step=upload_start");
        AssiLog("step=upload rclone=" + rclone + " local_size=" + localSizeBytes);
        AssiLog("[VideoPipeline] upload_remote=" + RcloneRemote);
        AssiLog("[VideoPipeline] upload_remote_dir=" + RcloneRemote + "/");
        var expectedRemotePath = RcloneRemote + "/" + filename;
        AssiLog("[VideoPipeline] upload_remote_expected_path=" + expectedRemotePath);

        string drivePath = "";
        string linkCmd = "";
        string linkOut = "";
        int exitCode;
        var copyCmd = "\"" + rclone + "\" copy \"" + localPath + "\" \"" + RcloneRemote + "/\"";
        var copyOut = RunZshWithExitCode(copyCmd, RcloneTimeoutMs, out exitCode);
        if (exitCode != 0 || copyOut.Contains("[ERROR]") || copyOut.Contains("[TIMEOUT]"))
        {
            AssiLog("UPLOAD_ERROR exit=" + exitCode + " out=" + (copyOut.Length > 150 ? copyOut.Substring(0, 150) + "..." : copyOut));
            return false;
        }
        _uploadCopySuccess = true;
        AssiLog("UPLOAD_COPY_DONE exit=0");
        AssiLog("[VideoPipeline] upload_copy_success=true");

        var lsjsonTarget = RcloneRemote;
        AssiLog("[VideoPipeline] upload_verify_method=lsjson upload_verify_target=" + lsjsonTarget);
        var lsjsonCmd = "\"" + rclone + "\" lsjson \"" + lsjsonTarget + "\"";
        var lsjsonOut = RunZshWithExitCode(lsjsonCmd, 5000, out exitCode);
        if (exitCode != 0 || string.IsNullOrEmpty(lsjsonOut) || lsjsonOut.Contains("[ERROR]") || lsjsonOut.Contains("[TIMEOUT]"))
        {
            var warn = "lsjson exit=" + exitCode + " (Drive サイズ確認不可)";
            _uploadVerifyWarning = warn;
            AssiLog("UPLOAD_VERIFY_WARNING " + warn);
            AssiLog("[VideoPipeline] upload_verify_warning=" + warn);
            driveSizeBytes = localSizeBytes;
            AssiLog("step=upload_done verify_skipped (copy succeeded) url=(none)");
            return true;
        }
        var esc = Regex.Escape(filename);
        var sizeMatch = Regex.Match(lsjsonOut, "\"Name\":\\s*\"" + esc + "\"[^}]*\"Size\":\\s*(\\d+)", RegexOptions.Singleline);
        if (!sizeMatch.Success)
            sizeMatch = Regex.Match(lsjsonOut, "\"Path\":\\s*\"" + esc + "\"[^}]*\"Size\":\\s*(\\d+)", RegexOptions.Singleline);
        if (!sizeMatch.Success)
            sizeMatch = Regex.Match(lsjsonOut, "\"Size\":\\s*(\\d+)[^}]*\"Name\":\\s*\"" + esc + "\"", RegexOptions.Singleline);
        if (!sizeMatch.Success)
        {
            var warn = "file not found in lsjson. filename=" + filename + " lsjson_target=" + lsjsonTarget;
            _uploadVerifyWarning = warn;
            AssiLog("UPLOAD_VERIFY_WARNING " + warn);
            AssiLog("[VideoPipeline] upload_verify_warning=" + warn);
            driveSizeBytes = localSizeBytes;
            drivePath = RcloneRemote + "/" + filename;
            linkCmd = "\"" + rclone + "\" link \"" + drivePath + "\"";
            linkOut = RunZshWithExitCode(linkCmd, 5000, out exitCode);
            if (exitCode == 0 && !string.IsNullOrEmpty(linkOut) && !linkOut.Contains("[ERROR]") && linkOut.StartsWith("http"))
            {
                var firstLine = linkOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                shareLink = firstLine.Length > 0 ? firstLine[0].Trim() : linkOut.Trim();
            }
            if (string.IsNullOrEmpty(shareLink)) AssiLog("rclone link skipped (not supported or failed)");
            AssiLog("step=upload_done verify_warning (copy succeeded) url=" + (string.IsNullOrEmpty(shareLink) ? "(none)" : shareLink));
            return true;
        }
        if (!long.TryParse(sizeMatch.Groups[1].Value, out driveSizeBytes) || driveSizeBytes <= 0)
        {
            var warn = "drive_size=0 or invalid";
            _uploadVerifyWarning = warn;
            AssiLog("UPLOAD_VERIFY_WARNING " + warn);
            AssiLog("[VideoPipeline] upload_verify_warning=" + warn);
            driveSizeBytes = localSizeBytes;
            AssiLog("step=upload_done verify_warning (copy succeeded) url=(none)");
            return true;
        }
        if (driveSizeBytes != localSizeBytes)
        {
            var warn = "size_mismatch local=" + localSizeBytes + " drive=" + driveSizeBytes;
            _uploadVerifyWarning = warn;
            AssiLog("UPLOAD_VERIFY_WARNING " + warn);
            AssiLog("[VideoPipeline] upload_verify_warning=" + warn);
            driveSizeBytes = localSizeBytes;
            AssiLog("step=upload_done verify_warning (copy succeeded) url=(none)");
            return true;
        }
        AssiLog("UPLOAD_VERIFY_OK drive_size=" + driveSizeBytes);

        drivePath = RcloneRemote + "/" + filename;
        linkCmd = "\"" + rclone + "\" link \"" + drivePath + "\"";
        linkOut = RunZshWithExitCode(linkCmd, 5000, out exitCode);
        if (exitCode == 0 && !string.IsNullOrEmpty(linkOut) && !linkOut.Contains("[ERROR]") && linkOut.StartsWith("http"))
        {
            var firstLine = linkOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            shareLink = firstLine.Length > 0 ? firstLine[0].Trim() : linkOut.Trim();
        }
        if (string.IsNullOrEmpty(shareLink)) AssiLog("rclone link skipped (not supported or failed)");
        AssiLog("step=upload_done url=" + (string.IsNullOrEmpty(shareLink) ? "(none)" : shareLink));

        return true;
    }

    static string BuildSlackMessage(string result, string errorStep, string localPath, long localFileSizeBytes, string drivePath, long driveFileSizeBytes, string driveShareLink, string previewGifPath = "", string previewGifDriveLink = "")
    {
        var sb = new StringBuilder();
        sb.AppendLine("VIDEO PIPELINE SELFTEST result=" + result);
        if (!string.IsNullOrEmpty(errorStep)) sb.AppendLine("error_step=" + errorStep);
        sb.AppendLine("local_path=" + (localPath ?? ""));
        sb.AppendLine("local_file_size_bytes=" + localFileSizeBytes);
        sb.AppendLine("drive_path=" + (drivePath ?? ""));
        sb.AppendLine("drive_file_size_bytes=" + driveFileSizeBytes);
        sb.AppendLine("drive_share_link=" + (string.IsNullOrEmpty(driveShareLink) ? "(none)" : driveShareLink));
        if (!string.IsNullOrEmpty(previewGifPath))
        {
            sb.AppendLine("preview_path=" + previewGifPath);
            sb.AppendLine("preview_drive_link=" + (string.IsNullOrEmpty(previewGifDriveLink) ? "(none)" : previewGifDriveLink));
        }
        return sb.ToString().TrimEnd();
    }

    static bool RunSlackNotify(string text, int timeoutMs)
    {
        // Slack 送信を完全停止
        _slackMessageStatus = "not_posted";
        AssiLog("SLACK_DISABLED");
        return true;
    }

    [MenuItem("SnowPanic/Self Test", false, 1)]
    [MenuItem("SnowPanic/VideoPipeline/SelfTest", false, 150)]
    public static void RunSelfTest()
    {
        if (_state == State.Recording)
        {
            RunManualStop();
            return;
        }
        if (_state != State.Idle)
        {
            UnityEngine.Debug.LogWarning("[VideoPipeline] SelfTest already in progress.");
            return;
        }

        _exitRoutineRan = false;
        _postStopPollEntered = false;
        _finalizeEntered = false;
        _finalizeReason = "";
        _manualStopCalledByUser = false;
        _autoStopBypassed = true;
        _executionMode = "selftest";
        _startHookCalled = false;
        _stopHookCalled = false;
        _previewStartCalled = false;
        _previewDoneCalled = false;
        _previewErrorReason = "";
        _uploadStarted = false;
        _uploadFinished = false;
        _earlyReportEmitted = false;
        _reportCopyReadyTimeMs = 0;
        _finalResultEmitted = false;
        _previewStartedAsync = false;
        _uploadStartedAsync = false;
        _slackStartedAsync = false;
        _backgroundWorkComplete = false;
        _backgroundFallbackToMainThread = false;
        VideoPipelineSelfTestMode.ManualStopOnly = true;
        lock (_consoleRingBuffer) { _consoleRingBuffer.Clear(); }
        lock (_consoleRingBufferFull) { _consoleRingBufferFull.Clear(); }

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionId = "vp_" + timestamp;
        _startedAt = DateTime.Now;
        _endedAt = null;
        _result = "";
        _errorStep = "none";
        _localPath = "";
        _localSizeBytes = 0;
        _drivePath = "";
        _driveSizeBytes = 0;
        _driveShareLink = "";
        _slackMessageTsOrLink = "";
        _localMp4Exists = false;
        _driveFileStatus = "";
        _slackMessageStatus = "";
        _slackError = "";
        _lastMp4PollCount = -1;
        WriteLastRunTimestamp();

        var outputDir = ResolveAndEnsureWritableOutputDir();
        if (string.IsNullOrEmpty(outputDir))
        {
            AssiLog("step=recorder_start_exception reason=outputDir_resolve_or_writable_failed");
            UnityEngine.Debug.LogError("[VideoPipeline] outputDir RESOLVE FAILED - check Recordings or ~/SnowPanicVideos/Recordings");
            _result = "ERROR";
            _errorStep = "outputDir_resolve_failed";
            _state = State.Done;
            _endedAt = DateTime.Now;
            WriteSessionData();
            RunExitRoutine(timedOut: false);
            return;
        }
        UnityEngine.Debug.Log("[VideoPipeline] outputDir=" + outputDir);
        var dirExists = Directory.Exists(outputDir);
        var writable = TestOutputDirWritable(outputDir);
        AssiLog("step=recorder_start outputDir=" + outputDir + " outputDirExists=" + dirExists + " outputDirWritable=" + writable);
        _outputDirWritable = writable;
        _outputPathBase = Path.Combine(outputDir, "snow_test_tmp_" + _sessionId);
        _tempMp4Path = Path.GetFullPath(Path.Combine(outputDir, "snow_test_tmp_" + _sessionId + ".mp4"));
        _mp4Path = Path.GetFullPath(Path.Combine(outputDir, "snow_test_latest.mp4"));
        _latestMp4Path = "";
        _dailyArchivePath = "";
        _dailyArchiveCreated = false;
        _previewPath = "";
        _previewCreated = false;
        _previewGifSize = 0;
        _previewType = "none";
        _previewSizeBytes = 0;
        _previewFallbackUsed = false;
        _gifPath = "";
        _ffmpegPathUsed = "";
        _ffmpegAvailable = false;
        _previewStatus = "PREVIEW_ERROR";
        _previewGifDriveLink = "";
        _uploadCopySuccess = false;
        _uploadVerifyWarning = "";
        _recordingStartedAt = DateTime.MinValue;
        if (!dirExists)
        {
            AssiLog("step=recorder_start_exception reason=outputDir_not_exists path=" + outputDir);
            _result = "ERROR";
            _errorStep = "recorder_start_failed";
            _state = State.Done;
            _endedAt = DateTime.Now;
            WriteSessionData();
            IsSelfTestSession = true;
            EditorApplication.delayCall += () =>
            {
                if (SnowLoopNoaReportAutoCopy.TryBuildReportOrSelfTestReport())
                {
                    AssiReportWindow.OpenAndShowReport();
                    var report = SnowLoopNoaReportAutoCopy.GetReportContent();
                    if (!string.IsNullOrEmpty(report)) EditorGUIUtility.systemCopyBuffer = report;
                    IsSelfTestSession = false;
                }
            };
            return;
        }

        WriteSessionDataAtStart();

        var path = GetAssiLogPath();
        var logDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
        {
            try { Directory.CreateDirectory(logDir); }
            catch (Exception ex) { AssiLog("step=recorder_start logDir_create ex=" + (ex.Message ?? "")); }
        }
        var startLine = "[VideoPipeline] step=start sessionId=" + _sessionId + " t=" + EditorApplication.timeSinceStartup.ToString("F1");
        File.WriteAllText(path, startLine + Environment.NewLine);
        _sessionRunLogPath = GetSessionRunLogPath(_sessionId);
        try
        {
            if (!string.IsNullOrEmpty(_sessionRunLogPath))
                File.WriteAllText(_sessionRunLogPath, startLine + Environment.NewLine);
        }
        catch { }

        _timerStart = EditorApplication.timeSinceStartup;
        AssiLog("selftest_auto_stop_disabled=true");
        AssiLog("auto_stop_bypassed=true");
        AssiLog("step=recorder_start sessionId=" + _sessionId + " tempPath=" + _tempMp4Path);
        _state = State.Recording;
        _lastStep = "recorder_start";

        WriteSessionActive();
        VideoPipelineSelfTestMode.SetActive(true);
        SubscribeLogHandler();
        VideoPipelineSelfTestOverlay.ShouldShow = true;
        VideoPipelineSelfTestOverlay.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        ScheduleWatchdogChain();
        EditorApplication.EnterPlaymode();
    }

    const string StopTriggeredMarker = "stop_triggered.txt";
    const string SessionMarkerFile = ".vp_session.txt";

    static string GetSessionMarkerPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "SnowPanicVideos");
        try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, SessionMarkerFile);
    }

    static void WriteSessionActive()
    {
        try
        {
            var path = GetSessionMarkerPath();
            var outDir = !string.IsNullOrEmpty(_outputPathBase) ? Path.GetDirectoryName(_outputPathBase) : ResolveOutputDir();
            if (string.IsNullOrEmpty(outDir)) outDir = ResolveOutputDir();
            var content = (_sessionId ?? "") + "\n" + (_tempMp4Path ?? EnsureTempMp4Path() ?? "") + "\n" + (outDir ?? "");
            File.WriteAllText(path, content);
        }
        catch (Exception ex) { AssiLog("WriteSessionActive failed ex=" + (ex.Message ?? "")); }
    }

    static void DeleteSessionActive()
    {
        try
        {
            var path = GetSessionMarkerPath();
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    static bool TryReadSessionActive(out string sessionId, out string tempPath, out string outputDir)
    {
        sessionId = null;
        tempPath = null;
        outputDir = null;
        try
        {
            var path = GetSessionMarkerPath();
            if (!File.Exists(path)) return false;
            var lines = File.ReadAllLines(path);
            if (lines != null && lines.Length >= 2)
            {
                sessionId = lines[0].Trim();
                tempPath = lines[1].Trim();
                if (lines.Length >= 3) outputDir = lines[2].Trim();
                return !string.IsNullOrEmpty(sessionId);
            }
        }
        catch { }
        return false;
    }

    static bool TryReadSessionActive(out string sessionId, out string tempPath)
    {
        string outDir;
        return TryReadSessionActive(out sessionId, out tempPath, out outDir);
    }

    /// <summary>outputDir を必ず解決。失敗時は null。書き込み不可の場合は ~/SnowPanicVideos へフォールバック。</summary>
    static string ResolveOutputDir()
    {
        var dataPath = Application.dataPath;
        var outDir = "";
        if (!string.IsNullOrEmpty(dataPath))
            outDir = Path.GetFullPath(Path.Combine(dataPath, "..", "Recordings"));
        if (string.IsNullOrEmpty(outDir))
        {
            var curDir = Environment.CurrentDirectory;
            if (!string.IsNullOrEmpty(curDir))
                outDir = Path.GetFullPath(Path.Combine(curDir, "Recordings"));
        }
        if (string.IsNullOrEmpty(outDir))
        {
            AssiLog("OUTPUTDIR_RESOLVE_FAIL dataPath=" + (dataPath ?? "(null)") + " currentDir=" + (Environment.CurrentDirectory ?? "(null)"));
            return null;
        }
        return outDir;
    }

    /// <summary>outputDir を確保し書き込みテスト。失敗時は ~/SnowPanicVideos へフォールバック。戻り値は使用可能な outputDir、不可なら null。</summary>
    static string ResolveAndEnsureWritableOutputDir()
    {
        var primary = ResolveOutputDir();
        if (string.IsNullOrEmpty(primary)) return null;
        try { Directory.CreateDirectory(primary); } catch { }
        var writable = TestOutputDirWritable(primary);
        AssiLog("outputDirWritableTest=" + writable.ToString());
        UnityEngine.Debug.Log("[VideoPipeline] outputDirWritableTest=" + writable);
        if (writable) return primary;
        var fallback = Path.Combine(GetSnowPanicVideosDir(), "Recordings");
        try { Directory.CreateDirectory(fallback); } catch { }
        writable = TestOutputDirWritable(fallback);
        AssiLog("[VideoPipeline] outputDirFallback path=" + fallback + " writableTest=" + writable.ToString());
        if (writable) return fallback;
        AssiLog("OUTPUTDIR_ALL_WRITABLE_FAIL primary=" + primary + " fallback=" + fallback);
        return null;
    }

    static bool TestOutputDirWritable(string outputDir)
    {
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir)) return false;
        try
        {
            var testPath = Path.Combine(outputDir, "write_test.tmp");
            File.WriteAllText(testPath, "test");
            File.Delete(testPath);
            return true;
        }
        catch { return false; }
    }

    /// <summary>通常Play押下時に呼ばれる。自動録画フック。outputDir確保→録画開始。</summary>
    static void StartAutoRecordSession()
    {
        var outputDir = ResolveAndEnsureWritableOutputDir();
        if (string.IsNullOrEmpty(outputDir))
        {
            AssiLog("step=auto_record_skip reason=outputDir_resolve_or_writable_failed");
            UnityEngine.Debug.LogWarning("[VideoPipeline] AutoRecord on Play skipped: outputDir failed.");
            return;
        }
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionId = "vp_play_" + timestamp;
        _startedAt = DateTime.Now;
        _endedAt = null;
        _result = "";
        _errorStep = "none";
        _localPath = "";
        _localSizeBytes = 0;
        _outputPathBase = Path.Combine(outputDir, "snow_test_tmp_" + _sessionId);
        _tempMp4Path = Path.GetFullPath(Path.Combine(outputDir, "snow_test_tmp_" + _sessionId + ".mp4"));
        _mp4Path = Path.GetFullPath(Path.Combine(outputDir, "snow_test_latest.mp4"));
        _latestMp4Path = "";
        _dailyArchivePath = "";
        _dailyArchiveCreated = false;
        _previewPath = "";
        _previewCreated = false;
        _previewGifSize = 0;
        _previewType = "none";
        _previewSizeBytes = 0;
        _previewFallbackUsed = false;
        _gifPath = "";
        _ffmpegPathUsed = "";
        _ffmpegAvailable = false;
        _previewStatus = "PREVIEW_ERROR";
        _previewGifDriveLink = "";
        _uploadCopySuccess = false;
        _uploadVerifyWarning = "";
        _recordingStartedAt = DateTime.MinValue;
        _recorderStartOk = false;
        _recorderStopRequested = false;
        _exitRoutineRan = false;
        _postStopPollEntered = false;
        _finalizeEntered = false;
        _finalizeReason = "";
        _lastStep = "auto_record_start";
        WriteLastRunTimestamp();
        WriteSessionDataAtStart();
        var path = GetAssiLogPath();
        var logDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
        var startLine = "[VideoPipeline] step=auto_record sessionId=" + _sessionId;
        try { File.WriteAllText(path, startLine + Environment.NewLine); } catch { }
        _sessionRunLogPath = GetSessionRunLogPath(_sessionId);
        _timerStart = EditorApplication.timeSinceStartup;
        _state = State.Recording;
        _executionMode = "play";
        _startHookCalled = false;
        _stopHookCalled = false;
        _previewStartCalled = false;
        _previewDoneCalled = false;
        _previewErrorReason = "";
        _uploadStarted = false;
        _uploadFinished = false;
        _earlyReportEmitted = false;
        _reportCopyReadyTimeMs = 0;
        _finalResultEmitted = false;
        _previewStartedAsync = false;
        _uploadStartedAsync = false;
        _slackStartedAsync = false;
        _backgroundWorkComplete = false;
        _backgroundFallbackToMainThread = false;
        WriteSessionActive();
        VideoPipelineSelfTestMode.SetActive(true);
        VideoPipelineSelfTestMode.ManualStopOnly = true;
        SubscribeLogHandler();
        VideoPipelineSelfTestOverlay.ShouldShow = true;
        VideoPipelineSelfTestOverlay.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        ScheduleWatchdogChain();
        AssiLog("execution_mode=" + _executionMode + " (official_route=play)");
        AssiLog("step=auto_record sessionId=" + _sessionId + " outputDir=" + outputDir + " expectedPath=" + _mp4Path);
        UnityEngine.Debug.Log("[VideoPipeline] ★ Play正式運用 ★ Stopでmp4→gif→upload→レポート");
        StartRecordingThisSession();
    }

    /// <summary>黒画面ブロック解除。Auto-record OFF + マーカー削除。次回 Play で通常画面に戻る。</summary>
    [MenuItem("SnowPanic/VideoPipeline/Force Normal Play (fix black screen)", false, 148)]
    static void ForceNormalPlay()
    {
        AutoRecordOnPlay = false;
        VideoPipelineSelfTestMode.SetActive(false);
        UnityEngine.Debug.Log("[VideoPipeline] Force Normal Play: Auto-record OFF, marker cleared. Play again.");
    }

    [MenuItem("SnowPanic/VideoPipeline/Auto-record on Play", false, 149)]
    static void ToggleAutoRecordOnPlay()
    {
        AutoRecordOnPlay = !AutoRecordOnPlay;
        UnityEngine.Debug.Log("[VideoPipeline] Auto-record on Play: " + (AutoRecordOnPlay ? "ON" : "OFF"));
    }

    [MenuItem("SnowPanic/VideoPipeline/Auto-record on Play", true)]
    static bool ValidateToggleAutoRecord()
    {
        UnityEditor.Menu.SetChecked("SnowPanic/VideoPipeline/Auto-record on Play", AutoRecordOnPlay);
        return true;
    }

    [MenuItem("SnowPanic/VideoPipeline/Ping (動作確認)", false, 150)]
    public static void Ping()
    {
        try
        {
            var path = Path.Combine(GetOutputDir(), "video_pipeline_ping.txt");
            File.WriteAllText(path, DateTime.Now.ToString("o") + " Ping OK" + Environment.NewLine);
            UnityEngine.Debug.Log("[VideoPipeline] Ping → " + path);
        }
        catch (Exception ex) { UnityEngine.Debug.LogError("[VideoPipeline] Ping failed: " + ex.Message); }
    }

    [MenuItem("SnowPanic/VideoPipeline/Stop2", false, 152)]
    [MenuItem("SnowPanic/VideoPipeline/Stop %#s", false, 151)]
    public static void RunManualStop()
    {
        WriteStopTriggeredMarker("menu_or_button");
        var activeController = _controller != null;
        var isRec = false;
        try { if (activeController) isRec = _controller.IsRecording(); } catch { }
        AssiLog("step=manual_stop_called sessionId=" + (_sessionId ?? "") + " activeController=" + activeController + " isRecording=" + isRec);
        UnityEngine.Debug.LogError("[VideoPipeline] ★ Stop押下検知 ★ sessionId=" + (_sessionId ?? "") + " activeController=" + activeController + " isRecording=" + isRec);
        if (_state != State.Recording)
        {
            string sid, tmp;
            if (TryReadSessionActive(out sid, out tmp))
            {
                _sessionId = sid;
                _tempMp4Path = tmp;
                _state = State.Recording;
                AssiLog("step=manual_stop_recovered_from_session_marker");
            }
            else
            {
                AssiLog("step=manual_stop_rejected state=" + _state + " no_session_marker");
                return;
            }
        }
        AssiLog("step=manual_stop_called_by_user");
        AssiLog("step=manual_stop_begin");
        _manualStopCalledByUser = true;
        DoStopAndFinalize();
    }

    /// <summary>StopRecording を呼び、bytes>0 になるまで PostStopPolling で待機。manual_stop/ExitingPlayMode の共通処理。</summary>
    static void DoStopAndFinalize()
    {
        _recorderStopRequested = true;
        _recorderStopRequestedAt = DateTime.Now;
        AssiLog("step=recorder_stop_called");
        AssiLog("step=manual_stop_recording_stop_called");
        try
        {
            if (_controller != null && _controller.IsRecording())
            {
                _controller.StopRecording();
                _stopHookCalled = true;
            }
            _lastStep = "recorder_stop";
        }
        catch (Exception ex)
        {
            _result = "FAIL";
            _errorStep = "file_write_or_finalize";
            AssiLog("step=recorder_stop_exception ex=" + (ex.Message ?? ""));
            AssiLog("step=recorder_stop_exception stacktrace=" + (ex.StackTrace ?? "").Replace("\r", " ").Replace("\n", " | "));
            LogTmpAndLatestStatus();
            EditorApplication.ExitPlaymode();
            return;
        }
        EnterPostStopPolling("DoStopAndFinalize");
    }

    static void EnterPostStopPolling(string reason)
    {
        _state = State.PostStopPolling;
        _flushWaiting = true;
        _flushWaitStart = DateTime.Now;
        _postStopPollStart = DateTime.Now;
        _postStopPollEntered = true;
        _postStopPollCount = 0;
        _lastPostStopPollBytes = -1;
        var tempPath = EnsureTempMp4Path();
        AssiLog("step=post_stop_poll_begin reason=" + reason + " tempPath=" + (tempPath ?? "") + " session_elapsed=" + GetSessionElapsedSeconds().ToString("F1") + "s maxWait=" + PostStopPollMaxSec + "s interval=" + PostStopPollIntervalSec + "s");
        LogTmpAndLatestStatus();
    }

    static void WriteStopTriggeredMarker(string source)
    {
        try
        {
            var outDir = GetOutputDir();
            if (string.IsNullOrEmpty(outDir))
                outDir = Path.GetDirectoryName(GetSessionMarkerPath()) ?? Path.GetTempPath();
            var path = Path.Combine(outDir, StopTriggeredMarker);
            File.WriteAllText(path, DateTime.Now.ToString("o") + " source=" + source + " state=" + _state + " controller=" + (_controller != null) + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>temp mp4 が存在し size>0 なら temp→latest へ rename。成功時 true。post_stop_poll 未実行時の fallback。</summary>
    static bool TryFinalizeFromTemp()
    {
        var tempPath = EnsureTempMp4Path();
        if (string.IsNullOrEmpty(tempPath) || !File.Exists(tempPath)) return false;
        long bytes;
        try { bytes = new FileInfo(tempPath).Length; } catch { return false; }
        if (bytes <= 0) return false;
        _postStopPollEntered = true;
        _finalizeEntered = true;
        _finalizeReason = "temp_bytes_gt_0_editmode_fallback";
        AssiLog("step=post_stop_poll_ready (TryFinalizeFromTemp) size=" + bytes);
        AssiLog("step=finalize_start (TryFinalizeFromTemp)");
        var outDir = GetOutputDir();
        var latestPath = Path.Combine(outDir ?? "", "snow_test_latest.mp4");
        try
        {
            if (File.Exists(latestPath)) File.Delete(latestPath);
            File.Move(tempPath, latestPath);
            var fi = new FileInfo(latestPath);
            var verifyBytes = fi.Length;
            AssiLog("latest_status path=" + latestPath + " exists=" + fi.Exists + " bytes=" + verifyBytes);
            AssiLog("step=file_written path=" + latestPath + " bytes=" + verifyBytes);
            AssiLog("step=finalize_done (TryFinalizeFromTemp)");
            AssiLog("[VideoPipeline] movie_created=True");
            AssiLog("[VideoPipeline] movie_path=" + latestPath);
            _mp4Path = Path.GetFullPath(latestPath);
            _latestMp4Path = _mp4Path;
            _localPath = latestPath;
            _localSizeBytes = verifyBytes;
            _localMp4Exists = true;
            _lastStep = "local_file";
            return true;
        }
        catch (Exception ex)
        {
            AssiLog("step=file_rename_failed (TryFinalizeFromTemp) ex=" + (ex.Message ?? ""));
            return false;
        }
    }

    static void LogTmpAndLatestStatus()
    {
        var outDir = !string.IsNullOrEmpty(_tempMp4Path) ? Path.GetDirectoryName(_tempMp4Path) : GetOutputDir();
        if (string.IsNullOrEmpty(outDir)) outDir = GetOutputDir();
        var latestPath = Path.Combine(outDir ?? "", "snow_test_latest.mp4");
        long tmpBytes = -1;
        var tmpExists = false;
        if (!string.IsNullOrEmpty(_tempMp4Path))
        {
            tmpExists = File.Exists(_tempMp4Path);
            if (tmpExists) try { tmpBytes = new FileInfo(_tempMp4Path).Length; } catch { }
            AssiLog("tmp_status path=" + _tempMp4Path + " exists=" + tmpExists + " bytes=" + tmpBytes);
        }
        long latestBytes = -1;
        var latestExists = File.Exists(latestPath);
        if (latestExists) try { latestBytes = new FileInfo(latestPath).Length; } catch { }
        AssiLog("latest_status path=" + latestPath + " exists=" + latestExists + " bytes=" + latestBytes);
    }

    const float MinRecordSeconds = 10f;
    const float AbsoluteTimeoutSeconds = 50f;

    static void ScheduleWatchdogChain()
    {
        EditorApplication.delayCall += () =>
        {
            if (_state == State.Idle || _state == State.Done) return;
            if (VideoPipelineSelfTestMode.ManualStopOnly)
            {
                ScheduleWatchdogChain();
                return;
            }
            var elapsed = (float)GetSessionElapsedSeconds();
            if (elapsed >= AbsoluteTimeoutSeconds)
            {
                _state = State.Done;
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
                RunExitRoutine(timedOut: true);
                return;
            }
            if (_state == State.Recording && !_recorderStopRequested && elapsed >= MinRecordSeconds)
            {
                AssiLog("step=watchdog_min_record_reached elapsed=" + elapsed.ToString("F1") + "s triggering stop");
                try
                {
                    _recorderStopRequested = true;
                    _recorderStopRequestedAt = DateTime.Now;
                    if (_controller != null && _controller.IsRecording())
                    {
                        _controller.StopRecording();
                        _stopHookCalled = true;
                        _lastStep = "recorder_stop";
                    }
                    EnterPostStopPolling("watchdog_min_record");
                }
                catch (Exception ex) { AssiLog("step=watchdog_stop_exception ex=" + (ex.Message ?? "")); }
            }
            ScheduleWatchdogChain();
        };
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        // SnowVisibilityLab では録画・Drive・Slack を完全停止（RCV2: 解除）
        // if (UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name == "SnowVisibilityLab") return;

        // 通常Play: ExitingEditMode(Play開始直前)でマーカー削除。古いsession混入防止のため_sessionIdもクリア。
        if (state == PlayModeStateChange.ExitingEditMode && !AutoRecordOnPlay)
        {
            VideoPipelineSelfTestMode.SetActive(false);
            if (_state != State.Recording)
            {
                DeleteSessionActive();
                _sessionId = null;
                _tempMp4Path = null;
                _outputPathBase = null;
            }
        }
        if (state == PlayModeStateChange.ExitingPlayMode)
        {
            SnowPackSpawner.EditorExitingPlayMode = true;
            var controllerBefore = _controller != null;
            var outputDirRaw = ResolveOutputDir();
            string markerSid, markerTmp, markerOutDir;
            var hasMarker = TryReadSessionActive(out markerSid, out markerTmp, out markerOutDir);
            AssiLog("step=exiting_playmode DIAG controller=" + controllerBefore + " outputDirRaw=" + (outputDirRaw ?? "(null)") + " dataPath=" + (Application.dataPath ?? "(null)") + " hasMarker=" + hasMarker + " markerOutDir=" + (markerOutDir ?? "(null)") + " markerTempPath=" + (markerTmp ?? "(null)") + " _outputPathBase=" + (_outputPathBase ?? "(null)") + " _tempMp4Path=" + (_tempMp4Path ?? "(null)"));
            UnityEngine.Debug.Log("[VideoPipeline] ExitingPlayMode DIAG controller=" + controllerBefore + " outputDirRaw=" + (outputDirRaw ?? "(null)") + " markerOutDir=" + (markerOutDir ?? "(null)") + " markerTempPath=" + (markerTmp ?? "(null)") + " _tempMp4Path=" + (_tempMp4Path ?? "(null)"));

            WriteStopTriggeredMarker("exiting_playmode");
            _recorderStopRequested = true;
            if (hasMarker || !string.IsNullOrEmpty(_sessionId))
                _stopHookCalled = true;
            if (hasMarker)
            {
                if (string.IsNullOrEmpty(_sessionId)) _sessionId = markerSid;
                if (string.IsNullOrEmpty(_tempMp4Path)) _tempMp4Path = markerTmp;
                if (string.IsNullOrEmpty(_outputPathBase) && !string.IsNullOrEmpty(markerOutDir) && !string.IsNullOrEmpty(markerSid))
                    _outputPathBase = Path.Combine(markerOutDir, "snow_test_tmp_" + markerSid);
                if (_state != State.Recording) _state = State.Recording;
            }
            AssiLog("step=exiting_playmode state=" + _state + " controller=" + (_controller != null) + " stopRequested=" + _recorderStopRequested + " sessionId=" + (_sessionId ?? "") + " tempPath=" + (_tempMp4Path ?? ""));
            UnityEngine.Debug.Log("[VideoPipeline] ExitingPlayMode state=" + _state + " controller=" + (_controller != null) + " stopRequested=" + _recorderStopRequested);
            try
            {
                if (_controller != null && _controller.IsRecording())
                {
                    AssiLog("step=recorder_stop_called (exiting_playmode fallback)");
                    _controller.StopRecording();
                    _stopHookCalled = true;
                    _lastStep = "recorder_stop";
                }
                else
                {
                    AssiLog("step=recorder_stop_skipped controller=" + (_controller != null ? "not_recording" : "null") + " (tempPath=" + (_tempMp4Path ?? "(null)") + " - will poll in PostRecord)");
                    UnityEngine.Debug.LogWarning("[VideoPipeline] controller=null at ExitingPlayMode - will poll for mp4 in PostRecord.");
                }
            }
            catch (Exception ex)
            {
                AssiLog("step=recorder_stop_exception ex=" + (ex.Message ?? ""));
            }
            // exiting_playmode fallback: 必ず finalize フローへ入れるため EnterPostStopPolling を呼ぶ
            if (hasMarker || !string.IsNullOrEmpty(_sessionId))
            {
                EnterPostStopPolling("exiting_playmode");
            }
            else
            {
                // 録画未開始でStopした場合もセッションを書き込み、古いログを上書き。レポートを開く。
                AssiLog("step=exiting_playmode no_session_write_fresh");
                WriteSessionData();
                if (SnowLoopNoaReportAutoCopy.TryBuildReportOrSelfTestReport())
                {
                    AssiReportWindow.OpenAndShowReport();
                    var report = SnowLoopNoaReportAutoCopy.GetReportContent();
                    if (!string.IsNullOrEmpty(report))
                        EditorGUIUtility.systemCopyBuffer = report;
                }
            }
            LogTmpAndLatestStatus();
            return;
        }
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            SnowPackSpawner.EditorExitingPlayMode = false;
            string markerSid, markerTmp, markerOutDir;
            var hasMarker = TryReadSessionActive(out markerSid, out markerTmp, out markerOutDir);
            if (_state != State.Recording && hasMarker)
            {
                AssiLog("step=entered_playmode recovered_from_session_marker (domain_reload suspected) state_was=" + _state);
                _sessionId = markerSid;
                _tempMp4Path = markerTmp;
                if (!string.IsNullOrEmpty(markerOutDir))
                    _outputPathBase = Path.Combine(markerOutDir, "snow_test_tmp_" + markerSid);
                _state = State.Recording;
                _sessionRunLogPath = GetSessionRunLogPath(markerSid);
            }
            if (_state == State.Idle && AutoRecordOnPlay)
            {
                StartAutoRecordSession();
                return;
            }
            // 通常Play（AutoRecordOff）: 残留マーカーを必ずクリア。SELFTEST黒画面防止。
            if (_state == State.Idle && !AutoRecordOnPlay)
            {
                VideoPipelineSelfTestMode.SetActive(false);
            }
            if (_state != State.Recording) return;
            StartRecordingThisSession();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            SnowPackSpawner.EditorExitingPlayMode = false;
            CleanupController();
            string restSessionId, restTempPath;
            if (TryReadSessionActive(out restSessionId, out restTempPath))
            {
                if (string.IsNullOrEmpty(_sessionId)) _sessionId = restSessionId;
                if (string.IsNullOrEmpty(_tempMp4Path)) _tempMp4Path = restTempPath;
                if (string.IsNullOrEmpty(_mp4Path)) _mp4Path = Path.Combine(GetOutputDir(), "snow_test_latest.mp4");
                if (_state != State.Recording && _state != State.WaitingEdit && _state != State.PostStopPolling) _state = State.WaitingEdit;
            }
            if (_state == State.PostStopPolling)
            {
                AssiLog("step=entered_editmode keeping PostStopPolling for edit_mode poll");
            }
            else if (_state == State.Recording || _state == State.WaitingEdit)
            {
                _state = State.WaitingEdit;
                EditorApplication.delayCall += () =>
                {
                    if (_state == State.WaitingEdit)
                    {
                        // temp 存在＆size>0 なら post_stop_poll 相当の finalize を即実行（exiting_playmode fallback）
                        if (TryFinalizeFromTemp())
                        {
                            AssiLog("step=finalize_from_temp_done (EnteredEditMode fallback)");
                            _postPhase = PostPhase.WaitMp4;
                            _mp4CandidatePath = _mp4Path;
                            _mp4CandidateSize = _localSizeBytes;
                            _mp4CandidateFirstSeenUtc = DateTime.UtcNow.AddSeconds(-(Mp4SizeStableDelaySec + 1));
                        }
                        else
                        {
                            _postPhase = PostPhase.WaitMp4;
                        }
                        _state = State.PostRecord;
                        _postPhaseStart = DateTime.Now;
                        _mp4CandidatePath = null;
                        _mp4CandidateSize = -1;
                        _mp4CandidateFirstSeenUtc = default;
                        if (_postPhase == PostPhase.WaitMp4)
                            AssiLog("step=mp4_wait_begin expectedPath=" + _mp4Path + " tempPath=" + (_tempMp4Path ?? "") + " poll_interval=" + Mp4PollIntervalSec + "s max_wait=" + Mp4WaitMaxSec + "s condition=FileExists_and_size_gt_0");
                    }
                };
            }
        }
    }

    static void OnUpdate()
    {
        if (_backgroundFallbackToMainThread)
        {
            _backgroundFallbackToMainThread = false;
            try
            {
                RunUploadPhaseFromLatest();
            }
            finally
            {
                _backgroundTasksPending = false;
                _backgroundWorkComplete = true;
            }
        }
        if (_backgroundWorkComplete)
        {
            _backgroundWorkComplete = false;
            _finalResultEmitted = true;
            WriteSessionData();
            AssiLog("step=background_complete preview_status=" + (_previewStatus ?? "?") + " upload=" + (_driveFileStatus ?? "?"));
            AssiLog("preview_done_called=" + _previewDoneCalled.ToString().ToLower());
            AssiLog("upload_finished=" + _uploadFinished.ToString().ToLower());
            AssiLog("final_result_emitted=true");
            if (SnowLoopNoaReportAutoCopy.TryBuildReportOrSelfTestReport())
                AssiReportWindow.RefreshIfOpen();
        }

        var elapsed = (float)GetSessionElapsedSeconds();
        if (VideoPipelineSelfTestMode.ManualStopOnly)
        {
            if (_state == State.Recording && !_recorderStopRequested)
                return;
        }
        else
        {
            var watchdogLimit = AbsoluteTimeoutSeconds;
            if (_state == State.Recording && !_recorderStopRequested && elapsed >= MinRecordSeconds)
            {
                AssiLog("step=onupdate_min_record_reached session_elapsed=" + elapsed.ToString("F1") + "s triggering stop");
                _recorderStopRequested = true;
                _recorderStopRequestedAt = DateTime.Now;
                try
                {
                    if (_controller != null && _controller.IsRecording())
                    {
                        _controller.StopRecording();
                        _stopHookCalled = true;
                        _lastStep = "recorder_stop";
                    }
                    EnterPostStopPolling("onupdate_min_record");
                }
                catch (Exception ex) { AssiLog("step=onupdate_stop_exception ex=" + (ex.Message ?? "")); }
                return;
            }
            if (elapsed >= watchdogLimit && _state != State.Idle && _state != State.Done)
            {
                _state = State.Done;
                if (EditorApplication.isPlaying)
                    EditorApplication.ExitPlaymode();
                RunExitRoutine(timedOut: true);
                return;
            }
        }

        if (!EditorApplication.isPlaying && _state == State.Recording && elapsed >= RecorderStartTimeoutSec)
        {
            _result = "ERROR";
            _errorStep = "mp4_not_created";
            _lastStep = "play_never_entered";
            _localMp4Exists = false;
            _driveFileStatus = "not_found";
            _slackMessageStatus = "not_posted";
            AssiLog("step=play_never_entered after=" + RecorderStartTimeoutSec + "s isPlaying=false (EnterPlaymode may have failed)");
            LogMp4FailureDetails();
            _state = State.Done;
            RunExitRoutine(timedOut: false);
            return;
        }

        if (EditorApplication.isPlaying)
        {
            if (_state == State.Recording && _recorderStopRequested && _controller != null && _controller.IsRecording())
            {
                try
                {
                    _controller.StopRecording();
                    _stopHookCalled = true;
                    AssiLog("step=recorder_stop from_update poll (controller existed)");
                }
                catch (Exception ex) { AssiLog("step=recorder_stop_exception from_update ex=" + (ex.Message ?? "")); }
            }
            if (_flushWaiting || _state == State.PostStopPolling)
            {
                var tempPath = EnsureTempMp4Path();
                var pollElapsed = (DateTime.Now - _postStopPollStart).TotalSeconds;
                var maxWait = _state == State.PostStopPolling ? PostStopPollMaxSec : FlushWaitMaxSec;
                long bytes = 0;
                var exists = !string.IsNullOrEmpty(tempPath) && File.Exists(tempPath);
                if (exists) try { bytes = new FileInfo(tempPath).Length; } catch { }
                var pollInterval = _state == State.PostStopPolling ? PostStopPollIntervalSec : 0.5;
                var pollCount = (int)(pollElapsed / pollInterval);
                if (pollCount != _postStopPollCount && _state == State.PostStopPolling)
                {
                    _postStopPollCount = pollCount;
                    AssiLog("step=post_stop_poll size=" + bytes + " exists=" + exists + " elapsed=" + pollElapsed.ToString("F1") + "s");
                    _lastPostStopPollBytes = bytes;
                }
                if (exists && bytes > 0)
                {
                    AssiLog("step=post_stop_poll_ready size=" + bytes);
                    _finalizeEntered = true;
                    _finalizeReason = "bytes_gt_0";
                    AssiLog("step=finalize_start");
                    AssiLog("tmp_status path=" + tempPath + " exists=true bytes=" + bytes);
                    var outDir = GetOutputDir();
                    var latestPath = Path.Combine(outDir ?? "", "snow_test_latest.mp4");
                    try
                    {
                        if (File.Exists(latestPath)) File.Delete(latestPath);
                        File.Move(tempPath, latestPath);
                        var fi = new FileInfo(latestPath);
                        var verifyBytes = fi.Length;
                        AssiLog("latest_status path=" + latestPath + " exists=" + fi.Exists + " bytes=" + verifyBytes);
                        AssiLog("step=file_written path=" + latestPath + " bytes=" + verifyBytes);
                        AssiLog("step=finalize_done");
                        AssiLog("file_exists_after_write=" + (verifyBytes > 0));
                        _mp4Path = Path.GetFullPath(latestPath);
                        _localMp4Exists = true;
                        _localPath = latestPath;
                        _localSizeBytes = verifyBytes;
                        AssiLog("[VideoPipeline] movie_created=True");
                        AssiLog("[VideoPipeline] movie_path=" + latestPath);
                    }
                    catch (Exception ex)
                    {
                        AssiLog("step=file_rename_failed ex=" + (ex.Message ?? ""));
                        AssiLog("file_exists_after_write=false");
                        _result = "FAIL";
                        _errorStep = "file_write_or_finalize";
                    }
                    _flushWaiting = false;
                    _state = State.Recording;
                    AssiLog("step=flush_wait_done exiting_playmode");
                    EditorApplication.ExitPlaymode();
                    return;
                }
                if (pollElapsed >= maxWait)
                {
                    LogTmpAndLatestStatus();
                    AssiLog("step=post_stop_poll_timeout after=" + maxWait + "s tempPath=" + (tempPath ?? "") + " exists=" + exists + " bytes=" + bytes);
                    AssiLog("file_exists_after_write=false");
                    _result = "FAIL";
                    _errorStep = "file_write_or_finalize";
                    _localMp4Exists = false;
                    _flushWaiting = false;
                    _state = State.Recording;
                    EditorApplication.ExitPlaymode();
                    return;
                }
                return;
            }
            if (!VideoPipelineSelfTestMode.ManualStopOnly && _state == State.Recording && VideoPipelineStopRequestor.RequestStop && !_recorderStopRequested)
            {
                AssiLog("step=timer_fire Runtime_requested_stop");
                _recorderStopRequested = true;
                _recorderStopRequestedAt = DateTime.Now;
                try
                {
                    var isRec = _controller != null && _controller.IsRecording();
                    AssiLog("step=recorder_stop controller=" + (_controller != null ? "exists" : "null") + " isRecording=" + isRec);
                    if (_controller != null && _controller.IsRecording()) { _controller.StopRecording(); _stopHookCalled = true; }
                    _lastStep = "recorder_stop";
                    EnterPostStopPolling("RequestStop");
                }
                catch (Exception ex)
                {
                    _result = "ERROR";
                    _errorStep = "recorder_stop_failed";
                    AssiLog("step=recorder_stop_exception ex=" + (ex.Message ?? ""));
                }
            }
            else if (_state == State.Recording && _controller != null && _controller.IsRecording() && _lastStep != "recording_running")
            {
                _lastStep = "recording_running";
                AssiLog("step=recording_running isRecording=true");
            }
            if (_state == State.Recording && elapsed >= RecorderStartTimeoutSec && _lastStep != "recording_running")
            {
                var ok = _controller != null && _controller.IsRecording();
                if (!ok)
                {
                    _result = "ERROR";
                    _errorStep = "mp4_not_created";
                    _lastStep = "recorder_start_timeout";
                    _localMp4Exists = false;
                    _driveFileStatus = "not_found";
                    _slackMessageStatus = "not_posted";
                    AssiLog("step=recorder_start_timeout after=" + RecorderStartTimeoutSec + "s controller=" + (_controller != null ? "exists" : "null") + " isRecording=" + (ok ? "true" : "false"));
                    LogMp4FailureDetails();
                    _state = State.Done;
                    RunExitRoutine(timedOut: false);
                    EditorApplication.ExitPlaymode();
                    return;
                }
            }
            else if (!VideoPipelineSelfTestMode.ManualStopOnly && _state == State.Recording && _controller != null && !_controller.IsRecording() && _lastStep == "recording_running")
            {
                if (!_recorderStopRequested)
                {
                    _recorderStopRequested = true;
                    _recorderStopRequestedAt = DateTime.Now;
                    _controller.StopRecording();
                    _stopHookCalled = true;
                    _lastStep = "recorder_stop";
                    AssiLog("step=recorder_stop reason=rec_done");
                    EnterPostStopPolling("rec_done");
                }
            }
            else if (!VideoPipelineSelfTestMode.ManualStopOnly && _state == State.Recording && _recorderStartOk && (DateTime.Now - _recordingStartedAt).TotalSeconds >= RecordDurationSeconds)
            {
                if (!_recorderStopRequested)
                {
                    _recorderStopRequested = true;
                    _recorderStopRequestedAt = DateTime.Now;
                    if (_controller != null && _controller.IsRecording()) { _controller.StopRecording(); _stopHookCalled = true; }
                    _lastStep = "recorder_stop";
                    AssiLog("step=recorder_stop reason=10s_timer");
                    EnterPostStopPolling("10s_timer");
                }
            }
            else if (!VideoPipelineSelfTestMode.ManualStopOnly && _state == State.Recording && elapsed >= RecordTimeoutSeconds + 3f && !_recorderStopRequested)
            {
                _recorderStopRequested = true;
                _recorderStopRequestedAt = DateTime.Now;
                if (_controller != null && _controller.IsRecording()) { _controller.StopRecording(); _stopHookCalled = true; }
                _lastStep = "recorder_stop";
                AssiLog("step=recorder_stop reason=fallback_13s");
                EnterPostStopPolling("fallback_13s");
            }
            return;
        }

        if (!EditorApplication.isPlaying && (_state == State.PostStopPolling || _flushWaiting))
        {
            var tempPath = EnsureTempMp4Path();
            var pollElapsed = (DateTime.Now - _postStopPollStart).TotalSeconds;
            var maxWait = _state == State.PostStopPolling ? PostStopPollMaxSec : FlushWaitMaxSec;
            long bytes = 0;
            var exists = !string.IsNullOrEmpty(tempPath) && File.Exists(tempPath);
            if (exists) try { bytes = new FileInfo(tempPath).Length; } catch { }
            var pollInterval = _state == State.PostStopPolling ? PostStopPollIntervalSec : 0.5;
            var pollCount = (int)(pollElapsed / pollInterval);
            if (pollCount != _postStopPollCount && _state == State.PostStopPolling)
            {
                _postStopPollCount = pollCount;
                AssiLog("step=post_stop_poll_edit_mode size=" + bytes + " exists=" + exists + " elapsed=" + pollElapsed.ToString("F1") + "s");
            }
            if (exists && bytes > 0)
            {
                AssiLog("step=post_stop_poll_ready_edit_mode size=" + bytes);
                _finalizeEntered = true;
                _finalizeReason = "bytes_gt_0_edit_mode";
                AssiLog("step=finalize_start (edit mode)");
                var outDir = GetOutputDir();
                var latestPath = Path.Combine(outDir ?? "", "snow_test_latest.mp4");
                try
                {
                    if (File.Exists(latestPath)) File.Delete(latestPath);
                    File.Move(tempPath, latestPath);
                    var fi = new FileInfo(latestPath);
                    var verifyBytes = fi.Length;
                    AssiLog("step=file_written path=" + latestPath + " bytes=" + verifyBytes);
                    _mp4Path = Path.GetFullPath(latestPath);
                    _localMp4Exists = true;
                    _localPath = latestPath;
                    _localSizeBytes = verifyBytes;
                    _latestMp4Path = _mp4Path;
                    AssiLog("[VideoPipeline] movie_created=True (edit mode finalize)");
                    _flushWaiting = false;
                    _state = State.PostRecord;
                    _postPhase = PostPhase.WaitMp4;
                    _postPhaseStart = DateTime.Now;
                    _mp4CandidatePath = _mp4Path;
                    _mp4CandidateSize = _localSizeBytes;
                    _mp4CandidateFirstSeenUtc = DateTime.UtcNow.AddSeconds(-(Mp4SizeStableDelaySec + 1));
                    AssiLog("step=edit_mode_finalize_done proceeding to background_tasks");
                }
                catch (Exception ex)
                {
                    AssiLog("step=file_rename_failed_edit_mode ex=" + (ex.Message ?? ""));
                }
                return;
            }
            if (pollElapsed >= maxWait)
            {
                AssiLog("step=post_stop_poll_timeout_edit_mode after=" + maxWait + "s tempPath=" + (tempPath ?? ""));
                _flushWaiting = false;
                _state = State.PostRecord;
                _postPhase = PostPhase.WaitMp4;
                _postPhaseStart = DateTime.Now;
                if (!_localMp4Exists)
                {
                    var fi = FindExpectedOrNewestMp4(Mp4MinSizeBytes);
                    if (fi != null)
                    {
                        _mp4Path = fi.FullName;
                        _localMp4Exists = true;
                        _localPath = fi.FullName;
                        _localSizeBytes = fi.Length;
                        _latestMp4Path = fi.FullName;
                        AssiLog("step=edit_mode_late_mp4_found path=" + fi.FullName);
                    }
                }
            }
            return;
        }

        if (_state != State.PostRecord || _postPhase == PostPhase.Complete) return;

        if (_postPhase == PostPhase.WaitMp4)
        {
            var waitElapsed = (DateTime.Now - _postPhaseStart).TotalSeconds;
            var pollCount = (int)(waitElapsed / Mp4PollIntervalSec);
            var fi = FindExpectedOrNewestMp4(Mp4MinSizeBytes);
            if (pollCount != _lastMp4PollCount)
            {
                _lastMp4PollCount = pollCount;
                var status = fi != null ? "found" : "not_found";
                var actualPath = fi != null ? fi.FullName : "(none)";
                AssiLog("step=file_poll expectedPath=" + _mp4Path + " actualPath=" + actualPath + " status=" + status + " poll_count=" + pollCount);
            }
            if (fi == null)
            {
                _mp4CandidatePath = null;
                _mp4CandidateSize = -1;
            }
            else
            {
                var useFile = false;
                if (string.IsNullOrEmpty(_mp4CandidatePath) || !string.Equals(_mp4CandidatePath, fi.FullName, StringComparison.Ordinal))
                {
                    _mp4CandidatePath = fi.FullName;
                    _mp4CandidateSize = fi.Length;
                    _mp4CandidateFirstSeenUtc = DateTime.UtcNow;
                }
                else
                {
                    if (fi.Length != _mp4CandidateSize)
                    {
                        _mp4CandidateSize = fi.Length;
                        _mp4CandidateFirstSeenUtc = DateTime.UtcNow;
                    }
                    var stableElapsed = (DateTime.UtcNow - _mp4CandidateFirstSeenUtc).TotalSeconds;
                    if (stableElapsed >= Mp4SizeStableDelaySec)
                    {
                        long currentSize = -1;
                        try { var refi = new FileInfo(fi.FullName); if (refi.Exists) currentSize = refi.Length; } catch { }
                        if (currentSize >= Mp4MinSizeBytes && currentSize == _mp4CandidateSize)
                            useFile = true;
                    }
                }
                if (!useFile)
                    fi = null;
            }
            var fromLateFound = false;
            if (fi == null && waitElapsed >= Mp4WaitMaxSec)
            {
                var finalFi = FindExpectedOrNewestMp4(Mp4MinSizeBytes);
                if (finalFi != null)
                {
                    AssiLog("step=file_check mp4_late_found at_timeout path=" + finalFi.FullName + " size=" + finalFi.Length);
                    fi = finalFi;
                    fromLateFound = true;
                }
            }
            if (fi != null)
            {
                _mp4Path = fi.FullName;
                _filename = fi.Name;
                LogTmpAndLatestStatus();
                AssiLog("step=file_check mp4_path=" + _mp4Path + " mp4_size_bytes=" + fi.Length + " lastWriteTime=" + fi.LastWriteTimeUtc.ToString("o") + " createdTime=" + fi.CreationTimeUtc.ToString("o") + (fromLateFound ? " source=late_found" : " size_stable=true"));
                if (fi.Length == 0)
                {
                    _result = "FAIL";
                    _errorStep = "file_write_or_finalize";
                    _localMp4Exists = true;
                    _driveFileStatus = "not_found";
                    _slackMessageStatus = "not_posted";
                    AssiLog("LOCAL_FILE path=" + _mp4Path + " bytes=0 (禁止)");
                    AssiLog("file_exists_after_write=false");
                    AssiError("rec", "finalize_or_file_write");
                    _state = State.Done;
                    RunExitRoutine(timedOut: false);
                    return;
                }
                _lastStep = "local_file";
                _localMp4Exists = true;
                AssiLog("[VideoPipeline] movie_created=True");
                AssiLog("[VideoPipeline] movie_path=" + fi.FullName);
                var resolvedPath = fi.FullName;
                var resolvedSize = fi.Length;
                AssiLog("step=resolved_mp4_path path=" + resolvedPath + " bytes=" + resolvedSize);
                AssiLog("step=file_written path=" + resolvedPath + " bytes=" + resolvedSize);
                AssiLog("file_exists_after_write=" + (resolvedSize > 0));
                AssiLog("step=mp4_detect path=" + _mp4Path + " size=" + fi.Length);
                var outDirMp4 = GetOutputDir();
                var latestPath = Path.Combine(outDirMp4, "snow_test_latest.mp4");
                try
                {
                    var srcFull = Path.GetFullPath(fi.FullName);
                    var dstFull = Path.GetFullPath(latestPath);
                    if (!string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(latestPath)) File.Delete(latestPath);
                        File.Move(fi.FullName, latestPath);
                    }
                    var afterFi = new FileInfo(latestPath);
                    AssiLog("step=file_written path=" + latestPath + " bytes=" + afterFi.Length);
                    AssiLog("file_exists_after_write=" + (afterFi.Length > 0));
                }
                catch (Exception ex) { AssiLog("step=rename_to_latest_failed ex=" + (ex.Message ?? "")); }
                _latestMp4Path = File.Exists(latestPath) ? Path.GetFullPath(latestPath) : _mp4Path;
                var uploadPath = !string.IsNullOrEmpty(_latestMp4Path) ? _latestMp4Path : _mp4Path;
                var uploadFi = new FileInfo(uploadPath);
                var uploadSize = uploadFi.Exists ? uploadFi.Length : 0;
                LogTmpAndLatestStatus();
                AssiLog("step=done local_mp4_path=" + uploadPath + " local_mp4_exists=true size=" + uploadSize);
                if (!uploadFi.Exists || uploadSize <= 0)
                {
                    AssiLog("step=upload_skip reason=latest_status_not_ready exists=" + uploadFi.Exists + " bytes=" + uploadSize);
                    _result = "FAIL";
                    _errorStep = "file_write_or_finalize";
                    _localMp4Exists = false;
                    _driveFileStatus = "not_found";
                    _slackMessageStatus = "not_posted";
                    _state = State.Done;
                    RunExitRoutine(timedOut: false);
                    return;
                }
                _localPath = uploadPath;
                _localSizeBytes = uploadSize;
                _result = "LOCAL_READY";
                _previewStatus = "PENDING";
                _driveFileStatus = "pending";
                _backgroundTasksPending = true;
                _backgroundTasksRequested = true;
                AssiLog("[VideoPipeline] background_tasks_started=true");
                AssiLog("background_tasks_started=true");
                _previewStartedAsync = true;
                _uploadStartedAsync = true;
                _slackStartedAsync = true;
                WriteSessionData();
                EmitEarlyReportAndCopy();
                _postPhase = PostPhase.Complete;
                _state = State.Done;
                RunExitRoutine(timedOut: false);
                Task.Run(() =>
                {
                    try
                    {
                        RunUploadPhaseFromLatest();
                    }
                    catch (Exception ex)
                    {
                        AssiLog("step=background_exception ex=" + (ex.Message ?? "").Replace("\r", " ").Replace("\n", " | ") + " -> fallback_to_main_thread");
                        _backgroundFallbackToMainThread = true;
                        return;
                    }
                    _backgroundTasksPending = false;
                    _backgroundWorkComplete = true;
                });
            }
            if (fi == null && waitElapsed >= Mp4WaitMaxSec)
            {
                AssiLog("step=file_check mp4_exists=false (timeout after " + Mp4WaitMaxSec + "s, no mp4 with size>0 in Recordings)");
                AssiLog("file_exists_after_write=false");
                _result = "FAIL";
                _errorStep = "file_write_or_finalize";
                _localMp4Exists = false;
                _driveFileStatus = "not_found";
                _slackMessageStatus = "not_posted";
                LogMp4FailureDetails();
                AssiError("rec", "file_not_created_or_empty");
                _state = State.Done;
                RunExitRoutine(timedOut: false);
            }
        }
    }

    static void LogMp4FailureDetails()
    {
        var outputDir = !string.IsNullOrEmpty(_outputPathBase) ? Path.GetDirectoryName(_outputPathBase) : null;
        if (string.IsNullOrEmpty(outputDir)) outputDir = ResolveOutputDir();
        if (string.IsNullOrEmpty(outputDir)) outputDir = GetOutputDir();
        AssiLog("MP4_FAILURE_DIAG detected_path=" + (_mp4Path ?? "(none)") + " detection=newest_mp4_in_Recordings");
        AssiLog("MP4_FAILURE_DIAG actual_output_dir=" + (outputDir ?? "(null)"));
        AssiLog("MP4_FAILURE_DIAG output_dir_Exists=" + (!string.IsNullOrEmpty(outputDir) && Directory.Exists(outputDir)));
        AssiLog("MP4_FAILURE_DIAG recorder_start_ok=" + _recorderStartOk);
        AssiLog("MP4_FAILURE_DIAG recorder_stop_requested=" + _recorderStopRequested);
        if (!string.IsNullOrEmpty(outputDir))
        {
            var exists = Directory.Exists(outputDir);
            AssiLog("MP4_FAILURE_DIAG output_dir=" + outputDir + " exists=" + exists);
            if (exists)
            {
                try
                {
                    var testPath = Path.Combine(outputDir, ".write_test_" + Guid.NewGuid().ToString("N"));
                    var writable = false;
                    try { File.WriteAllText(testPath, ""); writable = true; File.Delete(testPath); } catch { }
                    AssiLog("MP4_FAILURE_DIAG output_dir_writable=" + writable);
                }
                catch (Exception ex) { AssiLog("MP4_FAILURE_DIAG output_dir_check_error=" + ex.Message); }
            }
        }
        else AssiLog("MP4_FAILURE_DIAG output_dir=(null)");
        if (!string.IsNullOrEmpty(_lastRecorderException))
            AssiLog("MP4_FAILURE_DIAG last_exception=" + _lastRecorderException.Replace("\r", " ").Replace("\n", " | "));
    }

    const float RecorderOutputCheckDelaySec = 0.8f;
    const float TempPathCheck0Sec = 0.5f;
    const float TempPathCheck1Sec = 1f;

    static void ScheduleTempPathEarlyCheck()
    {
        EditorApplication.delayCall += () =>
        {
            if (_state != State.Recording || _controller == null) return;
            var elapsed = (DateTime.Now - _recordingStartedAt).TotalSeconds;
            if (elapsed < TempPathCheck0Sec)
            {
                ScheduleTempPathEarlyCheck();
                return;
            }
            var exists = !string.IsNullOrEmpty(_tempMp4Path) && File.Exists(_tempMp4Path);
            long bytes = -1;
            if (exists) try { bytes = new FileInfo(_tempMp4Path).Length; } catch { }
            UnityEngine.Debug.Log("[VideoPipeline] early_tmp_check exists=" + exists + " bytes=" + bytes);
            AssiLog("step=tempPath_check at=" + elapsed.ToString("F1") + "s path=" + (_tempMp4Path ?? "") + " exists=" + exists + " bytes=" + bytes);
            if (elapsed < TempPathCheck1Sec)
            {
                ScheduleTempPathEarlyCheck();
                return;
            }
            if (!exists)
            {
                AssiLog("step=recorder_not_producing_mp4 tempPath missing at 1s - early fail");
                _result = "FAIL";
                _errorStep = "recorder_not_writing";
                _lastStep = "recorder_not_producing";
                _localMp4Exists = false;
                _driveFileStatus = "not_found";
                _slackMessageStatus = "not_posted";
                _state = State.Done;
                if (_controller != null) try { _controller.StopRecording(); } catch { }
                RunExitRoutine(timedOut: false);
                EditorApplication.ExitPlaymode();
            }
        };
    }

    static void StartRecordingThisSession()
    {
    }

    /// <summary>EditorApplication.delayCall で10秒後に必ず Stop を呼ぶ。ManualStopOnly のときはスキップ。</summary>
    static void ScheduleForceStopTimer()
    {
        if (VideoPipelineSelfTestMode.ManualStopOnly) return;
        EditorApplication.delayCall += () =>
        {
            if (_state != State.Recording || _state == State.Done) return;
            if (_recorderStopRequested) return;
            var elapsed = (DateTime.Now - _recordingStartedAt).TotalSeconds;
            if (elapsed >= RecordDurationSeconds)
            {
                AssiLog("step=timer_fire elapsed=" + elapsed.ToString("F1") + "s forcing recorder_stop");
                try
                {
                    _recorderStopRequested = true;
                    _recorderStopRequestedAt = DateTime.Now;
                    var isRec = _controller != null && _controller.IsRecording();
                    AssiLog("step=recorder_stop controller=" + (_controller != null ? "exists" : "null") + " isRecording=" + isRec);
                    if (_controller != null && _controller.IsRecording()) { _controller.StopRecording(); _stopHookCalled = true; }
                    _lastStep = "recorder_stop";
                    EnterPostStopPolling("ScheduleForceStopTimer");
                }
                catch (Exception ex)
                {
                    _result = "ERROR";
                    _errorStep = "recorder_stop_failed";
                    AssiLog("step=recorder_stop_exception ex=" + (ex.Message ?? "").Replace("\r", " ").Replace("\n", " | "));
                    AssiLog("step=recorder_stop_exception stacktrace=" + (ex.StackTrace ?? "").Replace("\r", " ").Replace("\n", " | "));
                }
                return;
            }
            ScheduleForceStopTimer();
        };
    }

    static void ScheduleFlushThenExitPlaymode()
    {
        EditorApplication.delayCall += () =>
        {
            if (_state != State.Recording) return;
            var flushElapsed = (DateTime.Now - _recorderStopRequestedAt).TotalSeconds;
            if (flushElapsed < RecorderFlushDelaySec)
            {
                ScheduleFlushThenExitPlaymode();
                return;
            }
            AssiLog("step=recorder_flush_done exiting_playmode");
            EditorApplication.ExitPlaymode();
        };
    }

    static void ScheduleRecorderOutputCheck()
    {
        EditorApplication.delayCall += () =>
        {
            if (_state != State.Recording || _controller == null) return;
            var elapsed = (float)(EditorApplication.timeSinceStartup - _timerStart);
            if (elapsed < RecorderOutputCheckDelaySec) { ScheduleRecorderOutputCheck(); return; }
            var newest = FindNewestMp4InRecordings(1);
            var anyMp4 = newest != null && newest.Length > 0;
            if (!anyMp4)
            {
                var dir = GetOutputDir();
                var mp4Count = 0;
                try { if (Directory.Exists(dir)) mp4Count = Directory.GetFiles(dir, "*.mp4").Length; } catch { }
                AssiLog("step=recorder_start_failed after=" + RecorderOutputCheckDelaySec + "s anyMp4InRecordings=false mp4_count=" + mp4Count);
                _result = "ERROR";
                _errorStep = "recorder_start_failed";
                _lastStep = "recorder_start_failed";
                _localMp4Exists = false;
                _driveFileStatus = "not_found";
                _slackMessageStatus = "not_posted";
                LogMp4FailureDetails();
                _state = State.Done;
                if (_controller != null) try { _controller.StopRecording(); } catch { }
                RunExitRoutine(timedOut: false);
                EditorApplication.ExitPlaymode();
            }
        };
    }

    static void CleanupController()
    {
        if (_controller != null) { _controller = null; }
        if (_controllerSettings != null) { ScriptableObject.DestroyImmediate(_controllerSettings); _controllerSettings = null; }
        if (_movieSettings != null) { ScriptableObject.DestroyImmediate(_movieSettings); _movieSettings = null; }
    }
}
#endif
