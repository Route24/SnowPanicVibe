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
        // BeforeSceneLoad ではシーン名が取れないため、HUD/Score 生成は AfterSceneLoad 側に委ねる
        // このメソッドでは何もしない
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void BootstrapAfterScene()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "SnowCore_AntiProtocol") return;
        UnifiedHUD.EnsureBootstrap();
        if (!UnifiedHUD.IsActive) EnsureUIRootAndScoreText();
        SnowPhysicsScoreManager.EnsureBootstrapIfNeeded();
    }

    public static void EnsureUIRootAndScoreText()
    {
        if (UnifiedHUD.IsActive) return; // Phase1-1F: canonical HUD only - no legacy Canvas/ScoreText
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
                SyncScoreToText(text);
                EnsureWireOnScoreText(existing.gameObject, text: text, tmp: null);
                Debug.Log("[UIBootstrap] ScoreText found (Legacy), wire ensured");
                return;
            }
            var tmp = existing.GetComponent(System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro"));
            if (tmp != null)
            {
                ApplyScoreTextLayoutTMP(tmp);
                SyncScoreToTMP(tmp);
                EnsureWireOnScoreText(existing.gameObject, text: null, tmp: tmp);
                Debug.Log("[UIBootstrap] ScoreText found (TMP), wire ensured");
                return;
            }
        }

        var go = new GameObject("ScoreText");
        go.transform.SetParent(canvasRoot.transform, false);

        var tmpText = TryCreateTMP(go);
        if (tmpText != null)
        {
            ApplyScoreTextLayoutTMP(tmpText);
            SyncScoreToTMP(tmpText);
            WireScoreToTMP(tmpText);
            Debug.Log("[UIBootstrap] ScoreText created (TextMeshProUGUI)");
            return;
        }

        var legText = go.AddComponent<Text>();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font != null) legText.font = font;
        ApplyScoreTextLayout(legText);
        SyncScoreToText(legText);
        WireScoreToText(legText);
        Debug.Log("[UIBootstrap] ScoreText created (Legacy Text - TMP missing)");
    }

    static void SyncScoreToText(Text t)
    {
        if (t == null) return;
        int s = SnowPhysicsScoreManager.Instance != null ? SnowPhysicsScoreManager.Instance.Score : 0;
        t.text = "SCORE: " + s;
    }

    static void SyncScoreToTMP(Component tmp)
    {
        if (tmp == null) return;
        int s = SnowPhysicsScoreManager.Instance != null ? SnowPhysicsScoreManager.Instance.Score : 0;
        tmp.GetType().GetProperty("text")?.SetValue(tmp, "SCORE: " + s);
    }

    static void EnsureWireOnScoreText(GameObject go, Text text, Component tmp)
    {
        if (go == null) return;
        var wire = go.GetComponent<UIScoreWire>();
        if (wire != null) return;
        wire = go.AddComponent<UIScoreWire>();
        if (text != null)
            wire.InitLegacy(text);
        else if (tmp != null)
            wire.InitTMP(tmp);
    }

    static readonly Color ScoreTextColor = new Color(255f / 255f, 220f / 255f, 0f, 1f);

    static void LogScoreVisual(Component tmp)
    {
        try
        {
            var color = tmp.GetType().GetProperty("color")?.GetValue(tmp);
            var outlineW = tmp.GetType().GetProperty("outlineWidth")?.GetValue(tmp);
            var outlineC = tmp.GetType().GetProperty("outlineColor")?.GetValue(tmp);
            int fs = 0;
            var fsVal = tmp.GetType().GetProperty("fontSize")?.GetValue(tmp);
            if (fsVal != null) fs = (int)(float)fsVal;
            bool outlineOn = outlineW != null && ((float)outlineW) > 0.001f;
            UnityEngine.Debug.Log($"[ScoreVisual] score_text_color={color} score_outline_enabled={outlineOn.ToString().ToLower()} score_outline_color={outlineC} score_font_size={fs} score_visual_pass={outlineOn.ToString().ToLower()}");
            SnowLoopLogCapture.AppendToAssiReport($"=== ScoreVisual === score_text_color={color} score_outline_enabled={outlineOn} score_outline_color={outlineC} score_font_size={fs} score_visual_pass={outlineOn}");
        }
        catch { }
    }

    static void ApplyScoreTextLayout(Text t)
    {
        if (t == null) return;
        t.fontSize = 72;
        t.color = ScoreTextColor;
        t.fontStyle = FontStyle.Bold;
        var shadow = t.gameObject.GetComponent<UnityEngine.UI.Shadow>();
        if (shadow == null) shadow = t.gameObject.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = Color.black;
        shadow.effectDistance = new Vector2(2f, 2f);
        var outline = t.gameObject.GetComponent<UnityEngine.UI.Outline>();
        if (outline == null) outline = t.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, 2f);
        var rt = t.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(10f, -10f);
        rt.sizeDelta = new Vector2(420f, 96f);
        LogScoreOutline(t, "UI.Outline");
    }

    static void LogScoreOutline(Text t, string method)
    {
        if (t == null) return;
        var ol = t.gameObject.GetComponent<UnityEngine.UI.Outline>();
        bool outlineOn = ol != null;
        string path = GetGameObjectPath(t.gameObject);
        int dupCount = CountScoreObjects();
        UnityEngine.Debug.Log($"[ScoreOutline] score_object_name={t.gameObject.name} score_object_path={path} score_duplicate_count={dupCount} score_outline_enabled={outlineOn.ToString().ToLower()} score_outline_method={method} score_outline_color=#000000 score_font_size={t.fontSize} score_position=top-left score_single_display_pass={(dupCount == 1).ToString().ToLower()} score_outline_visual_pass={outlineOn.ToString().ToLower()}");
        SnowLoopLogCapture.AppendToAssiReport($"=== ScoreOutline === score_object_name={t.gameObject.name} score_object_path={path} score_duplicate_count={dupCount} score_outline_enabled={outlineOn} score_outline_method={method} score_outline_color=#000000 score_font_size={t.fontSize} score_position=top-left score_single_display_pass={(dupCount == 1)} score_outline_visual_pass={outlineOn}");
    }

    static string GetGameObjectPath(GameObject go)
    {
        if (go == null) return "?";
        var parts = new System.Collections.Generic.List<string>();
        var t = go.transform;
        while (t != null) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static int CountScoreObjects()
    {
        int c = 0;
        foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name == "ScoreText" && t.gameObject.activeInHierarchy) c++;
        }
        return c;
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
            rt.sizeDelta = new Vector2(420f, 96f);
        }
        try
        {
            tmp.GetType().GetProperty("fontSize")?.SetValue(tmp, 72);
            SetTMPColorSafe(tmp, "color", (Color32)ScoreTextColor);
            tmp.GetType().GetProperty("outlineWidth")?.SetValue(tmp, 0.25f);
            SetTMPColorSafe(tmp, "outlineColor", new Color32(0, 0, 0, 255));
            var fontMat = tmp.GetType().GetProperty("fontMaterial")?.GetValue(tmp);
            if (fontMat is Material mat && mat != null)
            {
                mat.EnableKeyword("OUTLINE_ON");
                mat.SetFloat("_OutlineWidth", 0.25f);
                mat.SetColor("_OutlineColor", Color.black);
            }
            var updatePad = tmp.GetType().GetMethod("UpdateMeshPadding", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance, null, System.Type.EmptyTypes, null);
            updatePad?.Invoke(tmp, null);
            LogScoreOutlineTMP(tmp, "TMP_outline+material");
        }
        catch { }
    }

    static void LogScoreOutlineTMP(Component tmp, string method)
    {
        if (tmp == null) return;
        try
        {
            var outlineW = tmp.GetType().GetProperty("outlineWidth")?.GetValue(tmp);
            bool outlineOn = outlineW != null && ((float)outlineW) > 0.001f;
            var fsVal = tmp.GetType().GetProperty("fontSize")?.GetValue(tmp);
            int fs = fsVal != null ? (int)(float)fsVal : 72;
            string path = GetGameObjectPath((tmp as MonoBehaviour)?.gameObject);
            int dupCount = CountScoreObjects();
            UnityEngine.Debug.Log($"[ScoreOutline] score_object_name={((tmp as MonoBehaviour)?.gameObject?.name ?? "?")} score_object_path={path} score_duplicate_count={dupCount} score_outline_enabled={outlineOn.ToString().ToLower()} score_outline_method={method} score_outline_color=#000000 score_font_size={fs} score_position=top-left score_single_display_pass={(dupCount == 1).ToString().ToLower()} score_outline_visual_pass={outlineOn.ToString().ToLower()}");
            SnowLoopLogCapture.AppendToAssiReport($"=== ScoreOutline === score_object_name={((tmp as MonoBehaviour)?.gameObject?.name ?? "?")} score_object_path={path} score_duplicate_count={dupCount} score_outline_enabled={outlineOn} score_outline_method={method} score_outline_color=#000000 score_font_size={fs} score_position=top-left score_single_display_pass={(dupCount == 1)} score_outline_visual_pass={outlineOn}");
        }
        catch { }
    }

    static Component TryCreateTMP(GameObject go)
    {
        var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        if (tmpType == null) return null;
        return go.AddComponent(tmpType) as Component;
    }

    /// <summary>TMP の color/outlineColor を PropertyType に合わせて安全に設定。Color32→Color 例外を回避。</summary>
    static void SetTMPColorSafe(Component tmp, string propName, Color32 c32)
    {
        var prop = tmp?.GetType().GetProperty(propName);
        if (prop == null) return;
        object val = prop.PropertyType == typeof(Color) ? (Color)c32 : c32;
        prop.SetValue(tmp, val);
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
