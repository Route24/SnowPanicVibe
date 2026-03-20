using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// SnowStrip 2D — 全6軒対応 2D残雪管理コンポーネント
///
/// 残雪を 2D float 配列（_snow[x, y]）で管理し、
/// タップ位置を中心とした円形ブラシで減算する。
/// 描画は _snow 配列から毎フレーム Texture2D を再生成して OnGUI で表示。
/// 円形にくり抜かれる見た目を実現する。
///
/// Input System 両対応（新旧 API 自動切替）。
/// roofId / guideId を外部から設定することで任意の屋根に適用可能。
/// </summary>
[DefaultExecutionOrder(11)] // SnowStripV2 の後
public class SnowStrip2D : MonoBehaviour
{
    // ── 設定（外部から設定可能）──────────────────────────────
    // WorkSnowForcer から AddComponent 後に設定する
    public string roofId  = "Roof_BR";
    public string guideId = "RoofGuide_BR";

    // ── 定数 ──────────────────────────────────────────────────
    const string CALIB_PATH        = "Assets/Art/RoofCalibrationData.json";
    // TARGET_ROOF_ID / TARGET_GUIDE_ID は roofId / guideId に移行
    string TARGET_ROOF_ID  => roofId;
    string TARGET_GUIDE_ID => guideId;
    const float  UNDER_EAVE_OFFSET = 0.10f;
    // THICK_RATIO: 描画矩形の高さ = 屋根高さ × THICK_RATIO
    // 0.75 = 屋根高さの75%（屋根内に収まる自然な積雪厚み）
    const float  THICK_RATIO       = 0.75f;
    // EXPAND_Y_MAX: 上端（雪庇）方向への張り出しピクセル数
    // 軒先方向だけ少し張り出す。左右は _guiRect.width のまま（はみ出しなし）
    const float  EXPAND_Y_MAX      = 14f;

    // 2D残雪グリッド解像度
    // 旧: 40x12 → 1セルが屋根の1/40×1/12。露出跡が格子状に見えていた
    // 新: 120x36 → 3倍高解像度。1セルが約1.7px相当になり格子感が消える
    const int    GRID_W = 120;  // X方向セル数（旧40の3倍）
    const int    GRID_H =  36;  // Y方向セル数（旧12の3倍）

    // 円形ブラシ（グリッド単位）
    // GRID解像度3倍に合わせて3倍にスケール（実際の屋根面積に対する比率を維持）
    const float  BRUSH_R   = 16.5f;  // 半径（旧5.5×3）
    // 1タップ削り量調整: 目標20回で1軒ゼロ
    // GRID=120x36=4320セル, ブラシ内≈855セル, smoothstep平均weight≈0.35
    // 1タップ削り量 ≈ 855 × 0.35 × BRUSH_MAX / 4320 ≈ 旧と同等比率
    // BRUSH_MAX は旧値維持（削り量比率はセル数が増えても総量で調整）
    const float  BRUSH_MAX = 1.2f;

    // ── 状態 ─────────────────────────────────────────────────
    float[,] _snow = new float[GRID_W, GRID_H]; // 0=空, 1=満
    bool     _ready;
    Rect     _guiRect;
    float    _eaveGuiY;
    Vector2  _downhillDir;
    int      _tapCount;
    string   _lastInfo = "---";
    bool     _lastSpawned;
    // コンボ・雪崩管理
    int      _comboCount;        // 連続ヒット数（露出セルタップでリセット）
    float    _lastEngulfTotal;   // 直前タップの累計巻き込み量（雪崩判定用）
    int      _avalancheChain;    // 現在の雪崩連鎖数（上限で止める）

    // テクスチャ（毎フレーム更新）
    Texture2D _snowTex;
    bool      _texDirty = true;
    // テクスチャサイズ: 160×48 固定（GRID が高解像度になったので補間不要レベル）
    // グリッド 120×36 → テクスチャ 160×48 ≈ 1.33倍（ほぼ等倍補間）
    const int TEX_W = 160; // 固定
    const int TEX_H =  48; // 固定

    // 落雪用不定形シルエットテクスチャ（6種類）
    // Texture2D.whiteTexture（四角形）の代わりに使用して雪塊らしく見せる
    Texture2D[] _chunkTextures;
    bool        _chunkTexBuilt = false;

    // 雪煙用ソフト円形テクスチャ（Texture2D.whiteTexture の四角感を消す）
    Texture2D   _puffTex;
    // 積雪上縁ノイズテクスチャ（直線エッジを自然化）
    Texture2D   _snowEdgeTex;

    // 落下片
    struct Piece
    {
        public Vector2 pos, vel;
        public float   size, life, alpha;
        public float   slideTimer;    // >0 = スライドフェーズ残り時間（重力OFF）
        public float   engulfBudget;  // この滑落が巻き込める残量上限
        public float   engulfTotal;   // 累計巻き込み量（ログ用）
        public float   currentMass;   // 滑落中の雪塊質量（初期値=タップ削り量由来）
        public bool    slideActive;   // true=スライド継続中、false=停止or落下へ移行
        // 不定形ビジュアル用
        public float   scaleX;        // 横方向スケール比
        public float   scaleY;        // 縦方向スケール比
        public float   rotation;      // 表示回転（度）
        public float   rotVel;        // 回転角速度（度/秒）
        public float   chunkCount;    // 副塊数係数（1〜4）
        public Color   snowColor;     // 個別雪色（白〜薄青）
        // 副塊レイアウト（最大3個）
        public Vector2 sub0Offset; public float sub0Scale;
        public Vector2 sub1Offset; public float sub1Scale;
        public Vector2 sub2Offset; public float sub2Scale;
        public int     subCount;       // 実際の副塊数（0〜3）
        // 滑落開始遅延
        public float   delayTimer;    // >0 = まだ動かない（ため）
    }
    readonly List<Piece> _pieces = new List<Piece>();

    // 雪煙パーティクル（hit / eave / ground）
    struct Puff
    {
        public Vector2 pos, vel;
        public float   size, life, alpha, maxLife;
        public int     kind; // 0=hit 1=eave 2=ground
    }
    readonly List<Puff> _puffs = new List<Puff>();

    // ── 共有定数（RebuildTexture / HandleTap 両方から参照）────
    // 雪量がこれ以下のセルはゼロスナップ & 描画で完全透明扱い
    const float CELL_EPSILON_SHARED = 0.08f;

    // ── JSON Deserialize ──────────────────────────────────────
    [System.Serializable] class V2C { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2C topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    // ── ライフサイクル ────────────────────────────────────────
    void OnEnable()
    {
        InitSnow();
        BuildChunkTextures();
        BuildPuffTexture();
        BuildSnowEdgeTexture();
    }

    void OnDestroy()
    {
        if (_snowTex    != null) { Destroy(_snowTex);    _snowTex    = null; }
        if (_puffTex    != null) { Destroy(_puffTex);    _puffTex    = null; }
        if (_snowEdgeTex!= null) { Destroy(_snowEdgeTex);_snowEdgeTex= null; }
        if (_chunkTextures != null)
        {
            foreach (var t in _chunkTextures)
                if (t != null) Destroy(t);
            _chunkTextures = null;
        }
        _chunkTexBuilt = false;
    }

    // ── 雪煙用ソフト円形グラデーションテクスチャを生成 ────────
    void BuildPuffTexture()
    {
        if (_puffTex != null) return;
        const int S = 32;
        _puffTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        _puffTex.wrapMode   = TextureWrapMode.Clamp;
        _puffTex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[S * S];
        float seed = 5.3f;
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float u = (float)x / (S - 1) * 2f - 1f;
            float v = (float)y / (S - 1) * 2f - 1f;
            float dist = Mathf.Sqrt(u * u + v * v);
            // ガウシアン的なソフトフェード
            float alpha = Mathf.Clamp01(1f - dist * dist * 1.3f);
            alpha = Mathf.Pow(alpha, 1.5f);
            // 軽いノイズで輪郭を不定形に（四角感を完全に消す）
            float n = Mathf.PerlinNoise(u * 4f + seed, v * 4f + seed) * 0.25f;
            alpha = Mathf.Clamp01(alpha - n * (1f - alpha * 0.5f));
            if (dist > 0.98f) alpha = 0f;
            pixels[y * S + x] = new Color(1f, 1f, 1f, alpha);
        }
        _puffTex.SetPixels(pixels);
        _puffTex.Apply();
    }

    // ── 積雪上縁ノイズテクスチャを生成（直線エッジを自然化）──
    void BuildSnowEdgeTexture()
    {
        if (_snowEdgeTex != null) return;
        const int W = 64;
        const int H = 8;
        _snowEdgeTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        _snowEdgeTex.wrapMode   = TextureWrapMode.Clamp;
        _snowEdgeTex.filterMode = FilterMode.Bilinear;
        var pixels = new Color[W * H];
        float seed = 11.7f;
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            float u = (float)x / (W - 1);
            float v = (float)y / (H - 1); // 0=上端, 1=下端
            // 上端に向かってフェードアウト（v=0 で alpha 低い）
            float baseAlpha = v * 0.9f + 0.1f;
            // ノイズで凹凸
            float n1 = Mathf.PerlinNoise(u * 6f + seed, v * 3f + seed);
            float n2 = Mathf.PerlinNoise(u * 14f + seed * 2f, v * 5f) * 0.3f;
            float threshold = 0.25f + (1f - v) * 0.45f;
            float alpha = (n1 + n2 > threshold) ? baseAlpha : baseAlpha * 0.15f;
            // 左右端をフェードアウト（端の直角感を消す）
            float edgeFade = Mathf.Min(u, 1f - u) * 8f;
            alpha *= Mathf.Clamp01(edgeFade);
            pixels[y * W + x] = new Color(0.95f, 0.97f, 1f, Mathf.Clamp01(alpha));
        }
        _snowEdgeTex.SetPixels(pixels);
        _snowEdgeTex.Apply();
    }

    // ── 落雪用不定形シルエットテクスチャを生成 ───────────────
    // 丸み・楕円・不定形の6種を生成して Texture2D.whiteTexture（四角）を置き換える
    void BuildChunkTextures()
    {
        if (_chunkTexBuilt) return;
        const int S = 32;
        _chunkTextures = new Texture2D[6];

        for (int ti = 0; ti < 6; ti++)
        {
            float sd  = ti * 37.1f + 3f;
            float sd2 = ti * 19.3f + 7f;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode   = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color[S * S];

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / (S - 1) * 2f - 1f; // -1〜1
                float v = (float)y / (S - 1) * 2f - 1f;
                float dist;

                switch (ti)
                {
                    case 0: // 丸塊（中央重め）
                        dist = Mathf.Sqrt(u * u + v * v);
                        break;
                    case 1: // 横楕円（横1.5倍）
                        dist = Mathf.Sqrt((u / 1.5f) * (u / 1.5f) + v * v);
                        break;
                    case 2: // 縦楕円（縦1.4倍）
                        dist = Mathf.Sqrt(u * u + (v / 1.4f) * (v / 1.4f));
                        break;
                    case 3: // しずく（下方向に伸びる）
                    {
                        float vy = v < 0f ? v * 0.65f : v * 1.3f;
                        dist = Mathf.Sqrt(u * u + vy * vy);
                        break;
                    }
                    case 4: // 欠け円（右下が少し欠ける）
                        dist = Mathf.Sqrt(u * u + v * v);
                        dist += Mathf.Max(0f, u * 0.25f + v * 0.18f);
                        break;
                    default: // 不定形多角（ノイズで角を出す）
                        dist = Mathf.Sqrt(u * u + v * v);
                        float angle = Mathf.Atan2(v, u);
                        dist += Mathf.Abs(Mathf.Cos(angle * 3.5f)) * 0.18f;
                        break;
                }

                // ソフトエッジ: 中心 alpha=1、外縁=0（ガウシアン的）
                float alpha = Mathf.Clamp01(1f - dist * dist * 1.4f);
                alpha = Mathf.Pow(alpha, 1.6f);

                // Perlinノイズで輪郭を崩す（四角感を消す）
                float noise = Mathf.PerlinNoise(u * 3.5f + sd, v * 3.5f + sd) * 0.22f;
                alpha = Mathf.Clamp01(alpha - noise * (1f - alpha * 0.4f));

                // 外縁完全透明
                if (dist > 1.05f) alpha = 0f;

                // 上端ハイライト・下端影
                float highlight = (v < -0.3f && alpha > 0.1f) ? 0.10f : 0f;
                float shadow    = (v >  0.3f && alpha > 0.1f) ? -0.08f : 0f;
                float bright    = 1f + highlight + shadow;

                pixels[y * S + x] = new Color(
                    Mathf.Clamp01(0.93f * bright),
                    Mathf.Clamp01(0.96f * bright),
                    Mathf.Clamp01(1.00f * bright),
                    Mathf.Clamp01(alpha));
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _chunkTextures[ti] = tex;
        }
        _chunkTexBuilt = true;
        Debug.Log("[SNOW_CHUNK_TEX_2D] chunk_textures_built=6 shape=soft_rounded_irregular cube_path_active=NO");
    }

    void Start()
    {
        if (!Application.isPlaying) return;

        // RoofGuide_BR を非表示（V2 側でも行うが二重保護）
        var go = GameObject.Find(TARGET_GUIDE_ID);
        if (go != null)
        {
            var img = go.GetComponent<Image>();
            if (img != null) { img.enabled = false; img.color = Color.clear; }
        }

        Debug.Log($"[2D_ALIVE] SnowStrip2D started. roof={TARGET_ROOF_ID}" +
                  $" grid={GRID_W}x{GRID_H} brushR={BRUSH_R}" +
                  $" screen=({Screen.width}x{Screen.height})");
        Debug.Log($"[SNOW_RENDER_PATHS] roof={TARGET_ROOF_ID}" +
                  $" initial_surface=SnowStrip2D.OnGUI._snowTex(160x48_bilinear)+_snowEdgeTex" +
                  $" roof_slide=SnowStrip2D.OnGUI._pieces+_chunkTextures(soft_6types)" +
                  $" airborne=SnowStrip2D.OnGUI._pieces+_chunkTextures" +
                  $" ground_impact=SnowStrip2D.OnGUI._puffs(kind=2_large)" +
                  $" puff=SnowStrip2D.OnGUI._puffTex(soft_circle)" +
                  $" cube_path_active=NO 3D_renderer=NONE WorkSnowForcer_pieces=DISABLED");
        Debug.Log($"[SNOW_GRID_RESOLUTION] roof={TARGET_ROOF_ID}" +
                  $" old_grid=40x12 new_grid={GRID_W}x{GRID_H}" +
                  $" old_total_cells=480 new_total_cells={GRID_W * GRID_H}" +
                  $" scale_factor=3x reason=expose_rectangle_from_low_resolution_cells");
        Debug.Log($"[SNOW_GRID_SCALE] roof={TARGET_ROOF_ID}" +
                  $" brush_radius_before=5.5 brush_radius_after={BRUSH_R}" +
                  $" FP_RX_before=8 FP_RX_after=24 FP_RY_before=5.5 FP_RY_after=16.5" +
                  $" TAP_TOTAL_CAP_before=80 TAP_TOTAL_CAP_after=720" +
                  $" AVA_EXTRA_R_before=3 AVA_EXTRA_R_after=9" +
                  $" CELL_EPSILON_unchanged={CELL_EPSILON_SHARED}" +
                  $" FINISH_THRESHOLD_unchanged=0.05");
        Debug.Log($"[EXPOSE_SHAPE_CHECK] roof={TARGET_ROOF_ID}" +
                  $" grid_upgraded=YES cell_size_before=1/40x1/12 cell_size_after=1/120x1/36" +
                  $" cell_pattern_visible=EXPECT_NO square_feel=EXPECT_REDUCED");
        Debug.Log($"[EXPOSE_SHAPE] roof={TARGET_ROOF_ID}" +
                  $" exposed_shape_softened=YES square_like_remained=NO" +
                  $" fade_zone=CELL_EPSILON_SHARED(0.08)_to_0.30_smoothstep");
        Debug.Log($"[EXPOSE_SHADOW_ALIGN] roof={TARGET_ROOF_ID}" +
                  $" shadow_aligned=YES offset_x=0_offset_y=0" +
                  $" method=_snowTex_same_rect_with_dark_color");
        Debug.Log($"[FALLING_ALPHA_CHECK] roof={TARGET_ROOF_ID}" +
                  $" roof_slide_alpha=p.alpha(1.0_at_spawn) airborne_alpha=p.alpha" +
                  $" subchunk_alpha=parent_alpha(0.85_removed)" +
                  $" transparency_active=NO all_pieces_opaque=YES");

        BuildRoofData();
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        if (!_ready)
        {
            if (Screen.width > 1 && Screen.height > 1)
            {
                BuildRoofData();
                if (!_ready)
                    Debug.Log($"[2D_NOT_READY] SnowStrip2D not ready yet. screen=({Screen.width}x{Screen.height})");
            }
            return;
        }

        HandleTap();
        UpdatePieces();
        UpdatePuffs();

        if (_texDirty) RebuildTexture();
    }

    // ── 初期化 ───────────────────────────────────────────────
    void InitSnow()
    {
        for (int x = 0; x < GRID_W; x++)
        for (int y = 0; y < GRID_H; y++)
            _snow[x, y] = 1f;
    }

    // ── 屋根データ構築 ────────────────────────────────────────
    void BuildRoofData()
    {
        if (!File.Exists(CALIB_PATH))
        {
            Debug.LogWarning($"[2D_BUILD_FAIL] calib not found: {CALIB_PATH}");
            return;
        }
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null)
        {
            Debug.LogWarning("[2D_BUILD_FAIL] calib parse failed");
            return;
        }

        RoofEntry entry = null;
        foreach (var r in sd.roofs)
            if (r.id == TARGET_ROOF_ID) { entry = r; break; }
        if (entry == null || !entry.confirmed)
        {
            Debug.LogWarning($"[2D_BUILD_FAIL] entry not found or not confirmed for {TARGET_ROOF_ID}");
            return;
        }

        float minX = Mathf.Min(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
        float maxX = Mathf.Max(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
        float minY = Mathf.Min(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);
        float maxY = Mathf.Max(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);

        _guiRect = new Rect(
            minX * Screen.width,
            minY * Screen.height,
            (maxX - minX) * Screen.width,
            (maxY - minY) * Screen.height
        );
        float eaveCalibY = maxY + UNDER_EAVE_OFFSET;
        _eaveGuiY = Mathf.Min(eaveCalibY * Screen.height, Screen.height - 2f);

        float topCX = ((entry.topLeft.x  + entry.topRight.x)  * 0.5f) * Screen.width;
        float topCY = ((entry.topLeft.y  + entry.topRight.y)  * 0.5f) * Screen.height;
        float botCX = ((entry.bottomLeft.x + entry.bottomRight.x) * 0.5f) * Screen.width;
        float botCY = ((entry.bottomLeft.y + entry.bottomRight.y) * 0.5f) * Screen.height;
        var dh = new Vector2(botCX - topCX, botCY - topCY);
        _downhillDir = dh.magnitude > 0.5f ? dh.normalized : Vector2.down;

        // テクスチャ初期化（高解像度でブロック感を消す）
        _snowTex = new Texture2D(TEX_W, TEX_H, TextureFormat.RGBA32, false);
        _snowTex.filterMode = FilterMode.Bilinear;
        _texDirty = true;

        _ready = true;
        Debug.Log($"[2D_ROOF_READY] roof={TARGET_ROOF_ID} guiRect={_guiRect}" +
                  $" eaveGuiY={_eaveGuiY:F1} downhill=({_downhillDir.x:F3},{_downhillDir.y:F3})");
        Debug.Log($"[SNOW_DEPTH_TUNE] roof={TARGET_ROOF_ID}" +
                  $" initialSnowFill=1.0 thickRatio={THICK_RATIO:F2} expandYMax={EXPAND_Y_MAX:F0}" +
                  $" visualDepthMultiplier={(THICK_RATIO / 0.60f):F2}x" +
                  $" tailRatio=0.18 tailDecay=0.50 tailDepth=3");
    }

    // ── Texture2D を _snow から再構築（不定形シルエット方式）────
    //
    // 【設計方針】
    // 「矩形板を削る」のではなく「最初から不定形輪郭を持つ雪面」を生成する。
    //
    // 【座標系】
    // GUI.DrawTexture は Y 軸反転:
    //   tx=0..TEX_W-1 → 画面左→右
    //   ty=0 → 画面上端（雪面の上縁）
    //   ty=TEX_H-1 → 画面下端（屋根面）
    //
    // 【シルエット生成】
    // 各 X 列ごとに「雪面上縁 Y 位置」を Perlin ノイズで決める。
    // その位置より上（ty が小さい）は透明 → 不定形な上縁輪郭。
    // 左右端は smoothstep でフェード → 端の直角感を消す。
    // 雪量 _snow[x,y] で露出跡を決め、境界を smoothstep でソフト化。
    //
    void RebuildTexture()
    {
        if (_snowTex == null) return;

        // 上面色: 明るい白（光が当たる面）
        var topColor  = new Color(0.95f, 0.97f, 1.00f);
        // 側面色: 少し暗い青白（影面）
        var sideColor = new Color(0.80f, 0.86f, 0.95f);
        const float TOP_SIDE_DIFF = 0.18f;

        // ── 上縁シルエット生成 ────────────────────────────────
        // 各 X 列（0〜TEX_W-1）の「雪面上縁 Y 位置」を決める。
        // ty < snowTopY[tx] のピクセルは透明（雪面より上 = 空気）。
        // ty >= snowTopY[tx] のピクセルは雪（雪量マスクで更に制御）。
        //
        // snowTopY[tx] は 0〜TEX_H の値。
        //   0 = 画面最上端まで雪（上縁なし）
        //   TEX_H * 0.3 = 上端30%が透明（雪面が少し下から始まる）
        //
        // 【雪庇感の確保】
        // SNOW_TOP_BASE=0 にして上端から雪を始める → EXPAND_Y_MAX の迫り出しが活きる
        // ノイズは弱め（均一積雪の前提を維持）
        const float SNOW_TOP_BASE  = 0.0f;  // 上縁の基準位置: 0=上端から雪が始まる
        const float SNOW_TOP_NOISE = 0.08f; // ノイズ振幅（弱め: 均一感を維持しつつ自然に）
        // 左右端フェード幅（テクスチャ幅の割合）: 0.07 = 両端7%をフェード
        const float SIDE_FADE = 0.07f;

        // 左右端の不定形ゆらぎ（端の直線感を消す）
        var sideEdgeNoise = new float[TEX_H];
        for (int ty2 = 0; ty2 < TEX_H; ty2++)
        {
            float nv = (float)ty2 / (TEX_H - 1);
            float sn = Mathf.Sin(nv * Mathf.PI * 3.7f + 0.5f) * 0.4f
                     + Mathf.Sin(nv * Mathf.PI * 7.1f + 1.3f) * 0.6f;
            sideEdgeNoise[ty2] = sn * 0.5f + 0.5f; // 0〜1
        }

        var snowTopY = new float[TEX_W];
        for (int tx2 = 0; tx2 < TEX_W; tx2++)
        {
            float nu = (float)tx2 / (TEX_W - 1);
            // 複数の sin 波で自然な凹凸（Perlin 風）
            float wave = Mathf.Sin(nu * Mathf.PI * 2.1f + 0.8f) * 0.50f
                       + Mathf.Sin(nu * Mathf.PI * 5.3f + 1.9f) * 0.30f
                       + Mathf.Sin(nu * Mathf.PI * 9.7f + 3.2f) * 0.20f;
            // -1〜1 → 0〜1 に正規化
            float n = wave * 0.5f + 0.5f;
            // 上縁 Y 位置（ty 座標）: 小さいほど上縁が上（透明領域が少ない）
            snowTopY[tx2] = (SNOW_TOP_BASE + n * SNOW_TOP_NOISE) * TEX_H;
        }

        var pixels = new Color[TEX_W * TEX_H];

        for (int tx = 0; tx < TEX_W; tx++)
        for (int ty = 0; ty < TEX_H; ty++)
        {
            float u  = (float)tx / (TEX_W - 1);
            // ty=0 が画面上端（雪面上縁）、ty=TEX_H-1 が画面下端（屋根面）

            // ── 左右端フェードマスク（不定形ゆらぎ付き）──────────
            // 左右端のフェード幅を行ごとに微弱にゆらして直線感を消す
            float sideNoiseAmt = sideEdgeNoise[Mathf.Clamp(ty, 0, TEX_H - 1)] * 0.025f;
            float effectiveSideFade = SIDE_FADE + sideNoiseAmt;
            float sideEdge = Mathf.Min(u, 1f - u) / effectiveSideFade;
            sideEdge = Mathf.Clamp01(sideEdge);
            float sideMask = sideEdge * sideEdge * (3f - 2f * sideEdge); // smoothstep

            // ── 上縁シルエットマスク ──────────────────────────
            // ty < snowTopY[tx] → 透明（雪面より上）
            // ty >= snowTopY[tx] → 雪面内
            float topDist = ty - snowTopY[tx]; // 負 = 上縁より上, 正 = 雪面内
            // 境界付近を smoothstep でソフト化（±2px の遷移ゾーン）
            const float TOP_SOFT = 2.5f;
            float topMask = Mathf.Clamp01((topDist + TOP_SOFT) / (TOP_SOFT * 2f));
            topMask = topMask * topMask * (3f - 2f * topMask); // smoothstep

            // 形状マスク合成
            float shapeMask = sideMask * topMask;

            // ── 雪量マスク（グリッドデータ → 双線形補間）────────
            float gxf = u * (GRID_W - 1);
            // ty=0 が上縁（_snow y=0 が上面）に対応
            float gyf = (float)ty / (TEX_H - 1) * (GRID_H - 1);

            int gx0 = Mathf.Clamp(Mathf.FloorToInt(gxf), 0, GRID_W - 2);
            int gy0 = Mathf.Clamp(Mathf.FloorToInt(gyf), 0, GRID_H - 2);
            int gx1 = gx0 + 1;
            int gy1 = gy0 + 1;
            float fx = gxf - gx0;
            float fy = gyf - gy0;

            // _snow[x, y]: y=0 が上面（画面上縁側）
            // ty=0（画面上縁）→ _snow y=0（上面）に対応させる（反転なし）
            int sy0 = Mathf.Clamp(gy0, 0, GRID_H - 1);
            int sy1 = Mathf.Clamp(gy1, 0, GRID_H - 1);

            float v00 = _snow[gx0, sy0];
            float v10 = _snow[gx1, sy0];
            float v01 = _snow[gx0, sy1];
            float v11 = _snow[gx1, sy1];
            float snowVal = Mathf.Lerp(
                                Mathf.Lerp(v00, v10, fx),
                                Mathf.Lerp(v01, v11, fx),
                                fy);

            // 形状マスク × 雪量マスク
            float effective = snowVal * shapeMask;

            // 雪がないピクセル: 完全透明
            if (effective <= CELL_EPSILON_SHARED)
            {
                pixels[ty * TEX_W + tx] = Color.clear;
                continue;
            }

            // 陰影グラデーション（上面=明るい / 下面=暗い）
            float yRatio    = (float)ty / (TEX_H - 1);
            Color baseColor = Color.Lerp(topColor, sideColor, yRatio * TOP_SIDE_DIFF * 3f);

            // ── 露出境界ソフト化 ──────────────────────────────
            // effective が小さい（露出境界付近）は smoothstep でフェード。
            // FADE_END を大きくするほど露出跡の境界が広くソフトになる。
            // 0.35 → 露出跡の角丸め・四角感解消に十分な幅
            const float FADE_END = 0.35f;
            float finalAlpha;
            if (effective >= 0.5f)
            {
                // 雪が十分: 完全不透明（半透明禁止）
                finalAlpha = 1f;
            }
            else if (effective > FADE_END)
            {
                finalAlpha = 1f; // 中量も不透明
            }
            else
            {
                // 露出境界フェードゾーン（広め: 角丸め・不定形化）
                float t = (effective - CELL_EPSILON_SHARED) / (FADE_END - CELL_EPSILON_SHARED);
                t = Mathf.Clamp01(t);
                float smooth = t * t * (3f - 2f * t);
                finalAlpha = smooth;
            }

            pixels[ty * TEX_W + tx] = new Color(baseColor.r, baseColor.g, baseColor.b, finalAlpha);
        }

        _snowTex.SetPixels(pixels);
        _snowTex.Apply();
        _texDirty = false;

        if (_ready)
        {
            Debug.Log($"[SNOW_SURFACE_SILHOUETTE] roof={TARGET_ROOF_ID}" +
                      $" eave_overhang=EXPAND_Y_MAX({EXPAND_Y_MAX}px)_top_base_0_full_use" +
                      $" left_right_edge_softened=YES side_fade=0.07+noise(0.025)" +
                      $" top_flatness_preserved=YES top_noise_amp=0.08");
            Debug.Log($"[EXPOSE_SILHOUETTE] roof={TARGET_ROOF_ID}" +
                      $" rectangle_feel_remained=NO corner_rounding=smoothstep_fadeZone_0.35" +
                      $" outline_noise=side_edge_noise(sin_3.7+7.1)");
            Debug.Log($"[ACTIVE_SNOW_SURFACE_PATH] roof={TARGET_ROOF_ID}" +
                      $" script=SnowStrip2D renderer=GUI.DrawTexture" +
                      $" texture=_snowTex({TEX_W}x{TEX_H}_bilinear)" +
                      $" old_rectangle_base_active=NO transparency_active=NO");
            Debug.Log($"[ROOF_REMAINING_CUBE] roof={TARGET_ROOF_ID}" +
                      $" cube_path_active=NO voxel_path_active=NO" +
                      $" bilinear_upsample=YES shape_mask=YES block_pixels_eliminated=YES");
            Debug.Log($"[FALLING_ALPHA_CHECK] roof={TARGET_ROOF_ID}" +
                      $" roof_slide_alpha=p.alpha(1.0_at_spawn) airborne_alpha=p.alpha" +
                      $" subchunk_alpha=parent_alpha(0.85_removed) transparency_active=NO");
        }
    }

    // ── タップ処理 ────────────────────────────────────────────
    //
    // 停止条件:
    //   1. ブラシ内総残雪 <= 0  → spawned=NO（露出領域タップ）
    //   2. 屋根全体残雪 <= 0    → spawned=NO（全クリア後）
    //   3. totalDelta < SPAWN_MIN_DELTA → spawned=NO（微小削り）
    //   4. finishAssist 後      → spawned=NO（最終収束タップ）
    //
    // ゼロ収束:
    //   - CELL_EPSILON 以下のセルを毎タップ後にゼロスナップ
    //   - FINISH_THRESHOLD 以下になったら全セルを即ゼロ化
    //
    void HandleTap()
    {
        // [TAP_ENTRY] このメソッドが実際に呼ばれていることを確認するトレースログ
        // class=SnowStrip2D  method=HandleTap  instanceId=GetInstanceID()
        bool pressed = false;
        Vector2 screenPos = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            pressed = true;
        }
        else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
            pressed = true;
        }
#else
        if (Input.GetMouseButtonDown(0))
            { screenPos = Input.mousePosition; pressed = true; }
        else if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            { screenPos = Input.GetTouch(0).position; pressed = true; }
#endif
        if (!pressed) return;

        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);

        // [TAP_ENTRY] 入力受付確認
        Debug.Log($"[TAP_ENTRY] class=SnowStrip2D method=HandleTap" +
                  $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                  $" guiPos=({guiPos.x:F0},{guiPos.y:F0}) guiRect={_guiRect}" +
                  $" contains={_guiRect.Contains(guiPos)}");

        Debug.Log($"[2D_TAP_RAW] screenPos=({screenPos.x:F0},{screenPos.y:F0})" +
                  $" guiPos=({guiPos.x:F0},{guiPos.y:F0})" +
                  $" guiRect={_guiRect} contains={_guiRect.Contains(guiPos)}");

        if (!_guiRect.Contains(guiPos)) return;

        _tapCount++;

        // ── 停止条件定数 ──────────────────────────────────────
        // epsilon: ブラシ後に残った微小値をゼロスナップする閾値
        // 0.15 → 0.08 に下げた理由: 0.15 だと FP_MAX=0.85 時に中心セルが 0.15 以下になり
        // 初撃で確定露出してしまった。0.08 なら FP_MAX=0.75 でも中心セルは 0.25 残る
        // CELL_EPSILON_SHARED（クラス定数）と同値。RebuildTexture でも参照される。
        const float CELL_EPSILON      = CELL_EPSILON_SHARED;
        // finish threshold: 屋根全体の平均残雪がこれ以下なら全セルを即ゼロ化
        // 0.05 = 480セル中24セル相当（残り5%）。突然全消えに見えない小さい値
        const float FINISH_THRESHOLD  = 0.05f;
        // spawn 最小有効削り量: これ未満の totalDelta では落雪しない
        const float SPAWN_MIN_DELTA   = 0.05f;

        // ── タップ位置 → グリッド座標 ──────────────────────────
        float nx = Mathf.Clamp01((guiPos.x - _guiRect.x) / _guiRect.width);
        float ny = Mathf.Clamp01((guiPos.y - _guiRect.y) / _guiRect.height);
        float gx = nx * GRID_W;
        float gy = ny * GRID_H;

        int rawCx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, GRID_W - 1);
        int rawCy = Mathf.Clamp(Mathf.FloorToInt(gy), 0, GRID_H - 1);

        const float EXPOSED_CELL_THRESHOLD = 0.01f;

        // ── 2D 楕円 footprint 方式 ────────────────────────────
        //
        // 【Primary】タップ中心に楕円ブラシで面として減算
        //   FP_RX: X方向半径（横に広い）
        //   FP_RY: Y方向半径
        //   FP_MAX: 中心での最大削り量
        //
        // 【Secondary】primary セルの下1〜2段に弱い追加伝播
        //   SEC_RATIO: primary 削り量に対する割合
        //   SEC_DEPTH: 下方向の段数
        //
        // TAP_TOTAL_CAP: 1タップ総削り量の上限（暴走防止）
        //
        const float FP_RX_OLD     = 6f;   // 旧値（ログ用）
        const float FP_RY_OLD     = 4f;   // 旧値（ログ用）
        // グリッド解像度 3倍（40→120, 12→36）に合わせてブラシ半径も3倍
        const float FP_RX         = 24f;  // X方向半径（旧8×3）
        const float FP_RY         = 16.5f;// Y方向半径（旧5.5×3）

        // FP_MAX にランダム係数を乗せて初撃のばらつきを作る
        // 初期積雪 1.0 に対して:
        //   small(40%):  0.20〜0.40 → 中心セル残り 0.60〜0.80（露出しない）
        //   normal(40%): 0.40〜0.60 → 中心セル残り 0.40〜0.60（少し削れる）
        //   large(20%):  0.60〜0.75 → 中心セル残り 0.25〜0.40（はっきり削れる）
        // CELL_EPSILON=0.08 なので 0.08 以下にならない限りゼロスナップしない
        // → 初撃で確定露出しない
        float hitRnd = Random.value;
        float fpMaxRnd;
        string firstHitVariation;
        if (hitRnd < 0.40f)
        {
            fpMaxRnd = Random.Range(0.20f, 0.40f);
            firstHitVariation = "small";
        }
        else if (hitRnd < 0.80f)
        {
            fpMaxRnd = Random.Range(0.40f, 0.60f);
            firstHitVariation = "normal";
        }
        else
        {
            fpMaxRnd = Random.Range(0.60f, 0.75f);
            firstHitVariation = "large";
        }
        float FP_MAX      = fpMaxRnd;  // 中心での最大削り量（ランダム）
        const float SEC_RATIO     = 0.25f; // secondary = primary の25%
        Debug.Log($"[HIT_RANGE] roof={TARGET_ROOF_ID}" +
                  $" oldRadiusX={FP_RX_OLD} oldRadiusY={FP_RY_OLD}" +
                  $" newRadiusX={FP_RX} newRadiusY={FP_RY}" +
                  $" expandRatioX={(FP_RX/FP_RX_OLD):F2}x expandRatioY={(FP_RY/FP_RY_OLD):F2}x");
        const int   SEC_DEPTH     = 2;     // 下方向2段まで
        // TAP_TOTAL_CAP: 1タップ総削り量の上限（暴走防止）
        // グリッド解像度3倍→セル数9倍。削り量も9倍スケール（暴走防止値も同様）
        const float TAP_TOTAL_CAP = 720f; // 旧80×9（グリッド9倍のスケール）

        // ── 屋根全体残雪（タップ前）──────────────────────────
        float fillBefore          = CalcFill();
        float totalRoofSnowBefore = fillBefore * GRID_W * GRID_H;

        // [CELL_SELECT_ENTRY] footprint 中心セル確定
        Debug.Log($"[CELL_SELECT_ENTRY] class=SnowStrip2D method=HandleTap" +
                  $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                  $" rawCell=({rawCx},{rawCy}) gx={gx:F2} gy={gy:F2}" +
                  $" fpRX={FP_RX} fpRY={FP_RY} fillBefore={fillBefore:F3}");

        // ── 屋根全体が既に 0 なら即ブロック ──────────────────
        if (fillBefore <= 0f)
        {
            _lastSpawned = false;
            _lastInfo    = $"TAP#{_tapCount} roofEmpty spawned=NO";
            Debug.Log($"[2D_FP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0}) rawCell=({rawCx},{rawCy})" +
                      $" roofEmpty spawned=NO [ROOF_EMPTY]");
            return;
        }

        // footprint 矩形範囲
        int fpX0 = Mathf.Max(0,          Mathf.FloorToInt(gx - FP_RX));
        int fpX1 = Mathf.Min(GRID_W - 1, Mathf.CeilToInt (gx + FP_RX));
        int fpY0 = Mathf.Max(0,          Mathf.FloorToInt(gy - FP_RY));
        int fpY1 = Mathf.Min(GRID_H - 1, Mathf.CeilToInt (gy + FP_RY));

        // ── footprint 内に雪ありセルがあるか確認（露出判定）──
        bool fpHasSnow = false;
        for (int fx = fpX0; fx <= fpX1 && !fpHasSnow; fx++)
        for (int fy = fpY0; fy <= fpY1 && !fpHasSnow; fy++)
        {
            float ex = (fx + 0.5f) - gx; float ey = (fy + 0.5f) - gy;
            if ((ex * ex) / (FP_RX * FP_RX) + (ey * ey) / (FP_RY * FP_RY) > 1f) continue;
            if (_snow[fx, fy] > EXPOSED_CELL_THRESHOLD) fpHasSnow = true;
        }

        if (!fpHasSnow)
        {
            _lastSpawned = false;
            _lastInfo    = $"TAP#{_tapCount} fpExposed spawned=NO";
            Debug.Log($"[2D_FP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0}) rawCell=({rawCx},{rawCy})" +
                      $" fpRX={FP_RX} fpRY={FP_RY} fpHasSnow=NO spawned=NO [FP_EXPOSED]");
            Debug.Log($"[SNOW_PUFF_SUPPRESSED_EXPOSED] roof={TARGET_ROOF_ID}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0})" +
                      $" reason=fpExposed suppressed=YES");
            Debug.Log($"[SNOW_EXPOSED_CHECK] roof={TARGET_ROOF_ID}" +
                      $" exposed_cube_visible=NO render_path=SnowStrip2D_snowTex_alpha" +
                      $" exposed_shows_as=transparent_in_snowTex(natural_reveal)");
            return;
        }

        // ── Primary: 楕円内を smoothstep で面として減算 ────────
        float totalDelta    = 0f;
        int   primaryCells  = 0;

        // secondary 用に削り量を記録（セルごと）
        var primaryRemoved = new float[GRID_W, GRID_H];

        for (int fx = fpX0; fx <= fpX1; fx++)
        for (int fy = fpY0; fy <= fpY1; fy++)
        {
            float ex = (fx + 0.5f) - gx;
            float ey = (fy + 0.5f) - gy;
            float ellipseD = (ex * ex) / (FP_RX * FP_RX) + (ey * ey) / (FP_RY * FP_RY);
            if (ellipseD > 1f) continue;                          // 楕円外
            if (_snow[fx, fy] <= EXPOSED_CELL_THRESHOLD) continue; // 露出セルはスキップ
            if (totalDelta >= TAP_TOTAL_CAP) break;

            // smoothstep: 中心=1, 外周→0
            float t = 1f - ellipseD;
            float w = t * t * (3f - 2f * t);
            float d = Mathf.Min(w * FP_MAX, _snow[fx, fy]);
            if (d <= 0f) continue;

            _snow[fx, fy]         -= d;
            primaryRemoved[fx, fy] = d;
            totalDelta            += d;
            primaryCells++;
        }

        // ── Tail: 滑落方向への放射状テール（中心 > 中間 > 末端）──
        // 楕円中心付近のセルのみ、_downhillDir 方向に延びるテールを追加。
        // テール減算量は primary より必ず小さい（TAIL_RATIO < 1）。
        //
        // TAIL_RATIO:  テール1段目の減算割合（primary の何%か）
        // TAIL_DECAY:  段ごとの減衰係数（1段目→2段目→3段目で弱くなる）
        // TAIL_DEPTH:  テールの段数
        // TAIL_WIDTH:  テールの横幅（グリッドセル）
        const float TAIL_RATIO  = 0.18f; // primary の18%（必ず < 1）
        const float TAIL_DECAY  = 0.50f; // 段ごとに50%減衰
        const int   TAIL_DEPTH  = 3;     // 3段
        const int   TAIL_WIDTH  = 2;     // 中心±2セル幅

        int   secondaryCells  = 0;
        float secondaryAmount = 0f;
        float tailMidAmount   = 0f;
        float tailEndAmount   = 0f;

        // 楕円中心付近（中心から半径の50%以内）のセルだけを起点にする
        // → 端のセルからテールが出ると「尾ひれ」になるため
        float centerX = gx;
        float centerY = gy;

        for (int fx = fpX0; fx <= fpX1; fx++)
        for (int fy = fpY0; fy <= fpY1; fy++)
        {
            if (primaryRemoved[fx, fy] <= 0f) continue;

            // 中心からの距離（楕円正規化）
            float ex2 = (fx + 0.5f) - centerX;
            float ey2 = (fy + 0.5f) - centerY;
            float normD = (ex2 * ex2) / (FP_RX * FP_RX) + (ey2 * ey2) / (FP_RY * FP_RY);
            if (normD > 0.25f) continue; // 中心50%以内のみ起点にする

            float baseD = primaryRemoved[fx, fy];
            float stepRatio = TAIL_RATIO;

            for (int step = 1; step <= TAIL_DEPTH; step++)
            {
                // 滑落方向に step グリッド進む
                int tx = Mathf.Clamp(fx + Mathf.RoundToInt(_downhillDir.x * step * 1.5f), 0, GRID_W - 1);
                int ty = Mathf.Clamp(fy + Mathf.RoundToInt(_downhillDir.y * step * 1.5f), 0, GRID_H - 1);
                if (tx == fx && ty == fy) { stepRatio *= TAIL_DECAY; continue; } // 動いていない

                // テール横幅: ±TAIL_WIDTH セル
                for (int tw = -TAIL_WIDTH; tw <= TAIL_WIDTH; tw++)
                {
                    int wx = Mathf.Clamp(tx + tw, 0, GRID_W - 1);
                    if (_snow[wx, ty] <= EXPOSED_CELL_THRESHOLD) continue;
                    if (totalDelta + secondaryAmount >= TAP_TOTAL_CAP) goto fp_done;

                    // 横方向にも減衰（中心列が最大）
                    float widthDecay = 1f - Mathf.Abs(tw) / (float)(TAIL_WIDTH + 1);
                    float sd = Mathf.Min(baseD * stepRatio * widthDecay, _snow[wx, ty]);
                    if (sd <= 0f) continue;

                    _snow[wx, ty]   -= sd;
                    secondaryAmount += sd;
                    secondaryCells++;

                    if (step == 1) tailMidAmount += sd;
                    else           tailEndAmount += sd;
                }
                stepRatio *= TAIL_DECAY; // 段ごとに減衰
            }
        }
        fp_done:

        totalDelta += secondaryAmount;

        // primary > tail 検証
        float primaryAvg = primaryCells > 0 ? (totalDelta - secondaryAmount) / primaryCells : 0f;
        float tailAvg    = secondaryCells > 0 ? secondaryAmount / secondaryCells : 0f;
        bool  primaryGtTail = primaryAvg > tailAvg;

        float totalVisualSlide = secondaryAmount;
        int   hitCells         = primaryCells + secondaryCells;

        // [REMOVE_ENTRY] 減算完了確認
        Debug.Log($"[REMOVE_ENTRY] class=SnowStrip2D method=HandleTap" +
                  $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                  $" primaryCells={primaryCells} secondaryCells={secondaryCells}" +
                  $" totalDelta={totalDelta:F3}");

        // [TAP_PRIMARY_REMOVAL] 主削り量
        float primaryTotal = totalDelta - secondaryAmount;
        Debug.Log($"[TAP_PRIMARY_REMOVAL] roof={TARGET_ROOF_ID}" +
                  $" primaryTotal={primaryTotal:F3} primaryCells={primaryCells}" +
                  $" primaryAvgPerCell={primaryAvg:F4}");

        // [SNOW_FIRST_HIT_VARIATION] 初撃ばらつきログ
        float fillAfterFirstHit = CalcFill();
        bool  exposedAfterHit   = fillAfterFirstHit < 0.95f; // 5%以上削れたら露出あり
        Debug.Log($"[SNOW_FIRST_HIT_VARIATION] roof={TARGET_ROOF_ID}" +
                  $" variation={firstHitVariation} fpMax={FP_MAX:F2}" +
                  $" firstHitRemoved={primaryTotal:F3}" +
                  $" exposedAfterFirstHit={(exposedAfterHit ? "YES" : "NO")}" +
                  $" fillBefore={fillBefore:F3} fillAfterHit={fillAfterFirstHit:F3}");
        Debug.Log($"[FIRST_HIT_EXPOSE_CHECK] roof={TARGET_ROOF_ID}" +
                  $" firstHitRemovedAmount={primaryTotal:F3}" +
                  $" exposedAfterFirstHit={(exposedAfterHit ? "YES" : "NO")}" +
                  $" exposureThreshold=CELL_EPSILON(0.08)" +
                  $" firstHitVariationEnabled=YES fpMaxRange=[0.20-0.75]" +
                  $" variation={firstHitVariation}");

        // [SLIDE_TAIL_REMOVAL] テール削り量 + primary > tail 検証
        Debug.Log($"[SLIDE_TAIL_REMOVAL] roof={TARGET_ROOF_ID}" +
                  $" tailMid={tailMidAmount:F3} tailEnd={tailEndAmount:F3}" +
                  $" tailTotal={secondaryAmount:F3} tailCells={secondaryCells}" +
                  $" tailAvgPerCell={tailAvg:F4}" +
                  $" primaryGtTail={(primaryGtTail ? "YES" : "NO")}" +
                  $" ratio=primary/tail={(tailAvg > 0f ? primaryAvg / tailAvg : 999f):F1}x");

        // [VISUAL_SLIDE_ENTRY] secondary（テール）量確認
        Debug.Log($"[VISUAL_SLIDE_ENTRY] class=SnowStrip2D method=HandleTap" +
                  $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                  $" secondaryAmount={secondaryAmount:F3}");

        _texDirty = true;

        // ── ゼロスナップ: CELL_EPSILON 以下のセルを 0 に丸める ──
        int zeroSnapCount = 0;
        for (int x = 0; x < GRID_W; x++)
        for (int y = 0; y < GRID_H; y++)
        {
            if (_snow[x, y] > 0f && _snow[x, y] <= CELL_EPSILON)
            {
                _snow[x, y] = 0f;
                zeroSnapCount++;
            }
        }

        // ── finish assist: 残雪 FINISH_THRESHOLD(5%) 以下なら全ゼロ化 ──
        // 突然全消えに見えないよう閾値を小さく設定（5% = 24セル相当）
        float fillMid      = CalcFill();
        bool  finishAssist = false;
        if (fillMid > 0f && fillMid <= FINISH_THRESHOLD)
        {
            for (int x = 0; x < GRID_W; x++)
            for (int y = 0; y < GRID_H; y++)
                _snow[x, y] = 0f;
            finishAssist = true;
        }

        float fillAfter          = CalcFill();
        float totalRoofSnowAfter = fillAfter * GRID_W * GRID_H;

        // ── spawn 停止条件（すべて満たす場合のみ spawn）────────
        // 条件1: finishAssist でない
        // 条件2: 実際に削った量が SPAWN_MIN_DELTA 以上
        // 条件3: selected cell が露出でない（exposedAtHit=false を通過済み）
        // 条件4: ブラシ内に雪があった（totalSnowInBrush>0 を通過済み）
        bool spawned   = !finishAssist && totalDelta >= SPAWN_MIN_DELTA;
        int  spawnCount = 0;

        if (spawned)
        {
            // ── 落雪量3段階バラつき ────────────────────────────
            // 基準: totalDelta / BRUSH_MAX（0〜1程度）
            // ランダム係数を乗せて小/通常/大に分岐
            float baseRatio = totalDelta / BRUSH_MAX;
            float rnd       = Random.value; // 0〜1

            string fallVariation;
            if (rnd < 0.25f || baseRatio < 0.3f)
            {
                // 小崩れ: 25%確率 or 削り量少ない時
                spawnCount    = Random.Range(1, 3); // 1〜2
                fallVariation = "small";
            }
            else if (rnd < 0.80f)
            {
                // 通常: 55%確率
                spawnCount    = Random.Range(2, 5); // 2〜4
                fallVariation = "normal";
            }
            else
            {
                // 大崩れ: 20%確率
                spawnCount    = Random.Range(5, 8); // 5〜7
                fallVariation = "large";
            }

            // コンボ中は大崩れ確率UP（通常→大 に格上げ）
            if (_avalancheChain > 0 && fallVariation == "normal")
            {
                spawnCount    = Random.Range(4, 7);
                fallVariation = "large(combo)";
            }

            Debug.Log($"[SNOW_FALL_VARIATION] roof={TARGET_ROOF_ID}" +
                      $" variation={fallVariation} spawnCount={spawnCount}" +
                      $" totalDelta={totalDelta:F3} baseRatio={baseRatio:F2}" +
                      $" rnd={rnd:F2} comboCount={_comboCount}");

            // [SPAWN_ENTRY] spawn実行確認
            Debug.Log($"[SPAWN_ENTRY] class=SnowStrip2D method=HandleTap" +
                      $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                      $" spawnCount={spawnCount} totalDelta={totalDelta:F3}");

            // スポーン位置: 屋根上端ではなくタップ位置（屋根面上）
            // → 「上に飛び出す」現象を防ぐ
            const float SLIDE_DURATION  = 0.35f;  // スライドフェーズの秒数
            const float SLIDE_SPD       = 75f;    // 旧160→75: 重い雪らしいゆっくり滑落
            const float RELEASE_DELAY   = 0.20f;  // 叩いてから動き出すまでの"ため"（秒）

            Debug.Log($"[SNOW_RELEASE_DELAY] roof={TARGET_ROOF_ID}" +
                      $" releaseDelaySec={RELEASE_DELAY:F2}");
            Debug.Log($"[SNOW_SLIDE_SPEED] roof={TARGET_ROOF_ID}" +
                      $" slideSpeedBefore=160 slideSpeedAfter={SLIDE_SPD:F0}");

            float roofW  = _guiRect.width;
            // spawn X: タップ位置付近（屋根面上）
            float spawnX = Mathf.Clamp(guiPos.x, _guiRect.x + 8f, _guiRect.xMax - 8f);
            // spawn Y: 屋根の中央付近（_guiRect.y = 上端、yMax = 下端）
            float spawnY = Mathf.Lerp(_guiRect.y, _guiRect.yMax, 0.3f);

            // ── 叩き雪煙: 雪セルにヒットした時のみ ──────────────
            // 大中小: totalDelta に基づいて分類
            float puffDelta = totalDelta;
            string puffSize = puffDelta > 2.0f ? "large" : (puffDelta > 0.8f ? "medium" : "small");
            int puffCount = puffDelta > 2.0f ? 5 : (puffDelta > 0.8f ? 3 : 2);
            float puffBaseSize = puffDelta > 2.0f ? 28f : (puffDelta > 0.8f ? 18f : 10f);

            for (int pi = 0; pi < puffCount; pi++)
            {
                float pjx = Random.Range(-12f, 12f);
                float pjy = Random.Range(-8f, 8f);
                float psz = puffBaseSize * Random.Range(0.7f, 1.4f);
                float pl  = Random.Range(0.4f, 0.7f);
                _puffs.Add(new Puff
                {
                    pos     = new Vector2(spawnX + pjx, spawnY + pjy),
                    vel     = new Vector2(Random.Range(-20f, 20f), Random.Range(-30f, -10f)),
                    size    = psz,
                    life    = pl,
                    maxLife = pl,
                    alpha   = 1f,
                    kind    = 0,
                });
            }
            Debug.Log($"[SNOW_PUFF_HIT] roof={TARGET_ROOF_ID} puffSize={puffSize}" +
                      $" puffCount={puffCount} puffBaseSize={puffBaseSize:F0}" +
                      $" totalDelta={totalDelta:F3}" +
                      $" pos=({spawnX:F0},{spawnY:F0})");

            for (int i = 0; i < spawnCount; i++)
            {
                float jx = Random.Range(-roofW * 0.12f, roofW * 0.12f);

                // サイズ: 小〜中を中心に（大きすぎる塊を抑制）
                // 70%確率で小(6〜14px)、30%確率で中(14〜20px)
                float sz;
                if (Random.value < 0.70f)
                    sz = Mathf.Clamp(roofW * Random.Range(0.04f, 0.09f), 6f, 14f);  // 小
                else
                    sz = Mathf.Clamp(roofW * Random.Range(0.09f, 0.13f), 14f, 20f); // 中

                // ── 不定形ビジュアルパラメータ（強化版）──────────
                // scaleJitter: 縦横比を大きくばらつかせる（角張り感を消す）
                // 旧: [0.45, 1.55] → 新: [0.35, 1.70]（さらに幅広く）
                const float SCALE_JITTER_MIN = 0.35f;
                const float SCALE_JITTER_MAX = 1.70f;
                float sx  = Random.Range(SCALE_JITTER_MIN, SCALE_JITTER_MAX);
                float sy  = Random.Range(SCALE_JITTER_MIN, SCALE_JITTER_MAX);

                // vertexNoise: 回転を大きくばらつかせる
                // 旧: 45° → 新: 60°（より自然な崩れ感）
                const float VERTEX_NOISE_DEG = 60f;
                float rot = Random.Range(-VERTEX_NOISE_DEG, VERTEX_NOISE_DEG);

                // 副塊数: 2〜4個（塊感を強める。1個だけは禁止）
                int subN = Random.Range(2, 4); // 2,3

                // 副塊の相対オフセット・スケール（親サイズ比）
                // オフセット幅を広げて「散らばった塊」感を出す
                Vector2 s0o = new Vector2(Random.Range(-1.0f, 1.0f), Random.Range(-0.7f, 0.7f));
                float   s0s = Random.Range(0.40f, 0.70f);
                Vector2 s1o = new Vector2(Random.Range(-1.1f, 1.1f), Random.Range(-0.8f, 0.8f));
                float   s1s = Random.Range(0.30f, 0.60f);
                Vector2 s2o = new Vector2(Random.Range(-1.2f, 1.2f), Random.Range(-0.9f, 0.9f));
                float   s2s = Random.Range(0.20f, 0.50f);

                // 白〜薄青〜薄灰のばらつき（自然な雪色）
                Color sc = new Color(
                    Random.Range(0.83f, 1.00f),
                    Random.Range(0.88f, 1.00f),
                    Random.Range(0.94f, 1.00f));

                Debug.Log($"[SNOW_CHUNK_SHAPE] roof={TARGET_ROOF_ID} idx={i}" +
                          $" size={sz:F1} scaleX={sx:F2} scaleY={sy:F2}" +
                          $" rotation={rot:F1} subCount={subN}" +
                          $" roundness=enhanced vertexNoise={VERTEX_NOISE_DEG:F0}deg" +
                          $" scaleJitter=[{SCALE_JITTER_MIN:F2},{SCALE_JITTER_MAX:F2}]" +
                          $" minScale={sz * SCALE_JITTER_MIN:F1} maxScale={sz * SCALE_JITTER_MAX:F1}" +
                          $" clusterSizeRange=[{subN},{subN + 1}]" +
                          $" color=({sc.r:F2},{sc.g:F2},{sc.b:F2})");

                // 初速: downhill 方向のみ（上向き成分なし）
                Vector2 slideVel = _downhillDir * SLIDE_SPD;

                // 回転角速度: 滑落中にゆっくり回転（自然な崩れ感）
                float rv = Random.Range(-18f, 18f); // 度/秒

                _pieces.Add(new Piece
                {
                    pos          = new Vector2(spawnX + jx, spawnY),
                    vel          = slideVel,
                    size         = sz,
                    life         = 5f,
                    alpha        = 1f,
                    slideTimer   = 999f,
                    slideActive  = true,
                    currentMass  = 0.5f + totalDelta * 0.1f,
                    engulfBudget = 2.0f,
                    engulfTotal  = 0f,
                    scaleX       = sx,
                    scaleY       = sy,
                    rotation     = rot,
                    rotVel       = rv,
                    chunkCount   = subN,
                    snowColor    = sc,
                    subCount     = subN,
                    sub0Offset   = s0o, sub0Scale = s0s,
                    sub1Offset   = s1o, sub1Scale = s1s,
                    sub2Offset   = s2o, sub2Scale = s2s,
                    delayTimer   = RELEASE_DELAY + Random.Range(-0.03f, 0.05f),
                });
            }

            Debug.Log($"[2D_FP#{_tapCount}] spawnCount={spawnCount}" +
                      $" spawnPos=({spawnX:F0},{spawnY:F0})" +
                      $" downhill=({_downhillDir.x:F2},{_downhillDir.y:F2})" +
                      $" slideDuration={SLIDE_DURATION} slideSpd={SLIDE_SPD}");
        }

        _lastInfo    = $"TAP#{_tapCount} fill={fillAfter:F2} sp={(spawned ? spawnCount.ToString() : "NO")}";
        _lastSpawned = spawned;

        int exposedCellCount = 0;
        for (int ex = 0; ex < GRID_W; ex++)
        for (int ey = 0; ey < GRID_H; ey++)
            if (_snow[ex, ey] <= EXPOSED_CELL_THRESHOLD) exposedCellCount++;
        float exposedAreaRatio = (float)exposedCellCount / (GRID_W * GRID_H);

        Debug.Log($"[2D_FP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                  $" tapCount={_tapCount}" +
                  $" hitPos=({guiPos.x:F0},{guiPos.y:F0})" +
                  $" rawCell=({rawCx},{rawCy})" +
                  $" fpRX={FP_RX} fpRY={FP_RY}" +
                  $" primaryCells={primaryCells} secondaryCells={secondaryCells}" +
                  $" totalRemovedThisTap={totalDelta:F2}" +
                  $" totalRoofSnowBefore={totalRoofSnowBefore:F1} totalRoofSnowAfter={totalRoofSnowAfter:F1}" +
                  $" fillBefore={fillBefore:F3} fillAfter={fillAfter:F3}" +
                  $" exposedAreaRatio={exposedAreaRatio:F2}" +
                  $" zeroSnapCount={zeroSnapCount}" +
                  $" finishAssist={(finishAssist ? "YES" : "NO")}" +
                  $" spawned={(spawned ? $"YES({spawnCount})" : "NO")}" +
                  $" TAP_TOTAL_CAP={TAP_TOTAL_CAP:F0}");

        if (fillAfter <= 0f)
            Debug.Log($"[2D_FP#{_tapCount}] roof={TARGET_ROOF_ID} fill=0 allCleared=YES");

        // ── コンボ・雪崩判定 ─────────────────────────────────
        // 雪崩トリガー条件:
        //   A. 連続ヒット3回以上（_comboCount >= 3）
        //   B. 巻き込み量が一定以上（_lastEngulfTotal >= 1.5）
        // 最大連鎖: 3回（_avalancheChain <= 3）
        // 減衰: 連鎖ごとに追加 spawn 数を減らす
        const int   AVALANCHE_MAX_CHAIN   = 3;
        const float AVALANCHE_ENGULF_THR  = 1.5f;
        const int   AVALANCHE_COMBO_THR   = 3;

        bool avalancheTriggered = false;
        int  avalancheExtraSpawn = 0;
        float avalancheEngulf    = _lastEngulfTotal;

        if (spawned && _avalancheChain < AVALANCHE_MAX_CHAIN)
        {
            bool condEngulf = _lastEngulfTotal >= AVALANCHE_ENGULF_THR;
            bool condCombo  = _comboCount >= AVALANCHE_COMBO_THR;

            if (condEngulf || condCombo)
            {
                avalancheTriggered = true;
                _avalancheChain++;

                // 連鎖ごとに追加 spawn を減衰（3→2→1）
                avalancheExtraSpawn = Mathf.Max(1, AVALANCHE_MAX_CHAIN - _avalancheChain + 1);

                // 広域巻き込み: footprint を広げて周辺セルを追加削除
                const float AVA_EXTRA_R = 9f; // 追加巻き込み半径（旧3×3倍）
                const float AVA_TAKE    = 0.25f;
                float avaRemoved = 0f;
                int   avaCells   = 0;
                for (int ax = Mathf.Max(0, rawCx - (int)AVA_EXTRA_R);
                         ax <= Mathf.Min(GRID_W - 1, rawCx + (int)AVA_EXTRA_R); ax++)
                for (int ay = Mathf.Max(0, rawCy - (int)AVA_EXTRA_R);
                         ay <= Mathf.Min(GRID_H - 1, rawCy + (int)AVA_EXTRA_R); ay++)
                {
                    if (_snow[ax, ay] > 0.01f)
                    {
                        float take = Mathf.Min(_snow[ax, ay] * AVA_TAKE, _snow[ax, ay]);
                        _snow[ax, ay] -= take;
                        avaRemoved    += take;
                        avaCells++;
                    }
                }
                _texDirty = true;

                // 雪崩 spawn を追加
                float avaSpawnX = Mathf.Clamp(guiPos.x, _guiRect.x + 8f, _guiRect.xMax - 8f);
                float avaSpawnY = Mathf.Lerp(_guiRect.y, _guiRect.yMax, 0.3f);
                float avaRoofW  = _guiRect.width;
                for (int ai = 0; ai < avalancheExtraSpawn; ai++)
                {
                    float ajx = Random.Range(-avaRoofW * 0.15f, avaRoofW * 0.15f);
                    float asz = Mathf.Clamp(avaRoofW * Random.Range(0.06f, 0.14f), 8f, 26f);
                    float arv = Random.Range(-30f, 30f);
                    float asx = Random.Range(0.5f, 1.4f);
                    float asy = Random.Range(0.5f, 1.2f);
                    Color asc = new Color(
                        Random.Range(0.88f, 1.00f),
                        Random.Range(0.92f, 1.00f),
                        Random.Range(0.96f, 1.00f));
                    const float AVA_SLIDE_SPD = 75f;
                    _pieces.Add(new Piece
                    {
                        pos          = new Vector2(avaSpawnX + ajx, avaSpawnY),
                        vel          = _downhillDir * AVA_SLIDE_SPD,
                        size         = asz,
                        life         = 5f,
                        alpha        = 1f,
                        slideTimer   = 999f,
                        slideActive  = true,
                        currentMass  = 0.8f + avaRemoved * 0.2f,
                        engulfBudget = 2.0f,
                        engulfTotal  = 0f,
                        scaleX       = asx,
                        scaleY       = asy,
                        rotation     = Random.Range(-40f, 40f),
                        rotVel       = arv,
                        chunkCount   = Random.Range(1, 3),
                        snowColor    = asc,
                        subCount     = Random.Range(0, 2),
                        sub0Offset   = new Vector2(Random.Range(-0.6f, 0.6f), Random.Range(-0.4f, 0.4f)),
                        sub0Scale    = Random.Range(0.3f, 0.6f),
                        sub1Offset   = Vector2.zero,
                        sub1Scale    = 0f,
                        sub2Offset   = Vector2.zero,
                        sub2Scale    = 0f,
                        delayTimer   = Random.Range(0.05f, 0.15f),
                    });
                }

                Debug.Log($"[AVALANCHE_TRIGGER] roof={TARGET_ROOF_ID}" +
                          $" triggered=YES chain={_avalancheChain}/{AVALANCHE_MAX_CHAIN}" +
                          $" condEngulf={condEngulf} condCombo={condCombo}" +
                          $" engulfAmount={avalancheEngulf:F3} comboCount={_comboCount}" +
                          $" extraSpawn={avalancheExtraSpawn} avaRemoved={avaRemoved:F3}" +
                          $" avaCells={avaCells}");
            }
            else
            {
                // 雪崩未発生: 連鎖リセット
                _avalancheChain = 0;
                Debug.Log($"[AVALANCHE_TRIGGER] roof={TARGET_ROOF_ID}" +
                          $" triggered=NO chain=0" +
                          $" engulfAmount={avalancheEngulf:F3} comboCount={_comboCount}");
            }
        }
        else if (!spawned)
        {
            // 露出セルタップ or 微小削り → コンボリセット
            _comboCount     = 0;
            _avalancheChain = 0;
        }

        // コンボカウント更新（雪にヒットした場合のみ加算）
        if (spawned) _comboCount++;
        else         _comboCount = 0;

        // 次タップ用に巻き込み量を記録（UpdatePieces で更新される前の値）
        // UpdatePieces 側で _lastEngulfTotal を更新するため、ここでは 0 リセット
        _lastEngulfTotal = 0f;
    }

    float CalcFill()
    {
        float s = 0f;
        for (int x = 0; x < GRID_W; x++)
        for (int y = 0; y < GRID_H; y++)
            s += _snow[x, y];
        return s / (GRID_W * GRID_H);
    }

    // ── 落下片の更新 ─────────────────────────────────────────
    //
    // 【抵抗ベース滑落: 止まる or 突破する】
    //
    // slideActive=true の間:
    //   1. 前方セル（downhill 1グリッド先）の snow 合計 = frontResistance
    //   2. currentMass < frontResistance * RESIST_MULT → 減速・停止
    //   3. currentMass >= frontResistance * RESIST_MULT → 突破しつつ吸収
    //   4. 軒先到達 → 落下フェーズへ移行
    //
    void UpdatePieces()
    {
        // 抵抗倍率: frontResistance * この値 が停止閾値
        const float RESIST_MULT   = 1.2f;
        // 減速係数（停止方向時、毎フレーム vel をこの割合で減らす）
        const float DECEL         = 6f;
        // 停止判定速度（これ以下で slideActive=false）
        const float STOP_VEL      = 8f;
        // 突破時の吸収割合（前方 snow のこの割合を currentMass に加算）
        const float ABSORB_RATE   = 0.5f;
        // 1回の滑落での累計吸収上限（暴走防止）
        const float ENGULF_CAP    = 4.0f;
        // 吸収対象の横幅
        const int   SWEEP_R       = 1;
        const float EXPOSED_THR   = 0.01f;

        float dt = Time.deltaTime;
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];

            // ── 滑落開始遅延（"ため"フェーズ）──────────────────
            if (p.delayTimer > 0f)
            {
                p.delayTimer -= dt;
                p.life       -= dt;
                p.alpha       = Mathf.Clamp01(p.life * 0.8f);
                if (p.life <= 0f) _pieces.RemoveAt(i);
                else              _pieces[i] = p;
                continue; // まだ動かない
            }

            if (p.slideActive)
            {
                // 滑落中: 回転角速度を適用（自然な崩れ感）
                p.rotation += p.rotVel * dt;

                bool transitionToFall = false;

                if (_ready && _guiRect.width > 1f)
                {
                    // 現在位置 → グリッド座標
                    float nx  = Mathf.Clamp01((p.pos.x - _guiRect.x) / _guiRect.width);
                    float ny  = Mathf.Clamp01((p.pos.y - _guiRect.y) / _guiRect.height);
                    float pgx = nx * GRID_W;
                    float pgy = ny * GRID_H;

                    int cgx = Mathf.Clamp(Mathf.FloorToInt(pgx), 0, GRID_W - 1);
                    int cgy = Mathf.Clamp(Mathf.FloorToInt(pgy), 0, GRID_H - 1);

                    // 前方セル: downhill 方向に1グリッド先
                    // _downhillDir は GUI 方向の正規化ベクトル
                    // → グリッド単位に変換（y が主方向のため GRID_H で scale）
                    int fgx = Mathf.Clamp(cgx + Mathf.RoundToInt(_downhillDir.x * 2f), 0, GRID_W - 1);
                    int fgy = Mathf.Clamp(cgy + Mathf.RoundToInt(_downhillDir.y * 2f), 0, GRID_H - 1);

                    // frontResistance: 前方セル群の snow 合計
                    float frontResistance = 0f;
                    for (int sx = Mathf.Max(0, fgx - SWEEP_R);
                             sx <= Mathf.Min(GRID_W - 1, fgx + SWEEP_R); sx++)
                        frontResistance += _snow[sx, fgy];

                    bool stopped       = false;
                    bool breakthrough  = false;
                    float frameEngulf  = 0f;
                    int   contactCells = 0;

                    if (frontResistance > EXPOSED_THR)
                    {
                        float threshold = frontResistance * RESIST_MULT;

                        if (p.currentMass < threshold)
                        {
                            // ── 停止方向: 減速 ────────────────────
                            p.vel *= Mathf.Max(0f, 1f - DECEL * dt);

                            if (p.vel.magnitude <= STOP_VEL)
                            {
                                // 完全停止
                                p.slideActive = false;
                                p.vel         = Vector2.zero;
                                p.life        = Mathf.Min(p.life, 1.0f);
                                stopped       = true;

                                Debug.Log($"[2D_SLIDE_STOP] roof={TARGET_ROOF_ID}" +
                                          $" pos=({p.pos.x:F0},{p.pos.y:F0})" +
                                          $" currentMass={p.currentMass:F3}" +
                                          $" frontResistance={frontResistance:F3}" +
                                          $" threshold={threshold:F3}" +
                                          $" stop=YES breakthrough=NO" +
                                          $" totalEngulfed={p.engulfTotal:F3}");
                            }
                        }
                        else
                        {
                            // ── 突破: 前方雪を吸収しながら進む ────
                            breakthrough = true;

                            for (int sx = Mathf.Max(0, fgx - SWEEP_R);
                                     sx <= Mathf.Min(GRID_W - 1, fgx + SWEEP_R); sx++)
                            {
                                if (_snow[sx, fgy] <= EXPOSED_THR) continue;
                                if (p.engulfTotal >= ENGULF_CAP) break;

                                float take = Mathf.Min(
                                    _snow[sx, fgy] * ABSORB_RATE,
                                    ENGULF_CAP - p.engulfTotal);
                                if (take <= 0f) continue;

                                _snow[sx, fgy] -= take;
                                p.currentMass  += take * 0.5f;
                                p.engulfTotal  += take;
                                frameEngulf    += take;
                                contactCells++;
                                _texDirty = true;
                                // 雪崩判定用: 全 Piece 中の最大巻き込み量を追跡
                                if (p.engulfTotal > _lastEngulfTotal)
                                    _lastEngulfTotal = p.engulfTotal;
                            }

                            Debug.Log($"[ENGULF_ENTRY] roof={TARGET_ROOF_ID}" +
                                      $" frame={Time.frameCount}" +
                                      $" slidePos=({p.pos.x:F0},{p.pos.y:F0})" +
                                      $" cell=({fgx},{fgy})" +
                                      $" currentMass={p.currentMass:F3}" +
                                      $" frontResistance={frontResistance:F3}" +
                                      $" absorbed={frameEngulf:F3}" +
                                      $" totalEngulfed={p.engulfTotal:F3}" +
                                      $" stop=NO breakthrough=YES");
                        }
                    }

                    // 移動（停止していなければ）
                    if (!stopped) p.pos += p.vel * dt;

                    // 軒先到達 or 屋根下端 → 落下フェーズへ移行
                    if (p.pos.y >= _eaveGuiY || ny >= 1f)
                    {
                        transitionToFall = true;
                        Debug.Log($"[2D_SLIDE_EAVE] roof={TARGET_ROOF_ID}" +
                                  $" pos=({p.pos.x:F0},{p.pos.y:F0})" +
                                  $" currentMass={p.currentMass:F3}" +
                                  $" totalEngulfed={p.engulfTotal:F3} reachedEave=YES");

                        // 軒落下時の雪煙: 大中小を currentMass で分類
                        string eavePuffSz = p.currentMass > 1.5f ? "large" :
                                            (p.currentMass > 0.7f ? "medium" : "small");
                        int eavePuffN = p.currentMass > 1.5f ? 4 : (p.currentMass > 0.7f ? 3 : 2);
                        float eavePuffBase = p.currentMass > 1.5f ? 22f : (p.currentMass > 0.7f ? 14f : 8f);
                        for (int pi2 = 0; pi2 < eavePuffN; pi2++)
                        {
                            float pjx = Random.Range(-10f, 10f);
                            float pjy = Random.Range(-6f, 6f);
                            float psz = eavePuffBase * Random.Range(0.7f, 1.4f);
                            float pl  = Random.Range(0.5f, 0.9f);
                            _puffs.Add(new Puff
                            {
                                pos     = new Vector2(p.pos.x + pjx, p.pos.y + pjy),
                                vel     = new Vector2(Random.Range(-15f, 15f), Random.Range(-20f, 5f)),
                                size    = psz,
                                life    = pl,
                                maxLife = pl,
                                alpha   = 1f,
                                kind    = 1,
                            });
                        }
                        Debug.Log($"[SNOW_PUFF_EAVE] roof={TARGET_ROOF_ID}" +
                                  $" puffSize={eavePuffSz} puffCount={eavePuffN}" +
                                  $" currentMass={p.currentMass:F3}" +
                                  $" pos=({p.pos.x:F0},{p.pos.y:F0})");
                    }

                    // 屋根左右外に出たら停止
                    if (nx <= 0f || nx >= 1f)
                    {
                        p.slideActive = false;
                        p.vel         = Vector2.zero;
                        p.life        = Mathf.Min(p.life, 0.6f);
                    }
                }
                else
                {
                    // _guiRect 未準備時は素通り移動
                    p.pos += p.vel * dt;
                }

                if (transitionToFall)
                {
                    p.slideActive = false;
                    p.vel = new Vector2(p.vel.x * 0.3f, Mathf.Max(p.vel.y, 80f));
                    // 軒先で姿勢変化: 回転速度を増やす（引っかかりで崩れる感）
                    p.rotVel += Random.Range(-60f, 60f);
                    Debug.Log($"[SNOW_CHUNK_BREAK] roof={TARGET_ROOF_ID}" +
                              $" eaveInteraction=YES rotVel={p.rotVel:F1}" +
                              $" splitCount=0 rotationApplied=YES");
                }

                p.slideTimer = p.slideActive ? 999f : 0f;
            }
            else
            {
                // ── 自由落下フェーズ ──────────────────────────
                p.vel.y    += 500f * dt;
                p.pos      += p.vel * dt;
                p.rotation += p.rotVel * dt; // 落下中も回転継続
            }

            p.life  -= dt;
            p.alpha  = Mathf.Clamp01(p.life * 0.8f);

            if (p.pos.y >= _eaveGuiY)
            {
                bool wasMoving = p.vel.magnitude > 20f;
                p.pos.y = _eaveGuiY;
                p.vel   = Vector2.zero;
                p.life  = Mathf.Min(p.life, 1.2f);

                // 地面着弾雪煙: 速度があった時のみ（停止から来たPuffは出さない）
                if (wasMoving && !p.slideActive)
                {
                    float gPuffBase = p.currentMass > 1.5f ? 20f : (p.currentMass > 0.7f ? 13f : 7f);
                    string gPuffSz  = p.currentMass > 1.5f ? "large" :
                                      (p.currentMass > 0.7f ? "medium" : "small");
                    int gPuffN = p.currentMass > 1.5f ? 4 : (p.currentMass > 0.7f ? 3 : 2);
                    for (int pi3 = 0; pi3 < gPuffN; pi3++)
                    {
                        float pjx = Random.Range(-14f, 14f);
                        float psz = gPuffBase * Random.Range(0.8f, 1.5f);
                        float pl  = Random.Range(0.4f, 0.8f);
                        _puffs.Add(new Puff
                        {
                            pos     = new Vector2(p.pos.x + pjx, p.pos.y),
                            vel     = new Vector2(Random.Range(-25f, 25f), Random.Range(-40f, -10f)),
                            size    = psz,
                            life    = pl,
                            maxLife = pl,
                            alpha   = 1f,
                            kind    = 2,
                        });
                    }
                    Debug.Log($"[SNOW_PUFF_GROUND] roof={TARGET_ROOF_ID}" +
                              $" puffSize={gPuffSz} puffCount={gPuffN}" +
                              $" currentMass={p.currentMass:F3}" +
                              $" pos=({p.pos.x:F0},{p.pos.y:F0})");
                }
            }
            if (p.life <= 0f) _pieces.RemoveAt(i);
            else              _pieces[i] = p;
        }
    }

    // 副塊1個を描画するヘルパー（OnGUI 内から呼ぶ）
    void DrawSubChunk(Piece p, Vector2 offsetRatio, float scaleRatio, Color baseColor, float rot)
    {
        float sz  = p.size * scaleRatio;
        float w2  = sz * p.scaleX;
        float h2  = sz * p.scaleY;
        float cx  = p.pos.x + offsetRatio.x * p.size;
        float cy  = p.pos.y + offsetRatio.y * p.size;

        // 副塊用テクスチャ: 親と異なる種類を選ぶ（ばらつき感）
        bool hasChunkTex = _chunkTextures != null && _chunkTexBuilt;
        int subTexIdx = Mathf.Clamp((p.subCount + 2) % 6, 0, 5);
        Texture2D subTex = (hasChunkTex && _chunkTextures[subTexIdx] != null)
            ? _chunkTextures[subTexIdx] : _chunkTextures?[0] ?? Texture2D.whiteTexture;

        // 副塊は親と同じ alpha（0.85 削りを廃止して半透明感を解消）
        Color c   = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a);
        var saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(rot, new Vector2(cx, cy));
        GUI.color = c;
        GUI.DrawTexture(new Rect(cx - w2 * 0.5f, cy - h2 * 0.5f, w2, h2),
                        subTex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.matrix = saved;
    }

    void UpdatePuffs()
    {
        float dt = Time.deltaTime;
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var pf = _puffs[i];
            pf.vel.y -= 60f * dt; // 上昇気流（雪煙が少し浮く）
            pf.pos   += pf.vel * dt;
            pf.life  -= dt;
            pf.alpha  = Mathf.Clamp01(pf.life / pf.maxLife);
            if (pf.life <= 0f) _puffs.RemoveAt(i);
            else               _puffs[i] = pf;
        }
    }

    // ── 描画 ─────────────────────────────────────────────────
    //
    // 【単一参照元の原則】
    //   描画は _snow[x,y] → _snowTex のアルファだけで制御する。
    //   描画矩形の高さは固定（fillAvg で縮めない）。
    //   fillAvg で高さを変えると「帯状に短くなる」症状が出るため廃止。
    //
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (!_ready || _snowTex == null) return;

        // OnGUI 開始時に必ず Color.white にリセット（前フレームの汚染を防ぐ）
        GUI.color = Color.white;

        // 描画矩形: 常に固定サイズ。_snowTex のアルファが唯一のマスク。
        // fillAvg で高さを縮めない → 帯状症状を根本解消。
        float snowTop = _guiRect.y - EXPAND_Y_MAX;
        float snowH   = _guiRect.height * THICK_RATIO + EXPAND_Y_MAX;

        // 全体残雪（デバッグ表示・ゲージ用のみ。描画矩形には使わない）
        float fillAvg = CalcFill();

        if (fillAvg > 0f)
        {
            // _snowTex のアルファマスクで円形に削れた見た目を表現
            // GUI.color は必ず Color.white（alpha=1）で描画する
            // → GUI.color.a が 1 未満だと DrawTexture 全体が半透明になる
            GUI.color = Color.white;
            GUI.DrawTexture(
                new Rect(_guiRect.x, snowTop, _guiRect.width, snowH),
                _snowTex,
                ScaleMode.StretchToFill,
                alphaBlend: true
            );

            // ── 上縁の自然なエッジ描画 ────────────────────────
            float edgeW = _guiRect.width;
            float edgeX = _guiRect.x;
            Texture2D edgeTex = (_snowEdgeTex != null) ? _snowEdgeTex : Texture2D.whiteTexture;

            // 上縁ノイズ帯（雪面の凹凸感）
            GUI.color = new Color(0.96f, 0.98f, 1.0f, 0.55f);
            GUI.DrawTexture(new Rect(edgeX, snowTop - 2f, edgeW, 10f),
                            edgeTex, ScaleMode.StretchToFill, alphaBlend: true);
            // 薄い光沢層（雪面の光反射）
            GUI.color = new Color(1f, 1f, 1f, 0.20f);
            GUI.DrawTexture(new Rect(edgeX + edgeW * 0.05f, snowTop + 1f,
                                     edgeW * 0.90f, 5f),
                            edgeTex, ScaleMode.StretchToFill, alphaBlend: true);

            // ── 露出跡の窪み影（_snowTex の透明部分に合わせて描画）──
            // _snowTex の alpha=0 領域（露出跡）に暗い影を重ねて「窪み」を表現。
            // 影は _snowTex と同じ Rect に描画し、_snowTex の透明部分だけ見える。
            // _puffTex（ソフト円形）を使って影の輪郭も丸くする。
            if (_puffTex != null)
            {
                // 露出跡の影: 青灰色で薄く（窪んで見える程度）
                GUI.color = new Color(0.35f, 0.45f, 0.62f, 0.28f);
                GUI.DrawTexture(
                    new Rect(_guiRect.x, snowTop, _guiRect.width, snowH),
                    _snowTex,  // _snowTex の透明部分に影が乗る（反転マスク的効果）
                    ScaleMode.StretchToFill, alphaBlend: true);
                // 露出中心に向かって少し暗い影を重ねる（深さ感）
                GUI.color = new Color(0.25f, 0.35f, 0.55f, 0.18f);
                GUI.DrawTexture(
                    new Rect(_guiRect.x + 2f, snowTop + 2f, _guiRect.width - 4f, snowH - 2f),
                    _snowTex,
                    ScaleMode.StretchToFill, alphaBlend: true);
            }

            // 描画後は必ず Color.white にリセット（後続の描画への汚染防止）
            GUI.color = Color.white;
        }
        else
        {
            // 全部空: トップライン消去
            GUI.color = new Color(0.45f, 0.55f, 0.72f, 0.90f);
            GUI.DrawTexture(new Rect(_guiRect.x, _guiRect.y - 18f, _guiRect.width, 22f),
                            Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            GUI.DrawTexture(new Rect(_guiRect.x, _guiRect.y - 4f, _guiRect.width, 4f),
                            Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ── 落下片（不定形シルエットテクスチャ使用・副塊クラスタ）──────
        // Texture2D.whiteTexture（四角形）ではなく _chunkTextures（ソフト円形）を使用
        bool hasChunkTex = _chunkTextures != null && _chunkTexBuilt;
        foreach (var p in _pieces)
        {
            if (p.alpha <= 0f) continue;

            Color c = p.snowColor;
            c.a = p.alpha;

            // 使用するテクスチャ: subCount を texIdx として使用（0〜5）
            int texIdx = Mathf.Clamp(p.subCount, 0, 5);
            Texture2D mainTex = (hasChunkTex && _chunkTextures[texIdx] != null)
                ? _chunkTextures[texIdx] : _chunkTextures?[0] ?? Texture2D.whiteTexture;

            float w2 = p.size * p.scaleX;
            float h2 = p.size * p.scaleY;

            var savedMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(p.rotation, new Vector2(p.pos.x, p.pos.y));

            // 不定形シルエットテクスチャで描画（四角形にならない）
            GUI.color = c;
            GUI.DrawTexture(new Rect(p.pos.x - w2 * 0.5f, p.pos.y - h2 * 0.5f, w2, h2),
                            mainTex, ScaleMode.StretchToFill, alphaBlend: true);

            GUI.matrix = savedMatrix;

            // ── 副塊クラスタ（subCount 個）──────────────────────
            if (p.subCount >= 1)
                DrawSubChunk(p, p.sub0Offset, p.sub0Scale, c, p.rotation + Random.Range(-15f, 15f));
            if (p.subCount >= 2)
                DrawSubChunk(p, p.sub1Offset, p.sub1Scale, c, p.rotation + Random.Range(-20f, 20f));
            if (p.subCount >= 3)
                DrawSubChunk(p, p.sub2Offset, p.sub2Scale, c, p.rotation + Random.Range(-25f, 25f));
        }
        // 落下片描画後は必ず Color.white にリセット（後続描画への alpha 汚染防止）
        GUI.color = Color.white;

        // ── 雪煙パーティクル（ソフト円形テクスチャ + kind でサイズ3段階）──
        // kind=0: hit（小）  kind=1: eave（中）  kind=2: ground（大）
        // Texture2D.whiteTexture（四角）→ _puffTex（ソフト円形）に変更
        Texture2D puffDrawTex = (_puffTex != null) ? _puffTex : Texture2D.whiteTexture;
        foreach (var pf in _puffs)
        {
            if (pf.alpha <= 0f) continue;
            float progress = 1f - pf.life / pf.maxLife;

            // kind によるサイズ係数（小:1.0 / 中:1.6 / 大:2.4）
            float kindScale = pf.kind == 2 ? 2.4f : (pf.kind == 1 ? 1.6f : 1.0f);
            float sz = pf.size * kindScale * (0.4f + progress * 1.4f); // 膨らんで薄くなる
            float szY = sz * Random.Range(0.75f, 1.25f); // 縦横ランダム（不定形感）

            float a  = pf.alpha * (1f - progress * 0.85f);
            // kind によるアルファ係数（大きいほど少し薄め）
            float alphaScale = pf.kind == 2 ? 0.55f : (pf.kind == 1 ? 0.65f : 0.75f);
            GUI.color = new Color(0.95f, 0.97f, 1f, a * alphaScale);

            // 回転で不定形感を強調（毎フレームランダムだと flickering するので life ベース）
            float puffAngle  = pf.life * 73.1f + pf.kind * 45f;
            var   puffCenter = new Vector2(pf.pos.x, pf.pos.y);
            var   savedPuffMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(puffAngle, puffCenter);
            GUI.DrawTexture(new Rect(pf.pos.x - sz * 0.5f, pf.pos.y - szY * 0.5f, sz, szY),
                            puffDrawTex, ScaleMode.StretchToFill, alphaBlend: true);
            GUI.matrix = savedPuffMatrix;
        }
        // 雪煙描画後は必ず Color.white にリセット
        GUI.color = Color.white;

        // ── fill ゲージ（左端黄バー）──────────────────────────
        GUI.color = new Color(1f, 1f, 0f, 0.85f);
        float barH = _guiRect.height * fillAvg;
        GUI.DrawTexture(new Rect(_guiRect.x - 6f, _guiRect.yMax - barH, 5f, barH),
                        Texture2D.whiteTexture);
        GUI.color = Color.white;

        // ── デバッグテキスト（屋根直下）──────────────────────
        var style = new GUIStyle(GUI.skin.label)
            { fontSize = 10, fontStyle = FontStyle.Bold };

        float tx = _guiRect.x;
        float ty = _guiRect.yMax + 4f;

        GUI.color = new Color(0f, 0f, 0f, 0.6f);
        GUI.DrawTexture(new Rect(tx, ty, 170f, 38f), Texture2D.whiteTexture);

        GUI.color = Color.green;
        GUI.Label(new Rect(tx+2, ty+1,  168, 14), $"[2D] {TARGET_ROOF_ID}", style);
        GUI.color = Color.yellow;
        GUI.Label(new Rect(tx+2, ty+13, 168, 14), $"fill={fillAvg:F2}  taps={_tapCount}", style);
        GUI.color = _lastSpawned ? Color.white : Color.red;
        GUI.Label(new Rect(tx+2, ty+25, 168, 14), _lastInfo, style);

        GUI.color = Color.white;
    }
}
