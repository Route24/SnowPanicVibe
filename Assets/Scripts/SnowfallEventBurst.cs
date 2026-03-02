using System.Collections;
using UnityEngine;

/// <summary>常時降雪をイベント時のみ一時ブーストする</summary>
public class SnowfallEventBurst : MonoBehaviour
{
    static SnowfallEventBurst _instance;

    ParticleSystem _ps;
    float _baseRate;
    float _burstRate;
    float _baseSpeed;
    float _burstSpeed;
    int _baseMaxParticles;
    int _burstMaxParticles;
    float _defaultDuration;
    Coroutine _burstCoroutine;
    Coroutine _endRewardCoroutine;

    public static void TriggerGlobal(float duration = -1f)
    {
        if (_instance != null) _instance.Trigger(duration);
    }

    public static void TriggerEndRewardGlobal(float reducedRate = 20f, float duration = 0.2f)
    {
        if (_instance != null) _instance.TriggerEndReward(reducedRate, duration);
    }

    public void Configure(
        ParticleSystem ps,
        float baseRate,
        float burstRate,
        float burstDuration,
        int baseMaxParticles,
        int burstMaxParticles,
        float baseSpeed,
        float burstSpeed)
    {
        _ps = ps;
        _baseRate = baseRate;
        _burstRate = burstRate;
        _defaultDuration = burstDuration;
        _baseMaxParticles = baseMaxParticles;
        _burstMaxParticles = burstMaxParticles;
        _baseSpeed = baseSpeed;
        _burstSpeed = burstSpeed;
        _instance = this;
        ApplyBase();
    }

    void Trigger(float duration)
    {
        if (_ps == null) return;
        if (_endRewardCoroutine != null)
        {
            StopCoroutine(_endRewardCoroutine);
            _endRewardCoroutine = null;
        }
        if (_burstCoroutine != null) StopCoroutine(_burstCoroutine);
        float d = duration > 0f ? duration : _defaultDuration;
        _burstCoroutine = StartCoroutine(BurstRoutine(d));
    }

    void TriggerEndReward(float reducedRate, float duration)
    {
        if (_ps == null) return;
        if (_burstCoroutine != null)
        {
            StopCoroutine(_burstCoroutine);
            _burstCoroutine = null;
        }
        if (_endRewardCoroutine != null) StopCoroutine(_endRewardCoroutine);
        _endRewardCoroutine = StartCoroutine(EndRewardRoutine(reducedRate, duration));
    }

    IEnumerator BurstRoutine(float duration)
    {
        ApplyBurst();
        yield return new WaitForSeconds(duration);
        ApplyBase();
        _burstCoroutine = null;
    }

    IEnumerator EndRewardRoutine(float reducedRate, float duration)
    {
        ApplyReducedRate(reducedRate);
        yield return new WaitForSeconds(duration);
        ApplyBase();
        _endRewardCoroutine = null;
    }

    void ApplyBase()
    {
        if (_ps == null) return;
        var main = _ps.main;
        main.maxParticles = _baseMaxParticles;
        main.startSpeed = _baseSpeed;
        var em = _ps.emission;
        em.rateOverTime = _baseRate;
    }

    void ApplyBurst()
    {
        if (_ps == null) return;
        var main = _ps.main;
        main.maxParticles = _burstMaxParticles;
        main.startSpeed = _burstSpeed;
        var em = _ps.emission;
        em.rateOverTime = _burstRate;
    }

    void ApplyReducedRate(float reducedRate)
    {
        if (_ps == null) return;
        var main = _ps.main;
        main.maxParticles = _baseMaxParticles;
        main.startSpeed = _baseSpeed;
        var em = _ps.emission;
        em.rateOverTime = reducedRate;
    }
}

