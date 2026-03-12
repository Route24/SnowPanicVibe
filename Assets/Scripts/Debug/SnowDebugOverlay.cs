using UnityEngine;

public class SnowDebugOverlay : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod]
    static void Boot()
    {
        var go = new GameObject("SnowDebugOverlay");
        go.AddComponent<SnowDebugOverlay>();
    }

    void Awake()
    {
        Debug.Log("[SNOW_DEBUG_OVERLAY_BOOT] started");
    }

    void OnGUI()
    {
        Debug.Log("[SNOW_DEBUG_OVERLAY_ONGUI] called");
        GUI.Box(new Rect(20, 20, 500, 140), "SNOW DEBUG OVERLAY\nVISIBLE TEST");
    }
}
