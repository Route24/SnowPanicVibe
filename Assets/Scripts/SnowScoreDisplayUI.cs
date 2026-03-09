using UnityEngine;
using UnityEngine.UI;

/// <summary>ASSI: 画面上に SCORE: N を表示（常時・可読性優先）</summary>
[RequireComponent(typeof(Canvas))]
public class SnowScoreDisplayUI : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureBootstrapOnLoad()
    {
        EnsureBootstrap();
    }

    public static void EnsureBootstrap()
    {
        if (FindFirstObjectByType<SnowScoreDisplayUI>() != null) return;
        var uiroot = GameObject.Find("Canvas") ?? GameObject.Find("UIRoot");
        if (uiroot != null && uiroot.transform.Find("ScoreText") != null)
            return;
        SnowPhysicsScoreManager.EnsureBootstrapIfNeeded();
        var go = new GameObject("SnowScoreDisplay");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        go.AddComponent<GraphicRaycaster>();
        go.AddComponent<SnowScoreDisplayUI>();
        UnityEngine.Object.DontDestroyOnLoad(go);
        Debug.Log("[SnowScoreDisplayUI] bootstrapped Canvas+ScoreText");
    }

    Text _text;
    Canvas _canvas;

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        _text = GetComponentInChildren<Text>(true);
        if (_text == null)
        {
            var go = new GameObject("ScoreText");
            go.transform.SetParent(transform, false);
            _text = go.AddComponent<Text>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null) _text.font = font;
            _text.fontSize = 36;
            _text.color = new Color(1f, 1f, 1f, 1f);
            _text.fontStyle = FontStyle.Bold;
            var rt = _text.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -10f);
            rt.sizeDelta = new Vector2(300f, 48f);
            Debug.Log("[SnowScoreDisplayUI] Canvas created, ScoreText auto-created (fontSize=36 pos=10,-10)");
        }
        else
        {
            _text.fontSize = Mathf.Max(_text.fontSize, 36);
            var rt = _text.rectTransform;
            rt.anchoredPosition = new Vector2(10f, -10f);
            Debug.Log("[SnowScoreDisplayUI] Canvas found, ScoreText found");
        }
        _text.text = "SCORE: 0";
    }

    void Start()
    {
        var mgr = SnowPhysicsScoreManager.Instance;
        if (mgr != null)
        {
            _text.text = $"SCORE: {mgr.Score}";
            mgr.OnScoreChanged += OnScoreChanged;
            Debug.Log($"[SnowScoreDisplayUI] Start: Score={mgr.Score} OnScoreChanged wired");
        }
        else
        {
            Debug.LogWarning("[SnowScoreDisplayUI] Start: SnowPhysicsScoreManager.Instance is null");
        }
    }

    void OnDestroy()
    {
        if (SnowPhysicsScoreManager.Instance != null)
            SnowPhysicsScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
    }

    void OnScoreChanged(int score)
    {
        if (_text != null)
            _text.text = $"SCORE: {score}";
    }

    void Update()
    {
        if (_text != null && SnowPhysicsScoreManager.Instance != null)
        {
            int s = SnowPhysicsScoreManager.Instance.Score;
            if (_text.text != $"SCORE: {s}")
                _text.text = $"SCORE: {s}";
        }
    }
}
