using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SnowStrip 2D — Roof_BR 専用プロトタイプ
///
/// 残雪を 2D float 配列（_snow[x, y]）で管理し、
/// タップ位置を中心とした円形ブラシで減算する。
/// 描画は _snow 配列から毎フレーム Texture2D を再生成して OnGUI で表示。
/// 円形にくり抜かれる見た目を実現する。
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
    const float  BRUSH_R   = 4.5f;  // 半径（グリッドセル単位）
    const float  BRUSH_MAX = 0.30f; // 1タップあたりの最大削り量（中心セル）

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
                  $" grid={GRID_W}x{GRID_H} brushR={BRUSH_R}");

        BuildRoofData();
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        if (!_ready)
        {
            if (Screen.width > 1 && Screen.height > 1) BuildRoofData();
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
        if (!File.Exists(CALIB_PATH)) return;
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null) return;

        RoofEntry entry = null;
        foreach (var r in sd.roofs)
            if (r.id == TARGET_ROOF_ID) { entry = r; break; }
        if (entry == null || !entry.confirmed) return;

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
    void HandleTap()
    {
        bool pressed = false;
        Vector2 screenPos = Vector2.zero;

        if (Input.GetMouseButtonDown(0))
            { screenPos = Input.mousePosition; pressed = true; }
        else if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            { screenPos = Input.GetTouch(0).position; pressed = true; }
        if (!pressed) return;

        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        if (!_guiRect.Contains(guiPos)) return;

        _tapCount++;

        // guiPos → グリッド座標
        // 雪帯は guiRect の上側に拡張されているが、
        // タップは guiRect 内なので x/y ともに 0〜1 正規化
        float nx = Mathf.Clamp01((guiPos.x - _guiRect.x) / _guiRect.width);
        // y=0 = guiRect 上端（= 雪の表面方向）
        float ny = Mathf.Clamp01((guiPos.y - _guiRect.y) / _guiRect.height);

        float gx = nx * GRID_W;  // 0〜GRID_W
        float gy = ny * GRID_H;  // 0〜GRID_H（表面=0）

        // 中心セル
        int cx = Mathf.Clamp(Mathf.FloorToInt(gx), 0, GRID_W - 1);
        int cy = Mathf.Clamp(Mathf.FloorToInt(gy), 0, GRID_H - 1);

        float centerBefore = _snow[cx, cy];

        // 全体の平均（spawn判定）
        float fillBefore = CalcFill();

        if (fillBefore <= 0f)
        {
            _lastInfo = $"TAP#{_tapCount} fill=0 spawned=NO";
            Debug.Log($"[2D_TAP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                      $" fillBefore={fillBefore:F3} spawned=NO [2D_ALL_EMPTY]");
            return;
        }

        // ── 円形ブラシ減算 ────────────────────────────────────
        int bx0 = Mathf.Max(0,          Mathf.FloorToInt(gx - BRUSH_R));
        int bx1 = Mathf.Min(GRID_W - 1, Mathf.CeilToInt (gx + BRUSH_R));
        int by0 = Mathf.Max(0,          Mathf.FloorToInt(gy - BRUSH_R));
        int by1 = Mathf.Min(GRID_H - 1, Mathf.CeilToInt (gy + BRUSH_R));

        float totalDelta   = 0f;
        float centerDelta  = 0f;
        int   hitCells     = 0;

        for (int bx = bx0; bx <= bx1; bx++)
        for (int by = by0; by <= by1; by++)
        {
            float dx = (bx + 0.5f) - gx;
            float dy = (by + 0.5f) - gy;
            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            if (dist >= BRUSH_R) continue;

            // smoothstep フォールオフ（中心=1, 外周→0）
            float t = 1f - dist / BRUSH_R;
            float w = t * t * (3f - 2f * t);

            float d = w * BRUSH_MAX;
            d = Mathf.Min(d, _snow[bx, by]);
            if (d <= 0f) continue;

            _snow[bx, by] -= d;
            totalDelta    += d;
            hitCells++;
            if (bx == cx && by == cy) centerDelta = d;
        }

        _texDirty = true;

        float fillAfter    = CalcFill();
        float centerAfter  = _snow[cx, cy];

        // ── 落雪生成（実際に減った量 > 0 のときのみ）─────────
        bool spawned = totalDelta > 0.001f;
        int  spawnCount = 0;
        if (spawned)
        {
            spawnCount = Mathf.Max(1, Mathf.RoundToInt(totalDelta / BRUSH_MAX * 3f));
            spawnCount = Mathf.Clamp(spawnCount, 1, 4);

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

        _lastInfo    = $"TAP#{_tapCount} fill={fillAfter:F2} sp={(spawned?spawnCount.ToString():"NO")}";
        _lastSpawned = spawned;

        Debug.Log($"[2D_TAP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                  $" guiPos=({guiPos.x:F0},{guiPos.y:F0})" +
                  $" gridCenter=({cx},{cy})" +
                  $" brushR={BRUSH_R} hitCells={hitCells}" +
                  $" centerBefore={centerBefore:F3} centerAfter={centerAfter:F3}" +
                  $" centerDelta={centerDelta:F3} totalDelta={totalDelta:F3}" +
                  $" fillBefore={fillBefore:F3} fillAfter={fillAfter:F3}" +
                  $" spawned={(spawned?spawnCount.ToString():"NO")}");

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
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (!_ready || _snowTex == null) return;

        // ── 雪帯: Texture2D を stretched 描画 ───────────────
        // 雪帯の表示領域: 縦方向は _guiRect 上端 -(EXPAND_Y_MAX) から guiRect.yMax まで
        float fillAvg  = CalcFill();
        float expandY  = EXPAND_Y_MAX * fillAvg;
        float snowTop  = _guiRect.y - expandY;
        float snowH    = _guiRect.height * THICK_RATIO * fillAvg + expandY;

        if (snowH >= 1f && fillAvg > 0f)
        {
            // テクスチャを雪帯全体にストレッチ描画
            // _snowTex のアルファがマスクになる
            GUI.color = Color.white;
            GUI.DrawTexture(
                new Rect(_guiRect.x, snowTop, _guiRect.width, snowH),
                _snowTex,
                ScaleMode.StretchToFill,
                alphaBlend: true
            );

            // 上端シアンライン
            GUI.color = new Color(0f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(_guiRect.x, snowTop, _guiRect.width, 3f),
                            Texture2D.whiteTexture);
        }
        else if (fillAvg <= 0f)
        {
            // 全部空: トップライン消去
            GUI.color = new Color(0.45f, 0.55f, 0.72f, 0.90f);
            GUI.DrawTexture(new Rect(_guiRect.x, _guiRect.y - 18f, _guiRect.width, 22f),
                            Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.2f, 0.2f, 0.8f);
            GUI.DrawTexture(new Rect(_guiRect.x, _guiRect.y - 4f,  _guiRect.width,  4f),
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
