using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Run終了後の結果画面。1クリックでRetry、Titleへ戻る。
/// </summary>
public class RunResultUI : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<RunResultUI>() != null) return;
        var go = new GameObject("RunResultUI");
        go.AddComponent<RunResultUI>();
        DontDestroyOnLoad(go);
    }

    GameObject _panel;
    Button _retryButton;
    Button _titleButton;

    bool _subscribed;

    void Start()
    {
        if (VideoPipelineSelfTestMode.IsActive) return;
        TrySubscribeAndEnsurePanel();
    }

    void TrySubscribeAndEnsurePanel()
    {
        if (RunStructureManager.Instance == null || _subscribed) return;
        EnsurePanel();
        RunStructureManager.Instance.OnStateChanged += OnStateChanged;
        RunStructureManager.Instance.OnRunEnded += OnRunEnded;
        _subscribed = true;
        if (_panel != null) _panel.SetActive(false);
    }

    void OnDestroy()
    {
        if (_subscribed && RunStructureManager.Instance != null)
        {
            RunStructureManager.Instance.OnStateChanged -= OnStateChanged;
            RunStructureManager.Instance.OnRunEnded -= OnRunEnded;
            _subscribed = false;
        }
    }

    void Update()
    {
        if (RunStructureManager.Instance == null) return;
        TrySubscribeAndEnsurePanel();
        if (RunStructureManager.Instance.State == RunStructureManager.RunState.ShowingResult && _panel != null && !_panel.activeSelf)
            _panel.SetActive(true);
    }

    void OnStateChanged(RunStructureManager.RunState s)
    {
        if (_panel != null)
            _panel.SetActive(s == RunStructureManager.RunState.ShowingResult);
    }

    void OnRunEnded()
    {
        if (_panel != null)
        {
            _panel.SetActive(true);
            UpdatePanelText();
        }
    }

    void EnsurePanel()
    {
        var canvas = GameObject.Find("Canvas") ?? GameObject.Find("UIRoot");
        if (canvas == null)
        {
            UIBootstrap.EnsureUIRootAndScoreText();
            canvas = GameObject.Find("Canvas") ?? GameObject.Find("UIRoot");
        }
        if (canvas == null) return;
        var existing = canvas.transform.Find("RunResultPanel");
        if (existing != null) { _panel = existing.gameObject; return; }

        _panel = new GameObject("RunResultPanel");
        _panel.transform.SetParent(canvas.transform, false);

        var image = _panel.AddComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.7f);
        var rt = _panel.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var resultLabel = new GameObject("ResultLabel");
        resultLabel.transform.SetParent(_panel.transform, false);
        var resultText = resultLabel.AddComponent<Text>();
        resultText.font = font;
        resultText.fontSize = 48;
        resultText.color = Color.white;
        resultText.alignment = TextAnchor.MiddleCenter;
        resultText.text = "RESULT";
        var resultRt = resultLabel.GetComponent<RectTransform>();
        resultRt.anchorMin = new Vector2(0.5f, 0.85f);
        resultRt.anchorMax = new Vector2(0.5f, 0.85f);
        resultRt.pivot = new Vector2(0.5f, 0.5f);
        resultRt.anchoredPosition = Vector2.zero;
        resultRt.sizeDelta = new Vector2(400, 60);

        var statsLabel = new GameObject("StatsLabel");
        statsLabel.transform.SetParent(_panel.transform, false);
        var statsText = statsLabel.AddComponent<Text>();
        statsText.font = font;
        statsText.fontSize = 28;
        statsText.color = Color.white;
        statsText.alignment = TextAnchor.MiddleCenter;
        statsText.text = "";
        var statsRt = statsLabel.GetComponent<RectTransform>();
        statsRt.anchorMin = new Vector2(0.5f, 0.55f);
        statsRt.anchorMax = new Vector2(0.5f, 0.55f);
        statsRt.pivot = new Vector2(0.5f, 0.5f);
        statsRt.anchoredPosition = Vector2.zero;
        statsRt.sizeDelta = new Vector2(500, 200);
        statsLabel.AddComponent<RunResultStats>().statsText = statsText;

        var bestLabel = new GameObject("BestLabel");
        bestLabel.transform.SetParent(_panel.transform, false);
        var bestText = bestLabel.AddComponent<Text>();
        bestText.font = font;
        bestText.fontSize = 22;
        bestText.color = new Color(1f, 0.9f, 0.5f);
        bestText.alignment = TextAnchor.MiddleCenter;
        bestText.text = "";
        var bestRt = bestLabel.GetComponent<RectTransform>();
        bestRt.anchorMin = new Vector2(0.5f, 0.35f);
        bestRt.anchorMax = new Vector2(0.5f, 0.35f);
        bestRt.pivot = new Vector2(0.5f, 0.5f);
        bestRt.anchoredPosition = Vector2.zero;
        bestRt.sizeDelta = new Vector2(500, 80);
        bestLabel.AddComponent<RunResultBest>().bestText = bestText;

        _retryButton = CreateButton(_panel.transform, "Retry", new Vector2(0.5f, 0.18f), font, () => RunStructureManager.Instance?.Retry());
        _titleButton = CreateButton(_panel.transform, "Title", new Vector2(0.5f, 0.06f), font, () => RunStructureManager.Instance?.GoToTitle());

        _panel.SetActive(false);
    }

    static Button CreateButton(Transform parent, string label, Vector2 anchorY, Font font, System.Action onClick)
    {
        var go = new GameObject(label + "Button");
        go.transform.SetParent(parent, false);
        var image = go.AddComponent<Image>();
        image.color = new Color(0.2f, 0.5f, 0.9f);
        var button = go.AddComponent<Button>();
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, anchorY.y);
        rt.anchorMax = new Vector2(0.5f, anchorY.y);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(240, 50);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(go.transform, false);
        var text = textGo.AddComponent<Text>();
        text.font = font;
        text.fontSize = 28;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.text = label;
        var textRt = textGo.GetComponent<RectTransform>();
        textRt.anchorMin = Vector2.zero;
        textRt.anchorMax = Vector2.one;
        textRt.offsetMin = Vector2.zero;
        textRt.offsetMax = Vector2.zero;

        button.onClick.AddListener(() => onClick?.Invoke());
        return button;
    }

    void UpdatePanelText()
    {
        var stats = _panel != null ? _panel.GetComponentInChildren<RunResultStats>() : null;
        var best = _panel != null ? _panel.GetComponentInChildren<RunResultBest>() : null;
        if (stats != null) stats.Refresh();
        if (best != null) best.Refresh();
    }
}

/// <summary>結果の数値を表示</summary>
public class RunResultStats : MonoBehaviour
{
    public Text statsText;

    public void Refresh()
    {
        if (statsText == null || RunStructureManager.Instance == null) return;
        var m = RunStructureManager.Instance;
        statsText.text = $"Score: {m.FinalScore}\nCombo: {m.MaxCombo}\nMega: {m.MegaAvalanches}\nVillager Hits: {m.VillagerHits}\nRank: {m.ResultRank}";
    }
}

/// <summary>Best記録を表示</summary>
public class RunResultBest : MonoBehaviour
{
    public Text bestText;

    public void Refresh()
    {
        if (bestText == null || RunStructureManager.Instance == null) return;
        var m = RunStructureManager.Instance;
        bestText.text = $"Best Score: {m.BestScore}  Best Combo: {m.BestCombo}  Best Rank: {m.BestRank}";
    }
}
