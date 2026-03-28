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
        DisableLegacyHUD();
        Debug.Log($"[UnifiedHUD] Created Score+Status (scoreTMP={scoreTMP} statusTMP={statusTMP})");
    }

    /// <summary>Phase1-1F: disable legacy ScoreText/Canvas that would conflict with canonical HUD.</summary>
    void DisableLegacyHUD()
    {
        foreach (var c in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c == null || c.gameObject == gameObject) continue;
            var st = c.transform.Find("ScoreText");
            if (st != null && st.gameObject.activeSelf)
            {
                st.gameObject.SetActive(false);
                Debug.Log("[UnifiedHUD] Disabled legacy ScoreText: " + GetPath(st));
            }
        }
        var snowScore = Object.FindFirstObjectByType<SnowScoreDisplayUI>();
        if (snowScore != null && snowScore.gameObject.activeInHierarchy)
        {
            snowScore.gameObject.SetActive(false);
            Debug.Log("[UnifiedHUD] Disabled legacy SnowScoreDisplayUI");
        }
    }

    static string GetPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new System.Collections.Generic.List<string>();
        while (t != null) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    void CreateScoreBacking()
    {
        var backing = new GameObject("ScoreBacking");
        backing.transform.SetParent(transform, false);
        backing.transform.SetAsFirstSibling();
        var img = backing.AddComponent<Image>();
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        img.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
        img.color = Color.black;
        var rt = backing.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(8f, -8f);
        rt.sizeDelta = new Vector2(420f, 110f);
    }

    bool TryCreateScoreText()
    {
        CreateScoreBacking();
        var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        if (tmpType == null) return false;
        var go = new GameObject(ScoreTextName);
        go.transform.SetParent(transform, false);
        var tmp = go.AddComponent(tmpType) as Component;
        if (tmp == null) return false;
        tmp.GetType().GetProperty("text")?.SetValue(tmp, "SCORE: 0");
        tmp.GetType().GetProperty("fontSize")?.SetValue(tmp, 72);
        SetTMPColorSafe(tmp, "color", new Color32(255, 220, 0, 255));
        tmp.GetType().GetProperty("outlineWidth")?.SetValue(tmp, 0.4f);
        SetTMPColorSafe(tmp, "outlineColor", new Color32(0, 0, 0, 255));
        var outline = go.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(2f, -2f);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(12f, -12f); rt.sizeDelta = new Vector2(400f, 90f);
        _scoreTMP = tmp;
        return true;
    }

    void CreateScoreTextLegacy()
    {
        CreateScoreBacking();
        var go = new GameObject(ScoreTextName);
        go.transform.SetParent(transform, false);
        var t = go.AddComponent<Text>();
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.fontSize = 72;
        t.color = new Color(255f/255f, 220f/255f, 0f, 1f);
        t.fontStyle = FontStyle.Bold;
        t.text = "SCORE: 0";
        var shadow = go.AddComponent<Shadow>(); shadow.effectColor = Color.black; shadow.effectDistance = new Vector2(2f, 2f);
        var outline = go.AddComponent<Outline>(); outline.effectColor = Color.black; outline.effectDistance = new Vector2(2f, -2f);
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
        SetTMPColorSafe(tmp, "color", new Color32(255, 220, 0, 255));
        tmp.GetType().GetProperty("outlineWidth")?.SetValue(tmp, 0.2f);
        SetTMPColorSafe(tmp, "outlineColor", new Color32(0, 0, 0, 255));
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
        var outline = go.AddComponent<Outline>(); outline.effectColor = Color.black; outline.effectDistance = new Vector2(2f, 2f);
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
        Invoke(nameof(InspectScoreStyle), 0.2f);
    }

    void InspectScoreStyle()
    {
        var go = _scoreTMP != null ? _scoreTMP.gameObject : _scoreText != null ? _scoreText.gameObject : null;
        if (go == null) { Debug.Log("[SCORE_STYLE_INSPECT] text_gameobject=null"); return; }
        string path = GetPath(go.transform);
        var comps = go.GetComponents<Component>();
        var compNames = new System.Collections.Generic.List<string>();
        foreach (var c in comps) { if (c != null) compNames.Add(c.GetType().Name); }
        string componentType = _scoreTMP != null ? "TextMeshProUGUI" : "UI.Text";
        string currentText = _scoreText != null ? (_scoreText.text ?? "") : (string)(_scoreTMP?.GetType().GetProperty("text")?.GetValue(_scoreTMP) ?? "");
        Debug.Log($"[SCORE_STYLE_INSPECT] owner_file=UnifiedHUD.cs owner_class=UnifiedHUD text_gameobject_name={go.name} full_hierarchy_path={path} component_type={componentType} attached_components=[{string.Join(",", compNames)}] current_text={currentText}");
        if (_scoreTMP != null)
        {
            float ow = 0f; try { var p = _scoreTMP.GetType().GetProperty("outlineWidth"); if (p != null) ow = (float)(p.GetValue(_scoreTMP) ?? 0f); } catch { }
            object oc = null; try { var p = _scoreTMP.GetType().GetProperty("outlineColor"); if (p != null) oc = p.GetValue(_scoreTMP); } catch { }
            object fc = null; try { var p = _scoreTMP.GetType().GetProperty("color"); if (p != null) fc = p.GetValue(_scoreTMP); } catch { }
            string matName = "unknown"; try { var fontProp = _scoreTMP.GetType().GetProperty("font"); if (fontProp != null) { var font = fontProp.GetValue(_scoreTMP); if (font != null) matName = font.GetType().GetProperty("material")?.GetValue(font)?.ToString() ?? "null"; } } catch { }
            var sharedMatProp = _scoreTMP.GetType().GetProperty("fontSharedMaterial"); if (sharedMatProp != null) { try { var mat = sharedMatProp.GetValue(_scoreTMP); if (mat != null) matName = mat.ToString(); } catch { } }
            Debug.Log($"[SCORE_STYLE_INSPECT] tmp_material={matName} tmp_outline_width={ow} tmp_outline_color={oc} tmp_face_color={fc}");
        }
        if (_scoreText != null)
        {
            var outline = go.GetComponent<Outline>();
            var shadow = go.GetComponent<Shadow>();
            Debug.Log($"[SCORE_STYLE_INSPECT] ui_outline_exists={outline != null} ui_outline_effectColor={(outline != null ? outline.effectColor.ToString() : "n/a")} ui_outline_effectDistance={(outline != null ? outline.effectDistance.ToString() : "n/a")} ui_shadow_exists={shadow != null}");
        }
        if (_scoreTMP != null)
        {
            var outline = go.GetComponent<Outline>();
            Debug.Log($"[SCORE_STYLE_INSPECT] ui_outline_exists={outline != null} ui_outline_effectColor={(outline != null ? outline.effectColor.ToString() : "n/a")} ui_outline_effectDistance={(outline != null ? outline.effectDistance.ToString() : "n/a")}");
        }
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
        // HUD 全体の表示/非表示（SnowStrip2D 廃止のため常時 true）
        bool vis = true;
        if (_scoreText  != null) _scoreText.gameObject.SetActive(vis);
        if (_scoreTMP   != null) _scoreTMP.gameObject.SetActive(vis);
        if (_statusText != null) _statusText.gameObject.SetActive(vis);
        if (_statusTMP  != null) _statusTMP.gameObject.SetActive(vis);
        // ScoreBacking（黒背景）も同期
        var backing = transform.Find("ScoreBacking");
        if (backing != null) backing.gameObject.SetActive(vis);

        if (!vis) return;

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

    /// <summary>TMP の color/outlineColor を PropertyType に合わせて安全に設定。Color32→Color 例外を回避。</summary>
    static void SetTMPColorSafe(Component tmp, string propName, Color32 c32)
    {
        var prop = tmp?.GetType().GetProperty(propName);
        if (prop == null) return;
        object val = prop.PropertyType == typeof(Color) ? (Color)c32 : c32;
        prop.SetValue(tmp, val);
    }
}
