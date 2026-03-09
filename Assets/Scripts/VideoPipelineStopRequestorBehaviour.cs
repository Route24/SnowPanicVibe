using UnityEngine;

/// <summary>SelfTest 中、10 秒後に Stop を要求する。Play 中必ず動く MonoBehaviour。</summary>
[DefaultExecutionOrder(-32000)]
public class VideoPipelineStopRequestorBehaviour : MonoBehaviour
{
    const float StopAfterSeconds = 10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        if (Object.FindFirstObjectByType<VideoPipelineStopRequestorBehaviour>() != null) return;
        var go = new GameObject("VideoPipelineStopRequestor");
        go.hideFlags = HideFlags.HideAndDontSave;
        go.AddComponent<VideoPipelineStopRequestorBehaviour>();
    }

    void Update()
    {
        if (!VideoPipelineSelfTestMode.IsActive) return;
        if (VideoPipelineSelfTestMode.ManualStopOnly) return; // 10秒自動停止は無効。手動Stopのみ。
        if (VideoPipelineStopRequestor.RecordingStartedRealtime == 0f)
            VideoPipelineStopRequestor.RecordingStartedRealtime = Time.realtimeSinceStartup;
        if (VideoPipelineStopRequestor.RequestStop) return;
        var elapsed = Time.realtimeSinceStartup - VideoPipelineStopRequestor.RecordingStartedRealtime;
        if (elapsed >= StopAfterSeconds)
        {
            VideoPipelineStopRequestor.RequestStop = true;
        }
    }
}
