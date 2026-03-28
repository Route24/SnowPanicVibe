using UnityEditor;
using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// latest_summary.txt を読んで current_protocol.txt を自動生成する。
/// current_status.txt を WAITING_APPROVAL に更新する。
/// ゲームロジック・見た目・SAFE には一切触れない。
/// </summary>
public static class SummaryToProtocol
{
    static readonly string AutomationDir = Path.GetFullPath("Automation");

    static readonly string SummaryPath  = Path.Combine(AutomationDir, "latest_summary.txt");
    static readonly string ProtocolPath = Path.Combine(AutomationDir, "current_protocol.txt");
    static readonly string StatusPath   = Path.Combine(AutomationDir, "current_status.txt");

    // AssiAutoSummary から呼ばれるエントリポイント
    public static void RunAfterSummary()
    {
        try
        {
            SetStatus("SUMMARY_READY");
            string summary = File.Exists(SummaryPath) ? File.ReadAllText(SummaryPath) : "";
            string protocol = GenerateProtocol(summary);
            File.WriteAllText(ProtocolPath, protocol, new System.Text.UTF8Encoding(false));
            SetStatus("WAITING_APPROVAL");
            Debug.Log("[SummaryToProtocol] current_protocol.txt 生成完了 → " + ProtocolPath);
            Debug.Log("[SummaryToProtocol] current_status=WAITING_APPROVAL");
        }
        catch (Exception ex)
        {
            SetStatus("ERROR");
            Debug.LogWarning("[SummaryToProtocol] 失敗: " + ex.Message);
        }
    }

    [MenuItem("SnowPanic/Generate Protocol from Summary", false, 401)]
    public static void RunManual() => RunAfterSummary();

    /// <summary>アシが実装完了後に呼ぶ。READY_FOR_UNITY_PLAY に遷移する。</summary>
    [MenuItem("SnowPanic/Mark Ready for Unity Play", false, 402)]
    public static void MarkReadyForUnityPlay()
    {
        SetStatus("READY_FOR_UNITY_PLAY");
        Debug.Log("[SummaryToProtocol] status=READY_FOR_UNITY_PLAY → Play して確認してください");
    }

    // ---------------------------------------------------------------
    // プロトコル生成ロジック
    // ---------------------------------------------------------------

    static string GenerateProtocol(string summary)
    {
        string result        = Pick(summary, "result")              ?? "UNKNOWN";
        string compileResult = Pick(summary, "compile_result")      ?? "UNKNOWN";
        string errorCount    = Pick(summary, "error_count")         ?? "0";
        string snowState     = Pick(summary, "snow_state")          ?? "UNKNOWN";
        string underEave     = Pick(summary, "under_eave_stop")     ?? "UNKNOWN";
        string fallsStraight = Pick(summary, "falls_straight_down") ?? "UNKNOWN";
        string avalancheFeel = Pick(summary, "avalanche_feel")      ?? "UNKNOWN";
        string sceneName     = Pick(summary, "scene_name")          ?? "UNKNOWN";

        // --- 判定 ---

        // 1. Compile FAIL / error あり → 修正最優先
        if (compileResult != "PASS" || (int.TryParse(errorCount, out int ec) && ec > 0))
            return BuildProtocol_CompileFix(compileResult, errorCount, sceneName);

        // 2. under_eave_stop 未確認
        if (underEave == "UNKNOWN")
            return BuildProtocol_UnderEaveCheck(sceneName);

        // 3. falls_straight_down 未確認
        if (fallsStraight == "UNKNOWN")
            return BuildProtocol_FallsStraightCheck(sceneName);

        // 4. 真下落下が確認されている → 修正
        if (fallsStraight == "YES")
            return BuildProtocol_FixFallsStraight(sceneName);

        // 5. 軒下停止NG → 修正
        if (underEave == "NO")
            return BuildProtocol_FixUnderEave(sceneName);

        // 6. PASS かつ avalanche_feel が WEAK/NONE → 気持ちよさ調整（TL 1軒限定）
        if (result == "PASS" && (avalancheFeel == "WEAK" || avalancheFeel == "NONE"))
            return BuildProtocol_AvalancheFeel(sceneName);

        // 7. NEED_CHECK → 確認系
        if (result == "NEED_CHECK")
            return BuildProtocol_NeedCheck(snowState, underEave, fallsStraight, sceneName);

        // 8. PASS → 次の優先タスク
        return BuildProtocol_NextTask(sceneName);
    }

    // ---------------------------------------------------------------
    // プロトコルテンプレート
    // ---------------------------------------------------------------

    static string BuildProtocol_CompileFix(string compileResult, string errorCount, string scene)
    {
        return $@"SNOW PANIC - ASSI PROTOCOL

目的:
Compile エラーを修正して PASS 状態に戻す。

前提:
- compile_result={compileResult}
- error_count={errorCount}
- scene={scene}

今回触るモジュール:
- エラーが出ているスクリプト（Console で確認）

実装対象:
- Console のエラーを1件ずつ修正する
- 修正後 Compile PASS を確認する

完了条件:
- compile_result=PASS
- error_count=0

生成日時: {Now()}
";
    }

    static string BuildProtocol_UnderEaveCheck(string scene)
    {
        return $@"SNOW PANIC - ASSI PROTOCOL

目的:
軒下停止（under_eave_stop）が YES か NO かを確認する。
今回は確認のみ。修正は次のタスクにする。

前提:
- under_eave_stop=UNKNOWN
- scene={scene}

今回触るモジュール:
- なし（確認のみ）

実装対象:
- なし

確認手順:
1. Play する
2. 屋根をタップして雪を落とす
3. 雪が軒下で止まるか目視確認する
4. Stop → latest_summary.txt の under_eave_stop を確認

完了条件:
- under_eave_stop が YES / NO / PARTIAL のいずれかに更新される

生成日時: {Now()}
";
    }

    static string BuildProtocol_FallsStraightCheck(string scene)
    {
        return $@"SNOW PANIC - ASSI PROTOCOL

目的:
雪が真下に落ちているか（falls_straight_down）を確認する。
今回は確認のみ。修正は次のタスクにする。

前提:
- falls_straight_down=UNKNOWN
- scene={scene}

今回触るモジュール:
- なし（確認のみ）

実装対象:
- なし

確認手順:
1. Play する
2. 屋根をタップして雪を落とす
3. 雪が屋根沿いに滑るか、真下に落ちるかを目視確認する
4. Stop → latest_summary.txt の falls_straight_down を確認

完了条件:
- falls_straight_down が YES / NO のいずれかに更新される

生成日時: {Now()}
";
    }

    static string BuildProtocol_FixFallsStraight(string scene)
    {
        return $@"SNOW PANIC - ASSI PROTOCOL

目的:
雪が真下に落ちている問題を修正する。
屋根沿いの滑走を実現する。

前提:
- falls_straight_down=YES（真下落下を確認済み）
- scene={scene}

今回触るモジュール:
- SnowPackSpawner.cs（滑走方向・初速パラメータ）

禁止:
- SnowVisual 変更禁止
- UnderEaveLanding 変更禁止
- SAFE 変更禁止

実装対象:
- 屋根沿い方向への初速を強化する
- 重力適用タイミングを遅らせる

完了条件:
- falls_straight_down=NO になる
- 屋根沿いに滑る動きが目視で確認できる

生成日時: {Now()}
";
    }

    static string BuildProtocol_FixUnderEave(string scene)
    {
        return $@"SNOW PANIC - ASSI PROTOCOL

目的:
雪が軒下で止まらない問題を修正する。

前提:
- under_eave_stop=NO（軒下停止なしを確認済み）
- scene={scene}

今回触るモジュール:
- SnowPackSpawner.cs（軒下停止ロジック）

禁止:
- UnderEaveLanding 変更禁止
- SnowVisual 変更禁止
- SAFE 変更禁止

実装対象:
- 軒下エリアでの速度減衰・停止処理を確認・修正する

完了条件:
- under_eave_stop=YES になる
- 雪が軒下で一時停止する動きが目視で確認できる

生成日時: {Now()}
";
    }

    static string BuildProtocol_AvalancheFeel(string scene)
    {
        return $@"SNOW PANIC - ASSI PROTOCOL

目的:
雪崩の滑走気持ちよさを強化する。
TL（左上）1軒のみ対象。他の軒は触らない。

前提:
- result=PASS
- avalanche_feel=WEAK
- scene={scene}

今回触るモジュール:
- SnowPackSpawner.cs（SlideSpeed / Cascade パラメータのみ）

禁止:
- TL 以外の軒を変更しない
- SnowVisual 変更禁止
- SAFE 変更禁止
- 複数パラメータを同時に変えない

実装対象:
- SlideSpeed または Cascade を1つだけ調整する
- 変更前後の値を記録する

完了条件:
- avalanche_feel が GOOD / MEDIUM に改善する
- 他の軒に影響が出ていない

生成日時: {Now()}
";
    }

    static string BuildProtocol_NeedCheck(string snowState, string underEave, string fallsStraight, string scene)
    {
        return $@"SNOW PANIC - ASSI PROTOCOL

目的:
NEED_CHECK 状態の項目を目視確認する。
今回は確認のみ。修正は次のタスクにする。

前提:
- result=NEED_CHECK
- snow_state={snowState}
- under_eave_stop={underEave}
- falls_straight_down={fallsStraight}
- scene={scene}

今回触るモジュール:
- なし（確認のみ）

確認手順:
1. Play する
2. 屋根をタップして雪を落とす
3. 雪の挙動を目視確認する
4. Stop → latest_summary.txt を確認

完了条件:
- UNKNOWN 項目が YES / NO に更新される

生成日時: {Now()}
";
    }

    static string BuildProtocol_NextTask(string scene)
    {
        return $@"SNOW PANIC - ASSI PROTOCOL

目的:
現状 PASS。次の優先タスクを実行する。
SAFEは維持したまま、雪崩の気持ちよさをさらに強化する。

前提:
- result=PASS
- scene={scene}

今回触るモジュール:
- SnowPackSpawner.cs（TL 1軒のみ）

禁止:
- SAFE 変更禁止
- SnowVisual 変更禁止
- TL 以外の軒を変更しない

実装対象:
- TL 1軒の雪崩挙動をさらに自然に調整する

完了条件:
- 雪崩の滑走が自然に見える
- 他の軒に影響が出ていない

生成日時: {Now()}
";
    }

    // ---------------------------------------------------------------
    // ヘルパー
    // ---------------------------------------------------------------

    static string Pick(string text, string key)
    {
        var m = Regex.Match(text, @"(?m)^" + Regex.Escape(key) + @"=(.+)$");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    static void SetStatus(string status)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("status=" + status);
            sb.AppendLine("updated_at=" + Now());

            string existing = File.Exists(StatusPath) ? File.ReadAllText(StatusPath) : "";
            string lastSummary  = Pick(existing, "last_summary_at")  ?? "";
            string lastProtocol = Pick(existing, "last_protocol_at") ?? "";
            string lastApproved = Pick(existing, "last_approved_at") ?? "";

            if (status == "SUMMARY_READY")  lastSummary  = Now();
            if (status == "WAITING_APPROVAL") lastProtocol = Now();

            sb.AppendLine("last_summary_at="  + lastSummary);
            sb.AppendLine("last_protocol_at=" + lastProtocol);
            sb.AppendLine("last_approved_at=" + lastApproved);

            File.WriteAllText(StatusPath, sb.ToString(), new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[SummaryToProtocol] status更新失敗: " + ex.Message);
        }
    }
}
