using UnityEngine;

/// <summary>SelfTest 時に Play 中 10 秒で Stop を要求。Editor の OnUpdate が検知して StopRecording 実行。</summary>
public static class VideoPipelineStopRequestor
{
    public static bool RequestStop;
    /// <summary>0=未開始。Runtime の Update で Time.realtimeSinceStartup に設定される。</summary>
    public static float RecordingStartedRealtime;
}
