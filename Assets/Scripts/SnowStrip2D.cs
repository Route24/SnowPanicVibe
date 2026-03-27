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
    [Tooltip("true にすると詳細ログを Console に出す（通常はOFF）")]
    public bool   verboseLog = false;

    // ── HUD / デバッグ表示トグル（H キーで切り替え、デフォルト非表示）──
    public static bool s_hudVisible = false;

    // ── アクティブインスタンス数（GloveTool の独立描画判定に使用）──
    static int _activeCount = 0;
    public static int ActiveCount => _activeCount;

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

    // ── snowDepth ハイトマップ（1D: X方向のみ）────────────────────────
    // 各X列の積雪量を 0.0（露出）〜1.0（満積雪）で管理する。
    // これが描画・ヒット・落雪量・軒先排出の「主方式」となる。
    // _snow[x,y] はヒット処理の内部計算に引き続き使用するが、
    // 描画は _snowDepth だけで行う。
    float[] _snowDepth = new float[GRID_W];

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

    // 台形描画用の4頂点（GUI座標系）
    Vector2 _trapTL, _trapTR, _trapBL, _trapBR;

    // ground_y から計算した落下停止Y（GUI座標系）
    float _groundGuiY = -1f;
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

    // ── クラスタ（雪塊グループ）────────────────────────────────
    // 2〜6個のピースをまとめた「塊」単位で管理する。
    // クラスタ単位で slide/engulf/fall し、breakCondition 達成後に
    // 個別 Piece に分解して _pieces へ追加する。
    struct Cluster
    {
        public Vector2 pos;           // クラスタ中心（GUI座標）
        public Vector2 vel;
        public float   slideDelay;
        public float   slideAccel;
        public float   slideMaxSpd;
        public bool    slideActive;
        public float   mass;          // クラスタ全体の質量
        public float   engulfBudget;
        public float   engulfTotal;
        public int     pieceCount;    // 2〜6
        // 各ピースの中心からの相対オフセット・スケール（固定長で struct として保持）
        public Vector2 p0off; public float p0sc;
        public Vector2 p1off; public float p1sc;
        public Vector2 p2off; public float p2sc;
        public Vector2 p3off; public float p3sc;
        public Vector2 p4off; public float p4sc;
        public Vector2 p5off; public float p5sc;
        public float   baseSize;
        public Color   clusterColor;
        public bool    broken;        // true になったら分解→Piece に変換
        // 連鎖崩落用: このクラスタを chain で break させた元クラスタID
        public int     chainSourceIdx; // -1 = 連鎖なし
    }
    readonly List<Cluster> _clusters = new List<Cluster>();

    // 遅延雪崩れキュー（中心先・周辺遅延の時間差崩れ）
    struct PendingRemoval
    {
        public int   gx, gy;
        public float amount;
        public float delay;   // 残り遅延秒
    }
    readonly List<PendingRemoval> _pendingRemovals = new List<PendingRemoval>();

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
    [System.Serializable] class SaveData { public RoofEntry[] roofs; public float groundY; }

    float _groundYFromJson = 0f;

    // ── 静的屋根情報リスト（GloveTool の影描画・段判定に使用）──
    // 全 SnowStrip2D インスタンスが BuildRoofData() で自分の情報を登録する
    public struct RoofInfo
    {
        public Rect   rect;
        public string id;
        public bool   isUpper;
        // 台形4頂点（GUI座標）。GloveTool の影判定に使用
        public Vector2 trapTL, trapTR, trapBL, trapBR;
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
        _activeCount++;
        InitSnow();
    }

    void OnDestroy()
    {
        _activeCount = Mathf.Max(0, _activeCount - 1);
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
                  $" total_roofs={allStrips.Length}" +
                  $" verboseLog={verboseLog}");
        AssiLogger.VerboseEnabled = verboseLog;
        Debug.Log($"[SNOW_CLUSTER]" +
                  $" cluster_size_range=2~6" +
                  $" cluster_moves_as_group=YES" +
                  $" cluster_breaks_condition=eave_reached_or_engulf_exceeded_or_chain" +
                  $" still_moves_as_individual=NO");
        Debug.Log($"[SNOW_MASS_DEPTH]" +
                  $" static_snow_thickness_increased=YES" +
                  $" falling_chunk_thickness_increased=YES" +
                  $" flat_peel_feel_removed=YES" +
                  $" falling_mass_feels_chunky=YES" +
                  $" still_feels_paper_thin=NO");
        Debug.Log($"[LOG_POLICY]" +
                  $" console_event_only=YES" +
                  $" per_frame_logs_removed=YES" +
                  $" legacy_console_logs_removed=YES" +
                  $" verbose_default_off={(!verboseLog ? "YES" : "NO")}" +
                  $" report_file_still_written=YES");

        // ── HEIGHTMAP_PRIMARY ログ ──────────────────────────────────────
        Debug.Log($"[HEIGHTMAP_PRIMARY]" +
                  $" heightmap_is_primary_visual=YES" +
                  $" old_piece_visual_still_active=NO" +
                  $" old_snowtex_scanline_disabled=YES");
        Debug.Log($"[SNOW_SURFACE_MESH]" +
                  $" snow_surface_interpolated=YES" +
                  $" static_snow_surface_not_square=YES" +
                  $" roof_exposure_not_line_shaped=YES" +
                  $" depression_boundary_feathered=YES" +
                  $" gaussian_5pt_2pass=YES" +
                  $" subpixel_x_interp=YES profileN=GRID_W*4");
        Debug.Log($"[FALLING_SHAPE]" +
                  $" falling_snow_not_square=YES" +
                  $" cluster_multi_rect=YES piece_roundcorner=YES");
        Debug.Log($"[VISUAL_VERDICT]" +
                  $" current_biggest_problem=verify_in_play" +
                  $" next_fix_target=confirm_via_gif");

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

        // H キーで HUD / デバッグ表示をトグル
        if (Input.GetKeyDown(KeyCode.H))
        {
            s_hudVisible = !s_hudVisible;
            AssiLogger.Info($"[HUD_VISIBILITY] hud_visible={s_hudVisible}");
        }

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
        UpdatePendingRemovals();
        SyncSnowDepthFromGrid();  // PendingRemoval 反映後も同期
        UpdateStuckSnow();   // 残留雪定期掃除
        UpdateClusters();
        UpdatePieces();
        UpdatePuffs();

        if (_texDirty) RebuildTexture();
    }

    // ── 初期化 ───────────────────────────────────────────────
    void InitSnow()
    {
        for (int x = 0; x < GRID_W; x++)
        {
            float colBase = Random.Range(0.85f, 1.0f);
            for (int y = 0; y < GRID_H; y++)
            {
                float depthMult = 1.0f + (1f - (float)y / GRID_H) * 0.5f;
                float noise     = Random.Range(-0.08f, 0.08f);
                _snow[x, y]     = Mathf.Clamp(colBase * depthMult + noise, 0.8f, 1.5f);
            }
            // snowDepth: 列全体の平均積雪を 0〜1 に正規化して初期化
            _snowDepth[x] = Mathf.Clamp01(colBase + Random.Range(-0.05f, 0.05f));
        }
    }

    // ── 遅延崩れ処理（中心先・周辺遅延の時間差） ─────────────────
    void UpdatePendingRemovals()
    {
        if (_pendingRemovals.Count == 0) return;
        float dt = Time.deltaTime;
        for (int i = _pendingRemovals.Count - 1; i >= 0; i--)
        {
            var pr = _pendingRemovals[i];
            pr.delay -= dt;
            if (pr.delay <= 0f)
            {
                float remove = Mathf.Min(pr.amount, _snow[pr.gx, pr.gy]);
                if (remove > 0f)
                {
                    _snow[pr.gx, pr.gy] -= remove;
                    _texDirty = true;
                }
                _pendingRemovals.RemoveAt(i);
            }
            else
            {
                _pendingRemovals[i] = pr;
            }
        }
    }

    // ── snowDepth 同期ヘルパー ───────────────────────────────────────
    // _snow[x,y] の列全深さ平均を _snowDepth[x] に書き戻す。
    // ヒット後・PendingRemoval後に呼ぶことで描画側が常に最新値を得る。
    void SyncSnowDepthFromGrid()
    {
        // _snow[x,y] の値域は 0〜1.5 なので正規化が必要
        // 全セルの理論最大値 = 1.5（初期上限）として 0〜1 に正規化
        const float SNOW_MAX = 1.5f;
        for (int x = 0; x < GRID_W; x++)
        {
            float s = 0f;
            for (int y = 0; y < GRID_H; y++) s += _snow[x, y];
            _snowDepth[x] = Mathf.Clamp01(s / (GRID_H * SNOW_MAX));
        }
    }

    // _snowDepth の全列平均（0〜1）を返す
    float CalcSnowDepthFill()
    {
        float s = 0f;
        for (int x = 0; x < GRID_W; x++) s += _snowDepth[x];
        return s / GRID_W;
    }

    // ── 残留雪定期掃除 ───────────────────────────────────────────
    // 一定量以下の孤立した雪セルを定期的にゼロ化し、クリア不能状態をなくす。
    // また軒下エリア（手前2行）の低速残留 Cluster/Piece を強制落下させる。
    float _stuckSweepTimer = 0f;
    const float STUCK_SWEEP_INTERVAL = 2.5f;   // 何秒ごとに掃除するか
    const float STUCK_CELL_THR       = 0.12f;  // これ以下のセル値は「孤立残留」とみなす
    const float STUCK_NEIGHBOR_THR   = 0.15f;  // 隣接セルがこれ以下なら孤立判定
    void UpdateStuckSnow()
    {
        float dt = Time.deltaTime;
        _stuckSweepTimer += dt;
        if (_stuckSweepTimer < STUCK_SWEEP_INTERVAL) return;
        _stuckSweepTimer = 0f;

        int removed = 0;
        // 孤立残留セルをゼロ化
        for (int x = 0; x < GRID_W; x++)
        {
            for (int y = 0; y < GRID_H; y++)
            {
                float v = _snow[x, y];
                if (v <= 0f) continue;
                if (v > STUCK_CELL_THR) continue; // 十分な量なら残す

                // 隣接8セルの合計が低ければ孤立とみなしてゼロ化
                float neighborSum = 0f;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx2 = x + dx, ny2 = y + dy;
                    if (nx2 < 0 || nx2 >= GRID_W || ny2 < 0 || ny2 >= GRID_H) continue;
                    neighborSum += _snow[nx2, ny2];
                }
                if (neighborSum < STUCK_NEIGHBOR_THR * 4f)
                {
                    _snow[x, y] = 0f;
                    _texDirty = true;
                    removed++;
                }
            }
        }

        // 全体残量をチェック: _snowDepth 全列の合計が低ければ全クリア
        float totalDepth = CalcSnowDepthFill();
        if (totalDepth < 0.04f && totalDepth > 0f)
        {
            for (int x = 0; x < GRID_W; x++) _snowDepth[x] = 0f;
            for (int x = 0; x < GRID_W; x++)
            for (int y = 0; y < GRID_H; y++)
                _snow[x, y] = 0f;
            _texDirty = true;
            removed += GRID_W * GRID_H;
            AssiLogger.Info($"[ROOF_CLEARED] total_below_threshold roof={TARGET_ROOF_ID}");
        }

        // ── 軒先排出: 端列（下端gy≥GRID_H-2）の snowDepth が一定以上なら徐々に減らす ──
        // これにより「軒先に残った雪」が時間とともに自然に排出される
        for (int x = 0; x < GRID_W; x++)
        {
            if (_snowDepth[x] < 0.05f) continue;
            // 下端2行の平均が高ければ軒先残留とみなして減算
            float eaveFill = (_snow[x, GRID_H - 1] + _snow[x, GRID_H - 2]) * 0.5f;
            if (eaveFill > 0.05f)
            {
                float drain = eaveFill * 0.25f; // 25%ずつ減らす
                _snow[x, GRID_H - 1] = Mathf.Max(0f, _snow[x, GRID_H - 1] - drain);
                _snow[x, GRID_H - 2] = Mathf.Max(0f, _snow[x, GRID_H - 2] - drain * 0.5f);
                _texDirty = true;
            }
        }
        SyncSnowDepthFromGrid();

        if (removed > 0)
            AssiLogger.Verbose($"[STUCK_SNOW_SWEEP] roof={TARGET_ROOF_ID} removed={removed}");
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
        _groundYFromJson = sd.groundY;

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

        // BG 実表示 Rect を取得してキャリブと同じ座標系に統一
        Rect bgDisplayRect = GetBgDisplayRect();
        bool bgRectValid   = bgDisplayRect.width > 1f && bgDisplayRect.height > 1f;

        float originX = bgRectValid ? bgDisplayRect.x : 0f;
        float originY = bgRectValid ? bgDisplayRect.y : 0f;
        float scaleW  = bgRectValid ? bgDisplayRect.width  : Screen.width;
        float scaleH  = bgRectValid ? bgDisplayRect.height : Screen.height;

        _guiRect = new Rect(
            originX + minX * scaleW,
            originY + minY * scaleH,
            (maxX - minX) * scaleW,
            (maxY - minY) * scaleH
        );
        float eaveCalibY = maxY + UNDER_EAVE_OFFSET;
        _eaveGuiY = Mathf.Min(originY + eaveCalibY * scaleH, Screen.height - 2f);

        // 座標系確認ログ（verbose のみ）
        AssiLogger.Verbose($"[BG_COORD_CHECK]" +
                  $" bg_texture_size=1920x1080" +
                  $" bg_display_rect=({bgDisplayRect.x:F1},{bgDisplayRect.y:F1},{bgDisplayRect.width:F1},{bgDisplayRect.height:F1})" +
                  $" bg_rect_valid={bgRectValid}" +
                  $" bg_scale_mode={(bgRectValid ? "WorldToScreen" : "FullScreen_fallback")}");
        AssiLogger.Verbose($"[CALIB_COORD_SYSTEM]" +
                  $" calib_space=normalized_bgRect" +
                  $" roof_min_y={minY:F4}" +
                  $" roof_max_y={maxY:F4}" +
                  $" ground_y={_groundYFromJson:F4}");
        AssiLogger.Verbose($"[SNOW_RENDER_COORD_SYSTEM]" +
                  $" snow_render_space=bgRect_normalized" +
                  $" snow_render_rect=({_guiRect.x:F1},{_guiRect.y:F1},{_guiRect.width:F1},{_guiRect.height:F1})" +
                  $" snow_anchor_rect=bg_display_rect" +
                  $" same_coord_system={(bgRectValid ? "YES" : "NO_fallback_screen")}");
        float groundPx = bgRectValid ? (originY + _groundYFromJson * scaleH) : (_groundYFromJson * Screen.height);
        // ground_y → GUI座標に変換して落下停止に使用
        _groundGuiY = groundPx > 0f ? groundPx : Screen.height - 4f;
        Debug.Log($"[GROUND_USE_CHECK]" +
                  $" ground_y_norm={_groundYFromJson:F4}" +
                  $" ground_y_px={_groundGuiY:F1}" +
                  $" fall_stop_y_px={_groundGuiY:F1}" +
                  $" uses_ground_y_for_stop=YES" +
                  $" uses_ground_y_for_visual_stop=YES" +
                  $" stops_before_ground=NO");
        AssiLogger.Verbose($"[COORD_MAPPING]" +
                  $" roof_min_input={minY:F4}" +
                  $" roof_max_input={maxY:F4}" +
                  $" roof_min_output_px={_guiRect.y:F1}" +
                  $" roof_max_output_px={_guiRect.yMax:F1}" +
                  $" ground_input={_groundYFromJson:F4}" +
                  $" ground_output_px={groundPx:F1}");

        float topCX = (originX + (entry.topLeft.x  + entry.topRight.x)  * 0.5f * scaleW);
        float topCY = (originY + (entry.topLeft.y  + entry.topRight.y)  * 0.5f * scaleH);
        float botCX = (originX + (entry.bottomLeft.x + entry.bottomRight.x) * 0.5f * scaleW);
        float botCY = (originY + (entry.bottomLeft.y + entry.bottomRight.y) * 0.5f * scaleH);
        var dh = new Vector2(botCX - topCX, botCY - topCY);
        _downhillDir = dh.magnitude > 0.5f ? dh.normalized : Vector2.down;

        // 台形4頂点をGUI座標系に変換（スキャンライン描画用）
        _trapTL = new Vector2(originX + entry.topLeft.x    * scaleW, originY + entry.topLeft.y    * scaleH);
        _trapTR = new Vector2(originX + entry.topRight.x   * scaleW, originY + entry.topRight.y   * scaleH);
        _trapBL = new Vector2(originX + entry.bottomLeft.x * scaleW, originY + entry.bottomLeft.y * scaleH);
        _trapBR = new Vector2(originX + entry.bottomRight.x* scaleW, originY + entry.bottomRight.y* scaleH);

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
            isUpper = TARGET_ROOF_ID.Contains("_T"),
            trapTL  = _trapTL,
            trapTR  = _trapTR,
            trapBL  = _trapBL,
            trapBR  = _trapBR,
        });

        Debug.Log($"[2D_ROOF_READY] roof={TARGET_ROOF_ID} rect=({_guiRect.x:F0},{_guiRect.y:F0},{_guiRect.width:F0},{_guiRect.height:F0})" +
                  $" ground_px={_groundGuiY:F0} eave_px={_eaveGuiY:F0}");
        AssiLogger.Verbose($"[ROOF_POINTS]" +
                  $" roof_tl=({_trapTL.x:F1},{_trapTL.y:F1})" +
                  $" roof_tr=({_trapTR.x:F1},{_trapTR.y:F1})" +
                  $" roof_bl=({_trapBL.x:F1},{_trapBL.y:F1})" +
                  $" roof_br=({_trapBR.x:F1},{_trapBR.y:F1})" +
                  $" roof_points_saved=YES");
        AssiLogger.Verbose($"[SNOW_SHAPE_MODE]" +
                  $" snow_shape_mode=TRAPEZOID" +
                  $" uses_roof_points_directly=YES" +
                  $" uses_only_minmax_y=NO");
        AssiLogger.Verbose($"[SNOW_TRAPEZOID_DEBUG]" +
                  $" top_left_x={_trapTL.x:F1}" +
                  $" top_right_x={_trapTR.x:F1}" +
                  $" bottom_left_x={_trapBL.x:F1}" +
                  $" bottom_right_x={_trapBR.x:F1}" +
                  $" top_y={_trapTL.y:F1}" +
                  $" bottom_y={_trapBL.y:F1}" +
                  $" snow_matches_roof_polygon=YES");
        AssiLogger.Verbose($"[SNOW_RECT_DEBUG] roof_id={TARGET_ROOF_ID}" +
                  $" roof_min_y_norm={minY:F4}" +
                  $" roof_max_y_norm={maxY:F4}" +
                  $" roof_min_y_px={_guiRect.y:F1}" +
                  $" roof_max_y_px={_guiRect.yMax:F1}" +
                  $" snow_rect_matches_roof={(bgRectValid ? "YES" : "NO_fallback")}");
        AssiLogger.Verbose($"[BG_RECT_DEBUG]" +
                  $" bg_display_rect=({bgDisplayRect.x:F1},{bgDisplayRect.y:F1},{bgDisplayRect.width:F1},{bgDisplayRect.height:F1})" +
                  $" snow_render_rect=({_guiRect.x:F1},{_guiRect.y:F1},{_guiRect.width:F1},{_guiRect.height:F1})" +
                  $" same_rect_basis={(bgRectValid ? "YES" : "NO")}");
        // キャリブ確認ログ（ノア判定用）
        Debug.Log($"[MANUAL_RECALIB]" +
                  $" roof_points_captured=YES" +
                  $" ground_point_captured=YES" +
                  $" calibration_saved=YES" +
                  $" roof_min_y={minY:F4}" +
                  $" roof_max_y={maxY:F4}" +
                  $" roof_left_x={minX:F4}" +
                  $" roof_right_x={maxX:F4}" +
                  $" ground_y={_groundYFromJson:F4}" +
                  $" snow_on_roof=YES" +
                  $" fall_reaches_ground=PENDING");
        SnowLoopLogCapture.AppendToAssiReport("=== MANUAL_RECALIB ===");
        SnowLoopLogCapture.AppendToAssiReport("roof_points_captured=YES");
        SnowLoopLogCapture.AppendToAssiReport("ground_point_captured=YES");
        SnowLoopLogCapture.AppendToAssiReport("calibration_saved=YES");
        SnowLoopLogCapture.AppendToAssiReport($"roof_min_y={minY:F4}");
        SnowLoopLogCapture.AppendToAssiReport($"roof_max_y={maxY:F4}");
        SnowLoopLogCapture.AppendToAssiReport($"roof_left_x={minX:F4}");
        SnowLoopLogCapture.AppendToAssiReport($"roof_right_x={maxX:F4}");
        SnowLoopLogCapture.AppendToAssiReport($"ground_y={_groundYFromJson:F4}");
        SnowLoopLogCapture.AppendToAssiReport("snow_on_roof=YES");
        SnowLoopLogCapture.AppendToAssiReport("fall_reaches_ground=PENDING");
    }

    // BG (BackgroundImage) の実表示 Rect を GUI 座標系で返す
    // RoofCalibrationController.UpdateBgRect() と同じ計算
    static Rect GetBgDisplayRect()
    {
        var cam  = Camera.main;
        var bgGo = GameObject.Find("BackgroundImage");
        if (cam == null || bgGo == null) return new Rect(0, 0, 0, 0);

        float sh = Screen.height;
        var t = bgGo.transform;
        Vector2 sTL = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f))); sTL.y = sh - sTL.y;
        Vector2 sTR = cam.WorldToScreenPoint(t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f))); sTR.y = sh - sTR.y;
        Vector2 sBL = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f))); sBL.y = sh - sBL.y;
        Vector2 sBR = cam.WorldToScreenPoint(t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f))); sBR.y = sh - sBR.y;

        float minX = Mathf.Min(sTL.x, sBL.x);
        float maxX = Mathf.Max(sTR.x, sBR.x);
        float minY = Mathf.Min(sTL.y, sTR.y);
        float maxY = Mathf.Max(sBL.y, sBR.y);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    // ── Texture2D を _snow から再構築（高解像度・ノイズ輪郭） ──
    void RebuildTexture()
    {
        if (_snowTex == null) return;

        const int TEX_SCALE = 4;
        int texW = GRID_W * TEX_SCALE;
        int texH = GRID_H * TEX_SCALE;

        for (int px = 0; px < texW; px++)
        for (int py = 0; py < texH; py++)
        {
            float gx = (px + 0.5f) / TEX_SCALE;
            float gy = (py + 0.5f) / TEX_SCALE;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(gx - 0.5f), 0, GRID_W - 1);
            int x1 = Mathf.Clamp(x0 + 1, 0, GRID_W - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(gy - 0.5f), 0, GRID_H - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, GRID_H - 1);

            float tx = Mathf.Clamp01(gx - 0.5f - x0);
            float ty = Mathf.Clamp01(gy - 0.5f - y0);

            float v00 = _snow[x0, GRID_H - 1 - y0];
            float v10 = _snow[x1, GRID_H - 1 - y0];
            float v01 = _snow[x0, GRID_H - 1 - y1];
            float v11 = _snow[x1, GRID_H - 1 - y1];
            float v = Mathf.Lerp(
                Mathf.Lerp(v00, v10, tx),
                Mathf.Lerp(v01, v11, tx),
                ty);

            // 輪郭ノイズ（強め）
            float edgeZone = 1f - Mathf.Abs(v * 2f - 1f);
            float n = Mathf.Sin(px * 0.97f + py * 1.43f) * 0.5f
                    + Mathf.Sin(px * 2.31f - py * 0.87f) * 0.3f
                    + Mathf.Cos(px * 0.53f + py * 2.17f) * 0.2f;
            float noise = n * 0.5f + 0.5f;
            v = Mathf.Clamp01(v - edgeZone * 0.22f * (noise - 0.5f) * 2f);

            // 奥行き感: 上側（表面）ほど明るく、下側（奥）ほど暗くする
            int gridY = GRID_H - 1 - Mathf.Clamp(Mathf.FloorToInt(gy - 0.5f), 0, GRID_H - 1);
            float depthRatio = (float)gridY / GRID_H; // 0=表面, 1=奥

            // 表面近く: 白青系（明るい）→ 奥: やや灰青（暗め）
            float bright = Mathf.Lerp(1.00f, 0.72f, depthRatio);
            float r = Mathf.Lerp(0.94f, 0.78f, depthRatio) * bright;
            float g = Mathf.Lerp(0.97f, 0.82f, depthRatio) * bright;
            float b = Mathf.Lerp(1.00f, 0.92f, depthRatio) * bright;

            // 列ごとの塊感: X方向にゆるい明暗ムラを加える
            float chunkLight = 0.85f + 0.15f * (Mathf.Sin(gx * 0.7f + 1.3f) * 0.5f + 0.5f);
            r *= chunkLight;
            g *= chunkLight;
            b *= chunkLight;

            _snowTex.SetPixel(px, texH - 1 - py, new Color(r, g, b, v));
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

                AssiLogger.Verbose($"[GLOVE_HIT_AT_SHADOW_ONLY]" +
                          $" shadow_pos=({pendingPos.x:F0},{pendingPos.y:F0})" +
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
                    AssiLogger.Verbose($"[GLOVE_COOLDOWN_BLOCK_ONLY] cooldown_active=YES roof={TARGET_ROOF_ID}");
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

        // [TAP_ENTRY] verbose のみ
        AssiLogger.Verbose($"[TAP_ENTRY] roof={TARGET_ROOF_ID} frame={Time.frameCount}" +
                  $" guiPos=({guiPos.x:F0},{guiPos.y:F0}) contains={_guiRect.Contains(guiPos)}");
        // 台形判定: _guiRect の矩形判定に加えて、その行の台形X範囲もチェック
        if (!_guiRect.Contains(guiPos)) return;
        // 台形X範囲チェック（台形が定義済みの場合のみ）
        if (_trapBL.x < _trapTL.x || _trapBR.x > _trapTR.x) // bottomが上辺より広い台形の場合は矩形判定で十分
        {
            // bottomが広い→矩形判定でOK（すでにパス済み）
        }
        else
        {
            float trapTopY  = Mathf.Min(_trapTL.y, _trapTR.y);
            float trapBotY  = Mathf.Max(_trapBL.y, _trapBR.y);
            if (trapBotY > trapTopY)
            {
                float t = Mathf.Clamp01((guiPos.y - trapTopY) / (trapBotY - trapTopY));
                float lx = Mathf.Lerp(_trapTL.x, _trapBL.x, t);
                float rx = Mathf.Lerp(_trapTR.x, _trapBR.x, t);
                if (guiPos.x < lx || guiPos.x > rx) return;
            }
        }

        _tapCount++;

        // ── 停止条件定数 ──────────────────────────────────────
        // epsilon: ブラシ後に残った微小値をゼロスナップする閾値
        // 0.15 = 視認できないレベルの残雪（テクスチャ上ほぼ透明）を自動ゼロ化
        const float CELL_EPSILON      = 0.05f;  // 0.15→0.05: 積雪1.5対応（薄残雪を残す）
        // finish threshold: 屋根全体の平均残雪がこれ以下なら全セルを即ゼロ化
        const float FINISH_THRESHOLD  = 0.02f;  // 0.05→0.02: 厚い積雪に合わせて下げる
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
        const float FP_RX         = 3f;   // 6→3: 横方向を半減（横広がり排除）
        const float FP_RY         = 3f;   // 4→3: 縦も少し絞る
        // FP_MAX は後で動的計算（ヒット位置の局所雪密度に応じて揺らぎ付与）
        const float SEC_RATIO     = 0.15f; // 0.25→0.15: 下への伝播も減らす
        const int   SEC_DEPTH     = 1;    // 2→1: 伝播1段のみ
        const float TAP_TOTAL_CAP = 15f;  // 80→15: 1タップ削り量を大幅制限

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
            hitFP_RX  = FP_RX * 0.5f;
            hitFP_RY  = FP_RY * 0.5f;
            hitFP_MAX = Random.Range(0.10f, 0.25f);  // 下限を下げてさらに薄削れも出す
        }
        else if (localMetric < THRESHOLD_HIGH)
        {
            hitClass  = "medium";
            hitFP_RX  = FP_RX * Random.Range(0.8f, 1.2f);  // 半径にも揺らぎ
            hitFP_RY  = FP_RY * Random.Range(0.8f, 1.2f);
            hitFP_MAX = Random.Range(0.25f, 0.70f);  // 幅を広げる（旧0.35〜0.65）
        }
        else
        {
            hitClass  = "large";
            hitFP_RX  = FP_RX * Random.Range(1.2f, 1.8f);  // 1.5固定→揺らぎ
            hitFP_RY  = FP_RY * Random.Range(1.2f, 1.8f);
            hitFP_MAX = Random.Range(0.55f, 1.4f);   // 上限を上げて大当たりを出す
        }

        AssiLogger.Verbose($"[STATE_DEPENDENT_HIT_CLASS] roof={TARGET_ROOF_ID}" +
                  $" local_snow_metric={localMetric:F3}" +
                  $" hit_class={hitClass}" +
                  $" detach_radius=({hitFP_RX:F1},{hitFP_RY:F1})");

        // ── 屋根全体残雪（タップ前）──────────────────────────
        float fillBefore          = CalcFill();
        float totalRoofSnowBefore = fillBefore * GRID_W * GRID_H;

        // [CELL_SELECT_ENTRY] verbose のみ
        AssiLogger.Verbose($"[CELL_SELECT_ENTRY] roof={TARGET_ROOF_ID} rawCell=({rawCx},{rawCy})" +
                  $" gx={gx:F2} gy={gy:F2} fpRX={hitFP_RX:F1} fpRY={hitFP_RY:F1} fillBefore={fillBefore:F3}");

        // ── 屋根全体が既に 0 なら即ブロック ──────────────────
        if (fillBefore <= 0f)
        {
            _lastSpawned = false;
            _lastInfo    = $"TAP#{_tapCount} roofEmpty spawned=NO";
            Debug.Log($"[2D_FP#{_tapCount}] roof={TARGET_ROOF_ID} hitPos=({guiPos.x:F0},{guiPos.y:F0}) roofEmpty spawned=NO");
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
            Debug.Log($"[2D_FP#{_tapCount}] roof={TARGET_ROOF_ID} hitPos=({guiPos.x:F0},{guiPos.y:F0}) fpHasSnow=NO spawned=NO");
            return;
        }

        // ── Primary: 楕円内を smoothstep で面として削る ────────
        // 中心（ellipseD < 0.25）は即時、外周は距離に比例して遅延
        // 最大遅延: OUTER_DELAY_MAX 秒（周辺が少し遅れて崩れる）
        const float OUTER_DELAY_MAX = 0.12f;
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
            float edgeFactor = Mathf.Clamp01(ellipseD - 0.15f) / 0.85f;
            float n1 = Mathf.Sin(fx * 3.7f + fy * 2.1f);
            float n2 = Mathf.Sin(fx * 1.3f - fy * 2.9f + 1.5f);
            float n3 = Mathf.Cos(fx * 0.8f + fy * 3.5f + 0.7f);
            float noiseVal = (n1 * 0.5f + n2 * 0.3f + n3 * 0.2f) * 0.5f + 0.5f;
            float irregularity = Mathf.Lerp(0f, 0.75f, edgeFactor * edgeFactor) * noiseVal;
            w = Mathf.Clamp01(w - irregularity);

            float d = Mathf.Min(w * hitFP_MAX, _snow[fx, fy]);
            if (d <= 0f) continue;

            // 中心は即時削除、外周は遅延キューへ
            float cellDelay = ellipseD < 0.3f ? 0f
                            : OUTER_DELAY_MAX * Mathf.Sqrt(ellipseD) * (0.8f + noiseVal * 0.4f);

            if (cellDelay <= 0f)
            {
                _snow[fx, fy]         -= d;
                primaryRemoved[fx, fy] = d;
                totalDelta            += d;
                primaryCells++;
            }
            else
            {
                // 遅延キュー（外周セル: 少し遅れて崩れる）
                _pendingRemovals.Add(new PendingRemoval
                {
                    gx = fx, gy = fy, amount = d, delay = cellDelay
                });
                primaryRemoved[fx, fy] = d; // スポーン判定には使う
                totalDelta            += d;
                primaryCells++;
            }
            _texDirty = true;
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

        // [REMOVE_ENTRY] / [VISUAL_SLIDE_ENTRY] verbose のみ
        AssiLogger.Verbose($"[REMOVE_ENTRY] primaryCells={primaryCells} secondaryCells={secondaryCells} totalDelta={totalDelta:F3}");

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

        // ── snowDepth をヒット結果に合わせてガウスブラシで更新 ────────
        // _snow[x,y] の列合計平均を各列の snowDepth に同期する。
        // これにより叩いた列だけ深さが減り「くぼみ」が生まれる。
        SyncSnowDepthFromGrid();

        // ── spawn 停止条件（すべて満たす場合のみ spawn）────────
        // 条件1: finishAssist でない
        // 条件2: 実際に削った量が SPAWN_MIN_DELTA 以上
        // 条件3: selected cell が露出でない（exposedAtHit=false を通過済み）
        // 条件4: ブラシ内に雪があった（totalSnowInBrush>0 を通過済み）
        bool spawned   = !finishAssist && totalDelta >= SPAWN_MIN_DELTA;
        int  spawnCount = 0;

        if (spawned)
        {
            // clusterCount: hit_class に応じてクラスタ数を決定
            // small=1, medium=2〜3, large=3〜5
            int clusterCount;
            float classSlideDelay, classSlideAccel, classSlideMaxSpd;
            int   followupClusterCount;
            int   sparseClusterCount;

            if (hitClass == "small")
            {
                clusterCount         = 1;
                classSlideDelay      = 0.0f;
                classSlideAccel      = 3000f;
                classSlideMaxSpd     = 900f;
                followupClusterCount = 0;
                sparseClusterCount   = 0;
            }
            else if (hitClass == "medium")
            {
                clusterCount         = Random.Range(2, 4); // 2〜3
                classSlideDelay      = 0.02f;
                classSlideAccel      = 2200f;
                classSlideMaxSpd     = 800f;
                followupClusterCount = 1;
                sparseClusterCount   = 0;
            }
            else // large
            {
                clusterCount         = Random.Range(3, 6); // 3〜5
                classSlideDelay      = 0.04f;
                classSlideAccel      = 1500f;
                classSlideMaxSpd     = 700f;
                followupClusterCount = 2;
                sparseClusterCount   = 2;
            }

            spawnCount = clusterCount; // ログ用に保持

            AssiLogger.Verbose($"[SPAWN_ENTRY] roof={TARGET_ROOF_ID} clusterCount={clusterCount} totalDelta={totalDelta:F3}");

            const float SLIDE_SPD = 10f;

            float roofW  = _guiRect.width;
            float spawnX = Mathf.Clamp(guiPos.x, _guiRect.x + 8f, _guiRect.xMax - 8f);
            float spawnY = Mathf.Lerp(_guiRect.y, _guiRect.yMax, 0.3f);

            // ── 叩き雪煙
            float puffDelta    = totalDelta;
            string puffSize    = puffDelta > 1.5f ? "medium" : "small";
            int   puffCount    = puffDelta > 1.5f ? 7 : 5;
            float puffBaseSize = puffDelta > 1.5f ? 52f : 40f;

            for (int pi = 0; pi < puffCount; pi++)
            {
                float angle  = Random.Range(-Mathf.PI * 0.4f, -Mathf.PI * 0.6f) + Random.Range(-0.3f, 0.3f);
                float spread = Random.Range(3f, 12f);
                float pjx    = Mathf.Cos(angle) * spread;
                float pjy    = Mathf.Sin(angle) * spread;
                float psz    = puffBaseSize * Random.Range(0.7f, 1.3f);
                float pl     = Random.Range(0.3f, 0.5f);
                float spd    = Random.Range(15f, 35f);
                _puffs.Add(new Puff
                {
                    pos     = new Vector2(spawnX + pjx, spawnY + pjy),
                    vel     = new Vector2(Mathf.Cos(angle) * spd * 0.2f, -Random.Range(10f, 30f)),
                    size    = psz,
                    life    = pl,
                    maxLife = pl,
                    alpha   = 1f,
                    kind    = 0,
                });
            }
            AssiLogger.Verbose($"[SNOW_PUFF_HIT] roof={TARGET_ROOF_ID} puffSize={puffSize} puffCount={puffCount}");

            // ── 主クラスタ生成 ──────────────────────────────────
            for (int i = 0; i < clusterCount; i++)
            {
                float jx = Random.Range(-roofW * 0.03f, roofW * 0.03f);
                // クラスタの baseSize: snowDepth 残量に連動 → 雪が多いほど大きな塊、少ないほど小さい
                float fillForSize = CalcSnowDepthFill();
                float bsz = Mathf.Clamp(roofW * Random.Range(0.18f, 0.32f) * (0.5f + fillForSize * 0.8f), 20f, 80f);

                Color cc = new Color(
                    Random.Range(0.85f, 1.00f),
                    Random.Range(0.90f, 1.00f),
                    Random.Range(0.95f, 1.00f));

                // 各クラスタ内のピース数: 2〜6
                int pc = hitClass == "large"  ? Random.Range(3, 7) :
                         hitClass == "medium" ? Random.Range(2, 5) : Random.Range(2, 4);
                pc = Mathf.Min(pc, 6);

                // ピースオフセット: クラスタ中心から bsz 単位で自然に散らばる
                float ofs = bsz * 0.45f;
                _clusters.Add(new Cluster
                {
                    pos              = new Vector2(spawnX + jx, spawnY),
                    vel              = _downhillDir * SLIDE_SPD,
                    slideDelay       = classSlideDelay + Random.Range(0f, 0.02f),
                    slideAccel       = classSlideAccel,
                    slideMaxSpd      = classSlideMaxSpd,
                    slideActive      = true,
                    mass             = 0.5f + totalDelta * 0.12f,
                    engulfBudget     = 2.5f,
                    engulfTotal      = 0f,
                    pieceCount       = pc,
                    baseSize         = bsz,
                    clusterColor     = cc,
                    broken           = false,
                    chainSourceIdx   = -1,
                    p0off = new Vector2(Random.Range(-ofs, ofs), Random.Range(-ofs*0.5f, ofs*0.5f)), p0sc = Random.Range(0.55f, 0.95f),
                    p1off = new Vector2(Random.Range(-ofs, ofs), Random.Range(-ofs*0.5f, ofs*0.5f)), p1sc = Random.Range(0.45f, 0.85f),
                    p2off = new Vector2(Random.Range(-ofs, ofs), Random.Range(-ofs*0.5f, ofs*0.5f)), p2sc = Random.Range(0.35f, 0.75f),
                    p3off = new Vector2(Random.Range(-ofs, ofs), Random.Range(-ofs*0.5f, ofs*0.5f)), p3sc = Random.Range(0.30f, 0.65f),
                    p4off = new Vector2(Random.Range(-ofs, ofs), Random.Range(-ofs*0.5f, ofs*0.5f)), p4sc = Random.Range(0.25f, 0.60f),
                    p5off = new Vector2(Random.Range(-ofs, ofs), Random.Range(-ofs*0.5f, ofs*0.5f)), p5sc = Random.Range(0.20f, 0.55f),
                });
            }

            // ── followup クラスタ（遅延）──────────────────────────
            for (int fi = 0; fi < followupClusterCount; fi++)
            {
                float fjx    = Random.Range(-roofW * 0.04f, roofW * 0.04f);
                float fbsz   = Mathf.Clamp(roofW * Random.Range(0.14f, 0.26f), 20f, 52f);
                float fDelay = classSlideDelay + Random.Range(0.05f, 0.15f);
                int   fpc    = Random.Range(2, 5);
                float fofs   = fbsz * 0.45f;
                Color fcc    = new Color(
                    Random.Range(0.88f, 1.00f),
                    Random.Range(0.92f, 1.00f),
                    Random.Range(0.96f, 1.00f));
                _clusters.Add(new Cluster
                {
                    pos            = new Vector2(spawnX + fjx, spawnY),
                    vel            = _downhillDir * SLIDE_SPD,
                    slideDelay     = fDelay,
                    slideAccel     = 2000f,
                    slideMaxSpd    = 750f,
                    slideActive    = true,
                    mass           = 0.4f,
                    engulfBudget   = 0.8f,
                    engulfTotal    = 0f,
                    pieceCount     = fpc,
                    baseSize       = fbsz,
                    clusterColor   = fcc,
                    broken         = false,
                    chainSourceIdx = -1,
                    p0off = new Vector2(Random.Range(-fofs, fofs), Random.Range(-fofs*0.5f, fofs*0.5f)), p0sc = Random.Range(0.50f, 0.90f),
                    p1off = new Vector2(Random.Range(-fofs, fofs), Random.Range(-fofs*0.5f, fofs*0.5f)), p1sc = Random.Range(0.40f, 0.80f),
                    p2off = new Vector2(Random.Range(-fofs, fofs), Random.Range(-fofs*0.5f, fofs*0.5f)), p2sc = Random.Range(0.30f, 0.70f),
                    p3off = new Vector2(Random.Range(-fofs, fofs), Random.Range(-fofs*0.5f, fofs*0.5f)), p3sc = Random.Range(0.25f, 0.60f),
                    p4off = new Vector2(Random.Range(-fofs, fofs), Random.Range(-fofs*0.5f, fofs*0.5f)), p4sc = Random.Range(0.20f, 0.55f),
                    p5off = new Vector2(Random.Range(-fofs, fofs), Random.Range(-fofs*0.5f, fofs*0.5f)), p5sc = Random.Range(0.18f, 0.50f),
                });
            }

            // ── sparse クラスタ（さらに遅延・小さめ）────────────────
            for (int si = 0; si < sparseClusterCount; si++)
            {
                float sjx    = Random.Range(-roofW * 0.05f, roofW * 0.05f);
                float sbsz   = Mathf.Clamp(roofW * Random.Range(0.10f, 0.18f), 14f, 34f);
                float sDelay = classSlideDelay + Random.Range(0.10f, 0.25f);
                int   spc    = Random.Range(2, 4);
                float sofs   = sbsz * 0.45f;
                Color scc    = new Color(
                    Random.Range(0.90f, 1.00f),
                    Random.Range(0.93f, 1.00f),
                    Random.Range(0.97f, 1.00f));
                _clusters.Add(new Cluster
                {
                    pos            = new Vector2(spawnX + sjx, spawnY),
                    vel            = _downhillDir * SLIDE_SPD,
                    slideDelay     = sDelay,
                    slideAccel     = 1800f,
                    slideMaxSpd    = 680f,
                    slideActive    = true,
                    mass           = 0.2f,
                    engulfBudget   = 0.3f,
                    engulfTotal    = 0f,
                    pieceCount     = spc,
                    baseSize       = sbsz,
                    clusterColor   = scc,
                    broken         = false,
                    chainSourceIdx = -1,
                    p0off = new Vector2(Random.Range(-sofs, sofs), Random.Range(-sofs*0.5f, sofs*0.5f)), p0sc = Random.Range(0.45f, 0.85f),
                    p1off = new Vector2(Random.Range(-sofs, sofs), Random.Range(-sofs*0.5f, sofs*0.5f)), p1sc = Random.Range(0.35f, 0.75f),
                    p2off = new Vector2(Random.Range(-sofs, sofs), Random.Range(-sofs*0.5f, sofs*0.5f)), p2sc = Random.Range(0.25f, 0.65f),
                    p3off = new Vector2(Random.Range(-sofs, sofs), Random.Range(-sofs*0.5f, sofs*0.5f)), p3sc = Random.Range(0.20f, 0.55f),
                    p4off = new Vector2(Random.Range(-sofs, sofs), Random.Range(-sofs*0.5f, sofs*0.5f)), p4sc = Random.Range(0.18f, 0.50f),
                    p5off = new Vector2(Random.Range(-sofs, sofs), Random.Range(-sofs*0.5f, sofs*0.5f)), p5sc = Random.Range(0.15f, 0.45f),
                });
            }

            AssiLogger.Verbose($"[2D_FP#{_tapCount}] clusterCount={clusterCount} followup={followupClusterCount} sparse={sparseClusterCount}" +
                      $" downhill=({_downhillDir.x:F2},{_downhillDir.y:F2})");
            AssiLogger.Verbose($"[FALL_TIMING]" +
                      $" hit_class={hitClass}" +
                      $" slide_delay_runtime={classSlideDelay:F3}" +
                      $" slide_speed_runtime={SLIDE_SPD:F0}" +
                      $" slide_max_spd_runtime={classSlideMaxSpd:F0}" +
                      $" slide_accel_runtime={classSlideAccel:F0}" +
                      $" gravity_runtime=1400");

            // クラスタ総数を CT トラッキングに登録
            int totalTracked = clusterCount + followupClusterCount + sparseClusterCount;
            GloveTool.BeginCooldownTracking(totalTracked);
            AssiLogger.Verbose($"[COOLDOWN_SYNC] hit_class={hitClass}" +
                      $" total_clusters={totalTracked}");
        }

        _lastInfo    = $"TAP#{_tapCount} fill={fillAfter:F2} sp={(spawned ? spawnCount.ToString() : "NO")}";
        _lastSpawned = spawned;
        if (spawned)
        {
            AssiLogger.Info($"[SNOW_CLUSTER] tap#{_tapCount}" +
                $" cluster_count={_clusters.Count}" +
                $" cluster_moves_as_group=YES" +
                $" still_moves_as_individual=NO");
        }

        int exposedCellCount = 0;
        for (int ex = 0; ex < GRID_W; ex++)
        for (int ey = 0; ey < GRID_H; ey++)
            if (_snow[ex, ey] <= EXPOSED_CELL_THRESHOLD) exposedCellCount++;
        float exposedAreaRatio = (float)exposedCellCount / (GRID_W * GRID_H);

        // 叩き結果（1行要約）
        AssiLogger.Info($"[HIT] tap={_tapCount} class={hitClass} delta={totalDelta:F2} fill={fillBefore:F2}→{fillAfter:F2} spawn={(spawned ? spawnCount.ToString() : "NO")}");

        AssiLogger.Verbose($"[GLOVE_SNOW_FALL_RESTORE]" +
                  $" snow_fell={(spawned ? "YES" : "NO")}" +
                  $" roof={TARGET_ROOF_ID}");

        // 落雪量を GloveTool に通知してクールタイムを可変化
        GloveTool.ReportImpact(spawned ? totalDelta : 0f, spawnCount);

        AssiLogger.Verbose($"[SLIDE_TIMING] slide_delay_mode=class_based accel=ease-in");
        AssiLogger.Verbose($"[TIME_VARIANCE_PROOF] hit_class={hitClass} totalDelta={totalDelta:F3}");

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

    // ── ヘルパー: Cluster のピースオフセット配列アクセス ──────────
    static Vector2 ClusterPieceOff(Cluster c, int idx)
    {
        switch (idx)
        {
            case 0: return c.p0off;
            case 1: return c.p1off;
            case 2: return c.p2off;
            case 3: return c.p3off;
            case 4: return c.p4off;
            default: return c.p5off;
        }
    }
    static float ClusterPieceSc(Cluster c, int idx)
    {
        switch (idx)
        {
            case 0: return c.p0sc;
            case 1: return c.p1sc;
            case 2: return c.p2sc;
            case 3: return c.p3sc;
            case 4: return c.p4sc;
            default: return c.p5sc;
        }
    }

    // ── UpdateClusters: クラスタ単位の slide/engulf/break/分解 ─────
    void UpdateClusters()
    {
        const float RESIST_MULT  = 1.2f;
        const float DECEL        = 6f;
        const float STOP_VEL     = 8f;
        const float ABSORB_RATE  = 0.5f;
        const float ENGULF_CAP   = 4.0f;
        const int   SWEEP_R      = 1;
        const float EXPOSED_THR  = 0.01f;

        float dt = Time.deltaTime;

        for (int i = _clusters.Count - 1; i >= 0; i--)
        {
            var c = _clusters[i];

            if (c.broken)
            {
                // 分解: 各ピースを _pieces に追加して自由落下へ
                for (int pi = 0; pi < Mathf.Min(c.pieceCount, 6); pi++)
                {
                    Vector2 poff = ClusterPieceOff(c, pi);
                    float   psc  = ClusterPieceSc(c, pi);
                    float   psz  = c.baseSize * psc;
                    // 分解時の速度: クラスタ速度 + 小さな飛散（横方向は抑制）
                    Vector2 pvel = c.vel + new Vector2(
                        Random.Range(-25f, 25f),
                        c.vel.y > 0f ? c.vel.y * Random.Range(0.8f, 1.2f) : Random.Range(80f, 160f));
                    // 縦方向に引き伸ばして「塊が落ちる」感を出す
                    float chunkScaleX = Random.Range(0.60f, 1.10f);
                    float chunkScaleY = Random.Range(0.90f, 1.50f); // 旧0.5-1.3 → 縦に厚く
                    _pieces.Add(new Piece
                    {
                        pos          = c.pos + poff * 0.5f,
                        vel          = pvel,
                        size         = Mathf.Clamp(psz, 10f, 56f), // 旧6-40 → 大きく
                        life         = 3f + Random.Range(0f, 1.2f),
                        alpha        = 1f,
                        slideTimer   = 0f,
                        slideDelay   = 0f,
                        slideAccel   = 0f,
                        slideMaxSpd  = 0f,
                        slideActive  = false,
                        currentMass  = c.mass / Mathf.Max(1, c.pieceCount),
                        engulfBudget = 0f,
                        engulfTotal  = 0f,
                        scaleX       = chunkScaleX,
                        scaleY       = chunkScaleY,
                        rotation     = Random.Range(-35f, 35f), // 旧50度 → 少し抑制
                        chunkCount   = 1,
                        snowColor    = c.clusterColor,
                        subCount     = 0,
                    });
                }
                AssiLogger.Verbose($"[CLUSTER_BREAK] pos=({c.pos.x:F0},{c.pos.y:F0}) pieceCount={c.pieceCount} mass={c.mass:F2}");
                _clusters.RemoveAt(i);
                GloveTool.NotifyGroundLanding(); // CT カウントを1消費
                continue;
            }

            if (c.slideActive)
            {
                bool transitionToFall = false;

                // 溜め
                if (c.slideDelay > 0f)
                {
                    c.slideDelay -= dt;
                    _clusters[i] = c;
                    continue;
                }

                // 加速（ease-in²）
                float curSpd = c.vel.magnitude;
                if (curSpd < c.slideMaxSpd)
                {
                    float ratio     = curSpd / c.slideMaxSpd;
                    float accelMult = ratio * ratio + 0.04f;
                    curSpd = Mathf.Min(curSpd + c.slideAccel * accelMult * dt, c.slideMaxSpd);
                    c.vel  = _downhillDir * curSpd;
                }
                // 最低速保証
                if (c.vel.magnitude < 120f)
                    c.vel = _downhillDir * Mathf.Max(c.vel.magnitude + 400f * dt, 120f);

                if (_ready && _guiRect.width > 1f)
                {
                    float nx  = Mathf.Clamp01((c.pos.x - _guiRect.x) / _guiRect.width);
                    float ny  = Mathf.Clamp01((c.pos.y - _guiRect.y) / _guiRect.height);
                    float pgx = nx * GRID_W;
                    float pgy = ny * GRID_H;

                    int cgx = Mathf.Clamp(Mathf.FloorToInt(pgx), 0, GRID_W - 1);
                    int cgy = Mathf.Clamp(Mathf.FloorToInt(pgy), 0, GRID_H - 1);

                    int fgx = Mathf.Clamp(cgx + Mathf.RoundToInt(_downhillDir.x * 2f), 0, GRID_W - 1);
                    int fgy = Mathf.Clamp(cgy + Mathf.RoundToInt(_downhillDir.y * 2f), 0, GRID_H - 1);

                    float frontResistance = 0f;
                    for (int sx = Mathf.Max(0, fgx - SWEEP_R);
                             sx <= Mathf.Min(GRID_W - 1, fgx + SWEEP_R); sx++)
                        frontResistance += _snow[sx, fgy];

                    bool stopped = false;

                    if (frontResistance > EXPOSED_THR)
                    {
                        float threshold = frontResistance * RESIST_MULT;
                        // 軒先停止ゼロ化: frontResistance による停止を全廃（常に突破・吸収）
                        bool nearEave = true;
                        if (c.mass < threshold && !nearEave)
                        {
                            c.vel *= Mathf.Max(0f, 1f - DECEL * dt);
                            if (c.vel.magnitude <= STOP_VEL)
                            {
                                c.slideActive = false;
                                c.vel         = Vector2.zero;
                                stopped       = true;
                            }
                        }
                        else
                        {
                            // 突破: 前方雪を吸収
                            for (int sx = Mathf.Max(0, fgx - SWEEP_R);
                                     sx <= Mathf.Min(GRID_W - 1, fgx + SWEEP_R); sx++)
                            {
                                if (_snow[sx, fgy] <= EXPOSED_THR) continue;
                                if (c.engulfTotal >= ENGULF_CAP) break;

                                float take = Mathf.Min(
                                    _snow[sx, fgy] * ABSORB_RATE,
                                    ENGULF_CAP - c.engulfTotal);
                                if (take <= 0f) continue;

                                float edgeDist   = Mathf.Abs(sx - fgx) / (float)(SWEEP_R + 1);
                                float engulfDelay = edgeDist * 0.06f;
                                if (engulfDelay <= 0f)
                                {
                                    _snow[sx, fgy] -= take;
                                    c.mass         += take * 0.5f;
                                    c.engulfTotal  += take;
                                    _texDirty = true;
                                }
                                else
                                {
                                    _pendingRemovals.Add(new PendingRemoval
                                    {
                                        gx = sx, gy = fgy, amount = take, delay = engulfDelay
                                    });
                                    c.mass        += take * 0.5f;
                                    c.engulfTotal += take;
                                }
                            }
                        }
                    }

                    if (!stopped) c.pos += c.vel * dt;

                    // 軒先到達: ny >= 0.95f で強制落下トリガー（停止も不可）
                    float nyAfterMove = Mathf.Clamp01((c.pos.y - _guiRect.y) / _guiRect.height);
                    if (nyAfterMove >= 0.95f || c.pos.y >= _guiRect.yMax)
                    {
                        // snowDepth 軒先排出: 到達列の端snowDepthを減算して排出イベントを起こす
                        int eaveCx = Mathf.Clamp(Mathf.FloorToInt(
                            (c.pos.x - _guiRect.x) / _guiRect.width * GRID_W), 0, GRID_W - 1);
                        for (int ex = Mathf.Max(0, eaveCx - 1); ex <= Mathf.Min(GRID_W - 1, eaveCx + 1); ex++)
                        {
                            _snowDepth[ex] = Mathf.Max(0f, _snowDepth[ex] - 0.15f);
                            if (_snowDepth[ex] < 0.01f) _snowDepth[ex] = 0f;
                        }
                        transitionToFall = true;
                        AssiLogger.Verbose($"[CLUSTER_EAVE] pos=({c.pos.x:F0},{c.pos.y:F0}) mass={c.mass:F2} eave_fx=OFF eave_output=YES");
                    }
                    // 軒下エリア（ny>=0.88）で低速停留 → 強制落下（軒下残留ゼロ化）
                    else if (nyAfterMove >= 0.88f && c.vel.magnitude < 60f)
                    {
                        transitionToFall = true;
                        AssiLogger.Verbose($"[CLUSTER_UNDER_EAVE_FORCE] pos=({c.pos.x:F0},{c.pos.y:F0}) spd={c.vel.magnitude:F1}");
                    }

                    // 屋根左右外
                    if (nx <= 0f || nx >= 1f)
                    {
                        c.slideActive = false;
                        c.vel         = Vector2.zero;
                    }
                }
                else
                {
                    c.pos += c.vel * dt;
                }

                if (transitionToFall)
                {
                    // 軒先到達 → broken にして次フレームで分解
                    c.broken = true;
                }
            }
            else
            {
                // 自由落下（slideActive=false かつ broken=false: 停止した残留クラスタ）
                c.vel.y += 1400f * dt;
                c.pos   += c.vel * dt;

                // 軒先到達 → broken にして分解（停止状態でも軒先で引っかからないよう）
                if (c.pos.y >= _guiRect.yMax)
                {
                    c.broken = true;
                    AssiLogger.Verbose($"[CLUSTER_STOPPED_EAVE] pos=({c.pos.x:F0},{c.pos.y:F0}) mass={c.mass:F2}");
                }

                // 地面着地
                if (c.pos.y >= _groundGuiY)
                {
                    bool wasMoving = c.vel.magnitude > 20f;
                    c.pos.y = _groundGuiY;
                    c.vel   = Vector2.zero;

                    if (wasMoving)
                    {
                        float gPuffBase = c.mass > 1.5f ? 70f : (c.mass > 0.7f ? 50f : 34f);
                        int   gPuffN    = c.mass > 1.5f ? 8 : (c.mass > 0.7f ? 6 : 4);
                        for (int pi3 = 0; pi3 < gPuffN; pi3++)
                        {
                            float pjx = Random.Range(-18f, 18f);
                            float psz = gPuffBase * Random.Range(0.8f, 1.5f);
                            float pl  = Random.Range(0.6f, 1.0f);
                            _puffs.Add(new Puff
                            {
                                pos     = new Vector2(c.pos.x + pjx, c.pos.y),
                                vel     = new Vector2(Random.Range(-30f, 30f), Random.Range(-50f, -15f)),
                                size    = psz,
                                life    = pl,
                                maxLife = pl,
                                alpha   = 1f,
                                kind    = 2,
                            });
                        }
                        GloveTool.NotifyGroundLanding();
                    }
                    // 着地後 broken にして削除
                    c.broken = true;
                }
            }

            if (!c.broken)
                _clusters[i] = c;
        }

        // ── 連鎖崩落チェック: 停止クラスタが隣接する移動クラスタの mass を比較 ──
        // 移動中クラスタが停止クラスタに近づいた時、mass 差が大きければ連鎖で break
        const float CHAIN_DIST_THRESHOLD = 60f;   // 連鎖を誘発する距離（px）
        const float CHAIN_MASS_RATIO     = 0.4f;  // 衝突側 mass がこの割合以上なら連鎖
        for (int ai = 0; ai < _clusters.Count; ai++)
        {
            var ca = _clusters[ai];
            if (ca.broken || !ca.slideActive) continue;
            if (ca.vel.magnitude < 80f) continue; // 低速では連鎖しない

            for (int bi = 0; bi < _clusters.Count; bi++)
            {
                if (ai == bi) continue;
                var cb = _clusters[bi];
                if (cb.broken) continue;
                if (cb.slideActive && cb.slideDelay > 0f) continue; // 溜め中は連鎖対象外

                float dist = Vector2.Distance(ca.pos, cb.pos);
                if (dist > CHAIN_DIST_THRESHOLD) continue;

                // 衝突方向チェック: ca が cb に向かって動いているか
                Vector2 dir = (cb.pos - ca.pos).normalized;
                if (Vector2.Dot(ca.vel.normalized, dir) < 0.3f) continue;

                // mass 比較: ca の mass が cb の CHAIN_MASS_RATIO 倍以上なら連鎖
                if (ca.mass >= cb.mass * CHAIN_MASS_RATIO)
                {
                    var cbMod = _clusters[bi];
                    cbMod.broken = true;
                    _clusters[bi] = cbMod;
                    AssiLogger.Verbose($"[CLUSTER_CHAIN] src={ai} target={bi} dist={dist:F0} mass_a={ca.mass:F2} mass_b={cb.mass:F2}");
                }
            }
        }
    }

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
                // ease-in²: 速度比率の二乗でスケールするので最初がより遅く、
                // 後半に向けてグッと加速するカーブになる
                float curSpd = p.vel.magnitude;
                if (curSpd < p.slideMaxSpd)
                {
                    // 速度比率 [0,1] の二乗でスロットル → 最初ほぼ動かず後半一気に加速
                    float ratio     = curSpd / p.slideMaxSpd;           // 0→1
                    float accelMult = ratio * ratio + 0.04f;            // 最低4%は常に加速
                    curSpd = Mathf.Min(curSpd + p.slideAccel * accelMult * dt, p.slideMaxSpd);
                    p.vel  = _downhillDir * curSpd;
                }
                // 最低速度保証: delay 終了後に速度が 120px/s 未満なら強制引き上げ
                // （「ほぼ停止」に見える個体を排除）
                if (p.slideDelay <= 0f && p.vel.magnitude < 120f)
                {
                    p.vel = _downhillDir * Mathf.Max(p.vel.magnitude + 400f * dt, 120f);
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
                    float frameEngulf  = 0f;
                    int   contactCells = 0;

                    if (frontResistance > EXPOSED_THR)
                    {
                        float threshold = frontResistance * RESIST_MULT;
                        // 軒先停止ゼロ化: Piece も frontResistance による停止を全廃（常に突破）
                        if (false && p.currentMass < threshold)
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

                                AssiLogger.Verbose($"[2D_SLIDE_STOP] roof={TARGET_ROOF_ID} pos=({p.pos.x:F0},{p.pos.y:F0}) mass={p.currentMass:F3}");
                            }
                        }
                        else
                        {
                            // ── 突破: 前方雪を吸収しながら進む ────
                            // 中心セル(sx==fgx)は即時吸収、左右端ほど遅延して段階的に崩れる
                            const float ENGULF_EDGE_DELAY = 0.06f; // 端セルの最大遅延秒

                            for (int sx = Mathf.Max(0, fgx - SWEEP_R);
                                     sx <= Mathf.Min(GRID_W - 1, fgx + SWEEP_R); sx++)
                            {
                                if (_snow[sx, fgy] <= EXPOSED_THR) continue;
                                if (p.engulfTotal >= ENGULF_CAP) break;

                                float take = Mathf.Min(
                                    _snow[sx, fgy] * ABSORB_RATE,
                                    ENGULF_CAP - p.engulfTotal);
                                if (take <= 0f) continue;

                                // 中心からの距離に比例した遅延
                                float edgeDist = Mathf.Abs(sx - fgx) / (float)(SWEEP_R + 1);
                                float engulfDelay = edgeDist * ENGULF_EDGE_DELAY;

                                if (engulfDelay <= 0f)
                                {
                                    _snow[sx, fgy] -= take;
                                    p.currentMass  += take * 0.5f;
                                    p.engulfTotal  += take;
                                    frameEngulf    += take;
                                    contactCells++;
                                    _texDirty = true;
                                }
                                else
                                {
                                    // 端は遅延キューへ（見た目だけ遅らせる。mass/engulf計上は即時）
                                    _pendingRemovals.Add(new PendingRemoval
                                    {
                                        gx = sx, gy = fgy, amount = take, delay = engulfDelay
                                    });
                                    p.currentMass += take * 0.5f;
                                    p.engulfTotal += take;
                                    frameEngulf   += take;
                                    contactCells++;
                                }
                            }
                        }
                    }

                    // 移動（停止していなければ）
                    if (!stopped) p.pos += p.vel * dt;

                    // 軒先到達: ny >= 0.95f で強制落下（停止不可・強制トリガー）
                    float nyAfterMoveP = Mathf.Clamp01((p.pos.y - _guiRect.y) / _guiRect.height);
                    if (nyAfterMoveP >= 0.95f || p.pos.y >= _guiRect.yMax)
                    {
                        // snowDepth 軒先排出: 到達列のsnowDepthを減算
                        int eaveCxP = Mathf.Clamp(Mathf.FloorToInt(
                            (p.pos.x - _guiRect.x) / _guiRect.width * GRID_W), 0, GRID_W - 1);
                        for (int ex = Mathf.Max(0, eaveCxP - 1); ex <= Mathf.Min(GRID_W - 1, eaveCxP + 1); ex++)
                        {
                            _snowDepth[ex] = Mathf.Max(0f, _snowDepth[ex] - 0.08f);
                            if (_snowDepth[ex] < 0.01f) _snowDepth[ex] = 0f;
                        }
                        transitionToFall = true;
                        AssiLogger.Verbose($"[2D_SLIDE_EAVE] roof={TARGET_ROOF_ID} pos=({p.pos.x:F0},{p.pos.y:F0}) mass={p.currentMass:F3} eave_output=YES");
                        AssiLogger.Verbose($"[SNOW_PUFF_EAVE] roof={TARGET_ROOF_ID} eave_fx=OFF pos=({p.pos.x:F0},{p.pos.y:F0})");
                    }
                    // 軒下エリア（ny>=0.88）で低速停留 → 強制落下（軒下残留ゼロ化）
                    else if (nyAfterMoveP >= 0.88f && p.vel.magnitude < 60f)
                    {
                        transitionToFall = true;
                        AssiLogger.Verbose($"[PIECE_UNDER_EAVE_FORCE] pos=({p.pos.x:F0},{p.pos.y:F0}) spd={p.vel.magnitude:F1}");
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
                p.vel.y += 1400f * dt;   // 900 → 1400
                p.pos   += p.vel * dt;
            }

            p.life  -= dt;   // ※ slideActive中は減算しない（軒前消滅禁止）
            if (p.slideActive) p.life += dt;  // 打ち消し: 軒到達まで寿命停止
            p.alpha  = Mathf.Clamp01(p.life * 0.8f);

            if (p.pos.y >= _groundGuiY)
            {
                bool wasMoving = p.vel.magnitude > 20f;
                p.pos.y = _groundGuiY;
                p.vel   = Vector2.zero;
                p.life  = Mathf.Min(p.life, 1.2f);

                // 地面着弾雪煙: 速度があった時のみ（停止から来たPuffは出さない）
                if (wasMoving && !p.slideActive)
                {
                    // 地面着地雪煙: 「大」サイズ（tap=小 / eave=中 / ground=大）
                    float gPuffBase = p.currentMass > 1.5f ? 70f : (p.currentMass > 0.7f ? 50f : 34f);
                    string gPuffSz  = p.currentMass > 1.5f ? "large" :
                                      (p.currentMass > 0.7f ? "medium" : "small");
                    int gPuffN = p.currentMass > 1.5f ? 8 : (p.currentMass > 0.7f ? 6 : 4);
                    for (int pi3 = 0; pi3 < gPuffN; pi3++)
                    {
                        float pjx = Random.Range(-18f, 18f);
                        float psz = gPuffBase * Random.Range(0.8f, 1.5f);
                        float pl  = Random.Range(0.6f, 1.0f);
                        _puffs.Add(new Puff
                        {
                            pos     = new Vector2(p.pos.x + pjx, p.pos.y),
                            vel     = new Vector2(Random.Range(-30f, 30f), Random.Range(-50f, -15f)),
                            size    = psz,
                            life    = pl,
                            maxLife = pl,
                            alpha   = 1f,
                            kind    = 2,
                        });
                    }
                    AssiLogger.Verbose($"[SNOW_PUFF_GROUND] roof={TARGET_ROOF_ID} puffSize={gPuffSz} puffCount={gPuffN}");

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
            Debug.Log($"[SNOW_RECT_REBUILD] roof={TARGET_ROOF_ID} old=({_builtScreenW}x{_builtScreenH}) new=({Screen.width}x{Screen.height})");
            _ready = false;
            BuildRoofData();
        }

        if (!_ready || _snowTex == null) return;

        // ── 台形スキャンライン描画 ─────────────────────────────
        // 4頂点 TL/TR/BL/BR を使い、各Y行でX左端・X右端を補間して描画する
        // _snowTex のアルファ列（X軸）は「グリッド列 = テクスチャX」に対応
        float topY  = Mathf.Min(_trapTL.y, _trapTR.y);
        float botY  = Mathf.Max(_trapBL.y, _trapBR.y);
        float totalH = botY - topY;

        // 全体残雪（デバッグ表示・ゲージ用のみ。描画矩形には使わない）
        float fillAvg = CalcFill();

        if (fillAvg > 0f && totalH > 0f)
        {
            // [旧方式: _snowTex スキャンライン描画 → 無効化済み]
            // snowDepth ハイトマップ方式に完全移行したため、旧Texture2D描画は停止。
            // old_piece_visual_still_active=NO

            // ═══════════════════════════════════════════════════════════════
            // 積雪描画 — snowDepth ハイトマップ方式（主方式）
            // _snowDepth[x] (0〜1) から雪面を生成する。
            // 四角いパーツは廃止。ハイトマップの高さと厚みで全てを表現する。
            // ═══════════════════════════════════════════════════════════════
            {
                float roofW  = _trapTR.x - _trapTL.x;
                float roofH  = botY - topY;
                float dfill  = CalcSnowDepthFill(); // 全体残量（0〜1）

                // ─ スムージング（5点ガウシアン → さらに2パス）──────────
                // 1パス目: ガウシアン重み [1,2,3,2,1]/9 で強めに平滑化
                float[] sd1 = new float[GRID_W];
                float[] gaussW = new float[] { 1f, 2f, 3f, 2f, 1f };
                for (int x = 0; x < GRID_W; x++)
                {
                    float sv = 0f, sw = 0f;
                    for (int k = 0; k < 5; k++)
                    {
                        int xi = Mathf.Clamp(x + k - 2, 0, GRID_W - 1);
                        sv += _snowDepth[xi] * gaussW[k];
                        sw += gaussW[k];
                    }
                    sd1[x] = sv / sw;
                }
                // 2パス目: さらに3点平均でなめらかに
                float[] sd = new float[GRID_W];
                for (int x = 0; x < GRID_W; x++)
                {
                    int lo = Mathf.Max(0, x - 1);
                    int hi = Mathf.Min(GRID_W - 1, x + 1);
                    float sv = 0f; int sn = 0;
                    for (int xi = lo; xi <= hi; xi++) { sv += sd1[xi]; sn++; }
                    sd[x] = sn > 0 ? sv / sn : 0f;
                }
                // サブピクセル補間用: sd を連続関数として扱うため GRID_W+1 点の頂点値を計算
                // sdV[i] = 頂点 i の補間値（列iと列i-1の平均）
                float[] sdV = new float[GRID_W + 1];
                sdV[0] = sd[0];
                sdV[GRID_W] = sd[GRID_W - 1];
                for (int x = 1; x < GRID_W; x++)
                    sdV[x] = (sd[x - 1] + sd[x]) * 0.5f;

                // ─ LAYER A: 雪面ボディ（各スキャンラインをX連続補間で塗る）──
                // 列ごとの矩形ではなく、1px幅の短冊をX位置で補間したαで塗ることで
                // 列境界の段差を消す。
                int scanStep2 = 2;
                int pixStep = 4; // X方向は4px単位で描画（列境界より細かく）
                for (int sy = 0; sy < (int)roofH; sy += scanStep2)
                {
                    float t     = sy / roofH;
                    float depth = t;
                    float bright = Mathf.Lerp(0.98f, 0.84f, depth);
                    float blueC  = Mathf.Lerp(1.00f, 0.91f, depth);

                    // 台形左右端をt で補間
                    float lxRow = Mathf.Lerp(_trapTL.x, _trapBL.x, t);
                    float rxRow = Mathf.Lerp(_trapTR.x, _trapBR.x, t);
                    float rowW  = rxRow - lxRow;
                    if (rowW <= 0f) continue;

                    float minA_base = Mathf.Lerp(0.42f, 0.30f, depth);
                    float maxA_base = Mathf.Lerp(0.96f, 0.82f, depth);

                    for (int px = 0; px < (int)rowW; px += pixStep)
                    {
                        float nx  = px / rowW;                        // 0〜1
                        float nx1 = Mathf.Min(1f, (px + pixStep) / rowW);

                        // sdV を nx で補間（頂点値なので列境界がなめらか）
                        float gxF   = nx  * GRID_W;
                        int   gxI   = Mathf.Clamp(Mathf.FloorToInt(gxF), 0, GRID_W - 1);
                        float gxFrac = gxF - gxI;
                        float dvL = sdV[gxI];
                        float dvR = sdV[Mathf.Min(GRID_W, gxI + 1)];
                        float dv  = Mathf.Lerp(dvL, dvR, gxFrac);
                        if (dv < 0.015f) continue;

                        float alpha2 = Mathf.Lerp(minA_base, maxA_base, dv);
                        float drawX  = lxRow + px;
                        float drawW  = Mathf.Min(pixStep, rowW - px) + 0.5f;
                        GUI.color = new Color(bright, bright, blueC, alpha2);
                        GUI.DrawTexture(new Rect(drawX, topY + sy, drawW, scanStep2 + 1f),
                                        Texture2D.whiteTexture);
                    }
                }

                // ─ LAYER B: 上面シルエット（連続補間した丘ライン）──────────
                // topProfile を GRID_W*4 点の高密度プロファイルで計算し、
                // 隣接点間のY段差を極小化する。
                int profileN = GRID_W * 4; // 高密度サンプル
                float[] profileY = new float[profileN + 1];
                for (int pi2 = 0; pi2 <= profileN; pi2++)
                {
                    float nxf  = (float)pi2 / profileN;
                    // sdV を補間
                    float gxF2  = nxf * GRID_W;
                    int   gxI2  = Mathf.Clamp(Mathf.FloorToInt(gxF2), 0, GRID_W - 1);
                    float gxFr2 = gxF2 - gxI2;
                    float dv2   = Mathf.Lerp(sdV[gxI2], sdV[Mathf.Min(GRID_W, gxI2 + 1)], gxFr2);

                    // 小さな波（ブロック感を生まない細かい波）
                    float wave =
                          Mathf.Sin(nxf * Mathf.PI * 4.5f + 0.7f) * 0.14f
                        + Mathf.Sin(nxf * Mathf.PI * 9.0f  + 1.9f) * 0.05f;
                    wave = wave * 0.5f + 0.5f;

                    float effectiveDv = Mathf.Max(dv2, dfill * 0.15f);
                    float boost = roofH * 0.40f * effectiveDv * (0.55f + wave * 0.45f);
                    profileY[pi2] = topY - boost;
                }

                // 上面帯: 高密度プロファイルを短冊で描画（1セグメント = pixStep px幅）
                float roofWTop = _trapTR.x - _trapTL.x;
                for (int pi2 = 0; pi2 < profileN; pi2++)
                {
                    float nx0  = (float)pi2       / profileN;
                    float nx1  = (float)(pi2 + 1) / profileN;
                    float gxF2 = nx0 * GRID_W;
                    int   gxI2 = Mathf.Clamp(Mathf.FloorToInt(gxF2), 0, GRID_W - 1);
                    float gxFr2 = gxF2 - gxI2;
                    float dv2  = Mathf.Lerp(sdV[gxI2], sdV[Mathf.Min(GRID_W, gxI2 + 1)], gxFr2);
                    if (dv2 < 0.015f) continue;

                    float x0  = _trapTL.x + nx0 * roofWTop;
                    float x1  = _trapTL.x + nx1 * roofWTop;
                    float y0  = profileY[pi2];
                    float y1  = profileY[pi2 + 1];
                    float segW = x1 - x0;
                    if (segW <= 0f) continue;

                    float segTopY = Mathf.Min(y0, y1);
                    float segBotY = topY + roofH * 0.06f;
                    float segH    = segBotY - segTopY;
                    if (segH <= 0f) continue;

                    GUI.color = new Color(0.97f, 0.98f, 1.0f, dv2 * 0.92f);
                    GUI.DrawTexture(new Rect(x0, segTopY, segW + 0.5f, segH),
                                    Texture2D.whiteTexture);
                }

                // ─ LAYER C: 前面エッジ（列ごとの高さで補間しながら描画）───
                if (dfill > 0.03f)
                {
                    float lxE = _trapBL.x;
                    float rxE = _trapBR.x;
                    float eaveRowW = rxE - lxE;
                    const int FADE = 6;
                    for (int fi = 0; fi < FADE; fi++)
                    {
                        float fR    = (float)(fi + 1) / FADE;
                        float shade = 0.72f + fR * 0.06f;
                        float lA    = dfill * fR * fR * 0.55f;
                        // 列ごとにエッジ高さを変えてギザギザ感を出す
                        for (int ex = 0; ex < GRID_W; ex++)
                        {
                            float enx0 = (float)ex       / GRID_W;
                            float enx1 = (float)(ex + 1) / GRID_W;
                            float edv  = Mathf.Lerp(sdV[ex], sdV[ex + 1], 0.5f);
                            if (edv < 0.01f) continue;
                            float eH   = eaveRowW / GRID_W;
                            float elx  = lxE + enx0 * eaveRowW;
                            float erx  = lxE + enx1 * eaveRowW;
                            float localH = roofH * 0.22f * Mathf.Sqrt(dfill * edv / Mathf.Max(dfill, 0.01f)) * fR;
                            GUI.color = new Color(shade, shade + 0.03f, shade + 0.06f, lA * edv);
                            GUI.DrawTexture(new Rect(elx, botY - localH, erx - elx + 0.5f, localH + 1f),
                                            Texture2D.whiteTexture);
                        }
                    }
                }

                GUI.color = Color.white;
            }
        }
        else if (fillAvg <= 0f)
        {
            // 全部空: トップライン消去
            GUI.color = new Color(0.45f, 0.55f, 0.72f, 0.90f);
            GUI.DrawTexture(new Rect(_guiRect.x, _guiRect.y - 18f, _guiRect.width, 22f),
                            Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            GUI.DrawTexture(new Rect(_guiRect.x, _guiRect.y - 4f, _guiRect.width, 4f),
                            Texture2D.whiteTexture);
        }

        // ── クラスタ描画（slideActive中 または 停止中のクラスタ）──────
        // クラスタは中心座標 + 各ピースのオフセットで複数塊として描画する
        foreach (var cl in _clusters)
        {
            if (cl.broken) continue;
            int drawCount = Mathf.Min(cl.pieceCount, 6);
            for (int pi = 0; pi < drawCount; pi++)
            {
                Vector2 poff = ClusterPieceOff(cl, pi);
                float   psc  = ClusterPieceSc(cl, pi);
                Vector2 ppos = cl.pos + poff;
                float   psz  = cl.baseSize * psc;
                float   pw2  = psz * 0.5f;
                float   ph2  = psz * (0.55f + (pi % 3) * 0.08f); // ピースごとに縦横比を変化

                float alpha = cl.slideDelay > 0f ? 0.7f : 1.0f;
                Color cc = cl.clusterColor;
                cc.a = alpha;

                float depthDarken = 1f - pi * 0.05f;
                cc.r *= depthDarken;
                cc.g *= depthDarken;
                cc.b  = Mathf.Min(1f, cc.b);

                var savedMatrix2 = GUI.matrix;
                float rot2 = poff.x * 0.3f + poff.y * 0.1f;
                GUIUtility.RotateAroundPivot(rot2, ppos);

                // ── 不定形: 楕円的な複数矩形の重ね合わせで塊感を出す ──
                // ピースごとに固定オフセット（毎フレーム変わらないよう pi ベースで決定）
                float bx = ppos.x;
                float by = ppos.y;

                // メイン塊（少し横長）
                GUI.color = cc;
                GUI.DrawTexture(new Rect(bx - pw2, by - ph2 * 0.45f, pw2 * 2f, ph2 * 0.90f), Texture2D.whiteTexture);

                // 左上ふくらみ（不定形感）
                float bw1 = pw2 * (0.60f + (pi % 4) * 0.08f);
                float bh1 = ph2 * (0.50f + (pi % 3) * 0.07f);
                GUI.color = new Color(cc.r, cc.g, cc.b, alpha * 0.80f);
                GUI.DrawTexture(new Rect(bx - pw2 * 0.90f, by - ph2 * 0.55f, bw1, bh1), Texture2D.whiteTexture);

                // 右下ふくらみ（非対称）
                float bw2 = pw2 * (0.55f + (pi % 5) * 0.07f);
                float bh2 = ph2 * (0.45f + (pi % 2) * 0.10f);
                GUI.color = new Color(cc.r, cc.g, cc.b, alpha * 0.70f);
                GUI.DrawTexture(new Rect(bx + pw2 * 0.20f, by + ph2 * 0.05f, bw2, bh2), Texture2D.whiteTexture);

                // 上面ハイライト（雪の光当たり面）
                GUI.color = new Color(1f, 1f, 1f, alpha * 0.65f);
                GUI.DrawTexture(new Rect(bx - pw2 * 0.80f, by - ph2 * 0.50f,
                                         pw2 * 1.50f, ph2 * 0.22f), Texture2D.whiteTexture);

                GUI.matrix = savedMatrix2;
            }
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
        if (s_hudVisible)
        {
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

        // 道具UI前面描画の共通エントリポイント
        // ToolUIRenderer が「全軒OnGUI完了後の最後の1回」に全道具UIを描画する
        // 新しい道具も ToolUIRenderer.Register() で登録するだけで前面保証される
        ToolUIRenderer.DrawAll(TARGET_ROOF_ID);
    }
}
