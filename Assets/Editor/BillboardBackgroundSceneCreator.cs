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

    // 6枚の屋根: 背景画像(1024x576)の各家屋根ピクセル座標から逆算
    // BgPos=(0,-0.3,0.6) BgRot=(36,0,0) BgScale=(15,8.5,1)
    // local_x=(px_x/1024-0.5)*15  local_y=(0.5-px_y/576)*8.5
    // world_x=local_x  world_y=-0.3+local_y*0.809  world_z=0.6-local_y*0.588
    // scale_x=px_w/1024*15  scale_y=px_h/576*8.5*0.809 (奥行き補正)
    // 背景面より 0.05m 手前にオフセット (法線=(0,sin36,−cos36)=(0,0.588,−0.809))
    static readonly (string name, Vector3 pos, Vector3 scale)[] RoofDefs = new[]
    {
        // TL: px(215,200) w=190 h=45
        ("RoofPlane_TL", new Vector3(-3.05f+0f, 0.21f+0.03f, 0.23f-0.03f), new Vector3(2.6f, 0.50f, 1f)),
        // TM: px(510,182) w=215 h=50
        ("RoofPlane_TM", new Vector3(-0.04f+0f, 0.43f+0.03f, 0.07f-0.03f), new Vector3(2.9f, 0.55f, 1f)),
        // TR: px(795,192) w=195 h=48
        ("RoofPlane_TR", new Vector3( 2.90f+0f, 0.32f+0.03f, 0.15f-0.03f), new Vector3(2.6f, 0.52f, 1f)),
        // BL: px(205,370) w=255 h=60
        ("RoofPlane_BL", new Vector3(-3.19f+0f,-1.08f+0.03f, 1.16f-0.03f), new Vector3(3.4f, 0.65f, 1f)),
        // BM: px(510,400) w=185 h=50
        ("RoofPlane_BM", new Vector3(-0.04f+0f,-1.43f+0.03f, 1.42f-0.03f), new Vector3(2.5f, 0.55f, 1f)),
        // BR: px(800,368) w=255 h=60
        ("RoofPlane_BR", new Vector3( 2.93f+0f,-1.06f+0.03f, 1.15f-0.03f), new Vector3(3.4f, 0.65f, 1f)),
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

        // ── RoofPlane x6 (Quad) ──────────────────────────────
        // Quad はデフォルト 1x1 unit (XY平面)。scale.x=幅, scale.y=奥行き相当。
        // 屋根オーバーレイ: x=1.0, y=0.45, z=1.0
        // 傾斜: X軸 -15° で back→front slope
        var roofRoot = new GameObject("RoofPlanes");
        int created = 0;
        foreach (var (rName, rPos, rScale) in RoofDefs)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = rName;
            // MeshCollider(Quad付属) を除去して BoxCollider を追加
            var meshCol = go.GetComponent<MeshCollider>();
            if (meshCol != null) Object.DestroyImmediate(meshCol);
            go.AddComponent<BoxCollider>();

            // 位置・スケール・回転を設定してから親に追加
            go.transform.position = rPos;
            go.transform.localScale = rScale;
            go.transform.eulerAngles = new Vector3(RoofSlopeDeg, 0f, 0f);
            go.transform.SetParent(roofRoot.transform, true);

            // 半透明ピンクマテリアル（アライン確認用）
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(1f, 0.4f, 0.7f, 0.6f);
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            go.GetComponent<Renderer>().sharedMaterial = mat;

            go.tag = "Untagged";
            go.layer = 0;

            created++;
            Debug.Log($"[BillboardBG] roof_quad_created={rName} pos={rPos} scale={rScale} type=Quad");
        }
        Debug.Log($"[BillboardBG] roof_planes_created={created}/6 mesh_type=Quad");
    }
}
#endif
