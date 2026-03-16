#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Billboard Background + 2D RoofGuide Canvas シーンを生成する。
/// SnowPanic/Billboard: Create Avalanche_Billboard_Test から実行。
///
/// 構成:
///   Main Camera (3D)
///   BackgroundImage (3D Quad, Unlit/Texture)
///   RoofGuideCanvas (Screen Space - Overlay Canvas)
///     └ RoofGuide_TL/TM/TR/BL/BM/BR (Image, 半透明)
///   BillboardSnowKiller
///   DirectionalLight
/// </summary>
public static class BillboardBackgroundSceneCreator
{
    const string ScenePath   = "Assets/Scenes/Avalanche_Billboard_Test.unity";
    const string BgImagePath = "Assets/Art/clean_background.png";

    // カメラ設定（変更禁止）
    static readonly Vector3 CamPos = new Vector3(0f, 5.6f, -7.5f);
    static readonly Vector3 CamRot = new Vector3(36f, 0f, 0f);

    // 背景 Quad
    static readonly Vector3 BgPos = new Vector3(0f, -0.3f, 0.6f);
    static readonly Vector3 BgRot = new Vector3(36f, 0f, 0f);
    const float BgWidth  = 15.0f;
    const float BgHeight =  8.5f;

    // 2D RoofGuide: 4-point trapezoid (normalized 0..1, 左上原点)
    // 画像解析 (clean_background.png 1536x1024) から自動算出した値
    struct GuideRect
    {
        public string name;
        public float xMin, xMax, yMin, yMax; // bounding box (anchorMin/Max 用)
        public Color color;
        // 台形4点 (normalized, 左上原点)
        public Vector2 topLeft, topRight, bottomLeft, bottomRight;
    }

    // 台形4点: clean_background.png (1536x1024) 画像解析から算出
    // normalized screen-space (0..1, 左上原点)
    static readonly GuideRect[] GuideRects = new GuideRect[]
    {
        new GuideRect {
            name="RoofGuide_TL", color=new Color(1.0f,0.3f,0.6f,0.45f),
            topLeft    =new Vector2(0.083f,0.210f), topRight    =new Vector2(0.326f,0.209f),
            bottomLeft =new Vector2(0.083f,0.466f), bottomRight =new Vector2(0.326f,0.459f),
            xMin=0.083f, xMax=0.326f, yMin=0.209f, yMax=0.466f },
        new GuideRect {
            name="RoofGuide_TM", color=new Color(0.3f,0.8f,1.0f,0.45f),
            topLeft    =new Vector2(0.382f,0.210f), topRight    =new Vector2(0.617f,0.210f),
            bottomLeft =new Vector2(0.382f,0.465f), bottomRight =new Vector2(0.617f,0.465f),
            xMin=0.382f, xMax=0.617f, yMin=0.210f, yMax=0.465f },
        new GuideRect {
            name="RoofGuide_TR", color=new Color(0.3f,1.0f,0.5f,0.45f),
            topLeft    =new Vector2(0.674f,0.210f), topRight    =new Vector2(0.935f,0.184f),
            bottomLeft =new Vector2(0.674f,0.463f), bottomRight =new Vector2(0.935f,0.466f),
            xMin=0.674f, xMax=0.935f, yMin=0.184f, yMax=0.466f },
        new GuideRect {
            name="RoofGuide_BL", color=new Color(1.0f,0.8f,0.2f,0.45f),
            topLeft    =new Vector2(0.065f,0.446f), topRight    =new Vector2(0.354f,0.445f),
            bottomLeft =new Vector2(0.065f,0.867f), bottomRight =new Vector2(0.354f,0.867f),
            xMin=0.065f, xMax=0.354f, yMin=0.445f, yMax=0.867f },
        new GuideRect {
            name="RoofGuide_BM", color=new Color(0.8f,0.3f,1.0f,0.45f),
            topLeft    =new Vector2(0.382f,0.449f), topRight    =new Vector2(0.617f,0.442f),
            bottomLeft =new Vector2(0.382f,0.849f), bottomRight =new Vector2(0.617f,0.857f),
            xMin=0.382f, xMax=0.617f, yMin=0.442f, yMax=0.857f },
        new GuideRect {
            name="RoofGuide_BR", color=new Color(1.0f,0.5f,0.2f,0.45f),
            topLeft    =new Vector2(0.656f,0.450f), topRight    =new Vector2(0.953f,0.444f),
            bottomLeft =new Vector2(0.656f,0.847f), bottomRight =new Vector2(0.953f,0.844f),
            xMin=0.656f, xMax=0.953f, yMin=0.444f, yMax=0.847f },
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
            Debug.Log($"[BillboardBG] scene_created=true path={ScenePath} mode=2D_roof_guide");
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

        // ── 背景 Quad (BackgroundImage) ──────────────────────
        var bgGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgGo.name = "BackgroundImage";
        Object.DestroyImmediate(bgGo.GetComponent<MeshCollider>());
        bgGo.transform.position = BgPos;
        bgGo.transform.eulerAngles = BgRot;
        bgGo.transform.localScale = new Vector3(BgWidth, BgHeight, 1f);

        var bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>(BgImagePath);
        const string bgMatPath = "Assets/Materials/BillboardBG.mat";
        var bgMat = new Material(Shader.Find("Unlit/Texture"));
        if (bgTex != null)
        {
            bgMat.mainTexture = bgTex;
            Debug.Log($"[BillboardBG] background_image_loaded=true tex={bgTex.name}");
        }
        else
        {
            bgMat.color = new Color(0.8f, 0f, 0.8f, 1f);
            Debug.LogWarning($"[BillboardBG] background_image_loaded=false path={BgImagePath}");
        }
        bgGo.GetComponent<Renderer>().sharedMaterial = bgMat;
        var existingBg = AssetDatabase.LoadAssetAtPath<Material>(bgMatPath);
        if (existingBg != null) AssetDatabase.DeleteAsset(bgMatPath);
        AssetDatabase.CreateAsset(bgMat, bgMatPath);

        // ── 2D RoofGuide Canvas ───────────────────────────────
        // Screen Space - Overlay: Play 前の Editor でも Game View に表示される
        var canvasGo = new GameObject("RoofGuideCanvas");
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        canvasGo.AddComponent<CanvasScaler>(); // デフォルト設定で OK
        canvasGo.AddComponent<GraphicRaycaster>();
        Debug.Log("[BillboardBG] guide_canvas_created=true renderMode=ScreenSpaceOverlay");

        // 白テクスチャ（Image のデフォルト sprite 代わり）
        const string whiteSprPath = "Assets/Materials/RoofGuideWhite.png";

        foreach (var g in GuideRects)
        {
            var go = new GameObject(g.name);
            go.transform.SetParent(canvasGo.transform, false);

            var rt = go.AddComponent<RectTransform>();
            // bounding box を anchorMin/Max で指定（Unity y は下から）
            rt.anchorMin = new Vector2(g.xMin, 1f - g.yMax);
            rt.anchorMax = new Vector2(g.xMax, 1f - g.yMin);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = g.color;

            // 台形4点データをログ出力（後で RoofCollider 生成に使用）
            Debug.Log($"[ROOF_POLY] name={g.name} " +
                $"TL=({g.topLeft.x:F3},{g.topLeft.y:F3}) " +
                $"TR=({g.topRight.x:F3},{g.topRight.y:F3}) " +
                $"BL=({g.bottomLeft.x:F3},{g.bottomLeft.y:F3}) " +
                $"BR=({g.bottomRight.x:F3},{g.bottomRight.y:F3})");
        }

        // ── Roof Calibration Controller ───────────────────────
        // キー1〜6で屋根選択、左クリックで4点入力、S保存、L読み込み
        var calibGo = new GameObject("RoofCalibrationController");
        calibGo.AddComponent<RoofCalibrationController>();
        Debug.Log("[BillboardBG] calibration_mode_added=true");

        // ── SnowKiller ────────────────────────────────────────
        var killerGo = new GameObject("BillboardSnowKiller");
        killerGo.AddComponent<BillboardSnowKiller>();
        Debug.Log("[BillboardBG] snow_killer_added=true");

        // ── ライト ───────────────────────────────────────────
        var lightGo = new GameObject("DirectionalLight");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        lightGo.transform.eulerAngles = new Vector3(50f, -30f, 0f);
    }
}
#endif
