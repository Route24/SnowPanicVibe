using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SnowStrip V2 — Single Source of Truth 設計（全6軒・局所減少対応）
///
/// 対象: Roof_TL / Roof_TM / Roof_TR / Roof_BL / Roof_BM / Roof_BR
///
/// 設計原則:
///   - 各屋根ごとに snowCols[] (列ごとの残雪量 0〜1) が唯一の真実。
///   - snowFill は snowCols の平均値（spawn判定・デバッグ表示用）。
///   - タップ → タップ列を中心にガウス分布で snowCols 減少 → 落雪生成。
///   - 全列平均 <= 0 なら絶対に落雪しない。
///   - 表示は列ごとの高さで描画（帯状にならない）。
/// </summary>
[DefaultExecutionOrder(10)]
public class SnowStripV2 : MonoBehaviour
{
    // ── 定数 ──────────────────────────────────────────────────
    const string CALIB_PATH        = "Assets/Art/RoofCalibrationData.json";
    const float  UNDER_EAVE_OFFSET = 0.10f;
    const float  THICK_RATIO       = 0.65f;
    const float  EXPAND_Y_MAX      = 14f;
    const int    SNOW_COLS         = 12;   // 列数（局所減少の解像度）

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

        // 列ごとの残雪量（唯一の真実）
        public float[]  snowCols = new float[SNOW_COLS];
        // snowCols の平均（spawn判定・デバッグ用）
        public float    snowFill => CalcFill(snowCols);

        public int      tapCount      = 0;
        public string   lastSpawnInfo = "---";

        public List<Piece> pieces = new List<Piece>();

        public void InitCols()
        {
            for (int c = 0; c < SNOW_COLS; c++) snowCols[c] = 1f;
        }

        static float CalcFill(float[] cols)
        {
            float sum = 0f;
            for (int c = 0; c < SNOW_COLS; c++) sum += cols[c];
            return sum / SNOW_COLS;
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
            _roofs[i].InitCols();
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
                  $" scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}" +
                  $" gameObject={gameObject.name}");

        // RoofGuide_* の Image を完全無効化（全6軒）
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

    // ── タップ処理 ────────────────────────────────────────────
    void HandleTap()
    {
        bool pressed = false;
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

        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);

        for (int ri = 0; ri < 6; ri++)
        {
            var roof = _roofs[ri];
            if (!roof.ready) continue;
            if (!roof.guiRect.Contains(guiPos)) continue;

            float fillBefore = roof.snowFill;
            roof.tapCount++;

            // ── タップ列を計算 ──────────────────────────────
            float localX  = (guiPos.x - roof.guiRect.x) / roof.guiRect.width;
            int   tapCol  = Mathf.Clamp(Mathf.FloorToInt(localX * SNOW_COLS), 0, SNOW_COLS - 1);

            // ── 局所範囲（tapCol ±1 列）の残雪を確認 ────────
            // フォールバック禁止: 局所範囲外の雪は関与させない
            const int LOCAL_HALF = 1; // タップ列から左右1列 = 計3列が局所範囲
            int localMin = Mathf.Max(0, tapCol - LOCAL_HALF);
            int localMax = Mathf.Min(SNOW_COLS - 1, tapCol + LOCAL_HALF);

            float localSnowBefore = 0f;
            for (int c = localMin; c <= localMax; c++)
                localSnowBefore += roof.snowCols[c];

            if (localSnowBefore <= 0f)
            {
                // 局所範囲が空 → 遠方フォールバックなしで即ブロック
                roof.lastSpawnInfo = $"TAP#{roof.tapCount} local=0.00 spawned=NO";
                Debug.Log($"[V2_TAP#{roof.tapCount}] roof={roof.id}" +
                          $" hitPos=({guiPos.x:F0},{guiPos.y:F0})" +
                          $" tapCol={tapCol} localRange={localMin}-{localMax}(±{LOCAL_HALF}cols)" +
                          $" localSnow_before={localSnowBefore:F3} localSnow_after={localSnowBefore:F3}" +
                          $" maxDist={LOCAL_HALF} spawned=NO fallback_used=NO [V2_SPAWN_BLOCKED]");
                break;
            }

            // ── 局所範囲内のみガウス分布で減少（範囲外は触らない）──
            // sigma = LOCAL_HALF * 0.6 = 0.6列分の広がり（中心集中・隣接は小）
            float sigma      = LOCAL_HALF * 0.6f;
            float totalDelta = 0f;
            float[] deltas   = new float[SNOW_COLS]; // 局所外は 0 のまま

            int   spawnCount    = Random.Range(1, 4);
            float deltaPerPiece = Random.Range(0.06f, 0.12f);
            float rawDelta      = deltaPerPiece * spawnCount;

            // 局所範囲内のみガウス重みを計算
            float weightSum = 0f;
            for (int c = localMin; c <= localMax; c++)
            {
                float dist = c - tapCol;
                float w    = Mathf.Exp(-0.5f * (dist / sigma) * (dist / sigma));
                deltas[c]  = w;
                weightSum += w;
            }
            // 局所範囲内のみ減少適用
            for (int c = localMin; c <= localMax; c++)
            {
                float d = (deltas[c] / weightSum) * rawDelta;
                d = Mathf.Min(d, roof.snowCols[c]);
                deltas[c]        = d;
                totalDelta      += d;
                roof.snowCols[c] = Mathf.Max(0f, roof.snowCols[c] - d);
            }

            float localSnowAfter = 0f;
            for (int c = localMin; c <= localMax; c++)
                localSnowAfter += roof.snowCols[c];

            float fillAfter = roof.snowFill;

            // ── 落雪生成（局所で実際に減った場合のみ）─────────
            bool spawned = totalDelta > 0.001f;
            if (spawned)
            {
                float roofW  = roof.guiRect.width;
                float spawnX = Mathf.Clamp(guiPos.x, roof.guiRect.x + 10f, roof.guiRect.xMax - 10f);
                float spawnY = roof.guiRect.y;

                for (int i = 0; i < spawnCount; i++)
                {
                    float jx  = Random.Range(-roofW * 0.15f, roofW * 0.15f);
                    float sz  = Mathf.Clamp(roofW * Random.Range(0.12f, 0.28f), 20f, 70f);
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

            roof.lastSpawnInfo = $"TAP#{roof.tapCount} local={localSnowAfter:F2} sp={(spawned ? spawnCount.ToString() : "NO")}";

            Debug.Log($"[V2_TAP#{roof.tapCount}] roof={roof.id}" +
                      $" hitPos=({guiPos.x:F0},{guiPos.y:F0})" +
                      $" centerCell={tapCol} affectedCells={localMin}-{localMax}(±{LOCAL_HALF}cols)" +
                      $" maxDist={LOCAL_HALF}" +
                      $" localSnow_before={localSnowBefore:F3} localSnow_after={localSnowAfter:F3}" +
                      $" fill_before={fillBefore:F3} fill_after={fillAfter:F3}" +
                      $" totalDelta={totalDelta:F3} spawned={(spawned ? spawnCount.ToString() : "NO")} fallback_used=NO");

            if (fillAfter <= 0f)
                Debug.Log($"[V2_TAP#{roof.tapCount}] roof={roof.id} fill=0.000 all_removed=YES spawned=NO_next_tap");

            break; // 1タップ = 1軒のみ処理
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

        var debugColor = new Color(0.0f, 0.9f, 0.85f, 0.90f); // シアン（確認用）
        var topLineColor = new Color(0.0f, 1f, 1f, 1f);

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
            float colW     = roofW / SNOW_COLS;
            float maxThickH = roof.guiRect.height * THICK_RATIO + EXPAND_Y_MAX;

            bool anySnow = false;

            // ── 列ごとに雪帯を描画（局所表示）────────────────
            for (int c = 0; c < SNOW_COLS; c++)
            {
                float fill = roof.snowCols[c];
                if (fill <= 0f)
                {
                    // 列が空: 背景色で上書き（トップライン消去）
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

            // 上端ライン（雪がある場合のみ）
            if (anySnow)
            {
                // 各列トップに細いシアンラインを引く
                for (int c = 0; c < SNOW_COLS; c++)
                {
                    float fill = roof.snowCols[c];
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

            // ── 黄色 fill ゲージ（屋根左端）──────────────────
            float fillAvg = roof.snowFill;
            GUI.color = new Color(1f, 1f, 0f, 0.85f);
            float barH = roof.guiRect.height * fillAvg;
            GUI.DrawTexture(new Rect(roof.guiRect.x - 6f, roof.guiRect.yMax - barH, 5f, barH), _whiteTex);

            // ── デバッグテキスト（屋根直下）──────────────────
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
