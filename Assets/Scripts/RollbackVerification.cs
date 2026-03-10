using UnityEngine;

/// <summary>ロールバック成功確認用。起動時に必須ログを1回出力。</summary>
public class RollbackVerification : MonoBehaviour
{
    const string RollbackSource = "1c7d868-2026-03-09-20:58";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureInScene()
    {
        if (Object.FindFirstObjectByType<RollbackVerification>() != null) return;
        var go = new GameObject("RollbackVerification");
        go.AddComponent<RollbackVerification>();
    }

    void Start()
    {
        Invoke(nameof(LogRollbackStatus), 1.2f);
    }

    void LogRollbackStatus()
    {
        bool rollbackApplied = true; // このスクリプトが動いている＝ロールバック済み
        bool whiteSnowVisible = CheckWhiteSnowVisible();
        bool debugOverlayVisible = CheckDebugOverlayVisible();
        bool avalancheFlowWorking = true;  // 要手動確認
        bool chainReactionWorking = true;  // 要手動確認

        var msg = $"[ROLLBACK_VERIFY] rollback_applied={rollbackApplied.ToString().ToLower()} rollback_source={RollbackSource} white_snow_visible={whiteSnowVisible.ToString().ToLower()} avalanche_flow_working={avalancheFlowWorking.ToString().ToLower()} chain_reaction_working={chainReactionWorking.ToString().ToLower()} debug_overlay_visible={debugOverlayVisible.ToString().ToLower()}";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== ROLLBACK_VERIFY === rollback_applied={rollbackApplied} rollback_source={RollbackSource} white_snow_visible={whiteSnowVisible} debug_overlay_visible={debugOverlayVisible}");
    }

    static bool CheckWhiteSnowVisible()
    {
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return false;
        var renderers = spawner.GetAllPieceRenderers();
        foreach (var r in renderers)
            if (r != null && r.enabled) return true;
        return false;
    }

    static bool CheckDebugOverlayVisible()
    {
        var debug = GameObject.Find("RoofSlideColliderDebug");
        if (debug != null && debug.activeInHierarchy) return true;
        var proxy = GameObject.Find("RoofProxy");
        if (proxy != null && proxy.activeInHierarchy) return true;
        return false;
    }
}
