using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// WORK_SNOW シーン専用。
/// 【モード: ALL_6_ROOFS + ALL_6_UNDER_EAVE_LANDING + TL_THICK_SNOW】
///
/// ① 6軒の屋根雪表示（Canvas Image anchor fit）
/// ② 6軒すべて: タップ検出 → 屋根雪縮小 → 白い雪片が OnGUI で落下 → 各軒下で停止
/// ③ Roof_TL のみ: OnGUI で屋根上端に厚雪帯を追加描画（叩くと縮む）
///
/// 着地 Y は各屋根の calib maxY + UNDER_EAVE_OFFSET_CALIB から直接計算。
/// </summary>
[ExecuteAlways]
public class WorkSnowForcer : MonoBehaviour
{
    const string CALIB_PATH = "Assets/Art/RoofCalibrationData.json";

    // 屋根下端からの軒下オフセット（calib 座標、0〜1）
    const float UNDER_EAVE_OFFSET_CALIB = 0.08f;

    // TL 厚雪: 屋根上端から上方向に描く帯の最大高さ（px）
    // 屋根の高さの約 60% を「積もった雪の厚み」として表示する
    const float TL_THICK_SNOW_MAX_PX = 0f; // BuildRoofData() で屋根高さから動的計算
    const float TL_THICK_SNOW_RATIO  = 0.6f; // 屋根高さに対する厚雪の割合

    static readonly (string calibId, string guideId)[] RoofPairs =
    {
        ("Roof_TL", "RoofGuide_TL"),
        ("Roof_TM", "RoofGuide_TM"),
        ("Roof_TR", "RoofGuide_TR"),
        ("Roof_BL", "RoofGuide_BL"),
        ("Roof_BM", "RoofGuide_BM"),
        ("Roof_BR", "RoofGuide_BR"),
    };

    static readonly Color SnowWhite = new Color(0.93f, 0.96f, 1.0f, 0.95f);

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
        public bool    ready;
    }
    RoofData[] _roofs = new RoofData[6];

    // ── 落下中の雪片 ──────────────────────────────────────────
    struct FallingPiece
    {
        public Vector2 pos;
        public Vector2 vel;
        public float   size;
        public float   life;
        public int     roofIdx;  // どの屋根から落ちたか
    }
    readonly List<FallingPiece> _pieces = new List<FallingPiece>();

    // ── 着地済み雪片 ──────────────────────────────────────────
    struct LandedPiece
    {
        public Vector2 pos;
        public float   size;
        public float   remainLife;
        public int     roofIdx;
    }
    readonly List<LandedPiece> _landedPieces = new List<LandedPiece>();

    // ── TL 厚雪パラメータ ─────────────────────────────────────
    // 屋根上端の上に重ねる白い帯（OnGUI 描画）
    // 高さ = thickMaxPx * snowFill（叩くと縮む）
    float _tlThickMaxPx = 0f;  // BuildRoofData() で屋根高さ * RATIO から計算

    // ── spawn マーカー ────────────────────────────────────────
    Vector2 _lastSpawnPos;
    bool    _hasSpawnMarker  = false;
    float   _spawnMarkerLife = 0f;

    bool      _applied  = false;
    bool      _roofsReady = false;
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
        _applied    = false;
        _roofsReady = false;
        _pieces.Clear();
        _landedPieces.Clear();
        _hasSpawnMarker = false;
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
    }

    void Start()  { Apply(); }
    void Update()
    {
        if (!_applied) Apply();
        if (!Application.isPlaying) return;
        if (!_roofsReady) BuildRoofData();
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
            img.color         = SnowWhite;
            img.raycastTarget = false;

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

            _roofs[ri].id        = calibId;
            _roofs[ri].guideId   = guideId;
            _roofs[ri].guiRect   = new Rect(
                minX * Screen.width,
                minY * Screen.height,
                (maxX - minX) * Screen.width,
                (maxY - minY) * Screen.height);
            _roofs[ri].eaveGuiY  = eaveCalibY  * Screen.height;
            _roofs[ri].eaveGuiX  = eaveCenterX * Screen.width;
            _roofs[ri].ready     = true;
            readyCount++;

            // TL のみ: 厚雪の最大高さを屋根高さから計算
            if (calibId == "Roof_TL")
            {
                _tlThickMaxPx = _roofs[ri].guiRect.height * TL_THICK_SNOW_RATIO;
                Debug.Log($"[TL_THICK_SNOW] max_px={_tlThickMaxPx:F1} roof_h={_roofs[ri].guiRect.height:F1} ratio={TL_THICK_SNOW_RATIO}");
            }

            Debug.Log($"[UNDER_EAVE_TARGET] roof={calibId} created=YES" +
                      $" eave_calib_y={eaveCalibY:F4} gui_y={_roofs[ri].eaveGuiY:F1}" +
                      $" gui_x={_roofs[ri].eaveGuiX:F1}");
        }

        _roofsReady = readyCount == 6;
        Debug.Log($"[UNDER_EAVE_TARGET] all_6_targets_created={(_roofsReady ? "YES" : "NO")} count={readyCount}");
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

            // spawn 位置: 屋根中央下端
            float spawnX = _roofs[ri].guiRect.x + _roofs[ri].guiRect.width  * 0.5f;
            float spawnY = _roofs[ri].guiRect.y + _roofs[ri].guiRect.height;

            _pieces.Add(new FallingPiece
            {
                pos     = new Vector2(spawnX, spawnY),
                vel     = new Vector2(0f, 80f),
                size    = 40f,
                life    = 10f,
                roofIdx = ri,
            });

            _lastSpawnPos    = new Vector2(spawnX, spawnY);
            _hasSpawnMarker  = true;
            _spawnMarkerLife = 3f;

            Debug.Log($"[DETACH] roof={_roofs[ri].id} tap_detected=YES" +
                      $" spawn_gui=({spawnX:F1},{spawnY:F1})" +
                      $" eave_gui_y={_roofs[ri].eaveGuiY:F1}" +
                      $" snow_fill={_roofs[ri].snowFill:F2}");
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

    // ── 落下雪片の更新（各軒下で着地）───────────────────────────
    void UpdatePieces()
    {
        float dt = Time.deltaTime;

        if (_hasSpawnMarker)
        {
            _spawnMarkerLife -= dt;
            if (_spawnMarkerLife <= 0f) _hasSpawnMarker = false;
        }

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
            p.pos   += p.vel * dt;
            p.vel.y += 180f * dt;
            p.life  -= dt;

            int ri = p.roofIdx;
            if (ri >= 0 && ri < _roofs.Length && _roofs[ri].ready && p.pos.y >= _roofs[ri].eaveGuiY)
            {
                p.pos.y = _roofs[ri].eaveGuiY;
                _landedPieces.Add(new LandedPiece
                {
                    pos        = p.pos,
                    size       = p.size,
                    remainLife = 30f,
                    roofIdx    = ri,
                });
                Debug.Log($"[UNDER_EAVE_LANDING] roof={_roofs[ri].id} under_eave_hit=YES" +
                          $" hit_gui_y={_roofs[ri].eaveGuiY:F1}" +
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

        // ① 各軒下マーカー（シアンの短いバー）
        if (_roofsReady)
        {
            GUI.color = new Color(0f, 1f, 1f, 0.85f);
            for (int ri = 0; ri < _roofs.Length; ri++)
            {
                if (!_roofs[ri].ready) continue;
                float barW = _roofs[ri].guiRect.width * 1.1f;
                float barX = _roofs[ri].eaveGuiX - barW * 0.5f;
                GUI.DrawTexture(new Rect(barX, _roofs[ri].eaveGuiY - 3f, barW, 6f), _whiteTex);
            }
        }

        // ① TL 厚雪帯: 屋根上端から上方向に snowFill に比例した高さの白い帯を描く
        // 屋根の guiRect.y = 屋根上端（OnGUI 左上原点）
        // 帯は屋根上端から上に伸びる（y が小さい方向）
        if (_roofsReady && _tlThickMaxPx > 0f)
        {
            int tlIdx = 0; // RoofPairs[0] = Roof_TL
            if (_roofs[tlIdx].ready)
            {
                float fill       = _roofs[tlIdx].snowFill;
                float thickH     = _tlThickMaxPx * fill;
                float roofTop    = _roofs[tlIdx].guiRect.y;       // 屋根上端 Y
                float roofLeft   = _roofs[tlIdx].guiRect.x;
                float roofWidth  = _roofs[tlIdx].guiRect.width;

                // 帯の上端 = 屋根上端 - 厚み
                float bandTop    = roofTop - thickH;

                // 白い帯（不透明・雪色）
                GUI.color = new Color(0.93f, 0.96f, 1.0f, 0.97f);
                GUI.DrawTexture(new Rect(roofLeft, bandTop, roofWidth, thickH), _whiteTex);

                // 帯の下端に影色（立体感）
                if (thickH > 4f)
                {
                    GUI.color = new Color(0.75f, 0.82f, 0.92f, 0.6f);
                    GUI.DrawTexture(new Rect(roofLeft, roofTop - 4f, roofWidth, 4f), _whiteTex);
                }
            }
        }

        // ② 落下中の雪片（白・不透明）
        GUI.color = Color.white;
        foreach (var p in _pieces)
            GUI.DrawTexture(new Rect(p.pos.x - p.size * 0.5f, p.pos.y - p.size * 0.5f, p.size, p.size), _whiteTex);

        // ③ 着地済み雪片（白・不透明・残留）
        GUI.color = new Color(0.9f, 0.95f, 1f, 1f);
        foreach (var lp in _landedPieces)
            GUI.DrawTexture(new Rect(lp.pos.x - lp.size * 0.5f, lp.pos.y - lp.size * 0.5f, lp.size, lp.size), _whiteTex);

        // ④ spawn マーカー（黄色の十字）
        if (_hasSpawnMarker)
        {
            GUI.color = Color.yellow;
            float mx = _lastSpawnPos.x, my = _lastSpawnPos.y;
            GUI.DrawTexture(new Rect(mx - 12f, my - 2f, 24f, 4f), _whiteTex);
            GUI.DrawTexture(new Rect(mx - 2f, my - 12f, 4f, 24f), _whiteTex);
        }

        // ⑤ 最新 landing マーカー（緑の十字）
        if (_landedPieces.Count > 0)
        {
            GUI.color = Color.green;
            var lp = _landedPieces[_landedPieces.Count - 1];
            GUI.DrawTexture(new Rect(lp.pos.x - 12f, lp.pos.y - 2f, 24f, 4f), _whiteTex);
            GUI.DrawTexture(new Rect(lp.pos.x - 2f, lp.pos.y - 12f, 4f, 24f), _whiteTex);
        }

        GUI.color = Color.white;
    }

    void OnDestroy()
    {
        if (_whiteTex != null) { Object.DestroyImmediate(_whiteTex); _whiteTex = null; }
    }
}
