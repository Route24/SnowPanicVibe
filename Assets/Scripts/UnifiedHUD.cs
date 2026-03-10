using UnityEngine;
using UnityEngine.UI;

/// <summary>Phase1-1E: 単一の安定 HUD。Score + Ready/Cooldown。Canvas + UIText/TMP のみ。OnGUI 非使用。</summary>
public class UnifiedHUD : MonoBehaviour
{
    public static bool IsActive { get; private set; }

    const string RootName = "UnifiedHUD";
    const string ScoreTextName = "UnifiedHUD_ScoreText";
    const string StatusTextName = "UnifiedHUD_StatusText";

    Text _scoreText;
    Text _statusText;
    Component _scoreTMP;
    Component _statusTMP;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        if (Object.FindFirstObjectByType<UnifiedHUD>() != null) return;
        var go = new GameObject(RootName);
        go.AddComponent<UnifiedHUD>();
        DontDestroyOnLoad(go);
    }

    void Awake()
    {
        var existing = Object.FindFirstObjectByType<UnifiedHUD>(); if (existing != null && existing != this) { Destroy(gameObject); return; }
        CreateHUD();
        IsActive = true;
    }

    void CreateHUD()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 150;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();

        bool scoreTMP = TryCreateScoreText();
        if (!scoreTMP) CreateScoreTextLegacy();
        bool statusTMP = TryCreateStatusText();
        if (!statusTMP) CreateStatusTextLegacy();
        Debug.Log($"[UnifiedHUD] Created Score+Status (scoreTMP={scoreTMP} statusTMP={statusTMP})");
    }

    bool TryCreateScoreText()
    {
        var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        if (tmpType == null) return false;
        var go = new GameObject(ScoreTextName);
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent(tmpType) as Component;
        if (tmp == null) return false;
        tmp.GetType().GetProperty("text")?.SetValue(tmp, "SCORE: 0");
        tmp.GetType().GetProperty("fontSize")?.SetValue(tmp, 72);
        tmp.GetType().GetProperty("color")?.SetValue(tmp, new Color(255f/255f, 220f/255f, 0f, 1f));
        tmp.GetType().GetProperty("outlineWidth")?.SetValue(tmp, 0.2f);
        tmp.GetType().GetProperty("outlineColor")?.SetValue(tmp, Color.black);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f); rt.sizeDelta = new Vector2(400f, 90f);
        _scoreTMP = tmp;
        return true;
    }

    void CreateScoreTextLegacy()
    {
        var go = new GameObject(ScoreTextName);
        go.transform.SetParent(transform, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 72;
        t.color = new Color(255f/255f, 220f/255f, 0f, 1f);
        t.fontStyle = FontStyle.Bold;
        t.text = "SCORE: 0";
        var shadow = go.AddComponent<Shadow>(); shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(2f, 2f);
        var outline = go.AddComponent<Outline>(); outline.effectColor = Color.black; outline.effectDistance = new Vector2(2f, 2f);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f); rt.sizeDelta = new Vector2(400f, 90f);
        _scoreText = t;
    }

    bool TryCreateStatusText()
    {
        var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        if (tmpType == null) return false;
        var go = new GameObject(StatusTextName);
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent(tmpType) as Component;
        if (tmp == null) return false;
        tmp.GetType().GetProperty("text")?.SetValue(tmp, "Ready");
        tmp.GetType().GetProperty("fontSize")?.SetValue(tmp, 48);
        tmp.GetType().GetProperty("color")?.SetValue(tmp, new Color(255f/255f, 220f/255f, 0f, 1f));
        tmp.GetType().GetProperty("outlineWidth")?.SetValue(tmp, 0.2f);
        tmp.GetType().GetProperty("outlineColor")?.SetValue(tmp, Color.black);
        try { var alignProp = tmp.GetType().GetProperty("alignment"); if (alignProp != null) alignProp.SetValue(tmp, 514); } catch { } // TMP Center
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 120f); rt.sizeDelta = new Vector2(300f, 60f);
        _statusTMP = tmp;
        return true;
    }

    void CreateStatusTextLegacy()
    {
        var go = new GameObject(StatusTextName);
        go.transform.SetParent(transform, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 48;
        t.color = new Color(255f/255f, 220f/255f, 0f, 1f);
        t.fontStyle = FontStyle.Bold;
        t.alignment = TextAnchor.MiddleCenter;
        t.text = "Ready";
        var shadow = go.AddComponent<Shadow>(); shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(2f, 2f);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0f); rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, 120f); rt.sizeDelta = new Vector2(300f, 60f);
        _statusText = t;
    }

    void Start()
    {
        var mgr = SnowPhysicsScoreManager.Instance;
        if (mgr != null && (_scoreText != null || _scoreTMP != null))
            mgr.OnScoreChanged += OnScoreChanged;
    }

    void OnDisable()
    {
        if (SnowPhysicsScoreManager.Instance != null)
            SnowPhysicsScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
    }

    void OnDestroy() { if (IsActive) IsActive = false; }

    void OnScoreChanged(int score)
    {
        SetScoreText("SCORE: " + score);
    }

    void SetScoreText(string s)
    {
        if (_scoreText != null) _scoreText.text = s;
        else if (_scoreTMP != null) _scoreTMP.GetType().GetProperty("text")?.SetValue(_scoreTMP, s);
    }

    void Update()
    {
        if (_scoreText == null && _scoreTMP == null) return;
        var mgr = SnowPhysicsScoreManager.Instance;
        if (mgr != null) SetScoreText("SCORE: " + mgr.Score);

        var run = RunStructureManager.Instance;
        var cd = Object.FindFirstObjectByType<ToolCooldownManager>();
        string status = "";
        if (run != null)
        {
            if (run.State == RunStructureManager.RunState.Countdown)
                status = run.CountdownPhase == 0 ? "Ready" : "Start";
            else if (run.State == RunStructureManager.RunState.Running && cd != null && cd.CooldownRemaining > 0.01f)
                status = cd.CooldownRemaining.ToString("F1") + "s";
        }
        else if (cd != null && cd.CooldownRemaining > 0.01f)
            status = cd.CooldownRemaining.ToString("F1") + "s";

        if (_statusText != null) _statusText.text = status;
        else if (_statusTMP != null) _statusTMP.GetType().GetProperty("text")?.SetValue(_statusTMP, status);
    }

    public static void EnsureBootstrap() { Bootstrap(); }
}
