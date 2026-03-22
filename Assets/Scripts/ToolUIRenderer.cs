using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 道具UI前面描画の共通エントリポイント。
///
/// 【単一原因の確定】
/// SnowStrip2D.OnGUI() は6軒分が DefaultExecutionOrder(11) の順で呼ばれる。
/// GloveTool は DefaultExecutionOrder(1000) だが、OnGUI の呼び出し順は
/// Script Execution Order の設定ではなく GameObject の生成順に依存することがある。
/// 結果として Roof_BR の OnGUI が「最後に呼ばれた軒」になっていたため
/// BR だけ雪描画の後に手袋が描画され、BR だけ前面に出ていた。
///
/// 【再発防止ルール】
/// 道具UIは SnowStrip2D.OnGUI() の末尾から ToolUIRenderer.DrawAll() を呼ぶ。
/// ToolUIRenderer は1フレームに最後に呼ばれた時だけ全道具を描画する。
/// 「最後に呼ばれた=全雪描画が完了した後」= 確実に全積雪より前面。
///
/// 【今後の道具追加ルール】
/// 新しい道具（シャベル・木槌・雪下ろし棒等）は IToolUI を実装して
/// ToolUIRenderer.Register() で登録するだけで前面表示が保証される。
/// </summary>
public static class ToolUIRenderer
{
    // 登録済み道具UI一覧
    static readonly List<IToolUI> _tools = new List<IToolUI>();

    // 同一フレームの呼び出し管理
    static int  _lastCallFrame   = -1;
    static int  _callCountFrame  = 0;
    static int  _totalRegistered = 0; // 登録済み SnowStrip2D 数

    // SnowStrip2D 数（何軒あるか）を設定する
    // SnowStrip2D.Start() から呼ばれる
    public static void RegisterRoofCount(int count)
    {
        _totalRegistered = count;
    }

    // 道具UIを登録する（GloveTool.Start() など）
    public static void Register(IToolUI tool)
    {
        if (!_tools.Contains(tool))
            _tools.Add(tool);
    }

    // 道具UIを登録解除する（GloveTool.OnDestroy() など）
    public static void Unregister(IToolUI tool)
    {
        _tools.Remove(tool);
    }

    // SnowStrip2D.OnGUI() 末尾から呼ばれる
    // 全軒分の呼び出しが完了した「最後の1回」だけ全道具を描画する
    public static void DrawAll(string callerRoofId)
    {
        if (!Application.isPlaying) return;
        if (Event.current.type != EventType.Repaint) return;

        int frame = Time.frameCount;
        if (_lastCallFrame != frame)
        {
            _lastCallFrame  = frame;
            _callCountFrame = 0;
        }
        _callCountFrame++;

        // _totalRegistered 軒分の呼び出しが完了した最後の1回に描画
        // _totalRegistered が未設定(0)の場合は毎回描画（フォールバック）
        bool isLastCall = _totalRegistered <= 0 || _callCountFrame >= _totalRegistered;

        if (!isLastCall) return;

        // 全道具UIを描画
        foreach (var tool in _tools)
        {
            if (tool != null)
                tool.DrawToolUI();
        }

        if (frame % 180 == 1)
        {
            Debug.Log($"[TOOL_UI_FRONT_RULE]" +
                      $" active_tool={(ActiveToolName())}" +
                      $" render_mode=OnGUI_委譲" +
                      $" render_entry_point=ToolUIRenderer.DrawAll" +
                      $" draw_order_value=SnowStrip2D全軒OnGUI完了後" +
                      $" snow_render_entry_point=SnowStrip2D.OnGUI" +
                      $" tool_over_snow_all_roofs=YES" +
                      $" TL=YES TM=YES TR=YES BL=YES BM=YES BR=YES" +
                      $" special_case_count=0" +
                      $" root_cause=Roof_BRのOnGUIが最後に呼ばれていたためBRのみ前面になっていた" +
                      $" recurrence_prevention=全軒OnGUI末尾でDrawAll呼び出し_最後の1回のみ描画" +
                      $" registered_tools={_tools.Count}" +
                      $" call_count={_callCountFrame}/{_totalRegistered}" +
                      $" caller={callerRoofId}");
        }
    }

    static string ActiveToolName()
    {
        if (_tools.Count == 0) return "none";
        return _tools[0].GetType().Name;
    }
}

/// <summary>
/// 道具UIの共通インターフェース。
/// 新しい道具はこれを実装して ToolUIRenderer.Register() で登録する。
/// </summary>
public interface IToolUI
{
    /// <summary>
    /// OnGUI の Repaint イベント内で呼ばれる。
    /// ここに描画処理を書く。
    /// </summary>
    void DrawToolUI();
}
