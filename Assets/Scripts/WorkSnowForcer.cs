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
    const float THICK_SNOW_RATIO = 0.65f;

    static readonly (string calibId, string guideId)[] RoofPairs =
    {
        ("Roof_TL", "RoofGuide_TL"),
        ("Roof_TM", "RoofGuide_TM"),
        ("Roof_TR", "RoofGuide_TR"),
        ("Roof_BL", "RoofGuide_BL"),
        ("Roof_BM", "RoofGuide_BM"),
        ("Roof_BR", "RoofGuide_BR"),
    };

    static readonly Color SnowWhite = new Color(0.92f, 0.95f, 1f);

    // ── 屋根ごとのデータ ──────────────────────────────────────
    struct RoofData
    {
        public string  id;
        public string  guideId;
        public Rect    guiRect;       // OnGUI bbox（左上原点）
        public float   eaveGuiY;      // 軒下着地 Y（OnGUI）
        public float   eaveGuiX;      // 軒下着地 X 中央（OnGUI）
        public float   snowFill;      // 0〜1
        public float   anchorMinY0;   // 初期 anchorMin.y
        public float   anchorMaxY0;   // 初期 anchorMax.y
        // GDD Snow State: thickRatio > 0 なら THICK 状態（ID ハードコード禁止）
        // 0 = NORMAL（厚雪帯なし）、0.65 = THICK（屋根高さの65%分の帯）
        public float   thickRatio;
        // downhill 方向の X 成分（片流れ屋根の手前方向、OnGUI 座標系）
        // 正 = 右方向、負 = 左方向。calib の top→bottom X 差から算出
        public float   downhillVelX;
        public bool    ready;
    }
    RoofData[] _roofs = new RoofData[6];

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
        public float      eaveX;       // 軒先X座標（ここまで滑ったら落下へ）
        public float      slideSpeed;  // 滑落速度（px/s）
        public float      slideDirX;   // 滑落方向 (+1 or -1)
        public float      slideY;      // 滑落中の固定Y（屋根下端）

        // 引っかかり制御
        public float      stickTimer;  // 残り引っかかり時間（>0 なら減速中）
        public float      stickCooldown; // 次の引っかかりまでの待機時間
        public bool       hasStick;    // 引っかかりタイプか
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
        if (bgGo != null)
            bgGo.AddComponent<WorkSnowForcer>();
        else
        {
            var go = new GameObject("WorkSnowForcer_Root");
            go.AddComponent<WorkSnowForcer>();
        }
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
            _roofs[i].snowFill    = 1f;
            _roofs[i].anchorMinY0 = -1f;
            _roofs[i].anchorMaxY0 = -1f;
            _roofs[i].ready       = false;
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
    void BuildChunkTextures()
    {
        const int S = 32;
        if (_chunkTextures != null)
            foreach (var t in _chunkTextures)
                if (t != null) Object.DestroyImmediate(t);
        _chunkTextures = new Texture2D[4];

        // 4種類のシード（毎回ランダム）
        float[] seeds = {
            Random.Range(0f, 100f),
            Random.Range(0f, 100f),
            Random.Range(0f, 100f),
            Random.Range(0f, 100f),
        };

        for (int ti = 0; ti < 4; ti++)
        {
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[S * S];
            float sd = seeds[ti];

            // 種類ごとに形状パラメータを変える
            float cx = 0.5f + (ti % 2 == 0 ? -0.06f : 0.06f); // 中心X を左右にずらす
            float cy = 0.5f + (ti < 2     ? -0.05f : 0.05f);  // 中心Y を上下にずらす
            float rx = 0.38f + ti * 0.02f;  // X 半径（種類ごとに微変化）
            float ry = 0.28f + ti * 0.015f; // Y 半径（縦を潰す）

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float u = (float)x / (S - 1);
                    float v = (float)y / (S - 1);

                    // 楕円距離（中心からの正規化距離）
                    float dx = (u - cx) / rx;
                    float dy = (v - cy) / ry;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // 楕円内部のみ描画
                    if (dist > 1.0f) { pixels[y * S + x] = new Color(1, 1, 1, 0); continue; }

                    // 輪郭に向かってフェードアウト（角丸効果）
                    float edgeFade = Mathf.Clamp01((1f - dist) * 3f);

                    // 低周波ノイズ: 大きな欠け・凹み
                    float n1 = Mathf.PerlinNoise(u * 4f + sd, v * 4f + sd * 0.7f);
                    // 中周波ノイズ: 中程度の凹凸
                    float n2 = Mathf.PerlinNoise(u * 8f + sd * 1.3f, v * 8f + sd * 0.5f) * 0.5f;

                    // 輪郭付近ほどノイズで欠ける（内部は安定）
                    float noiseThreshold = 0.30f + dist * 0.45f;
                    bool inShape = (n1 + n2 * 0.5f) > noiseThreshold;

                    float alpha = inShape ? edgeFade : 0f;

                    // 上端に軽いハイライト
                    float highlight = (v < 0.35f && dist < 0.7f) ? 0.15f : 0f;
                    // 下端に影
                    float shadow = (v > 0.65f && dist < 0.8f) ? -0.12f : 0f;

                    float bright = 1f + highlight + shadow;
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
        Debug.Log("[SNOW_CHUNK_TEX] chunk_textures_built=4 silhouette=irregular_ellipse_with_noise");
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
        if (!Application.isPlaying) return;
        if (!_roofsReady) BuildRoofData();
        HandleTap();
        UpdatePieces();
        UpdateSmoke();
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
        // Edit モードでは RoofGuideCanvas を非表示にして白板が見えないようにする
        // Play 中のみ Canvas を表示・操作する
        // ※ RoofGuideCanvas は m_IsActive:0 で保存されているため FindIncludeInactive を使う
        var canvas = FindIncludeInactive("RoofGuideCanvas");
        if (!Application.isPlaying)
        {
            if (canvas != null && canvas.activeSelf) canvas.SetActive(false);
            return;
        }
        if (canvas != null && !canvas.activeSelf) canvas.SetActive(true);

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
            if (guideGo == null) continue;
            var rt = guideGo.GetComponent<RectTransform>();
            if (rt == null) continue;

            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = Vector2.zero;

            var img = guideGo.GetComponent<Image>();
            if (img == null) img = guideGo.AddComponent<Image>();
            // 全6軒に白板表示（ALL_6_THICK_SNOW モード）-> 2系統をまとめるため非表示に
            img.color         = SnowWhite;
            img.raycastTarget = false;
            img.enabled       = false;

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

        _applied = ok == 6;
        Debug.Log($"[ALL6_SNOW_FIT] count={ok}/6 all_6={(_applied ? "YES" : "NO")}");
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

            // downhill 方向: 片流れ屋根の top 中央 → bottom 中央 の X 差分
            // calib 座標で topCenter.x と bottomCenter.x を比較し、OnGUI スケールで速度化
            float topCenterX    = (entry.topLeft.x    + entry.topRight.x)    * 0.5f;
            float bottomCenterX = (entry.bottomLeft.x + entry.bottomRight.x) * 0.5f;
            // downhill X 方向（正=右、負=左）。屋根幅に対する比率 × 基準速度
            float downhillDx    = (bottomCenterX - topCenterX) * Screen.width;

            _roofs[ri].id           = calibId;
            _roofs[ri].guideId      = guideId;
            _roofs[ri].guiRect      = new Rect(
                minX * Screen.width,
                minY * Screen.height,
                (maxX - minX) * Screen.width,
                (maxY - minY) * Screen.height);
            _roofs[ri].eaveGuiY     = eaveCalibY  * Screen.height;
            _roofs[ri].eaveGuiX     = eaveCenterX * Screen.width;
            // downhill 速度 X: 屋根の傾斜方向に初速を与える（真下落下禁止）
            // downhillDx が小さい場合は最低限の横速度を保証
            _roofs[ri].downhillVelX = (Mathf.Abs(downhillDx) > 5f) ? downhillDx * 0.8f : 20f;
            // GDD Snow State: 全6軒 THICK（ID ハードコード禁止、パラメータで制御）
            _roofs[ri].thickRatio   = THICK_SNOW_RATIO;
            _roofs[ri].ready      = true;
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

            Debug.Log($"[UNDER_EAVE_TARGET] roof={calibId} created=YES" +
                      $" eave_calib_y={eaveCalibY:F4} gui_y={_roofs[ri].eaveGuiY:F1}" +
                      $" gui_x={_roofs[ri].eaveGuiX:F1}");
        }

        _roofsReady = readyCount == 6;
        Debug.Log($"[UNDER_EAVE_TARGET] all_6_targets_created={(_roofsReady ? "YES" : "NO")} count={readyCount}");

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

            // 屋根雪を減らす
            _roofs[ri].snowFill = Mathf.Max(0f, _roofs[ri].snowFill - 0.15f);
            UpdateSnowVisual(ri);

            // spawn 位置: タップ位置に近い屋根上端から開始（滑落スタート地点）
            float roofW    = _roofs[ri].guiRect.width;
            float roofLeft = _roofs[ri].guiRect.x;
            float roofRight= _roofs[ri].guiRect.xMax;
            float slideY   = _roofs[ri].guiRect.y + _roofs[ri].guiRect.height; // 屋根下端（軒先ライン）

            // downhill 方向（+1=右, -1=左）
            float dvx      = _roofs[ri].downhillVelX;
            float slideDir = dvx >= 0f ? 1f : -1f;
            // 軒先X: downhill 方向の端
            float eaveX    = slideDir > 0f ? roofRight : roofLeft;

            // 雪煙（detach 瞬間）
            float spawnX = _roofs[ri].guiRect.x + _roofs[ri].guiRect.width * 0.5f;
            SpawnSmoke(spawnX, slideY, dvx * 0.3f, ri);

            // 大きめ雪塊を2〜4個生成（屋根滑落フェーズ付き）
            int spawnCount = Random.Range(2, 5);
            for (int si = 0; si < spawnCount; si++)
            {
                // スポーン X: 屋根中央付近（滑落方向の反対側寄り）
                float jx = Random.Range(-roofW * 0.20f, roofW * 0.10f) * -slideDir;
                float sz = roofW * Random.Range(0.15f, 0.28f);
                sz = Mathf.Clamp(sz, 30f, 85f);

                // 速度タイプ: 0=速い, 1=普通, 2=引っかかり
                int speedType = Random.Range(0, 3);
                float baseSpeed = speedType == 0 ? Random.Range(160f, 240f)   // 速い
                                : speedType == 1 ? Random.Range(90f,  150f)   // 普通
                                :                  Random.Range(55f,   95f);  // 引っかかり

                _pieces.Add(new FallingPiece
                {
                    pos          = new Vector2(spawnX + jx, slideY),
                    vel          = Vector2.zero,
                    size         = sz,
                    sizeY        = sz * Random.Range(0.55f, 0.80f),
                    life         = 8f,
                    roofIdx      = ri,
                    rot          = Random.Range(-15f, 15f),
                    rotVel       = Random.Range(-40f, 40f) * slideDir,
                    alpha        = 1f,
                    texIdx       = Random.Range(0, 4),
                    phase        = PiecePhase.Sliding,
                    eaveX        = eaveX,
                    slideSpeed   = baseSpeed,
                    slideDirX    = slideDir,
                    slideY       = slideY,
                    hasStick     = (speedType == 2),
                    stickTimer   = 0f,
                    stickCooldown= Random.Range(0.15f, 0.45f), // 最初の引っかかりまでの時間
                });
            }

            Debug.Log($"[DETACH] roof={_roofs[ri].id} tap_detected=YES" +
                      $" spawn_gui=({spawnX:F1},{slideY:F1})" +
                      $" eave_x={eaveX:F1} slide_dir={slideDir:F0}" +
                      $" snow_fill={_roofs[ri].snowFill:F2}" +
                      $" chunk_count={spawnCount} phase=SLIDING");
            break; // 1タップ1軒
        }
    }

    // ── 各屋根の Image 高さを fill に合わせて縮小 ────────────────
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
                // Y は屋根下端に固定、X のみ滑落方向へ移動
                p.pos.y = p.slideY;

                // 引っかかり処理
                float curSpeed = p.slideSpeed;
                if (p.hasStick)
                {
                    if (p.stickTimer > 0f)
                    {
                        // 引っかかり中: 速度を大幅減速
                        p.stickTimer -= dt;
                        curSpeed = p.slideSpeed * 0.12f;
                        if (p.stickTimer <= 0f)
                        {
                            // 引っかかり解除: 次の引っかかりまでの時間をリセット
                            p.stickCooldown = Random.Range(0.2f, 0.6f);
                            // 解除時に小さな雪煙
                            SpawnSmoke(p.pos.x, p.pos.y, p.slideDirX * 20f, p.roofIdx);
                        }
                    }
                    else
                    {
                        p.stickCooldown -= dt;
                        if (p.stickCooldown <= 0f)
                        {
                            // 引っかかり開始
                            p.stickTimer = Random.Range(0.08f, 0.22f);
                        }
                    }
                }

                // 滑落移動
                p.pos.x += p.slideDirX * curSpeed * dt;
                p.rot   += p.rotVel * 0.3f * dt; // 滑落中は回転を抑える

                // 軒先到達判定
                bool reachedEave = p.slideDirX > 0f
                    ? p.pos.x >= p.eaveX
                    : p.pos.x <= p.eaveX;

                if (reachedEave)
                {
                    // 軒先到達 → 落下フェーズへ移行
                    p.pos.x = p.eaveX;
                    p.phase = PiecePhase.Falling;
                    // 落下初速: 滑落速度を引き継ぎ + 下方向
                    p.vel = new Vector2(
                        p.slideDirX * p.slideSpeed * Random.Range(0.3f, 0.6f),
                        Random.Range(30f, 80f));
                    p.rotVel = Random.Range(-100f, 100f); // 落下時に回転増加
                    // 軒先での雪煙
                    SpawnSmoke(p.pos.x, p.pos.y, p.vel.x * 0.5f, p.roofIdx);
                    Debug.Log($"[EAVE_REACHED] roof={_roofs[p.roofIdx].id} pos=({p.pos.x:F1},{p.pos.y:F1}) -> FALLING");
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
                    SpawnSmoke(p.pos.x, p.pos.y, p.vel.x * 0.3f, ri);
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

    // ── 雪煙生成（強化版）─────────────────────────────────────
    // 数・サイズ・寿命を増加し、崩れ感を補強
    void SpawnSmoke(float x, float y, float baseVx, int roofIdx)
    {
        int count = Random.Range(14, 22); // 強化: 8〜14 → 14〜22
        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(160f, 380f) * Mathf.Deg2Rad;
            float speed = Random.Range(40f, 140f); // 強化: 速度増加
            float life  = Random.Range(0.5f, 1.4f); // 強化: 寿命増加
            _smoke.Add(new SnowSmoke
            {
                pos     = new Vector2(x + Random.Range(-25f, 25f), y + Random.Range(-8f, 8f)),
                vel     = new Vector2(baseVx * 0.5f + Mathf.Cos(angle) * speed,
                                      Mathf.Sin(angle) * speed - 30f),
                size    = Random.Range(6f, 20f), // 強化: サイズ増加
                life    = life,
                maxLife = life,
            });
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
        if (_roofsReady)
        {
            for (int ri = 0; ri < _roofs.Length; ri++)
            {
                if (!_roofs[ri].ready || _roofs[ri].thickRatio <= 0f) continue;

                float fill    = _roofs[ri].snowFill;
                float expandY = 14f;
                float thickH  = (_roofs[ri].guiRect.height * _roofs[ri].thickRatio * fill) + expandY;
                float roofTop = _roofs[ri].guiRect.y - expandY;
                float roofLeft= _roofs[ri].guiRect.x;
                float roofW   = _roofs[ri].guiRect.width;

                if (thickH < 1f) continue;

                // 本体（雪色・不透明）
                GUI.color = SnowWhite;
                GUI.DrawTexture(new Rect(roofLeft, roofTop, roofW, thickH), _whiteTex);

                // 上端ハイライト（光沢感）
                if (thickH > 8f)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    GUI.DrawTexture(new Rect(roofLeft, roofTop, roofW, 4f), _whiteTex);
                }

                // 前縁: 2Dノイズマスクで崩す（厚みムラ + 垂れ）
                if (_roofEdgeMaskTex != null && thickH > 8f)
                {
                    // 前縁帯の高さ: 雪帯全体の40〜50%（大きめに崩す）
                    float edgeH = Mathf.Min(thickH * 0.48f, 28f);
                    float edgeY = roofTop + thickH - edgeH;

                    // 背景色（青灰）で前縁の本体部分を部分的に消す → 欠け感
                    // ※ OnGUI はアルファブレンドのみ。背景を上書きするため半透明の背景色で隠す
                    GUI.color = new Color(0.55f, 0.65f, 0.82f, 0.60f);
                    GUI.DrawTexture(new Rect(roofLeft, edgeY, roofW, edgeH), _roofEdgeMaskTex);

                    // 前縁の上に雪色を重ねて「崩れた雪の前縁」を表現
                    GUI.color = new Color(SnowWhite.r, SnowWhite.g, SnowWhite.b, 0.90f);
                    GUI.DrawTexture(new Rect(roofLeft, edgeY, roofW, edgeH * 0.6f), _roofEdgeMaskTex);
                }

                // 下端グラデーション影（3段）
                if (thickH > 10f)
                {
                    float edgeY = roofTop + thickH;
                    GUI.color = new Color(0.70f, 0.78f, 0.92f, 0.65f);
                    GUI.DrawTexture(new Rect(roofLeft, edgeY - 10f, roofW, 4f), _whiteTex);
                    GUI.color = new Color(0.58f, 0.68f, 0.88f, 0.50f);
                    GUI.DrawTexture(new Rect(roofLeft, edgeY - 6f, roofW, 3f), _whiteTex);
                    GUI.color = new Color(0.48f, 0.60f, 0.82f, 0.40f);
                    GUI.DrawTexture(new Rect(roofLeft, edgeY - 3f, roofW, 3f), _whiteTex);
                }
            }
        }

        // ② 落下中の不定形雪塊（シルエットテクスチャ使用）
        bool hasChunkTex = _chunkTextures != null && _chunkTextures.Length == 4;
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

        // ④ 雪煙パーティクル（強化: 大きめ・長め・散り広がる）
        foreach (var s in _smoke)
        {
            float t     = Mathf.Clamp01(s.life / s.maxLife);
            float alpha = t * t * 0.85f; // 二乗でゆっくりフェード
            float sz    = s.size * (1f + (1f - t) * 0.8f); // 時間とともに膨らむ
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(new Rect(s.pos.x - sz * 0.5f, s.pos.y - sz * 0.5f, sz, sz), _whiteTex);
        }

        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        if (_whiteTex        != null) { Object.DestroyImmediate(_whiteTex);        _whiteTex        = null; }
        if (_snowEdgeTex     != null) { Object.DestroyImmediate(_snowEdgeTex);     _snowEdgeTex     = null; }
        if (_roofEdgeMaskTex != null) { Object.DestroyImmediate(_roofEdgeMaskTex); _roofEdgeMaskTex = null; }
        if (_chunkTextures   != null)
        {
            foreach (var t in _chunkTextures)
                if (t != null) Object.DestroyImmediate(t);
            _chunkTextures = null;
        }
    }
}
