using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// ASSI: Canvas + ScoreText を必ず作成。BeforeSceneLoad で最早実行し、失敗しない。
/// Hierarchy に UIRoot(Canvas) と ScoreText が存在することを保証する。
/// </summary>
public class UIBootstrap : MonoBehaviour
{
    static bool _booted;

    void Awake()
    {
        EnsureUIRootAndScoreText();
    }

    void Start()
    {
        EnsureUIRootAndScoreText();
        var root = GameObject.Find("Canvas") ?? GameObject.Find("UIRoot");
        var scoreText = root != null ? root.transform.Find("ScoreText") : null;
        Debug.Log($"[UIBootstrap Start] Canvas={(root != null ? "OK" : "MISSING")} ScoreText={(scoreText != null ? "OK" : "MISSING")}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BootstrapBeforeScene()
    {
        if (_booted) return;
        _booted = true;
        try
        {
            EnsureUIRootAndScoreText();
            SnowPhysicsScoreManager.EnsureBootstrapIfNeeded();
            var c = GameObject.Find("Canvas") ?? GameObject.Find("UIRoot");
            Debug.Log($"[UIBootstrap] BeforeSceneLoad complete Canvas={(c != null ? "OK" : "FAIL")}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[UIBootstrap] BeforeSceneLoad failed: {ex.Message}\n{ex.StackTrace}");
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapAfterScene()
    {
        EnsureUIRootAndScoreText();
        SnowPhysicsScoreManager.EnsureBootstrapIfNeeded();
    }

    public static void EnsureUIRootAndScoreText()
    {
        var root = EnsureCanvas();
        EnsureScoreText(root);
    }

    static GameObject EnsureCanvas()
    {
        var existing = GameObject.Find("Canvas");
        if (existing == null) existing = GameObject.Find("UIRoot");
        if (existing != null)
        {
            if (existing.GetComponent<Canvas>() != null)
            {
                Debug.Log($"[UIBootstrap] Canvas found ({existing.name})");
                return existing;
            }
        }

        var go = new GameObject("Canvas");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();

        UnityEngine.Object.DontDestroyOnLoad(go);
        Debug.Log("[UIBootstrap] Canvas created (Screen Space Overlay)");
        return go;
    }

    static void EnsureScoreText(GameObject canvasRoot)
    {
        if (canvasRoot == null) return;

        var existing = canvasRoot.transform.Find("ScoreText");
        if (existing != null)
        {
            var text = existing.GetComponent<Text>();
            if (text != null)
            {
                ApplyScoreTextLayout(text);
                text.text = "SCORE: 0";
                Debug.Log("[UIBootstrap] ScoreText found");
                return;
            }
        }

        var go = new GameObject("ScoreText");
        go.transform.SetParent(canvasRoot.transform, false);

        var tmpText = TryCreateTMP(go);
        if (tmpText != null)
        {
            ApplyScoreTextLayoutTMP(tmpText);
            tmpText.GetType().GetProperty("text")?.SetValue(tmpText, "SCORE: 0");
            WireScoreToTMP(tmpText);
            Debug.Log("[UIBootstrap] ScoreText created (TextMeshProUGUI)");
            return;
        }

        var legText = go.AddComponent<Text>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) legText.font = font;
        ApplyScoreTextLayout(legText);
        legText.text = "SCORE: 0";
        WireScoreToText(legText);
        Debug.Log("[UIBootstrap] ScoreText created (Legacy Text - TMP missing)");
    }

    static void ApplyScoreTextLayout(Text t)
    {
        if (t == null) return;
        t.fontSize = 36;
        t.color = new Color(1f, 1f, 1f, 1f);
        t.fontStyle = FontStyle.Bold;
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(10f, -10f);
        rt.sizeDelta = new Vector2(320f, 48f);
    }

    static void ApplyScoreTextLayoutTMP(Component tmp)
    {
        if (tmp == null) return;
        var rt = tmp.GetComponent<RectTransform>();
        if (rt != null)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(10f, -10f);
            rt.sizeDelta = new Vector2(320f, 48f);
        }
        try
        {
            tmp.GetType().GetProperty("fontSize")?.SetValue(tmp, 36);
            tmp.GetType().GetProperty("color")?.SetValue(tmp, new Color(1f, 1f, 1f, 1f));
        }
        catch { }
    }

    static Component TryCreateTMP(GameObject go)
    {
        var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        if (tmpType == null) return null;
        return go.AddComponent(tmpType) as Component;
    }

    static void WireScoreToText(Text t)
    {
        if (t == null) return;
        var comp = t.gameObject.AddComponent<UIScoreWire>();
        comp.InitLegacy(t);
    }

    static void WireScoreToTMP(Component tmp)
    {
        if (tmp == null) return;
        var comp = tmp.gameObject.AddComponent<UIScoreWire>();
        comp.InitTMP(tmp);
    }
}

/// <summary>ScoreManager の変化を ScoreText に反映。更新は変化時のみ。</summary>
public class UIScoreWire : MonoBehaviour
{
    Text _legacyText;
    Component _tmpText;
    int _lastShownScore = -1;

    public void InitLegacy(Text t)
    {
        _legacyText = t;
        _tmpText = null;
    }

    public void InitTMP(Component tmp)
    {
        _tmpText = tmp;
        _legacyText = null;
    }

    void Update()
    {
        var mgr = SnowPhysicsScoreManager.Instance;
        if (mgr == null) return;
        int s = mgr.Score;
        if (s == _lastShownScore) return;
        _lastShownScore = s;
        string str = "SCORE: " + s;
        if (_legacyText != null)
            _legacyText.text = str;
        else if (_tmpText != null)
            _tmpText.GetType().GetProperty("text")?.SetValue(_tmpText, str);
    }

    void OnEnable()
    {
        var mgr = SnowPhysicsScoreManager.Instance;
        if (mgr != null)
        {
            _lastShownScore = -1;
            mgr.OnScoreChanged += OnScoreChanged;
        }
    }

    void OnDisable()
    {
        if (SnowPhysicsScoreManager.Instance != null)
            SnowPhysicsScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
    }

    void OnScoreChanged(int score)
    {
        if (_lastShownScore != score)
        {
            _lastShownScore = score;
            string str = "SCORE: " + score;
            if (_legacyText != null)
                _legacyText.text = str;
            else if (_tmpText != null)
                _tmpText.GetType().GetProperty("text")?.SetValue(_tmpText, str);
        }
    }
}
