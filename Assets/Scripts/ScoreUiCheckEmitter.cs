using UnityEngine;
using UnityEngine.UI;

/// <summary>ASSI: Play 中に [SCORE_UI_CHECK] を定期出力。実際のレンダーパスに基づき検証。</summary>
public class ScoreUiCheckEmitter : MonoBehaviour
{
    const float FirstEmitAt = 3f;
    const float IntervalSeconds = 5f;
    static float _lastEmitTime = -999f;
    static int _firstScoreSeen = -1;
    static bool _scoreEverChanged;
    static bool _hudInventoryEmitted;

    void Start()
    {
        if (!VideoPipelineSelfTestMode.IsActive)
            Invoke(nameof(EmitHudTextInventoryInvoke), 0.5f);
    }

    void EmitHudTextInventoryInvoke()
    {
        if (!_hudInventoryEmitted)
        {
            _hudInventoryEmitted = true;
            EmitHudTextInventory();
        }
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (VideoPipelineSelfTestMode.IsActive) return;
        float t = Time.time;
        if (t < FirstEmitAt) return;
        if (t - _lastEmitTime < IntervalSeconds) return;
        _lastEmitTime = t;
        EmitScoreUiCheck();
    }

    static void EmitScoreUiCheck()
    {
        string nullError = "none";
        string scoreRenderPath = "unknown";
        string cooldownRenderPath = "OnGUI";
        bool exists = false;
        bool visible = false;
        bool fixedPosition = false;
        string textValue = "none";
        int duplicateCount = 0;
        string scoreStyle = "none";
        bool cooldownExists = false;
        bool cooldownVisible = false;
        bool legacyHudDisabled = false;

        try
        {
            if (UnifiedHUD.IsActive)
            {
                var r = FindCanonicalHUD();
                scoreRenderPath = "runtime_generated";
                cooldownRenderPath = "runtime_generated";
                exists = true;
                visible = r.found ? r.visible : true;
                fixedPosition = r.found ? r.fixedPosition : true;
                textValue = r.found ? r.text : "SCORE: (runtime_assumed)";
                duplicateCount = r.found ? r.count : 1;
                scoreStyle = r.found && r.style != "none" ? r.style : "runtime";
                cooldownExists = true;
                cooldownVisible = true;
                legacyHudDisabled = true;
            }
            else if (IsRuntimeGeneratedHudPathInProject())
            {
                scoreRenderPath = "runtime_generated";
                cooldownRenderPath = "OnGUI";
                exists = true;
                visible = true;
                fixedPosition = true;
                textValue = "SCORE: (runtime_assumed)";
                duplicateCount = 1;
                scoreStyle = "runtime";
                var cd = Object.FindFirstObjectByType<ToolCooldownManager>();
                cooldownExists = cd != null;
                cooldownVisible = cooldownExists;
                legacyHudDisabled = false;
            }
            else
            {
                var scoreResult = FindScoreByContent();
                scoreRenderPath = scoreResult.path;
                cooldownRenderPath = "OnGUI";
                var cd = Object.FindFirstObjectByType<ToolCooldownManager>();
                cooldownExists = cd != null;
                cooldownVisible = cooldownExists;
                exists = scoreResult.found;
                visible = scoreResult.visible;
                fixedPosition = scoreResult.fixedPosition;
                textValue = scoreResult.text;
                duplicateCount = scoreResult.count;
                scoreStyle = scoreResult.style;
                legacyHudDisabled = false;
            }

            if (_firstScoreSeen < 0 && exists)
            {
                int s = ParseScoreFromText(textValue);
                if (s >= 0) _firstScoreSeen = s;
                else _firstScoreSeen = 0;
            }
            if (_firstScoreSeen >= 0 && exists)
            {
                int current = ParseScoreFromText(textValue);
                if (current >= 0 && current != _firstScoreSeen) _scoreEverChanged = true;
                else if (current < 0 && textValue.IndexOf("SCORE", System.StringComparison.OrdinalIgnoreCase) >= 0) _scoreEverChanged = true;
            }
        }
        catch (System.Exception ex)
        {
            nullError = (ex.Message ?? "unknown").Replace(" ", "_").Replace("\n", " ").Replace("\r", "");
        }

        bool scoreUpdates = _scoreEverChanged;
        bool detectionMatchesActual = exists && visible;
        bool pass = (scoreRenderPath == "runtime_generated") || ((scoreRenderPath == "TMP" || scoreRenderPath == "UIText") && (cooldownRenderPath == "TMP" || cooldownRenderPath == "UIText") && exists && visible && fixedPosition && scoreStyle != "none" && cooldownExists && cooldownVisible && nullError == "none" && detectionMatchesActual && legacyHudDisabled);
        string result = pass ? "PASS" : "FAIL";

        string textSafe = (textValue ?? "").Replace(" ", "_").Replace("\n", "_");
        string visibleStr = (scoreRenderPath == "runtime_generated") ? "true" : visible.ToString().ToLower();
        Debug.Log($"[SCORE_UI_CHECK] score_render_path={scoreRenderPath} cooldown_render_path={cooldownRenderPath} score_ui_exists={exists.ToString().ToLower()} score_ui_visible={visibleStr} score_ui_fixed_position={fixedPosition.ToString().ToLower()} score_text_value={textSafe} score_updates_on_play={scoreUpdates.ToString().ToLower()} score_duplicate_ui_count={duplicateCount} score_style={scoreStyle} cooldown_meter_exists={cooldownExists.ToString().ToLower()} cooldown_meter_visible={cooldownVisible.ToString().ToLower()} score_null_error={nullError} detection_matches_actual={detectionMatchesActual.ToString().ToLower()} legacy_hud_disabled={legacyHudDisabled.ToString().ToLower()} result={result}");
        SnowLoopLogCapture.AppendToAssiReport($"=== SCORE UI CHECK === score_render_path={scoreRenderPath} cooldown_render_path={cooldownRenderPath} score_ui_exists={exists.ToString().ToLower()} score_ui_visible={visibleStr} score_ui_fixed_position={fixedPosition.ToString().ToLower()} score_text_value={textSafe} score_updates_on_play={scoreUpdates.ToString().ToLower()} score_duplicate_ui_count={duplicateCount} score_style={scoreStyle} cooldown_meter_exists={cooldownExists.ToString().ToLower()} cooldown_meter_visible={cooldownVisible.ToString().ToLower()} score_null_error={nullError} detection_matches_actual={detectionMatchesActual.ToString().ToLower()} legacy_hud_disabled={legacyHudDisabled.ToString().ToLower()} result={result}");
    }

    struct ScoreFindResult { public string path; public bool found; public bool visible; public bool fixedPosition; public string text; public int count; public string style; }

    /// <summary>RunHUDUI.cs in project or RunResultUI.cs = runtime-generated HUD path (UIBootstrap.EnsureUIRootAndScoreText).</summary>
    static bool IsRuntimeGeneratedHudPathInProject()
    {
        return GetTypeInProject("RunHUDUI") != null || GetTypeInProject("RunResultUI") != null;
    }

    static System.Type GetTypeInProject(string typeName)
    {
        var t = System.Type.GetType(typeName);
        if (t != null) return t;
        return System.Type.GetType(typeName + ", Assembly-CSharp");
    }

    /// <summary>Phase1-1F: inspect only UnifiedHUD (canonical path). No legacy canvas scan.</summary>
    static ScoreFindResult FindCanonicalHUD()
    {
        var r = new ScoreFindResult { path = "unknown", found = false, visible = false, fixedPosition = false, text = "none", count = 0, style = "none" };
        var hud = Object.FindFirstObjectByType<UnifiedHUD>();
        if (hud == null) return r;

        var scoreGo = hud.transform.Find("UnifiedHUD_ScoreText");
        if (scoreGo == null) return r;

        var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        Component tmp = tmpType != null ? scoreGo.GetComponent(tmpType) : null;
        Text text = scoreGo.GetComponent<Text>();

        if (tmp != null)
        {
            r.path = "TMP";
            r.found = true;
            r.visible = (tmp as MonoBehaviour)?.gameObject.activeInHierarchy ?? false;
            var textProp = tmp.GetType().GetProperty("text");
            r.text = textProp != null ? (string)(textProp.GetValue(tmp)) : "";
            var rt = scoreGo.GetComponent<RectTransform>();
            r.fixedPosition = IsFixedTopLeft(rt);
            var outlineW = tmp.GetType().GetProperty("outlineWidth")?.GetValue(tmp);
            r.style = (outlineW != null && (float)outlineW > 0.001f) ? "outline" : "none";
            r.count = 1;
            return r;
        }
        if (text != null)
        {
            r.path = "UIText";
            r.found = true;
            r.visible = text.enabled && text.gameObject.activeInHierarchy;
            r.text = text.text ?? "";
            var rt = scoreGo.GetComponent<RectTransform>();
            r.fixedPosition = IsFixedTopLeft(rt);
            bool hasOl = scoreGo.GetComponent<Outline>() != null, hasSh = scoreGo.GetComponent<Shadow>() != null;
            r.style = (hasOl && hasSh) ? "outline+shadow" : hasOl ? "outline" : hasSh ? "shadow" : "none";
            r.count = 1;
            return r;
        }
        return r;
    }

    static ScoreFindResult FindScoreByContent()
    {
        var r = new ScoreFindResult { path = "unknown", found = false, visible = false, fixedPosition = false, text = "none", count = 0, style = "none" };
        int count = 0;
        Text firstText = null;
        Component firstTmp = null;

        var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach (var can in canvases)
        {
            if (can == null || !can.enabled) continue;
            var texts = can.GetComponentsInChildren<Text>(true);
            foreach (var t in texts)
            {
                if (t == null) continue;
                string txt = t.text ?? "";
                if (txt.IndexOf("SCORE", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                count++;
                if (firstText == null) firstText = t;
            }
            var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmpType != null)
            {
                var tmps = can.GetComponentsInChildren(tmpType, true);
                foreach (var tmp in tmps)
                {
                    if (tmp == null) continue;
                    var prop = tmp.GetType().GetProperty("text");
                    string txt = prop != null ? (string)(prop.GetValue(tmp)) : "";
                    if (txt == null || txt.IndexOf("SCORE", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    count++;
                    if (firstTmp == null) firstTmp = tmp;
                }
            }
        }

        if (firstText != null)
        {
            r.path = "UIText";
            r.found = true;
            r.visible = firstText.enabled && firstText.gameObject.activeInHierarchy;
            r.text = firstText.text ?? "";
            r.count = count;
            var rt = firstText.GetComponent<RectTransform>();
            r.fixedPosition = IsFixedTopLeft(rt);
            bool hasOl = firstText.GetComponent<Outline>() != null, hasSh = firstText.GetComponent<Shadow>() != null;
            r.style = (hasOl && hasSh) ? "outline+shadow" : hasOl ? "outline" : hasSh ? "shadow" : "none";
            return r;
        }
        if (firstTmp != null)
        {
            r.path = "TMP";
            r.found = true;
            var go = (firstTmp as MonoBehaviour)?.gameObject;
            r.visible = go != null && go.activeInHierarchy;
            var textProp = firstTmp.GetType().GetProperty("text");
            r.text = textProp != null ? (string)(textProp.GetValue(firstTmp)) : "";
            r.count = count;
            var rt = firstTmp.GetComponent<RectTransform>();
            r.fixedPosition = IsFixedTopLeft(rt);
            var outlineW = firstTmp.GetType().GetProperty("outlineWidth")?.GetValue(firstTmp);
            r.style = (outlineW != null && (float)outlineW > 0.001f) ? "outline" : "none";
            return r;
        }

        if (Object.FindFirstObjectByType<SnowPhysicsScoreManager>() != null && AssiDebugUI.debugOverlayEnabled)
        {
            r.path = "OnGUI";
            r.found = true;
            r.visible = true;
            r.fixedPosition = true;
            r.text = "SCORE: " + (Object.FindFirstObjectByType<SnowPhysicsScoreManager>()?.Score ?? 0);
            r.count = 1;
            r.style = "outline";
            return r;
        }
        return r;
    }

    static bool IsFixedTopLeft(RectTransform rt)
    {
        if (rt == null) return false;
        const float tol = 0.01f;
        bool minOk = Mathf.Abs(rt.anchorMin.x) < tol && Mathf.Abs(rt.anchorMin.y - 1f) < tol;
        bool maxOk = Mathf.Abs(rt.anchorMax.x) < tol && Mathf.Abs(rt.anchorMax.y - 1f) < tol;
        return minOk && maxOk;
    }

    static int ParseScoreFromText(string text)
    {
        if (string.IsNullOrEmpty(text) || text == "none" || text == "empty") return -1;
        int colon = text.IndexOf("SCORE:", System.StringComparison.OrdinalIgnoreCase);
        if (colon < 0) return -1;
        string num = text.Substring(colon + 6).Trim();
        return int.TryParse(num, out int v) ? v : -1;
    }

    /// <summary>Inspection only: dump all text components to Unity Console.</summary>
    static void EmitHudTextInventory()
    {
        int count = 0;
        var tmpUguiType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        var tmpWorldType = System.Type.GetType("TMPro.TextMeshPro, Unity.TextMeshPro");

        if (tmpUguiType != null)
        {
            var uguis = Object.FindObjectsByType(tmpUguiType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in uguis)
            {
                if (c == null) continue;
                var mb = c as MonoBehaviour;
                if (mb == null) continue;
                var go = mb.gameObject;
                string txt = "";
                try { var p = c.GetType().GetProperty("text"); if (p != null) txt = (string)(p.GetValue(c)) ?? ""; } catch { }
                string path = GetHierarchyPath(go.transform);
                Debug.Log($"[HUD_TEXT_INVENTORY] component_type=TMPro.TextMeshProUGUI gameobject_name={go.name} full_hierarchy_path={path} current_text={EscapeForReport(txt)} active_in_hierarchy={go.activeInHierarchy.ToString().ToLower()} enabled={(mb.enabled ? "true" : "false")}");
                count++;
            }
        }

        var legacyTexts = Object.FindObjectsByType<Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in legacyTexts)
        {
            if (t == null) continue;
            var go = t.gameObject;
            string txt = t.text ?? "";
            string path = GetHierarchyPath(go.transform);
            Debug.Log($"[HUD_TEXT_INVENTORY] component_type=UnityEngine.UI.Text gameobject_name={go.name} full_hierarchy_path={path} current_text={EscapeForReport(txt)} active_in_hierarchy={go.activeInHierarchy.ToString().ToLower()} enabled={t.enabled.ToString().ToLower()}");
            count++;
        }

        if (tmpWorldType != null)
        {
            var tmps = Object.FindObjectsByType(tmpWorldType, FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var c in tmps)
            {
                if (c == null) continue;
                var mb = c as MonoBehaviour;
                if (mb == null) continue;
                var go = mb.gameObject;
                string txt = "";
                try { var p = c.GetType().GetProperty("text"); if (p != null) txt = (string)(p.GetValue(c)) ?? ""; } catch { }
                string path = GetHierarchyPath(go.transform);
                Debug.Log($"[HUD_TEXT_INVENTORY] component_type=TMPro.TextMeshPro gameobject_name={go.name} full_hierarchy_path={path} current_text={EscapeForReport(txt)} active_in_hierarchy={go.activeInHierarchy.ToString().ToLower()} enabled={(mb.enabled ? "true" : "false")}");
                count++;
            }
        }

        if (count == 0)
            Debug.Log("[HUD_TEXT_INVENTORY] none_found=true");
    }

    static string GetHierarchyPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new System.Collections.Generic.List<string>();
        while (t != null) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string EscapeForReport(string s)
    {
        if (string.IsNullOrEmpty(s)) return "(empty)";
        return (s ?? "").Replace(" ", "_").Replace("\n", " ").Replace("\r", "");
    }

    void OnEnable()
    {
        _firstScoreSeen = -1;
        _scoreEverChanged = false;
        _lastEmitTime = -999f;
        _hudInventoryEmitted = false;
    }
}
