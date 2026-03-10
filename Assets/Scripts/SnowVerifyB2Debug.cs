using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// B2-debug: 生成直後に total=0 になる理由を個別ログで特定する。
/// </summary>
public static class SnowVerifyB2Debug
{
    public static bool Enabled;
    public const int MaxPieceDetailLogs = 5;

    static readonly Dictionary<string, int> _discardReasonCounts = new Dictionary<string, int>();
    public static bool CleanupCalled;
    public static bool PoolReturnCalled;
    public static int InvalidSizeCount;
    public static int InvalidPositionCount;
    public static int InvalidParentCount;

    public static string ZeroTransitionStep;
    public static float LastTapTimeAtTestB;
    public static string ZeroTotalTriggerStep;

    /// <summary>PhaseB2: true で ClearSnowPack の破棄処理をスキップ（屋根残雪を消さない）。</summary>
    public static bool PauseCleanup;
    /// <summary>PhaseB2: true で ReturnToPool をスキップ（slideRoot 内にピースを残す）。</summary>
    public static bool PausePoolReturn;
    public static float LastPoolSkipLogTime = -10f;

    public static void RecordZeroTransition(string step)
    {
        if (!Enabled) return;
        if (string.IsNullOrEmpty(ZeroTransitionStep))
            ZeroTransitionStep = step;
    }

    public static void RecordTapForTestB(float tapTime)
    {
        if (!Enabled) return;
        LastTapTimeAtTestB = tapTime;
    }

    public static void RecordDiscard(string reason, string source)
    {
        if (!Enabled) return;
        var key = $"{reason}:{source}";
        if (!_discardReasonCounts.TryGetValue(key, out var c)) c = 0;
        _discardReasonCounts[key] = c + 1;
    }

    public static void RecordClip(int count)
    {
        if (!Enabled) return;
        var key = "ClipToRoofBounds";
        if (!_discardReasonCounts.TryGetValue(key, out var c)) c = 0;
        _discardReasonCounts[key] = c + count;
    }

    public static void RecordPoolReturn(string source)
    {
        if (!Enabled) return;
        RecordDiscard("PoolReturn", source);
    }

    public static void RecordCleanup()
    {
        if (!Enabled) return;
        RecordDiscard("Cleanup", "ClearSnowPack");
    }

    public static string GetDiscardReasonCountsString()
    {
        if (_discardReasonCounts.Count == 0) return "{}";
        var parts = new List<string>();
        foreach (var kv in _discardReasonCounts)
            parts.Add($"{kv.Key}={kv.Value}");
        return "{" + string.Join(",", parts) + "}";
    }

    public static void Reset()
    {
        _discardReasonCounts.Clear();
        CleanupCalled = false;
        PoolReturnCalled = false;
        InvalidSizeCount = 0;
        InvalidPositionCount = 0;
        InvalidParentCount = 0;
        ZeroTransitionStep = null;
        LastTapTimeAtTestB = -10f;
        ZeroTotalTriggerStep = null;
        PauseCleanup = false;
        PausePoolReturn = false;
        LastPoolSkipLogTime = -10f;
    }

    public static string FormatPieceState(int index, Transform t, bool active, bool pooled, string discardReason)
    {
        if (t == null) return $"piece_{index}=null";
        var lp = t.localPosition;
        var wp = t.position;
        var ls = t.localScale;
        var ws = t.lossyScale;
        string parentName = t.parent != null ? t.parent.name : "None";
        bool rendererEnabled = false;
        bool colliderEnabled = false;
        var r = t.GetComponentInChildren<Renderer>(true);
        if (r != null) rendererEnabled = r.enabled;
        var col = t.GetComponentInChildren<Collider>(true);
        if (col != null) colliderEnabled = col.enabled;
        return $"piece_{index} active={active} pooled={pooled} localPos=({lp.x:F3},{lp.y:F3},{lp.z:F3}) worldPos=({wp.x:F3},{wp.y:F3},{wp.z:F3}) localScale=({ls.x:F3},{ls.y:F3},{ls.z:F3}) worldScale=({ws.x:F3},{ws.y:F3},{ws.z:F3}) parent={parentName} rendererEnabled={rendererEnabled} colliderEnabled={colliderEnabled} discardReason={discardReason}";
    }
}
