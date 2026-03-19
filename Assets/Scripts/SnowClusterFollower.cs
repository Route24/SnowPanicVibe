using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 塊追従コンポーネント。
/// root ピース（SnowPackFallingPiece）の動きにオフセットを保ちながら追従する。
/// root が着地・Despawn したら自分も Despawn する。
/// </summary>
public class SnowClusterFollower : MonoBehaviour
{
    public SnowPackFallingPiece root;
    public SnowPackSpawner spawner;
    Vector3 _offset;
    bool _released;

    public void Init(SnowPackFallingPiece rootPiece, SnowPackSpawner sp)
    {
        root    = rootPiece;
        spawner = sp;
        _offset = transform.position - rootPiece.transform.position;
        _released = false;

        // Kinematic にして物理エンジンの干渉を排除
        var rb = GetComponent<Rigidbody>();
        if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }

        // Collider を無効化（root が代表で衝突判定）
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    void LateUpdate()
    {
        if (_released || root == null) return;

        // root が Despawn 済み（gameObject が null or 非アクティブ）なら自分も消える
        if (root.gameObject == null || !root.gameObject.activeInHierarchy)
        {
            Release("root_gone");
            return;
        }

        // root の位置 + 初期オフセットに追従
        transform.position = root.transform.position + _offset;
        transform.rotation = root.transform.rotation;
    }

    /// <summary>root から呼ばれる。フォロワーを解放してプールに戻す。</summary>
    public void Release(string reason)
    {
        if (_released) return;
        _released = true;

        var rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
        Destroy(this);

        if (spawner != null && spawner.gameObject != null)
            spawner.ReturnToPoolFromFalling(transform, "ClusterFollower_" + reason);
    }
}
