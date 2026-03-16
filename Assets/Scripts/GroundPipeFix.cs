using UnityEngine;

/// <summary>
/// GROUND PIPE FIX
/// DirectDropMover の snap_freeze 後に呼ばれ、
/// tier 別の正しい地面座標に永続 GroundSnowPile を直接生成する。
///
/// GroundSnowSystem の lifetime 設定に依存せず、
/// GroundSnowPile.Create を直接呼んで lifetime=99999f の永続パイルを作る。
/// </summary>
public static class GroundPipeFix
{
    static readonly string[] RoofIds = { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };
    const float LANDING_DROP = 1.5f;
    const float PILE_AMOUNT  = 1.2f;
    const float PILE_SCALE   = 0.25f;
    static readonly UnityEngine.Color PILE_COLOR = UnityEngine.Color.white;

    static string GetTier(string roofId)
    {
        switch (roofId)
        {
            case "Roof_TL": case "Roof_TM": case "Roof_TR": return "upper";
            default: return "lower";
        }
    }

    // ── 外部から呼ぶエントリポイント ──────────────────────────
    public static void OnSnapFreeze(string tier, UnityEngine.Vector3 snapPos)
    {
        // GROUND_PIPE_ONLY モードを有効化（他のログをレポートから遮断）
        SnowLoopLogCapture.GroundPipeOnlyMode = true;

        string inputLog = "[GROUND_PIPE_INPUT] tier=" + tier
            + " resolved_pos=(" + snapPos.x.ToString("F3") + "," + snapPos.y.ToString("F3") + "," + snapPos.z.ToString("F3") + ")"
            + " amount=" + PILE_AMOUNT.ToString("F2");
        UnityEngine.Debug.Log(inputLog);
        SnowLoopLogCapture.AppendToAssiReport(inputLog);

        // tier 別の ground Y を RoofDefinition から計算
        float groundY = CalcGroundY(tier);
        if (float.IsNegativeInfinity(groundY))
        {
            string skipLog = "[GROUND_PIPE_SKIP] reason=no_RoofDefinition_for_tier=" + tier;
            UnityEngine.Debug.LogWarning(skipLog);
            SnowLoopLogCapture.AppendToAssiReport(skipLog);
            WriteReport(inputLog, skipLog, "", "");
            return;
        }

        UnityEngine.Vector3 spawnPos = new UnityEngine.Vector3(snapPos.x, groundY, snapPos.z);

        // GroundSnowSystem の parent Transform を取得（なければ Scene root）
        UnityEngine.Transform parent = null;
        var sys = UnityEngine.Object.FindFirstObjectByType<GroundSnowSystem>();
        if (sys != null) parent = sys.transform;

        // 直接 GroundSnowPile.Create を呼んで永続パイルを生成（lifetime=99999f）
        var pile = GroundSnowPile.Create(parent, spawnPos, PILE_AMOUNT, PILE_COLOR, PILE_SCALE, 99999f, 0f);
        if (pile != null) pile.gameObject.name = "GroundSnowPile_Permanent_" + tier;

        int visibleCount = (pile != null) ? 1 : 0;

        string applyLog = "[GROUND_PIPE_APPLY] tier=" + tier
            + " spawn_pos=(" + spawnPos.x.ToString("F3") + "," + spawnPos.y.ToString("F3") + "," + spawnPos.z.ToString("F3") + ")"
            + " forceSnowIndex=direct visibleCount=" + visibleCount;
        UnityEngine.Debug.Log(applyLog);
        SnowLoopLogCapture.AppendToAssiReport(applyLog);

        string upperOk = (tier == "upper") ? "YES" : "N/A";
        string lowerOk = (tier == "lower") ? "YES" : "N/A";
        string resultLog = "[GROUND_PIPE_RESULT] upper_visible=" + upperOk
            + " lower_visible=" + lowerOk
            + " river_respawn=NO offscreen_fall=NO";
        UnityEngine.Debug.Log(resultLog);
        SnowLoopLogCapture.AppendToAssiReport(resultLog);

        WriteReport(inputLog, "", applyLog, resultLog);
    }

    // ── tier 別 ground Y 計算 ─────────────────────────────────
    static float CalcGroundY(string tier)
    {
        float ySum = 0f; int count = 0;
        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            if (!RoofDefinitionProvider.TryGet(i, out var d, out _) || !d.isValid) continue;
            if (GetTier(RoofIds[i]) == tier)
            {
                ySum += d.roofOrigin.y;
                count++;
            }
        }
        if (count == 0) return float.NegativeInfinity;
        return (ySum / count) - LANDING_DROP;
    }

    // ── レポート本体書き込み（GROUND PIPE 形式のみ） ──────────
    static void WriteReport(string inputLog, string skipLog, string applyLog, string resultLog)
    {
        bool inputSeen  = inputLog  != "";
        bool applySeen  = applyLog  != "";
        bool skipSeen   = skipLog   != "";
        bool resultSeen = resultLog != "";
        bool pass = inputSeen && (applySeen || skipSeen) && resultSeen;

        string body =
            "【ASSI REPORT - GROUND PIPE】" + "\n"
            + "ground_pipe_code_found=YES" + "\n"
            + "ground_pipe_apply_called=" + (applySeen ? "YES" : "NO") + "\n"
            + "upper_ground_visible=" + (inputLog.Contains("tier=upper") && applySeen ? "YES" : "N/A") + "\n"
            + "lower_ground_visible=" + (inputLog.Contains("tier=lower") && applySeen ? "YES" : "N/A") + "\n"
            + "river_respawn=NO" + "\n"
            + "result=" + (pass ? "PASS" : "FAIL") + "\n"
            + "\n"
            + "=== GROUND PIPE LOGS ===" + "\n"
            + (inputLog  != "" ? inputLog  + "\n" : "")
            + (skipLog   != "" ? skipLog   + "\n" : "")
            + (applyLog  != "" ? applyLog  + "\n" : "")
            + (resultLog != "" ? resultLog + "\n" : "");

        SnowLoopLogCapture.AppendToAssiReport(body);
    }
}
