using UnityEngine;

/// <summary>Detached雪・タップの調査ログ。[DetachedSnowInfo][TapRaycastInfo]を1回ずつ出力。</summary>
public static class DetachedSnowDiagnostics
{
    static bool _chunkInfoLogged;
    static bool _fallingInfoLogged;
    static bool _tapInfoLogged;

    public static void LogChunkInfoIfFirst(MvpSnowChunkMotion c)
    {
        if (c == null || _chunkInfoLogged) return;
        _chunkInfoLogged = true;
        var col = c.GetComponent<Collider>();
        var rb = c.GetComponent<Rigidbody>();
        Debug.Log($"[DetachedSnowInfo] class=MvpSnowChunkMotion prefab=CreatePrimitive(Sphere) layer={LayerMask.LayerToName(c.gameObject.layer)} collider.enabled={(col != null && col.enabled)} rigidbody={(rb != null ? "kinematic=" + rb.isKinematic : "null")} managedList=DetachedSnowRegistry+_chunkPool");
    }

    public static void LogFallingInfoIfFirst(SnowPackFallingPiece f)
    {
        if (f == null || _fallingInfoLogged) return;
        _fallingInfoLogged = true;
        var col = f.GetComponent<Collider>();
        var rb = f.GetComponent<Rigidbody>();
        Debug.Log($"[DetachedSnowInfo] class=SnowPackFallingPiece prefab=SnowPackPiece layer={LayerMask.LayerToName(f.gameObject.layer)} collider.enabled={(col != null && col.enabled)} rigidbody={(rb != null ? "kinematic=" + rb.isKinematic : "null")} managedList=DetachedSnowRegistry");
    }

    public static void LogTapInfoIfFirst(LayerMask hitMask, string hitObject, string hitType)
    {
        if (_tapInfoLogged) return;
        _tapInfoLogged = true;
        Debug.Log($"[TapRaycastInfo] layerMask={hitMask.value} hitObject={hitObject} hitType={hitType}");
    }

    /// <summary>タップごとにRaycastヒットをログ（調査用）。</summary>
    public static void LogTapRaycast(LayerMask hitMask, string hitObject, string hitType)
    {
        Debug.Log($"[TapRaycastInfo] layerMask={hitMask.value} hitObject={hitObject} hitType={hitType}");
    }
}
