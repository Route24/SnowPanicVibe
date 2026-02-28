using UnityEngine;

/// <summary>軒先のトリガー。雪塊が入ったら屋根との衝突を無視して落下させる</summary>
public class EavesDropTrigger : MonoBehaviour
{
    public Collider roofCollider;

    void OnTriggerEnter(Collider other)
    {
        var clump = other.GetComponent<SnowClump>();
        if (clump == null || clump.HasLanded) return;

        clump.ForceDropFromEaves(roofCollider);
    }
}
