using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SnowStrip V2 — Single Source of Truth 設計（全6軒・円形ブラシ減少）
///
/// 対象: Roof_TL / Roof_TM / Roof_TR / Roof_BL / Roof_BM / Roof_BR
///
/// 設計原則:
///   - 各屋根ごとに snowGrid[col, row] が唯一の真実（2Dグリッド）。
///   - snowFill は snowGrid の全セル平均（spawn判定・デバッグ用）。
///   - タップ → タップ位置を中心とした円形ブラシで snowGrid 減少 → 落雪生成。
///   - 中心セルが空なら spawned=NO。
///   - 表示は列ごとに行最大値を使って高さを決定。
/// </summary>
[DefaultExecutionOrder(10)]
public class SnowStripV2 : MonoBehaviour
{
    // ── 定数 ──────────────────────────────────────────────────
    const string CALIB_PATH        = "Assets/Art/RoofCalibrationData.json";
    const float  UNDER_EAVE_OFFSET = 0.10f;
    const float  THICK_RATIO       = 0.65f;
    const float  EXPAND_Y_MAX      = 14f;

    // 2Dグリッド解像度
    const int    GRID_COLS = 20;  // X方向（幅）
    const int    GRID_ROWS = 1;   // Y方向は1行固定（X軸のみ管理・確実に減少）

    // 円形ブラシ半径（一時無効化: 中心1セルのみ）
    const float  BRUSH_RADIUS = 0.6f;  // 半径を1列未満にして中心セルのみに限定

    // V2 管理対象屋根（全6軒）
    static readonly string[] V2_ROOF_IDS  = { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };
    static readonly string[] V2_GUIDE_IDS = { "RoofGuide_TL", "RoofGuide_TM", "RoofGuide_TR", "RoofGuide_BL", "RoofGuide_BM", "RoofGuide_BR" };

    // ── 屋根ごとの状態 ────────────────────────────────────────
    struct Piece
    {
        public Vector2 pos, vel;
        public float   size, life, alpha;
    }

    class RoofState
    {
        public string   id;
        public Rect     guiRect;
        public float    eaveGuiY;
        public float    eaveGuiX;
        public Vector2  downhillDir;
        public bool     ready;

        // 2Dグリッド（唯一の真実）: [col, row]  col=X方向, row=Y方向（0=表面）
        public float[,] snowGrid = new float[GRID_COLS, GRID_ROWS];
        // snowGrid の全セル平均（spawn判定・デバッグ用）
        public float snowFill
        {
            get
            {
                float s = 0f;
                for (int c = 0; c < GRID_COLS; c++)
                    for (int r = 0; r < GRID_ROWS; r++)
                        s += snowGrid[c, r];
                return s / (GRID_COLS * GRID_ROWS);
            }
        }
        // 列ごとの最大値（表示高さ計算用）
        public float ColMax(int c)
        {
            float m = 0f;
            for (int r = 0; r < GRID_ROWS; r++)
                m = Mathf.Max(m, snowGrid[c, r]);
            return m;
        }

        public int      tapCount      = 0;
        public string   lastSpawnInfo = "---";

        public List<Piece> pieces = new List<Piece>();

        public void InitGrid()
        {
            for (int c = 0; c < GRID_COLS; c++)
                for (int r = 0; r < GRID_ROWS; r++)
                    snowGrid[c, r] = 1f;
        }
    }

    readonly RoofState[] _roofs = new RoofState[6];
    Texture2D _whiteTex;

    // ── Serialize 用 ──────────────────────────────────────────
    [System.Serializable] class V2Coord { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2Coord topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    // ── ライフサイクル ────────────────────────────────────────
    void OnEnable()
    {
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();

        for (int i = 0; i < 6; i++)
        {
            _roofs[i] = new RoofState { id = V2_ROOF_IDS[i] };
            _roofs[i].InitGrid();
        }
    }

    void OnDestroy()
    {
        if (_whiteTex != null) { Object.DestroyImmediate(_whiteTex); _whiteTex = null; }
    }

    void Start()
    {
        if (!Application.isPlaying) return;

        Debug.Log($"[V2_ALIVE] SnowStripV2 started. targets=ALL6(TL/TM/TR/BL/BM/BR)" +
                  $" grid={GRID_COLS}x{GRID_ROWS} brushRadius={BRUSH_RADIUS}" +
                  $" scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

        for (int i = 0; i < V2_GUIDE_IDS.Length; i++)
        {
            var go = GameObject.Find(V2_GUIDE_IDS[i]);
            if (go == null) continue;
            var img = go.GetComponent<Image>();
            if (img == null) continue;
            img.enabled = false;
            img.color   = Color.clear;
            Debug.Log($"[V2_GUIDE_IMAGE_OFF] id={V2_GUIDE_IDS[i]} forced_off=YES");
        }

        BuildAllRoofData();
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        bool anyNotReady = false;
        for (int i = 0; i < 6; i++)
            if (!_roofs[i].ready) anyNotReady = true;

        if (anyNotReady)
        {
            if (Screen.width > 1 && Screen.height > 1)
                BuildAllRoofData();
            return;
        }

        HandleTap();
        UpdateAllPieces();
    }

    // ── ルーフデータ構築 ─────────────────────────────────────
    void BuildAllRoofData()
    {
        if (!File.Exists(CALIB_PATH)) return;
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null) return;

        for (int ri = 0; ri < 6; ri++)
        {
            string targetId = V2_ROOF_IDS[ri];
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == targetId) { entry = r; break; }
            if (entry == null || !entry.confirmed) continue;

            float minX = Mathf.Min(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
            float maxX = Mathf.Max(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
            float minY = Mathf.Min(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);
            float maxY = Mathf.Max(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);

            var roof = _roofs[ri];
            roof.guiRect = new Rect(
                minX * Screen.width,
                minY * Screen.height,
                (maxX - minX) * Screen.width,
                (maxY - minY) * Screen.height
            );

            float eaveCalibY = maxY + UNDER_EAVE_OFFSET;
            roof.eaveGuiY = Mathf.Min(eaveCalibY * Screen.height, Screen.height - 2f);
            roof.eaveGuiX = ((minX + maxX) * 0.5f) * Screen.width;

            float topCX = ((entry.topLeft.x + entry.topRight.x) * 0.5f) * Screen.width;
            float topCY = ((entry.topLeft.y + entry.topRight.y) * 0.5f) * Screen.height;
            float botCX = ((entry.bottomLeft.x + entry.bottomRight.x) * 0.5f) * Screen.width;
            float botCY = ((entry.bottomLeft.y + entry.bottomRight.y) * 0.5f) * Screen.height;
            var dhRaw = new Vector2(botCX - topCX, botCY - topCY);
            float dhLen = dhRaw.magnitude;
            roof.downhillDir = dhLen > 0.5f ? dhRaw.normalized : new Vector2(0f, 1f);

            roof.ready = true;
            Debug.Log($"[V2_ROOF_READY] roof={targetId} guiRect={roof.guiRect}" +
                      $" eaveGuiY={roof.eaveGuiY:F1}" +
                      $" downhill=({roof.downhillDir.x:F3},{roof.downhillDir.y:F3})");
        }
    }

    // ── タップ処理（円形ブラシ）────────────────────────────────
    void HandleTap()
    {
        bool pressed = false;
        Vector2 screenPos = Vector2.zero;

        if (Input.GetMouseButtonDown(0))        { screenPos = Input.mousePosition; pressed = true; }
        else if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
                                                { screenPos = Input.GetTouch(0).position; pressed = true; }
        if (!pressed) return;

        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);

        for (int ri = 0; ri < 6; ri++)
        {
            var roof = _roofs[ri];
            if (!roof.ready) continue;
            if (!roof.guiRect.Contains(guiPos)) continue;

            float fillBefore = roof.snowFill;
            roof.tapCount++;

            // ── タップ位置 → グリッド列を計算（X軸のみ）──────────
            float localX  = (guiPos.x - roof.guiRect.x) / roof.guiRect.width;
            float tapGX   = Mathf.Clamp01(localX) * GRID_COLS;

            // Y軸は1行固定（GRID_ROWS=1 のため常に row=0）
            float tapGY   = 0.5f;  // 行0の中心
            int   centerCol = Mathf.Clamp(Mathf.FloorToInt(tapGX), 0, GRID_COLS - 1);
            int   centerRow = 0;

            // ── 中心セルが空なら即ブロック ─────────────────────
            float centerSnowBefore = roof.snowGrid[centerCol, centerRow];
            if (centerSnowBefore <= 0f)
            {
                roof.lastSpawnInfo = $"TAP#{roof.tapCount} center=0.00 spawned=NO";
                Debug.Log($"[V2_TAP#{roof.tapCount}] roof={roof.id}" +
                          $" hitPos=({guiPos.x:F0},{guiPos.y:F0})" +
                          $" centerCell=({centerCol},{centerRow}) centerSnow={centerSnowBefore:F3}" +
                          $" brushRadius={BRUSH_RADIUS} spawned=NO [V2_SPAWN_BLOCKED]");
                break;
            }

            // ── 円形ブラシで減少 ────────────────────────────────
            int   spawnCount    = Random.Range(1, 4);
            float deltaPerPiece = Random.Range(0.06f, 0.12f);
            float rawDelta      = deltaPerPiece * spawnCount;

            // ブラシが影響するセル範囲（bounding box）
            int cMin = Mathf.Max(0, Mathf.FloorToInt(tapGX - BRUSH_RADIUS));
            int cMax = Mathf.Min(GRID_COLS - 1, Mathf.CeilToInt(tapGX + BRUSH_RADIUS));
            int rMin = Mathf.Max(0, Mathf.FloorToInt(tapGY - BRUSH_RADIUS));
            int rMax = Mathf.Min(GRID_ROWS - 1, Mathf.CeilToInt(tapGY + BRUSH_RADIUS));

            // 各セルへの重みを計算（半径外=0、線形フォールオフ）
            float weightSum = 0f;
            float[,] weights = new float[GRID_COLS, GRID_ROWS];
            int affectedCount = 0;

            for (int c = cMin; c <= cMax; c++)
            for (int r = rMin; r <= rMax; r++)
            {
                // セル中心 vs タップ位置の距離（グリッド単位）
                float dc = (c + 0.5f) - tapGX;
                float dr = (r + 0.5f) - tapGY;
                float dist = Mathf.Sqrt(dc * dc + dr * dr);

                if (dist >= BRUSH_RADIUS) continue;

                // 線形フォールオフ: center=1.0, edge→0
                float w = 1f - (dist / BRUSH_RADIUS);
                // smoothstep でより中心集中に
                w = w * w * (3f - 2f * w);

                weights[c, r] = w;
                weightSum += w;
                affectedCount++;
            }

            // 重みに基づいて各セルを減少
            float totalDelta      = 0f;
            float centerDeltaActual = 0f;
            float edgeDeltaMax   = 0f;

            for (int c = cMin; c <= cMax; c++)
            for (int r = rMin; r <= rMax; r++)
            {
                if (weights[c, r] <= 0f) continue;

                float d = (weights[c, r] / weightSum) * rawDelta;
                d = Mathf.Min(d, roof.snowGrid[c, r]);
                roof.snowGrid[c, r] = Mathf.Max(0f, roof.snowGrid[c, r] - d);
                totalDelta += d;

                if (c == centerCol && r == centerRow) centerDeltaActual = d;
                else edgeDeltaMax = Mathf.Max(edgeDeltaMax, d);
            }

            float centerSnowAfter = roof.snowGrid[centerCol, centerRow];
            float fillAfter = roof.snowFill;

            // ── 落雪生成（中心で実際に減った場合のみ）─────────
            bool spawned = centerDeltaActual > 0.001f;
            if (spawned)
            {
                float roofW  = roof.guiRect.width;
                float spawnX = Mathf.Clamp(guiPos.x, roof.guiRect.x + 10f, roof.guiRect.xMax - 10f);
                float spawnY = roof.guiRect.y;

                for (int i = 0; i < spawnCount; i++)
                {
                    float jx  = Random.Range(-roofW * 0.10f, roofW * 0.10f);
                    float sz  = Mathf.Clamp(roofW * Random.Range(0.10f, 0.22f), 16f, 60f);
                    float spd = Random.Range(80f, 200f);

                    roof.pieces.Add(new Piece
                    {
                        pos   = new Vector2(spawnX + jx, spawnY),
                        vel   = new Vector2(roof.downhillDir.x * spd * 0.5f, roof.downhillDir.y * spd),
                        size  = sz,
                        life  = 5f,
                        alpha = 1f,
                    });
                }
            }

            roof.lastSpawnInfo = $"TAP#{roof.tapCount} ctr={centerSnowAfter:F2} sp={(spawned ? spawnCount.ToString() : "NO")}";

            Debug.Log($"[V2_TAP#{roof.tapCount}] roof={roof.id}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0})" +
                      $" centerCell=({centerCol},{centerRow})" +
                      $" brushRadius={BRUSH_RADIUS} affectedCells={affectedCount}" +
                      $" centerSnow_before={centerSnowBefore:F3} centerSnow_after={centerSnowAfter:F3}" +
                      $" centerDelta={centerDeltaActual:F3} edgeDeltaMax={edgeDeltaMax:F3}" +
                      $" fill_before={fillBefore:F3} fill_after={fillAfter:F3}" +
                      $" spawned={(spawned ? spawnCount.ToString() : "NO")}");

            if (fillAfter <= 0f)
                Debug.Log($"[V2_TAP#{roof.tapCount}] roof={roof.id} fill=0.000 all_removed=YES spawned=NO_next_tap");

            break;
        }
    }

    // ── 落下雪塊の更新 ────────────────────────────────────────
    void UpdateAllPieces()
    {
        float dt = Time.deltaTime;
        for (int ri = 0; ri < 6; ri++)
        {
            var roof = _roofs[ri];
            for (int i = roof.pieces.Count - 1; i >= 0; i--)
            {
                var p = roof.pieces[i];
                p.vel.y += 520f * dt;
                p.pos   += p.vel * dt;
                p.life  -= dt;
                p.alpha  = Mathf.Clamp01(p.life * 0.8f);

                if (p.pos.y >= roof.eaveGuiY)
                {
                    p.pos.y = roof.eaveGuiY;
                    p.vel   = Vector2.zero;
                    p.life  = Mathf.Min(p.life, 1.2f);
                    Debug.Log($"[V2_UNDER_EAVE_STOP] roof={roof.id} pos=({p.pos.x:F1},{p.pos.y:F1})");
                }

                if (p.life <= 0f)
                    roof.pieces.RemoveAt(i);
                else
                    roof.pieces[i] = p;
            }
        }
    }

    // ── 描画 ─────────────────────────────────────────────────
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (_whiteTex == null) return;

        var debugColor   = new Color(0.0f, 0.9f, 0.85f, 0.90f); // シアン
        var topLineColor = new Color(0.0f, 1f,   1f,   1f);

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
        };

        for (int ri = 0; ri < 6; ri++)
        {
            var roof = _roofs[ri];
            if (!roof.ready) continue;

            float roofLeft = roof.guiRect.x;
            float roofW    = roof.guiRect.width;
            float colW     = roofW / GRID_COLS;

            bool anySnow = false;

            // ── 列ごとに ColMax を使って高さを決定して描画 ────
            for (int c = 0; c < GRID_COLS; c++)
            {
                float fill = roof.ColMax(c);  // 列の行最大値

                if (fill <= 0f)
                {
                    GUI.color = new Color(0.45f, 0.55f, 0.72f, 0.90f);
                    GUI.DrawTexture(new Rect(roofLeft + c * colW, roof.guiRect.y - 18f, colW + 1f, 22f), _whiteTex);
                    continue;
                }

                anySnow = true;
                float expandY = EXPAND_Y_MAX * fill;
                float thickH  = (roof.guiRect.height * THICK_RATIO * fill) + expandY;
                float colTop  = roof.guiRect.y - expandY;

                if (thickH < 1f) continue;

                GUI.color = debugColor;
                GUI.DrawTexture(new Rect(roofLeft + c * colW, colTop, colW + 1f, thickH), _whiteTex);
            }

            if (anySnow)
            {
                for (int c = 0; c < GRID_COLS; c++)
                {
                    float fill = roof.ColMax(c);
                    if (fill <= 0f) continue;
                    float expandY = EXPAND_Y_MAX * fill;
                    float colTop  = roof.guiRect.y - expandY;
                    GUI.color = topLineColor;
                    GUI.DrawTexture(new Rect(roofLeft + c * colW, colTop, colW + 1f, 3f), _whiteTex);
                }
            }

            // ── 落下雪塊 ─────────────────────────────────────
            foreach (var p in roof.pieces)
            {
                if (p.alpha <= 0f) continue;
                GUI.color = new Color(0.0f, 0.9f, 0.85f, p.alpha);
                float half = p.size * 0.5f;
                GUI.DrawTexture(new Rect(p.pos.x - half, p.pos.y - half, p.size, p.size), _whiteTex);
            }

            // ── 黄色 fill ゲージ ──────────────────────────────
            float fillAvg = roof.snowFill;
            GUI.color = new Color(1f, 1f, 0f, 0.85f);
            float barH = roof.guiRect.height * fillAvg;
            GUI.DrawTexture(new Rect(roof.guiRect.x - 6f, roof.guiRect.yMax - barH, 5f, barH), _whiteTex);

            // ── デバッグテキスト ──────────────────────────────
            float tx = roof.guiRect.x;
            float ty = roof.guiRect.yMax + 4f;

            GUI.color = new Color(0f, 0f, 0f, 0.60f);
            GUI.DrawTexture(new Rect(tx, ty, 160f, 38f), _whiteTex);

            GUI.color = Color.cyan;
            GUI.Label(new Rect(tx + 2f, ty + 1f,  158f, 14f), $"[V2] {roof.id}", style);

            GUI.color = Color.yellow;
            GUI.Label(new Rect(tx + 2f, ty + 13f, 158f, 14f),
                      $"fill={fillAvg:F2}  taps={roof.tapCount}", style);

            GUI.color = fillAvg <= 0f ? Color.red : Color.white;
            GUI.Label(new Rect(tx + 2f, ty + 25f, 158f, 14f), roof.lastSpawnInfo, style);
        }

        GUI.color = Color.white;
    }
}
