using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Snow Panic 雪崩物理: 物理単位 cluster（3〜8個のピース群）。
/// 見た目=小キューブ / 物理=cluster。
/// </summary>
public class SnowCluster
{
    public int cluster_id;
    public readonly List<Transform> piece_list = new List<Transform>();
    public float support_value;
    public ClusterState weak_state = ClusterState.Stable;
    /// <summary>Weak Point: 叩くと連鎖崩壊しやすい</summary>
    public bool isWeakPoint;

    public enum ClusterState
    {
        Stable,
        Weak,
        Critical
    }

    public Vector3 Center
    {
        get
        {
            if (piece_list == null || piece_list.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (var t in piece_list)
            {
                if (t != null && t.gameObject.activeInHierarchy)
                {
                    sum += t.position;
                    n++;
                }
            }
            return n > 0 ? sum / n : piece_list[0] != null ? piece_list[0].position : Vector3.zero;
        }
    }

    public int ActivePieceCount
    {
        get
        {
            int n = 0;
            foreach (var t in piece_list)
            {
                if (t != null && t.gameObject.activeInHierarchy) n++;
            }
            return n;
        }
    }

    public void UpdateState(float thresholdStable, float thresholdWeak)
    {
        if (support_value > thresholdStable)
            weak_state = ClusterState.Stable;
        else if (support_value > thresholdWeak)
            weak_state = ClusterState.Weak;
        else
            weak_state = ClusterState.Critical;
    }
}
