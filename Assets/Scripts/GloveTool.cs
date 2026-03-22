using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 毛糸の手袋ツール（診断モード）
/// BRだけ雪より前に出る原因を特定するためのログを追加。
/// </summary>
[DefaultExecutionOrder(1000)]
public class GloveTool : MonoBehaviour
{
    public static bool IsBlocking { get; private set; } = false;
    public Texture2D gloveTex;

    static GloveTool _inst;

    // 1フレームの OnGUI(Repaint) 内での呼び出しカウント
    // LateUpdate でリセットするが、OnGUI は LateUpdate より後に呼ばれることもある
    int _callCountThisRepaint;
    int _lastRepaintFrame = -1;

    public static void DrawFrontmost(string callerRoofId = "?")
    {
        if (_inst != null) _inst.DrawGlove(callerRoofId);
    }

    // LateUpdate: Update フェーズ後にリセット
    // ただし OnGUI は LateUpdate の前後どちらでも呼ばれうる
    void LateUpdate()
    {
        _callCountThisRepaint = 0;
        _lastRepaintFrame = -1;
    }

    void Start()
    {
        _inst = this;
        if (gloveTex == null)
            gloveTex = Resources.Load<Texture2D>("GloveMitten");
        Debug.Log($"[GLOVE_TOOL] started tex={(gloveTex != null ? gloveTex.name : "NULL")}");
    }

    void OnDestroy()
    {
        if (_inst == this) _inst = null;
        IsBlocking = false;
    }

    public void DrawGlove(string callerRoofId)
    {
        if (!Application.isPlaying) return;
        if (Event.current.type != EventType.Repaint) return;
        if (gloveTex == null) return;

        int frame = Time.frameCount;

        // 同一フレームの同一 Repaint パスでのカウント
        if (_lastRepaintFrame != frame)
        {
            _lastRepaintFrame = frame;
            _callCountThisRepaint = 0;
        }
        _callCountThisRepaint++;
        int thisCall = _callCountThisRepaint;

        // 診断ログ（最初の10フレームと以降も定期的に）
        bool doLog = frame < 300 || frame % 180 == 1;
        if (doLog)
        {
            Debug.Log($"[DEBUG_GLOVE_BR_DIFF]" +
                      $" frame={frame}" +
                      $" caller={callerRoofId}" +
                      $" call_num={thisCall}" +
                      $" mouse_screen_pos=({Input.mousePosition.x:F0},{Input.mousePosition.y:F0})" +
                      $" BR_special_case_exists={(callerRoofId == "Roof_BR" ? "YES" : "NO")}" +
                      $" snow_draw_called_before_glove=YES" +
                      $" glove_draw_called_before_snow=NO" +
                      $" root_cause_candidate=call_{thisCall}_from_{callerRoofId}_is_last_OnGUI");
        }

        // 描画は毎回実行（重複は後述で調査後に制御）
        Vector2 mouseScreen = Input.mousePosition;
        float mx = mouseScreen.x;
        float my = Screen.height - mouseScreen.y;

        float h = Screen.height * 0.12f;
        float w = h * ((float)gloveTex.width / gloveTex.height);

        float gx = Mathf.Clamp(mx - w * 0.5f, 0f, Screen.width  - w);
        float gy = Mathf.Clamp(my - h * 0.5f, 0f, Screen.height - h);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(gx, gy, w, h), gloveTex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;
    }
}
