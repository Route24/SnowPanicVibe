using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// SnowStrip 2D — Roof_BR 専用プロトタイプ
///
/// 残雪を 2D float 配列（_snow[x, y]）で管理し、
/// タップ位置を中心とした円形ブラシで減算する。
/// 描画は _snow 配列から毎フレーム Texture2D を再生成して OnGUI で表示。
/// 円形にくり抜かれる見た目を実現する。
///
/// Input System 両対応（新旧 API 自動切替）。
/// SnowStripV2 の Roof_BR 処理を完全に引き継ぐ。
/// </summary>
[DefaultExecutionOrder(11)] // SnowStripV2 の後
public class SnowStrip2D : MonoBehaviour
{
    // ── 定数 ──────────────────────────────────────────────────
    const string CALIB_PATH        = "Assets/Art/RoofCalibrationData.json";
    const string TARGET_ROOF_ID    = "Roof_BR";
    const string TARGET_GUIDE_ID   = "RoofGuide_BR";
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
    Vector2  _downhillDir;
    int      _tapCount;
    string   _lastInfo = "---";
    bool     _lastSpawned;

    // テクスチャ（毎フレーム更新）
    Texture2D _snowTex;
    bool      _texDirty = true;

    // 落下片
    struct Piece
    {
        public Vector2 pos, vel;
        public float   size, life, alpha;
        public float   slideTimer;    // >0 = スライドフェーズ残り時間（重力OFF）
        public float   engulfBudget;  // この滑落が巻き込める残量上限
        public float   engulfTotal;   // 累計巻き込み量（ログ用）
    }
    readonly List<Piece> _pieces = new List<Piece>();

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
    }

    void OnDestroy()
    {
        if (_snowTex != null) { Destroy(_snowTex); _snowTex = null; }
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

        // テクスチャ初期化
        _snowTex = new Texture2D(GRID_W, GRID_H, TextureFormat.RGBA32, false);
        _snowTex.filterMode = FilterMode.Bilinear;
        _texDirty = true;

        _ready = true;
        Debug.Log($"[2D_ROOF_READY] roof={TARGET_ROOF_ID} guiRect={_guiRect}" +
                  $" eaveGuiY={_eaveGuiY:F1} downhill=({_downhillDir.x:F3},{_downhillDir.y:F3})");
    }

    // ── Texture2D を _snow から再構築 ─────────────────────────
    // 各ピクセル = 1グリッドセル。alpha = _snow[x,y]
    // y=0 が表面（テクスチャでは上 = flipY）
    void RebuildTexture()
    {
        if (_snowTex == null) return;

        var cyan = new Color(0f, 0.9f, 0.85f);
        for (int x = 0; x < GRID_W; x++)
        for (int y = 0; y < GRID_H; y++)
        {
            float v = _snow[x, y];
            // テクスチャY=0が下なので flip: texY = GRID_H-1-y
            int texY = GRID_H - 1 - y;
            _snowTex.SetPixel(x, texY, new Color(cyan.r, cyan.g, cyan.b, v));
        }
        _snowTex.Apply();
        _texDirty = false;
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
        const float FP_MAX        = 1.0f; // 中心での最大削り量
        const float SEC_RATIO     = 0.25f; // secondary = primary の25%
        const int   SEC_DEPTH     = 2;    // 下方向2段まで
        const float TAP_TOTAL_CAP = 80f;  // 1タップ上限（暴走防止）

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
            spawnCount = Mathf.Clamp(Mathf.RoundToInt(totalDelta / BRUSH_MAX * 3f), 1, 4);

            // [SPAWN_ENTRY] spawn実行確認
            Debug.Log($"[SPAWN_ENTRY] class=SnowStrip2D method=HandleTap" +
                      $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                      $" spawnCount={spawnCount} totalDelta={totalDelta:F3}");

            // スポーン位置: 屋根上端ではなくタップ位置（屋根面上）
            // → 「上に飛び出す」現象を防ぐ
            const float SLIDE_DURATION = 0.35f; // スライドフェーズの秒数
            const float SLIDE_SPD      = 160f;  // スライド初速（GUI座標/秒）

            float roofW  = _guiRect.width;
            // spawn X: タップ位置付近（屋根面上）
            float spawnX = Mathf.Clamp(guiPos.x, _guiRect.x + 8f, _guiRect.xMax - 8f);
            // spawn Y: 屋根の中央付近（_guiRect.y = 上端、yMax = 下端）
            float spawnY = Mathf.Lerp(_guiRect.y, _guiRect.yMax, 0.3f);

            for (int i = 0; i < spawnCount; i++)
            {
                float jx  = Random.Range(-roofW * 0.08f, roofW * 0.08f);
                float sz  = Mathf.Clamp(roofW * Random.Range(0.06f, 0.16f), 10f, 40f);

                // 初速: downhill 方向のみ（上向き成分なし）
                // スライドフェーズ中は重力を掛けないので pos.y は増加のみ
                Vector2 slideVel = _downhillDir * SLIDE_SPD;

                _pieces.Add(new Piece
                {
                    pos          = new Vector2(spawnX + jx, spawnY),
                    vel          = slideVel,
                    size         = sz,
                    life         = 5f,
                    alpha        = 1f,
                    slideTimer   = SLIDE_DURATION,
                    engulfBudget = 0.8f, // 1滑落あたりの巻き込み上限
                    engulfTotal  = 0f,
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
    // スライドフェーズ中、Piece の GUI 座標を屋根ローカル 2D セルへ変換し、
    // そのセルと近傍に対して小さな巻き込み減算を行う。
    // _guiRect が有効な場合のみ変換を実行。
    //
    void UpdatePieces()
    {
        const float ENGULF_PER_FRAME = 0.04f; // 1フレームあたりの巻き込み量
        const float ENGULF_CELL_R    = 1.5f;  // 巻き込み近傍半径（グリッド単位）
        const float EXPOSED_THR      = 0.01f;

        float dt = Time.deltaTime;
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];

            if (p.slideTimer > 0f)
            {
                // ── スライドフェーズ ──────────────────────────
                p.slideTimer -= dt;
                p.pos        += p.vel * dt;

                // ── 通過セルへの巻き込み ──────────────────────
                if (_ready && p.engulfBudget > 0f && _guiRect.width > 1f)
                {
                    // GUI 座標 → 屋根ローカル正規化座標 → グリッドセル
                    float nx = Mathf.Clamp01((p.pos.x - _guiRect.x) / _guiRect.width);
                    float ny = Mathf.Clamp01((p.pos.y - _guiRect.y) / _guiRect.height);
                    float pgx = nx * GRID_W;
                    float pgy = ny * GRID_H;

                    int ex0 = Mathf.Max(0,          Mathf.FloorToInt(pgx - ENGULF_CELL_R));
                    int ex1 = Mathf.Min(GRID_W - 1, Mathf.CeilToInt (pgx + ENGULF_CELL_R));
                    int ey0 = Mathf.Max(0,          Mathf.FloorToInt(pgy - ENGULF_CELL_R));
                    int ey1 = Mathf.Min(GRID_H - 1, Mathf.CeilToInt (pgy + ENGULF_CELL_R));

                    int   contactCells = 0;
                    float frameEngulf  = 0f;

                    for (int ex = ex0; ex <= ex1; ex++)
                    for (int ey = ey0; ey <= ey1; ey++)
                    {
                        if (_snow[ex, ey] <= EXPOSED_THR) continue;
                        float edx = (ex + 0.5f) - pgx;
                        float edy = (ey + 0.5f) - pgy;
                        if (edx * edx + edy * edy > ENGULF_CELL_R * ENGULF_CELL_R) continue;

                        float take = Mathf.Min(ENGULF_PER_FRAME, _snow[ex, ey],
                                               p.engulfBudget - frameEngulf);
                        if (take <= 0f) continue;

                        _snow[ex, ey] -= take;
                        frameEngulf   += take;
                        contactCells++;
                        _texDirty = true;
                    }

                    p.engulfBudget -= frameEngulf;
                    p.engulfTotal  += frameEngulf;

                    if (frameEngulf > 0f)
                    {
                        // [ENGULF_ENTRY] 巻き込み実行確認
                        Debug.Log($"[ENGULF_ENTRY] class=SnowStrip2D method=UpdatePieces" +
                                  $" roof={TARGET_ROOF_ID} frame={Time.frameCount} instanceId={GetInstanceID()}" +
                                  $" contactCells={contactCells} frameEngulf={frameEngulf:F3}" +
                                  $" totalEngulfed={p.engulfTotal:F3} budgetLeft={p.engulfBudget:F3}");
                        Debug.Log($"[2D_SLIDE_ENGULF] roof={TARGET_ROOF_ID}" +
                                  $" slidePos=({p.pos.x:F0},{p.pos.y:F0})" +
                                  $" gridPos=({pgx:F1},{pgy:F1})" +
                                  $" contactCells={contactCells}" +
                                  $" frameEngulf={frameEngulf:F3}" +
                                  $" totalEngulfed={p.engulfTotal:F3}" +
                                  $" budgetLeft={p.engulfBudget:F3}");
                    }
                }

                // スライド終了時に通常落下速度へ移行
                if (p.slideTimer <= 0f)
                    p.vel = new Vector2(p.vel.x * 0.3f, Mathf.Max(p.vel.y, 60f));
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
                p.pos.y = _eaveGuiY;
                p.vel   = Vector2.zero;
                p.life  = Mathf.Min(p.life, 1.2f);
            }
            if (p.life <= 0f) _pieces.RemoveAt(i);
            else              _pieces[i] = p;
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

            // 上端シアンライン（固定位置）
            GUI.color = new Color(0f, 1f, 1f, 0.9f);
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

        // ── 落下片 ───────────────────────────────────────────
        foreach (var p in _pieces)
        {
            if (p.alpha <= 0f) continue;
            GUI.color = new Color(0f, 0.9f, 0.85f, p.alpha);
            float h = p.size * 0.5f;
            GUI.DrawTexture(new Rect(p.pos.x - h, p.pos.y - h, p.size, p.size),
                            Texture2D.whiteTexture);
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
    }
}
