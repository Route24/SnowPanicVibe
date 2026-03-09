using UnityEngine;
using System.Collections.Generic;

/// <summary>ASSI: 軒先直下のキャッチゾーン。雪塊が入ると applyDuration 秒だけ余分なドラッグを適用</summary>
public class EavesCatchZone : MonoBehaviour
{
    public float dragMultiplier = 0.92f;
    public float applyDuration = 0.3f;
    readonly Dictionary<SnowClump, float> _enterTime = new Dictionary<SnowClump, float>();

    void OnTriggerEnter(Collider other)
    {
        var clump = other.GetComponent<SnowClump>();
        if (clump != null && clump.IsFallingState() && !_enterTime.ContainsKey(clump))
            _enterTime[clump] = Time.time;
    }

    void OnTriggerStay(Collider other)
    {
        var clump = other.GetComponent<SnowClump>();
        if (clump == null || !clump.IsFallingState()) return;
        if (!_enterTime.TryGetValue(clump, out float t0)) return;
        if (Time.time - t0 > applyDuration) return;

        var rb = clump.GetComponent<Rigidbody>();
        if (rb == null || rb.isKinematic) return;
        rb.linearVelocity *= dragMultiplier;
    }

    void OnTriggerExit(Collider other)
    {
        var clump = other.GetComponent<SnowClump>();
        if (clump != null) _enterTime.Remove(clump);
    }
}
