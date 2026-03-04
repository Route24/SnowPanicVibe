using UnityEngine;

/// <summary>
/// Core gameplay loop: money from snow removal, roof weight tracking, collapse, game over.
/// </summary>
public class CoreGameplayManager : MonoBehaviour
{
    public static CoreGameplayManager Instance { get; private set; }

    [Header("Economy")]
    public int moneyPerChunk = 10;
    public int moneyPerFallingPiece = 10;

    [Header("Collapse")]
    public float collapseThresholdMeters = 0.95f;

    int _money;
    bool _isGameOver;
    RoofSnowSystem _roofSnow;

    public int Money => _money;
    public bool IsGameOver => _isGameOver;
    public float RoofWeightMeters => _roofSnow != null ? _roofSnow.roofSnowDepthMeters : 0f;
    public float CollapseThreshold => collapseThresholdMeters;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _roofSnow = FindFirstObjectByType<RoofSnowSystem>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        if (_isGameOver) return;
        if (_roofSnow == null) _roofSnow = FindFirstObjectByType<RoofSnowSystem>();
        if (_roofSnow == null) return;

        if (_roofSnow.roofSnowDepthMeters >= collapseThresholdMeters)
        {
            TriggerHouseCollapse();
        }
    }

    /// <summary>Called when a snow chunk lands on ground (MvpSnowChunkMotion or equivalent).</summary>
    public void AddMoneyFromChunkLanding(float depositAmount)
    {
        if (_isGameOver) return;
        int amount = Mathf.Max(1, Mathf.RoundToInt(depositAmount * 500f));
        amount = Mathf.Min(amount, moneyPerChunk * 3);
        _money += amount;
    }

    /// <summary>Called when a packed falling piece lands (SnowPackFallingPiece).</summary>
    public void AddMoneyFromFallingPiece()
    {
        if (_isGameOver) return;
        _money += moneyPerFallingPiece;
    }

    void TriggerHouseCollapse()
    {
        if (_isGameOver) return;
        _isGameOver = true;
        var fall = FindFirstObjectByType<SnowFallSystem>();
        if (fall != null) fall.enabled = false;
        Debug.Log($"[CoreGameplay] HOUSE COLLAPSE! Roof weight={RoofWeightMeters:F3} >= threshold={collapseThresholdMeters:F3}");
    }

    public void AddMoney(int amount)
    {
        if (_isGameOver) return;
        _money += amount;
    }
}
