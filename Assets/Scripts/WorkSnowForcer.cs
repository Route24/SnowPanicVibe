using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// WORK_SNOW シーン専用。
/// 【モード: ALL_6_ROOFS + TL_DETACH_DEBUG_VISUAL】
///
/// ① 6軒の屋根雪表示（Canvas Image anchor fit）
/// ② Roof_TL 専用: タップ検出 → 屋根雪縮小 → 白い雪片が OnGUI で落下 → 上段地面で停止
///
/// 着地 Y は「上段3軒の屋根 maxY の平均」をキャリブデータから直接計算。
/// 3D ワールド座標変換は使わない（画面外になる問題を回避）。
/// </summary>
[ExecuteAlways]
public class WorkSnowForcer : MonoBehaviour
{
    const string CALIB_PATH = "Assets/Art/RoofCalibrationData.json";

    static readonly (string calibId, string guideId)[] RoofPairs =
    {
        ("Roof_TL", "RoofGuide_TL"),
        ("Roof_TM", "RoofGuide_TM"),
        ("Roof_TR", "RoofGuide_TR"),
        ("Roof_BL", "RoofGuide_BL"),
        ("Roof_BM", "RoofGuide_BM"),
        ("Roof_BR", "RoofGuide_BR"),
    };

    // 上段屋根 ID（着地 Y 計算に使う）
    static readonly string[] UpperRoofIds = { "Roof_TL", "Roof_TM", "Roof_TR" };

    static readonly Color SnowWhite = new Color(0.93f, 0.96f, 1.0f, 0.95f);

    // ── 屋根雪の現在の高さ比率（1=満杯, 0=空）──────────────────
    float _tlSnowFill    = 1f;
    float _tlAnchorMinY0 = -1f; // Apply() 後の初期 anchorMin.y
    float _tlAnchorMaxY0 = -1f; // Apply() 後の初期 anchorMax.y

    // ── 落下中の雪片 ──────────────────────────────────────────
    struct FallingPiece
    {
        public Vector2 pos;      // OnGUI 座標（左上原点）
        public Vector2 vel;      // px/sec
        public float   size;
        public float   alpha;
        public float   life;
    }
    readonly List<FallingPiece> _pieces = new List<FallingPiece>();

    // ── 着地済み雪片（残留表示用）────────────────────────────
    struct LandedPiece
    {
        public Vector2 pos;
        public float   size;
        public float   remainLife; // 残留寿命（十分長く設定）
    }
    readonly List<LandedPiece> _landedPieces = new List<LandedPiece>();

    // ── Roof_TL の OnGUI bbox ─────────────────────────────────
    Rect  _tlRect;
    bool  _tlRectReady = false;

    // ── 上段地面の OnGUI Y（キャリブデータから直接計算）──────────
    float _upperGroundGuiY = -1f;
    bool  _upperGroundReady = false;

    // ── spawn マーカー（デバッグ可視化）─────────────────────────
    Vector2 _lastSpawnPos;
    bool    _hasSpawnMarker = false;
    float   _spawnMarkerLife = 0f;

    bool      _applied = false;
    Texture2D _whiteTex;

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
        _applied = false;
        _pieces.Clear();
        _landedPieces.Clear();
        _tlSnowFill = 1f;
        _tlRectReady = false;
        _upperGroundReady = false;
        _hasSpawnMarker = false;
        _tlAnchorMinY0 = -1f;
        _tlAnchorMaxY0 = -1f;

        if (_whiteTex == null)
        {
            _whiteTex = new Texture2D(1, 1);
            _whiteTex.SetPixel(0, 0, Color.white);
            _whiteTex.Apply();
        }
    }

    void Start()  { Apply(); }
    void Update()
    {
        if (!_applied) Apply();
        if (!Application.isPlaying) return;
        UpdateTlRect();
        UpdateUpperGroundY();
        HandleTap();
        UpdatePieces();
    }

    // ── 6軒の屋根雪 Canvas Image を更新 ─────────────────────────
    void Apply()
    {
        var canvas = GameObject.Find("RoofGuideCanvas");
        if (canvas != null && !canvas.activeSelf) canvas.SetActive(true);

        if (!File.Exists(CALIB_PATH)) return;
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null) return;

        int ok = 0;
        foreach (var (calibId, guideId) in RoofPairs)
        {
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
            img.color         = SnowWhite;
            img.raycastTarget = false;

            // Roof_TL の初期 anchor を保存
            if (calibId == "Roof_TL" && _tlAnchorMinY0 < 0f)
            {
                _tlAnchorMinY0 = anchorMin.y;
                _tlAnchorMaxY0 = anchorMax.y;
            }
            ok++;
        }

        _applied = ok == 6;
        Debug.Log($"[ALL6_SNOW_FIT] count={ok}/6 all_6={(_applied ? "YES" : "NO")}");
    }

    // ── Roof_TL の OnGUI bbox をキャリブデータから計算 ───────────
    void UpdateTlRect()
    {
        if (_tlRectReady) return; // 一度計算したら固定
        if (!File.Exists(CALIB_PATH)) return;
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null) return;

        foreach (var r in sd.roofs)
        {
            if (r.id != "Roof_TL" || !r.confirmed) continue;
            float minX = Mathf.Min(r.topLeft.x, r.topRight.x, r.bottomRight.x, r.bottomLeft.x);
            float maxX = Mathf.Max(r.topLeft.x, r.topRight.x, r.bottomRight.x, r.bottomLeft.x);
            float minY = Mathf.Min(r.topLeft.y, r.topRight.y, r.bottomRight.y, r.bottomLeft.y);
            float maxY = Mathf.Max(r.topLeft.y, r.topRight.y, r.bottomRight.y, r.bottomLeft.y);

            // calib Y は上0→下1、OnGUI も左上原点なのでそのまま
            _tlRect = new Rect(
                minX * Screen.width,
                minY * Screen.height,
                (maxX - minX) * Screen.width,
                (maxY - minY) * Screen.height);
            _tlRectReady = true;
            Debug.Log($"[TL_RECT] gui=({_tlRect.x:F1},{_tlRect.y:F1} {_tlRect.width:F1}x{_tlRect.height:F1})");
            break;
        }
    }

    // ── 上段地面の OnGUI Y をキャリブデータから直接計算 ─────────
    // 上段3軒の屋根 maxY（下端）の平均 + オフセットを着地ラインとする
    void UpdateUpperGroundY()
    {
        if (_upperGroundReady) return; // 一度計算したら固定
        if (!File.Exists(CALIB_PATH)) return;
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null) return;

        float sumMaxY = 0f;
        int   count   = 0;
        foreach (var r in sd.roofs)
        {
            bool isUpper = false;
            foreach (var uid in UpperRoofIds) if (r.id == uid) { isUpper = true; break; }
            if (!isUpper || !r.confirmed) continue;

            float maxY = Mathf.Max(r.topLeft.y, r.topRight.y, r.bottomRight.y, r.bottomLeft.y);
            sumMaxY += maxY;
            count++;
        }
        if (count == 0) return;

        // calib Y（0=上端, 1=下端）→ OnGUI Y（px）
        float avgMaxY = sumMaxY / count;
        // 屋根下端より少し下（+5%）を地面ラインとする
        float groundCalibY = avgMaxY + 0.05f;
        _upperGroundGuiY  = groundCalibY * Screen.height;
        _upperGroundReady = true;

        Debug.Log($"[UPPER_GROUND_Y] calib_avg_maxY={avgMaxY:F3} ground_calib_y={groundCalibY:F3}" +
                  $" gui_y={_upperGroundGuiY:F1} screen_height={Screen.height}" +
                  $" upper_ground_hit_object=calib_derived upper_ground_hit_layer=N/A");
    }

    // ── タップ検出 ────────────────────────────────────────────
    void HandleTap()
    {
        if (!_tlRectReady) return;

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

        // Input.mousePosition は左下原点 → OnGUI は左上原点
        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        if (!_tlRect.Contains(guiPos)) return;

        // 屋根雪を減らす
        _tlSnowFill = Mathf.Max(0f, _tlSnowFill - 0.15f);
        UpdateTlSnowVisual();

        // spawn 位置: 屋根の中央下端
        float spawnX = _tlRect.x + _tlRect.width  * 0.5f;
        float spawnY = _tlRect.y + _tlRect.height;  // 屋根下端

        // デバッグ: 1個だけ大きめの雪片を生成
        _pieces.Add(new FallingPiece
        {
            pos   = new Vector2(spawnX, spawnY),
            vel   = new Vector2(Random.Range(-20f, 20f), 60f),
            size  = 40f,   // 大きめ・見えやすい
            alpha = 1f,
            life  = 10f,   // 着地前に消えないよう長め
        });

        // spawn マーカー（3秒表示）
        _lastSpawnPos   = new Vector2(spawnX, spawnY);
        _hasSpawnMarker = true;
        _spawnMarkerLife = 3f;

        Debug.Log($"[TL_DETACH] tap_detected=YES detach_triggered=YES" +
                  $" spawn_gui=({spawnX:F1},{spawnY:F1})" +
                  $" upper_ground_gui_y={_upperGroundGuiY:F1}" +
                  $" snow_fill={_tlSnowFill:F2} piece_size=40px");
    }

    // ── Roof_TL の Image 高さを fill に合わせて縮小 ──────────────
    void UpdateTlSnowVisual()
    {
        if (_tlAnchorMinY0 < 0f || _tlAnchorMaxY0 < 0f) return;
        var tlGo = GameObject.Find("RoofGuide_TL");
        if (tlGo == null) return;
        var rt = tlGo.GetComponent<RectTransform>();
        if (rt == null) return;

        // fill=1 → anchorMin=_tlAnchorMinY0（元の下端）
        // fill=0 → anchorMin=_tlAnchorMaxY0（上端まで縮む）
        float newMinY = Mathf.Lerp(_tlAnchorMaxY0, _tlAnchorMinY0, _tlSnowFill);
        rt.anchorMin = new Vector2(rt.anchorMin.x, newMinY);
    }

    // ── 落下雪片の更新（着地判定付き）───────────────────────────
    void UpdatePieces()
    {
        float dt = Time.deltaTime;

        // spawn マーカー寿命
        if (_hasSpawnMarker)
        {
            _spawnMarkerLife -= dt;
            if (_spawnMarkerLife <= 0f) _hasSpawnMarker = false;
        }

        // 着地済み雪片の寿命（十分長いので実質永続）
        for (int i = _landedPieces.Count - 1; i >= 0; i--)
        {
            var lp = _landedPieces[i];
            lp.remainLife -= dt;
            if (lp.remainLife <= 0f) { _landedPieces.RemoveAt(i); continue; }
            _landedPieces[i] = lp;
        }

        // 落下中の雪片
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];
            p.pos   += p.vel * dt;
            p.vel.y += 180f * dt; // 重力加速（下が正）
            p.life  -= dt;

            // 上段地面で着地
            if (_upperGroundReady && p.pos.y >= _upperGroundGuiY)
            {
                p.pos.y = _upperGroundGuiY;
                _landedPieces.Add(new LandedPiece
                {
                    pos        = p.pos,
                    size       = p.size,
                    remainLife = 30f, // 30秒残留
                });
                Debug.Log($"[UPPER_GROUND_LANDING] upper_ground_hit_detected=YES" +
                          $" hit_gui_y={_upperGroundGuiY:F1}" +
                          $" piece_pos=({p.pos.x:F1},{p.pos.y:F1})" +
                          $" falling_piece_stops=YES remains_visible=YES falls_off_screen=NO");
                _pieces.RemoveAt(i);
                continue;
            }

            if (p.life <= 0f) { _pieces.RemoveAt(i); continue; }
            _pieces[i] = p;
        }
    }

    // ── OnGUI: 全デバッグ描画 ─────────────────────────────────
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (_whiteTex == null) return;

        // ① 上段地面ライン（太め・目立つ色）
        if (_upperGroundReady)
        {
            GUI.color = new Color(1f, 0.3f, 0f, 0.9f); // オレンジ
            GUI.DrawTexture(new Rect(0, _upperGroundGuiY - 3f, Screen.width, 6f), _whiteTex);
        }

        // ② 落下中の雪片（大きめ・不透明）
        GUI.color = Color.white;
        foreach (var p in _pieces)
        {
            GUI.DrawTexture(new Rect(p.pos.x - p.size * 0.5f, p.pos.y - p.size * 0.5f, p.size, p.size), _whiteTex);
        }

        // ③ 着地済み雪片（白・不透明・永続）
        GUI.color = new Color(0.9f, 0.95f, 1f, 1f);
        foreach (var lp in _landedPieces)
        {
            GUI.DrawTexture(new Rect(lp.pos.x - lp.size * 0.5f, lp.pos.y - lp.size * 0.5f, lp.size, lp.size), _whiteTex);
        }

        // ④ spawn マーカー（黄色の十字）
        if (_hasSpawnMarker)
        {
            GUI.color = Color.yellow;
            float mx = _lastSpawnPos.x;
            float my = _lastSpawnPos.y;
            GUI.DrawTexture(new Rect(mx - 12f, my - 2f, 24f, 4f), _whiteTex); // 横
            GUI.DrawTexture(new Rect(mx - 2f, my - 12f, 4f, 24f), _whiteTex); // 縦
        }

        // ⑤ landing マーカー（緑の十字）
        if (_landedPieces.Count > 0)
        {
            GUI.color = Color.green;
            var lp = _landedPieces[_landedPieces.Count - 1];
            float mx = lp.pos.x;
            float my = lp.pos.y;
            GUI.DrawTexture(new Rect(mx - 12f, my - 2f, 24f, 4f), _whiteTex);
            GUI.DrawTexture(new Rect(mx - 2f, my - 12f, 4f, 24f), _whiteTex);
        }

        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        if (_whiteTex != null) { Object.DestroyImmediate(_whiteTex); _whiteTex = null; }
    }
}
