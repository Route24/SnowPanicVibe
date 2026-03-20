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
    const float  THICK_RATIO       = 1.30f;  // 旧0.90 → さらに厚く（屋根高さの1.3倍）
    const float  EXPAND_Y_MAX      = 30f;    // 旧20 → 上端への張り出しをさらに増やす

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
    // コンボ・雪崩管理
    int      _comboCount;        // 連続ヒット数（露出セルタップでリセット）
    float    _lastEngulfTotal;   // 直前タップの累計巻き込み量（雪崩判定用）
    int      _avalancheChain;    // 現在の雪崩連鎖数（上限で止める）

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

        // テクスチャ初期化
        _snowTex = new Texture2D(GRID_W, GRID_H, TextureFormat.RGBA32, false);
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

    // ── Texture2D を _snow から再構築 ─────────────────────────
    void RebuildTexture()
    {
        if (_snowTex == null) return;

        // 雪色: 白ベース + 薄い青みの陰影
        var snowColor = new Color(0.92f, 0.95f, 1.00f);
        Debug.Log($"[SNOW_VISUAL_COLOR] class=SnowStrip2D color=({snowColor.r:F2},{snowColor.g:F2},{snowColor.b:F2})");

        // 表面ノイズ: 各列の上端に小さなランダム凸凹を加える
        // SURFACE_NOISE: alpha に加算するノイズ量（0=なし、0.15=強め）
        const float SURFACE_NOISE  = 0.12f;
        // EDGE_ROUNDNESS: 左右端の alpha をどれだけ絞るか（0=なし、1=完全ゼロ）
        const float EDGE_ROUNDNESS = 0.35f;

        for (int x = 0; x < GRID_W; x++)
        for (int y = 0; y < GRID_H; y++)
        {
            float v    = _snow[x, y];
            int   texY = GRID_H - 1 - y;

            // 下段ほど少し暗くして奥行き感
            float shadow = 1f - (float)y / GRID_H * 0.15f;

            // 表面ノイズ: y=0（表面）付近のセルに凸凹を加える
            // 表面に近いほど強く、奥に行くほど弱くなる
            float surfaceProximity = 1f - (float)y / GRID_H; // 表面=1, 奥=0
            float noise = (Mathf.Sin(x * 1.7f + y * 2.3f) * 0.5f + 0.5f) * SURFACE_NOISE * surfaceProximity;
            float noisyAlpha = Mathf.Clamp01(v - noise * (1f - v)); // 雪があるところだけノイズ

            // 左右端の丸み: エッジセルの alpha を絞る
            float edgeFactor = 1f;
            float normX = (float)x / (GRID_W - 1); // 0〜1
            float edgeDist = Mathf.Min(normX, 1f - normX) * 2f; // 0(端)〜1(中央)
            edgeFactor = Mathf.Lerp(1f - EDGE_ROUNDNESS, 1f, edgeDist * edgeDist);

            float finalAlpha = noisyAlpha * edgeFactor;

            _snowTex.SetPixel(x, texY,
                new Color(snowColor.r * shadow, snowColor.g * shadow, snowColor.b * shadow, finalAlpha));
        }
        _snowTex.Apply();
        _texDirty = false;

        Debug.Log($"[SNOW_SURFACE_SHAPE] roof={TARGET_ROOF_ID}" +
                  $" surfaceNoise={SURFACE_NOISE:F2} edgeRoundness={EDGE_ROUNDNESS:F2}" +
                  $" overhangAmount={EXPAND_Y_MAX:F0}px thickRatio={THICK_RATIO:F2}");
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
        const float FP_RX_OLD     = 6f;   // 旧値（ログ用）
        const float FP_RY_OLD     = 4f;   // 旧値（ログ用）
        const float FP_RX         = 8f;   // X方向半径（旧6→8: 約1.33倍）
        const float FP_RY         = 5.5f; // Y方向半径（旧4→5.5: 約1.38倍）
        const float FP_MAX        = 1.0f; // 中心での最大削り量
        const float SEC_RATIO     = 0.25f; // secondary = primary の25%
        Debug.Log($"[HIT_RANGE] roof={TARGET_ROOF_ID}" +
                  $" oldRadiusX={FP_RX_OLD} oldRadiusY={FP_RY_OLD}" +
                  $" newRadiusX={FP_RX} newRadiusY={FP_RY}" +
                  $" expandRatioX={(FP_RX/FP_RX_OLD):F2}x expandRatioY={(FP_RY/FP_RY_OLD):F2}x");
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
                const float AVA_EXTRA_R = 3f; // 追加巻き込み半径
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
            GUI.DrawTexture(new Rect(pf.pos.x - sz * 0.5f, pf.pos.y - sz * 0.5f, sz, sz),
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
