#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;

/// <summary>
/// SnowCore_AntiProtocol シーンを新規作成する。
/// 既存Snow系スクリプト・Prefab・Singletonへの参照は一切持たない。
/// メニュー: SnowPanic → Create SnowCore_AntiProtocol
/// </summary>
public static class AntiProtocolSceneCreator
{
    const string ScenePath = "Assets/Scenes/SnowCore_AntiProtocol.unity";
    const string SnowLayerName = "Snow";
    const float roofThickness = 0.5f;
    const float roofWidth     = 14.9f;
    const float roofDepth     = 4f;
    const float roofPosX      = 0f;
    const float roofPosY      = 0.5f;
    const float roofPosZ      = 9f;

    [MenuItem("SnowPanic/Create SnowCore_AntiProtocol", false, 20)]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        EnsureSnowLayerExists();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── Main Camera ─────────────────────────────────────
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.12f, 0.20f, 1f);
        cam.fieldOfView = 50f;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        cam.cullingMask = ~0;
        camGo.transform.position = new Vector3(0f, 4f, -8f);
        camGo.transform.eulerAngles = new Vector3(20f, 0f, 0f);
        camGo.AddComponent<AudioListener>();
        var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
        urp.renderType = CameraRenderType.Base;
        urp.renderShadows = true;

        // ── Directional Light ────────────────────────────────
        var lightGo = new GameObject("Directional Light");
        var lt = lightGo.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1.2f;
        lt.color = Color.white;
        lightGo.transform.eulerAngles = new Vector3(50f, -30f, 0f);
        lightGo.AddComponent<UniversalAdditionalLightData>();

        // ── Environment ──────────────────────────────────────
        var envGo = new GameObject("Environment");

        // ── BackgroundImage（BG Quad）────────────────────────
        // BGはカメラ(rot.x=20°)に正対させて傾け、画面基準の下敷きとして配置する。
        // カメラ forward 方向に距離 20 の位置に置き、FOV50・実アスペクト1920/1080 でフィットさせる。
        // これにより余白ゼロの完全フィットを最小コードで実現する。
        var bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/clean_background.png");
        if (bgTex != null)
        {
            float texW      = bgTex.width;
            float texH      = bgTex.height;
            float texAspect = texW / texH;

            // カメラ正面方向に z_dist=20 進んだ位置
            // cam pos=(0,4,-8), rot.x=20° => forward=(0,-sin20,cos20)
            // center = (0, 4 + (-sin20)*20, -8 + cos20*20) = (0, -2.84, 10.79)
            const float zDist  = 20f;
            const float camRot = 20f; // deg
            float sinR  = Mathf.Sin(camRot * Mathf.Deg2Rad);
            float cosR  = Mathf.Cos(camRot * Mathf.Deg2Rad);
            float bgY   = 4f  + (-sinR) * zDist;  // ≈ -2.84
            float bgZ   = -8f + cosR    * zDist;   // ≈ 10.79

            // フィットサイズ: h = 2*zDist*tan(FOV/2), w = h * texAspect
            const float fovHalf = 0.4663f; // tan(25°)
            float hFit = 2f * zDist * fovHalf;
            float wFit = hFit * texAspect;

            var bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            bgQuad.name = "BackgroundImage";
            bgQuad.transform.SetParent(envGo.transform, false);
            bgQuad.transform.localPosition    = new Vector3(0f, bgY, bgZ);
            bgQuad.transform.localEulerAngles = new Vector3(camRot, 0f, 0f); // カメラと同角度に傾ける
            bgQuad.transform.localScale       = new Vector3(wFit, hFit, 1f);

            var bgSh = Shader.Find("Unlit/Texture") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (bgSh != null)
            {
                var bgMat = new Material(bgSh) { name = "BackgroundImage_Mat" };
                bgMat.mainTexture = bgTex;
                bgQuad.GetComponent<MeshRenderer>().sharedMaterial = bgMat;
            }
            var bgCol = bgQuad.GetComponent<Collider>();
            if (bgCol != null) bgCol.enabled = false;

            Debug.Log($"[AntiProtocol] bg_texture_width={texW} bg_texture_height={texH}"
                    + $" bg_aspect_ratio={texAspect:F4} bg_scale_w={wFit:F3} bg_scale_h={hFit:F3}"
                    + $" bg_tilt_deg={camRot} bg_y={bgY:F3} bg_z={bgZ:F3}");
        }

        // ── Roof (Environment の子) ──────────────────────────
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Roof";
        roof.transform.SetParent(envGo.transform, false);
        roof.transform.localPosition = new Vector3(roofPosX, roofPosY, roofPosZ);
        roof.transform.localScale = new Vector3(roofWidth, roofThickness, roofDepth);
        SetLitColor(roof, new Color(0.52f, 0.38f, 0.22f));
        // Roof は Default レイヤーのまま。BoxCollider は残す（RoofはSnowLayerMaskに含まれない）

        // ── System ───────────────────────────────────────────
        var sysGo = new GameObject("System");
        var controller = sysGo.AddComponent<InputTapController>();

        // snowLayerMask に Snow レイヤーを設定
        int snowLayer = LayerMask.NameToLayer(SnowLayerName);
        if (snowLayer >= 0)
        {
            SerializedObject so = new SerializedObject(controller);
            var prop = so.FindProperty("snowLayerMask");
            if (prop != null)
            {
                prop.intValue = 1 << snowLayer;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        else
        {
            Debug.LogWarning("[AntiProtocol] Snow レイヤーが見つかりません。snowLayerMask を手動で設定してください。");
        }

        // ── SnowLayer ────────────────────────────────────────
        var snowLayerGo = new GameObject("SnowLayer");

        // ── CyanSnowBox (SnowLayer の子) ─────────────────────
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "CyanSnowBox";
        box.transform.SetParent(snowLayerGo.transform, false);
        box.transform.localPosition = new Vector3(0f, 0.75f, 0f);
        box.transform.localScale = Vector3.one;
        SetUnlitColor(box, Color.cyan);
        box.AddComponent<SnowBlockNode>();

        if (snowLayer >= 0)
            box.layer = snowLayer;
        else
            Debug.LogWarning("[AntiProtocol] CyanSnowBox のレイヤー設定をスキップ（Snow レイヤー未作成）");

        // ── AntiProtocolVisibilityReporter を System にアタッチ ──
        var reporter = sysGo.AddComponent<AntiProtocolVisibilityReporter>();
        SerializedObject soReporter = new SerializedObject(reporter);
        var propRoof = soReporter.FindProperty("roofObject");
        var propCyan = soReporter.FindProperty("cyanBoxObject");
        if (propRoof != null) propRoof.objectReferenceValue = roof;
        if (propCyan != null) propCyan.objectReferenceValue = box;
        soReporter.ApplyModifiedPropertiesWithoutUndo();

        // ── シーン保存 ────────────────────────────────────────
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[AntiProtocol] scene_created=YES path={ScenePath}");
        EditorUtility.DisplayDialog("SnowCore_AntiProtocol 作成完了",
            ScenePath + "\n\n" +
            "【確認手順】\n" +
            "1. Play → 屋根(茶)とシアンボックスが見えるか確認\n" +
            "2. シアンボックスをクリック → 消えるか確認\n" +
            "3. 屋根・空白をクリック → 何も起きないか確認\n" +
            "4. 数秒放置して何も自動生成されないか確認\n" +
            "5. Console に error/warning が出ないか確認",
            "OK");
    }

    /// <summary>Snow レイヤーが存在しなければ User Layer に追加する。</summary>
    static void EnsureSnowLayerExists()
    {
        if (LayerMask.NameToLayer(SnowLayerName) >= 0) return;

        var tagManager = new SerializedObject(
            AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/TagManager.asset"));
        var layers = tagManager.FindProperty("layers");
        if (layers == null) return;

        for (int i = 8; i < layers.arraySize; i++)
        {
            var element = layers.GetArrayElementAtIndex(i);
            if (string.IsNullOrEmpty(element.stringValue))
            {
                element.stringValue = SnowLayerName;
                tagManager.ApplyModifiedProperties();
                Debug.Log($"[AntiProtocol] Snow レイヤーを User Layer {i} に追加しました。");
                return;
            }
        }
        Debug.LogWarning("[AntiProtocol] 空き User Layer が見つかりません。手動で Snow レイヤーを追加してください。");
    }

    static void SetLitColor(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (sh == null) return;
        var mat = new Material(sh) { name = go.name + "_Mat" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mr.sharedMaterial = mat;
    }

    static void SetUnlitColor(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var sh = Shader.Find("Universal Render Pipeline/Unlit")
              ?? Shader.Find("Unlit/Color")
              ?? Shader.Find("Standard");
        if (sh == null) return;
        var mat = new Material(sh) { name = go.name + "_Unlit" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        mr.sharedMaterial = mat;
    }
}
#endif
