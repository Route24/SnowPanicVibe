#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Billboard Background + 6 RoofPlanes シーンを生成する。
/// SnowPanic/Billboard: Create Avalanche_Billboard_Test から実行。
/// </summary>
public static class BillboardBackgroundSceneCreator
{
    const string ScenePath = "Assets/Scenes/Avalanche_Billboard_Test.unity";
    const string BgImagePath = "Assets/Art/BillboardBackground.png";

    // カメラ設定
    static readonly Vector3 CamPos = new Vector3(0f, 5.6f, -7.5f);
    static readonly Vector3 CamRot = new Vector3(36f, 0f, 0f);

    // 背景 Quad: カメラ正面 10m 先に垂直配置（回転なし・カメラに正対）
    // cam=(0,5.6,-7.5) rot=(36,0,0) forward=(0,-0.588,0.809)
    // center = cam + 10*forward = (0, 5.6-5.88, -7.5+8.09) = (0,-0.28,0.59)
    // 画面高さ = 2*10*tan(22.5°) ≈ 8.28 → width=14.7 (16:9)
    // Quad はデフォルトで XY 平面・法線+Z → カメラと同じ rot(36,0,0) で正対
    static readonly Vector3 BgPos = new Vector3(0f, -0.3f, 0.6f);
    static readonly Vector3 BgRot = new Vector3(36f, 0f, 0f);
    const float BgWidth  = 15.0f;
    const float BgHeight =  8.5f;

    // RoofPlane 共通設定
    // mono-pitch: back(奥) が高く front(手前) が低い → X 軸回転 -15 度
    const float RoofSlopeDeg = -15f;
    const float RoofThick    = 0.05f;

    // 6枚の屋根: 背景画像の各家屋根に対応
    // 画像レイアウト（1024x576 px 相当）:
    //   上段3軒: 画面 X = -35%/-0%/+35%, 画面 Y = 上から 30-55%
    //   下段3軒: 画面 X = -28%/-0%/+28%, 画面 Y = 上から 50-80%
    // カメラ rot=36° で奥=高Y・大Z、手前=低Y・小Z
    // 上段: world Y≈2.0, Z≈1.5, 幅1.7, 奥1.0
    // 下段: world Y≈0.3, Z≈3.5, 幅2.0, 奥1.2
    static readonly (string name, Vector3 pos, Vector3 scale)[] RoofDefs = new[]
    {
        // 上段（奥の3軒）: 小さめ
        ("RoofPlane_TL", new Vector3(-2.8f, 2.0f, 1.5f), new Vector3(1.7f, RoofThick, 1.0f)),
        ("RoofPlane_TM", new Vector3( 0.0f, 2.0f, 1.5f), new Vector3(1.7f, RoofThick, 1.0f)),
        ("RoofPlane_TR", new Vector3( 2.8f, 2.0f, 1.5f), new Vector3(1.7f, RoofThick, 1.0f)),
        // 下段（手前の3軒）: 大きめ
        ("RoofPlane_BL", new Vector3(-2.2f, 0.3f, 3.5f), new Vector3(2.0f, RoofThick, 1.2f)),
        ("RoofPlane_BM", new Vector3( 0.0f, 0.3f, 3.5f), new Vector3(2.0f, RoofThick, 1.2f)),
        ("RoofPlane_BR", new Vector3( 2.2f, 0.3f, 3.5f), new Vector3(2.0f, RoofThick, 1.2f)),
    };

    [MenuItem("SnowPanic/Billboard: Create Avalanche_Billboard_Test", false, 50)]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Build();
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[BillboardBG] scene_created=true path={ScenePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[BillboardBG] Create failed: {e.Message}\n{e.StackTrace}");
        }
    }

    static void Build()
    {
        // ── カメラ ──────────────────────────────────────────
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.15f, 0.25f);
        cam.fieldOfView = 45f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        camGo.transform.position = CamPos;
        camGo.transform.eulerAngles = CamRot;
        camGo.AddComponent<AudioListener>();
        Debug.Log($"[BillboardBG] camera_locked=true pos={CamPos} rot={CamRot}");

        // ── 背景 Quad ────────────────────────────────────────
        var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgGo.name = "BackgroundImage";
        Object.DestroyImmediate(bgGo.GetComponent<MeshCollider>());
        bgGo.transform.position = BgPos;
        bgGo.transform.eulerAngles = BgRot; // カメラと同じ傾きで正面を向く
        bgGo.transform.localScale = new Vector3(BgWidth, BgHeight, 1f);

        // テクスチャを読み込んでマテリアルに設定
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(BgImagePath);
        const string bgMatPath = "Assets/Materials/BillboardBG.mat";
        var bgMat = new Material(Shader.Find("Unlit/Texture"));
        if (tex != null)
        {
            bgMat.mainTexture = tex;
            Debug.Log($"[BillboardBG] background_image_loaded=true tex={tex.name}");
        }
        else
        {
            // テクスチャ未 import 時はマゼンタで目立たせる（白にしない）
            bgMat.color = new Color(0.8f, 0f, 0.8f, 1f);
            Debug.LogWarning($"[BillboardBG] background_image_loaded=false path={BgImagePath}");
        }
        bgGo.GetComponent<Renderer>().sharedMaterial = bgMat;
        // 既存マテリアルを上書き
        var existing = AssetDatabase.LoadAssetAtPath<Material>(bgMatPath);
        if (existing != null) AssetDatabase.DeleteAsset(bgMatPath);
        AssetDatabase.CreateAsset(bgMat, bgMatPath);

        // ── SnowKiller: Play 時に Snow 系 GO を全破棄 ────────
        var killerGo = new GameObject("BillboardSnowKiller");
        killerGo.AddComponent<BillboardSnowKiller>();
        Debug.Log("[BillboardBG] snow_killer_added=true");

        // ── ライト ───────────────────────────────────────────
        var lightGo = new GameObject("DirectionalLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightGo.transform.eulerAngles = new Vector3(50f, -30f, 0f);

        // ── RoofPlane x6 ─────────────────────────────────────
        var roofRoot = new GameObject("RoofPlanes");
        int created = 0;
        foreach (var (rName, rPos, rScale) in RoofDefs)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = rName;
            go.transform.SetParent(roofRoot.transform, false);
            go.transform.position = rPos;
            go.transform.localScale = rScale;
            go.transform.eulerAngles = new Vector3(RoofSlopeDeg, 0f, 0f);

            // BoxCollider はそのまま使用（Cube に付属）
            var col = go.GetComponent<BoxCollider>();
            if (col == null) go.AddComponent<BoxCollider>();

            // 半透明マテリアル（アライン確認用）
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.5f, 0.8f, 1f, 0.45f);
            mat.SetFloat("_Mode", 3f); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            // RoofSnowSystem 用タグ
            go.tag = "Untagged";
            go.layer = 0;

            created++;
            Debug.Log($"[BillboardBG] roof_plane_created={rName} pos={rPos} scale={rScale}");
        }
        Debug.Log($"[BillboardBG] roof_planes_created={created}/6");
    }
}
#endif
