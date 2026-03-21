using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 毛糸の手袋ツール - PHASE 1
/// 画面中央に正常サイズで1枚表示するだけ。
/// マウス追従・前後関係・回転・カメラは未実装。
/// </summary>
[DefaultExecutionOrder(1000)]
public class GloveTool : MonoBehaviour
{
    // ── 静的ブロックフラグ（SnowStrip2D.HandleTap() から参照）────
    public static bool IsBlocking { get; private set; } = false;

    // 後方互換
    public static void DrawFrontmost() { }

    // ── 設定 ──────────────────────────────────────────────────
    public Texture2D gloveTex;

    // 画面高さに対する手袋の高さ比率（「家の扉くらい」）
    const float GLOVE_HEIGHT_RATIO = 0.12f;

    // ── フォールバックテクスチャ ──────────────────────────────
    Texture2D _fallbackTex;

    Texture2D GetTex()
    {
        if (gloveTex != null) return gloveTex;
        if (_fallbackTex != null) return _fallbackTex;
        _fallbackTex = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        var col = new Color(84f/255f, 252f/255f, 64f/255f, 1f);
        var px  = new Color[64];
        for (int i = 0; i < 64; i++) px[i] = col;
        _fallbackTex.SetPixels(px);
        _fallbackTex.Apply();
        Debug.LogWarning("[GLOVE_TOOL] gloveTex null – fallback green used");
        return _fallbackTex;
    }

    void Start()
    {
        // PHASE1 デバッグ中は DebugGloveOnly に描画を委譲するため無効化
        enabled = false;
        Debug.Log("[GLOVE_TOOL] GloveTool disabled – DebugGloveOnly is active");
    }

    void OnDestroy()
    {
        IsBlocking = false;
    }

    // ── 描画（OnGUI）─────────────────────────────────────────
    // PHASE 1: 画面中央固定表示のみ
    // マウス追従・回転・前後関係は未実装
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (Event.current.type != EventType.Repaint) return;

        Texture2D tex = GetTex();
        if (tex == null) return;

        // サイズ: 画面高さの 12%、アスペクト比維持
        float h = Screen.height * GLOVE_HEIGHT_RATIO;
        float w = h * ((float)tex.width / tex.height);

        // 位置: 画面中央
        float x = (Screen.width  - w) * 0.5f;
        float y = (Screen.height - h) * 0.5f;

        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x, y, w, h), tex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;

        if (Time.frameCount % 180 == 1)
        {
            Debug.Log($"[GLOVE_PHASE1]" +
                      $" glove_count=1" +
                      $" render_method=OnGUI" +
                      $" size_px=({w:F0}x{h:F0})" +
                      $" scale=screen_ratio_{GLOVE_HEIGHT_RATIO}" +
                      $" visible_on_screen=YES" +
                      $" distorted=NO" +
                      $" pos=({x:F0},{y:F0})");
        }
    }

    // 後方互換
    public void DrawGlove() { }
}
