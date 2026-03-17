using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 助成金企画書用 UI モック。
/// ダミー値固定。機能不要。見た目優先。
/// ExecuteInEditMode により Editor 上でも即座に表示される。
/// snow / ground / landing 系には一切触らない。
/// </summary>
[ExecuteInEditMode]
public class MockHudCanvas : MonoBehaviour
{
    // ── 内部参照 ──────────────────────────────────────
    Canvas      _canvas;
    GameObject  _topBar;
    GameObject  _gaugePanel;
    GameObject  _bottomBar;
    GameObject  _comboLabel;
    GameObject  _weatherLabel;
    GameObject  _chainLabel;

    // ── ダミー値（固定） ──────────────────────────────
    const string DUMMY_SCORE   = "SCORE  12,480";
    const string DUMMY_TIME    = "TIME  01:23";
    const string DUMMY_COMBO   = "COMBO ×7  FEVER!";
    const string DUMMY_STAGE   = "STAGE 3";
    const string DUMMY_WEATHER = "❄ BLIZZARD";
    const string DUMMY_CHAIN   = "CHAIN  4";
    const string DUMMY_GUIDE   = "TAP → 雪を落とす    HOLD → 連鎖    SWIPE → 方向指定";
    const float  DUMMY_POWER   = 0.72f;
    const float  DUMMY_FEVER   = 0.55f;
    const float  DUMMY_CHAIN_G = 0.88f;

    // ── カラーパレット ────────────────────────────────
    static readonly Color ColYellow  = new Color(1f,  0.88f, 0.1f,  1f);
    static readonly Color ColCyan    = new Color(0.3f,0.9f,  1f,    1f);
    static readonly Color ColOrange  = new Color(1f,  0.55f, 0.1f,  1f);
    static readonly Color ColWhite   = new Color(1f,  1f,    1f,    1f);
    static readonly Color ColBg      = new Color(0f,  0f,    0f,    0.55f);
    static readonly Color ColBgLight = new Color(0f,  0.05f, 0.15f, 0.65f);
    static readonly Color ColGreen   = new Color(0.2f,1f,    0.4f,  1f);
    static readonly Color ColRed     = new Color(1f,  0.25f, 0.2f,  1f);
    static readonly Color ColPurple  = new Color(0.7f,0.3f,  1f,    1f);

    // ── ライフサイクル ────────────────────────────────
    void OnEnable()
    {
        // 既に子が存在する場合は再ビルドしない（Editor での重複防止）
        if (transform.childCount > 0) return;

        BuildCanvas();
        BuildTopBar();
        BuildGaugePanel();
        BuildComboLabel();
        BuildWeatherChain();
        BuildBottomGuide();
        BuildFaceWipe();
        Debug.Log("[MockHudCanvas] mock_ui_built=YES");
    }

    // ─────────────────────────────────────────────────
    // Canvas 本体
    // ─────────────────────────────────────────────────
    void BuildCanvas()
    {
        _canvas = gameObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        gameObject.AddComponent<GraphicRaycaster>();
    }

    // ─────────────────────────────────────────────────
    // 上部バー：スコア・タイム・ステージ
    // ─────────────────────────────────────────────────
    void BuildTopBar()
    {
        _topBar = MakePanel("TopBar", transform,
            anchorMin: new Vector2(0f, 1f),
            anchorMax: new Vector2(1f, 1f),
            pivot:     new Vector2(0.5f, 1f),
            pos:       new Vector2(0f, 0f),
            size:      new Vector2(0f, 80f),
            color:     ColBg);

        // スコア（左）
        MakeText("ScoreText", _topBar.transform,
            text:      DUMMY_SCORE,
            fontSize:  48,
            color:     ColYellow,
            anchorMin: new Vector2(0f, 0f),
            anchorMax: new Vector2(0.4f, 1f),
            pos:       new Vector2(20f, 0f),
            size:      new Vector2(-20f, 0f),
            bold:      true,
            align:     TextAnchor.MiddleLeft);

        // タイム（中央）
        MakeText("TimeText", _topBar.transform,
            text:      DUMMY_TIME,
            fontSize:  52,
            color:     ColCyan,
            anchorMin: new Vector2(0.3f, 0f),
            anchorMax: new Vector2(0.7f, 1f),
            pos:       new Vector2(0f, 0f),
            size:      new Vector2(0f, 0f),
            bold:      true,
            align:     TextAnchor.MiddleCenter);

        // ステージ（右）
        MakeText("StageText", _topBar.transform,
            text:      DUMMY_STAGE,
            fontSize:  40,
            color:     ColWhite,
            anchorMin: new Vector2(0.7f, 0f),
            anchorMax: new Vector2(1f, 1f),
            pos:       new Vector2(-20f, 0f),
            size:      new Vector2(20f, 0f),
            bold:      false,
            align:     TextAnchor.MiddleRight);
    }

    // ─────────────────────────────────────────────────
    // 右側ゲージパネル：POWER / FEVER / CHAIN
    // ─────────────────────────────────────────────────
    void BuildGaugePanel()
    {
        _gaugePanel = MakePanel("GaugePanel", transform,
            anchorMin: new Vector2(1f, 0.5f),
            anchorMax: new Vector2(1f, 0.5f),
            pivot:     new Vector2(1f, 0.5f),
            pos:       new Vector2(-16f, 0f),
            size:      new Vector2(160f, 320f),
            color:     ColBgLight);

        AddGaugeRow(_gaugePanel.transform, "POWER",  DUMMY_POWER,  ColOrange, -100f);
        AddGaugeRow(_gaugePanel.transform, "FEVER",  DUMMY_FEVER,  ColPurple,    0f);
        AddGaugeRow(_gaugePanel.transform, "CHAIN",  DUMMY_CHAIN_G,ColGreen,   100f);
    }

    void AddGaugeRow(Transform parent, string label, float fill, Color barColor, float yOffset)
    {
        // ラベル
        MakeText($"{label}_Label", parent,
            text:      label,
            fontSize:  28,
            color:     ColWhite,
            anchorMin: new Vector2(0f, 0.5f),
            anchorMax: new Vector2(1f, 0.5f),
            pos:       new Vector2(0f, yOffset + 28f),
            size:      new Vector2(0f, 34f),
            bold:      true,
            align:     TextAnchor.MiddleCenter);

        // バー背景
        var bg = MakePanel($"{label}_BarBg", parent,
            anchorMin: new Vector2(0.1f, 0.5f),
            anchorMax: new Vector2(0.9f, 0.5f),
            pivot:     new Vector2(0.5f, 0.5f),
            pos:       new Vector2(0f, yOffset),
            size:      new Vector2(0f, 18f),
            color:     new Color(0.1f, 0.1f, 0.1f, 0.8f));

        // バー本体（fill 幅）
        var fillGo = MakePanel($"{label}_BarFill", bg.transform,
            anchorMin: new Vector2(0f, 0f),
            anchorMax: new Vector2(fill, 1f),
            pivot:     new Vector2(0f, 0.5f),
            pos:       new Vector2(0f, 0f),
            size:      new Vector2(0f, 0f),
            color:     barColor);

        // グロー風の明るい端
        MakePanel($"{label}_BarGlow", fillGo.transform,
            anchorMin: new Vector2(1f, 0f),
            anchorMax: new Vector2(1f, 1f),
            pivot:     new Vector2(1f, 0.5f),
            pos:       new Vector2(0f, 0f),
            size:      new Vector2(6f, 0f),
            color:     Color.white);
    }

    // ─────────────────────────────────────────────────
    // コンボ / FEVER ラベル（画面中央上寄り）
    // ─────────────────────────────────────────────────
    void BuildComboLabel()
    {
        _comboLabel = MakePanel("ComboPanel", transform,
            anchorMin: new Vector2(0.5f, 1f),
            anchorMax: new Vector2(0.5f, 1f),
            pivot:     new Vector2(0.5f, 1f),
            pos:       new Vector2(0f, -84f),
            size:      new Vector2(480f, 60f),
            color:     new Color(0.8f, 0.2f, 0f, 0.7f));

        MakeText("ComboText", _comboLabel.transform,
            text:      DUMMY_COMBO,
            fontSize:  42,
            color:     Color.white,
            anchorMin: Vector2.zero,
            anchorMax: Vector2.one,
            pos:       Vector2.zero,
            size:      Vector2.zero,
            bold:      true,
            align:     TextAnchor.MiddleCenter);
    }

    // ─────────────────────────────────────────────────
    // 天気・チェーン（左側中段）
    // ─────────────────────────────────────────────────
    void BuildWeatherChain()
    {
        var panel = MakePanel("WeatherPanel", transform,
            anchorMin: new Vector2(0f, 0.5f),
            anchorMax: new Vector2(0f, 0.5f),
            pivot:     new Vector2(0f, 0.5f),
            pos:       new Vector2(16f, 0f),
            size:      new Vector2(200f, 110f),
            color:     ColBgLight);

        MakeText("WeatherText", panel.transform,
            text:      DUMMY_WEATHER,
            fontSize:  32,
            color:     ColCyan,
            anchorMin: new Vector2(0f, 0.5f),
            anchorMax: new Vector2(1f, 1f),
            pos:       new Vector2(8f, 0f),
            size:      new Vector2(-8f, 0f),
            bold:      false,
            align:     TextAnchor.MiddleLeft);

        MakeText("ChainText", panel.transform,
            text:      DUMMY_CHAIN,
            fontSize:  32,
            color:     ColGreen,
            anchorMin: new Vector2(0f, 0f),
            anchorMax: new Vector2(1f, 0.5f),
            pos:       new Vector2(8f, 0f),
            size:      new Vector2(-8f, 0f),
            bold:      true,
            align:     TextAnchor.MiddleLeft);
    }

    // ─────────────────────────────────────────────────
    // 下部操作ガイド
    // ─────────────────────────────────────────────────
    void BuildBottomGuide()
    {
        _bottomBar = MakePanel("BottomGuide", transform,
            anchorMin: new Vector2(0f, 0f),
            anchorMax: new Vector2(1f, 0f),
            pivot:     new Vector2(0.5f, 0f),
            pos:       new Vector2(0f, 0f),
            size:      new Vector2(0f, 52f),
            color:     ColBg);

        MakeText("GuideText", _bottomBar.transform,
            text:      DUMMY_GUIDE,
            fontSize:  28,
            color:     new Color(0.85f, 0.85f, 0.85f, 1f),
            anchorMin: Vector2.zero,
            anchorMax: Vector2.one,
            pos:       new Vector2(20f, 0f),
            size:      new Vector2(-20f, 0f),
            bold:      false,
            align:     TextAnchor.MiddleCenter);
    }

    // ─────────────────────────────────────────────────
    // 顔ワイプ（専用Canvas・最前面・sortingOrder=300）
    // 重複防止: 既存の FaceWipeCanvas を全削除してから1つ生成
    // ─────────────────────────────────────────────────
    void BuildFaceWipe()
    {
        const float SIZE   = 240f;
        const float MARGIN =   8f;  // 左上に寄せて SCORE と干渉しないよう余白を詰める

        // ── 既存の FaceWipeCanvas を全削除（重複防止） ────
        var existing = Object.FindObjectsByType<Canvas>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var c in existing)
        {
            if (c != null && c.gameObject.name == "FaceWipeCanvas")
                Object.DestroyImmediate(c.gameObject);
        }

        // ── 顔ワイプ専用 Canvas（最前面・1つだけ） ────────
        var canvasGo = new GameObject("FaceWipeCanvas");
        canvasGo.transform.SetParent(transform.parent, false);
        var faceCanvas = canvasGo.AddComponent<Canvas>();
        faceCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        faceCanvas.sortingOrder = 300;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // ── 顔画像 ────────────────────────────────────────
        var portrait = new GameObject("FaceWipePortrait");
        portrait.transform.SetParent(canvasGo.transform, false);
        var portraitImg = portrait.AddComponent<Image>();

        var tex = LoadTexture("Assets/Art/BoyFaceUI.png");
        if (tex != null)
        {
            portraitImg.sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f));
            portraitImg.color = Color.white;
        }
        else
        {
            portraitImg.color  = new Color(0.20f, 0.50f, 0.80f, 1f);
            portraitImg.sprite = GetDefaultCircleSprite();
        }
        portraitImg.type           = Image.Type.Simple;
        portraitImg.preserveAspect = true;

        var rt = portrait.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0f, 1f);
        rt.anchorMax        = new Vector2(0f, 1f);
        rt.pivot            = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(MARGIN, -MARGIN);
        rt.sizeDelta        = new Vector2(SIZE, SIZE);

        // YUKI ラベルは非表示（生成しない）
    }

    // Assets フォルダから直接テクスチャを読み込む（Editor / Runtime 両対応）
    static Texture2D LoadTexture(string assetPath)
    {
#if UNITY_EDITOR
        return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
#else
        // ランタイムでは Resources フォルダ経由のみ（今回は Editor 用途なので null でフォールバック）
        return null;
#endif
    }

    // Knob スプライトが見つからない場合の白円スプライトフォールバック
    static Sprite GetDefaultCircleSprite()
    {
        // 32×32 の白円テクスチャを動的生成
        const int R = 16;
        var tex = new Texture2D(R * 2, R * 2, TextureFormat.RGBA32, false);
        var pixels = new Color32[R * 2 * R * 2];
        for (int y = 0; y < R * 2; y++)
        for (int x = 0; x < R * 2; x++)
        {
            float dx = x - R + 0.5f;
            float dy = y - R + 0.5f;
            pixels[y * R * 2 + x] = (dx * dx + dy * dy <= (R - 0.5f) * (R - 0.5f))
                ? new Color32(255, 255, 255, 255)
                : new Color32(0, 0, 0, 0);
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, R * 2, R * 2), new Vector2(0.5f, 0.5f), R * 2);
    }

    // ─────────────────────────────────────────────────
    // ヘルパー：半透明パネル生成
    // ─────────────────────────────────────────────────
    static GameObject MakePanel(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
        return go;
    }

    // ─────────────────────────────────────────────────
    // ヘルパー：テキスト生成（Legacy UI.Text）
    // ─────────────────────────────────────────────────
    static void MakeText(string name, Transform parent,
        string text, int fontSize, Color color,
        Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pos, Vector2 size,
        bool bold, TextAnchor align)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Text>();
        t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        t.text      = text;
        t.fontSize  = fontSize;
        t.color     = color;
        t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        t.alignment = align;
        t.resizeTextForBestFit = false;
        t.supportRichText = true;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor    = new Color(0f, 0f, 0f, 0.8f);
        shadow.effectDistance = new Vector2(2f, -2f);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin        = anchorMin;
        rt.anchorMax        = anchorMax;
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta        = size;
    }
}
