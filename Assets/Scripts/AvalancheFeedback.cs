using System.Collections;
using UnityEngine;

/// <summary>雪崩開始時の共通フィードバック（微小カメラシェイク + 降雪ブースト）</summary>
public class AvalancheFeedback : MonoBehaviour
{
    static AvalancheFeedback _instance;
    Coroutine _shakeCoroutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInstance()
    {
        if (_instance != null) return;
        var go = new GameObject("AvalancheFeedback");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<AvalancheFeedback>();
    }

    public static void Trigger()
    {
        if (_instance == null) EnsureInstance();
        if (_instance == null) return;

        SnowfallEventBurst.TriggerGlobal(0.5f);
        _instance.StartTwoHitShake();
    }

    public static void Trigger(float shakeDuration, float shakeIntensity)
    {
        Trigger();
    }

    void StartTwoHitShake()
    {
        if (_shakeCoroutine != null) StopCoroutine(_shakeCoroutine);
        _shakeCoroutine = StartCoroutine(TwoHitShakeRoutine());
    }

    IEnumerator TwoHitShakeRoutine()
    {
        yield return ShakeRoutine(0.06f, 0.10f); // Hit 1
        yield return ShakeRoutine(0.08f, 0.15f); // Hit 2
        _shakeCoroutine = null;
    }

    IEnumerator ShakeRoutine(float duration, float intensity)
    {
        var cam = Camera.main;
        if (cam == null) yield break;

        Vector3 basePos = cam.transform.position;
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Clamp01(t / duration);
            Vector3 offset = Random.insideUnitSphere * intensity * k;
            cam.transform.position = basePos + offset;
            yield return null;
        }
        cam.transform.position = basePos;
    }
}

