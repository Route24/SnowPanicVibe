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
    struct Piece { public Vector2 pos, vel; public float size, life, alpha; }
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

        Debug.Log($"[2D_TAP_RAW] screenPos=({screenPos.x:F0},{screenPos.y:F0})" +
                  $" guiPos=({guiPos.x:F0},{guiPos.y:F0})" +
                  $" guiRect={_guiRect} contains={_guiRect.Contains(guiPos)}");

        if (!_guiRect.Contains(guiPos)) return;

        _tapCount++;

        // ── 停止条件定数 ──────────────────────────────────────
        // epsilon: ブラシ後に残った微小値をゼロスナップする閾値
        const float CELL_EPSILON      = 0.08f;
        // finish threshold: 屋根全体の平均残雪がこれ以下なら全セルを即ゼロ化
        // 0.20 = 480セル中96セル相当。残り20%で収束 → 実質20タップ以内に収まる
        const float FINISH_THRESHOLD  = 0.20f;
        // spawn 最小有効削り量: これ未満の totalDelta では落雪しない
        const float SPAWN_MIN_DELTA   = 0.05f;

        // ── タップ位置 → グリッド座標 ──────────────────────────
        float nx = Mathf.Clamp01((guiPos.x - _guiRect.x) / _guiRect.width);
        float ny = Mathf.Clamp01((guiPos.y - _guiRect.y) / _guiRect.height);
        float gx = nx * GRID_W;
        float gy = ny * GRID_H;

        int cx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, GRID_W - 1);
        int cy = Mathf.Clamp(Mathf.FloorToInt(gy), 0, GRID_H - 1);

        float centerBefore = _snow[cx, cy];

        // ── 屋根全体残雪（タップ前）──────────────────────────
        float fillBefore          = CalcFill();
        float totalRoofSnowBefore = fillBefore * GRID_W * GRID_H;

        // ── 露出判定: hit position の center cell が露出していたら即ブロック ──
        // 参照元: _snow[cx, cy]（タップ位置に対応する 2D セル）
        // center cell が空 = 屋根が見えている = 落雪させない
        const float EXPOSED_CELL_THRESHOLD = 0.01f;
        bool exposedAtHit = centerBefore <= EXPOSED_CELL_THRESHOLD;
        if (exposedAtHit)
        {
            _lastSpawned = false;
            _lastInfo    = $"TAP#{_tapCount} exposed spawned=NO";
            Debug.Log($"[2D_TAP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0}) gridCenter=({cx},{cy})" +
                      $" exposedAtHit=YES centerSnow={centerBefore:F3}" +
                      $" totalRoofSnowBefore={totalRoofSnowBefore:F1}" +
                      $" totalSnowInBrush=- delta=0 spawned=NO [EXPOSED_HARD_STOP]");
            return;
        }

        // ── 屋根全体が既に 0 なら即ブロック ──────────────────
        if (fillBefore <= 0f)
        {
            _lastSpawned = false;
            _lastInfo    = $"TAP#{_tapCount} roofEmpty spawned=NO";
            Debug.Log($"[2D_TAP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0}) gridCenter=({cx},{cy})" +
                      $" exposedAtHit=NO totalRoofSnowBefore={totalRoofSnowBefore:F1}" +
                      $" totalSnowInBrush=0 delta=0 spawned=NO [ROOF_EMPTY]");
            return;
        }

        // ── ブラシ範囲計算 ────────────────────────────────────
        int bx0 = Mathf.Max(0,          Mathf.FloorToInt(gx - BRUSH_R));
        int bx1 = Mathf.Min(GRID_W - 1, Mathf.CeilToInt (gx + BRUSH_R));
        int by0 = Mathf.Max(0,          Mathf.FloorToInt(gy - BRUSH_R));
        int by1 = Mathf.Min(GRID_H - 1, Mathf.CeilToInt (gy + BRUSH_R));

        // ── ブラシ内総残雪を計算 ──────────────────────────────
        float totalSnowInBrush = 0f;
        int   brushCells       = 0;
        for (int bx = bx0; bx <= bx1; bx++)
        for (int by = by0; by <= by1; by++)
        {
            float dx = (bx + 0.5f) - gx;
            float dy = (by + 0.5f) - gy;
            if (Mathf.Sqrt(dx * dx + dy * dy) >= BRUSH_R) continue;
            totalSnowInBrush += _snow[bx, by];
            brushCells++;
        }

        // ── 停止条件: ブラシ内に雪がない ─────────────────────
        if (totalSnowInBrush <= 0f)
        {
            _lastSpawned = false;
            _lastInfo    = $"TAP#{_tapCount} brushEmpty spawned=NO";
            Debug.Log($"[2D_TAP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0}) gridCenter=({cx},{cy})" +
                      $" exposedAtHit=NO totalRoofSnowBefore={totalRoofSnowBefore:F1}" +
                      $" totalSnowInBrush={totalSnowInBrush:F3} brushCells={brushCells}" +
                      $" delta=0 spawned=NO [BRUSH_EMPTY]");
            return;
        }

        // ── 円形ブラシで 2D 減算 ──────────────────────────────
        float totalDelta  = 0f;
        float centerDelta = 0f;
        int   hitCells    = 0;

        for (int bx = bx0; bx <= bx1; bx++)
        for (int by = by0; by <= by1; by++)
        {
            float dx   = (bx + 0.5f) - gx;
            float dy   = (by + 0.5f) - gy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist >= BRUSH_R) continue;

            // smoothstep フォールオフ（中心=1, 外周→0）
            float t = 1f - dist / BRUSH_R;
            float w = t * t * (3f - 2f * t);

            float d = Mathf.Min(w * BRUSH_MAX, _snow[bx, by]);
            if (d <= 0f) continue;

            _snow[bx, by] -= d;
            totalDelta    += d;
            hitCells++;
            if (bx == cx && by == cy) centerDelta = d;
        }

        // ── 滑落: 削った雪を下方向セルへ移す ────────────────
        //
        // ルール:
        //   - 削ったセル (bx, by) ごとに、同 X の1つ下 (bx, by+1) へ雪を移す
        //   - 下セルの空き容量（1.0 - 現在値）分だけ受け取れる
        //   - 受け取れなかった分は落雪 spawn に回す（下端も同様）
        //   - 滑落先セルが露出（空）でも受け取れる（雪を積み上げる）
        //   - SLIDE_RATIO: 削り量のうち滑落に回す割合（残りは即落雪）
        //
        const float SLIDE_RATIO = 0.7f; // 削り量の70%を滑落、30%を即落雪

        float totalSlid    = 0f;
        float totalFalloff = 0f; // 滑落できなかった分（spawn に回る）

        for (int bx = bx0; bx <= bx1; bx++)
        for (int by = by0; by <= by1; by++)
        {
            float dx   = (bx + 0.5f) - gx;
            float dy   = (by + 0.5f) - gy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist >= BRUSH_R) continue;

            // このセルで削った量を再計算（ブラシ適用後の差分）
            float t = 1f - dist / BRUSH_R;
            float w = t * t * (3f - 2f * t);
            // 削り量の上限は BRUSH_MAX * w だが、実際に削った量は totalDelta 按分では
            // なく、セルごとに独立して計算する。ここでは削り量の近似値を使う。
            // （正確には減算ループで記録すべきだが、シンプルさを優先）
            float approxRemoved = w * BRUSH_MAX * SLIDE_RATIO;
            if (approxRemoved <= 0f) continue;

            float slideAmount = approxRemoved;

            if (by + 1 < GRID_H)
            {
                // 下セルの空き容量
                float capacity = Mathf.Max(0f, 1f - _snow[bx, by + 1]);
                float canSlide = Mathf.Min(slideAmount, capacity);
                _snow[bx, by + 1] += canSlide;
                totalSlid    += canSlide;
                totalFalloff += slideAmount - canSlide;
            }
            else
            {
                // 下端: 滑落先なし → 全量 falloff
                totalFalloff += slideAmount;
            }
        }

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

        // ── finish assist: 残雪が FINISH_THRESHOLD 以下なら全ゼロ化 ──
        float fillMid     = CalcFill();
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
        float centerAfter        = _snow[cx, cy];

        // ── 停止条件 2: finishAssist 後は spawn しない ────────
        // 停止条件 3: 落雪量（totalFalloff）が最小有効量未満なら spawn しない
        // spawn 量は totalDelta ではなく totalFalloff（滑落できなかった分）に基づく
        float spawnBasis = totalFalloff > 0f ? totalFalloff : totalDelta * (1f - SLIDE_RATIO);
        bool spawned   = !finishAssist && spawnBasis >= SPAWN_MIN_DELTA;
        int  spawnCount = 0;

        if (spawned)
        {
            spawnCount = Mathf.Clamp(Mathf.RoundToInt(spawnBasis / BRUSH_MAX * 3f), 1, 4);

            float roofW  = _guiRect.width;
            float spawnX = Mathf.Clamp(guiPos.x, _guiRect.x + 8f, _guiRect.xMax - 8f);
            float spawnY = _guiRect.y;

            for (int i = 0; i < spawnCount; i++)
            {
                float jx  = Random.Range(-roofW * 0.10f, roofW * 0.10f);
                float sz  = Mathf.Clamp(roofW * Random.Range(0.08f, 0.20f), 12f, 50f);
                float spd = Random.Range(80f, 180f);

                _pieces.Add(new Piece
                {
                    pos   = new Vector2(spawnX + jx, spawnY),
                    vel   = new Vector2(_downhillDir.x * spd * 0.4f, _downhillDir.y * spd),
                    size  = sz,
                    life  = 5f,
                    alpha = 1f,
                });
            }
        }

        _lastInfo    = $"TAP#{_tapCount} fill={fillAfter:F2} sp={(spawned ? spawnCount.ToString() : "NO")}";
        _lastSpawned = spawned;

        // 唯一の真実: _snow[x,y] 配列
        //   露出判定: _snow[cx,cy] <= EXPOSED_CELL_THRESHOLD
        //   減算:     _snow[bx,by] -= d
        //   マスク:   _texDirty=true → RebuildTexture() が _snow から再構築
        //   spawn:    totalDelta（_snow から削った合計）
        //   finish:   CalcFill()（_snow の全セル合計）
        int exposedCellCount = 0;
        for (int ex = 0; ex < GRID_W; ex++)
        for (int ey = 0; ey < GRID_H; ey++)
            if (_snow[ex, ey] <= EXPOSED_CELL_THRESHOLD) exposedCellCount++;
        float exposedAreaRatio = (float)exposedCellCount / (GRID_W * GRID_H);

        Debug.Log($"[2D_TAP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                  $" tapCount={_tapCount}" +
                  $" hitPos=({guiPos.x:F0},{guiPos.y:F0})" +
                  $" gridCenter=({cx},{cy}) brushRadius={BRUSH_R}" +
                  $" exposedAtHit=NO centerSnow_before={centerBefore:F3}" +
                  $" totalSnowInBrush={totalSnowInBrush:F3} brushCells={brushCells} hitCells={hitCells}" +
                  $" totalRoofSnowBefore={totalRoofSnowBefore:F1} totalRoofSnowAfter={totalRoofSnowAfter:F1}" +
                  $" removedAmount={totalDelta:F2}" +
                  $" slidAmount={totalSlid:F2}" +
                  $" falloffAmount={totalFalloff:F2}" +
                  $" spawnBasis={spawnBasis:F2}" +
                  $" centerDelta={centerDelta:F3}" +
                  $" fillBefore={fillBefore:F3} fillAfter={fillAfter:F3}" +
                  $" exposedAreaRatio={exposedAreaRatio:F2}" +
                  $" zeroSnapCount={zeroSnapCount}" +
                  $" finishAssist={(finishAssist ? "YES" : "NO")}" +
                  $" spawned={(spawned ? $"YES({spawnCount})" : "NO")}" +
                  $" SLIDE_RATIO={SLIDE_RATIO} BRUSH_MAX={BRUSH_MAX} FINISH_THRESHOLD={FINISH_THRESHOLD}");

        if (fillAfter <= 0f)
            Debug.Log($"[2D_TAP#{_tapCount}] roof={TARGET_ROOF_ID} fill=0 allCleared=YES");
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
    void UpdatePieces()
    {
        float dt = Time.deltaTime;
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];
            p.vel.y += 500f * dt;
            p.pos   += p.vel * dt;
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
