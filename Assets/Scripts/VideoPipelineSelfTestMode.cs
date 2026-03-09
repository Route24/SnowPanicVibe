using System;
using System.IO;

/// <summary>VideoPipeline SelfTest実行中か。Editorが ~/SnowPanicVideos/.vpselftest_active を配置して通知。</summary>
public static class VideoPipelineSelfTestMode
{
    static string MarkerPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "SnowPanicVideos", ".vpselftest_active");

    public static bool IsActive
    {
        get
        {
            try { return File.Exists(MarkerPath); }
            catch { return false; }
        }
    }

    /// <summary>true のとき 自動停止を完全無効化。手動Stopのみ。Self Test では true 固定。</summary>
    public static bool ManualStopOnly { get; set; } = true;

#if UNITY_EDITOR
    public static void SetActive(bool value)
    {
        try
        {
            var dir = Path.GetDirectoryName(MarkerPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (value)
                File.WriteAllText(MarkerPath, "1");
            else if (File.Exists(MarkerPath))
                File.Delete(MarkerPath);
        }
        catch { }
    }
#endif
}
