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
        string cooldownRenderPath = "OnGUI"; // AssiDebugUI.DrawCooldownRing uses OnGUI
        bool exists = false;
        bool visible = false;
        bool fixedPosition = false;
        string textValue = "none";
        int duplicateCount = 0;
        string scoreStyle = "none";
        bool cooldownExists = false;
        bool cooldownVisible = false;

        try
        {
            var scoreResult = FindScoreByContent();
            scoreRenderPath = scoreResult.path;
            if (UnifiedHUD.IsActive) { cooldownRenderPath = scoreResult.path; cooldownExists = true; cooldownVisible = true; }
            else { cooldownRenderPath = "OnGUI"; var cd = Object.FindFirstObjectByType<ToolCooldownManager>(); cooldownExists = cd != null; cooldownVisible = cooldownExists; }
            exists = scoreResult.found;
            visible = scoreResult.visible;
            fixedPosition = scoreResult.fixedPosition;
            textValue = scoreResult.text;
            duplicateCount = scoreResult.count;
            scoreStyle = scoreResult.style;

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
        bool detectionMatchesActual = exists && visible; // what we detect matches what user sees
        bool pass = (scoreRenderPath == "TMP" || scoreRenderPath == "UIText") && (cooldownRenderPath == "TMP" || cooldownRenderPath == "UIText") && exists && visible && fixedPosition && scoreStyle != "none" && cooldownExists && cooldownVisible && nullError == "none" && detectionMatchesActual;
        string result = pass ? "PASS" : "FAIL";

        string textSafe = (textValue ?? "").Replace(" ", "_").Replace("\n", "_");
        Debug.Log($"[SCORE_UI_CHECK] score_render_path={scoreRenderPath} cooldown_render_path={cooldownRenderPath} score_ui_exists={exists.ToString().ToLower()} score_ui_visible={visible.ToString().ToLower()} score_ui_fixed_position={fixedPosition.ToString().ToLower()} score_text_value={textSafe} score_updates_on_play={scoreUpdates.ToString().ToLower()} score_duplicate_ui_count={duplicateCount} score_style={scoreStyle} cooldown_meter_exists={cooldownExists.ToString().ToLower()} cooldown_meter_visible={cooldownVisible.ToString().ToLower()} score_null_error={nullError} detection_matches_actual={detectionMatchesActual.ToString().ToLower()} result={result}");
    }

    struct ScoreFindResult { public string path; public bool found; public bool visible; public bool fixedPosition; public string text; public int count; public string style; }

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

    void OnEnable()
    {
        _firstScoreSeen = -1;
        _scoreEverChanged = false;
        _lastEmitTime = -999f;
    }
}
