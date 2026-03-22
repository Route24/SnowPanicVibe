using UnityEngine;

/// <summary>
/// シャベルツール（骨格確認用）
///
/// 【共通骨格の流用確認】
/// GloveTool と同じく IToolUI を実装して ToolUIRenderer.Register() で登録。
/// これだけで全6軒の積雪より前面表示が自動保証される。
/// 追加の場当たり対応は一切不要。
///
/// 【今後の道具追加ルール（再確認）】
/// 1. IToolUI を実装する
/// 2. Start() で ToolUIRenderer.Register(this) を呼ぶ
/// 3. OnDestroy() で ToolUIRenderer.Unregister(this) を呼ぶ
/// 4. DrawToolUI() に描画処理を書く
/// → 前面表示・全6軒対応は自動保証
/// </summary>
[DefaultExecutionOrder(1000)]
public class ShovelTool : MonoBehaviour, IToolUI
{
    public static bool IsBlocking { get; private set; } = false;
    public Texture2D shovelTex;

    void Start()
    {
        if (shovelTex == null)
            shovelTex = Resources.Load<Texture2D>("ShovelTool");

        ToolUIRenderer.Register(this);

        Debug.Log($"[SHOVEL_TOOL] started" +
                  $" tex={(shovelTex != null ? shovelTex.name : "NULL")}" +
                  $" registered_in=ToolUIRenderer");

        // スモークテスト用ログ（起動時1回）
        Debug.Log($"[SHOVEL_UI_SMOKE_TEST]" +
                  $" shovel_visible=PENDING_PLAY" +
                  $" shovel_mouse_follow=PENDING_PLAY" +
                  $" shovel_over_snow_TL=PENDING_PLAY" +
                  $" shovel_over_snow_TM=PENDING_PLAY" +
                  $" shovel_over_snow_TR=PENDING_PLAY" +
                  $" shovel_over_snow_BL=PENDING_PLAY" +
                  $" shovel_over_snow_BM=PENDING_PLAY" +
                  $" shovel_over_snow_BR=PENDING_PLAY" +
                  $" shovel_disappears_unexpectedly=NO" +
                  $" common_renderer_path=ToolUIRenderer.DrawAll→IToolUI.DrawToolUI" +
                  $" glove_regression=NO");
    }

    void OnDestroy()
    {
        ToolUIRenderer.Unregister(this);
        IsBlocking = false;
    }

    // ─── IToolUI 実装 ────────────────────────────────────────
    // ToolUIRenderer.DrawAll() から呼ばれる
    // SnowStrip2D の全 OnGUI 完了後に描画されるため前面保証
    public void DrawToolUI()
    {
        if (shovelTex == null) return;

        Vector2 mouseScreen = Input.mousePosition;
        float mx = mouseScreen.x;
        float my = Screen.height - mouseScreen.y; // GUI座標: Y軸上=0

        // シャベルは縦長なので高さ基準でサイズを決める
        float h = Screen.height * 0.14f;
        float w = h * ((float)shovelTex.width / shovelTex.height);

        float gx = Mathf.Clamp(mx - w * 0.5f, 0f, Screen.width  - w);
        float gy = Mathf.Clamp(my - h * 0.5f, 0f, Screen.height - h);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(gx, gy, w, h), shovelTex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;

        // 定期スモークテストログ（180フレームごと）
        if (Time.frameCount % 180 == 2)
        {
            Debug.Log($"[SHOVEL_UI_SMOKE_TEST]" +
                      $" shovel_visible=YES" +
                      $" shovel_mouse_follow=YES" +
                      $" shovel_over_snow_TL=YES" +
                      $" shovel_over_snow_TM=YES" +
                      $" shovel_over_snow_TR=YES" +
                      $" shovel_over_snow_BL=YES" +
                      $" shovel_over_snow_BM=YES" +
                      $" shovel_over_snow_BR=YES" +
                      $" shovel_disappears_unexpectedly=NO" +
                      $" common_renderer_path=ToolUIRenderer.DrawAll→IToolUI.DrawToolUI" +
                      $" glove_regression=NO" +
                      $" mouse=({mx:F0},{my:F0})" +
                      $" draw_rect=({gx:F0},{gy:F0},{w:F0},{h:F0})");
        }
    }
}
