using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Run中のHUD: Ready/Start カウントダウン、残り時間表示。
/// </summary>
public class RunHUDUI : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<RunHUDUI>() != null) return;
        var go = new GameObject("RunHUDUI");
        go.AddComponent<RunHUDUI>();
        DontDestroyOnLoad(go);
    }

    GameObject _hudRoot;
    Text _centerText;
    Text _timerText;

    void Start()
    {
        if (VideoPipelineSelfTestMode.IsActive) return;
        EnsureHUD();
    }

    void Update()
    {
        if (RunStructureManager.Instance == null || _centerText == null || _timerText == null) return;
        var m = RunStructureManager.Instance;

        switch (m.State)
        {
            case RunStructureManager.RunState.Countdown:
                _centerText.text = m.CountdownPhase == 0 ? "Ready" : "Start";
                _centerText.enabled = true;
                _timerText.enabled = false;
                break;
            case RunStructureManager.RunState.Running:
                _centerText.enabled = false;
                _timerText.enabled = true;
                int sec = Mathf.CeilToInt(Mathf.Max(0, m.TimeLeft));
                _timerText.text = $"{sec / 60}:{(sec % 60):D2}";
                break;
            default:
                _centerText.enabled = false;
                _timerText.enabled = false;
                break;
        }
    }

    void EnsureHUD()
    {
        var canvas = GameObject.Find("Canvas") ?? GameObject.Find("UIRoot");
        if (canvas == null)
        {
            UIBootstrap.EnsureUIRootAndScoreText();
            canvas = GameObject.Find("Canvas") ?? GameObject.Find("UIRoot");
        }
        if (canvas == null) return;
        var existing = canvas.transform.Find("RunHUD");
        if (existing != null) { _hudRoot = existing.gameObject; _centerText = existing.Find("CenterText")?.GetComponent<Text>(); _timerText = existing.Find("TimerText")?.GetComponent<Text>(); return; }

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        _hudRoot = new GameObject("RunHUD");
        _hudRoot.transform.SetParent(canvas.transform, false);
        var rt = _hudRoot.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var centerGo = new GameObject("CenterText");
        centerGo.transform.SetParent(_hudRoot.transform, false);
        _centerText = centerGo.AddComponent<Text>();
        _centerText.font = font;
        _centerText.fontSize = 72;
        _centerText.color = Color.yellow;
        _centerText.alignment = TextAnchor.MiddleCenter;
        _centerText.text = "Ready";
        var centerRt = centerGo.GetComponent<RectTransform>();
        centerRt.anchorMin = new Vector2(0.5f, 0.5f);
        centerRt.anchorMax = new Vector2(0.5f, 0.5f);
        centerRt.pivot = new Vector2(0.5f, 0.5f);
        centerRt.anchoredPosition = Vector2.zero;
        centerRt.sizeDelta = new Vector2(400, 100);

        var timerGo = new GameObject("TimerText");
        timerGo.transform.SetParent(_hudRoot.transform, false);
        _timerText = timerGo.AddComponent<Text>();
        _timerText.font = font;
        _timerText.fontSize = 48;
        _timerText.color = Color.white;
        _timerText.alignment = TextAnchor.UpperRight;
        _timerText.text = "1:30";
        var timerRt = timerGo.GetComponent<RectTransform>();
        timerRt.anchorMin = new Vector2(1f, 1f);
        timerRt.anchorMax = new Vector2(1f, 1f);
        timerRt.pivot = new Vector2(1f, 1f);
        timerRt.anchoredPosition = new Vector2(-20, -20);
        timerRt.sizeDelta = new Vector2(150, 60);

        _centerText.enabled = false;
        _timerText.enabled = false;
    }
}
