using UnityEngine;
using System;

/// <summary>ASSI: 雪塊が着地→点滅→消滅したとき +1 スコア。プロトタイプ用（Debug.Log 可）</summary>
public class SnowPhysicsScoreManager : MonoBehaviour
{
    public static SnowPhysicsScoreManager Instance { get; private set; }

    public const int ScorePerDespawn = 1;

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

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>雪塊が地面着地→点滅→消滅のサイクルを完了したとき呼ぶ</summary>
    public void AddScoreOnDespawn()
    {
        Add(ScorePerDespawn);
    }

    /// <summary>スコア加算。ScoreText は変化時のみ更新。</summary>
    public void Add(int delta)
    {
        if (delta <= 0) return;
        _score += delta;
        OnScoreChanged?.Invoke(_score);
        Debug.Log($"[SnowPhysicsScore] +{delta} total={_score}");
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
    }
}
