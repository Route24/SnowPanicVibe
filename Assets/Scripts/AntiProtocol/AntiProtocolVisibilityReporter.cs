using UnityEngine;

/// <summary>
/// イベントログ専用 Reporter。状態推測・UNKNOWN出力なし。
/// このファイル単体で確実に取れる事実だけを記録する。
/// </summary>
public sealed class AntiProtocolVisibilityReporter : MonoBehaviour
{
    private void OnEnable()
    {
        Debug.Log("[EVENT] reporter_enabled");
    }

    private void Start()
    {
        Debug.Log("[EVENT] reporter_initialized");
    }

    private void OnDisable()
    {
        Debug.Log("[EVENT] reporter_disabled");
    }

    private void OnDestroy()
    {
        Debug.Log("[EVENT] reporter_destroyed");
    }

    /// <summary>外部から呼ばれた時だけ記録する。判定しない。</summary>
    public void NotifyCyanDestroyed()
    {
        Debug.Log("[EVENT] manual_log_called");
    }
}
