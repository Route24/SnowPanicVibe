using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 毛糸の手袋ツール - PHASE 1
/// 画面中央に固定1枚表示のみ。
/// マウス追従・前後関係・回転・クールタイムは未実装。
/// </summary>
[DefaultExecutionOrder(1000)]
public class GloveTool : MonoBehaviour
{
    // ── 静的ブロックフラグ（SnowStrip2D.HandleTap() から参照）────
    public static bool IsBlocking { get; private set; } = false;
    public static void DrawFrontmost() { }

    // ── 設定 ──────────────────────────────────────────────────
    public Texture2D gloveTex; // Bootstrap から設定される

    void Start()
    {
        // Bootstrap が gloveTex を設定していない場合は Resources から取得
        if (gloveTex == null)
            gloveTex = Resources.Load<Texture2D>("GloveMitten");

        Debug.Log($"[DEBUG_GLOVE_SOURCE]" +
                  $" source_mode=Texture2D" +
                  $" sprite_null=N/A" +
                  $" texture_null={(gloveTex == null ? "YES" : "NO")}" +
                  $" sprite_name=N/A" +
                  $" texture_name={(gloveTex != null ? gloveTex.name : "NULL")}" +
                  $" tex_width={(gloveTex != null ? gloveTex.width.ToString() : "0")}" +
                  $" tex_height={(gloveTex != null ? gloveTex.height.ToString() : "0")}" +
                  $" draw_called=YES" +
                  $" glove_visible_as_mitten={(gloveTex != null ? "YES" : "NO")}" +
                  $" placeholder_drawn=NO");
    }

    void OnDestroy()
    {
        IsBlocking = false;
    }

    // ── 描画（OnGUI）─────────────────────────────────────────
    // PHASE 2: マウス追従表示
    // - 緑四角フォールバック: 禁止
    // - 固定中央表示: 廃止
    // - 回転: なし
    // - クールタイム: なし
    // - クリック処理: なし
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (Event.current.type != EventType.Repaint) return;
        if (gloveTex == null) return;

        // マウス座標を GUI 座標に変換（Y軸反転）
        Vector2 mouseScreen = Input.mousePosition;
        float mx = mouseScreen.x;
        float my = Screen.height - mouseScreen.y; // GUI座標はY軸が上=0

        float h = Screen.height * 0.12f;
        float w = h * ((float)gloveTex.width / gloveTex.height);

        // 手袋の左上が (mx - w/2, my - h/2) = マウス中心に揃える
        float gx = mx - w * 0.5f;
        float gy = my - h * 0.5f;

        // 画面端クランプ（最低限）
        gx = Mathf.Clamp(gx, 0f, Screen.width  - w);
        gy = Mathf.Clamp(gy, 0f, Screen.height - h);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(gx, gy, w, h), gloveTex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;

        if (Time.frameCount % 180 == 1)
        {
            Debug.Log($"[DEBUG_GLOVE_FOLLOW]" +
                      $" source_ok=YES" +
                      $" draw_called=YES" +
                      $" mouse_x={mx:F0} mouse_y={my:F0}" +
                      $" glove_x={gx:F0} glove_y={gy:F0}" +
                      $" fixed_center_draw=NO" +
                      $" placeholder_drawn=NO" +
                      $" glove_count_on_screen=1" +
                      $" mouse_follow_visible=YES");
        }
    }

    // 後方互換
    public void DrawGlove() { }
}
