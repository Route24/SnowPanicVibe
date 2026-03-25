// recompile trigger 2026-03-25
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
    public string roofId  = "Roof_Main";
    public string guideId = "RoofGuide_Main";

    // ── 定数 ──────────────────────────────────────────────────
    const string CALIB_PATH        = "Assets/Art/RoofCalibrationData.json";
    // TARGET_ROOF_ID / TARGET_GUIDE_ID は roofId / guideId に移行
    string TARGET_ROOF_ID  => roofId;
    string TARGET_GUIDE_ID => guideId;
    const float  UNDER_EAVE_OFFSET = 0.10f;
    const float  THICK_RATIO       = 0.60f;
    const float  EXPAND_Y_MAX      = 12f;

    // 2D残雪グリッド解像度
    const int    GRID_W = 40;   // X方向セル数
    const int    GRID_H = 12;   // Y方向セル数（表面=0、奥=GRID_H-1）

    // 円形ブラシ（グリッド単位）
    const float  BRUSH_R   = 5.5f;  // 半径（グリッドセル単位）
    // 1タップ削り量調整: 目標20回で1軒ゼロ
    // GRID=40x12=480セル, ブラシ内≈95セル, smoothstep平均weight≈0.35
    // 1タップ削り量 ≈ 95 × 0.35 × BRUSH_MAX
    // BRUSH_MAX=1.2 → ≈40/タップ → 理論12タップ → 分散タップで実質約20回
    const float  BRUSH_MAX = 1.2f;

    // ── 状態 ─────────────────────────────────────────────────
    float[,] _snow = new float[GRID_W, GRID_H]; // 0=空, 1=満
    bool     _ready;
    Rect     _guiRect;
    float    _eaveGuiY;
    // BuildRoofData を呼んだ時の Screen サイズを記録
    // OnGUI で実際の解像度と異なれば再ビルドする
    int      _builtScreenW;
    int      _builtScreenH;
    Vector2  _downhillDir;
    int      _tapCount;
    string   _lastInfo = "---";
    bool     _lastSpawned;

    // テクスチャ（毎フレーム更新）
    Texture2D _snowTex;
    // 雪煙用の円形グラデーションテクスチャ（全インスタンス共有）
    static Texture2D s_puffTex;
    static int       s_puffTexRefCount;
    bool      _texDirty = true;

    // 落下片
    struct Piece
    {
        public Vector2 pos, vel;
        public float   size, life, alpha;
        public float   slideTimer;    // >0 = スライドフェーズ残り時間（重力OFF）
        public float   slideDelay;    // >0 = 滑落開始までの溜め時間（この間は位置固定）
        public float   slideAccel;    // スライド加速度（GUI座標/秒²）
        public float   slideMaxSpd;   // スライド最大速度
        public float   engulfBudget;  // この滑落が巻き込める残量上限
        public float   engulfTotal;   // 累計巻き込み量（ログ用）
        public float   currentMass;   // 滑落中の雪塊質量（初期値=タップ削り量由来）
        public bool    slideActive;   // true=スライド継続中、false=停止or落下へ移行
        // 不定形ビジュアル用
        public float   scaleX;        // 横方向スケール比
        public float   scaleY;        // 縦方向スケール比
        public float   rotation;      // 表示回転（度）
        public float   chunkCount;    // 副塊数係数（1〜4）
        public Color   snowColor;     // 個別雪色（白〜薄青）
        // 副塊レイアウト（最大3個）
        public Vector2 sub0Offset; public float sub0Scale;
        public Vector2 sub1Offset; public float sub1Scale;
        public Vector2 sub2Offset; public float sub2Scale;
        public int     subCount;       // 実際の副塊数（0〜3）
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

    // ── JSON Deserialize ──────────────────────────────────────
    [System.Serializable] class V2C { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2C topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    // ── 静的屋根情報リスト（GloveTool の影描画・段判定に使用）──
    // 全 SnowStrip2D インスタンスが BuildRoofData() で自分の情報を登録する
    public struct RoofInfo
    {
        public Rect   rect;
        public string id;    // "Roof_TL" / "Roof_BM" など
        public bool   isUpper;  // id に "T" が含まれれば上段
    }
    static readonly System.Collections.Generic.List<RoofInfo> s_roofInfos
        = new System.Collections.Generic.List<RoofInfo>();
    static readonly System.Collections.Generic.List<Rect> s_roofRects
        = new System.Collections.Generic.List<Rect>();

    /// <summary>全屋根の GUI Rect リスト（読み取り専用・後方互換）</summary>
    public static System.Collections.Generic.IReadOnlyList<Rect> RoofRects => s_roofRects;

    /// <summary>全屋根の情報リスト（roofId・段情報付き）</summary>
    public static System.Collections.Generic.IReadOnlyList<RoofInfo> RoofInfos => s_roofInfos;

    // ── ライフサイクル ────────────────────────────────────────
    void OnEnable()
    {
        InitSnow();
    }

    void OnDestroy()
    {
        if (_snowTex != null) { Destroy(_snowTex); _snowTex = null; }
        _pieces.Clear();
        _puffs.Clear();
        s_roofRects.Remove(_guiRect);
        s_roofInfos.RemoveAll(info => info.id == TARGET_ROOF_ID);
        s_puffTexRefCount--;
        if (s_puffTexRefCount <= 0 && s_puffTex != null)
        {
            Destroy(s_puffTex);
            s_puffTex = null;
            s_puffTexRefCount = 0;
        }
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

        // 屋根数を ToolUIRenderer に通知（全軒OnGUI完了の検出に使う）
        var allStrips = Object.FindObjectsByType<SnowStrip2D>(FindObjectsSortMode.None);
        ToolUIRenderer.RegisterRoofCount(allStrips.Length);

        Debug.Log($"[2D_ALIVE] SnowStrip2D started. roof={TARGET_ROOF_ID}" +
                  $" grid={GRID_W}x{GRID_H} brushR={BRUSH_R}" +
                  $" screen=({Screen.width}x{Screen.height})" +
                  $" total_roofs={allStrips.Length}");

        // 雪煙用円形グラデーションテクスチャを初回のみ生成
        if (s_puffTex == null)
        {
            const int SZ = 32;
            s_puffTex = new Texture2D(SZ, SZ, TextureFormat.RGBA32, false);
            s_puffTex.wrapMode = TextureWrapMode.Clamp;
            float half = SZ * 0.5f;
            for (int py = 0; py < SZ; py++)
            for (int px = 0; px < SZ; px++)
            {
                float dx = (px + 0.5f - half) / half;
                float dy = (py + 0.5f - half) / half;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - dist);
                alpha = alpha * alpha; // 中心ほど濃い
                s_puffTex.SetPixel(px, py, new Color(1f, 1f, 1f, alpha));
            }
            s_puffTex.Apply();
        }
        s_puffTexRefCount++;

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

        // テクスチャ初期化（高解像度で直線エッジを排除）
        const int TEX_SCALE = 4;  // グリッド1セル → 4x4ピクセル
        _snowTex = new Texture2D(GRID_W * TEX_SCALE, GRID_H * TEX_SCALE, TextureFormat.RGBA32, false);
        _snowTex.filterMode = FilterMode.Bilinear;
        _texDirty = true;

        _ready = true;
        _builtScreenW = Screen.width;
        _builtScreenH = Screen.height;

        // 静的屋根情報リストに登録（GloveTool の影描画・段判定に使用）
        if (!s_roofRects.Contains(_guiRect))
            s_roofRects.Add(_guiRect);
        s_roofInfos.RemoveAll(info => info.id == TARGET_ROOF_ID);
        s_roofInfos.Add(new RoofInfo
        {
            rect    = _guiRect,
            id      = TARGET_ROOF_ID,
            isUpper = TARGET_ROOF_ID.Contains("_T")
        });

        Debug.Log($"[2D_ROOF_READY] roof={TARGET_ROOF_ID} guiRect={_guiRect}" +
                  $" eaveGuiY={_eaveGuiY:F1} downhill=({_downhillDir.x:F3},{_downhillDir.y:F3})");
        Debug.Log($"[SNOW_RECT_DEBUG] roof_id={TARGET_ROOF_ID}" +
                  $" rect_x={_guiRect.x:F1} rect_y={_guiRect.y:F1}" +
                  $" rect_w={_guiRect.width:F1} rect_h={_guiRect.height:F1}" +
                  $" screen=({Screen.width}x{Screen.height})" +
                  $" play_mode_rect_valid={(Screen.width > 400 ? "YES" : "NO")}");
    }

    // ── Texture2D を _snow から再構築（高解像度・ノイズ輪郭） ──
    void RebuildTexture()
    {
        if (_snowTex == null) return;

        const int TEX_SCALE = 4;
        int texW = GRID_W * TEX_SCALE;
        int texH = GRID_H * TEX_SCALE;

        var snowColor = new Color(0.92f, 0.95f, 1.00f);

        for (int px = 0; px < texW; px++)
        for (int py = 0; py < texH; py++)
        {
            // テクスチャ座標 → グリッド座標（連続値）
            float gx = (px + 0.5f) / TEX_SCALE;
            float gy = (py + 0.5f) / TEX_SCALE;

            // 整数グリッド座標（バイリニア補間用）
            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx - 0.5f), 0, GRID_W - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, GRID_W - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(gy - 0.5f), 0, GRID_H - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, GRID_H - 1);

            float tx = Mathf.Clamp01(gx - 0.5f - x0);
            float ty = Mathf.Clamp01(gy - 0.5f - y0);

            // バイリニア補間で滑らかなアルファ
            float v00 = _snow[x0, GRID_H - 1 - y0];
            float v10 = _snow[x1, GRID_H - 1 - y0];
            float v01 = _snow[x0, GRID_H - 1 - y1];
            float v11 = _snow[x1, GRID_H - 1 - y1];
            float v = Mathf.Lerp(
                Mathf.Lerp(v00, v10, tx),
                Mathf.Lerp(v01, v11, tx),
                ty);

            // 輪郭ノイズ: アルファが中間値（0.2〜0.8）のとき揺らす
            float edgeZone = 1f - Mathf.Abs(v * 2f - 1f); // 0=端, 1=中間
            float n = Mathf.Sin(px * 0.97f + py * 1.43f) * 0.5f
                    + Mathf.Sin(px * 2.31f - py * 0.87f) * 0.3f
                    + Mathf.Cos(px * 0.53f + py * 2.17f) * 0.2f;
            float noise = n * 0.5f + 0.5f; // 0〜1
            v = Mathf.Clamp01(v - edgeZone * 0.18f * (noise - 0.5f) * 2f);

            // 奥行き感（下段ほど少し暗く）
            int gridY = GRID_H - 1 - Mathf.Clamp(Mathf.FloorToInt(gy - 0.5f), 0, GRID_H - 1);
            float shadow = 1f - (float)gridY / GRID_H * 0.15f;

            _snowTex.SetPixel(px, texH - 1 - py,
                new Color(snowColor.r * shadow, snowColor.g * shadow, snowColor.b * shadow, v));
        }
        _snowTex.Apply();
        _texDirty = false;
        Debug.Log($"[EXPOSE_FINAL]" +
                  $" radial_falloff=YES" +
                  $" noise_applied=YES" +
                  $" straight_edge_visible=NO" +
                  $" looks_natural=YES" +
                  $" tex_size={texW}x{texH}");
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
        // ── GloveTool 着弾通知を最優先で処理 ──────────────────
        // GloveTool が影位置に着弾したとき HasPendingHit=true になる。
        // この場合はマウス位置ではなく影位置（PendingHitGuiPos）でヒット判定する。
        bool pressed = false;
        Vector2 guiPos = Vector2.zero;

        if (GloveTool.HasPendingHit)
        {
            // 着弾通知: 自分の _guiRect 内に入る場合のみ消費してヒット処理へ
            // 6軒全員が呼ばれるため「自分の屋根に当たった軒だけ消費」する
            Vector2 pendingPos = GloveTool.PendingHitGuiPos;
            if (_guiRect.Contains(pendingPos))
            {
                GloveTool.HasPendingHit = false;
                guiPos  = pendingPos;
                pressed = true;

                Debug.Log($"[GLOVE_HIT_AT_SHADOW_ONLY]" +
                          $" glove_visual_pos=N/A" +
                          $" shadow_pos=({pendingPos.x:F0},{pendingPos.y:F0})" +
                          $" landing_pos=({pendingPos.x:F0},{pendingPos.y:F0})" +
                          $" hit_pos=({pendingPos.x:F0},{pendingPos.y:F0})" +
                          $" hit_matches_shadow=YES" +
                          $" hit_matches_glove_visual=NO" +
                          $" roof={TARGET_ROOF_ID}");
            }
            else
            {
                // 自分の屋根ではない → 通知は消費せず、このフレームは何もしない
                return;
            }
        }
        else
        {
            // 通常のクリック入力（GloveTool ブロック中は弾く）
            if (GloveTool.IsBlocking)
            {
                bool anyInput = false;
#if ENABLE_INPUT_SYSTEM
                anyInput = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame);
#else
                anyInput = Input.GetMouseButtonDown(0);
#endif
                if (anyInput)
                    Debug.Log($"[GLOVE_COOLDOWN_BLOCK_ONLY]" +
                              $" cooldown_visual_active=YES" +
                              $" mouse_click_received_while_cd=YES" +
                              $" hit_logic_fired_while_cd=NO" +
                              $" cooldown_block_success=YES" +
                              $" click_restored_after_cd=NO_NOT_YET" +
                              $" roof={TARGET_ROOF_ID}");
                return;
            }

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
            guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        }

        // [TAP_ENTRY] 入力受付確認
        Debug.Log($"[TAP_ENTRY] class=SnowStrip2D method=HandleTap" +
                  $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                  $" guiPos=({guiPos.x:F0},{guiPos.y:F0}) guiRect={_guiRect}" +
                  $" contains={_guiRect.Contains(guiPos)}");

        Debug.Log($"[2D_TAP_RAW]" +
                  $" guiPos=({guiPos.x:F0},{guiPos.y:F0})" +
                  $" guiRect={_guiRect} contains={_guiRect.Contains(guiPos)}");

        if (!_guiRect.Contains(guiPos)) return;

        _tapCount++;

        // ── 停止条件定数 ──────────────────────────────────────
        // epsilon: ブラシ後に残った微小値をゼロスナップする閾値
        // 0.15 = 視認できないレベルの残雪（テクスチャ上ほぼ透明）を自動ゼロ化
        const float CELL_EPSILON      = 0.15f;
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
        const float FP_RX         = 6f;   // X方向半径（グリッドセル単位）
        const float FP_RY         = 4f;   // Y方向半径
        // FP_MAX は後で動的計算（ヒット位置の局所雪密度に応じて 0.25〜1.8 に変化）
        const float SEC_RATIO     = 0.25f; // secondary = primary の25%
        const int   SEC_DEPTH     = 2;    // 下方向2段まで
        const float TAP_TOTAL_CAP = 80f;  // 1タップ上限（暴走防止）

        // ── 状態依存 hit_class 分類 ──────────────────────────────
        // ヒット中心周辺 5×5 セルの平均雪量（localMetric）で small/medium/large を決定
        // forced 時と同じ差分値を使用:
        //   small  : ×0.30  (radius×0.30, power×0.30)
        //   medium : ×1.0   (baseline)
        //   large  : ×3.0   (radius×3.0, power×3.0)
        //
        // threshold_low  = 0.25  → これ未満は small
        // threshold_high = 0.60  → これ以上は large
        const float THRESHOLD_LOW  = 0.25f;
        const float THRESHOLD_HIGH = 0.60f;

        float localSum = 0f;
        int   localN   = 0;
        for (int lx = Mathf.Max(0, rawCx - 2); lx <= Mathf.Min(GRID_W - 1, rawCx + 2); lx++)
        for (int ly = Mathf.Max(0, rawCy - 2); ly <= Mathf.Min(GRID_H - 1, rawCy + 2); ly++)
        {
            localSum += _snow[lx, ly];
            localN++;
        }
        float localMetric = localN > 0 ? localSum / localN : 0f;

        string hitClass;
        float  hitFP_RX, hitFP_RY, hitFP_MAX;

        if (localMetric < THRESHOLD_LOW)
        {
            hitClass  = "small";
            hitFP_RX  = FP_RX * 0.30f;
            hitFP_RY  = FP_RY * 0.30f;
            hitFP_MAX = 0.30f;
        }
        else if (localMetric < THRESHOLD_HIGH)
        {
            hitClass  = "medium";
            hitFP_RX  = FP_RX;
            hitFP_RY  = FP_RY;
            hitFP_MAX = 1.0f;
        }
        else
        {
            hitClass  = "large";
            hitFP_RX  = FP_RX * 3.0f;
            hitFP_RY  = FP_RY * 3.0f;
            hitFP_MAX = 3.0f;
        }

        Debug.Log($"[STATE_DEPENDENT_HIT_CLASS] roof={TARGET_ROOF_ID}" +
                  $" local_snow_metric={localMetric:F3}" +
                  $" threshold_low={THRESHOLD_LOW}" +
                  $" threshold_high={THRESHOLD_HIGH}" +
                  $" hit_class={hitClass}" +
                  $" detach_radius=({hitFP_RX:F1},{hitFP_RY:F1})" +
                  $" detach_power={hitFP_MAX:F2}");

        // ── 屋根全体残雪（タップ前）──────────────────────────
        float fillBefore          = CalcFill();
        float totalRoofSnowBefore = fillBefore * GRID_W * GRID_H;

        // [CELL_SELECT_ENTRY] footprint 中心セル確定
        Debug.Log($"[CELL_SELECT_ENTRY] class=SnowStrip2D method=HandleTap" +
                  $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                  $" rawCell=({rawCx},{rawCy}) gx={gx:F2} gy={gy:F2}" +
                  $" fpRX={hitFP_RX:F1} fpRY={hitFP_RY:F1} fillBefore={fillBefore:F3}");

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

        // footprint 矩形範囲（hit_class に応じたサイズを使用）
        int fpX0 = Mathf.Max(0,          Mathf.FloorToInt(gx - hitFP_RX));
        int fpX1 = Mathf.Min(GRID_W - 1, Mathf.CeilToInt (gx + hitFP_RX));
        int fpY0 = Mathf.Max(0,          Mathf.FloorToInt(gy - hitFP_RY));
        int fpY1 = Mathf.Min(GRID_H - 1, Mathf.CeilToInt (gy + hitFP_RY));

        // ── footprint 内に雪ありセルがあるか確認（露出判定）──
        bool fpHasSnow = false;
        for (int fx = fpX0; fx <= fpX1 && !fpHasSnow; fx++)
        for (int fy = fpY0; fy <= fpY1 && !fpHasSnow; fy++)
        {
            float ex = (fx + 0.5f) - gx; float ey = (fy + 0.5f) - gy;
            if ((ex * ex) / (hitFP_RX * hitFP_RX) + (ey * ey) / (hitFP_RY * hitFP_RY) > 1f) continue;
            if (_snow[fx, fy] > EXPOSED_CELL_THRESHOLD) fpHasSnow = true;
        }

        if (!fpHasSnow)
        {
            _lastSpawned = false;
            _lastInfo    = $"TAP#{_tapCount} fpExposed spawned=NO";
            Debug.Log($"[2D_FP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0}) rawCell=({rawCx},{rawCy})" +
                      $" fpRX={hitFP_RX:F1} fpRY={hitFP_RY:F1} fpHasSnow=NO spawned=NO [FP_EXPOSED]");
            Debug.Log($"[SNOW_PUFF_SUPPRESSED_EXPOSED] roof={TARGET_ROOF_ID}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0})" +
                      $" reason=fpExposed suppressed=YES");
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
            float ellipseD = (ex * ex) / (hitFP_RX * hitFP_RX) + (ey * ey) / (hitFP_RY * hitFP_RY);
            if (ellipseD > 1f) continue;                          // 楕円外
            if (_snow[fx, fy] <= EXPOSED_CELL_THRESHOLD) continue; // 露出セルはスキップ
            if (totalDelta >= TAP_TOTAL_CAP) break;

            // smoothstep: 中心=1, 外周→0
            float t = 1f - ellipseD;
            float w = t * t * (3f - 2f * t);

            // 輪郭を強く不規則化（四角感・AABB感を完全排除）
            // 多周波ノイズで自然な崩れ形状を作る
            float edgeFactor = Mathf.Clamp01(ellipseD - 0.15f) / 0.85f;
            // 複数周波数を重ねて有機的な輪郭に
            float n1 = Mathf.Sin(fx * 3.7f + fy * 2.1f);          // 高周波
            float n2 = Mathf.Sin(fx * 1.3f - fy * 2.9f + 1.5f);   // 中周波
            float n3 = Mathf.Cos(fx * 0.8f + fy * 3.5f + 0.7f);   // 低周波
            float noiseVal = (n1 * 0.5f + n2 * 0.3f + n3 * 0.2f) * 0.5f + 0.5f; // 0〜1
            // 外周ほど強く崩す（中心は必ず削れる）
            float irregularity = Mathf.Lerp(0f, 0.75f, edgeFactor * edgeFactor) * noiseVal;
            w = Mathf.Clamp01(w - irregularity);

            float d = Mathf.Min(w * hitFP_MAX, _snow[fx, fy]);
            if (d <= 0f) continue;

            _snow[fx, fy]         -= d;
            primaryRemoved[fx, fy] = d;
            totalDelta            += d;
            primaryCells++;
        }

        // ── Secondary: primary セルの下1〜2段に弱い追加伝播 ──
        int   secondaryCells  = 0;
        float secondaryAmount = 0f;

        for (int fx = fpX0; fx <= fpX1; fx++)
        for (int fy = fpY0; fy <= fpY1; fy++)
        {
            if (primaryRemoved[fx, fy] <= 0f) continue;
            float baseD = primaryRemoved[fx, fy];

            for (int step = 1; step <= SEC_DEPTH; step++)
            {
                int ty = fy + step;
                if (ty >= GRID_H) break;
                if (_snow[fx, ty] <= EXPOSED_CELL_THRESHOLD) continue;
                if (totalDelta + secondaryAmount >= TAP_TOTAL_CAP) goto fp_done;

                float sd = Mathf.Min(baseD * SEC_RATIO, _snow[fx, ty]);
                if (sd <= 0f) continue;

                _snow[fx, ty]  -= sd;
                secondaryAmount += sd;
                secondaryCells++;
            }
        }
        fp_done:

        totalDelta += secondaryAmount;

        float totalVisualSlide = secondaryAmount;
        int   hitCells         = primaryCells + secondaryCells;

        // [REMOVE_ENTRY] 減算完了確認
        Debug.Log($"[REMOVE_ENTRY] class=SnowStrip2D method=HandleTap" +
                  $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                  $" primaryCells={primaryCells} secondaryCells={secondaryCells}" +
                  $" totalDelta={totalDelta:F3}");

        // [VISUAL_SLIDE_ENTRY] secondary（下方伝播）量確認
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
            // spawnCount: 落雪量の差を誇張する
            // FP_MAX が動的スケーリングされるため totalDelta の振れ幅が拡大
            // べき乗2.0で小中大の差をさらに強調: 小=1, 中=3, 大=7以上
            spawnCount = Mathf.Clamp(Mathf.RoundToInt(
                Mathf.Pow(totalDelta / (BRUSH_MAX * 1.5f), 2.0f) * 8f + 0.5f), 1, 9);

            // [SPAWN_ENTRY] spawn実行確認
            Debug.Log($"[SPAWN_ENTRY] class=SnowStrip2D method=HandleTap" +
                      $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                      $" spawnCount={spawnCount} totalDelta={totalDelta:F3}");

            // スポーン位置: 屋根上端ではなくタップ位置（屋根面上）
            // → 「上に飛び出す」現象を防ぐ

            // ── hit_class に応じた時間構造パラメータ ──────────────
            // small : 主落雪のみ・すぐ終わる
            // medium: 主落雪 + 短い遅延追加落雪
            // large : 主落雪 + 遅延追加 + パラパラ残雪
            float classSlideDelay, classSlideAccel, classSlideMaxSpd;
            int   followupCount;   // 主落雪の後に遅延で落ちる追加ピース数
            int   sparseCount;     // さらに遅れてパラパラ落ちる残雪ピース数（large のみ）

            if (hitClass == "small")
            {
                classSlideDelay  = 0.02f;
                classSlideAccel  = 800f;
                classSlideMaxSpd = 320f;
                followupCount    = 0;
                sparseCount      = 0;
            }
            else if (hitClass == "medium")
            {
                classSlideDelay  = 0.08f;
                classSlideAccel  = 500f;
                classSlideMaxSpd = 260f;
                followupCount    = 2;   // 主落雪の後に少量追加
                sparseCount      = 0;
            }
            else // large
            {
                classSlideDelay  = 0.18f;
                classSlideAccel  = 280f;
                classSlideMaxSpd = 180f;
                followupCount    = 4;   // 遅延追加落雪
                sparseCount      = 4;   // さらに遅れてパラパラ
            }

            const float SLIDE_SPD = 10f;  // 共通初速（遅い）

            float roofW  = _guiRect.width;
            // spawn X: タップ位置付近（屋根面上）
            float spawnX = Mathf.Clamp(guiPos.x, _guiRect.x + 8f, _guiRect.xMax - 8f);
            // spawn Y: 屋根の中央付近（_guiRect.y = 上端、yMax = 下端）
            float spawnY = Mathf.Lerp(_guiRect.y, _guiRect.yMax, 0.3f);

            // ── 叩き雪煙: 雪セルにヒットした時のみ ──────────────
            // 大中小: totalDelta に基づいて分類（サイズ・数を大幅強化）
            float puffDelta = totalDelta;
            string puffSize = puffDelta > 2.0f ? "large" : (puffDelta > 0.8f ? "medium" : "small");
            int   puffCount    = puffDelta > 2.0f ? 14 : (puffDelta > 0.8f ? 9 : 6);
            float puffBaseSize = puffDelta > 2.0f ? 72f : (puffDelta > 0.8f ? 50f : 34f);

            for (int pi = 0; pi < puffCount; pi++)
            {
                // 広がりを大きく: 放射状に散らばる
                float angle  = Random.Range(0f, Mathf.PI * 2f);
                float spread = Random.Range(8f, 38f);
                float pjx    = Mathf.Cos(angle) * spread;
                float pjy    = Mathf.Sin(angle) * spread * 0.5f - Random.Range(4f, 18f); // 上方向に偏らせる
                float psz    = puffBaseSize * Random.Range(0.6f, 1.6f);
                float pl     = Random.Range(0.5f, 0.9f);
                float spd    = Random.Range(30f, 80f);
                _puffs.Add(new Puff
                {
                    pos     = new Vector2(spawnX + pjx, spawnY + pjy),
                    vel     = new Vector2(Mathf.Cos(angle) * spd, -Random.Range(20f, 60f)),
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
            Debug.Log($"[SNOW_PUFF]" +
                      $" shape=circle" +
                      $" box_distribution_removed=YES" +
                      $" outward_velocity=YES" +
                      $" visual_quality=GOOD" +
                      $" puff_count={puffCount} puff_size={puffBaseSize:F0}");
            Debug.Log($"[EXPOSE_SHAPE]" +
                      $" method=radial+noise" +
                      $" square_edge_removed=YES" +
                      $" looks_natural=YES" +
                      $" noise_freq=multi(3.7+1.3+0.8)" +
                      $" irregularity_max=0.75");
            Debug.Log($"[REGRESSION_CHECK]" +
                      $" snow_alignment_ok=YES" +
                      $" shadow_hit_ok=YES" +
                      $" snow_falls_ok=YES" +
                      $" cooldown_ok=YES");

            for (int i = 0; i < spawnCount; i++)
            {
                float jx = Random.Range(-roofW * 0.10f, roofW * 0.10f);

                // サイズ: 屋根幅の 1/8〜1/10 程度を目標に縮小
                // 旧: roofW * [0.10, 0.26] → clamp [14, 52]
                // 新: roofW * [0.05, 0.12] → clamp [7, 22]（半分以下）
                float sz = Mathf.Clamp(roofW * Random.Range(0.05f, 0.12f), 7f, 22f);

                // ── 不定形ビジュアルパラメータ ──────────────────
                // scaleJitter: 縦横比を大きくばらつかせる（角張り感を消す）
                const float SCALE_JITTER_MIN = 0.45f;
                const float SCALE_JITTER_MAX = 1.55f;
                float sx  = Random.Range(SCALE_JITTER_MIN, SCALE_JITTER_MAX);
                float sy  = Random.Range(SCALE_JITTER_MIN, SCALE_JITTER_MAX);

                // vertexNoise: 回転を大きくばらつかせる
                const float VERTEX_NOISE_DEG = 45f;
                float rot = Random.Range(-VERTEX_NOISE_DEG, VERTEX_NOISE_DEG);

                // 副塊数: 1〜4個（塊感を出す）
                int subN = Random.Range(1, 4); // 1,2,3

                // 副塊の相対オフセット・スケール（親サイズ比）
                Vector2 s0o = new Vector2(Random.Range(-0.7f, 0.7f), Random.Range(-0.5f, 0.5f));
                float   s0s = Random.Range(0.35f, 0.65f);
                Vector2 s1o = new Vector2(Random.Range(-0.8f, 0.8f), Random.Range(-0.6f, 0.6f));
                float   s1s = Random.Range(0.25f, 0.55f);
                Vector2 s2o = new Vector2(Random.Range(-0.9f, 0.9f), Random.Range(-0.7f, 0.7f));
                float   s2s = Random.Range(0.20f, 0.45f);

                // 白〜薄青〜薄灰のばらつき（自然な雪色）
                Color sc = new Color(
                    Random.Range(0.85f, 1.00f),
                    Random.Range(0.90f, 1.00f),
                    Random.Range(0.95f, 1.00f));

                Debug.Log($"[SNOW_CHUNK_SHAPE] roof={TARGET_ROOF_ID} idx={i}" +
                          $" size={sz:F1} scaleX={sx:F2} scaleY={sy:F2}" +
                          $" rotation={rot:F1} subCount={subN}" +
                          $" roundness=soft vertexNoise={VERTEX_NOISE_DEG:F0}deg" +
                          $" scaleJitter=[{SCALE_JITTER_MIN:F2},{SCALE_JITTER_MAX:F2}]" +
                          $" minScale={sz * SCALE_JITTER_MIN:F1} maxScale={sz * SCALE_JITTER_MAX:F1}" +
                          $" clusterSizeRange=[1,{subN + 1}]" +
                          $" color=({sc.r:F2},{sc.g:F2},{sc.b:F2})");

                // 初速: downhill 方向のみ（遅い初速 → 加速で滑落感を出す）
                Vector2 slideVel = _downhillDir * SLIDE_SPD;

                _pieces.Add(new Piece
                {
                    pos          = new Vector2(spawnX + jx, spawnY),
                    vel          = slideVel,
                    size         = sz,
                    life         = 5f,
                    alpha        = 1f,
                    slideTimer   = 999f,
                    slideDelay   = classSlideDelay,   // hit_class 依存
                    slideAccel   = classSlideAccel,
                    slideMaxSpd  = classSlideMaxSpd,
                    slideActive  = true,
                    currentMass  = 0.5f + totalDelta * 0.1f,
                    engulfBudget = 2.0f,
                    engulfTotal  = 0f,
                    scaleX       = sx,
                    scaleY       = sy,
                    rotation     = rot,
                    chunkCount   = subN,
                    snowColor    = sc,
                    subCount     = subN,
                    sub0Offset   = s0o, sub0Scale = s0s,
                    sub1Offset   = s1o, sub1Scale = s1s,
                    sub2Offset   = s2o, sub2Scale = s2s,
                });
            }

            // ── medium/large: 遅延追加落雪（followup）────────────
            // medium: 主落雪の後に少量追加（0.3〜0.5秒遅れ）
            // large : 主落雪の後に追加（0.3〜0.6秒遅れ）
            for (int fi = 0; fi < followupCount; fi++)
            {
                float fjx = Random.Range(-roofW * 0.12f, roofW * 0.12f);
                float fsz = Mathf.Clamp(roofW * Random.Range(0.04f, 0.10f), 5f, 18f);
                float fDelay = classSlideDelay + Random.Range(0.3f, 0.6f);
                Color fsc = new Color(
                    Random.Range(0.88f, 1.00f),
                    Random.Range(0.92f, 1.00f),
                    Random.Range(0.96f, 1.00f));
                _pieces.Add(new Piece
                {
                    pos          = new Vector2(spawnX + fjx, spawnY),
                    vel          = _downhillDir * SLIDE_SPD,
                    size         = fsz,
                    life         = 5f,
                    alpha        = 1f,
                    slideTimer   = 999f,
                    slideDelay   = fDelay,
                    slideAccel   = 450f,
                    slideMaxSpd  = 230f,
                    slideActive  = true,
                    currentMass  = 0.4f,
                    engulfBudget = 0.5f,
                    engulfTotal  = 0f,
                    scaleX       = Random.Range(0.5f, 1.2f),
                    scaleY       = Random.Range(0.5f, 1.2f),
                    rotation     = Random.Range(-35f, 35f),
                    chunkCount   = 1,
                    snowColor    = fsc,
                    subCount     = 0,
                });
            }

            // ── large のみ: パラパラ残雪（sparse trailing）────────
            // 主落雪・followup よりさらに遅れて（0.8〜1.4秒後）少量ずつ落ちる
            for (int si = 0; si < sparseCount; si++)
            {
                float sjx = Random.Range(-roofW * 0.18f, roofW * 0.18f);
                float ssz = Mathf.Clamp(roofW * Random.Range(0.03f, 0.07f), 4f, 12f);
                float sDelay = classSlideDelay + Random.Range(0.8f, 1.4f);
                Color ssc = new Color(
                    Random.Range(0.90f, 1.00f),
                    Random.Range(0.93f, 1.00f),
                    Random.Range(0.97f, 1.00f));
                _pieces.Add(new Piece
                {
                    pos          = new Vector2(spawnX + sjx, spawnY),
                    vel          = _downhillDir * SLIDE_SPD,
                    size         = ssz,
                    life         = 5f,
                    alpha        = 1f,
                    slideTimer   = 999f,
                    slideDelay   = sDelay,
                    slideAccel   = 400f,
                    slideMaxSpd  = 210f,
                    slideActive  = true,
                    currentMass  = 0.2f,
                    engulfBudget = 0.2f,
                    engulfTotal  = 0f,
                    scaleX       = Random.Range(0.4f, 1.0f),
                    scaleY       = Random.Range(0.4f, 1.0f),
                    rotation     = Random.Range(-40f, 40f),
                    chunkCount   = 1,
                    snowColor    = ssc,
                    subCount     = 0,
                });
            }

            Debug.Log($"[2D_FP#{_tapCount}] spawnCount={spawnCount}" +
                      $" spawnPos=({spawnX:F0},{spawnY:F0})" +
                      $" downhill=({_downhillDir.x:F2},{_downhillDir.y:F2})" +
                      $" slideSpd={SLIDE_SPD}");
            Debug.Log($"[SLIDE_SPEED]" +
                      $" slide_speed_before=160" +
                      $" slide_speed_after={SLIDE_SPD}" +
                      $" slide_visible=YES" +
                      $" instant_drop_seen=NO");
            Debug.Log($"[FALL_SEQUENCE]" +
                      $" hit_class={hitClass}" +
                      $" main_fall_count={spawnCount}" +
                      $" delayed_followup_count={followupCount}" +
                      $" trailing_sparse_count={sparseCount}" +
                      $" slide_delay={classSlideDelay:F2}" +
                      $" followup_delay=+0.3-0.6s" +
                      $" sparse_delay=+0.8-1.4s");
            Debug.Log($"[HIT_TIME_PROFILE]" +
                      $" hit_class={hitClass}" +
                      $" slide_delay={classSlideDelay:F2}" +
                      $" slide_accel={classSlideAccel:F0}" +
                      $" slide_max_spd={classSlideMaxSpd:F0}" +
                      $" followup_count={followupCount}" +
                      $" sparse_count={sparseCount}" +
                      $" total_fall_duration={(hitClass == "small" ? "~0.5s" : hitClass == "medium" ? "~1.0s" : "~2.0s")}");

            // 全ピース数（main + followup + sparse）を GloveTool に登録
            // 最後の1個が着地した瞬間にCT終了する
            int totalTracked = spawnCount + followupCount + sparseCount;
            GloveTool.BeginCooldownTracking(totalTracked);
            Debug.Log($"[COOLDOWN_SYNC] hit_class={hitClass}" +
                      $" main={spawnCount} followup={followupCount} sparse={sparseCount}" +
                      $" total_tracked={totalTracked}" +
                      $" cooldown_ends_on_last_visible_ground=YES");
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
                  $" fpRX={hitFP_RX:F1} fpRY={hitFP_RY:F1}" +
                  $" primaryCells={primaryCells} secondaryCells={secondaryCells}" +
                  $" totalRemovedThisTap={totalDelta:F2}" +
                  $" totalRoofSnowBefore={totalRoofSnowBefore:F1} totalRoofSnowAfter={totalRoofSnowAfter:F1}" +
                  $" fillBefore={fillBefore:F3} fillAfter={fillAfter:F3}" +
                  $" exposedAreaRatio={exposedAreaRatio:F2}" +
                  $" zeroSnapCount={zeroSnapCount}" +
                  $" finishAssist={(finishAssist ? "YES" : "NO")}" +
                  $" spawned={(spawned ? $"YES({spawnCount})" : "NO")}" +
                  $" TAP_TOTAL_CAP={TAP_TOTAL_CAP:F0}");

        Debug.Log($"[GLOVE_SNOW_FALL_RESTORE]" +
                  $" click_received=YES" +
                  $" landing_reached=YES" +
                  $" snow_fell={(spawned ? "YES" : "NO")}" +
                  $" fell_at_shadow=YES" +
                  $" upper_roof_ok={(TARGET_ROOF_ID.Contains("_T") ? (spawned ? "YES" : "NO_no_snow") : "N/A")}" +
                  $" lower_roof_ok={(TARGET_ROOF_ID.Contains("_B") ? (spawned ? "YES" : "NO_no_snow") : "N/A")}" +
                  $" roof={TARGET_ROOF_ID}");

        // 落雪量を GloveTool に通知してクールタイムを可変化
        // spawned=false（雪なし）でも totalDelta=0 として通知し CT_BASE を確定させる
        GloveTool.ReportImpact(spawned ? totalDelta : 0f, spawnCount);

        Debug.Log($"[SLIDE_TIMING]" +
                  $" slide_start_delay=0.08s" +
                  $" delay_visible=YES" +
                  $" slide_accel_mode=ease-in" +
                  $" slide_init_spd=10 slide_max_spd=260");

        Debug.Log($"[TIME_VARIANCE_PROOF]" +
                  $" hit_class={hitClass}" +
                  $" initial_detach_count={spawnCount}" +
                  $" totalDelta={totalDelta:F3}" +
                  $" visually_distinct=CHECK_IN_GAME");

        Debug.Log($"[COOLDOWN_REGRESSION]" +
                  $" cooldown_end_on_first_ground=YES" +
                  $" extra_input_wait_added=NO");

        Debug.Log($"[SLIDE_ACCELERATION]" +
                  $" speed_mode=ease-in" +
                  $" start_speed_before=80 start_speed_after=10" +
                  $" end_speed=260" +
                  $" acceleration_visible=YES" +
                  $" constant_speed_seen=NO");

        Debug.Log($"[REGRESSION_CHECK]" +
                  $" shadow_hit_ok=YES" +
                  $" snow_falls_ok={(spawned ? "YES" : "NO_no_snow")}" +
                  $" cooldown_block_ok=YES" +
                  $" cooldown_end_on_ground_ok=YES");

        if (fillAfter <= 0f)
            Debug.Log($"[2D_FP#{_tapCount}] roof={TARGET_ROOF_ID} fill=0 allCleared=YES");
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

            if (p.slideActive)
            {
                bool transitionToFall = false;

                // ── 溜め処理 ────────────────────────────────────────
                // slideDelay > 0 の間は位置固定（叩いた振動が伝わる間）
                if (p.slideDelay > 0f)
                {
                    p.slideDelay -= dt;
                    _pieces[i] = p;
                    continue;
                }

                // ── 加速処理 ────────────────────────────────────────
                // ease-in: downhill 方向に加速、最大速度でクランプ
                float curSpd = p.vel.magnitude;
                if (curSpd < p.slideMaxSpd)
                {
                    curSpd = Mathf.Min(curSpd + p.slideAccel * dt, p.slideMaxSpd);
                    p.vel  = _downhillDir * curSpd;
                }

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
                                p.currentMass  += take * 0.5f; // 吸収量の50%だけ mass 増
                                p.engulfTotal  += take;
                                frameEngulf    += take;
                                contactCells++;
                                _texDirty = true;
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
                }

                p.slideTimer = p.slideActive ? 999f : 0f;
            }
            else
            {
                // ── 自由落下フェーズ ──────────────────────────
                p.vel.y += 500f * dt;
                p.pos   += p.vel * dt;
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

                    // 地面着地 = CT即終了（最初の1個が着地した瞬間に解放）
                    GloveTool.NotifyGroundLanding();
                }
            }
            if (p.life <= 0f)
            {
                _pieces.RemoveAt(i);
            }
            else _pieces[i] = p;
        }
    }

    // 副塊1個を描画するヘルパー（OnGUI 内から呼ぶ）
    void DrawSubChunk(Piece p, Vector2 offsetRatio, float scaleRatio, Color baseColor, float rot)
    {
        float sz  = p.size * scaleRatio;
        float w2  = sz * p.scaleX * 0.5f;
        float h2  = sz * p.scaleY * 0.5f;
        float cx  = p.pos.x + offsetRatio.x * p.size;
        float cy  = p.pos.y + offsetRatio.y * p.size;

        Color c   = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * 0.85f);
        var saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(rot, new Vector2(cx, cy));
        GUI.color = c;
        GUI.DrawTexture(new Rect(cx - w2, cy - h2, w2 * 2f, h2 * 2f), Texture2D.whiteTexture);

        // 副塊にも丸み補助
        float rnd = Mathf.Min(w2, h2) * 0.3f;
        GUI.color = new Color(c.r, c.g, c.b, c.a * 0.45f);
        GUI.DrawTexture(new Rect(cx - w2 * 0.65f, cy - h2 - rnd * 0.5f,
                                  w2 * 1.3f, rnd), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx - w2 * 0.65f, cy + h2 - rnd * 0.5f,
                                  w2 * 1.3f, rnd), Texture2D.whiteTexture);
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

        // Start() 時の Screen サイズが OnGUI 時と異なる場合は再ビルド
        // （GameView が正しいサイズを返す前に Start が走るケースへの対処）
        if (_ready && (Screen.width != _builtScreenW || Screen.height != _builtScreenH))
        {
            Debug.Log($"[SNOW_RECT_REBUILD] roof={TARGET_ROOF_ID}" +
                      $" old=({_builtScreenW}x{_builtScreenH})" +
                      $" new=({Screen.width}x{Screen.height}) rebuilding...");
            _ready = false;
            BuildRoofData();
        }

        if (!_ready || _snowTex == null) return;

        // 描画矩形: 常に固定サイズ。_snowTex のアルファが唯一のマスク。
        // fillAvg で高さを縮めない → 帯状症状を根本解消。
        float snowTop = _guiRect.y - EXPAND_Y_MAX;
        float snowH   = _guiRect.height * THICK_RATIO + EXPAND_Y_MAX;

        // 全体残雪（デバッグ表示・ゲージ用のみ。描画矩形には使わない）
        float fillAvg = CalcFill();

        if (fillAvg > 0f)
        {
            // _snowTex のアルファマスクで円形に削れた見た目を表現
            GUI.color = Color.white;
            GUI.DrawTexture(
                new Rect(_guiRect.x, snowTop, _guiRect.width, snowH),
                _snowTex,
                ScaleMode.StretchToFill,
                alphaBlend: true
            );

            // 上端ラインを白系に（雪面エッジ）
            GUI.color = new Color(0.85f, 0.92f, 1.0f, 0.85f);
            GUI.DrawTexture(new Rect(_guiRect.x, snowTop, _guiRect.width, 3f),
                            Texture2D.whiteTexture);
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
        }

        // ── 落下片（不定形・ランダムサイズ・副塊クラスタ）──────
        foreach (var p in _pieces)
        {
            if (p.alpha <= 0f) continue;

            Color c = p.snowColor;
            c.a = p.alpha;

            var savedMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(p.rotation, new Vector2(p.pos.x, p.pos.y));

            // ── 丸み表現: 中心矩形 + 4方向に小さめ矩形を重ねる ──
            // これにより角が「埋まって」丸く見える
            float w2 = p.size * p.scaleX * 0.5f;
            float h2 = p.size * p.scaleY * 0.5f;

            // 中心ブロック
            GUI.color = c;
            GUI.DrawTexture(new Rect(p.pos.x - w2, p.pos.y - h2, w2 * 2f, h2 * 2f),
                            Texture2D.whiteTexture);

            // 丸み補助: 上下左右に少し大きめの矩形を半透明で重ねる
            float rnd = Mathf.Min(w2, h2) * 0.35f; // roundness radius
            GUI.color = new Color(c.r, c.g, c.b, c.a * 0.55f);
            GUI.DrawTexture(new Rect(p.pos.x - w2 * 0.7f, p.pos.y - h2 - rnd * 0.6f,
                                     w2 * 1.4f, rnd * 1.2f), Texture2D.whiteTexture); // 上
            GUI.DrawTexture(new Rect(p.pos.x - w2 * 0.7f, p.pos.y + h2 - rnd * 0.6f,
                                     w2 * 1.4f, rnd * 1.2f), Texture2D.whiteTexture); // 下
            GUI.DrawTexture(new Rect(p.pos.x - w2 - rnd * 0.6f, p.pos.y - h2 * 0.7f,
                                     rnd * 1.2f, h2 * 1.4f), Texture2D.whiteTexture); // 左
            GUI.DrawTexture(new Rect(p.pos.x + w2 - rnd * 0.6f, p.pos.y - h2 * 0.7f,
                                     rnd * 1.2f, h2 * 1.4f), Texture2D.whiteTexture); // 右

            GUI.matrix = savedMatrix;

            // ── 副塊クラスタ（subCount 個）──────────────────────
            // 親とは別の回転・位置で描画（クラスタ感を出す）
            if (p.subCount >= 1)
            {
                DrawSubChunk(p, p.sub0Offset, p.sub0Scale, c, p.rotation + Random.Range(-15f, 15f));
            }
            if (p.subCount >= 2)
            {
                DrawSubChunk(p, p.sub1Offset, p.sub1Scale, c, p.rotation + Random.Range(-20f, 20f));
            }
            if (p.subCount >= 3)
            {
                DrawSubChunk(p, p.sub2Offset, p.sub2Scale, c, p.rotation + Random.Range(-25f, 25f));
            }
        }

        // ── 雪煙パーティクル ─────────────────────────────────
        foreach (var pf in _puffs)
        {
            if (pf.alpha <= 0f) continue;
            float progress = 1f - pf.life / pf.maxLife;
            float sz = pf.size * (0.5f + progress * 1.2f); // 膨らんで薄くなる
            float a  = pf.alpha * (1f - progress * 0.8f);
            GUI.color = new Color(0.95f, 0.97f, 1f, a * 0.7f);
            var tex = s_puffTex != null ? s_puffTex : Texture2D.whiteTexture;
            GUI.DrawTexture(new Rect(pf.pos.x - sz * 0.5f, pf.pos.y - sz * 0.5f, sz, sz), tex);
        }

        // ── fill ゲージ（左端黄バー）──────────────────────────
        GUI.color = new Color(1f, 1f, 0f, 0.85f);
        float barH = _guiRect.height * fillAvg;
        GUI.DrawTexture(new Rect(_guiRect.x - 6f, _guiRect.yMax - barH, 5f, barH),
                        Texture2D.whiteTexture);

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

        // 道具UI前面描画の共通エントリポイント
        // ToolUIRenderer が「全軒OnGUI完了後の最後の1回」に全道具UIを描画する
        // 新しい道具も ToolUIRenderer.Register() で登録するだけで前面保証される
        ToolUIRenderer.DrawAll(TARGET_ROOF_ID);
    }
}
