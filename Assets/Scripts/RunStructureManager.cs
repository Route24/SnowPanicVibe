using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 1 Run の流れ: 開始→チャンス探索→雪崩連鎖→危険回避→結果表示→即リトライ。
/// 60〜120秒の短時間ループで再挑戦したくなる構造。
/// </summary>
public class RunStructureManager : MonoBehaviour
{
    public static RunStructureManager Instance { get; private set; }

    [Header("Run timing")]
    public float runTimeLimit = 90f;
    public float countdownReadySec = 1f;
    public float countdownStartSec = 1f;

    [Header("Score goals (rank boundaries)")]
    public int scoreBronze = 3000;
    public int scoreSilver = 6000;
    public int scoreGold = 10000;
    public int scorePlatinum = 15000;

    [Header("Combo")]
    public float comboDecaySeconds = 2f;

    [Header("Navigation")]
    public string titleSceneName = "SampleScene";

    public enum RunState { PreCountdown, Countdown, Running, ShowingResult }
    public RunState State { get; private set; } = RunState.PreCountdown;

    public float TimeLeft { get; private set; }
    public int MaxCombo { get; private set; }
    public int VillagerHits { get; private set; }
    public int MegaAvalanches { get; private set; }
    public string ResultRank { get; private set; }
    public int FinalScore { get; private set; }
    public bool RunStarted { get; private set; }
    public bool RunFinished { get; private set; }
    public bool RetryAvailable { get; private set; }

    public int BestScore { get; private set; }
    public int BestCombo { get; private set; }
    public string BestRank { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "SnowCore_AntiProtocol") return;
        if (FindFirstObjectByType<RunStructureManager>() != null) return;
        var go = new GameObject("RunStructureManager");
        go.AddComponent<RunStructureManager>();
        DontDestroyOnLoad(go);
        Debug.Log("[RunStructureManager] bootstrapped");
    }

    const string PrefsBestScore = "RunStructure_BestScore";
    const string PrefsBestCombo = "RunStructure_BestCombo";
    const string PrefsBestRank = "RunStructure_BestRank";

    int _combo;
    float _lastScoreTime = -999f;
    int _lastScore;
    float _countdownElapsed;
    string _currentSceneName;

    /// <summary>0=Ready, 1=Start, 2=Running</summary>
    public int CountdownPhase => State == RunState.Countdown
        ? (_countdownElapsed < countdownReadySec ? 0 : 1)
        : 2;

    public event Action<RunState> OnStateChanged;
    public event Action OnRunEnded;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        LoadBests();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        if (VideoPipelineSelfTestMode.IsActive) return;
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "SnowCore_AntiProtocol") return;
        _currentSceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        BeginRun();
    }

    void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, LoadSceneMode _)
    {
        if (s.name == _currentSceneName && State == RunState.ShowingResult)
            BeginRun();
    }

    void OnEnable()
    {
        if (SnowPhysicsScoreManager.Instance != null)
            SnowPhysicsScoreManager.Instance.OnScoreChanged += OnScoreChanged;
    }

    void OnDisable()
    {
        if (SnowPhysicsScoreManager.Instance != null)
            SnowPhysicsScoreManager.Instance.OnScoreChanged -= OnScoreChanged;
    }

    void Update()
    {
        if (VideoPipelineSelfTestMode.IsActive) return;

        switch (State)
        {
            case RunState.PreCountdown:
                State = RunState.Countdown;
                _countdownElapsed = 0f;
                OnStateChanged?.Invoke(State);
                break;
            case RunState.Countdown:
                _countdownElapsed += Time.deltaTime;
                float total = countdownReadySec + countdownStartSec;
                if (_countdownElapsed >= total)
                {
                    State = RunState.Running;
                    RunStarted = true;
                    TimeLeft = runTimeLimit;
                    _lastScore = SnowPhysicsScoreManager.Instance != null ? SnowPhysicsScoreManager.Instance.Score : 0;
                    OnStateChanged?.Invoke(State);
                }
                break;
            case RunState.Running:
                TimeLeft -= Time.deltaTime;
                if (TimeLeft <= 0f)
                    EndRun();
                if (Time.time - _lastScoreTime > comboDecaySeconds)
                    _combo = 0;
                SyncMegaCount();
                break;
        }
    }

    void OnScoreChanged(int score)
    {
        if (State != RunState.Running) return;
        int delta = score - _lastScore;
        _lastScore = score;
        if (delta > 0)
        {
            _combo += delta;
            _lastScoreTime = Time.time;
            if (_combo > MaxCombo) MaxCombo = _combo;
        }
    }

    void SyncMegaCount()
    {
        MegaAvalanches = AvalanchePhysicsSystem.MegaAvalancheCount;
    }

    public void BeginRun()
    {
        SnowPhysicsScoreManager.ResetForNewRun();
        AvalanchePhysicsSystem.ResetRunCounters();
        _combo = 0;
        MaxCombo = 0;
        VillagerHits = 0;
        MegaAvalanches = 0;
        RunStarted = false;
        RunFinished = false;
        RetryAvailable = false;
        State = RunState.PreCountdown;
        _lastScore = 0;
        _lastScoreTime = -999f;
        _countdownElapsed = 0f;
    }

    public void EndRun()
    {
        if (State != RunState.Running) return;
        State = RunState.ShowingResult;
        RunFinished = true;
        FinalScore = SnowPhysicsScoreManager.Instance != null ? SnowPhysicsScoreManager.Instance.Score : 0;
        ResultRank = ComputeRank(FinalScore);
        SyncMegaCount();
        UpdateBests();
        RetryAvailable = true;
        OnStateChanged?.Invoke(State);
        OnRunEnded?.Invoke();
        Debug.Log($"[RunStructure] END final_score={FinalScore} max_combo={MaxCombo} mega={MegaAvalanches} villager_hits={VillagerHits} rank={ResultRank}");
    }

    string ComputeRank(int score)
    {
        if (score >= scorePlatinum) return "S";
        if (score >= scoreGold) return "A";
        if (score >= scoreSilver) return "B";
        if (score >= scoreBronze) return "C";
        return "D";
    }

    void UpdateBests()
    {
        bool changed = false;
        if (FinalScore > BestScore) { BestScore = FinalScore; changed = true; }
        if (MaxCombo > BestCombo) { BestCombo = MaxCombo; changed = true; }
        if (CompareRank(ResultRank, BestRank) > 0) { BestRank = ResultRank; changed = true; }
        if (changed) SaveBests();
    }

    static int RankOrder(string r)
    {
        return r switch { "S" => 5, "A" => 4, "B" => 3, "C" => 2, "D" => 1, _ => 0 };
    }
    static int CompareRank(string a, string b) => RankOrder(a) - RankOrder(b);

    void LoadBests()
    {
        BestScore = PlayerPrefs.GetInt(PrefsBestScore, 0);
        BestCombo = PlayerPrefs.GetInt(PrefsBestCombo, 0);
        BestRank = PlayerPrefs.GetString(PrefsBestRank, "D");
    }

    void SaveBests()
    {
        PlayerPrefs.SetInt(PrefsBestScore, BestScore);
        PlayerPrefs.SetInt(PrefsBestCombo, BestCombo);
        PlayerPrefs.SetString(PrefsBestRank, BestRank);
        PlayerPrefs.Save();
    }

    public void Retry()
    {
        if (!RetryAvailable) return;
        RetryAvailable = false;
        SceneManager.LoadScene(_currentSceneName);
    }

    public void GoToTitle()
    {
        RetryAvailable = false;
        if (!string.IsNullOrEmpty(titleSceneName))
            SceneManager.LoadScene(titleSceneName);
        else
            SceneManager.LoadScene(0);
    }

    public static void EmitRunStructureTestToReport()
    {
        var m = Instance;
        bool runStarted = m != null && m.RunStarted;
        bool runFinished = m != null && m.RunFinished;
        float runTimeLimit = m != null ? m.runTimeLimit : 90f;
        int finalScore = m != null ? m.FinalScore : 0;
        int maxCombo = m != null ? m.MaxCombo : 0;
        int villagerHits = m != null ? m.VillagerHits : 0;
        string resultRank = (m != null && !string.IsNullOrEmpty(m.ResultRank)) ? m.ResultRank : "N/A";
        bool retryAvailable = m != null && m.RetryAvailable;

        SnowLoopLogCapture.AppendToAssiReport("=== RUN STRUCTURE TEST ===");
        SnowLoopLogCapture.AppendToAssiReport("run_started=" + runStarted.ToString().ToLower());
        SnowLoopLogCapture.AppendToAssiReport("run_finished=" + runFinished.ToString().ToLower());
        SnowLoopLogCapture.AppendToAssiReport("run_time_limit=" + runTimeLimit);
        SnowLoopLogCapture.AppendToAssiReport("final_score=" + finalScore);
        SnowLoopLogCapture.AppendToAssiReport("max_combo=" + maxCombo);
        SnowLoopLogCapture.AppendToAssiReport("villager_hits=" + villagerHits);
        SnowLoopLogCapture.AppendToAssiReport("result_rank=" + resultRank);
        SnowLoopLogCapture.AppendToAssiReport("retry_available=" + retryAvailable.ToString().ToLower());
    }
}
