using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// WORK_SNOW シーン専用。
/// 【現在モード: DEBUG_VISIBILITY_RECOVERY】
/// 観測手段を最もシンプルな方法で復旧する。
/// GL / Camera.onPostRender は使わず、
/// OnGUI + Camera.WorldToScreenPoint だけで頂点マーカーと輪郭を描画する。
/// 落下・地面・SnowPackSpawner・GroundSnowSystem には一切触らない。
/// </summary>
public class WorkSnowForcer : MonoBehaviour
{
    const string SCENE_NAME     = "Avalanche_Billboard__WORK_SNOW";
    const string CALIB_PATH     = "Assets/Art/RoofCalibrationData.json";
    const float  FORWARD_OFFSET = 0.12f;

    static readonly string[] RoofIds =
        { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    // ── 状態 ──────────────────────────────────────────────────────
    bool   _calibFound   = false;
    bool   _bgFound      = false;
    string _lastError    = "";
    bool   _consoleLogged = false;
    int    _outlineCount = 0;

    // 輪郭データ
    struct RoofOutline { public Vector3 tl, tr, br, bl; public string id; }
    readonly List<RoofOutline> _outlines = new List<RoofOutline>();

    // マーカー描画用テクスチャ（1x1 単色）
    Texture2D _markerTex;

    // ── JSON デシリアライズ用 ──────────────────────────────────────
    [System.Serializable] class V2 { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2 topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    // ── Bootstrap ─────────────────────────────────────────────────
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene != SCENE_NAME && !scene.Contains("WORK_SNOW")) return;

        var go = new GameObject("WorkSnowForcer");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<WorkSnowForcer>();
        Debug.Log($"[WORK_SNOW_FORCE_VISIBLE] bootstrap scene={scene} mode=DEBUG_VISIBILITY_RECOVERY");
    }

    IEnumerator Start()
    {
        // 単色マーカーテクスチャ作成
        _markerTex = new Texture2D(1, 1);
        _markerTex.SetPixel(0, 0, Color.white);
        _markerTex.Apply();

        yield return null;
        yield return null;
        LoadOutlines();
    }

    // ── キャリブレーション4点を読み込む ──────────────────────────
    void LoadOutlines()
    {
        var bgGo = GameObject.Find("BackgroundImage");
        _bgFound = bgGo != null;
        if (bgGo == null)
        {
            _lastError = "BackgroundImage not found";
            Debug.LogWarning($"[DEBUG_VISIBLE] {_lastError} DRAW_OK=NO");
            return;
        }
        var bgT = bgGo.transform;

        Vector3 wTL = bgT.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = bgT.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = bgT.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = bgT.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        var cam = Camera.main;
        Vector3 camFwd = cam != null
            ? (cam.transform.position - bgT.position).normalized
            : -bgT.forward;

        System.Func<Vector2, Vector3> n2w = (n) =>
        {
            Vector3 top    = Vector3.Lerp(wTL, wTR, n.x);
            Vector3 bottom = Vector3.Lerp(wBL, wBR, n.x);
            return Vector3.Lerp(top, bottom, n.y) + camFwd * FORWARD_OFFSET;
        };

        _calibFound = File.Exists(CALIB_PATH);
        if (!_calibFound)
        {
            _lastError = $"calib not found: {CALIB_PATH}";
            Debug.LogWarning($"[DEBUG_VISIBLE] {_lastError} DRAW_OK=NO");
            return;
        }
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null)
        {
            _lastError = "JSON parse failed";
            Debug.LogWarning($"[DEBUG_VISIBLE] {_lastError} DRAW_OK=NO");
            return;
        }

        _outlines.Clear();
        _outlineCount = 0;

        foreach (var roofId in RoofIds)
        {
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == roofId) { entry = r; break; }

            if (entry == null || !entry.confirmed)
            {
                Debug.LogWarning($"[DEBUG_VISIBLE] roof={roofId} skip=no_confirmed_data DRAW_OK=NO");
                continue;
            }

            Vector3 p0 = n2w(new Vector2(entry.topLeft.x,     entry.topLeft.y));
            Vector3 p1 = n2w(new Vector2(entry.topRight.x,    entry.topRight.y));
            Vector3 p2 = n2w(new Vector2(entry.bottomRight.x, entry.bottomRight.y));
            Vector3 p3 = n2w(new Vector2(entry.bottomLeft.x,  entry.bottomLeft.y));

            _outlines.Add(new RoofOutline { id = roofId, tl = p0, tr = p1, br = p2, bl = p3 });
            _outlineCount++;

            Debug.Log($"[DEBUG_VISIBLE] roof={roofId}" +
                      $" TL=({p0.x:F2},{p0.y:F2},{p0.z:F2})" +
                      $" TR=({p1.x:F2},{p1.y:F2},{p1.z:F2})" +
                      $" BR=({p2.x:F2},{p2.y:F2},{p2.z:F2})" +
                      $" BL=({p3.x:F2},{p3.y:F2},{p3.z:F2})" +
                      $" DRAW_OK=YES");
        }

        bool all6 = _outlineCount == 6;
        Debug.Log($"[DEBUG_VISIBLE] loaded count={_outlineCount}/6 all_6={(all6?"YES":"NO")} mode=DEBUG_VISIBILITY_RECOVERY");
    }

    // ── OnGUI: ワールド座標 → スクリーン座標変換でマーカー描画 ──
    // GL も Camera.onPostRender も使わない。絶対に動く方法。
    void OnGUI()
    {
        var cam = Camera.main;

        // ── 右上オーバーレイ ──────────────────────────────────────
        float x = Screen.width - 260f, y = 8f, w = 252f, lh = 18f;
        int   lines = 12;

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(x-4, y-2, w+8, lh*lines+4), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle st  = new GUIStyle(GUI.skin.label); st.fontSize = 11; st.normal.textColor = Color.white;
        GUIStyle ok  = new GUIStyle(st); ok.normal.textColor  = new Color(0.4f,1f,0.4f);
        GUIStyle ng  = new GUIStyle(st); ng.normal.textColor  = new Color(1f,0.4f,0.4f);
        GUIStyle yel = new GUIStyle(st); yel.normal.textColor = Color.yellow;

        void Line(string lbl, string val, bool good)
        {
            GUI.Label(new Rect(x, y, w, lh), lbl, st);
            GUI.Label(new Rect(x+160, y, 90, lh), val, good ? ok : ng);
            y += lh;
        }

        GUI.Label(new Rect(x, y, w, lh), "── STATUS OVERLAY ──", yel); y += lh;
        Line("SCENE",              "WORK_SNOW",                              true);
        Line("MODE",               "DEBUG_VISIBILITY_RECOVERY",              true);
        Line("DEBUG_RENDER_PATH",  "ONGUI+WorldToScreen",                    true);
        Line("BG_IMAGE",           _bgFound    ? "OK" : "NG",                _bgFound);
        Line("CALIB_FILE",         _calibFound ? "OK" : "NG",                _calibFound);
        Line("OUTLINE_COUNT",      $"{_outlineCount}/6",                     _outlineCount == 6);
        Line("DEBUG_VISIBLE_ALL6", _outlineCount == 6 ? "YES" : "NO",        _outlineCount == 6);
        Line("CONSOLE_LOG_OK",     _consoleLogged ? "YES" : "PENDING",       _consoleLogged);

        if (_lastError != "")
            GUI.Label(new Rect(x, y, w, lh*2), $"ERR: {_lastError}", ng);

        if (!_consoleLogged && _outlineCount > 0)
        {
            _consoleLogged = true;
            Debug.Log($"[STATUS_OVERLAY] mode=DEBUG_VISIBILITY_RECOVERY" +
                      $" debug_render_path=ONGUI+WorldToScreen" +
                      $" outline_count={_outlineCount}/6" +
                      $" debug_visible_all6={((_outlineCount==6)?"YES":"NO")}" +
                      $" console_log_ok=YES");
        }

        // ── 頂点マーカーとラベルを描画（OnGUI Screen-space） ──────
        if (cam == null || _outlines.Count == 0) return;

        // 屋根ごとの色
        Color[] roofColors = new Color[]
        {
            new Color(1f, 0.3f, 0.3f, 1f),   // TL: 赤
            new Color(1f, 0.9f, 0.1f, 1f),   // TM: 黄
            new Color(0.3f, 1f, 0.3f, 1f),   // TR: 緑
            new Color(0.3f, 0.8f, 1f, 1f),   // BL: 水色
            new Color(1f, 0.4f, 1f, 1f),     // BM: ピンク
            new Color(1f, 0.6f, 0.2f, 1f),   // BR: オレンジ
        };

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
        labelStyle.fontSize = 10;
        labelStyle.fontStyle = FontStyle.Bold;

        for (int i = 0; i < _outlines.Count; i++)
        {
            var o = _outlines[i];
            Color c = i < roofColors.Length ? roofColors[i] : Color.white;

            // ワールド座標 → スクリーン座標（Y軸反転）
            Vector3 sTL = WorldToGUI(cam, o.tl);
            Vector3 sTR = WorldToGUI(cam, o.tr);
            Vector3 sBR = WorldToGUI(cam, o.br);
            Vector3 sBL = WorldToGUI(cam, o.bl);

            // カメラの前方にある点だけ描画（z > 0）
            if (sTL.z < 0 || sTR.z < 0 || sBR.z < 0 || sBL.z < 0) continue;

            // 頂点マーカー（8x8 の塗りつぶし矩形）
            const float ms = 8f;
            GUI.color = c;
            DrawMarker(sTL, ms, "TL");
            DrawMarker(sTR, ms, "TR");
            DrawMarker(sBR, ms, "BR");
            DrawMarker(sBL, ms, "BL");
            GUI.color = Color.white;

            // 屋根名ラベル（中心）
            Vector3 center = (sTL + sTR + sBR + sBL) / 4f;
            labelStyle.normal.textColor = c;
            GUI.Label(new Rect(center.x - 25, center.y - 8, 60, 18), o.id.Replace("Roof_",""), labelStyle);

            // 輪郭線（GL.DrawTexture で細い線を模擬）
            GUI.color = new Color(c.r, c.g, c.b, 0.8f);
            DrawLine(sTL, sTR, 2f);
            DrawLine(sTR, sBR, 2f);
            DrawLine(sBR, sBL, 2f);
            DrawLine(sBL, sTL, 2f);
            // 対角線（薄く）
            GUI.color = new Color(c.r, c.g, c.b, 0.35f);
            DrawLine(sTL, sBR, 1f);
            GUI.color = Color.white;
        }
    }

    // ── ヘルパー: ワールド座標 → GUI座標（Y反転） ────────────────
    static Vector3 WorldToGUI(Camera cam, Vector3 world)
    {
        Vector3 s = cam.WorldToScreenPoint(world);
        s.y = Screen.height - s.y;
        return s;
    }

    // ── ヘルパー: 頂点マーカー描画 ───────────────────────────────
    void DrawMarker(Vector3 sp, float size, string label)
    {
        GUI.DrawTexture(new Rect(sp.x - size/2, sp.y - size/2, size, size), _markerTex);
    }

    // ── ヘルパー: GUI 上で線を描画（細い矩形で近似） ─────────────
    static void DrawLine(Vector3 a, Vector3 b, float thickness)
    {
        float dx = b.x - a.x;
        float dy = b.y - a.y;
        float len = Mathf.Sqrt(dx*dx + dy*dy);
        if (len < 0.001f) return;

        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
        float cx = (a.x + b.x) / 2f;
        float cy = (a.y + b.y) / 2f;

        GUIUtility.RotateAroundPivot(angle, new Vector2(cx, cy));
        GUI.DrawTexture(new Rect(cx - len/2, cy - thickness/2, len, thickness), Texture2D.whiteTexture);
        GUIUtility.RotateAroundPivot(-angle, new Vector2(cx, cy));
    }
}
