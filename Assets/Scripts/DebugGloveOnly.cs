using UnityEngine;

/// <summary>
/// デバッグ用: 画面中央に手袋を1枚だけ固定表示する最小スクリプト。
///
/// - OnGUI のみ（SpriteRenderer 不使用）
/// - 画像ソース: Resources.Load("GloveMitten") 1本のみ
/// - 位置: 画面中央固定
/// - サイズ: 画面高さ × 0.12、アスペクト比維持
/// - 回転なし / マウス追従なし / クールタイムなし
/// </summary>
[DefaultExecutionOrder(2000)]
public class DebugGloveOnly : MonoBehaviour
{
    [Tooltip("手袋テクスチャ。Bootstrap または Start で Resources から設定される")]
    public Texture2D gloveTex;

    void Start()
    {
        // GloveTool.cs がマウス追従を担当するため DebugGloveOnly は無効化
        enabled = false;
        Debug.Log("[DEBUG_GLOVE_ONLY] disabled – GloveTool handles mouse follow");
    }

    Texture2D GetTex()
    {
        if (gloveTex != null) return gloveTex;

        // 緑フォールバック（テクスチャが絶対にnullにならない保証）
        var fb = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var col = new Color(84f/255f, 252f/255f, 64f/255f, 1f);
        var px  = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = col;
        fb.SetPixels(px);
        fb.Apply();
        return fb;
    }

    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (Event.current.type != EventType.Repaint) return;

        Texture2D tex = GetTex();

        // サイズ: 画面高さの 12%、アスペクト比維持
        float h = Screen.height * 0.12f;
        float w = h * ((float)tex.width / tex.height);

        // 位置: 画面中央
        float x = (Screen.width  - w) * 0.5f;
        float y = (Screen.height - h) * 0.5f;

        var drawRect = new Rect(x, y, w, h);

        GUI.color = Color.white;
        GUI.DrawTexture(drawRect, tex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;

        if (Time.frameCount % 180 == 1)
        {
            bool isReal = gloveTex != null;
            Debug.Log($"[DEBUG_GLOVE_ONLY]" +
                      $" script_attached=YES" +
                      $" glove_texture_loaded={(isReal ? "YES" : "NO(fallback)")}" +
                      $" render_method=OnGUI" +
                      $" visible_on_screen=YES" +
                      $" position=center_fixed" +
                      $" draw_rect=({drawRect.x:F0},{drawRect.y:F0},{drawRect.width:F0},{drawRect.height:F0})" +
                      $" size_px=({w:F0}x{h:F0})" +
                      $" glove_count=1" +
                      $" GloveTool_enabled=NO" +
                      $" fallback_green_box_used={(isReal ? "NO" : "YES")}");
        }
    }
}
