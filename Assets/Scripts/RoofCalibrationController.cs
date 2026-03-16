using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Roof Calibration Mode
/// キー1〜6で屋根を選択し、左クリックで4点（TL→TR→BR→BL）を入力して確定する。
/// S: JSON保存  L: JSON読み込み  R: 現在屋根リセット
/// 保存先: Assets/Art/RoofCalibrationData.json
/// </summary>
public class RoofCalibrationController : MonoBehaviour
{
    // ── データ構造 ──────────────────────────────────────────
    [System.Serializable]
    public class RoofPoints
    {
        public string id;
        public Vector2 topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }

    [System.Serializable]
    class SaveData { public List<RoofPoints> roofs; }

    static readonly string[] RoofIds = { "Roof_TL","Roof_TM","Roof_TR","Roof_BL","Roof_BM","Roof_BR" };
    static readonly string SavePath = "Assets/Art/RoofCalibrationData.json";

    // ── 状態 ────────────────────────────────────────────────
    RoofPoints[] _roofs;
    int _activeRoof = 0;   // 0〜5
    int _clickCount = 0;   // 0〜4: 入力済み点数
    string _saveStatus = "";          // 画面表示用保存ステータス
    float  _saveStatusTimer = 0f;     // 表示タイマー
    bool   _renderLogDone = false;    // 描画ログを1回だけ出すフラグ
    // Roof_TL 検証用: save/load/render の値を文字列で保持して比較
    string _tlSavePoints  = "";
    string _tlLoadPoints  = "";

    // ── 可視化 ──────────────────────────────────────────────
    Texture2D _dot;
    Texture2D _fill;
    // BackgroundImage の画面投影矩形（毎フレーム更新）
    Rect _bgRect;
    bool _bgRectValid = false;
    static readonly string[] PointLabels = { "TL","TR","BR","BL" };
    static readonly Color[] RoofColors =
    {
        new Color(1f,0.3f,0.6f,0.45f),
        new Color(0.3f,0.8f,1f,0.45f),
        new Color(0.3f,1f,0.5f,0.45f),
        new Color(1f,0.8f,0.2f,0.45f),
        new Color(0.8f,0.3f,1f,0.45f),
        new Color(1f,0.5f,0.2f,0.45f),
    };

    void Awake()
    {
        _roofs = new RoofPoints[6];
        for (int i = 0; i < 6; i++)
            _roofs[i] = new RoofPoints { id = RoofIds[i] };

        _dot  = MakeTex(1, 1, Color.white);
        _fill = MakeTex(1, 1, Color.white);

        // Canvas 上の既存 RoofGuide Rect を非表示にする
        var canvas = GameObject.Find("RoofGuideCanvas");
        if (canvas != null) canvas.SetActive(false);

        // 起動時は全屋根を未確定・非表示にする（Load するまで polygon は出さない）
        Debug.Log("[CALIB] calibration_mode_started=true active_roof=Roof_TL");
        Debug.Log("[CALIB] keys: 1-6=select  LClick=add point(TL>TR>BR>BL)  R=reset  S=save  L=load");
    }

    void Update()
    {
        // BackgroundImage の投影矩形を毎フレーム更新（OnGUI より前に確定させる）
        Update_BgRect();

        // キー1〜6で屋根選択
        for (int i = 0; i < 6; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                _activeRoof = i;
                _clickCount = CountInputtedPoints(_roofs[i]);
                Debug.Log($"[CALIB] selected={RoofIds[i]} confirmed_points={_clickCount}");
            }
        }

        // R: リセット
        if (Input.GetKeyDown(KeyCode.R))
        {
            _roofs[_activeRoof] = new RoofPoints { id = RoofIds[_activeRoof] };
            _clickCount = 0;
            Debug.Log($"[CALIB] reset={RoofIds[_activeRoof]}");
        }

        // S: 保存
        if (Input.GetKeyDown(KeyCode.S)) Save();

        // L: 読み込み
        if (Input.GetKeyDown(KeyCode.L)) Load();

        // 保存ステータス表示タイマー
        if (_saveStatusTimer > 0f) _saveStatusTimer -= Time.deltaTime;
        else _saveStatus = "";

        // 左クリックで点追加（確定済み屋根はスキップ）
        if (Input.GetMouseButtonDown(0) && _clickCount < 4)
        {
            var pos = Input.mousePosition;
            // BackgroundImage 投影矩形が有効なら bgRect 基準で normalized 化
            Vector2 norm;
            if (_bgRectValid && _bgRect.width > 1f && _bgRect.height > 1f)
            {
                // OnGUI 座標に変換してから bgRect で正規化
                float guiX = pos.x;
                float guiY = Screen.height - pos.y; // OnGUI は左上原点
                norm = new Vector2(
                    (guiX - _bgRect.x) / _bgRect.width,
                    (guiY - _bgRect.y) / _bgRect.height);
            }
            else
            {
                norm = new Vector2(pos.x / Screen.width, 1f - pos.y / Screen.height);
            }
            SetPoint(_roofs[_activeRoof], _clickCount, norm);
            _clickCount++;
            Debug.Log($"[CALIB] roof={RoofIds[_activeRoof]} point={PointLabels[_clickCount-1]}({_clickCount}/4) norm=({norm.x:F3},{norm.y:F3})");
            if (_clickCount == 4)
            {
                _roofs[_activeRoof].confirmed = true;
                NormalizePoints(_roofs[_activeRoof]);
                var r = _roofs[_activeRoof];
                Debug.Log($"[ROOF_CAPTURE_DONE] roof={r.id} points=4 TL=({r.topLeft.x:F3},{r.topLeft.y:F3}) TR=({r.topRight.x:F3},{r.topRight.y:F3}) BR=({r.bottomRight.x:F3},{r.bottomRight.y:F3}) BL=({r.bottomLeft.x:F3},{r.bottomLeft.y:F3})");
            }
        }
    }

    // BackgroundImage の画面投影矩形を取得（Update で毎フレーム呼ぶ）
    void Update_BgRect()
    {
        var cam = Camera.main;
        var bgGo = GameObject.Find("BackgroundImage");
        if (cam == null || bgGo == null) { _bgRectValid = false; return; }

        var t = bgGo.transform;
        Vector3 wTL = t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        float sh = Screen.height;
        // WorldToScreenPoint は左下原点 → OnGUI 左上原点に変換
        Vector2 sTL = cam.WorldToScreenPoint(wTL); sTL.y = sh - sTL.y;
        Vector2 sTR = cam.WorldToScreenPoint(wTR); sTR.y = sh - sTR.y;
        Vector2 sBL = cam.WorldToScreenPoint(wBL); sBL.y = sh - sBL.y;
        Vector2 sBR = cam.WorldToScreenPoint(wBR); sBR.y = sh - sBR.y;

        float minX = Mathf.Min(sTL.x, sBL.x);
        float maxX = Mathf.Max(sTR.x, sBR.x);
        float minY = Mathf.Min(sTL.y, sTR.y);
        float maxY = Mathf.Max(sBL.y, sBR.y);

        bool wasValid = _bgRectValid;
        _bgRect = new Rect(minX, minY, maxX - minX, maxY - minY);
        _bgRectValid = _bgRect.width > 1f && _bgRect.height > 1f;

        // bgRect が初めて有効になったら描画ログを再出力する
        if (!wasValid && _bgRectValid)
        {
            _renderLogDone = false;
            Debug.Log($"[CALIB_SPACE] bgRect_became_valid=true bgRect=({_bgRect.x:F1},{_bgRect.y:F1},{_bgRect.width:F1},{_bgRect.height:F1}) screen=({Screen.width},{Screen.height})");
        }
    }

    // ── OnGUI ────────────────────────────────────────────────
    void OnGUI()
    {
        // Update_BgRect は Update() で呼び済み。OnGUI では呼ばない。
        float sw = Screen.width, sh = Screen.height;

        for (int i = 0; i < 6; i++)
        {
            var r = _roofs[i];
            var col = RoofColors[i];

            // 確定済み: 検証 → ライン + ポリゴン fill + 4点マーカー
            if (r.confirmed)
            {
                bool polyValid = ValidateQuad(r, out string skipReason);

                // 1回だけ座標ログを出す
                if (!_renderLogDone)
                {
                    Debug.Log($"[CALIB_SPACE] screen=({sw:F0},{sh:F0}) bgRect=({_bgRect.x:F1},{_bgRect.y:F1},{_bgRect.width:F1},{_bgRect.height:F1}) bgRect_valid={_bgRectValid}");
                    Debug.Log($"[CALIB_POLYGON_INPUT] roof={r.id} tl=({r.topLeft.x:F3},{r.topLeft.y:F3}) tr=({r.topRight.x:F3},{r.topRight.y:F3}) br=({r.bottomRight.x:F3},{r.bottomRight.y:F3}) bl=({r.bottomLeft.x:F3},{r.bottomLeft.y:F3})");

                    if (!polyValid)
                    {
                        Debug.LogWarning($"[CALIB_POLYGON_SKIP_INVALID] roof={r.id} reason={skipReason}");
                    }
                    else
                    {
                        var pts4 = new (string lbl, Vector2 n)[]
                        {
                            ("TL", r.topLeft), ("TR", r.topRight),
                            ("BR", r.bottomRight), ("BL", r.bottomLeft)
                        };
                        foreach (var (lbl, n) in pts4)
                        {
                            var s = NormToScreen(n);
                            Debug.Log($"[CALIB_POINT_DRAW] roof={r.id} point={lbl} norm=({n.x:F3},{n.y:F3}) screen=({s.x:F1},{s.y:F1})");
                        }
                        // Roof_TL 専用: save/load との一致チェック
                        if (r.id == "Roof_TL")
                        {
                            Debug.Log($"[CALIB_RENDER_POINTS] roof=Roof_TL tl=({r.topLeft.x:F4},{r.topLeft.y:F4}) tr=({r.topRight.x:F4},{r.topRight.y:F4}) br=({r.bottomRight.x:F4},{r.bottomRight.y:F4}) bl=({r.bottomLeft.x:F4},{r.bottomLeft.y:F4})");
                            bool saveLoadMatch   = _tlSavePoints == _tlLoadPoints;
                            bool loadRenderMatch = _tlLoadPoints == MakeKey(r);
                            if (!saveLoadMatch)   Debug.LogWarning($"[CALIB_POINT_MISMATCH] roof=Roof_TL save_vs_load differ");
                            if (!loadRenderMatch) Debug.LogWarning($"[CALIB_POINT_MISMATCH] roof=Roof_TL load_vs_render differ");
                            Debug.Log($"[CALIB_TL_VERIFY] save_load_match={saveLoadMatch} load_render_match={loadRenderMatch}");
                        }
                        Debug.Log($"[CALIB_LINE_RENDER_OK] roof={r.id}");
                        Debug.Log($"[CALIB_POLYGON_RENDER_OK] roof={r.id}");
                    }
                }

                if (polyValid)
                {
                    // ① ポリゴン fill 一時非表示（SNOW_VISIBILITY_ISOLATION）
                    // DrawQuadNorm(r.topLeft, r.topRight, r.bottomRight, r.bottomLeft, col);
                    // ② 4点マーカー（残す）
                    DrawPointMarker(r.topLeft,     col, "TL");
                    DrawPointMarker(r.topRight,    col, "TR");
                    DrawPointMarker(r.bottomRight, col, "BR");
                    DrawPointMarker(r.bottomLeft,  col, "BL");
                    DrawLabelNorm(r.topLeft, r.id);
                }
                else
                {
                    // 無効: マーカーのみ（有効な点だけ）
                    if (r.topLeft     != Vector2.zero) DrawPointMarker(r.topLeft,     col, "TL");
                    if (r.topRight    != Vector2.zero) DrawPointMarker(r.topRight,    col, "TR");
                    if (r.bottomRight != Vector2.zero) DrawPointMarker(r.bottomRight, col, "BR");
                    if (r.bottomLeft  != Vector2.zero) DrawPointMarker(r.bottomLeft,  col, "BL");
                    DrawLabelNorm(r.topLeft != Vector2.zero ? r.topLeft : new Vector2(0.1f, 0.1f), r.id + "?");
                }
            }

            // アクティブ屋根: 入力済みの点をドット表示
            if (i == _activeRoof && !r.confirmed)
            {
                var pts = GetPoints(r);
                for (int p = 0; p < _clickCount; p++)
                    DrawDotNorm(pts[p], col);
            }
        }

        // 描画ログを1回出したらフラグを立てる
        if (!_renderLogDone)
        {
            int confirmedNow = 0;
            foreach (var r in _roofs) if (r.confirmed) confirmedNow++;
            if (confirmedNow > 0) _renderLogDone = true;
        }

        // マウスカーソル付近に「次の点」ラベル
        if (_clickCount < 4)
        {
            var mp = Input.mousePosition;
            GUI.color = Color.yellow;
            GUI.Label(new Rect(mp.x + 12, sh - mp.y - 14, 80, 22), $"→ {PointLabels[_clickCount]}");
            GUI.color = Color.white;
        }

        // ── HUD パネル ──────────────────────────────────────
        int done = 0; foreach (var r in _roofs) if (r.confirmed) done++;
        string pointStatus = _clickCount < 4 ? $"{_clickCount+1}/4  next: {PointLabels[_clickCount]}" : "DONE (4/4)";

        float hudW = 340, hudH = 150;
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.DrawTexture(new Rect(8, 8, hudW, hudH), _fill);
        GUI.color = Color.white;

        GUIStyle big = new GUIStyle(GUI.skin.label);
        big.fontSize = 15;
        big.normal.textColor = Color.white;

        GUIStyle hi = new GUIStyle(big);
        hi.normal.textColor = Color.yellow;

        // 選択中屋根（黄色・大きく）
        GUI.Label(new Rect(14, 12, hudW-10, 24), $"SELECTED:  {RoofIds[_activeRoof]}", hi);
        GUI.Label(new Rect(14, 38, hudW-10, 22), $"POINT:     {pointStatus}", big);
        // confirmed カウント（Load後は6/6になるはず）
        GUIStyle confStyle = new GUIStyle(big);
        confStyle.normal.textColor = done == 6 ? Color.green : Color.yellow;
        GUI.Label(new Rect(14, 62, hudW-10, 22), $"CONFIRMED: {done} / 6  (press L to load)", confStyle);

        // 保存ステータス（緑 or 赤）
        if (_saveStatus != "")
        {
            GUIStyle sv = new GUIStyle(big);
            sv.normal.textColor = _saveStatus.StartsWith("SAVED") ? Color.green : Color.red;
            GUI.Label(new Rect(14, 86, hudW-10, 22), _saveStatus, sv);
        }

        GUI.color = new Color(1f,1f,1f,0.6f);
        GUI.Label(new Rect(14, 112, hudW-10, 20), "1-6:select  LClick:point  R:reset  S:save  L:load");
        GUI.color = Color.white;
    }

    // ── 保存 / 読み込み ─────────────────────────────────────
    // 新ルール: 4点 complete (confirmed=true かつ全点非ゼロ) の roof だけ JSON に書く
    void Save()
    {
        var fullPath = Path.GetFullPath(SavePath);
        var completeRoofs = new List<RoofPoints>();

        foreach (var r in _roofs)
        {
            bool complete = r.confirmed &&
                            r.topLeft != Vector2.zero && r.topRight != Vector2.zero &&
                            r.bottomRight != Vector2.zero && r.bottomLeft != Vector2.zero;
            if (complete)
            {
                completeRoofs.Add(r);
                Debug.Log($"[CALIB_SAVE_COMPLETE] roof={r.id} tl=({r.topLeft.x:F4},{r.topLeft.y:F4}) tr=({r.topRight.x:F4},{r.topRight.y:F4}) br=({r.bottomRight.x:F4},{r.bottomRight.y:F4}) bl=({r.bottomLeft.x:F4},{r.bottomLeft.y:F4})");
                if (r.id == "Roof_TL") _tlSavePoints = MakeKey(r);
            }
            else
            {
                int pts = CountInputtedPoints(r);
                Debug.Log($"[CALIB_SAVE_DEFERRED] roof={r.id} points={pts}/4 → not written to JSON");
            }
        }

        var sd = new SaveData { roofs = completeRoofs };
        var json = JsonUtility.ToJson(sd, true);
        File.WriteAllText(SavePath, json);

        Debug.Log($"[ROOF_SAVE_OK] path={fullPath} complete={completeRoofs.Count}/6");
        _saveStatus = $"SAVED  {completeRoofs.Count}/6  →  {SavePath}";
        _saveStatusTimer = 5f;
    }

    void Load()
    {
        if (!File.Exists(SavePath)) { Debug.LogWarning($"[CALIB] load_failed=no_file path={SavePath}"); return; }
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        int loaded_count = 0;
        for (int i = 0; i < 6; i++)
        {
            var loaded = sd.roofs.Find(r => r.id == RoofIds[i]);
            // 新ルール: 4点すべて非ゼロのエントリだけ採用
            bool valid = loaded != null &&
                         loaded.topLeft != Vector2.zero && loaded.topRight != Vector2.zero &&
                         loaded.bottomRight != Vector2.zero && loaded.bottomLeft != Vector2.zero;
            if (valid)
            {
                _roofs[i] = loaded;
                _roofs[i].confirmed = true;
                NormalizePoints(_roofs[i]);
                loaded_count++;
            }
            else if (loaded != null)
            {
                Debug.LogWarning($"[CALIB_LOAD_SKIP] roof={RoofIds[i]} reason=zero_point_in_json → ignored");
            }
        }
        _clickCount = CountInputtedPoints(_roofs[_activeRoof]);
        var fullPath = Path.GetFullPath(SavePath);
        Debug.Log($"[CALIB] loaded=true path={fullPath} count={loaded_count}");
        // JSON から読み込んだ4点を全軒出力（4段階診断ログ）
        foreach (var r in _roofs)
        {
            bool hasData = r.confirmed &&
                           r.topLeft != Vector2.zero && r.topRight != Vector2.zero &&
                           r.bottomRight != Vector2.zero && r.bottomLeft != Vector2.zero;

            // [CALIB_JSON_READ] 全軒
            Debug.Log($"[CALIB_JSON_READ] roof={r.id} confirmed={r.confirmed} has_data={hasData} tl=({r.topLeft.x:F4},{r.topLeft.y:F4}) tr=({r.topRight.x:F4},{r.topRight.y:F4}) br=({r.bottomRight.x:F4},{r.bottomRight.y:F4}) bl=({r.bottomLeft.x:F4},{r.bottomLeft.y:F4})");

            if (!hasData)
                Debug.LogWarning($"[CALIB_JSON_READ_ZERO] roof={r.id} reason=not_calibrated_yet  → キー{System.Array.IndexOf(RoofIds, r.id)+1}で4点入力してSキーで保存してください");

            // Roof_TL 専用: ロード直後の検証ログ
            if (r.id == "Roof_TL")
            {
                _tlLoadPoints = MakeKey(r);
                Debug.Log($"[CALIB_LOAD_POINTS] roof=Roof_TL tl=({r.topLeft.x:F4},{r.topLeft.y:F4}) tr=({r.topRight.x:F4},{r.topRight.y:F4}) br=({r.bottomRight.x:F4},{r.bottomRight.y:F4}) bl=({r.bottomLeft.x:F4},{r.bottomLeft.y:F4})");
            }
        }

        int rendered = 0;
        foreach (var r in _roofs) if (r.confirmed) rendered++;
        Debug.Log($"[CALIB_RENDER_OK] roofs={rendered}");
        _saveStatus = $"LOADED  {loaded_count}/6  →  {SavePath}";
        _saveStatusTimer = 5f;
        _renderLogDone = false; // 次の OnGUI で描画ログを出す
        BuildAndInjectRoofDefinitions();
    }


    // ── Polygon → RoofDefinition 注入 ──────────────────────
    // キャリブレーション済み4点から RoofDefinition を生成し
    // RoofDefinitionProvider に注入する。
    // BackgroundImage の3Dワールド座標を使って normalized → world 変換を行う。
    void BuildAndInjectRoofDefinitions()
    {
        // BackgroundImage の4隅ワールド座標を取得
        var bgGo = GameObject.Find("BackgroundImage");
        var cam = Camera.main;
        if (bgGo == null || cam == null)
        {
            Debug.LogWarning("[POLYGON_ROOF_SHAPE] BackgroundImage not found – cannot inject RoofDefinitions");
            return;
        }

        var t = bgGo.transform;
        // BackgroundImage の4隅（ローカル座標 ±0.5）をワールド座標に変換
        Vector3 wTL = t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        // normalized (u,v) → ワールド座標（双線形補間）
        // u=0→左端, u=1→右端, v=0→上端, v=1→下端
        System.Func<Vector2, Vector3> normToWorld = (n) =>
        {
            Vector3 top    = Vector3.Lerp(wTL, wTR, n.x);
            Vector3 bottom = Vector3.Lerp(wBL, wBR, n.x);
            return Vector3.Lerp(top, bottom, n.y);
        };

        // 屋根面の法線（BackgroundImage の forward）
        Vector3 roofNormal = -t.forward; // 手前向き（カメラ方向）
        if (Vector3.Dot(roofNormal, Vector3.up) < 0f) roofNormal = -roofNormal;

        int injected = 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== POLYGON TO ROOF SHAPE ===");
        sb.AppendLine("calibration_pipeline_confirmed=YES");
        sb.AppendLine("polygon_verified=YES");

        string[] roofLabels = { "TL", "TM", "TR", "BL", "BM", "BR" };

        for (int i = 0; i < 6; i++)
        {
            var r = _roofs[i];
            bool hasData = r.confirmed &&
                           r.topLeft != Vector2.zero && r.topRight != Vector2.zero &&
                           r.bottomRight != Vector2.zero && r.bottomLeft != Vector2.zero;

            string label = roofLabels[i];

            if (!hasData)
            {
                sb.AppendLine($"roof_shape_bound_to_{label}=NO");
                Debug.LogWarning($"[POLYGON_ROOF_SHAPE] roof={r.id} skipped=no_data");
                continue;
            }

            // 4点をワールド座標に変換
            Vector3 wPtTL = normToWorld(r.topLeft);
            Vector3 wPtTR = normToWorld(r.topRight);
            Vector3 wPtBR = normToWorld(r.bottomRight);
            Vector3 wPtBL = normToWorld(r.bottomLeft);

            // 中心
            Vector3 origin = (wPtTL + wPtTR + wPtBR + wPtBL) * 0.25f;

            // 幅: top edge の長さ と bottom edge の長さの平均
            float topW    = Vector3.Distance(wPtTL, wPtTR);
            float bottomW = Vector3.Distance(wPtBL, wPtBR);
            float width   = (topW + bottomW) * 0.5f;

            // 奥行き: left edge と right edge の長さの平均
            float leftD  = Vector3.Distance(wPtTL, wPtBL);
            float rightD = Vector3.Distance(wPtTR, wPtBR);
            float depth  = (leftD + rightD) * 0.5f;

            // R 方向（横方向）: top edge の向き
            Vector3 roofR = (wPtTR - wPtTL).normalized;
            if (roofR.sqrMagnitude < 1e-6f) roofR = Vector3.right;

            // 傾斜方向: top → bottom（downhill）
            Vector3 topCenter    = (wPtTL + wPtTR) * 0.5f;
            Vector3 bottomCenter = (wPtBL + wPtBR) * 0.5f;
            Vector3 downhill     = (bottomCenter - topCenter).normalized;
            if (downhill.sqrMagnitude < 1e-6f) downhill = Vector3.forward;

            // slopeAngle: downhill と水平面のなす角
            float slopeAngle = Vector3.Angle(downhill, Vector3.ProjectOnPlane(downhill, Vector3.up).normalized);

            var def = new RoofDefinition
            {
                width         = Mathf.Max(0.1f, width),
                depth         = Mathf.Max(0.1f, depth),
                slopeAngle    = slopeAngle,
                slopeDirection = downhill,
                roofOrigin    = origin,
                roofNormal    = roofNormal,
                roofR         = roofR,
                roofF         = Vector3.Cross(roofNormal, roofR).normalized,
                roofDownhill  = downhill,
                isValid       = true,
                useExactRoofSize = true,
            };

            RoofDefinitionProvider.SetFromExternal(i, def);
            injected++;

            Debug.Log($"[POLYGON_ROOF_SHAPE] roof={r.id} houseIndex={i} origin=({origin.x:F2},{origin.y:F2},{origin.z:F2}) width={width:F3} depth={depth:F3} slopeAngle={slopeAngle:F1} injected=true");
            sb.AppendLine($"roof_shape_bound_to_{label}=YES");
        }

        sb.AppendLine($"snow_spawn_inside_polygon_only={(injected > 0 ? "YES" : "NO")}");
        sb.AppendLine($"all_6_ok={(injected == 6 ? "YES" : "NO")}");
        sb.AppendLine($"result={(injected > 0 ? "PASS" : "FAIL")}");

        SnowLoopLogCapture.AppendToAssiReport(sb.ToString());
        Debug.Log($"[POLYGON_ROOF_SHAPE] inject_done={injected}/6 all_ok={(injected == 6)}");

        // SnowPackSpawner を全軒リビルド
        var spawners = UnityEngine.Object.FindObjectsByType<SnowPackSpawner>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var sp in spawners)
        {
            if (sp != null && sp.isActiveAndEnabled)
            {
                sp.Rebuild();
                Debug.Log($"[POLYGON_ROOF_SHAPE] spawner_rebuilt houseIndex={sp.houseIndex}");
            }
        }
    }

    // ── ユーティリティ ───────────────────────────────────────
    // ── 4点正規化 ──────────────────────────────────────────
    // top 2点: x が小さい方を TL、大きい方を TR
    // bottom 2点: x が小さい方を BL、大きい方を BR
    static void NormalizePoints(RoofPoints r)
    {
        // top pair
        if (r.topLeft.x > r.topRight.x)
        {
            var tmp = r.topLeft; r.topLeft = r.topRight; r.topRight = tmp;
        }
        // bottom pair
        if (r.bottomLeft.x > r.bottomRight.x)
        {
            var tmp = r.bottomLeft; r.bottomLeft = r.bottomRight; r.bottomRight = tmp;
        }
        Debug.Log($"[CALIB_POINTS_NORMALIZED] roof={r.id} tl=({r.topLeft.x:F3},{r.topLeft.y:F3}) tr=({r.topRight.x:F3},{r.topRight.y:F3}) br=({r.bottomRight.x:F3},{r.bottomRight.y:F3}) bl=({r.bottomLeft.x:F3},{r.bottomLeft.y:F3})");
    }

    static void SetPoint(RoofPoints r, int idx, Vector2 v)
    {
        switch (idx) {
            case 0: r.topLeft     = v; break;
            case 1: r.topRight    = v; break;
            case 2: r.bottomRight = v; break;
            case 3: r.bottomLeft  = v; break;
        }
    }

    static Vector2[] GetPoints(RoofPoints r) =>
        new[] { r.topLeft, r.topRight, r.bottomRight, r.bottomLeft };

    // ── ポリゴン検証 ─────────────────────────────────────────
    // 正規化済み前提。ゼロ点チェックのみ。
    static bool ValidateQuad(RoofPoints r, out string skipReason)
    {
        if (r.topLeft == Vector2.zero || r.topRight == Vector2.zero ||
            r.bottomRight == Vector2.zero || r.bottomLeft == Vector2.zero)
        {
            skipReason = "zero_point";
            return false;
        }
        skipReason = "";
        return true;
    }

    // 4点を比較用文字列キーに変換（F4精度）
    static string MakeKey(RoofPoints r) =>
        $"{r.topLeft.x:F4},{r.topLeft.y:F4}|{r.topRight.x:F4},{r.topRight.y:F4}|{r.bottomRight.x:F4},{r.bottomRight.y:F4}|{r.bottomLeft.x:F4},{r.bottomLeft.y:F4}";

    // Load 後に入力済み点数を復元するためだけに使う（起動時は使わない）
    static int CountInputtedPoints(RoofPoints r)
    {
        if (r.confirmed) return 4;
        int c = 0;
        if (r.topLeft     != Vector2.zero) c++;
        if (r.topRight    != Vector2.zero) c++;
        if (r.bottomRight != Vector2.zero) c++;
        if (r.bottomLeft  != Vector2.zero) c++;
        return c;
    }

    // normalized (0..1) → OnGUI screen pixel
    // BackgroundImage の投影矩形が有効なら bgRect 基準、無効なら全画面基準
    Vector2 NormToScreen(Vector2 n)
    {
        if (_bgRectValid)
            return new Vector2(
                _bgRect.x + n.x * _bgRect.width,
                _bgRect.y + n.y * _bgRect.height);
        return new Vector2(n.x * Screen.width, n.y * Screen.height);
    }

    // 点ごとの色（TL=赤 TR=緑 BR=青 BL=黄）
    static Color PointColor(string label)
    {
        switch (label)
        {
            case "TL": return new Color(1f, 0.2f, 0.2f, 1f);
            case "TR": return new Color(0.2f, 1f, 0.2f, 1f);
            case "BR": return new Color(0.2f, 0.5f, 1f, 1f);
            case "BL": return new Color(1f, 1f, 0.1f, 1f);
            default:   return Color.white;
        }
    }

    // 大型十字マーカー (R=24px) + 16px 塗り + ラベル fontSize=22
    void DrawPointMarker(Vector2 n, Color roofCol, string label)
    {
        var s  = NormToScreen(n);
        var pc = PointColor(label);
        const float R  = 24f;  // 十字の半径
        const float TH = 6f;   // 十字の太さ
        const float SQ = 16f;  // 中心■のサイズ

        // 十字（水平）
        GUI.color = pc;
        GUI.DrawTexture(new Rect(s.x - R, s.y - TH * 0.5f, R * 2f, TH), _fill);
        // 十字（垂直）
        GUI.DrawTexture(new Rect(s.x - TH * 0.5f, s.y - R, TH, R * 2f), _fill);
        // 中心■（白）
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(s.x - SQ * 0.5f, s.y - SQ * 0.5f, SQ, SQ), _fill);

        // ラベル（黒縁取り風に白を重ねる）
        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.fontSize = 22;
        st.fontStyle = FontStyle.Bold;
        // 影（黒）
        st.normal.textColor = Color.black;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            if (dx != 0 || dy != 0)
                GUI.Label(new Rect(s.x + 14f + dx, s.y - 14f + dy, 50f, 28f), label, st);
        // 本体（点色）
        st.normal.textColor = pc;
        GUI.Label(new Rect(s.x + 14f, s.y - 14f, 50f, 28f), label, st);

        GUI.color = Color.white;
    }

    void DrawDotNorm(Vector2 n, Color col)
    {
        var s = NormToScreen(n);
        GUI.color = col;
        GUI.DrawTexture(new Rect(s.x - 5, s.y - 5, 10, 10), _dot);
        GUI.color = Color.white;
    }

    void DrawLabelNorm(Vector2 n, string label)
    {
        var s = NormToScreen(n);
        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.fontSize = 20;
        st.fontStyle = FontStyle.Bold;
        // 黒縁
        st.normal.textColor = Color.black;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            if (dx != 0 || dy != 0)
                GUI.Label(new Rect(s.x + 4 + dx, s.y - 24 + dy, 120, 26), label, st);
        // 白本体
        st.normal.textColor = Color.white;
        GUI.Label(new Rect(s.x + 4, s.y - 24, 120, 26), label, st);
        GUI.color = Color.white;
    }

    void DrawQuadNorm(Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl, Color col)
    {
        Vector2 sTL = NormToScreen(tl);
        Vector2 sTR = NormToScreen(tr);
        Vector2 sBR = NormToScreen(br);
        Vector2 sBL = NormToScreen(bl);

        float yMin = Mathf.Min(sTL.y, sTR.y, sBR.y, sBL.y);
        float yMax = Mathf.Max(sTL.y, sTR.y, sBR.y, sBL.y);
        if (yMax - yMin < 1f) return;

        var drawCol = new Color(col.r, col.g, col.b, Mathf.Max(col.a, 0.55f));
        GUI.color = drawCol;
        for (float y = yMin; y <= yMax; y += 1f)
        {
            float t = (y - yMin) / (yMax - yMin);
            float lx = Mathf.Lerp(sTL.x, sBL.x, t);
            float rx = Mathf.Lerp(sTR.x, sBR.x, t);
            if (lx > rx) { float tmp = lx; lx = rx; rx = tmp; }
            if (rx - lx > 0.5f) GUI.DrawTexture(new Rect(lx, y, rx - lx, 2f), _fill);
        }
        GUI.color = Color.white;
        DrawLine(sTL, sTR); DrawLine(sTR, sBR);
        DrawLine(sBR, sBL); DrawLine(sBL, sTL);
    }

    // normalized → screen pixel (OnGUI 座標: 左上原点) ※旧関数・互換用
    static Vector2 N2S(Vector2 n, float sw, float sh) => new Vector2(n.x * sw, n.y * sh);

    void DrawDot(Vector2 n, Color col, float sw, float sh)
    {
        var s = N2S(n, sw, sh);
        GUI.color = col;
        GUI.DrawTexture(new Rect(s.x - 5, s.y - 5, 10, 10), _dot);
        GUI.color = Color.white;
    }

    void DrawLabel(Vector2 n, string label, float sw, float sh)
    {
        var s = N2S(n, sw, sh);
        GUI.color = Color.white;
        GUI.Label(new Rect(s.x + 4, s.y - 2, 80, 18), label);
    }

    // 台形を行スキャンで塗りつぶす
    // 引数: tl=topLeft, tr=topRight, br=bottomRight, bl=bottomLeft (normalized 0..1)
    void DrawQuad(Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl, Color col, float sw, float sh)
    {
        // screen pixel 座標に変換（OnGUI: 左上原点）
        Vector2 sTL = N2S(tl, sw, sh);
        Vector2 sTR = N2S(tr, sw, sh);
        Vector2 sBR = N2S(br, sw, sh);
        Vector2 sBL = N2S(bl, sw, sh);

        float yMin = Mathf.Min(sTL.y, sTR.y, sBR.y, sBL.y);
        float yMax = Mathf.Max(sTL.y, sTR.y, sBR.y, sBL.y);
        if (yMax - yMin < 1f) return;

        // アルファを強めにして視認性を確保
        var drawCol = new Color(col.r, col.g, col.b, Mathf.Max(col.a, 0.55f));
        GUI.color = drawCol;

        // 1px ずつスキャン（確実に描画）
        for (float y = yMin; y <= yMax; y += 1f)
        {
            float t = (y - yMin) / (yMax - yMin);
            // 左辺: TL→BL、右辺: TR→BR
            float lx = Mathf.Lerp(sTL.x, sBL.x, t);
            float rx = Mathf.Lerp(sTR.x, sBR.x, t);
            if (lx > rx) { float tmp = lx; lx = rx; rx = tmp; }
            float w = rx - lx;
            if (w > 0.5f)
                GUI.DrawTexture(new Rect(lx, y, w, 2f), _fill);
        }
        GUI.color = Color.white;

        // 輪郭線（白）で4辺を描く
        DrawLine(sTL, sTR); // 上辺
        DrawLine(sTR, sBR); // 右辺
        DrawLine(sBR, sBL); // 下辺
        DrawLine(sBL, sTL); // 左辺
    }

    // 2点間に細い線を描く（1px 幅の矩形を並べる）
    void DrawLine(Vector2 a, Vector2 b)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        int steps = Mathf.Max(1, (int)Vector2.Distance(a, b));
        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            var p = Vector2.Lerp(a, b, t);
            GUI.DrawTexture(new Rect(p.x - 1, p.y - 1, 2, 2), _fill);
        }
        GUI.color = Color.white;
    }

    static Texture2D MakeTex(int w, int h, Color c)
    {
        var t = new Texture2D(w, h);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnDestroy()
    {
        if (_dot  != null) Destroy(_dot);
        if (_fill != null) Destroy(_fill);
    }
}
