using UnityEngine;

/// <summary>
/// 毛糸の手袋ツール（完成見本）
///
/// 描画: ToolUIRenderer に IToolUI として登録。
///       SnowStrip2D.OnGUI() 末尾 → ToolUIRenderer.DrawAll() → DrawToolUI() の経路で
///       全積雪描画完了後に描画されるため、全6軒で確実に前面表示される。
///
/// PHASE1: 画像クロップ・リサイズ・50度回転済み（GloveMitten.png で処理済み）
///         サイズ: 全体75% × 横さらに36%縮小（旧0.90 × 0.64 = 0.576）
/// PHASE2: クリックで ease-in 落下 → 影の位置で停止（常に下方向）
/// PHASE3: 着弾時に雪煙パーティクル1回発生（影の位置）
/// PHASE4: クールタイム中はグレー半透明、クリック無効
///
/// 影ルール:
///   shadow_pos = マウス直下の屋根面（屋根上方からの距離を縮める）
///   落下先 = 影位置（startY ≤ targetY を強制して上方向ジャンプ防止）
///   VFX発生点 = 影位置
///   ヒット判定 = 影位置（HasPendingHit / PendingHitGuiPos で SnowStrip2D に通知）
///   → 影 / 手袋到達点 / VFX / ヒット判定を同一点に統一
/// </summary>
[DefaultExecutionOrder(1000)]
public class GloveTool : MonoBehaviour, IToolUI
{
    // ── 公開状態 ──────────────────────────────────────────────
    public static bool IsBlocking { get; private set; } = false;
    public Texture2D gloveTex;

    // ── ヒット通知（SnowStrip2D が参照）─────────────────────
    // 着弾フレームに true になり、SnowStrip2D.HandleTap() が消費してクリアする
    public static bool    HasPendingHit    = false;
    public static Vector2 PendingHitGuiPos = Vector2.zero;

    // ── サイズ定数 ────────────────────────────────────────────
    // 縦: Screen.height × 0.190 × 0.75 = 0.1425（変更禁止）
    const float SCALE_H_BASE  = 0.190f;
    const float SCALE_OVERALL = 0.75f;
    // 横: 旧0.90 → 0.90 × 0.64 = 0.576（さらに20%縮小: 0.80 × 0.80 = 0.64）
    const float SCALE_W_RATIO = 0.90f * 0.64f;   // ≈ 0.576

    // ── PHASE2: 叩き挙動 ──────────────────────────────────────
    enum GloveState { Ready, Striking, Cooldown }
    GloveState _state = GloveState.Ready;

    const float STRIKE_DURATION = 0.18f;
    float _strikeTimer   = 0f;
    float _strikeStartY  = 0f;
    float _strikeTargetY = 0f;

    // ── 影・ヒット統一座標 ────────────────────────────────────
    // 影の中心 GUI 座標（= 落下先 = VFX発生点 = ヒット判定点）
    float _shadowCX = -1f;  // -1 = 有効な屋根が見つからない
    float _shadowCY = -1f;

    // 影基準の手袋描画Y（影から固定オフセット上）
    // 屋根上では常にこれを使う。屋根外（影なし）はマウス追従。
    float _drawGloveY = 0f;

    // 影→手袋上端のオフセット倍率（手袋高さ × この値 だけ上に置く）
    // 約2倍距離: 0.85 → 1.7
    const float SHADOW_GLOVE_OFFSET_RATIO = 1.7f;

    // ── 丸影テクスチャ ────────────────────────────────────────
    Texture2D _shadowTex;

    // ── PHASE4: クールタイム（可変）──────────────────────────
    // 固定値を廃止し、落雪量に応じて動的に決定する
    // cooldown = Clamp(CT_BASE + totalDelta * CT_SCALE, CT_MIN, CT_MAX)
    const float CT_BASE  = 0.25f;   // 最小クールタイムのベース（小ヒット時の連打感を確保）
    const float CT_SCALE = 0.18f;   // totalDelta あたりの増加量（0.12→0.18: 揺らぎ強化）
    const float CT_MIN   = 0.25f;   // 下限（小ヒット時）
    const float CT_MAX   = 2.50f;   // 上限（大雪崩時）
    float _cooldownTimer    = 0f;
    float _lastCooldownUsed = CT_BASE;  // ログ用

    // ── PHASE3: 雪煙 ──────────────────────────────────────────
    struct Puff
    {
        public Vector2 pos;
        public float   life;
        public float   size;
    }
    const int   MAX_PUFFS = 8;
    const float PUFF_LIFE = 0.35f;
    Puff[]  _puffs     = new Puff[MAX_PUFFS];
    int     _puffCount = 0;

    // ── 現在のマウス GUI 座標（DrawToolUI と Update で共有）──
    float _curGX, _curGY, _curW, _curH;

    // ── 後方互換 ──────────────────────────────────────────────
    public static void DrawFrontmost(string callerRoofId = "?")
        => ToolUIRenderer.DrawAll(callerRoofId);

    // ─────────────────────────────────────────────────────────
    void Awake()
    {
        _instance = this;
        // 丸影テクスチャを生成（グラデーション円: 中心が濃く、外縁が透明）
        const int SZ = 64;
        _shadowTex = new Texture2D(SZ, SZ, TextureFormat.RGBA32, false);
        _shadowTex.wrapMode = TextureWrapMode.Clamp;
        float center = SZ * 0.5f;
        for (int py = 0; py < SZ; py++)
        for (int px = 0; px < SZ; px++)
        {
            float dx = (px - center) / center;
            float dy = (py - center) / center;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            float alpha = Mathf.Clamp01(1f - dist);
            alpha = alpha * alpha;  // 二乗で中心に集中
            _shadowTex.SetPixel(px, py, new Color(0f, 0f, 0f, alpha));
        }
        _shadowTex.Apply();
    }

    void Start()
    {
        if (gloveTex == null)
            gloveTex = Resources.Load<Texture2D>("GloveMitten");

        ToolUIRenderer.Register(this);

        Debug.Log($"[GLOVE_TOOL] started tex={(gloveTex != null ? gloveTex.name : "NULL")}");
        Debug.Log($"[GLOVE_SHADOW_RULE]" +
                  $" shadow_is_primary=YES" +
                  $" glove_uses_fixed_offset=YES" +
                  $" offset_ratio={SHADOW_GLOVE_OFFSET_RATIO:F2}" +
                  $" separate_corrections_removed=YES");
    }

    void OnDestroy()
    {
        ToolUIRenderer.Unregister(this);
        IsBlocking         = false;
        HasPendingHit      = false;
        _pendingPieceCount = 0;
        if (_shadowTex != null) { Destroy(_shadowTex); _shadowTex = null; }
    }

    // ── 落雪量レポート（SnowStrip2D.HandleTap から呼ばれる）──
    // totalDelta: 1タップで削った雪量（グリッドセル単位）
    // spawnCount: 生成した落下ピース数
    // Cooldown 状態のときのみ有効（着弾直後に呼ばれる想定）
    public static void ReportImpact(float totalDelta, int spawnCount)
    {
        // シングルトン参照（static から instance メソッドを呼ぶため）
        if (_instance == null) return;
        _instance.ApplyDynamicCooldown(totalDelta, spawnCount);
    }

    // 地面着地通知: 全ピース着地でCT終了（最後の可視落雪基準）
    // BeginCooldownTracking で総ピース数を登録し、
    // NotifyGroundLanding が呼ばれるたびにカウントダウン → 0でCT終了
    static int _pendingPieceCount = 0;

    public static void BeginCooldownTracking(int totalPieces)
    {
        _pendingPieceCount = Mathf.Max(1, totalPieces);
        AssiLogger.Verbose($"[COOLDOWN_SYNC] tracking_started total_pieces={totalPieces}");
    }

    public static void ResetCooldownTracking()
    {
        _pendingPieceCount = 0;
    }

    public static void NotifyGroundLanding()
    {
        if (_instance == null) return;
        if (_pendingPieceCount <= 0) return;  // 追跡中でなければ無視
        _pendingPieceCount--;
        AssiLogger.Verbose($"[COOLDOWN_SYNC] ground_landing pending_remaining={_pendingPieceCount}");
        if (_pendingPieceCount <= 0)
            _instance.EndCooldownNow();
    }

    void EndCooldownNow()
    {
        if (_state != GloveState.Cooldown) return;
        _cooldownTimer = 0f;
        _state         = GloveState.Ready;
        IsBlocking     = false;
        Debug.Log("[COOLDOWN_SYNC] cooldown_ended=YES");
    }

    static GloveTool _instance;

    void ApplyDynamicCooldown(float totalDelta, int spawnCount)
    {
        if (_state != GloveState.Cooldown) return;

        float prevCT = _lastCooldownUsed;
        // cooldown = CT_BASE + totalDelta * CT_SCALE, clamped [CT_MIN, CT_MAX]
        float newCT = Mathf.Clamp(CT_BASE + totalDelta * CT_SCALE, CT_MIN, CT_MAX);

        _cooldownTimer    = newCT;
        _lastCooldownUsed = newCT;

        string hitSize = totalDelta > 8f ? "big" : (totalDelta > 2f ? "medium" : "small");

        AssiLogger.Verbose($"[COOLDOWN_DYNAMIC] ct={newCT:F2} hitSize={hitSize}");
    }

    // ─── Update: 入力・ステート管理 ──────────────────────────
    void Update()
    {
        if (!Application.isPlaying) return;

        // GUI 座標を毎フレーム計算
        Vector2 ms = Input.mousePosition;
        float mx = ms.x;
        float my = Screen.height - ms.y;

        float h = Screen.height * SCALE_H_BASE * SCALE_OVERALL;
        float w = (gloveTex != null)
            ? h * ((float)gloveTex.width / gloveTex.height) * SCALE_W_RATIO
            : h;

        _curW = w;
        _curH = h;
        _curGX = Mathf.Clamp(mx - w * 0.5f, 0f, Screen.width  - w);
        _curGY = Mathf.Clamp(my - h * 0.5f, 0f, Screen.height - h);

        // ── 影位置を毎フレーム更新 ──
        UpdateShadowPos(mx, my);

        // ── 手袋描画Y: 影基準で固定オフセット（屋根上のみ）──
        // 影がある（屋根上）    → 影から上に _curH × RATIO だけ置く
        // 影がない + キャッシュあり → キャッシュ位置で固定（ワープ防止）
        // 影がない + キャッシュなし → マウス追従（_curGY）
        if (_shadowCX >= 0f)
        {
            _drawGloveY = _shadowCY - _curH * SHADOW_GLOVE_OFFSET_RATIO;
        }
        else if (_lastValidShadowCX >= 0f)
        {
            // 無効領域：最後の有効影位置で固定（ワープしない）
            _drawGloveY = _lastValidShadowCY - _curH * SHADOW_GLOVE_OFFSET_RATIO;
        }
        else
        {
            _drawGloveY = _curGY;
        }

        // ── PHASE2: ステートマシン ──
        switch (_state)
        {
            case GloveState.Ready:
                // 影がない場所（屋根外・川・地面）ではクリックを無視
                // → 「影がある場所 = 叩ける場所」を完全一致させる
                if (Input.GetMouseButtonDown(0) && !IsBlocking)
                {
                    if (_shadowCX < 0f)
                    {
                        // 影なし = 屋根外クリック → 何もしない
                        break;
                    }

                    _state       = GloveState.Striking;
                    _strikeTimer = 0f;
                    _strikeStartY = _drawGloveY;

                    // 落下先 = 影のY - 手袋高さ（影上端に手袋下端が揃う）
                    // 影が見つからない場合のみフォールバック（下方向固定）
                    // ※ Mathf.Max 制約を外す: 屋根上端を狙うと targetY < startY になるが
                    //   それは「手袋が既に屋根より上にいる」ケースで正常な下方向落下
                    if (_shadowCY >= 0f)
                        _strikeTargetY = _shadowCY - _curH;
                    else
                        _strikeTargetY = _curGY + Screen.height * 0.08f;

                    IsBlocking = true;

                    Debug.Log($"[PHASE2_HIT] startY={_strikeStartY:F0} targetY={_strikeTargetY:F0} shadowPos=({_shadowCX:F0},{_shadowCY:F0})");
                }
                break;

            case GloveState.Striking:
                if (Input.GetMouseButtonDown(0))
                {
                    // striking 中クリックは無視（何もしない）
                }
                _strikeTimer += Time.deltaTime;
                float t = Mathf.Clamp01(_strikeTimer / STRIKE_DURATION);
                float eased = t * t;
                _curGY = Mathf.Lerp(_strikeStartY, _strikeTargetY, eased);

                if (t >= 1f)
                {
                    // VFX・視覚位置 = 影の視覚中心（_shadowCY）
                    // ヒット判定位置 = 屋根内部（_shadowHitY）→ _guiRect.Contains を確実に通す
                    float hitX    = _shadowCX >= 0f ? _shadowCX : (_curGX + _curW * 0.5f);
                    float vfxY    = _shadowCY >= 0f ? _shadowCY : (_curGY + _curH);
                    float hitY    = _shadowHitY >= 0f ? _shadowHitY : vfxY;

                    SpawnPuffs(hitX, vfxY);

                    // SnowStrip2D へヒット通知（屋根内部座標 = 実ヒット位置）
                    HasPendingHit    = true;
                    PendingHitGuiPos = new Vector2(hitX, hitY);

                    _state = GloveState.Cooldown;
                    // 暫定クールタイム（ReportImpact() で落雪量確定後に上書きされる）
                    _cooldownTimer    = CT_BASE;
                    _lastCooldownUsed = CT_BASE;

                    Debug.Log($"[GLOVE_HIT] hitPos=({hitX:F0},{hitY:F0}) shadowPos=({_shadowCX:F0},{_shadowCY:F0}) ct={CT_BASE:F2}");
                }
                break;

            case GloveState.Cooldown:
                if (Input.GetMouseButtonDown(0))
                {
                    // CD中クリックは無視
                }
                _cooldownTimer -= Time.deltaTime;
                if (_cooldownTimer <= 0f)
                {
                    _state     = GloveState.Ready;
                    IsBlocking = false;
                    Debug.Log("[PHASE4_COOLDOWN] cooldown_active=NO ready=YES");
                }
                break;
        }

        UpdatePuffs();
    }

    // ── 影位置更新 ────────────────────────────────────────────
    // 段ルール:
    //   手袋中心Y が上段帯にある → 上段屋根のみ影候補
    //   手袋中心Y が下段帯にある → 下段屋根のみ影候補
    //   段またぎ選択は禁止
    //
    // 影Y:
    //   屋根上端（r.y）を基準にするが、手袋下端より必ず下に補正
    //   → 上昇バグを構造的に防止
    //
    // ヒット位置:
    //   _shadowHitY = 屋根内部（r.y + r.height * 0.3f）
    //   → SnowStrip2D._guiRect.Contains() を確実に通す
    float _shadowHitY = -1f;  // ヒット判定用Y（屋根内部）

    void UpdateShadowPos(float mx, float my)
    {
        _shadowCX   = -1f;
        _shadowCY   = -1f;
        _shadowHitY = -1f;

        var infos = SnowStrip2D.RoofInfos;
        if (infos.Count == 0) return;

        // ── 上段/下段の境界を計算 ────────────────────────────────
        float upperMaxY = float.MinValue;
        float lowerMinY = float.MaxValue;
        for (int i = 0; i < infos.Count; i++)
        {
            if (infos[i].isUpper) upperMaxY = Mathf.Max(upperMaxY, infos[i].rect.yMax);
            else                  lowerMinY = Mathf.Min(lowerMinY, infos[i].rect.y);
        }
        bool hasUpper = upperMaxY > float.MinValue;
        bool hasLower = lowerMinY < float.MaxValue;
        bool gloveIsUpper;
        if (hasUpper && hasLower)
            gloveIsUpper = my < (upperMaxY + lowerMinY) * 0.5f;
        else if (hasUpper)
            gloveIsUpper = true;
        else
            gloveIsUpper = false;

        // ── 対象屋根を X 範囲と段で特定 ─────────────────────────
        // Y範囲チェックは「川・地面」除外のみ（屋根より上は許可）
        for (int i = 0; i < infos.Count; i++)
        {
            var info = infos[i];
            if (info.isUpper != gloveIsUpper) continue;
            if (mx < info.rect.x || mx > info.rect.xMax) continue;
            if (my > info.rect.yMax) continue;   // 屋根より下（川・地面）は影なし

            _shadowCX = mx;

            // ── 正規化 → 再マッピング（死に帯ゼロ） ─────────────
            // 手袋の可動域: 画面上端(0) 〜 屋根下端(roofBottom)
            // 影の可動域:   屋根上端(roofTop) 〜 屋根下端70%(roofShadowMax)
            float roofTop       = info.rect.y;
            float roofBottom    = info.rect.yMax;
            float roofShadowMax = Mathf.Lerp(roofTop, roofBottom, 0.7f);

            // 手袋中心Y を [0, roofBottom] で正規化
            float gloveMinY = 0f;
            float gloveMaxY = roofBottom;
            float t = Mathf.InverseLerp(gloveMinY, gloveMaxY, my);

            // 影を [roofTop, roofShadowMax] にマッピング
            _shadowCY = Mathf.Lerp(roofTop, roofShadowMax, t);

            // ヒット判定Y = 屋根内部（上端から30%）→ Contains() を確実に通す
            _shadowHitY = roofTop + info.rect.height * 0.3f;

            _gloveIsUpper        = gloveIsUpper;
            _selectedBandIsUpper = info.isUpper;

            // 有効位置をキャッシュ（無効領域でのワープ防止用）
            _lastValidShadowCX = _shadowCX;
            _lastValidShadowCY = _shadowCY;

            // 影Y が 30px 以上変化したときのみ verbose ログ
            if (Mathf.Abs(_shadowCY - _lastLoggedShadowCY) >= 30f)
            {
                _lastLoggedShadowCY = _shadowCY;
                AssiLogger.Verbose($"[SHADOW_MAPPING]" +
                          $" glove_y={my:F0}" +
                          $" shadow_y={_shadowCY:F0}" +
                          $" roof_range=({roofTop:F0},{roofShadowMax:F0})");
            }
            break;
        }
    }

    // 段ログ用フィールド
    bool  _gloveIsUpper        = false;
    bool  _selectedBandIsUpper = false;
    // SHADOW_MAPPING ログを間引くための前回値
    float _lastLoggedShadowCY  = -9999f;
    // 最後の有効影位置キャッシュ（無効領域でワープしないために使用）
    float _lastValidShadowCX   = -1f;
    float _lastValidShadowCY   = -1f;

    // ─── IToolUI 実装: OnGUI から呼ばれる ────────────────────
    public void DrawToolUI()
    {
        if (gloveTex == null) return;
        if (Event.current.type != EventType.Repaint) return;

        // ── 影の描画 ──
        // 有効位置(shadowCX>=0): Ready/Striking 中のみ影を描く
        // 無効位置(キャッシュあり): グレー影（叩けない状態を視覚的に示す）
        if (_shadowCX >= 0f && _state != GloveState.Cooldown)
            DrawShadow(_shadowCX, _shadowCY);
        else if (_shadowCX < 0f && _lastValidShadowCX >= 0f && _state != GloveState.Cooldown)
            DrawShadow(_lastValidShadowCX, _lastValidShadowCY, grayOut: true);

        // ── 手袋色ルール統一 ──
        // 叩ける   (Ready + 影あり) → 緑
        // 叩けない (Cooldown / 屋根外 / 影なし) → グレー半透明
        bool canHit = (_state == GloveState.Ready && _shadowCX >= 0f);
        Color gloveColor;
        if (_state == GloveState.Striking)
            gloveColor = new Color(0.4f, 0.9f, 0.4f, 1f);   // Striking中も緑（連続感）
        else if (canHit)
            gloveColor = new Color(0.4f, 0.9f, 0.4f, 1f);   // 叩ける → 緑
        else
            gloveColor = new Color(0.5f, 0.5f, 0.5f, 0.45f); // 叩けない → グレー

        // ── 手袋描画Y の決定 ──
        // Ready   : _drawGloveY（影基準固定オフセット）
        // Striking: ease-in で _strikeStartY → _strikeTargetY - h * RATIO
        // Cooldown: _drawGloveY（マウス追従、グレー表示）
        float drawY;
        if (_state == GloveState.Striking)
        {
            float t     = Mathf.Clamp01(_strikeTimer / STRIKE_DURATION);
            float eased = t * t;
            float targetDrawY = _strikeTargetY - _curH * SHADOW_GLOVE_OFFSET_RATIO;
            drawY = Mathf.Lerp(_strikeStartY, targetDrawY, eased);
        }
        else
        {
            drawY = _drawGloveY;
        }

        GUI.color = gloveColor;
        GUI.DrawTexture(new Rect(_curGX, drawY, _curW, _curH),
                        gloveTex, ScaleMode.ScaleToFit, alphaBlend: true);
        GUI.color = Color.white;

        // ── PHASE3: 雪煙描画 ──
        DrawPuffs();

        // 色・距離の確認ログ（verbose）
        AssiLogger.Verbose($"[GLOVE_STATE]" +
                  $" glove_distance_px={(_shadowCY - (drawY + _curH)):F1}" +
                  $" glove_can_hit={(canHit ? "YES" : "NO")}" +
                  $" glove_color_state={(canHit || _state == GloveState.Striking ? "GREEN" : "GRAY")}");
    }

    // ── 影描画: 屋根上端に丸影（グラデーション楕円）────────────
    void DrawShadow(float cx, float cy)
    {
        // 丸影テクスチャが未生成の場合は何もしない
        Texture2D tex = (_shadowTex != null) ? _shadowTex : Texture2D.whiteTexture;

        // 楕円形に見えるよう横長に描画
        float sw = _curW * 1.1f;   // 手袋幅より少し広め
        float sh = sw * 0.30f;     // 縦は横の30%（楕円）
        float sx = cx - sw * 0.5f;
        float sy = cy - sh * 0.5f;

        // 白で描画（グラデーション円テクスチャ自体が黒→透明）
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(sx, sy, sw, sh),
                        tex, ScaleMode.StretchToFill, alphaBlend: true);
        GUI.color = Color.white;
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
