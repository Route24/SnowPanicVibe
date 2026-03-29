using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SnowCore_AntiProtocol 専用の観測器。編集対象はこのファイルのみ。
///
/// 判定根拠（事実ベース）:
///   roof_visible             : Roof オブジェクトが activeInHierarchy かつ Renderer.enabled
///   cyan_box_visible         : CyanSnowBox が Start() 時点で activeInHierarchy かつ Renderer.enabled
///   score_ui_visible         : active Canvas 配下に SCORE を含む名前またはテキストを持つ表示物が存在する
///   ready_ui_visible         : active Canvas 配下に Ready を含む名前またはテキストを持つ表示物が存在する
///   click_hit_cyan_box       : NotifyCyanDestroyed() が呼ばれた（= Raycast が CyanSnowBox に命中した事実）
///   cyan_box_destroyed_on_click : クリック前に存在し、クリック後フレームに null になった事実
///   unexpected_respawn       : 破棄確認から 3 秒間 CyanSnowBox が再出現しなければ NO
/// </summary>
public sealed class AntiProtocolVisibilityReporter : MonoBehaviour
{
    [Tooltip("屋根オブジェクト（Roof）")]
    [SerializeField] private GameObject roofObject;

    [Tooltip("シアンボックスオブジェクト（CyanSnowBox）")]
    [SerializeField] private GameObject cyanBoxObject;

    // ── 内部フラグ ────────────────────────────────────────────────
    bool _cyanExistedAtStart;   // Start() 時点で CyanBox が存在したか
    bool _hitNotified;          // NotifyCyanDestroyed() が呼ばれたか（= click_hit 確定）
    bool _destroyConfirmed;     // 翌フレームで null を確認したか
    bool _respawnLogEmitted;    // unexpected_respawn ログを1回だけ出すガード

    // ── Start：全 bootstrap 完了後に可視状態をログ出力 ────────────
    private void Start()
    {
        bool roofVisible = IsVisible(roofObject);
        bool cyanVisible = IsVisible(cyanBoxObject);
        _cyanExistedAtStart = cyanVisible;

        bool hasScoreUi = false;
        bool hasReadyUi = false;

        // active な Canvas 配下の Text / TMP を検索
        // オブジェクト名またはテキスト内容に Score / Ready が含まれるか確認
        foreach (var canvas in FindObjectsOfType<Canvas>())
        {
            if (!canvas.gameObject.activeInHierarchy) continue;

            // Legacy Text
            foreach (var t in canvas.GetComponentsInChildren<Text>(false))
            {
                if (!t.gameObject.activeInHierarchy) continue;
                string name = t.gameObject.name;
                string text = t.text ?? "";
                if (ContainsCI(name, "Score") || ContainsCI(text, "SCORE")) hasScoreUi = true;
                if (ContainsCI(name, "Ready") || ContainsCI(text, "Ready"))  hasReadyUi = true;
            }

            // TextMeshProUGUI（リフレクション経由）
            var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
            if (tmpType != null)
            {
                foreach (Component c in canvas.GetComponentsInChildren(tmpType, false))
                {
                    if (c == null || !c.gameObject.activeInHierarchy) continue;
                    string name = c.gameObject.name;
                    string text = tmpType.GetProperty("text")?.GetValue(c) as string ?? "";
                    if (ContainsCI(name, "Score") || ContainsCI(text, "SCORE")) hasScoreUi = true;
                    if (ContainsCI(name, "Ready") || ContainsCI(text, "Ready"))  hasReadyUi = true;
                }
            }
        }

        Debug.Log("[AntiProtocolVis]"
            + " roof_visible="     + B(roofVisible)
            + " cyan_box_visible=" + B(cyanVisible)
            + " score_ui_visible=" + B(hasScoreUi)
            + " ready_ui_visible=" + B(hasReadyUi));
    }

    // ── NotifyCyanDestroyed：SnowBlockNode.OnHit() から呼ばれる ──
    // 「Raycast が CyanSnowBox に命中した」事実 = click_hit_cyan_box=YES
    // 「クリック後に消えた」確認 = cyan_box_destroyed_on_click
    public void NotifyCyanDestroyed()
    {
        if (_hitNotified) return;
        _hitNotified = true;
        // 翌フレームで null を確認 → cyan_box_destroyed_on_click を確定
        StartCoroutine(ConfirmDestroyNextFrame());
    }

    private IEnumerator ConfirmDestroyNextFrame()
    {
        yield return null; // Destroy() は同フレーム末実行 → 翌フレームで null になる

        // cyanBoxObject が null = Destroy 完了 = destroyed_on_click=YES
        _destroyConfirmed = cyanBoxObject == null;

        // SnowLoopNoaReportAutoCopy が受け取るログ形式に合わせて出力
        if (_destroyConfirmed)
        {
            Debug.Log("[SnowBlock] click_hit_cyan_box=YES cyan_box_destroyed=YES");
        }
        else
        {
            Debug.Log("[SnowBlock] click_hit_cyan_box=YES cyan_box_destroyed=NO");
        }

        // 3秒待って再出現がなければ unexpected_respawn=NO を確定
        if (_destroyConfirmed)
            StartCoroutine(CheckRespawnAfterDelay(3f));
        else
            EmitRespawnLog(false); // 消えていないなら respawn 判定不要
    }

    private IEnumerator CheckRespawnAfterDelay(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        if (_respawnLogEmitted) yield break;
        // 3秒後も null のまま = 再出現なし = unexpected_respawn=NO
        bool respawned = cyanBoxObject != null && cyanBoxObject.activeInHierarchy;
        EmitRespawnLog(respawned);
    }

    // ── OnDestroy：Stop 時の保険 ──────────────────────────────────
    private void OnDestroy()
    {
        if (_respawnLogEmitted) return;
        if (!_hitNotified)
        {
            // クリック自体なかった → respawn は発生しえない
            EmitRespawnLog(false);
        }
        else
        {
            bool cyanStillExists = cyanBoxObject != null && cyanBoxObject.activeInHierarchy;
            EmitRespawnLog(cyanStillExists);
        }
    }

    void EmitRespawnLog(bool respawned)
    {
        if (_respawnLogEmitted) return;
        _respawnLogEmitted = true;
        Debug.Log("[AntiProtocolVis] unexpected_respawn=" + B(respawned));
    }

    // ── ユーティリティ ────────────────────────────────────────────
    static bool IsVisible(GameObject go)
    {
        if (go == null) return false;
        if (!go.activeInHierarchy) return false;
        var r = go.GetComponent<Renderer>();
        return r == null || r.enabled;
    }

    static bool ContainsCI(string source, string keyword)
        => source != null && source.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0;

    static string B(bool v) => v ? "YES" : "NO";
}
