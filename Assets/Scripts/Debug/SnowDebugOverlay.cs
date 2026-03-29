using UnityEngine;

public class SnowDebugOverlay : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod]
    static void Boot()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "SnowCore_AntiProtocol") return;
        var go = new GameObject("SnowDebugOverlay");
        go.AddComponent<SnowDebugOverlay>();
    }

    void Awake()
    {
        Debug.Log("[SNOW_DEBUG_OVERLAY_BOOT] started");
        Debug.Log("[SNOW_DEBUG_OVERLAY_LAYOUT] rect=top-right fontSize=28");
    }

    void OnGUI()
    {
        // 企画書モック確認のため表示を無効化
    }
}