using UnityEngine;
using System;
using SD = System.Diagnostics;

/// <summary>ASSI: 雪塊が着地→点滅→消滅したとき +1 スコア。プロトタイプ用（Debug.Log 可）</summary>
public class SnowPhysicsScoreManager : MonoBehaviour
{
    public static SnowPhysicsScoreManager Instance { get; private set; }

    public const int ScorePerDespawn = 1;

    static bool _addEverCalledThisSession;

    int _score;

    public int Score => _score;

    public event Action<int> OnScoreChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        Debug.Log("[HIT_REGISTRATION_CHECK] hit_registered=false");
        Debug.Log("[HIT_REGISTRATION_CHECK] reason=session_start");
        Debug.Log("[HIT_REGISTRATION_CHECK] owner_file=AvalanchePhysicsSystem.cs SnowPackFallingPiece.cs SnowClump.cs SnowTestSlideAssist.cs");
        Debug.Log("[HIT_REGISTRATION_CHECK] owner_class=SnowPhysicsScoreManager");
        Debug.Log("[RAW_SCORE_CHECK] score_before=0");
        Debug.Log("[RAW_SCORE_CHECK] hit_test_performed=false");
        Debug.Log("[RAW_SCORE_CHECK] score_after=0");
        Debug.Log("[RAW_SCORE_CHECK] score_changed=false");
        Debug.Log("[RAW_SCORE_CHECK] increment_owner_file=AvalanchePhysicsSystem.cs SnowPackFallingPiece.cs SnowClump.cs SnowTestSlideAssist.cs");
        Debug.Log("[RAW_SCORE_CHECK] increment_owner_class=SnowPhysicsScoreManager");
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            if (!_addEverCalledThisSession)
            {
                Debug.Log("[HIT_REGISTRATION_CHECK] hit_registered=false");
                Debug.Log("[HIT_REGISTRATION_CHECK] reason=Add() never invoked during session");
                Debug.Log("[HIT_REGISTRATION_CHECK] owner_file=AvalanchePhysicsSystem.cs SnowPackFallingPiece.cs SnowClump.cs SnowTestSlideAssist.cs");
                Debug.Log("[HIT_REGISTRATION_CHECK] owner_class=SnowPhysicsScoreManager");
                Debug.Log("[RAW_SCORE_CHECK] score_before=0");
                Debug.Log("[RAW_SCORE_CHECK] hit_test_performed=false");
                Debug.Log("[RAW_SCORE_CHECK] score_after=0");
                Debug.Log("[RAW_SCORE_CHECK] score_changed=false");
                Debug.Log("[RAW_SCORE_CHECK] increment_owner_file=AvalanchePhysicsSystem.cs SnowPackFallingPiece.cs SnowClump.cs SnowTestSlideAssist.cs");
                Debug.Log("[RAW_SCORE_CHECK] increment_owner_class=SnowPhysicsScoreManager");
            }
            Instance = null;
        }
    }

    /// <summary>雪塊が地面着地→点滅→消滅のサイクルを完了したとき呼ぶ</summary>
    public void AddScoreOnDespawn()
    {
        Add(ScorePerDespawn);
    }

    /// <summary>Run Structure: 新Run開始時にスコアをリセット。</summary>
    public static void ResetForNewRun()
    {
        if (Instance == null) return;
        Instance._score = 0;
        Instance.OnScoreChanged?.Invoke(0);
        Debug.Log("[SnowPhysicsScore] ResetForNewRun");
    }

    /// <summary>スコア加算。ScoreText は変化時のみ更新。</summary>
    public void Add(int delta)
    {
        if (delta <= 0) return;
        int before = _score;
        _score += delta;
        _addEverCalledThisSession = true;
        BugOriginTracker.RecordScoreUpdate(before, _score);
        OnScoreChanged?.Invoke(_score);
        Debug.Log($"[SnowPhysicsScore] +{delta} total={_score}");
        string ownerFile = "unknown";
        string ownerClass = "unknown";
        try
        {
            var st = new SD.StackTrace(2, true);
            for (int i = 0; i < Math.Min(5, st.FrameCount); i++)
            {
                var frame = st.GetFrame(i);
                if (frame == null) continue;
                var method = frame.GetMethod();
                if (method == null || method.DeclaringType == typeof(SnowPhysicsScoreManager)) continue;
                ownerClass = method.DeclaringType?.Name ?? "unknown";
                var file = frame.GetFileName();
                ownerFile = !string.IsNullOrEmpty(file) ? System.IO.Path.GetFileName(file) : "unknown";
                break;
            }
        }
        catch { }
        Debug.Log("[HIT_REGISTRATION_CHECK] hit_registered=true");
        Debug.Log($"[HIT_REGISTRATION_CHECK] reason=Add(delta={delta}) invoked");
        Debug.Log($"[HIT_REGISTRATION_CHECK] owner_file={ownerFile}");
        Debug.Log($"[HIT_REGISTRATION_CHECK] owner_class={ownerClass}");
        Debug.Log($"[RAW_SCORE_CHECK] score_before={before}");
        Debug.Log("[RAW_SCORE_CHECK] hit_test_performed=true");
        Debug.Log($"[RAW_SCORE_CHECK] score_after={_score}");
        Debug.Log("[RAW_SCORE_CHECK] score_changed=true");
        Debug.Log($"[RAW_SCORE_CHECK] increment_owner_file={ownerFile}");
        Debug.Log($"[RAW_SCORE_CHECK] increment_owner_class={ownerClass}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureBootstrap()
    {
        EnsureBootstrapIfNeeded();
    }

    public static void EnsureBootstrapIfNeeded()
    {
        if (FindFirstObjectByType<SnowPhysicsScoreManager>() != null) return;
        var go = new GameObject("SnowPhysicsScoreManager");
        go.AddComponent<SnowPhysicsScoreManager>();    
        UnityEngine.Object.DontDestroyOnLoad(go);     
        Debug.Log("[SnowPhysicsScoreManager] bootstrapped");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic()
    {
        Instance = null;
        _addEverCalledThisSession = false;
    }
}
