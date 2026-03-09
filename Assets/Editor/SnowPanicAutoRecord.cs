#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// 10秒動画を ~/SnowPanicVideos に自動連続出力。メニューから Start/Stop のみ。
/// Recorder API 使用。MP4 1280x720 30fps。
/// </summary>
[InitializeOnLoad]
public static class SnowPanicAutoRecord
{
    const int RecordDurationSeconds = 10;
    const int RecWidth = 1280;
    const int RecHeight = 720;
    const float FrameRate = 30f;

    static bool _running;
    static RecorderController _controller;
    static RecorderControllerSettings _controllerSettings;
    static MovieRecorderSettings _movieSettings;
    static string _currentOutputPath;

    static SnowPanicAutoRecord()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.update += OnUpdate;
    }

    static string GetOutputDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(home))
            home = Environment.GetEnvironmentVariable("HOME") ?? "";
        return Path.Combine(home, "SnowPanicVideos");
    }

    static string GetNextOutputPath()
    {
        var dir = GetOutputDirectory();
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);
        var name = $"snow_{DateTime.Now:yyyyMMdd_HHmmss}";
        return Path.Combine(dir, name);
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            if (!_running) return;
            StartRecordingThisSession();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            CleanupController();
            if (_running)
            {
                EditorApplication.delayCall += () =>
                {
                    if (_running)
                    {
                        Debug.Log("[AutoRecord] 次の録画を開始します...");
                        EditorApplication.EnterPlaymode();
                    }
                };
            }
        }
    }

    static void OnUpdate()
    {
        if (!EditorApplication.isPlaying) return;
        if (_controller == null) return;
        if (_controller.IsRecording()) return;
        _controller.StopRecording();
        Debug.Log($"[AutoRecord] 録画完了 出力: {_currentOutputPath}.mp4");
        EditorApplication.ExitPlaymode();
    }

    static void StartRecordingThisSession()
    {
        try
        {
            _currentOutputPath = GetNextOutputPath();
            var outputDir = Path.GetDirectoryName(_currentOutputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            _controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            _controllerSettings.SetRecordModeToTimeInterval(0f, RecordDurationSeconds);
            _controllerSettings.FrameRate = FrameRate;
            _controllerSettings.CapFrameRate = true;
            _controllerSettings.ExitPlayMode = false;

            _movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            _movieSettings.name = "SnowPanic AutoRecord";
            _movieSettings.Enabled = true;
            _movieSettings.OutputFile = _currentOutputPath;
            _movieSettings.CaptureAlpha = false;
            _movieSettings.CaptureAudio = false;
            _movieSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = RecWidth,
                OutputHeight = RecHeight
            };
            _movieSettings.EncoderSettings = new CoreEncoderSettings
            {
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High,
                Codec = CoreEncoderSettings.OutputCodec.MP4
            };

            _controllerSettings.AddRecorderSettings(_movieSettings);
            _controller = new RecorderController(_controllerSettings);
            _controller.PrepareRecording();
            _controller.StartRecording();

            Debug.Log($"[AutoRecord] 録画開始 出力先: {GetOutputDirectory()} ファイル: {Path.GetFileName(_currentOutputPath)}.mp4 (10秒)");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AutoRecord] 録画開始失敗: {ex.Message}\n{ex.StackTrace}");
            EditorApplication.ExitPlaymode();
        }
    }

    static void CleanupController()
    {
        if (_controller != null)
            _controller = null;
        if (_controllerSettings != null)
        {
            ScriptableObject.DestroyImmediate(_controllerSettings);
            _controllerSettings = null;
        }
        if (_movieSettings != null)
        {
            ScriptableObject.DestroyImmediate(_movieSettings);
            _movieSettings = null;
        }
    }

    [MenuItem("SnowPanic/AutoRecord/Start (10s loop)", false, 100)]
    public static void StartAutoRecord()
    {
        if (_running)
        {
            Debug.LogWarning("[AutoRecord] 既に稼働中です。");
            return;
        }
        _running = true;
        var outDir = GetOutputDirectory();
        Debug.Log($"[AutoRecord] 開始 出力先: {outDir}");
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("SnowPanic/AutoRecord/Stop", false, 101)]
    public static void StopAutoRecord()
    {
        _running = false;
        Debug.Log("[AutoRecord] 停止予約。現在の録画完了後に終了します。");
    }

    [MenuItem("SnowPanic/AutoRecord/Start (10s loop)", true)]
    [MenuItem("SnowPanic/AutoRecord/Stop", true)]
    static bool ValidateMenu()
    {
        return true;
    }
}
#endif
