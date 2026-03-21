using UnityEngine;

/// <summary>
/// デバッグ用: 画面中央に手袋を1枚だけ固定表示する最小スクリプト。
///
/// - OnGUI のみ（SpriteRenderer 不使用）
/// - 位置: 画面中央固定
/// - サイズ: 画面高さ × 0.12、アスペクト比維持
/// - 回転なし / マウス追従なし / クールタイムなし
/// - 雪との前後関係なし
///
/// この表示が確認できたら次フェーズへ進む。
/// </summary>
[DefaultExecutionOrder(2000)] // 全スクリプトより後に描画
public class DebugGloveOnly : MonoBehaviour
{
    [Tooltip("手袋テクスチャ。null の場合は緑の単色フォールバック")]
    public Texture2D gloveTex;

    Texture2D _fallback;

    void Start()
    {
        // Bootstrap が GloveMitten を渡せるよう Resources からも試みる
        if (gloveTex == null)
            gloveTex = Resources.Load<Texture2D>("GloveMitten");

        Debug.Log($"[DEBUG_GLOVE_ONLY] script_attached=YES" +
                  $" glove_texture_loaded={(gloveTex != null ? "YES" : "NO (will use fallback)")}" +
                  $" render_method=OnGUI" +
                  $" position=center_fixed" +
                  $" GloveTool_enabled=NO");
    }

    Texture2D GetTex()
    {
        if (gloveTex != null) return gloveTex;
        if (_fallback != null) return _fallback;

        // 緑フォールバック
        _fallback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        var col = new Color(84f/255f, 252f/255f, 64f/255f, 1f);
        var px  = new Color[64 * 64];
        for (int i = 0; i < px.Length; i++) px[i] = col;
        _fallback.SetPixels(px);
        _fallback.Apply();
        Debug.LogWarning("[DEBUG_GLOVE_ONLY] using green fallback texture");
        return _fallback;
    }

    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (Event.current.type != EventType.Repaint) return;

        Texture2D tex = GetTex();
        if (tex == null) return;

        // サイズ: 画面高さの 12%、アスペクト比維持
        float h = Screen.height * 0.12f;
        float w = h * ((float)tex.width / tex.height);

        // 位置: 画面中央
        float x = (Screen.width  - w) * 0.5f;
        float y = (Screen.height - h) * 0.5f;

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x, y, w, h), tex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;

        // 毎 180 フレームにログ出力
        if (Time.frameCount % 180 == 1)
        {
            Debug.Log($"[DEBUG_GLOVE_ONLY]" +
                      $" script_attached=YES" +
                      $" glove_texture_loaded={(gloveTex != null ? "YES" : "NO")}" +
                      $" render_method=OnGUI" +
                      $" visible_on_screen=YES" +
                      $" position=center_fixed" +
                      $" size_px=({w:F0}x{h:F0})" +
                      $" glove_count=1" +
                      $" GloveTool_enabled=NO");
        }
    }
}
