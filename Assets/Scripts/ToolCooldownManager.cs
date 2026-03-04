using UnityEngine;

/// <summary>Tool cooldown: blocks hits for cooldownSec after each hit. Debug: cooldown remaining.</summary>
public class ToolCooldownManager : MonoBehaviour
{
    public static ToolCooldownManager Instance { get; private set; }

    [Header("Cooldown")]
    [Tooltip("Cannot spam; gives watch time for avalanche growth.")]
    public float cooldownSec = 0.80f;

    float _cooldownEndTime;
    float _lastHitTime;

    public float CooldownRemaining => Mathf.Max(0f, _cooldownEndTime - Time.time);
    public bool CanHit => CooldownRemaining <= 0f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void OnHit()
    {
        _lastHitTime = Time.time;
        _cooldownEndTime = Time.time + cooldownSec;
        Debug.Log($"[TempoDebug] cooldown started remaining={cooldownSec:F2}s");
    }
}
