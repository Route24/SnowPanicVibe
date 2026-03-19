using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// SnowStrip V2 — Single Source of Truth 設計（1軒限定・最小実装）
///
/// 対象: Roof_BR（下段右）のみ。他5軒は WorkSnowForcer V1 が管理する。
///
/// 設計原則:
///   - snowFill (0〜1) が唯一の真実。
///   - 表示雪は snowFill からのみ決まる。外から書き換えない。
///   - タップ → snowFill 減少 → 落雪生成 の一方通行。
///   - fill=0 なら絶対に落雪しない。
///   - RoofGuide_BR Image は完全に無効化し、OnGUI で描画する。
/// </summary>
[DefaultExecutionOrder(10)] // WorkSnowForcer より少し後に実行
public class SnowStripV2 : MonoBehaviour
{
    // ── 定数 ──────────────────────────────────────────────────
    const string CALIB_PATH          = "Assets/Art/RoofCalibrationData.json";
    const string TARGET_ROOF_ID      = "Roof_BR";
    const string TARGET_GUIDE_ID     = "RoofGuide_BR";
    const float  UNDER_EAVE_OFFSET   = 0.10f;  // eaveGuiY オフセット（calib 座標）
    const float  THICK_RATIO         = 0.65f;  // 雪帯高さ / 屋根高さ
    const float  EXPAND_Y_MAX        = 14f;    // fill=1 時の上方拡張 px

    static readonly Color SnowWhite = new Color(0.92f, 0.95f, 1f);

    // ── 内部状態 ──────────────────────────────────────────────
    float   _snowFill  = 1f;   // 唯一の残雪量（0〜1）。これだけを真実とする。
    bool    _ready     = false;
    int     _tapCount  = 0;    // タップ累計カウント（確認ログ用）
    string  _lastSpawnInfo = "---"; // 直近タップの spawn 結果テキスト
    Rect    _guiRect;          // 屋根の OnGUI 座標矩形
    float   _eaveGuiY;         // 落雪着地 Y（OnGUI 座標）
    float   _eaveGuiX;         // 落雪着地 X（OnGUI 座標）
    Vector2 _downhillDir;      // 正規化済み下り方向ベクトル
    Texture2D _whiteTex;

    // ── 落下雪塊 ──────────────────────────────────────────────
    struct Piece
    {
        public Vector2 pos;
        public Vector2 vel;
        public float   size;
        public float   life;
        public float   alpha;
    }
    readonly System.Collections.Generic.List<Piece> _pieces
        = new System.Collections.Generic.List<Piece>();

    // ── Serialize 用 ──────────────────────────────────────────
    [System.Serializable] class V2 { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2 topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    // ── ライフサイクル ────────────────────────────────────────
    void OnEnable()
    {
        _whiteTex = new Texture2D(1, 1);
        _whiteTex.SetPixel(0, 0, Color.white);
        _whiteTex.Apply();
    }

    void OnDestroy()
    {
        if (_whiteTex != null) { Object.DestroyImmediate(_whiteTex); _whiteTex = null; }
    }

    void Start()
    {
        if (!Application.isPlaying) return;

        Debug.Log($"[V2_ALIVE] SnowStripV2 started. target={TARGET_ROOF_ID}" +
                  $" scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}" +
                  $" gameObject={gameObject.name}");

        // RoofGuide_BR の Image を完全無効化（トップライン雪の原因を根本から断つ）
        var guideGo = GameObject.Find(TARGET_GUIDE_ID);
        if (guideGo != null)
        {
            var img = guideGo.GetComponent<Image>();
            if (img != null)
            {
                img.enabled = false;
                img.color   = Color.clear;
                Debug.Log($"[V2_GUIDE_IMAGE_OFF] id={TARGET_GUIDE_ID} forced_off=YES");
            }
        }

        BuildRoofData();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (!_ready)
        {
            // Screen.width/height が確定するまで待つ
            if (Screen.width > 1 && Screen.height > 1)
                BuildRoofData();
            return;
        }

        HandleTap();
        UpdatePieces();
    }

    // ── ルーフデータ構築 ─────────────────────────────────────
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

        // calib 座標 (0〜1) → OnGUI ピクセル座標（Y はそのまま: 0=上端）
        _guiRect = new Rect(
            minX * Screen.width,
            minY * Screen.height,
            (maxX - minX) * Screen.width,
            (maxY - minY) * Screen.height
        );

        float eaveCalibY = maxY + UNDER_EAVE_OFFSET;
        _eaveGuiY = Mathf.Min(eaveCalibY * Screen.height, Screen.height - 2f);
        _eaveGuiX = ((minX + maxX) * 0.5f) * Screen.width;

        // downhill ベクトル（屋根上端中心 → 下端中心）
        float topCenterX = ((entry.topLeft.x + entry.topRight.x) * 0.5f) * Screen.width;
        float topCenterY = ((entry.topLeft.y + entry.topRight.y) * 0.5f) * Screen.height;
        float botCenterX = ((entry.bottomLeft.x + entry.bottomRight.x) * 0.5f) * Screen.width;
        float botCenterY = ((entry.bottomLeft.y + entry.bottomRight.y) * 0.5f) * Screen.height;
        var dhRaw = new Vector2(botCenterX - topCenterX, botCenterY - topCenterY);
        float dhLen = dhRaw.magnitude;
        _downhillDir = dhLen > 0.5f ? dhRaw.normalized : new Vector2(0f, 1f);

        _ready = true;
        Debug.Log($"[V2_ROOF_READY] roof={TARGET_ROOF_ID} guiRect={_guiRect} eaveGuiY={_eaveGuiY:F1}" +
                  $" downhill=({_downhillDir.x:F3},{_downhillDir.y:F3})");
    }

    // ── タップ処理 ────────────────────────────────────────────
    // Single Source of Truth:
    //   1. snowFill で判定（>0 → 落雪許可、<=0 → 完全禁止）
    //   2. 落雪量 delta を先に決め、snowFill を減らしてから落雪生成
    //   3. 表示は OnGUI で snowFill からのみ計算 → 同フレームで反映
    void HandleTap()
    {
        if (!_ready) return;

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

        // Input 座標 (左下原点) → OnGUI 座標 (左上原点)
        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        if (!_guiRect.Contains(guiPos)) return;

        float fillBefore = _snowFill;
        _tapCount++;

        // ── fill=0 なら完全ブロック ─────────────────────────────
        if (fillBefore <= 0f)
        {
            _lastSpawnInfo = $"TAP#{_tapCount} fill=0.00 spawned=NO";
            Debug.Log($"[V2_TAP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                      $" fill_before={fillBefore:F3} fill_after={fillBefore:F3}" +
                      $" delta=0.000 spawned=NO reason=fill_empty [V2_SPAWN_BLOCKED]");
            return;
        }

        // ── 崩落量を決定（snowFill と比例）───────────────────────
        int   spawnCount    = Random.Range(1, 4);
        float deltaPerPiece = Random.Range(0.06f, 0.12f);
        float delta         = Mathf.Min(deltaPerPiece * spawnCount, fillBefore);

        // snowFill を先に減らす（表示は同フレームの OnGUI で自動反映）
        _snowFill = Mathf.Max(0f, fillBefore - delta);
        float fillAfter = _snowFill;

        // ── 落雪生成 ─────────────────────────────────────────────
        float roofW  = _guiRect.width;
        float spawnX = Mathf.Clamp(guiPos.x, _guiRect.x + 10f, _guiRect.xMax - 10f);
        float spawnY = _guiRect.y;

        for (int i = 0; i < spawnCount; i++)
        {
            float jx  = Random.Range(-roofW * 0.15f, roofW * 0.15f);
            float sz  = Mathf.Clamp(roofW * Random.Range(0.12f, 0.28f), 20f, 70f);
            float spd = Random.Range(80f, 200f);

            _pieces.Add(new Piece
            {
                pos   = new Vector2(spawnX + jx, spawnY),
                vel   = new Vector2(_downhillDir.x * spd * 0.5f, _downhillDir.y * spd),
                size  = sz,
                life  = 5f,
                alpha = 1f,
            });
        }

        // 表示 thickH を先計算（OnGUI と同じ式）
        float expY  = EXPAND_Y_MAX * fillAfter;
        float dispH = (_guiRect.height * THICK_RATIO * fillAfter) + expY;

        _lastSpawnInfo = $"TAP#{_tapCount} fill={fillAfter:F2} spawned={spawnCount}";

        Debug.Log($"[V2_TAP#{_tapCount}] roof={TARGET_ROOF_ID}" +
                  $" fill_before={fillBefore:F3} fill_after={fillAfter:F3}" +
                  $" delta={delta:F3} spawned={spawnCount} spawn=YES" +
                  $" display_thickH={dispH:F1}px");

        if (fillAfter <= 0f)
            Debug.Log($"[V2_TAP#{_tapCount}] roof={TARGET_ROOF_ID} fill=0.000 all_removed=YES spawned=NO_next_tap");
    }

    // ── 落下雪塊の更新 ────────────────────────────────────────
    void UpdatePieces()
    {
        float dt = Time.deltaTime;
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            var p = _pieces[i];
            p.vel.y += 520f * dt; // 重力
            p.pos   += p.vel * dt;
            p.life  -= dt;
            p.alpha  = Mathf.Clamp01(p.life * 0.8f);

            // 軒下着地
            if (p.pos.y >= _eaveGuiY)
            {
                p.pos.y = _eaveGuiY;
                p.vel   = Vector2.zero;
                p.life  = Mathf.Min(p.life, 1.2f); // 着地後に短くフェード
                Debug.Log($"[V2_UNDER_EAVE_STOP] roof={TARGET_ROOF_ID} pos=({p.pos.x:F1},{p.pos.y:F1})");
            }

            if (p.life <= 0f)
                _pieces.RemoveAt(i);
            else
                _pieces[i] = p;
        }
    }

    // ── 描画 ─────────────────────────────────────────────────
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        if (!_ready || _whiteTex == null) return;

        // ── [V2確認モード] 雪帯をシアン色で描画（他軒と一目で区別）──
        // ★確認完了後に SnowWhite に戻す★
        var debugColor = new Color(0.0f, 0.9f, 0.85f, 0.90f); // シアン

        // ── 屋根雪帯（snowFill からのみ決まる）─────────────────
        if (_snowFill > 0f)
        {
            float expandY = EXPAND_Y_MAX * _snowFill;
            float thickH  = (_guiRect.height * THICK_RATIO * _snowFill) + expandY;
            float roofTop = _guiRect.y - expandY;

            if (thickH >= 1f)
            {
                // 本体（確認用シアン）
                GUI.color = debugColor;
                GUI.DrawTexture(new Rect(_guiRect.x, roofTop, _guiRect.width, thickH), _whiteTex);

                // 上端に "V2" 表示代わりの濃いシアンライン
                GUI.color = new Color(0.0f, 1f, 1f, 1f);
                GUI.DrawTexture(new Rect(_guiRect.x, roofTop, _guiRect.width, 3f), _whiteTex);
            }
        }
        else
        {
            // fill=0: 背景色でトップライン雪を上書き消去
            GUI.color = new Color(0.45f, 0.55f, 0.72f, 0.90f);
            GUI.DrawTexture(new Rect(_guiRect.x, _guiRect.y - 18f, _guiRect.width, 22f), _whiteTex);
            // fill=0 確認: 赤いラインを表示
            GUI.color = new Color(1f, 0.2f, 0.2f, 0.85f);
            GUI.DrawTexture(new Rect(_guiRect.x, _guiRect.y - 4f, _guiRect.width, 4f), _whiteTex);
        }

        // ── 落下雪塊（確認用: シアン色）────────────────────────
        foreach (var p in _pieces)
        {
            if (p.alpha <= 0f) continue;
            GUI.color = new Color(0.0f, 0.9f, 0.85f, p.alpha);
            float half = p.size * 0.5f;
            GUI.DrawTexture(new Rect(p.pos.x - half, p.pos.y - half, p.size, p.size), _whiteTex);
        }

        // ── fillゲージをデバッグ表示（屋根左端に細い白バー）───
        GUI.color = new Color(1f, 1f, 0f, 0.85f); // 黄色バー
        float barH = _guiRect.height * _snowFill;
        GUI.DrawTexture(new Rect(_guiRect.x - 6f, _guiRect.yMax - barH, 5f, barH), _whiteTex);

        // ── デバッグテキスト（屋根の直下に3行表示）────────────────
        GUI.color = Color.black;
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 11,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.UpperLeft,
        };
        float tx = _guiRect.x;
        float ty = _guiRect.yMax + 4f;
        // 背景黒帯
        GUI.color = new Color(0f, 0f, 0f, 0.60f);
        GUI.DrawTexture(new Rect(tx, ty, 160f, 38f), _whiteTex);
        // テキスト
        GUI.color = Color.cyan;
        GUI.Label(new Rect(tx + 2f, ty + 1f,  158f, 14f),
                  $"[V2] {TARGET_ROOF_ID}", style);
        GUI.color = Color.yellow;
        GUI.Label(new Rect(tx + 2f, ty + 13f, 158f, 14f),
                  $"fill={_snowFill:F2}  taps={_tapCount}", style);
        GUI.color = _snowFill <= 0f ? Color.red : Color.white;
        GUI.Label(new Rect(tx + 2f, ty + 25f, 158f, 14f),
                  _lastSpawnInfo, style);

        GUI.color = Color.white;
    }
}
