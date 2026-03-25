using UnityEngine;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// Manual Calibration Controller（1軒モード対応版）
///
/// 操作:
///   モード選択:
///     キー1 = 屋根キャリブレーションモード（4点: TL→TR→BR→BL）
///     キー2 = 地面キャリブレーションモード（1点: 地面ライン）
///   左クリック = 点を追加
///   R = 現在モードのリセット
///   S = JSON保存
///   L = JSON読み込み
///
/// 保存先: Assets/Art/RoofCalibrationData.json
/// </summary>
public class RoofCalibrationController : MonoBehaviour
{
    // ── データ ─────────────────────────────────────────────────
    [System.Serializable]
    public class RoofPoints
    {
        public string id;
        public Vector2 topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }

    [System.Serializable]
    class SaveData { public List<RoofPoints> roofs; public float groundY; }

    static readonly string SavePath = "Assets/Art/RoofCalibrationData.json";

    // ── Inspector ──────────────────────────────────────────────
    [Header("Calibration Mode")]
    [Tooltip("false = ゲームモード。true = キャリブレーション作業時のみ ON にする。")]
    public bool calibrationModeActive = false;

    // ── 状態 ──────────────────────────────────────────────────
    enum CalibMode { Roof, Ground }
    CalibMode _mode = CalibMode.Roof;

    // 屋根 (Roof_Main 1件)
    RoofPoints _roof;
    int _clickCount = 0;

    // 地面Y (normalized 0..1)
    float _groundY = -1f;   // -1 = 未設定

    string _status = "";
    float  _statusTimer = 0f;

    // 可視化
    Texture2D _fill;
    Rect      _bgRect;
    bool      _bgRectValid = false;

    static readonly string[] PointLabels = { "TL", "TR", "BR", "BL" };

    // 点色
    static readonly Color[] PtColors =
    {
        new Color(1f, 0.2f, 0.2f, 1f),   // TL 赤
        new Color(0.2f, 1f, 0.2f, 1f),   // TR 緑
        new Color(0.3f, 0.6f, 1f, 1f),   // BR 青
        new Color(1f, 1f, 0.1f, 1f),     // BL 黄
    };

    // ── Unity ─────────────────────────────────────────────────
    void Awake()
    {
        _roof = new RoofPoints { id = "Roof_Main" };
        _fill = MakeTex(1, 1, Color.white);
        Debug.Log("[CALIB] 1-roof mode  key1=roof  key2=ground  LClick=add  R=reset  S=save  L=load");
    }

    void Update()
    {
        if (!calibrationModeActive) return;

        UpdateBgRect();

        // モード切替
        if (Input.GetKeyDown(KeyCode.Alpha1)) { _mode = CalibMode.Roof;   Debug.Log("[CALIB] mode=ROOF"); }
        if (Input.GetKeyDown(KeyCode.Alpha2)) { _mode = CalibMode.Ground; Debug.Log("[CALIB] mode=GROUND"); }

        // H: 積雪表示トグル（キャリブ時に屋根の角を見やすくする）
        if (Input.GetKeyDown(KeyCode.H)) ToggleSnow();

        // R: リセット
        if (Input.GetKeyDown(KeyCode.R))
        {
            if (_mode == CalibMode.Roof)
            {
                _roof = new RoofPoints { id = "Roof_Main" };
                _clickCount = 0;
                Debug.Log("[CALIB] reset=Roof_Main");
            }
            else
            {
                _groundY = -1f;
                Debug.Log("[CALIB] reset=ground");
            }
        }

        // S / L
        if (Input.GetKeyDown(KeyCode.S)) Save();
        if (Input.GetKeyDown(KeyCode.L)) Load();

        // タイマー
        if (_statusTimer > 0f) _statusTimer -= Time.deltaTime;
        else _status = "";

        // 左クリック
        if (Input.GetMouseButtonDown(0))
        {
            Vector2 norm = MouseToNorm();

            if (_mode == CalibMode.Roof && !_roof.confirmed && _clickCount < 4)
            {
                SetPoint(_roof, _clickCount, norm);
                _clickCount++;
                Debug.Log($"[CALIB] roof=Roof_Main point={PointLabels[_clickCount-1]}({_clickCount}/4) norm=({norm.x:F4},{norm.y:F4}) screen=({Screen.width}x{Screen.height}) bgRect_valid={_bgRectValid} bgRect=({_bgRect.x:F1},{_bgRect.y:F1},{_bgRect.width:F1},{_bgRect.height:F1})");

                if (_clickCount == 4)
                {
                    _roof.confirmed = true;
                    NormalizePoints(_roof);
                    Debug.Log($"[ROOF_CAPTURE_DONE] roof=Roof_Main TL=({_roof.topLeft.x:F4},{_roof.topLeft.y:F4}) TR=({_roof.topRight.x:F4},{_roof.topRight.y:F4}) BR=({_roof.bottomRight.x:F4},{_roof.bottomRight.y:F4}) BL=({_roof.bottomLeft.x:F4},{_roof.bottomLeft.y:F4})");
                    _status = "ROOF 4-point DONE  → S to save";
                    _statusTimer = 8f;
                }
            }
            else if (_mode == CalibMode.Ground)
            {
                _groundY = norm.y;
                Debug.Log($"[CALIB] ground_y={_groundY:F4} screen_y={Input.mousePosition.y:F0}");
                _status = $"GROUND  ground_y={_groundY:F4}  → S to save";
                _statusTimer = 8f;
            }
        }
    }

    // ── OnGUI ─────────────────────────────────────────────────
    void OnGUI()
    {
        if (!calibrationModeActive) return;

        float sw = Screen.width, sh = Screen.height;

        // ── 屋根オーバーレイ ──
        if (_roof.confirmed)
        {
            DrawPointMarker(_roof.topLeft,     PtColors[0], "TL");
            DrawPointMarker(_roof.topRight,    PtColors[1], "TR");
            DrawPointMarker(_roof.bottomRight, PtColors[2], "BR");
            DrawPointMarker(_roof.bottomLeft,  PtColors[3], "BL");
            DrawQuadOutline(_roof);
        }
        else
        {
            // 入力途中の点
            var pts = new[] { _roof.topLeft, _roof.topRight, _roof.bottomRight, _roof.bottomLeft };
            for (int p = 0; p < _clickCount; p++)
                DrawPointMarker(pts[p], PtColors[p], PointLabels[p]);
        }

        // ── 地面ライン ──
        if (_groundY >= 0f)
        {
            float gy = _groundY * sh;
            GUI.color = new Color(0.2f, 1f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(0, gy - 2, sw, 4), _fill);
            GUI.color = Color.white;
            GUIStyle lbst = new GUIStyle(GUI.skin.label);
            lbst.fontSize = 18; lbst.fontStyle = FontStyle.Bold;
            lbst.normal.textColor = new Color(0.2f, 1f, 1f, 1f);
            GUI.Label(new Rect(8, gy + 4, 200, 24), $"GROUND  y={_groundY:F4}", lbst);
        }

        // ── マウスカーソルラベル ──
        if (_mode == CalibMode.Roof && _clickCount < 4 && !_roof.confirmed)
        {
            var mp = Input.mousePosition;
            GUI.color = Color.yellow;
            GUI.Label(new Rect(mp.x + 14, sh - mp.y - 16, 100, 22),
                      $"→ {PointLabels[_clickCount]}");
            GUI.color = Color.white;
        }
        else if (_mode == CalibMode.Ground)
        {
            var mp = Input.mousePosition;
            GUI.color = new Color(0.2f, 1f, 1f);
            GUI.Label(new Rect(mp.x + 14, sh - mp.y - 16, 180, 22), "→ GROUND click here");
            GUI.color = Color.white;
        }

        // ── HUD パネル ──
        float hudW = 380, hudH = 200;
        GUI.color = new Color(0, 0, 0, 0.75f);
        GUI.DrawTexture(new Rect(8, 8, hudW, hudH), _fill);
        GUI.color = Color.white;

        GUIStyle big = new GUIStyle(GUI.skin.label);
        big.fontSize = 15; big.normal.textColor = Color.white;
        GUIStyle hi = new GUIStyle(big);
        hi.normal.textColor = Color.yellow;
        GUIStyle ok = new GUIStyle(big);
        ok.normal.textColor = Color.green;
        GUIStyle cyan = new GUIStyle(big);
        cyan.normal.textColor = new Color(0.2f, 1f, 1f);

        string modeStr = _mode == CalibMode.Roof ? "ROOF (key1)" : "GROUND (key2)";
        GUI.Label(new Rect(14, 12, hudW - 10, 24), $"MODE:  {modeStr}", hi);

        // 屋根状態
        string roofSt = _roof.confirmed ? "CONFIRMED ✓" : $"{_clickCount}/4  next:{PointLabels[Mathf.Min(_clickCount,3)]}";
        GUI.Label(new Rect(14, 38, hudW - 10, 22), $"ROOF:  {roofSt}", _roof.confirmed ? ok : big);

        // 地面状態
        string gndSt = _groundY >= 0f ? $"SET  y={_groundY:F4}  ✓" : "NOT SET";
        GUI.Label(new Rect(14, 62, hudW - 10, 22), $"GROUND: {gndSt}", _groundY >= 0f ? cyan : big);

        // ステータス
        if (_status != "")
        {
            GUIStyle svSt = new GUIStyle(big);
            svSt.normal.textColor = _status.StartsWith("SAVED") || _status.StartsWith("LOADED") ? Color.green : Color.yellow;
            GUI.Label(new Rect(14, 88, hudW - 10, 22), _status, svSt);
        }

        // 積雪非表示中の警告
        if (_snowHidden)
        {
            GUIStyle warn = new GUIStyle(GUI.skin.label);
            warn.fontSize = 20; warn.fontStyle = FontStyle.Bold;
            warn.normal.textColor = new Color(1f, 0.4f, 0.1f, 1f);
            GUI.Label(new Rect(14, 160, hudW - 10, 26), "⚠ SNOW HIDDEN  (H to restore)", warn);
        }

        GUI.color = new Color(1, 1, 1, 0.55f);
        GUI.Label(new Rect(14, 116, hudW - 10, 20), "key1=roof  key2=ground  LClick=point  R=reset");
        GUI.Label(new Rect(14, 138, hudW - 10, 20), "S=save  L=load  H=snow hide/show");
        GUI.color = Color.white;
    }

    // ── 積雪表示トグル ─────────────────────────────────────────
    bool _snowHidden = false;

    void ToggleSnow()
    {
        _snowHidden = !_snowHidden;
        var strips = Object.FindObjectsByType<SnowStrip2D>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var s in strips)
            s.enabled = !_snowHidden;
        Debug.Log($"[CALIB] snow_hidden={_snowHidden} strips={strips.Length}");
    }

    // ── Save / Load ────────────────────────────────────────────
    void Save()
    {
        var roofList = new List<RoofPoints>();
        if (_roof.confirmed &&
            _roof.topLeft != Vector2.zero && _roof.topRight != Vector2.zero &&
            _roof.bottomRight != Vector2.zero && _roof.bottomLeft != Vector2.zero)
        {
            roofList.Add(_roof);
            Debug.Log($"[CALIB_SAVE] roof=Roof_Main TL=({_roof.topLeft.x:F4},{_roof.topLeft.y:F4}) TR=({_roof.topRight.x:F4},{_roof.topRight.y:F4}) BR=({_roof.bottomRight.x:F4},{_roof.bottomRight.y:F4}) BL=({_roof.bottomLeft.x:F4},{_roof.bottomLeft.y:F4})");
        }
        else
        {
            Debug.LogWarning("[CALIB_SAVE] roof not complete – skipped");
        }

        var sd = new SaveData { roofs = roofList, groundY = _groundY };
        File.WriteAllText(SavePath, JsonUtility.ToJson(sd, true));

        string path = Path.GetFullPath(SavePath);
        Debug.Log($"[CALIB_SAVE_OK] path={path} roof_saved={roofList.Count > 0} ground_y={_groundY:F4}");
        _status = $"SAVED  roof={roofList.Count > 0}  ground={(_groundY >= 0f ? _groundY.ToString("F4") : "none")}";
        _statusTimer = 8f;

        // WorkSnowGameBootstrap の地面コライダーを即時更新
        ApplyGroundToBootstrap();
    }

    void Load()
    {
        if (!File.Exists(SavePath)) { Debug.LogWarning("[CALIB_LOAD] file not found"); return; }

        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        if (sd == null) { Debug.LogWarning("[CALIB_LOAD] parse failed"); return; }

        if (sd.roofs != null)
        {
            var r = sd.roofs.Find(x => x.id == "Roof_Main");
            if (r != null && r.topLeft != Vector2.zero && r.topRight != Vector2.zero &&
                r.bottomRight != Vector2.zero && r.bottomLeft != Vector2.zero)
            {
                _roof = r;
                _roof.confirmed = true;
                NormalizePoints(_roof);
                _clickCount = 4;
                Debug.Log($"[CALIB_LOAD] roof=Roof_Main TL=({_roof.topLeft.x:F4},{_roof.topLeft.y:F4}) TR=({_roof.topRight.x:F4},{_roof.topRight.y:F4}) BR=({_roof.bottomRight.x:F4},{_roof.bottomRight.y:F4}) BL=({_roof.bottomLeft.x:F4},{_roof.bottomLeft.y:F4})");
            }
        }

        // groundY の読み込み（フィールドがない古いJSONは0になるので-1扱い）
        _groundY = sd.groundY > 0f ? sd.groundY : -1f;
        Debug.Log($"[CALIB_LOAD] ground_y={_groundY:F4}");

        _status = $"LOADED  roof={_roof.confirmed}  ground={(_groundY >= 0f ? _groundY.ToString("F4") : "none")}";
        _statusTimer = 8f;
    }

    // ── 地面コライダー即時適用 ─────────────────────────────────
    void ApplyGroundToBootstrap()
    {
        if (_groundY < 0f) return;

        // ground_local_y = (0.5 - groundY) * BG_SCALE_Y
        const float BG_SCALE_Y = 8.5f;
        float localY = (0.5f - _groundY) * BG_SCALE_Y;

        // WorkSnow_Ground_Upper / Lower を探して更新
        foreach (var name in new[] { "WorkSnow_Ground_Upper", "WorkSnow_Ground_Lower" })
        {
            var go = GameObject.Find(name);
            if (go == null) continue;
            var pos = go.transform.localPosition;
            pos.y = localY;
            go.transform.localPosition = pos;
            Debug.Log($"[CALIB_GROUND_APPLY] name={name} local_y={localY:F4}");
        }

        Debug.Log($"[MANUAL_RECALIB] ground_y={_groundY:F4} ground_local_y={localY:F4}");
    }

    // ── ユーティリティ ─────────────────────────────────────────
    Vector2 MouseToNorm()
    {
        var pos = Input.mousePosition;
        if (_bgRectValid && _bgRect.width > 1f)
        {
            float guiX = pos.x;
            float guiY = Screen.height - pos.y;
            return new Vector2(
                (guiX - _bgRect.x) / _bgRect.width,
                (guiY - _bgRect.y) / _bgRect.height);
        }
        return new Vector2(pos.x / Screen.width, 1f - pos.y / Screen.height);
    }

    void UpdateBgRect()
    {
        var cam  = Camera.main;
        var bgGo = GameObject.Find("BackgroundImage");
        if (cam == null || bgGo == null) { _bgRectValid = false; return; }

        var t = bgGo.transform;
        float sh = Screen.height;
        Vector2 sTL = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f))); sTL.y = sh - sTL.y;
        Vector2 sTR = cam.WorldToScreenPoint(t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f))); sTR.y = sh - sTR.y;
        Vector2 sBL = cam.WorldToScreenPoint(t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f))); sBL.y = sh - sBL.y;
        Vector2 sBR = cam.WorldToScreenPoint(t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f))); sBR.y = sh - sBR.y;

        float minX = Mathf.Min(sTL.x, sBL.x);
        float maxX = Mathf.Max(sTR.x, sBR.x);
        float minY = Mathf.Min(sTL.y, sTR.y);
        float maxY = Mathf.Max(sBL.y, sBR.y);

        _bgRect = new Rect(minX, minY, maxX - minX, maxY - minY);
        _bgRectValid = _bgRect.width > 1f && _bgRect.height > 1f;
    }

    static void NormalizePoints(RoofPoints r)
    {
        if (r.topLeft.x > r.topRight.x)   { var t = r.topLeft;    r.topLeft    = r.topRight;    r.topRight    = t; }
        if (r.bottomLeft.x > r.bottomRight.x) { var t = r.bottomLeft; r.bottomLeft = r.bottomRight; r.bottomRight = t; }
    }

    static void SetPoint(RoofPoints r, int idx, Vector2 v)
    {
        switch (idx)
        {
            case 0: r.topLeft     = v; break;
            case 1: r.topRight    = v; break;
            case 2: r.bottomRight = v; break;
            case 3: r.bottomLeft  = v; break;
        }
    }

    // ── 描画 ────────────────────────────────────────────────────
    void DrawPointMarker(Vector2 n, Color col, string label)
    {
        var s = NormToScreen(n);
        const float R = 24f, TH = 6f, SQ = 16f;
        GUI.color = col;
        GUI.DrawTexture(new Rect(s.x - R, s.y - TH * 0.5f, R * 2f, TH), _fill);
        GUI.DrawTexture(new Rect(s.x - TH * 0.5f, s.y - R, TH, R * 2f), _fill);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(s.x - SQ * 0.5f, s.y - SQ * 0.5f, SQ, SQ), _fill);

        GUIStyle st = new GUIStyle(GUI.skin.label);
        st.fontSize = 22; st.fontStyle = FontStyle.Bold;
        st.normal.textColor = Color.black;
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            if (dx != 0 || dy != 0)
                GUI.Label(new Rect(s.x + 14f + dx, s.y - 14f + dy, 50f, 28f), label, st);
        st.normal.textColor = col;
        GUI.Label(new Rect(s.x + 14f, s.y - 14f, 50f, 28f), label, st);
        GUI.color = Color.white;
    }

    void DrawQuadOutline(RoofPoints r)
    {
        DrawLine(NormToScreen(r.topLeft),     NormToScreen(r.topRight));
        DrawLine(NormToScreen(r.topRight),    NormToScreen(r.bottomRight));
        DrawLine(NormToScreen(r.bottomRight), NormToScreen(r.bottomLeft));
        DrawLine(NormToScreen(r.bottomLeft),  NormToScreen(r.topLeft));
    }

    void DrawLine(Vector2 a, Vector2 b)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        int steps = Mathf.Max(1, (int)Vector2.Distance(a, b));
        for (int i = 0; i <= steps; i++)
        {
            var p = Vector2.Lerp(a, b, (float)i / steps);
            GUI.DrawTexture(new Rect(p.x - 1, p.y - 1, 2, 2), _fill);
        }
        GUI.color = Color.white;
    }

    Vector2 NormToScreen(Vector2 n)
    {
        if (_bgRectValid)
            return new Vector2(_bgRect.x + n.x * _bgRect.width, _bgRect.y + n.y * _bgRect.height);
        return new Vector2(n.x * Screen.width, n.y * Screen.height);
    }

    static Texture2D MakeTex(int w, int h, Color c)
    {
        var t = new Texture2D(w, h); t.SetPixel(0, 0, c); t.Apply(); return t;
    }

    void OnDestroy() { if (_fill != null) Destroy(_fill); }
}
