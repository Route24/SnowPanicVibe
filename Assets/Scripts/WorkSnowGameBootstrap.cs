using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// WORK_SNOW シーン専用。
/// 叩く → 落下 → 着地 → 積雪 のゲームループを起動する。
///
/// 起動内容:
/// 1. 上段・下段の地面コライダーを BackgroundImage の子として生成
/// 2. ゲームシステムを起動（TapToSlideOnRoof / SnowPackSpawner / GroundSnowSystem / SnowFallSystem）
/// 3. RoofDefinitionProvider に6軒の屋根定義を注入（キャリブレーションデータから）
///
/// BillboardSnowKiller は WORK_SNOW では KillAll しない（別途修正済み）。
/// </summary>
public class WorkSnowGameBootstrap : MonoBehaviour
{
    const float BG_SCALE_X = 15f;
    const float BG_SCALE_Y = 8.5f;

    // 上段地面: 上段屋根の下端（normalized y ≈ 0.316）の少し下
    const float UPPER_GROUND_LOCAL_Y = 1.264f;
    // 下段地面: 下段屋根の下端（normalized y ≈ 0.627）の少し下
    const float LOWER_GROUND_LOCAL_Y = -1.379f;

    const float GROUND_THICKNESS = 0.1f;
    const float GROUND_DEPTH     = 1f;

    static readonly string[] RoofIds =
        { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    // ── JSON デシリアライズ用（クラスレベルで定義） ──────────────
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
        string scene = SceneManager.GetActiveScene().name;
        if (!scene.Contains("WORK_SNOW")) return;

        // SnowTest を RuntimeInitialize タイミングで即無効化する
        // SnowPackSpawner.Awake()/Start() が走る前に SetActive(false) することで
        // roofCollider=null での RebuildSnowPack → 赤いキューブ生成を防ぐ
        // （WorkSnowGameBootstrap.Start() では遅すぎる）
        var snowTest = GameObject.Find("SnowTest");
        if (snowTest != null)
        {
            snowTest.SetActive(false);
            Debug.Log("[WORK_SNOW_GAME] SnowTest disabled at Bootstrap (prevents red cube spawn)");
        }

        if (Object.FindFirstObjectByType<WorkSnowGameBootstrap>() != null) return;

        var go = new GameObject("WorkSnowGameBootstrap");
        go.AddComponent<WorkSnowGameBootstrap>();
        Debug.Log($"[WORK_SNOW_GAME] Bootstrap started scene={scene}");
    }

    void Start()
    {
        SetupGroundColliders();
        InjectRoofDefinitions();
        SetupGameSystems();
        SetupDebugGlove();
        SetupShovelTool();
    }

    // ── 上段・下段の地面コライダーを生成 ──────────────────────────
    void SetupGroundColliders()
    {
        var bgGo = GameObject.Find("BackgroundImage");
        if (bgGo == null)
        {
            Debug.LogWarning("[WORK_SNOW_GAME] BackgroundImage not found – ground colliders skipped");
            return;
        }

        CreateGroundCollider(bgGo, "WorkSnow_Ground_Upper", UPPER_GROUND_LOCAL_Y, "upper");
        CreateGroundCollider(bgGo, "WorkSnow_Ground_Lower", LOWER_GROUND_LOCAL_Y, "lower");
    }

    void CreateGroundCollider(GameObject parent, string name, float localY, string tier)
    {
        if (GameObject.Find(name) != null) return;

        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = new Vector3(0f, localY, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;

        // BackgroundImage の scale=(15,8.5,1) の子なので
        // localSize (1, thin, depth) がワールドで (15, thin*8.5, depth) になる
        var box = go.AddComponent<BoxCollider>();
        box.size   = new Vector3(1f, GROUND_THICKNESS / BG_SCALE_Y, GROUND_DEPTH);
        box.center = Vector3.zero;

        // 視覚確認用 Quad は非表示（画面に白い板が出る原因になるため）

        Debug.Log($"[WORK_SNOW_GAME] ground_created name={name} tier={tier}" +
                  $" localY={localY:F3} parent={parent.name}");
    }

    // ── RoofDefinitionProvider に6軒の屋根定義を注入 ──────────────
    void InjectRoofDefinitions()
    {
        const string CALIB_PATH = "Assets/Art/RoofCalibrationData.json";
        if (!File.Exists(CALIB_PATH))
        {
            Debug.LogWarning("[WORK_SNOW_GAME] calib not found – roof definitions skipped");
            return;
        }

        var bgGo = GameObject.Find("BackgroundImage");
        if (bgGo == null) return;

        var t = bgGo.transform;
        Vector3 wTL = t.TransformPoint(new Vector3(-0.5f,  0.5f, 0f));
        Vector3 wTR = t.TransformPoint(new Vector3( 0.5f,  0.5f, 0f));
        Vector3 wBL = t.TransformPoint(new Vector3(-0.5f, -0.5f, 0f));
        Vector3 wBR = t.TransformPoint(new Vector3( 0.5f, -0.5f, 0f));

        System.Func<Vector2, Vector3> n2w = (n) =>
        {
            Vector3 top    = Vector3.Lerp(wTL, wTR, n.x);
            Vector3 bottom = Vector3.Lerp(wBL, wBR, n.x);
            return Vector3.Lerp(top, bottom, n.y);
        };

        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null) return;

        Vector3 roofNormal = -t.forward;
        int injected = 0;

        for (int i = 0; i < RoofIds.Length; i++)
        {
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == RoofIds[i]) { entry = r; break; }

            if (entry == null || !entry.confirmed) continue;

            Vector3 p0 = n2w(new Vector2(entry.topLeft.x,     entry.topLeft.y));
            Vector3 p1 = n2w(new Vector2(entry.topRight.x,    entry.topRight.y));
            Vector3 p2 = n2w(new Vector2(entry.bottomRight.x, entry.bottomRight.y));
            Vector3 p3 = n2w(new Vector2(entry.bottomLeft.x,  entry.bottomLeft.y));

            Vector3 origin   = (p0 + p1 + p2 + p3) * 0.25f;
            float   width    = (Vector3.Distance(p0, p1) + Vector3.Distance(p3, p2)) * 0.5f;
            float   depth    = (Vector3.Distance(p0, p3) + Vector3.Distance(p1, p2)) * 0.5f;
            Vector3 roofR    = (p1 - p0).sqrMagnitude > 0.0001f ? (p1 - p0).normalized : Vector3.right;
            Vector3 topCtr   = (p0 + p1) * 0.5f;
            Vector3 botCtr   = (p3 + p2) * 0.5f;
            Vector3 downhill = (botCtr - topCtr).sqrMagnitude > 0.0001f
                ? (botCtr - topCtr).normalized : Vector3.down;
            float slope = Vector3.Angle(downhill,
                Vector3.ProjectOnPlane(downhill, Vector3.up).normalized);

            var def = new RoofDefinition
            {
                width            = Mathf.Max(0.1f, width),
                depth            = Mathf.Max(0.1f, depth),
                slopeAngle       = slope,
                slopeDirection   = downhill,
                roofOrigin       = origin,
                roofNormal       = roofNormal,
                roofR            = roofR,
                roofF            = Vector3.Cross(roofNormal, roofR).normalized,
                roofDownhill     = downhill,
                isValid          = true,
                useExactRoofSize = true,
            };

            RoofDefinitionProvider.SetFromExternal(i, def);
            injected++;
            Debug.Log($"[WORK_SNOW_GAME] roof_injected id={RoofIds[i]} houseIndex={i}" +
                      $" origin=({origin.x:F2},{origin.y:F2},{origin.z:F2})" +
                      $" width={width:F3} depth={depth:F3}");
        }

        Debug.Log($"[WORK_SNOW_GAME] roof_definitions_injected={injected}/6");
    }

    // ── ゲームシステムを起動 ──────────────────────────────────────
    // WORK_SNOW では WorkSnowForcer の OnGUI システムが全て代替するため
    // SnowPackSpawner / GroundSnowSystem / RoofSnowSystem / SnowFallSystem は起動しない。
    // これらが起動すると groundCollider=null で GroundSnowLayer Cube が画面中央に生成され、
    // SnowPackSpawner(rebuildOnPlay=true) が roofCollider=null で赤いキューブを中央に生成する。
    void SetupGameSystems()
    {
        Debug.Log("[WORK_SNOW_GAME] SetupGameSystems SKIPPED" +
                  " – WorkSnowForcer handles all snow visuals in WORK_SNOW scene");
    }

    // ── シャベルツール生成（骨格確認用）──────────────────────────
    void SetupShovelTool()
    {
        if (Object.FindFirstObjectByType<ShovelTool>() != null)
        {
            Debug.Log("[WORK_SNOW_GAME] ShovelTool already exists – skipped");
            return;
        }

        var go   = new GameObject("ShovelTool");
        var tool = go.AddComponent<ShovelTool>();

        var tex = Resources.Load<Texture2D>("ShovelTool");
        if (tex != null)
        {
            tool.shovelTex = tex;
            Debug.Log($"[WORK_SNOW_GAME] ShovelTool created tex=ShovelTool({tex.width}x{tex.height})");
        }
        else
        {
            Debug.LogWarning("[WORK_SNOW_GAME] ShovelTool not found in Resources – ShovelTool will use fallback load in Start()");
        }
    }

    // ── 手袋ツール生成（GloveTool マウス追従表示）──────────────
    void SetupDebugGlove()
    {
        // 既存の GloveTool があればスキップ
        if (Object.FindFirstObjectByType<GloveTool>() != null)
        {
            Debug.Log("[WORK_SNOW_GAME] GloveTool already exists – skipped");
            return;
        }

        var go   = new GameObject("GloveTool");
        var tool = go.AddComponent<GloveTool>();

        var tex = Resources.Load<Texture2D>("GloveMitten");
        if (tex != null)
        {
            tool.gloveTex = tex;
            Debug.Log($"[WORK_SNOW_GAME] GloveTool created tex=GloveMitten({tex.width}x{tex.height})");
        }
        else
        {
            Debug.LogWarning("[WORK_SNOW_GAME] GloveMitten not found in Resources – GloveTool will use fallback load in Start()");
        }
    }
}
