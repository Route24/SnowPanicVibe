using UnityEngine;

/// <summary>
/// SnowCore_AntiProtocol 専用のイベントトレーサー。
/// YES/NO判定を行わない。起きた事実だけを時系列でログ出力する。
/// </summary>
public sealed class EventTraceLogger : MonoBehaviour
{
    [Tooltip("CyanSnowBox オブジェクト")]
    [SerializeField] private GameObject cyanSnowBox;

    private void Start()
    {
        Debug.Log("[EVENT] SceneStart");

        if (cyanSnowBox != null && cyanSnowBox.activeInHierarchy)
            Debug.Log("[EVENT] CyanSnowBoxFound");
    }

    private void OnDestroy()
    {
        Debug.Log("[EVENT] PlayEnd");
    }

    /// <summary>InputTapController から Raycast 命中時に呼ばれる。</summary>
    public void OnCyanBoxClicked()
    {
        Debug.Log("[EVENT] CyanSnowBoxClicked");
    }

    /// <summary>SnowBlockNode.OnHit() から Destroy 直前に呼ばれる。</summary>
    public void OnCyanBoxDestroyed()
    {
        Debug.Log("[EVENT] CyanSnowBoxDestroyed");
    }
}
