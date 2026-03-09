using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SelfTest 録画時に「VIDEO PIPELINE SELFTEST」と timestamp を画面中央に黒背景で表示。
/// Editor が ShouldShow=true にして EnterPlaymode すると表示される。
/// </summary>
public static class VideoPipelineSelfTestOverlay
{
    public static bool ShouldShow;
    public static string Timestamp = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnLoad()
    {
        if (!ShouldShow) return;
        ShouldShow = false;

        var go = new GameObject("VideoPipelineSelfTestOverlay");
        Object.DontDestroyOnLoad(go);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32767;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        go.AddComponent<GraphicRaycaster>();

        var bg = new GameObject("Background");
        bg.transform.SetParent(go.transform, false);
        var bgRect = bg.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        var img = bg.AddComponent<Image>();
        img.color = Color.black;

        var textGo = new GameObject("Title");
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.6f);
        textRect.anchorMax = new Vector2(0.5f, 0.6f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.sizeDelta = new Vector2(1000, 100);
        textRect.anchoredPosition = Vector2.zero;
        var text = textGo.AddComponent<Text>();
        text.text = "VIDEO PIPELINE SELFTEST";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Font.CreateDynamicFontFromOSFont("Arial", 48);
        text.fontSize = 56;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        var tsGo = new GameObject("Timestamp");
        tsGo.transform.SetParent(go.transform, false);
        var tsRect = tsGo.AddComponent<RectTransform>();
        tsRect.anchorMin = new Vector2(0.5f, 0.4f);
        tsRect.anchorMax = new Vector2(0.5f, 0.4f);
        tsRect.pivot = new Vector2(0.5f, 0.5f);
        tsRect.sizeDelta = new Vector2(800, 60);
        tsRect.anchoredPosition = Vector2.zero;
        var tsText = tsGo.AddComponent<Text>();
        tsText.text = string.IsNullOrEmpty(Timestamp) ? System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : Timestamp;
        tsText.font = text.font;
        tsText.fontSize = 36;
        tsText.alignment = TextAnchor.MiddleCenter;
        tsText.color = Color.white;
    }
}
