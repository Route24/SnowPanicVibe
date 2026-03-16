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

    // ── 可視化 ──────────────────────────────────────────────
    Texture2D _dot;
    Texture2D _fill;
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
            var norm = new Vector2(pos.x / Screen.width, 1f - pos.y / Screen.height);
            SetPoint(_roofs[_activeRoof], _clickCount, norm);
            _clickCount++;
            Debug.Log($"[CALIB] roof={RoofIds[_activeRoof]} point={PointLabels[_clickCount-1]}({_clickCount}/4) norm=({norm.x:F3},{norm.y:F3})");
            if (_clickCount == 4)
            {
                _roofs[_activeRoof].confirmed = true;
                var r = _roofs[_activeRoof];
                Debug.Log($"[ROOF_CAPTURE_DONE] roof={r.id} points=4 TL=({r.topLeft.x:F3},{r.topLeft.y:F3}) TR=({r.topRight.x:F3},{r.topRight.y:F3}) BR=({r.bottomRight.x:F3},{r.bottomRight.y:F3}) BL=({r.bottomLeft.x:F3},{r.bottomLeft.y:F3})");
            }
        }
    }

    // ── OnGUI ────────────────────────────────────────────────
    void OnGUI()
    {
        float sw = Screen.width, sh = Screen.height;

        for (int i = 0; i < 6; i++)
        {
            var r = _roofs[i];
            var col = RoofColors[i];

            // 確定済みのみ台形表示（Load 後 or 4点入力後のみ）
            if (r.confirmed)
            {
                // 描画直前に座標ログ（Load 直後の1回のみ）
                if (!_renderLogDone)
                {
                    Debug.Log($"[CALIB_RENDER_POINTS] roof={r.id} tl=({r.topLeft.x:F3},{r.topLeft.y:F3}) tr=({r.topRight.x:F3},{r.topRight.y:F3}) br=({r.bottomRight.x:F3},{r.bottomRight.y:F3}) bl=({r.bottomLeft.x:F3},{r.bottomLeft.y:F3})");
                    // JSON値との一致確認（_roofs は Load で上書き済みなので同一のはずだが念のため）
                    var fromJson = _roofs[i]; // 同じ参照
                    bool match = (fromJson.topLeft == r.topLeft && fromJson.topRight == r.topRight &&
                                  fromJson.bottomRight == r.bottomRight && fromJson.bottomLeft == r.bottomLeft);
                    if (!match)
                        Debug.LogWarning($"[CALIB_MISMATCH] roof={r.id} json_vs_render differ");
                }
                DrawQuad(r.topLeft, r.topRight, r.bottomRight, r.bottomLeft, col, sw, sh);
                DrawLabel(r.topLeft, r.id, sw, sh);
            }

            // アクティブ屋根: 入力済みの点をドット表示
            if (i == _activeRoof && !r.confirmed)
            {
                var pts = GetPoints(r);
                for (int p = 0; p < _clickCount; p++)
                    DrawDot(pts[p], col, sw, sh);
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
    void Save()
    {
        var sd = new SaveData { roofs = new List<RoofPoints>(_roofs) };
        var json = JsonUtility.ToJson(sd, true);
        var fullPath = Path.GetFullPath(SavePath);
        File.WriteAllText(SavePath, json);
        int done = 0; foreach (var r in _roofs) if (r.confirmed) done++;
        Debug.Log($"[ROOF_SAVE_OK] path={fullPath} confirmed={done}/6");
        _saveStatus = $"SAVED  {done}/6  →  {SavePath}";
        _saveStatusTimer = 5f;
        foreach (var r in _roofs)
            if (r.confirmed)
                Debug.Log($"[CALIB_DATA] id={r.id} TL=({r.topLeft.x:F3},{r.topLeft.y:F3}) TR=({r.topRight.x:F3},{r.topRight.y:F3}) BR=({r.bottomRight.x:F3},{r.bottomRight.y:F3}) BL=({r.bottomLeft.x:F3},{r.bottomLeft.y:F3})");
    }

    void Load()
    {
        if (!File.Exists(SavePath)) { Debug.LogWarning($"[CALIB] load_failed=no_file path={SavePath}"); return; }
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        int loaded_count = 0;
        for (int i = 0; i < 6; i++)
        {
            var loaded = sd.roofs.Find(r => r.id == RoofIds[i]);
            if (loaded != null)
            {
                _roofs[i] = loaded;
                _roofs[i].confirmed = true; // JSON から復元後に必ず confirmed=true にする
                loaded_count++;
            }
        }
        _clickCount = CountInputtedPoints(_roofs[_activeRoof]);
        var fullPath = Path.GetFullPath(SavePath);
        Debug.Log($"[CALIB] loaded=true path={fullPath} count={loaded_count}");
        // 描画確認ログ
        // JSON から読み込んだ4点を全軒出力
        foreach (var r in _roofs)
            Debug.Log($"[CALIB_POINTS] roof={r.id} confirmed={r.confirmed} tl=({r.topLeft.x:F3},{r.topLeft.y:F3}) tr=({r.topRight.x:F3},{r.topRight.y:F3}) br=({r.bottomRight.x:F3},{r.bottomRight.y:F3}) bl=({r.bottomLeft.x:F3},{r.bottomLeft.y:F3})");

        int rendered = 0;
        foreach (var r in _roofs) if (r.confirmed) rendered++;
        Debug.Log($"[CALIB_RENDER_OK] roofs={rendered}");
        _saveStatus = $"LOADED  {loaded_count}/6  →  {SavePath}";
        _saveStatusTimer = 5f;
        _renderLogDone = false; // 次の OnGUI で描画ログを出す
    }

    // ── ユーティリティ ───────────────────────────────────────
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

    // normalized → screen pixel (OnGUI 座標: 左上原点)
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
