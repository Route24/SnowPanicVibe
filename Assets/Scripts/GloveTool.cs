using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 毛糸の手袋ツール
///
/// 描画: ToolUIRenderer に IToolUI として登録。
///       SnowStrip2D.OnGUI() 末尾 → ToolUIRenderer.DrawAll() → DrawToolUI() の経路で
///       全積雪描画完了後に描画されるため、全6軒で確実に前面表示される。
///
/// 新しい道具（シャベル等）も同じく IToolUI を実装して
/// ToolUIRenderer.Register() で登録すれば前面表示が自動保証される。
/// </summary>
[DefaultExecutionOrder(1000)]
public class GloveTool : MonoBehaviour, IToolUI
{
    public static bool IsBlocking { get; private set; } = false;
    public Texture2D gloveTex;

    // 後方互換（SnowStrip2D が呼ぶ場合のフォールバック）
    public static void DrawFrontmost(string callerRoofId = "?")
    {
        ToolUIRenderer.DrawAll(callerRoofId);
    }

    void Start()
    {
        if (gloveTex == null)
            gloveTex = Resources.Load<Texture2D>("GloveMitten");

        // ToolUIRenderer に登録 → 全道具共通の前面描画パスに乗る
        ToolUIRenderer.Register(this);

        Debug.Log($"[GLOVE_TOOL] started" +
                  $" tex={(gloveTex != null ? gloveTex.name : "NULL")}" +
                  $" registered_in=ToolUIRenderer");
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
        if (gloveTex == null) return;

        Vector2 mouseScreen = Input.mousePosition;
        float mx = mouseScreen.x;
        float my = Screen.height - mouseScreen.y; // GUI座標: Y軸上=0

        float h = Screen.height * 0.12f;
        float w = h * ((float)gloveTex.width / gloveTex.height);

        float gx = Mathf.Clamp(mx - w * 0.5f, 0f, Screen.width  - w);
        float gy = Mathf.Clamp(my - h * 0.5f, 0f, Screen.height - h);

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(gx, gy, w, h), gloveTex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;
    }

    // 後方互換（呼び出し元が残っていても無害）
    public void DrawGlove(string callerRoofId = "?") { }
}
