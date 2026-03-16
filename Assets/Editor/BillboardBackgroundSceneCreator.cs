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

    // 2D RoofGuide: normalized (0..1, 左上原点) → anchorMin/Max で配置
    // 背景画像 BillboardBackground.png の屋根位置に対応
    struct GuideRect
    {
        public string name;
        public float xMin, xMax, yMin, yMax; // 0..1, 左上原点
        public Color color;
    }

    static readonly GuideRect[] GuideRects = new GuideRect[]
    {
        // 上段3軒（clean_background.png テンプレ初期値）
        new GuideRect { name="RoofGuide_TL", xMin=0.145f, xMax=0.305f, yMin=0.215f, yMax=0.365f, color=new Color(1.0f, 0.3f, 0.6f, 0.45f) },
        new GuideRect { name="RoofGuide_TM", xMin=0.400f, xMax=0.600f, yMin=0.205f, yMax=0.365f, color=new Color(0.3f, 0.8f, 1.0f, 0.45f) },
        new GuideRect { name="RoofGuide_TR", xMin=0.690f, xMax=0.855f, yMin=0.205f, yMax=0.365f, color=new Color(0.3f, 1.0f, 0.5f, 0.45f) },
        // 下段3軒（clean_background.png テンプレ初期値）
        new GuideRect { name="RoofGuide_BL", xMin=0.120f, xMax=0.330f, yMin=0.565f, yMax=0.735f, color=new Color(1.0f, 0.8f, 0.2f, 0.45f) },
        new GuideRect { name="RoofGuide_BM", xMin=0.405f, xMax=0.610f, yMin=0.565f, yMax=0.735f, color=new Color(0.8f, 0.3f, 1.0f, 0.45f) },
        new GuideRect { name="RoofGuide_BR", xMin=0.690f, xMax=0.900f, yMin=0.565f, yMax=0.735f, color=new Color(1.0f, 0.5f, 0.2f, 0.45f) },
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
            // anchorMin/Max で normalized 座標を直接指定
            // Unity の anchorMin.y は下から → yMin_unity = 1 - yMax_img
            rt.anchorMin = new Vector2(g.xMin, 1f - g.yMax);
            rt.anchorMax = new Vector2(g.xMax, 1f - g.yMin);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var img = go.AddComponent<Image>();
            img.color = g.color;
            // sprite は null のまま（白矩形として描画される）

            Debug.Log($"[BillboardBG] guide_rect_created=true name={g.name} xMin={g.xMin:F3} xMax={g.xMax:F3} yMin={g.yMin:F3} yMax={g.yMax:F3}");
        }

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
