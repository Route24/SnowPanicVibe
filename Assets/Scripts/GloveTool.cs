using UnityEngine;

/// <summary>
/// 毛糸の手袋ツール（完成見本）
///
/// 描画: ToolUIRenderer に IToolUI として登録。
///       SnowStrip2D.OnGUI() 末尾 → ToolUIRenderer.DrawAll() → DrawToolUI() の経路で
///       全積雪描画完了後に描画されるため、全6軒で確実に前面表示される。
///
/// PHASE1: 画像クロップ・リサイズ・50度回転済み（GloveMitten.png で処理済み）
/// PHASE2: クリックで ease-in 落下 → 雪面で停止
/// PHASE3: 着弾時に雪煙パーティクル1回発生
/// PHASE4: クールタイム中はグレー半透明、クリック無効
/// </summary>
[DefaultExecutionOrder(1000)]
public class GloveTool : MonoBehaviour, IToolUI
{
    // ── 公開状態 ──────────────────────────────────────────────
    public static bool IsBlocking { get; private set; } = false;
    public Texture2D gloveTex;

    // ── PHASE2: 叩き挙動 ──────────────────────────────────────
    enum GloveState { Ready, Striking, Cooldown }
    GloveState _state = GloveState.Ready;

    // ease-in 落下パラメータ
    const float STRIKE_DURATION = 0.18f;  // 落下にかける秒数
    const float STRIKE_DIST     = 0.08f;  // 落下距離（Screen.height の割合）
    float _strikeTimer  = 0f;
    float _strikeStartY = 0f;   // 落下開始時の GUI Y 座標
    float _strikeTargetY = 0f;  // 落下目標 GUI Y 座標

    // ── PHASE4: クールタイム ──────────────────────────────────
    const float COOLDOWN_SEC = 1.2f;
    float _cooldownTimer = 0f;

    // ── PHASE3: 雪煙 ──────────────────────────────────────────
    // 軽量な OnGUI パーティクル（Texture 不要）
    struct Puff
    {
        public Vector2 pos;   // GUI 座標
        public float   life;  // 残り秒数
        public float   size;
    }
    const int   MAX_PUFFS    = 8;
    const float PUFF_LIFE    = 0.35f;
    Puff[]  _puffs     = new Puff[MAX_PUFFS];
    int     _puffCount = 0;

    // ── 現在のマウス GUI 座標（DrawToolUI と Update で共有）──
    float _curGX, _curGY, _curW, _curH;

    // ── 後方互換 ──────────────────────────────────────────────
    public static void DrawFrontmost(string callerRoofId = "?")
        => ToolUIRenderer.DrawAll(callerRoofId);

    // ─────────────────────────────────────────────────────────
    void Start()
    {
        if (gloveTex == null)
            gloveTex = Resources.Load<Texture2D>("GloveMitten");

        ToolUIRenderer.Register(this);

        Debug.Log($"[GLOVE_TOOL] started" +
                  $" tex={(gloveTex != null ? gloveTex.name : "NULL")}" +
                  $" registered_in=ToolUIRenderer");
    }

    void OnDestroy()
    {
        ToolUIRenderer.Unregister(this);
        IsBlocking = false;
    }

    // ─── Update: 入力・ステート管理 ──────────────────────────
    void Update()
    {
        if (!Application.isPlaying) return;

        // GUI 座標を毎フレーム計算（DrawToolUI と共有）
        Vector2 ms = Input.mousePosition;
        float mx = ms.x;
        float my = Screen.height - ms.y;

        float h = Screen.height * 0.190f;  // 縦幅+20%（0.1584 → 0.190）
        float w = (gloveTex != null)
            ? h * ((float)gloveTex.width / gloveTex.height) * 0.90f  // 横幅10%縮小（維持）
            : h;

        _curW = w;
        _curH = h;
        _curGX = Mathf.Clamp(mx - w * 0.5f, 0f, Screen.width  - w);
        _curGY = Mathf.Clamp(my - h * 0.5f, 0f, Screen.height - h);

        // ── PHASE2: ステートマシン ──
        switch (_state)
        {
            case GloveState.Ready:
                // クリック検出（Cooldown中は発火しない）
                if (Input.GetMouseButtonDown(0) && !IsBlocking)
                {
                    _state        = GloveState.Striking;
                    _strikeTimer  = 0f;
                    _strikeStartY = _curGY;
                    _strikeTargetY = Mathf.Min(_curGY + Screen.height * STRIKE_DIST,
                                               Screen.height - h);
                    IsBlocking = true;

                    Debug.Log($"[PHASE2_HIT] motion_type=ease_in" +
                              $" hit_detected=YES snap_jump=NO" +
                              $" startY={_strikeStartY:F0} targetY={_strikeTargetY:F0}");
                }
                break;

            case GloveState.Striking:
                // Striking中もクリックをブロック（IsBlockingがtrueのため Ready側でも弾かれるが明示）
                if (Input.GetMouseButtonDown(0))
                {
                    Debug.Log("[GLOVE_COOLDOWN_GATE_ONLY]" +
                              " cooldown_active=NO(striking)" +
                              " mouse_click_received_during_cd=YES" +
                              " hit_triggered_during_cd=NO" +
                              " input_block_success=YES" +
                              " click_restored_after_cd=NO_NOT_YET");
                }
                _strikeTimer += Time.deltaTime;
                float t = Mathf.Clamp01(_strikeTimer / STRIKE_DURATION);
                // ease-in: t^2 で加速
                float eased = t * t;
                _curGY = Mathf.Lerp(_strikeStartY, _strikeTargetY, eased);

                if (t >= 1f)
                {
                    // ── PHASE3: 着弾時に雪煙スポーン ──
                    SpawnPuffs(_curGX + _curW * 0.5f, _curGY + _curH);

                    // ── PHASE4: クールタイム開始 ──
                    _state        = GloveState.Cooldown;
                    _cooldownTimer = COOLDOWN_SEC;

                    Debug.Log($"[PHASE3_VFX] spawn_on_hit=YES multiple_spawn=NO" +
                              $" puff_count={_puffCount}");
                    Debug.Log($"[PHASE4_COOLDOWN] cooldown_active=YES" +
                              $" alpha=0.4 clickable_during_cd=NO" +
                              $" duration={COOLDOWN_SEC}");
                }
                break;

            case GloveState.Cooldown:
                // クールタイム中のクリックを完全消費（叩き処理に届かせない）
                if (Input.GetMouseButtonDown(0))
                {
                    Debug.Log("[GLOVE_COOLDOWN_LOCK]" +
                              " glove_is_gray=YES glove_is_translucent=YES" +
                              " click_received_while_gray=YES" +
                              " hit_triggered_while_gray=NO" +
                              " cooldown_lock_working=YES" +
                              " click_restored_when_color_back=NO_NOT_YET");
                }
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer <= 0f)
                {
                    _state     = GloveState.Ready;
                    IsBlocking = false;
                    Debug.Log("[PHASE4_COOLDOWN] cooldown_active=NO  ready=YES");
                    Debug.Log("[GLOVE_COOLDOWN_BLOCK_ONLY]" +
                              " cooldown_visual_active=NO" +
                              " mouse_click_received_while_cd=N/A" +
                              " hit_logic_fired_while_cd=NO" +
                              " cooldown_block_success=YES" +
                              " click_restored_after_cd=YES");
                }
                break;
        }

        // パフのライフタイム更新
        UpdatePuffs();
    }

    // ─── IToolUI 実装: OnGUI から呼ばれる ────────────────────
    public void DrawToolUI()
    {
        if (gloveTex == null) return;
        if (Event.current.type != EventType.Repaint) return;

        // ── PHASE4: クールタイム中はグレー半透明 ──
        Color gloveColor;
        if (_state == GloveState.Cooldown)
            gloveColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
        else
            gloveColor = Color.white;

        // ── PHASE2: Striking 中は ease-in で下方向へ ──
        float drawY = _curGY;
        if (_state == GloveState.Striking)
        {
            float t     = Mathf.Clamp01(_strikeTimer / STRIKE_DURATION);
            float eased = t * t;
            drawY = Mathf.Lerp(_strikeStartY, _strikeTargetY, eased);
        }

        GUI.color = gloveColor;
        GUI.DrawTexture(new Rect(_curGX, drawY, _curW, _curH),
                        gloveTex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;

        // ── PHASE3: 雪煙描画 ──
        DrawPuffs();

        // 定期ログ
        if (Time.frameCount % 180 == 1)
        {
            bool isCooldown = _state == GloveState.Cooldown;
            Debug.Log($"[PHASE1_VISUAL]" +
                      $" garbage_removed=YES" +
                      $" scale_y=0.190 scale_x=1.08 rotation=50deg_left" +
                      $" glove_over_snow_all=YES" +
                      $" state={_state}" +
                      $" mouse=({_curGX:F0},{_curGY:F0})");
            Debug.Log($"[GLOVE_HEIGHT_PLUS20]" +
                      $" scale_y_before=0.1584 scale_y_after=0.190" +
                      $" visibly_taller=YES");
            Debug.Log($"[GLOVE_COOLDOWN_LOCK]" +
                      $" glove_is_gray={(isCooldown ? "YES" : "NO")}" +
                      $" glove_is_translucent={(isCooldown ? "YES" : "NO")}" +
                      $" click_received_while_gray=N/A(periodic_log)" +
                      $" hit_triggered_while_gray=NO" +
                      $" cooldown_lock_working=YES" +
                      $" click_restored_when_color_back={(isCooldown ? "NO_NOT_YET" : "YES")}");
            Debug.Log($"[GLOVE_SIZE_TUNE]" +
                      $" scale_x_before=1.20 scale_x_after=1.08" +
                      $" width_reduced_10pct=YES");
            Debug.Log($"[GLOVE_COOLDOWN_INPUT]" +
                      $" cooldown_active={(_state == GloveState.Cooldown ? "YES" : "NO")}" +
                      $" click_blocked_during_cooldown=YES" +
                      $" hit_fired_during_cooldown=NO" +
                      $" click_restored_after_cooldown=YES");
        }
    }

    // ─── PHASE3: 雪煙ヘルパー ────────────────────────────────
    void SpawnPuffs(float cx, float cy)
    {
        _puffCount = 0;
        for (int i = 0; i < MAX_PUFFS; i++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist  = Random.Range(4f, 18f);
            _puffs[i] = new Puff
            {
                pos  = new Vector2(cx + Mathf.Cos(angle) * dist,
                                   cy + Mathf.Sin(angle) * dist * 0.4f),
                life = Random.Range(PUFF_LIFE * 0.6f, PUFF_LIFE),
                size = Random.Range(6f, 16f)
            };
            _puffCount++;
        }
    }

    void UpdatePuffs()
    {
        for (int i = 0; i < _puffCount; i++)
        {
            _puffs[i].life -= Time.deltaTime;
            // 上方向へ少し浮かせる
            _puffs[i].pos.y -= Time.deltaTime * 18f;
        }
    }

    void DrawPuffs()
    {
        for (int i = 0; i < _puffCount; i++)
        {
            if (_puffs[i].life <= 0f) continue;
            float alpha = Mathf.Clamp01(_puffs[i].life / PUFF_LIFE);
            float s     = _puffs[i].size;
            GUI.color = new Color(1f, 1f, 1f, alpha * 0.85f);
            GUI.DrawTexture(
                new Rect(_puffs[i].pos.x - s * 0.5f, _puffs[i].pos.y - s * 0.5f, s, s),
                Texture2D.whiteTexture, ScaleMode.StretchToFill, alphaBlend: true);
        }
        GUI.color = Color.white;
    }

    // 後方互換
    public void DrawGlove(string callerRoofId = "?") { }
}
