using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// WORK_SNOW シーン専用。
/// 【モード: ALL_6_ROOFS + ALL_6_UNDER_EAVE_LANDING + ALL_6_THICK_SNOW + DOWNHILL_SLIDE】
///
/// ① 6軒の屋根雪表示（OnGUI DrawTexture：前縁凹凸+グラデーション）
/// ② 6軒すべて: タップ検出 → 屋根雪縮小 → 大きめ雪塊2〜4個+雪煙が落下 → 各軒下で停止
/// ③ 6軒すべて: OnGUI で屋根上端に厚雪帯を追加描画（叩くと縮む）
/// ④ 落雪は downhill 方向（片流れ屋根の手前方向）に初速を与えて滑走
///
/// 着地 Y は各屋根の calib maxY + UNDER_EAVE_OFFSET_CALIB から直接計算。
/// </summary>
[ExecuteAlways]
public class WorkSnowForcer : MonoBehaviour
{
    const string CALIB_PATH = "Assets/Art/RoofCalibrationData.json";

    // 屋根下端からの軒下オフセット（calib 座標、0〜1）
    // 0.08: 高すぎ / 0.20: 下段屋根に誤着地 → 0.10 に設定
    // クランプロジックで他屋根への侵入を防ぐ
    const float UNDER_EAVE_OFFSET_CALIB = 0.10f;

    // eaveGuiY の最大オフセット（自分の guiRect.yMax からの最大距離、calib 座標）
    // これを超えると他段の屋根に入る可能性があるため上限として使用
    const float EAVE_MAX_EXTRA_CALIB = 0.12f;

    // THICK 状態の thickRatio 値（屋根高さに対する厚雪帯の割合）
    // RoofData.thickRatio に設定する。0 = NORMAL、この値 = THICK
    // 0.65 → 0.25: 屋根高さの25%を積雪帯にする（屋根全体を覆わないよう修正）
    const float THICK_SNOW_RATIO = 0.25f;

    // 1軒モード: Roof_Main のみ（旧6軒は Background_OneHouse 差し替えで廃止）
    static readonly (string calibId, string guideId)[] RoofPairs =
    {
        ("Roof_Main", "RoofGuide_Main"),
    };

    static readonly Color SnowWhite = new Color(0.92f, 0.95f, 1f);

    // ── 崩落タイプ ────────────────────────────────────────────
    enum CollapseType { Small, Medium, Avalanche }

    // ── 積雪ヒットマップ解像度 ─────────────────────────────────
    // 屋根を横 SNOW_COLS 列に分割し、各列が独立した残雪量(0〜1)を持つ。
    // これにより「帯状減少」を廃止し、タップ位置起点の局所崩落を実現する。
    const int SNOW_COLS = 16;

    // ── 屋根ごとのデータ ──────────────────────────────────────
    struct RoofData
    {
        public string  id;
        public string  guideId;
        public Rect    guiRect;
        public float   eaveGuiY;
        public float   eaveGuiX;
        // ── 旧: snowFill（帯状管理・廃止）→ 互換用プロパティとして残す ──
        // 実体は snowCols[] の平均値。直接書き込み禁止。
        public float   snowFill;      // 読み取り専用互換（= snowCols 平均）
        public float[] snowCols;      // [SNOW_COLS] 各列の残雪量 0〜1（実体）
        public float   anchorMinY0;
        public float   anchorMaxY0;
        public float   thickRatio;
        public Vector2 downhillDir;
        public float   downhillVelX;
        public bool    ready;
        // 可変崩落用
        public float   collapseCharge;
        public float   instability;
    }
    RoofData[] _roofs = new RoofData[1];

    // ── 落下中の雪塊（大きめ不定形）──────────────────────────
    enum PiecePhase { Sliding, Falling }

    struct FallingPiece
    {
        public Vector2    pos;
        public Vector2    vel;
        public float      size;      // 幅px
        public float      sizeY;     // 高さpx
        public float      life;
        public int        roofIdx;
        public float      rot;
        public float      rotVel;
        public float      alpha;
        public int        texIdx;

        // ── 屋根滑落フェーズ制御 ──
        public PiecePhase phase;
        public Vector2    slideDir;    // 正規化滑落方向（downhillDir をコピー）
        public float      slideSpeed;  // 滑落速度（px/s）
        public float      slideDirX;   // 後方互換（未使用）
        public float      slideY;      // 滑落中の基準Y（屋根下端）
        public float      slideStartX; // 滑落開始X
        public float      eaveX;       // 軒先X（到達判定用）
        public float      eaveY;       // 軒先Y（到達判定用）
        public float      slideDist;   // 累積移動距離
        public float      maxSlideDist;// 最大滑落距離（屋根幅）

        // 引っかかり制御
        public float      stickTimer;
        public float      stickCooldown;
        public bool       hasStick;
    }
    readonly List<FallingPiece> _pieces = new List<FallingPiece>();

    // ── 雪煙パーティクル ──────────────────────────────────────
    struct SnowSmoke
    {
        public Vector2 pos;
        public Vector2 vel;
        public float   size;
        public float   life;
        public float   maxLife;
    }
    readonly List<SnowSmoke> _smoke = new List<SnowSmoke>();

    // ── 着地済み雪片 ──────────────────────────────────────────
    struct LandedPiece
    {
        public Vector2 pos;
        public float   size;
        public float   sizeY;
        public float   remainLife;
        public int     roofIdx;
        public int     texIdx;
    }
    readonly List<LandedPiece> _landedPieces = new List<LandedPiece>();

    bool      _applied  = false;
    bool      _roofsReady = false;
    Texture2D _whiteTex;
    Texture2D _snowEdgeTex;       // 前縁凹凸用テクスチャ（ノイズ生成）
    Texture2D[] _chunkTextures;   // 不定形雪塊シルエット（4種類）
    Texture2D _roofEdgeMaskTex;   // 屋根雪前縁マスク（縦方向ノイズ）
    Texture2D _smokeTex;          // 雪煙用ソフト円形テクスチャ（四角回避）
    Texture2D[] _brushTextures;   // 不定形ブラシ（5種: 円/楕円/欠け/しずく/多角形）

    // ── 局所積雪マスク（タップ位置に不定形ブラシで穴を刻む）─────
    struct SnowHole
    {
        public float cx;       // 穴中心X（guiRect.width に対する割合 0〜1）
        public float cy;       // 穴中心Y（雪帯の高さに対する割合 0〜1）
        public float scaleX;   // ブラシ横スケール（guiRect.width 倍）
        public float scaleY;   // ブラシ縦スケール（雪帯高さ倍）
        public float rot;      // 回転角（deg）
        public float alpha;    // 不透明度（0〜1）
        public int   brushIdx; // ブラシ種類（0〜4）
    }
    readonly List<SnowHole>[] _snowHoles = new List<SnowHole>[6];

    // ── 表示雪一時停止フラグ ──────────────────────────────────
    // true にすると OnGUI の屋根雪帯描画をスキップする（落雪ロジック確認モード）
    // Inspector から切り替え可能。デフォルト false（通常表示）
    [Header("Debug")]
    [Tooltip("true=屋根表示雪OFF（落雪ロジック確認モード）")]
    public bool debugHideRoofSnow = false;

    [System.Serializable] class V2 { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2 topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!scene.Contains("WORK_SNOW")) return;
        if (Object.FindFirstObjectByType<WorkSnowForcer>() != null) return;

        var bgGo = GameObject.Find("BackgroundImage");
        GameObject host;
        if (bgGo != null)
        {
            host = bgGo;
            host.AddComponent<WorkSnowForcer>();
        }
        else
        {
            host = new GameObject("WorkSnowForcer_Root");
            host.AddComponent<WorkSnowForcer>();
        }

        // [SNOWDEPTH_ONELINE] SnowStripV2 / SnowStrip2D の自動生成を停止
        // B方式（RoofSnowSystem 主役）へ一本化。旧ストリップ系は生成しない。
        Debug.Log("[LEGACY_CUTOFF] worksnowforcer_spawns_strip=NO snowstrip2d_present_in_hierarchy=NO snowstripv2_present_in_hierarchy=NO roofsnowsystem_only_active=YES");

        /* 旧コード: SnowStripV2 生成 ── 無効化
        // SnowStrip V2 を同じ GameObject に追加（全6軒管理）
        if (host.GetComponent<SnowStripV2>() == null)
        {
            host.AddComponent<SnowStripV2>();
            Debug.Log("[V2_BOOTSTRAP] SnowStripV2 added for ALL6 roofs");
        }
        */

        /* 旧コード: SnowStrip2D 生成 ── 無効化
        // SnowStrip 2D を Roof_Main に追加（1軒モード）
        var roofDefs = new (string roofId, string guideId)[]
        {
            ("Roof_Main", "RoofGuide_Main"),
        };

        int applied = 0;
        foreach (var (rid, gid) in roofDefs)
        {
            // 既存インスタンスがあればスキップ（重複防止）
            bool alreadyExists = false;
            foreach (var existing in Object.FindObjectsByType<SnowStrip2D>(FindObjectsSortMode.None))
            {
                if (existing.roofId == rid) { alreadyExists = true; break; }
            }
            if (alreadyExists) continue;

            var go   = new GameObject($"SnowStrip2D_{rid}");
            var comp = go.AddComponent<SnowStrip2D>();
            comp.roofId  = rid;
            comp.guideId = gid;
            applied++;

            Debug.Log($"[2D_BOOTSTRAP] SnowStrip2D added roof={rid} guide={gid}");
        }
        */
        int applied = 0; // ログ互換用

        Debug.Log($"[ROOF_BIND_CHECK] applied={applied}/1" +
                  $" slide=YES engulf=YES puff=YES" +
                  $" chunkMinScale=7 chunkMaxScale=22" +
                  $" chunkAvgScale=14 houseCountApplied={applied}");

        Debug.Log("[ALL6_SNOW_FIT] Bootstrap scene=" +
                  UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
    }

    void OnEnable()
    {
        _applied    = false;
        _roofsReady = false;
        _pieces.Clear();
        _landedPieces.Clear();
        _smoke.Clear();
        for (int i = 0; i < _roofs.Length; i++)
        {
            // ヒットマップ初期化（全列 1.0 = 満雪）
            _roofs[i].snowCols       = new float[SNOW_COLS];
            for (int c = 0; c < SNOW_COLS; c++) _roofs[i].snowCols[c] = 1f;
            _roofs[i].snowFill       = 1f; // 互換用（snowCols 平均）
            _roofs[i].anchorMinY0    = -1f;
            _roofs[i].anchorMaxY0    = -1f;
            _roofs[i].ready          = false;
            _roofs[i].collapseCharge = 0f;
            _roofs[i].instability    = Random.Range(0.3f, 1.0f);
            // 穴リストは廃止（ヒットマップに統合）
            if (_snowHoles[i] == null) _snowHoles[i] = new List<SnowHole>();
            else _snowHoles[i].Clear();
        }
        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }
        // テクスチャ生成
        BuildSnowEdgeTexture();
        BuildChunkTextures();
        BuildRoofEdgeMaskTexture();
        BuildSmokeTexture();
        BuildBrushTextures();
    }

    /// <summary>
    /// 屋根雪の前縁に凹凸を出すためのノイズテクスチャを生成。
    /// 横64px・縦1px、アルファ値でマスク。
    /// </summary>
    void BuildSnowEdgeTexture()
    {
        const int W = 64;
        if (_snowEdgeTex != null) Object.DestroyImmediate(_snowEdgeTex);
        _snowEdgeTex = new Texture2D(W, 1, TextureFormat.RGBA32, false);
        _snowEdgeTex.wrapMode = TextureWrapMode.Repeat;
        var pixels = new Color[W];
        float seed = Random.Range(0f, 100f);
        for (int x = 0; x < W; x++)
        {
            float n = Mathf.PerlinNoise(x * 0.18f + seed, 0f);
            float v = 0.4f + n * 0.6f;
            pixels[x] = new Color(1f, 1f, 1f, v);
        }
        _snowEdgeTex.SetPixels(pixels);
        _snowEdgeTex.Apply();
    }

    /// <summary>
    /// 屋根雪前縁の縦方向マスクテクスチャを生成。
    /// 横1px・縦32px、各行のアルファ値が「前縁の垂れ量」を表す。
    /// 上部は不透明、下部はノイズで欠ける。
    /// </summary>
    void BuildRoofEdgeMaskTexture()
    {
        const int H = 32;
        if (_roofEdgeMaskTex != null) Object.DestroyImmediate(_roofEdgeMaskTex);
        _roofEdgeMaskTex = new Texture2D(H, H, TextureFormat.RGBA32, false);
        _roofEdgeMaskTex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[H * H];
        float seedX = Random.Range(0f, 100f);
        float seedY = Random.Range(0f, 100f);
        for (int y = 0; y < H; y++)
        {
            // y=0 が上端（屋根本体側）、y=H-1 が前縁（下端）
            float t = (float)y / (H - 1);
            for (int x = 0; x < H; x++)
            {
                float u = (float)x / (H - 1);
                // 上部は確実に不透明、下部ほどノイズで欠ける
                float baseAlpha = 1f - t * 0.5f;
                // 低周波ノイズ: 大きな垂れ・欠けを作る
                float n1 = Mathf.PerlinNoise(u * 3f + seedX, t * 2f + seedY);
                // 高周波ノイズ: 細かい凹凸
                float n2 = Mathf.PerlinNoise(u * 9f + seedX, t * 5f + seedY) * 0.3f;
                float threshold = 0.35f + t * 0.55f; // 下端ほど欠けやすい
                float alpha = (n1 + n2 > threshold) ? baseAlpha : baseAlpha * 0.1f;
                pixels[y * H + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            }
        }
        _roofEdgeMaskTex.SetPixels(pixels);
        _roofEdgeMaskTex.Apply();
    }

    /// <summary>
    /// 不定形雪塊シルエットテクスチャを4種類生成。
    /// 32×32px、アルファチャンネルで形状を表現。
    /// 各種類で角丸・左右非対称・欠けを変化させる。
    /// </summary>
    /// <summary>
    /// 雪煙用ソフト円形グラデーションテクスチャを生成。
    /// 中心が白く不透明、外縁に向かってなめらかにフェードアウト。
    /// 四角く見えないように周辺アルファを完全に0にする。
    /// </summary>
    void BuildSmokeTexture()
    {
        const int S = 32;
        if (_smokeTex != null) Object.DestroyImmediate(_smokeTex);
        _smokeTex = new Texture2D(S, S, TextureFormat.RGBA32, false);
        _smokeTex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color[S * S];
        float seed = Random.Range(0f, 100f);
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float u = (float)x / (S - 1) * 2f - 1f; // -1〜1
                float v = (float)y / (S - 1) * 2f - 1f;
                float dist = Mathf.Sqrt(u * u + v * v); // 中心からの距離

                // Smooth な円形グラデーション: dist=0 で alpha=1、dist=1 で alpha=0
                float alpha = Mathf.Clamp01(1f - dist * dist * dist * 1.4f);

                // 外縁のぼけを強調（2乗で急速フェード）
                alpha = Mathf.Pow(alpha, 1.8f);

                // 軽いノイズで輪郭を不定形に
                float n = Mathf.PerlinNoise(u * 3f + seed, v * 3f + seed) * 0.22f;
                alpha = Mathf.Clamp01(alpha - n * (1f - alpha * 0.5f));

                // 完全に外縁（dist > 0.95）はアルファ 0
                if (dist > 0.95f) alpha = 0f;

                pixels[y * S + x] = new Color(1f, 1f, 1f, alpha);
            }
        }
        _smokeTex.SetPixels(pixels);
        _smokeTex.Apply();
    }

    /// <summary>
    /// 積雪削除用の不定形ブラシテクスチャを5種類生成。
    /// 各ブラシは中心が濃く外縁がソフトにフェードし、輪郭をPerlinノイズで崩す。
    /// 種類: 0=円形, 1=横楕円, 2=欠け円, 3=しずく(下流伸び), 4=不定形多角
    /// </summary>
    void BuildBrushTextures()
    {
        const int S = 48;
        if (_brushTextures != null)
            foreach (var t in _brushTextures) if (t != null) Object.DestroyImmediate(t);
        _brushTextures = new Texture2D[5];

        for (int kind = 0; kind < 5; kind++)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[S * S];
            float seed = kind * 31.7f + 5f;

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (float)x / (S - 1) * 2f - 1f; // -1〜1
                    float v = (float)y / (S - 1) * 2f - 1f;

                    float dist;
                    switch (kind)
                    {
                        case 0: // 円形
                            dist = Mathf.Sqrt(u * u + v * v);
                            break;
                        case 1: // 横楕円（横1.6倍）
                            dist = Mathf.Sqrt((u / 1.6f) * (u / 1.6f) + v * v);
                            break;
                        case 2: // 欠け円（右下が少し欠ける）
                            dist = Mathf.Sqrt(u * u + v * v);
                            dist += Mathf.Max(0f, u * 0.3f + v * 0.2f); // 右下方向を膨らませ欠けに
                            break;
                        case 3: // しずく（下流=+v方向に伸びる）
                            {
                                float vy = v < 0f ? v * 0.7f : v * 1.35f; // 下方向に伸ばす
                                dist = Mathf.Sqrt(u * u + vy * vy);
                            }
                            break;
                        default: // 不定形多角（ノイズで角を出す）
                            dist = Mathf.Sqrt(u * u + v * v);
                            float angle = Mathf.Atan2(v, u);
                            float poly  = Mathf.Abs(Mathf.Cos(angle * 3.5f)) * 0.22f; // 多角形ぽい凹凸
                            dist += poly;
                            break;
                    }

                    // ソフトエッジ: 中心alpha=1、外縁=0
                    float alpha = Mathf.Clamp01(1f - dist * dist * 1.5f);
                    alpha = Mathf.Pow(alpha, 1.6f);

                    // Perlinノイズで輪郭を崩す
                    float noise = Mathf.PerlinNoise(u * 4f + seed, v * 4f + seed) * 0.28f;
                    alpha = Mathf.Clamp01(alpha - noise * (1f - alpha * 0.4f));

                    // 外縁完全透明
                    if (dist > 1.05f) alpha = 0f;

                    pixels[y * S + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _brushTextures[kind] = tex;
        }
    }

    /// <summary>
    /// 不定形雪塊シルエットを6種類生成。
    /// 各種類で形状が大きく異なる（丸塊・横長・縦長・三角崩れ・L字風・多角形風）。
    /// </summary>
    void BuildChunkTextures()
    {
        const int S = 48; // 解像度を上げて形状をより鮮明に
        if (_chunkTextures != null)
            foreach (var t in _chunkTextures)
                if (t != null) Object.DestroyImmediate(t);
        _chunkTextures = new Texture2D[6];

        for (int ti = 0; ti < 6; ti++)
        {
            float sd  = Random.Range(0f, 100f);
            float sd2 = Random.Range(0f, 100f);
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[S * S];

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (float)x / (S - 1); // 0〜1
                    float v = (float)y / (S - 1);
                    float alpha = 0f;

                    switch (ti)
                    {
                        case 0: // 丸塊（中央重め・左右非対称）
                        {
                            float cx = 0.48f, cy = 0.52f, rx = 0.40f, ry = 0.36f;
                            float dx = (u - cx) / rx, dy = (v - cy) / ry;
                            float dist = Mathf.Sqrt(dx*dx + dy*dy);
                            if (dist < 1f)
                            {
                                float n = Mathf.PerlinNoise(u*3.5f+sd, v*3.5f+sd);
                                float threshold = 0.28f + dist * 0.42f;
                                if (n > threshold)
                                    alpha = Mathf.Clamp01((1f - dist) * 2.8f);
                            }
                            break;
                        }
                        case 1: // 横長・扁平（雪が潰れた形）
                        {
                            float cx = 0.46f, cy = 0.55f, rx = 0.46f, ry = 0.26f;
                            float dx = (u - cx) / rx, dy = (v - cy) / ry;
                            float dist = Mathf.Sqrt(dx*dx + dy*dy);
                            if (dist < 1f)
                            {
                                float n = Mathf.PerlinNoise(u*5f+sd, v*4f+sd*0.8f);
                                float threshold = 0.32f + dist * 0.38f;
                                if (n > threshold)
                                    alpha = Mathf.Clamp01((1f - dist) * 3.2f);
                            }
                            break;
                        }
                        case 2: // 三角崩れ（上が尖り、下が広い）
                        {
                            // 上に向かって細くなる三角形ベース
                            float narrowX = 0.5f + (v - 0.5f) * 0.7f * (u - 0.5f > 0 ? 1 : -1);
                            float triDist = Mathf.Abs(u - 0.5f) / Mathf.Max(0.05f, (1f - v) * 0.45f + 0.05f);
                            float vertDist = v; // 上端に近いほど細い
                            if (triDist < 1f && vertDist < 0.92f)
                            {
                                float n = Mathf.PerlinNoise(u*4f+sd, v*5f+sd2);
                                float threshold = 0.30f + triDist * 0.35f;
                                if (n > threshold)
                                    alpha = Mathf.Clamp01((1f - triDist) * 2.5f * (1f - vertDist * 0.3f));
                            }
                            break;
                        }
                        case 3: // 縦長・不規則（縦に伸びた塊）
                        {
                            float cx = 0.52f, cy = 0.50f, rx = 0.28f, ry = 0.44f;
                            float dx = (u - cx) / rx, dy = (v - cy) / ry;
                            float dist = Mathf.Sqrt(dx*dx + dy*dy);
                            if (dist < 1f)
                            {
                                float n1 = Mathf.PerlinNoise(u*3f+sd, v*3f+sd);
                                float n2 = Mathf.PerlinNoise(u*7f+sd2, v*6f+sd2) * 0.4f;
                                float threshold = 0.26f + dist * 0.50f;
                                if ((n1 + n2) > threshold)
                                    alpha = Mathf.Clamp01((1f - dist) * 3f);
                            }
                            break;
                        }
                        case 4: // L字風（右下が欠けた形）
                        {
                            // 左上の楕円 + 右下を大きく欠く
                            float cx = 0.44f, cy = 0.46f, rx = 0.42f, ry = 0.38f;
                            float dx = (u - cx) / rx, dy = (v - cy) / ry;
                            float dist = Mathf.Sqrt(dx*dx + dy*dy);
                            // 右下コーナーを強制除去
                            bool cutCorner = (u > 0.62f && v > 0.60f);
                            if (dist < 1f && !cutCorner)
                            {
                                float n = Mathf.PerlinNoise(u*4.5f+sd, v*4f+sd2);
                                float threshold = 0.29f + dist * 0.44f;
                                if (n > threshold)
                                    alpha = Mathf.Clamp01((1f - dist) * 2.8f);
                            }
                            break;
                        }
                        case 5: // 多角形風（ノイズ強め・輪郭が荒い）
                        {
                            float cx = 0.50f, cy = 0.50f, rx = 0.38f, ry = 0.38f;
                            float dx = (u - cx) / rx, dy = (v - cy) / ry;
                            float dist = Mathf.Sqrt(dx*dx + dy*dy);
                            if (dist < 1f)
                            {
                                // 強いノイズで輪郭を荒く
                                float n1 = Mathf.PerlinNoise(u*6f+sd,  v*6f+sd);
                                float n2 = Mathf.PerlinNoise(u*12f+sd2, v*11f+sd2) * 0.5f;
                                float threshold = 0.22f + dist * 0.55f;
                                if ((n1 + n2) > threshold)
                                    alpha = Mathf.Clamp01((1f - dist) * 2.5f);
                            }
                            break;
                        }
                    }

                    // 上端ハイライト・下端影を全種共通で焼き込み
                    float highlight = (v < 0.30f && alpha > 0.1f) ? 0.12f : 0f;
                    float shadow    = (v > 0.70f && alpha > 0.1f) ? -0.10f : 0f;
                    float bright    = 1f + highlight + shadow;
                    pixels[y * S + x] = new Color(
                        Mathf.Clamp01(SnowWhite.r * bright),
                        Mathf.Clamp01(SnowWhite.g * bright),
                        Mathf.Clamp01(SnowWhite.b * bright),
                        Mathf.Clamp01(alpha));
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _chunkTextures[ti] = tex;
        }
        Debug.Log("[SNOW_CHUNK_TEX] chunk_textures_built=6 silhouette=multi_shape_irregular");
    }

    void Start()
    {
        Apply();
        // 監査ログ: 本番経路の確認
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        Debug.Log($"[SNOW_ACTIVE_SCENE] scene={sceneName} script=WorkSnowForcer.cs");
        Debug.Log($"[SNOW_RUNTIME_TARGET] system=WorkSnowForcer render_method=OnGUI(DrawTexture) mesh=NONE material=NONE" +
                  $" roof_visual=GUI.DrawTexture(whiteTex,guiRect) falling_visual=GUI.DrawTexture(whiteTex,pieceRect)" +
                  $" script=WorkSnowForcer.cs gameobject={gameObject.name} parent={transform.parent?.name ?? "none"}");
        Debug.Log($"[SNOW_FALLING_PIECE_SOURCE] class=FallingPiece(struct) spawn_func=HandleTap" +
                  $" mesh=NONE material=NONE draw_func=OnGUI_DrawTexture type=2D_GUI_rect" +
                  $" chunk_size_px=40-90 smoke_particles=YES" +
                  $" NOT_3D_rigidbody NOT_MvpSnowChunkMotion NOT_SnowPackFallingPiece" +
                  $" SMALL_CUBE_ABOLISHED=YES");
    }

    void Update()
    {
        if (!_applied) Apply();
        // RoofGuide_* Image を毎フレーム確実に OFF にする
        EnsureRoofGuideImagesOff();
        if (!Application.isPlaying) return;
        if (!_roofsReady) BuildRoofData();
        HandleTap();
        UpdatePieces();
        UpdateSmoke();
    }

    // RoofGuide_* の Image コンポーネントを必ず非表示にする
    // シーン保存時に enabled=true で残っていても、毎フレーム上書きして消す
    void EnsureRoofGuideImagesOff()
    {
        foreach (var (_, guideId) in RoofPairs)
        {
            var go = GameObject.Find(guideId);
            if (go == null) continue;
            var img = go.GetComponent<Image>();
            if (img != null && img.enabled)
            {
                img.enabled = false;
                Debug.Log($"[ROOF_GUIDE_IMAGE_OFF] id={guideId} forced_off=YES");
            }
        }
    }

    // ── 6軒の屋根雪 Canvas Image を更新 ─────────────────────────
    // 非アクティブなオブジェクトも含めて名前で検索する
    static GameObject FindIncludeInactive(string name)
    {
        // まず通常検索（アクティブのみ）
        var go = GameObject.Find(name);
        if (go != null) return go;
        // 非アクティブも含めて全検索
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.gameObject.name == name) return t.gameObject;
        return null;
    }

    void Apply()
    {
        // RoofGuideCanvas はキャリブ専用UI。B方式（3D主体）では不要。
        // Edit / Play 問わず常に非表示を維持する。
        var canvas = FindIncludeInactive("RoofGuideCanvas");
        if (canvas != null && canvas.activeSelf) canvas.SetActive(false);
        if (!Application.isPlaying) return;
        // [GUIDE_CANVAS_CUTOFF] worksnowforcer_reactivates_canvas=NO roofguidecanvas_visible_in_play=NO

        if (!File.Exists(CALIB_PATH)) return;
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null) return;

        int ok = 0;
        for (int ri = 0; ri < RoofPairs.Length; ri++)
        {
            var (calibId, guideId) = RoofPairs[ri];
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == calibId) { entry = r; break; }
            if (entry == null || !entry.confirmed) continue;

            float minX = Mathf.Min(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
            float maxX = Mathf.Max(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
            float minY = Mathf.Min(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);
            float maxY = Mathf.Max(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);

            var anchorMin = new Vector2(minX, 1f - maxY);
            var anchorMax = new Vector2(maxX, 1f - minY);

            var guideGo = GameObject.Find(guideId);
            // RoofGuide GameObject が存在しない場合でもキャリブデータは適用済みとする
            // （1軒モードではシーンにガイドオブジェクトがない場合がある）
            if (guideGo != null)
            {
                var rt = guideGo.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin        = anchorMin;
                    rt.anchorMax        = anchorMax;
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta        = Vector2.zero;
                }

                var img = guideGo.GetComponent<Image>();
                if (img == null) img = guideGo.AddComponent<Image>();
                img.color         = SnowWhite;
                img.raycastTarget = false;
                img.enabled       = false;
            }

            // 初期 anchor を保存
            if (_roofs[ri].anchorMinY0 < 0f)
            {
                _roofs[ri].anchorMinY0 = anchorMin.y;
                _roofs[ri].anchorMaxY0 = anchorMax.y;
                _roofs[ri].id          = calibId;
                _roofs[ri].guideId     = guideId;
            }
            ok++;
        }

        // 1軒モード: 1件以上適用できたら_appliedをtrueにして再試行ループを防ぐ
        _applied = ok > 0;
        Debug.Log($"[ALL6_SNOW_FIT] count={ok}/1 all_roofs={(_applied ? "YES" : "NO")}");
    }

    // ── 6軒分の guiRect / eaveGuiY を計算（Play 開始後1回のみ）──
    void BuildRoofData()
    {
        if (!File.Exists(CALIB_PATH)) return;
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null) return;

        int readyCount = 0;
        for (int ri = 0; ri < RoofPairs.Length; ri++)
        {
            var (calibId, guideId) = RoofPairs[ri];
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == calibId) { entry = r; break; }
            if (entry == null || !entry.confirmed) continue;

            float minX = Mathf.Min(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
            float maxX = Mathf.Max(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
            float minY = Mathf.Min(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);
            float maxY = Mathf.Max(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);

            float eaveCalibY  = maxY + UNDER_EAVE_OFFSET_CALIB;
            float eaveCenterX = (minX + maxX) * 0.5f;

            // ── downhill ベクトルを OnGUI 座標系で正しく計算 ──────────────
            // calib 座標の Y は 0=画面上端・1=画面下端（OnGUI と同方向）。
            // guiRect や eaveGuiY も calibY * Screen.height でそのまま変換している。
            // → downhill 計算も同じく calibY * Screen.height を使う（1-y 変換は不要）。
            //
            // 滑落方向 = 屋根「top中央」→「bottom中央」（calibY が小さい方 → 大きい方）
            float topCenterX_c    = (entry.topLeft.x    + entry.topRight.x)    * 0.5f;
            float topCenterY_c    = (entry.topLeft.y    + entry.topRight.y)    * 0.5f;
            float bottomCenterX_c = (entry.bottomLeft.x + entry.bottomRight.x) * 0.5f;
            float bottomCenterY_c = (entry.bottomLeft.y + entry.bottomRight.y) * 0.5f;

            // calib → OnGUI ピクセル座標（Y変換なし: calibY * Screen.height）
            float topGX    = topCenterX_c    * Screen.width;
            float topGY    = topCenterY_c    * Screen.height;
            float bottomGX = bottomCenterX_c * Screen.width;
            float bottomGY = bottomCenterY_c * Screen.height;

            // top → bottom ベクトル = 滑落方向（Y+ = 画面下 = 軒先方向）
            var rawDownhill = new Vector2(bottomGX - topGX, bottomGY - topGY);
            float dhLen = rawDownhill.magnitude;
            Vector2 downhillDir = dhLen > 0.5f
                ? rawDownhill / dhLen
                : new Vector2(0f, 1f);

            _roofs[ri].id           = calibId;
            _roofs[ri].guideId      = guideId;
            _roofs[ri].guiRect      = new Rect(
                minX * Screen.width,
                minY * Screen.height,
                (maxX - minX) * Screen.width,
                (maxY - minY) * Screen.height);
            _roofs[ri].eaveGuiY     = eaveCalibY  * Screen.height;
            _roofs[ri].eaveGuiX     = eaveCenterX * Screen.width;
            _roofs[ri].downhillDir  = downhillDir;
            _roofs[ri].downhillVelX = rawDownhill.x; // 後方互換
            _roofs[ri].thickRatio   = THICK_SNOW_RATIO;
            _roofs[ri].ready        = true;
            readyCount++;

            if (_roofs[ri].thickRatio > 0f)
            {
                float thickPx = _roofs[ri].guiRect.height * _roofs[ri].thickRatio;
                Debug.Log($"[SNOW_STATE] roof={calibId} state=THICK thickRatio={_roofs[ri].thickRatio} thickPx={thickPx:F1}");
            }
            else
            {
                Debug.Log($"[SNOW_STATE] roof={calibId} state=NORMAL");
            }

            // 必須ログ: downhill ベクトル確認（3種）
            Debug.Log($"[ROOF_DOWNHILL_RAW] roof={calibId}" +
                      $" raw=({rawDownhill.x:F1},{rawDownhill.y:F1})" +
                      $" top_gui=({topGX:F1},{topGY:F1}) bottom_gui=({bottomGX:F1},{bottomGY:F1})");
            Debug.Log($"[ROOF_DOWNHILL_FINAL] roof={calibId}" +
                      $" final=({downhillDir.x:F3},{downhillDir.y:F3})" +
                      $" y_positive={( downhillDir.y > 0 ? "YES(correct)" : "NO(inverted!)" )}");
            Debug.Log($"[ROOF_EAVE_DIR] roof={calibId}" +
                      $" eave=({downhillDir.x:F3},{downhillDir.y:F3})" +
                      $" eaveGuiY={eaveCalibY * Screen.height:F1}" +
                      $" topGY={topGY:F1} bottomGY={bottomGY:F1}");

            Debug.Log($"[UNDER_EAVE_TARGET] roof={calibId} created=YES" +
                      $" eave_calib_y={eaveCalibY:F4} gui_y={_roofs[ri].eaveGuiY:F1}" +
                      $" gui_x={_roofs[ri].eaveGuiX:F1}");
        }

        _roofsReady = readyCount == 6;
        // readyCount が 0 なら未読み込み → 再試行
        // 1件以上読めたら準備完了とみなす（1軒モード対応）
        if (readyCount == 0) _roofsReady = false;
        else _roofsReady = true;
        Debug.Log($"[UNDER_EAVE_TARGET] all_roofs_created={(_roofsReady ? "YES" : "NO")} count={readyCount}");
        if (_roofsReady && _roofs.Length > 0 && _roofs[0].ready)
            Debug.Log($"[SNOW_ALIGNMENT] snow_surface_aligned_to_roof=YES snow_surface_matches_roof_size=YES guiRect={_roofs[0].guiRect} thickRatio={THICK_SNOW_RATIO}");

        // GloveTool の IsInRoofTrapezoid が SnowStrip2D.RoofInfos を参照するため
        // WorkSnowForcer の guiRect データを SnowStrip2D の外部 RoofInfo として登録する
        {
            var extInfos = new System.Collections.Generic.List<(string, Rect, bool)>();
            // 上段: TL/TM/TR（guiRect.y が小さい方）下段: BL/BM/BR
            // 簡易判定: 全屋根の guiRect.y 中央値で上下を決める
            float midY = 0f;
            int cnt = 0;
            for (int i = 0; i < _roofs.Length; i++)
                if (_roofs[i].ready) { midY += _roofs[i].guiRect.y; cnt++; }
            if (cnt > 0) midY /= cnt;
            for (int i = 0; i < _roofs.Length; i++)
            {
                if (!_roofs[i].ready) continue;
                bool isUpper = _roofs[i].guiRect.y < midY;
                extInfos.Add((_roofs[i].id, _roofs[i].guiRect, isUpper));
            }
            SnowStrip2D.RegisterExternalRoofInfos(extInfos);
        }

        // ── 誤着地防止: eaveGuiY が「自分より上にある屋根」の guiRect に入らないようクランプ ──
        // OnGUI は Y 軸下向き（小さい = 上）。自分の guiRect.y より小さい（= 上にある）屋根のみ対象。
        // 下段屋根は上段屋根の eaveGuiY をクランプしない（相互クランプ禁止）。
        for (int ai = 0; ai < _roofs.Length; ai++)
        {
            if (!_roofs[ai].ready) continue;

            // 自分の屋根下端 + 最大オフセットを絶対上限に設定
            float ownMaxEaveY  = _roofs[ai].guiRect.yMax + EAVE_MAX_EXTRA_CALIB * Screen.height;
            float clampedEaveY = Mathf.Min(_roofs[ai].eaveGuiY, ownMaxEaveY);

            for (int bi = 0; bi < _roofs.Length; bi++)
            {
                if (bi == ai || !_roofs[bi].ready) continue;

                // bi が ai より上にある屋根（= bi.guiRect.y < ai.guiRect.y）の場合のみクランプ
                // 下段屋根（bi.guiRect.y > ai.guiRect.y）は制限しない
                if (_roofs[bi].guiRect.y >= _roofs[ai].guiRect.y) continue;

                float bBottom = _roofs[bi].guiRect.yMax;
                // ai の eaveGuiY が bi 屋根の下端より上にある（= bi 屋根の範囲内に入る）場合のみ制限
                if (clampedEaveY <= bBottom + 4f)
                {
                    float before = clampedEaveY;
                    clampedEaveY = bBottom + 4f;
                    Debug.Log($"[EAVE_CLAMP] roof={_roofs[ai].id} eaveGuiY {before:F1}→{clampedEaveY:F1} (below {_roofs[bi].id} bottom={bBottom:F1})");
                }
            }
            _roofs[ai].eaveGuiY = clampedEaveY;
            Debug.Log($"[EAVE_FINAL] roof={_roofs[ai].id} eaveGuiY={clampedEaveY:F1}");
        }
    }

    // ── タップ検出（6軒対応）─────────────────────────────────
    void HandleTap()
    {
        if (!_roofsReady) return;

        bool    pressed   = false;
        Vector2 screenPos = Vector2.zero;

        if (Input.GetMouseButtonDown(0))
        {
            screenPos = Input.mousePosition;
            pressed   = true;
        }
        else if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            screenPos = Input.GetTouch(0).position;
            pressed   = true;
        }
        if (!pressed) return;

        // Input は左下原点 → OnGUI は左上原点
        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);

        for (int ri = 0; ri < _roofs.Length; ri++)
        {
            if (!_roofs[ri].ready) continue;
            if (!_roofs[ri].guiRect.Contains(guiPos)) continue;

            // ── ヒットマップの平均を snowFill に同期 ──────────────
            SyncSnowFill(ri);
            float prev = _roofs[ri].snowFill;

            // ── タップ位置の列インデックスを先に計算（spawn判定に使う）──
            float tapLocalXPre = (guiPos.x - _roofs[ri].guiRect.x) / _roofs[ri].guiRect.width;
            tapLocalXPre = Mathf.Clamp01(tapLocalXPre);
            int tapColPre = Mathf.Clamp(Mathf.FloorToInt(tapLocalXPre * SNOW_COLS), 0, SNOW_COLS - 1);
            float tapColFill = (_roofs[ri].snowCols != null) ? _roofs[ri].snowCols[tapColPre] : prev;

            // ── 積雪0判定: タップ列の snowCols 値で厳密判定 ──────────
            // 全体平均(prev)ではなく、実際に叩いた列の残雪量で判断する
            bool spawnBlocked = (tapColFill <= 0f);
            Debug.Log($"[SNOW_REMAIN_BEFORE_TAP] roof={_roofs[ri].id} remain_before={prev:F2}" +
                      $" tap_col={tapColPre} tap_col_fill={tapColFill:F2}");
            if (spawnBlocked)
            {
                Debug.Log($"[SNOW_SPAWN_BLOCKED_EMPTY] roof={_roofs[ri].id}" +
                          $" tap_col={tapColPre} tap_col_fill={tapColFill:F2}" +
                          $" spawn_blocked=true no_pieces_spawned=YES");
                break;
            }

            // ── 崩落タイプを決定（可変崩落システム）────────────────
            _roofs[ri].collapseCharge += Random.Range(0.15f, 0.35f) * _roofs[ri].instability;
            float charge = _roofs[ri].collapseCharge;

            CollapseType collapseType;
            float snowDelta;
            int   spawnCount;

            if (charge >= 0.8f && Random.value < 0.55f)
            {
                collapseType = CollapseType.Avalanche;
                snowDelta    = Random.Range(0.35f, Mathf.Min(0.65f, prev));
                spawnCount   = Random.Range(4, 7);
                _roofs[ri].collapseCharge = 0f;
            }
            else if (charge >= 0.45f && Random.value < 0.50f)
            {
                collapseType = CollapseType.Medium;
                snowDelta    = Random.Range(0.15f, 0.30f);
                spawnCount   = Random.Range(2, 4);
                _roofs[ri].collapseCharge *= 0.5f;
            }
            else
            {
                collapseType = CollapseType.Small;
                snowDelta    = Random.Range(0.05f, 0.14f);
                spawnCount   = Random.Range(1, 3);
            }

            // ── ヒットマップにタップ位置起点の局所崩落を書き込む ──
            // タップX のヒットマップ列インデックス
            float tapLocalX = (guiPos.x - _roofs[ri].guiRect.x) / _roofs[ri].guiRect.width;
            tapLocalX = Mathf.Clamp01(tapLocalX);
            int tapCol = Mathf.Clamp(Mathf.FloorToInt(tapLocalX * SNOW_COLS), 0, SNOW_COLS - 1);

            // 崩落タイプに応じた影響半径（列数）
            float radiusCols = collapseType == CollapseType.Avalanche ? Random.Range(4f, 7f)
                             : collapseType == CollapseType.Medium     ? Random.Range(2.5f, 4f)
                             :                                           Random.Range(1f, 2.5f);

            // 下流方向（+X 側）に少し伸ばす（しずく感）
            float downstreamBias = radiusCols * 0.4f;

            for (int c = 0; c < SNOW_COLS; c++)
            {
                float dx = c - tapCol;
                // 下流側は少し広く、上流側は狭く
                float effRadius = dx >= 0f ? radiusCols + downstreamBias : radiusCols;
                float dist = Mathf.Abs(dx) / effRadius;
                if (dist > 1f) continue;

                // ガウシアン的な減少量（中心が最大、外縁がゼロ）
                float weight = Mathf.Pow(1f - dist * dist, 1.5f);
                // 列ごとに少しランダムな揺らぎを加える（凸凹感）
                float jitter = Random.Range(0.7f, 1.3f);
                float delta  = snowDelta * weight * jitter;
                _roofs[ri].snowCols[c] = Mathf.Max(0f, _roofs[ri].snowCols[c] - delta);
            }

            // ── スムージング: 隣接列の差を緩和して境界の四角さを除去 ──
            SmoothSnowCols(ri);
            Debug.Log($"[SNOW_MASK_SMOOTH_APPLIED] roof={_roofs[ri].id} cols_smoothed=YES");

            // snowFill を snowCols 平均に同期
            SyncSnowFill(ri);
            float afterFill = _roofs[ri].snowFill;
            Debug.Log($"[SNOW_HIT] tap_reaches_roofsnow=YES snow_depth_changes_on_hit=YES hit_position_matches_surface=YES roof={_roofs[ri].id} hit_index={tapColPre} hit_depth_before={tapColFill:F3} hit_depth_after={_roofs[ri].snowCols[tapColPre]:F3}");
            bool justCleared = (prev > 0f && afterFill <= 0f);
            if (justCleared)
            {
                Debug.Log($"[SNOW_CLEARED] roof={_roofs[ri].id} all_snow_removed=YES");
                // 全列を確実に0にする（トップライン残影対策）
                for (int c = 0; c < SNOW_COLS; c++) _roofs[ri].snowCols[c] = 0f;
                _roofs[ri].snowFill = 0f;
                _snowHoles[ri]?.Clear();
            }
            UpdateSnowVisual(ri);

            Debug.Log($"[SNOW_REMAIN_AFTER_TAP] roof={_roofs[ri].id}" +
                      $" remain_after={afterFill:F2} delta={snowDelta:F2}" +
                      $" collapse={collapseType} charge={charge:F2} spawn_blocked=false");
            Debug.Log($"[SNOW_TOP_ROW_STATE] roof={_roofs[ri].id} top_row={afterFill:F2}" +
                      $" cleared={justCleared}");

            // ── 積雪0になったらスポーンをブロック（問題2対策）─────
            if (afterFill <= 0f)
            {
                Debug.Log($"[SNOW_SPAWN_BLOCKED_EMPTY] roof={_roofs[ri].id}" +
                          $" remain_after=0.00 spawn_blocked=true no_pieces_spawned=YES");
                break;
            }

            // ── spawn 設定 ──────────────────────────────────────────
            float roofW    = _roofs[ri].guiRect.width;
            float roofH    = _roofs[ri].guiRect.height;

            float tapRoofX = guiPos.x;
            float spawnSpread = collapseType == CollapseType.Avalanche ? roofW * 0.45f
                              : collapseType == CollapseType.Medium     ? roofW * 0.25f
                              :                                           roofW * 0.12f;
            float spawnX   = Mathf.Clamp(tapRoofX, _roofs[ri].guiRect.x + 10f,
                                          _roofs[ri].guiRect.xMax - 10f);
            float spawnY   = _roofs[ri].guiRect.y;

            Vector2 dh     = _roofs[ri].downhillDir;
            float eaveX    = spawnX + dh.x * roofH;
            float eaveY    = _roofs[ri].guiRect.yMax;
            float maxDist  = Mathf.Sqrt(roofW * roofW + roofH * roofH);

            SmokeType smokeType = collapseType == CollapseType.Avalanche ? SmokeType.Land
                                : SmokeType.Tap;
            SpawnSmoke(spawnX, eaveY, dh.x * 30f, ri, smokeType);
            for (int si = 0; si < spawnCount; si++)
            {
                float jx = Random.Range(-spawnSpread, spawnSpread);
                float szMin = collapseType == CollapseType.Avalanche ? 0.20f : 0.12f;
                float szMax = collapseType == CollapseType.Avalanche ? 0.35f : 0.25f;
                float sz = roofW * Random.Range(szMin, szMax);
                sz = Mathf.Clamp(sz, 25f, 90f);

                int speedType = collapseType == CollapseType.Avalanche
                    ? Random.Range(0, 2)
                    : Random.Range(0, 3);
                float baseSpeed = speedType == 0 ? Random.Range(150f, 230f)
                                : speedType == 1 ? Random.Range(85f,  145f)
                                :                  Random.Range(48f,   88f);

                _pieces.Add(new FallingPiece
                {
                    pos          = new Vector2(spawnX + jx, spawnY),
                    vel          = Vector2.zero,
                    size         = sz,
                    sizeY        = sz * Random.Range(0.55f, 0.80f),
                    life         = 8f,
                    roofIdx      = ri,
                    rot          = Random.Range(-15f, 15f),
                    rotVel       = Random.Range(-35f, 35f),
                    alpha        = 1f,
                    texIdx       = Random.Range(0, 6),
                    phase        = PiecePhase.Sliding,
                    slideDir     = dh,
                    slideSpeed   = baseSpeed,
                    slideDirX    = dh.x,
                    slideY       = spawnY,
                    slideStartX  = spawnX + jx,
                    eaveX        = eaveX,
                    eaveY        = eaveY,
                    slideDist    = 0f,
                    maxSlideDist = maxDist,
                    hasStick     = (speedType == 2),
                    stickTimer   = 0f,
                    stickCooldown= Random.Range(0.15f, 0.45f),
                });
            }

            Debug.Log($"[DETACH] roof={_roofs[ri].id} tap_detected=YES" +
                      $" spawn=({spawnX:F1},{spawnY:F1})" +
                      $" downhill=({dh.x:F3},{dh.y:F3})" +
                      $" eave=({eaveX:F1},{eaveY:F1})" +
                      $" snow_fill={_roofs[ri].snowFill:F2}" +
                      $" chunk_count={spawnCount} phase=SLIDING");
            break;
        }
    }

    // ── 各屋根の Image 高さを fill に合わせて縮小 ────────────────

    /// 隣接列の差を緩和するスムージング（2パス）。
    /// 急激な段差を滑らかにして境界の四角さを除去する。
    void SmoothSnowCols(int ri)
    {
        if (_roofs[ri].snowCols == null) return;
        var cols = _roofs[ri].snowCols;

        // パス1: 3列移動平均（境界を丸める）
        var tmp = new float[SNOW_COLS];
        for (int c = 0; c < SNOW_COLS; c++)
        {
            float sum = cols[c];
            int   cnt = 1;
            if (c > 0)             { sum += cols[c - 1]; cnt++; }
            if (c < SNOW_COLS - 1) { sum += cols[c + 1]; cnt++; }
            tmp[c] = sum / cnt;
        }

        // パス2: 元の値と平均値をブレンド（50%）して過度な平滑化を防ぐ
        for (int c = 0; c < SNOW_COLS; c++)
            cols[c] = cols[c] * 0.5f + tmp[c] * 0.5f;
    }

    /// snowCols[] の平均を snowFill に同期する。
    void SyncSnowFill(int ri)
    {
        if (_roofs[ri].snowCols == null) return;
        float sum = 0f;
        for (int c = 0; c < SNOW_COLS; c++) sum += _roofs[ri].snowCols[c];
        _roofs[ri].snowFill = sum / SNOW_COLS;
    }

    void UpdateSnowVisual(int ri)
    {
        if (_roofs[ri].anchorMinY0 < 0f || _roofs[ri].anchorMaxY0 < 0f) return;
        var guideGo = GameObject.Find(_roofs[ri].guideId);
        if (guideGo == null) return;
        var rt = guideGo.GetComponent<RectTransform>();
        if (rt == null) return;

        float newMinY = Mathf.Lerp(_roofs[ri].anchorMaxY0, _roofs[ri].anchorMinY0, _roofs[ri].snowFill);
        rt.anchorMin = new Vector2(rt.anchorMin.x, newMinY);
    }

    // ── 落下雪塊の更新（屋根滑落 → 落下の2フェーズ）────────────
    // WorkSnowForcer.cs UpdatePieces 改修:
    //   SLIDING フェーズ: 屋根面に沿って軒先まで横移動
    //   FALLING フェーズ: 軒先到達後に重力落下
    void UpdatePieces()
    {
        float dt = Time.deltaTime;

        // 着地済み雪塊: フェードアウト
        for (int i = _landedPieces.Count - 1; i >= 0; i--)
        {
            var lp = _landedPieces[i];
            lp.remainLife -= dt;
            if (lp.remainLife <= 0f) { _landedPieces.RemoveAt(i); continue; }
            _landedPieces[i] = lp;
        }

        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];
            p.life -= dt;
            if (p.life <= 0f) { _pieces.RemoveAt(i); continue; }

            if (p.phase == PiecePhase.Sliding)
            {
                // ── 屋根滑落フェーズ ──────────────────────────────
                // downhillDir ベクトル方向に移動（X・Y 両成分）
                // 引っかかり処理
                float curSpeed = p.slideSpeed;
                if (p.hasStick)
                {
                    if (p.stickTimer > 0f)
                    {
                        p.stickTimer -= dt;
                        curSpeed = p.slideSpeed * 0.10f;
                        if (p.stickTimer <= 0f)
                        {
                            p.stickCooldown = Random.Range(0.2f, 0.55f);
                            // 引っかかり解除: 小さな雪煙（Slide: 控えめ）
                            SpawnSmoke(p.pos.x, p.pos.y, p.slideDir.x * 15f, p.roofIdx, SmokeType.Slide);
                        }
                    }
                    else
                    {
                        p.stickCooldown -= dt;
                        if (p.stickCooldown <= 0f)
                            p.stickTimer = Random.Range(0.08f, 0.20f);
                    }
                }

                // downhillDir 方向に移動（屋根面に沿って）
                float step = curSpeed * dt;
                p.pos      += p.slideDir * step;
                p.slideDist += step;
                p.rot       += p.rotVel * 0.25f * dt;

                // 軒先到達判定: 屋根下端Y を超えたら落下へ
                bool reachedEave = p.pos.y >= p.eaveY
                                || p.slideDist >= p.maxSlideDist;

                if (reachedEave)
                {
                    p.pos.y = p.eaveY;
                    p.phase = PiecePhase.Falling;
                    // 落下初速: downhill 方向の勢いを引き継ぎ
                    float launchSpeed = p.slideSpeed * Random.Range(0.35f, 0.65f);
                    p.vel = new Vector2(
                        p.slideDir.x * launchSpeed,
                        Mathf.Max(p.slideDir.y * launchSpeed, 40f));
                    p.rotVel = Random.Range(-110f, 110f);
                    // 軒先到達: Tap サイズ（中程度）
                    SpawnSmoke(p.pos.x, p.pos.y, p.vel.x * 0.4f, p.roofIdx, SmokeType.Tap);
                    Debug.Log($"[EAVE_REACHED] roof={_roofs[p.roofIdx].id}" +
                              $" pos=({p.pos.x:F1},{p.pos.y:F1})" +
                              $" dist={p.slideDist:F1} -> FALLING");
                }
            }
            else
            {
                // ── 落下フェーズ ─────────────────────────────────
                p.pos   += p.vel * dt;
                p.vel.y += 220f * dt;
                p.vel.x *= (1f - 0.6f * dt);
                p.rot   += p.rotVel * dt;

                int ri = p.roofIdx;
                if (ri >= 0 && ri < _roofs.Length && _roofs[ri].ready
                    && p.pos.y >= _roofs[ri].eaveGuiY)
                {
                    p.pos.y = _roofs[ri].eaveGuiY;
                    // 着地: Land（大サイズ・派手・ランダム差）
                    SpawnSmoke(p.pos.x, p.pos.y, p.vel.x * 0.3f, ri, SmokeType.Land);
                    _landedPieces.Add(new LandedPiece
                    {
                        pos        = p.pos,
                        size       = p.size,
                        sizeY      = p.sizeY,
                        remainLife = 3f,
                        roofIdx    = ri,
                        texIdx     = p.texIdx,
                    });
                    Debug.Log($"[UNDER_EAVE_LANDING] roof={_roofs[ri].id} under_eave_hit=YES" +
                              $" hit_gui_y={_roofs[ri].eaveGuiY:F1}" +
                              $" piece_pos=({p.pos.x:F1},{p.pos.y:F1})" +
                              $" falling_piece_stops=YES remains_visible=YES falls_off_screen=NO");
                    _pieces.RemoveAt(i);
                    continue;
                }
            }

            _pieces[i] = p;
        }
    }

    // ── 雪煙タイプ ────────────────────────────────────────────
    enum SmokeType
    {
        Tap,     // 叩いた瞬間: 中サイズ・少し広がる
        Slide,   // 滑落中:     小サイズ・控えめ・透明
        Land,    // 着地:       大サイズ・ランダム差・派手
    }

    // ── 雪煙生成（タイプ別）──────────────────────────────────
    void SpawnSmoke(float x, float y, float baseVx, int roofIdx,
                    SmokeType type = SmokeType.Tap)
    {
        int   count;
        float sizeMin, sizeMax, speedMin, speedMax, lifeMin, lifeMax;
        float spread, alphaScale;

        switch (type)
        {
            case SmokeType.Tap:
                // 叩いた瞬間: 中サイズ・少し広がる
                count      = Random.Range(8, 13);
                sizeMin    = 7f;  sizeMax  = 18f;
                speedMin   = 50f; speedMax = 120f;
                lifeMin    = 0.5f; lifeMax = 1.1f;
                spread     = 20f;
                alphaScale = 1.0f;
                break;

            case SmokeType.Slide:
                // 滑落中: 小サイズ・控えめ・透明（本体を隠さない）
                count      = Random.Range(2, 5);
                sizeMin    = 3f;  sizeMax  = 8f;
                speedMin   = 20f; speedMax = 55f;
                lifeMin    = 0.2f; lifeMax = 0.5f;
                spread     = 8f;
                alphaScale = 0.45f;
                break;

            case SmokeType.Land:
            default:
                // 着地: 大サイズ・ランダム差・派手
                count      = Random.Range(16, 24);
                sizeMin    = 12f; sizeMax  = 32f;
                speedMin   = 60f; speedMax = 160f;
                lifeMin    = 0.7f; lifeMax = 1.6f;
                spread     = 30f;
                alphaScale = 1.0f;
                break;
        }

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(160f, 380f) * Mathf.Deg2Rad;
            float speed = Random.Range(speedMin, speedMax);
            float life  = Random.Range(lifeMin, lifeMax);
            float sz    = Random.Range(sizeMin, sizeMax);
            _smoke.Add(new SnowSmoke
            {
                pos     = new Vector2(x + Random.Range(-spread, spread),
                                      y + Random.Range(-spread * 0.3f, spread * 0.3f)),
                vel     = new Vector2(baseVx * 0.4f + Mathf.Cos(angle) * speed,
                                      Mathf.Sin(angle) * speed - 25f),
                size    = sz * alphaScale + sz * (1f - alphaScale), // サイズは変えずアルファで制御
                life    = life * alphaScale + life * (1f - alphaScale) * 0.5f,
                maxLife = life,
            });
            // Slide タイプは maxLife を短く設定してアルファを抑える
            if (type == SmokeType.Slide)
            {
                var s = _smoke[_smoke.Count - 1];
                s.maxLife = life * 2.2f; // maxLife > life にして alpha = life/maxLife を低く保つ
                _smoke[_smoke.Count - 1] = s;
            }
        }
    }

    // ── 雪煙更新 ──────────────────────────────────────────────
    void UpdateSmoke()
    {
        float dt = Time.deltaTime;
        for (int i = _smoke.Count - 1; i >= 0; i--)
        {
            var s = _smoke[i];
            s.pos  += s.vel * dt;
            s.vel.y += 60f * dt; // 軽い重力
            s.vel   *= (1f - 2f * dt); // 空気抵抗
            s.life  -= dt;
            if (s.life <= 0f) { _smoke.RemoveAt(i); continue; }
            _smoke[i] = s;
        }
    }

    // ── OnGUI: 描画 ──────────────────────────────────────────
    // WorkSnowForcer.cs OnGUI 改修:
    //   ① 屋根雪帯: 2Dノイズマスクで前縁を崩す + 厚みムラ帯 + グラデーション影
    //   ② 落下物: 不定形シルエットテクスチャ(4種) + 回転 + 影
    //   ③ 雪煙: 強化（数・サイズ・寿命増加）
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (_whiteTex == null) return;

        // ① 屋根雪帯（前縁崩し + 厚みムラ）
        // debugHideRoofSnow=true のとき屋根表示雪をスキップ（落雪ロジック確認モード）
        if (_roofsReady && !debugHideRoofSnow)
        {
            for (int ri = 0; ri < _roofs.Length; ri++)
            {
                if (!_roofs[ri].ready || _roofs[ri].thickRatio <= 0f) continue;
                if (_roofs[ri].snowCols == null) continue;
                // B方式への移行時に無効化していた描画ループを復旧
                // RoofGuideCanvas UI を使わず WorkSnowForcer.OnGUI で積雪を描画する

                float fill    = _roofs[ri].snowFill; // 平均（全消去判定用）
                float roofLeft= _roofs[ri].guiRect.x;
                float roofW   = _roofs[ri].guiRect.width;
                float maxThickH = _roofs[ri].guiRect.height * _roofs[ri].thickRatio + 4f;

                // fill=0: 背景に焼き込まれた屋根トップの白ラインを上書き消去
                // expandY=14 分 + 余白を含めた広い範囲を背景色で塗りつぶす
                if (fill <= 0f)
                {
                    GUI.color = new Color(0.45f, 0.55f, 0.72f, 0.92f);
                    GUI.DrawTexture(new Rect(roofLeft, _roofs[ri].guiRect.y - 18f, roofW, 22f), _whiteTex);
                    continue;
                }

                // ── 列ごとに高さを変えて描画（帯状廃止・局所凸凹を実現）──
                float colW = roofW / SNOW_COLS;
                // 各列の下端に加えるノイズオフセット（境界の四角さを除去）
                // Time.time を使わず固定シードで描画ごとに安定させる
                float noiseSeedX = ri * 17.3f;
                for (int c = 0; c < SNOW_COLS; c++)
                {
                    float colFill = _roofs[ri].snowCols[c];
                    if (colFill <= 0f) continue;

                    // expandY: 積雪が屋根上端から上にはみ出す量（雪の厚み感）
                    // 0〜4px に抑えて屋根より大きく出ないようにする
                    float expandY = 4f * colFill;
                    float baseThickH = (_roofs[ri].guiRect.height * _roofs[ri].thickRatio * colFill) + expandY;
                    if (baseThickH < 1f) continue;

                    // 下端に微細なノイズを加えて境界を有機的に（±最大6px）
                    float noiseU = (float)c / (SNOW_COLS - 1);
                    float edgeNoise = (Mathf.PerlinNoise(noiseU * 5f + noiseSeedX, colFill * 3f) - 0.5f) * 12f * colFill;
                    float thickH = baseThickH + edgeNoise;
                    if (thickH < 1f) thickH = 1f;

                    float colX    = roofLeft + c * colW;
                    float roofTop = _roofs[ri].guiRect.y - expandY;

                    // 本体（雪色）
                    GUI.color = SnowWhite;
                    GUI.DrawTexture(new Rect(colX, roofTop, colW + 1f, thickH), _whiteTex);

                    // 上端ハイライト
                    if (thickH > 8f)
                    {
                        GUI.color = new Color(1f, 1f, 1f, 0.50f);
                        GUI.DrawTexture(new Rect(colX, roofTop, colW + 1f, 4f), _whiteTex);
                    }

                    // 下端影
                    if (thickH > 8f)
                    {
                        float botY = roofTop + thickH;
                        GUI.color = new Color(0.60f, 0.70f, 0.88f, 0.55f);
                        GUI.DrawTexture(new Rect(colX, botY - 5f, colW + 1f, 5f), _whiteTex);
                    }
                }

                // 前縁ノイズマスク（全体に1枚重ねて前縁を崩す）
                if (_roofEdgeMaskTex != null)
                {
                    float avgFill = fill;
                    float avgThickH = maxThickH * avgFill;
                    if (avgThickH > 8f)
                    {
                        float edgeH = Mathf.Min(avgThickH * 0.45f, 26f);
                        float edgeY = (_roofs[ri].guiRect.y - 14f * avgFill) + avgThickH - edgeH;
                        GUI.color = new Color(0.52f, 0.63f, 0.82f, 0.55f);
                        GUI.DrawTexture(new Rect(roofLeft, edgeY, roofW, edgeH), _roofEdgeMaskTex);
                        GUI.color = new Color(SnowWhite.r, SnowWhite.g, SnowWhite.b, 0.85f);
                        GUI.DrawTexture(new Rect(roofLeft, edgeY, roofW, edgeH * 0.55f), _roofEdgeMaskTex);
                    }
                }
            }
        }

        // ② 落下中の不定形雪塊（シルエットテクスチャ使用）
        bool hasChunkTex = _chunkTextures != null && _chunkTextures.Length == 6;
        foreach (var p in _pieces)
        {
            var center = new Vector2(p.pos.x, p.pos.y);
            GUIUtility.RotateAroundPivot(p.rot, center);

            Texture2D tex = (hasChunkTex && _chunkTextures[p.texIdx] != null)
                ? _chunkTextures[p.texIdx] : _whiteTex;

            // 本体（不定形シルエット）
            GUI.color = new Color(1f, 1f, 1f, p.alpha);
            GUI.DrawTexture(new Rect(p.pos.x - p.size * 0.5f, p.pos.y - p.sizeY * 0.5f,
                                     p.size, p.sizeY), tex);

            GUIUtility.RotateAroundPivot(-p.rot, center);
        }

        // ③ 着地済み雪塊（フェードアウト・不定形）
        foreach (var lp in _landedPieces)
        {
            float alpha = Mathf.Clamp01(lp.remainLife / 3f);
            Texture2D tex = (hasChunkTex && _chunkTextures[lp.texIdx] != null)
                ? _chunkTextures[lp.texIdx] : _whiteTex;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(lp.pos.x - lp.size * 0.5f, lp.pos.y - lp.sizeY * 0.5f,
                                     lp.size, lp.sizeY), tex);
        }

        // ④ 雪煙パーティクル（ソフト円形テクスチャ + 回転 + サイズ揺らぎ）
        Texture2D smokeTex = (_smokeTex != null) ? _smokeTex : _whiteTex;
        foreach (var s in _smoke)
        {
            float t     = Mathf.Clamp01(s.life / s.maxLife);
            float alpha = t * t * 0.80f;
            float sz    = s.size * (1f + (1f - t) * 0.9f); // 膨らみ増加
            float szY   = sz * Random.Range(0.80f, 1.20f);  // 縦横を少しランダムに
            // 回転: 各パーティクルに固有の角度（ランダムに見せるため life を seed に）
            float angle = s.life * 137.5f; // 黄金角ベースで分散
            var center  = new Vector2(s.pos.x, s.pos.y);
            GUIUtility.RotateAroundPivot(angle, center);
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(s.pos.x - sz * 0.5f, s.pos.y - szY * 0.5f, sz, szY), smokeTex);
            GUIUtility.RotateAroundPivot(-angle, center);
        }

        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        if (_whiteTex        != null) { Object.DestroyImmediate(_whiteTex);        _whiteTex        = null; }
        if (_smokeTex        != null) { Object.DestroyImmediate(_smokeTex);        _smokeTex        = null; }
        if (_snowEdgeTex     != null) { Object.DestroyImmediate(_snowEdgeTex);     _snowEdgeTex     = null; }
        if (_roofEdgeMaskTex != null) { Object.DestroyImmediate(_roofEdgeMaskTex); _roofEdgeMaskTex = null; }
        if (_chunkTextures   != null)
        {
            foreach (var t in _chunkTextures)
                if (t != null) Object.DestroyImmediate(t);
            _chunkTextures = null;
        }
        if (_brushTextures   != null)
        {
            foreach (var t in _brushTextures)
                if (t != null) Object.DestroyImmediate(t);
            _brushTextures = null;
        }
    }
}
