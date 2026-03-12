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
        Debug.Log("[SNOW_DEBUG_OVERLAY_LAYOUT] rect=top-right fontSize=28");
    }

    void OnGUI()
    {
        var rect = new Rect(Screen.width - 420, 20, 400, 140);

        var style = new GUIStyle(GUI.skin.box);
        style.fontSize = 28;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.UpperLeft;
        style.normal.textColor = Color.white;

        GUI.Box(rect, "SNOW DEBUG OVERLAY\nVISIBLE TEST", style);
    }
}