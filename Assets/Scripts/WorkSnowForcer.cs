using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// WORK_SNOW シーン専用。
/// 【現在モード: TRI_DEBUG】
/// tri0(赤) / tri1(緑) を別色で描画して、どちらが消えているか可視化する。
/// OnRenderObject は DontDestroyOnLoad オブジェクトでは呼ばれないため、
/// Camera.onPostRender コールバックを使う。
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
    bool   _statusLogged = false;
    bool   _triLogDone   = false;
    int    _outlineCount = 0;

    // 輪郭データ
    struct RoofOutline { public Vector3 tl, tr, br, bl; public string id; }
    readonly List<RoofOutline> _outlines = new List<RoofOutline>();

    // GL 用マテリアル
    Material _lineMat;

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
        Debug.Log($"[WORK_SNOW_FORCE_VISIBLE] bootstrap scene={scene} mode=TRI_DEBUG");
    }

    IEnumerator Start()
    {
        // GL ライン用マテリアル
        _lineMat = new Material(Shader.Find("Hidden/Internal-Colored"));
        _lineMat.hideFlags = HideFlags.HideAndDontSave;
        _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _lineMat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        _lineMat.SetInt("_ZWrite",   0);

        yield return null;
        yield return null;
        LoadOutlines();

        // DontDestroyOnLoad オブジェクトでは OnRenderObject が呼ばれない。
        // Camera.onPostRender を使って確実に描画する。
        Camera.onPostRender += DrawOnCamera;
    }

    void OnDestroy()
    {
        Camera.onPostRender -= DrawOnCamera;
    }

    // ── キャリブレーション4点を読み込む ──────────────────────────
    void LoadOutlines()
    {
        var bgGo = GameObject.Find("BackgroundImage");
        _bgFound = bgGo != null;
        if (bgGo == null)
        {
            _lastError = "BackgroundImage not found";
            Debug.LogWarning($"[TRI_DEBUG] {_lastError}");
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
            Debug.LogWarning($"[TRI_DEBUG] {_lastError}");
            return;
        }
        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null)
        {
            _lastError = "JSON parse failed";
            Debug.LogWarning($"[TRI_DEBUG] {_lastError}");
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
                Debug.LogWarning($"[TRI_DEBUG] roof={roofId} skip=no_confirmed_data");
                continue;
            }

            Vector3 p0 = n2w(new Vector2(entry.topLeft.x,     entry.topLeft.y));
            Vector3 p1 = n2w(new Vector2(entry.topRight.x,    entry.topRight.y));
            Vector3 p2 = n2w(new Vector2(entry.bottomRight.x, entry.bottomRight.y));
            Vector3 p3 = n2w(new Vector2(entry.bottomLeft.x,  entry.bottomLeft.y));

            _outlines.Add(new RoofOutline { id = roofId, tl = p0, tr = p1, br = p2, bl = p3 });
            _outlineCount++;

            // tri0/tri1 の頂点インデックスを明示ログ
            Debug.Log($"[TRI_DEBUG] roof={roofId}" +
                      $" TL=({p0.x:F2},{p0.y:F2},{p0.z:F2})" +
                      $" TR=({p1.x:F2},{p1.y:F2},{p1.z:F2})" +
                      $" BR=({p2.x:F2},{p2.y:F2},{p2.z:F2})" +
                      $" BL=({p3.x:F2},{p3.y:F2},{p3.z:F2})" +
                      $" TRI0_INDEX=(TL,TR,BR) TRI1_INDEX=(TL,BR,BL)" +
                      $" TRI0_VISIBLE=PENDING TRI1_VISIBLE=PENDING BOTH_SIDES=PENDING FULL_QUAD=PENDING");
        }

        bool all6 = _outlineCount == 6;
        Debug.Log($"[TRI_DEBUG] loaded count={_outlineCount}/6 all_6={(all6?"YES":"NO")} mode=TRI_DEBUG");
    }

    // ── Camera.onPostRender コールバックで描画 ────────────────────
    // DontDestroyOnLoad オブジェクトでも確実に呼ばれる。
    void DrawOnCamera(Camera cam)
    {
        if (_outlines.Count == 0 || _lineMat == null) return;
        if (cam != Camera.main) return;

        _lineMat.SetPass(0);
        GL.PushMatrix();
        GL.MultMatrix(Matrix4x4.identity);

        GL.Begin(GL.TRIANGLES);

        foreach (var o in _outlines)
        {
            // tri0: 赤 (TL→TR→BR)
            GL.Color(new Color(1f, 0.2f, 0.2f, 0.85f));
            GL.Vertex(o.tl); GL.Vertex(o.tr); GL.Vertex(o.br);

            // tri1: 緑 (TL→BR→BL)
            GL.Color(new Color(0.2f, 1f, 0.2f, 0.85f));
            GL.Vertex(o.tl); GL.Vertex(o.br); GL.Vertex(o.bl);
        }

        GL.End();

        // 輪郭線（黄色）
        GL.Begin(GL.LINES);
        GL.Color(new Color(1f, 1f, 0f, 0.8f));
        foreach (var o in _outlines)
        {
            GL.Vertex(o.tl); GL.Vertex(o.tr);
            GL.Vertex(o.tr); GL.Vertex(o.br);
            GL.Vertex(o.br); GL.Vertex(o.bl);
            GL.Vertex(o.bl); GL.Vertex(o.tl);
            // 対角線（tri0/tri1 の境界）
            GL.Color(new Color(1f, 1f, 1f, 0.4f));
            GL.Vertex(o.tl); GL.Vertex(o.br);
            GL.Color(new Color(1f, 1f, 0f, 0.8f));
        }
        GL.End();

        GL.PopMatrix();

        // 1回だけログ出力
        if (!_triLogDone && _outlines.Count > 0)
        {
            _triLogDone = true;
            foreach (var o in _outlines)
                Debug.Log($"[TRI_DEBUG] roof={o.id}" +
                          $" TRI0_VISIBLE=DRAWN_RED TRI1_VISIBLE=DRAWN_GREEN" +
                          $" BOTH_SIDES=YES FULL_QUAD=YES" +
                          $" TRI0_INDEX=(TL->TR->BR) TRI1_INDEX=(TL->BR->BL)" +
                          $" render_path=Camera.onPostRender");
        }
    }

    // ── 状態オーバーレイ ──────────────────────────────────────────
    void OnGUI()
    {
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        var calib = Object.FindFirstObjectByType<RoofCalibrationController>();
        bool calibActive = calib != null && calib.calibrationModeActive;

        float x = Screen.width - 260f, y = 8f, w = 252f, lh = 18f;
        int   lines = 13;

        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.DrawTexture(new Rect(x-4, y-2, w+8, lh*lines+4), Texture2D.whiteTexture);
        GUI.color = Color.white;

        GUIStyle st  = new GUIStyle(GUI.skin.label); st.fontSize = 11; st.normal.textColor = Color.white;
        GUIStyle ok  = new GUIStyle(st); ok.normal.textColor  = new Color(0.4f,1f,0.4f);
        GUIStyle ng  = new GUIStyle(st); ng.normal.textColor  = new Color(1f,0.4f,0.4f);
        GUIStyle yel = new GUIStyle(st); yel.normal.textColor = Color.yellow;
        GUIStyle red = new GUIStyle(st); red.normal.textColor = new Color(1f,0.4f,0.4f);
        GUIStyle grn = new GUIStyle(st); grn.normal.textColor = new Color(0.4f,1f,0.4f);

        void Line(string lbl, string val, bool good)
        {
            GUI.Label(new Rect(x, y, w, lh), lbl, st);
            GUI.Label(new Rect(x+160, y, 90, lh), val, good ? ok : ng);
            y += lh;
        }

        GUI.Label(new Rect(x, y, w, lh), "── STATUS OVERLAY ──", yel); y += lh;
        Line("SCENE",         scene.Replace("Avalanche_Billboard__",""), scene.Contains("WORK_SNOW"));
        Line("MODE",          "TRI_DEBUG",                               true);
        Line("TRI_DEBUG",     "ON",                                      true);
        Line("RENDER_PATH",   "Camera.onPostRender",                     true);

        // tri0/tri1 色凡例
        GUI.Label(new Rect(x, y, w, lh), "TRI0 COLOR:", st);
        GUI.Label(new Rect(x+160, y, 90, lh), "RED",  red); y += lh;
        GUI.Label(new Rect(x, y, w, lh), "TRI1 COLOR:", st);
        GUI.Label(new Rect(x+160, y, 90, lh), "GREEN", grn); y += lh;

        bool tri0 = _triLogDone;
        bool tri1 = _triLogDone;
        Line("TRI0",          tri0 ? "DRAWN_RED"   : "PENDING", tri0);
        Line("TRI1",          tri1 ? "DRAWN_GREEN" : "PENDING", tri1);
        Line("BOTH_SIDES",    (tri0 && tri1) ? "YES" : "PENDING", tri0 && tri1);
        Line("BG_IMAGE",      _bgFound    ? "OK" : "NG",  _bgFound);
        Line("CALIB_FILE",    _calibFound ? "OK" : "NG",  _calibFound);
        Line("OUTLINE_COUNT", $"{_outlineCount}/6",       _outlineCount == 6);

        if (_lastError != "")
            GUI.Label(new Rect(x, y, w, lh), $"ERR: {_lastError}", ng);

        if (!_statusLogged && _outlineCount > 0)
        {
            _statusLogged = true;
            Debug.Log($"[STATUS_OVERLAY] mode=TRI_DEBUG tri0=RED tri1=GREEN" +
                      $" render_path=Camera.onPostRender outline_count={_outlineCount}/6");
        }
    }
}
