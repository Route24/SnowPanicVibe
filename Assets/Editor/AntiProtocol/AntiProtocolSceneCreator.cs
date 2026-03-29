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
    // TASK-29: 台形メッシュ方式（キャリブ4点直接使用）
    // キャリブ4点ワールド座標: tl=(-7.67,1.54,9.20) tr=(7.77,1.62,9.17) br=(10.16,-1.79,10.41) bl=(-9.97,-1.84,10.43)
    // メッシュ中心: (0.07,-0.12,9.80) / 頂点はローカル座標

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

        // ── Roof（台形カスタムメッシュ・キャリブ4点直接使用）──────
        // ワールド座標: tl=(-7.67,1.54,9.20) tr=(7.77,1.62,9.17) br=(10.16,-1.79,10.41) bl=(-9.97,-1.84,10.43)
        // メッシュ中心: (0.07,-0.12,9.80) / 頂点はローカル座標
        var roofGo = new GameObject("Roof");
        roofGo.transform.SetParent(envGo.transform, false);
        // TASK-29: キャリブ4点の平均座標をメッシュ中心に使用
        roofGo.transform.localPosition = new Vector3(0.0713f, -0.1198f, 9.8038f);

        var roofMf = roofGo.AddComponent<MeshFilter>();
        var roofMr = roofGo.AddComponent<MeshRenderer>();
        roofGo.AddComponent<BoxCollider>(); // Snow Raycast 用に最小コライダー残す

        // TASK-29: 上2点 Y+0.6・X±6.5 に修正（前回Y+1.8が大きすぎたため）
        // 下2点: キャリブ直接値維持
        // 0=bl, 1=br, 2=tr, 3=tl
        var verts = new Vector3[]
        {
            new Vector3(-10.0452f, -1.7223f,  0.6272f), // 0 bl（固定）
            new Vector3( 10.0837f, -1.6743f,  0.6092f), // 1 br（固定）
            new Vector3(  6.5000f,  2.3348f, -0.6318f), // 2 tr（Y+0.6・X±6.5）
            new Vector3( -6.5000f,  2.2618f, -0.6048f), // 3 tl（Y+0.6・X±6.5）
        };
        // 両面表示（表：0,1,2 / 0,2,3 / 裏：0,2,1 / 0,3,2）
        var tris = new int[] { 0,1,2, 0,2,3, 0,2,1, 0,3,2 };
        var uvs = new Vector2[]
        {
            new Vector2(0f, 0f), // bl
            new Vector2(1f, 0f), // br
            new Vector2(1f, 1f), // tr
            new Vector2(0f, 1f), // tl
        };

        var roofMesh = new Mesh { name = "RoofTrapezoid" };
        roofMesh.vertices  = verts;
        roofMesh.triangles = tris;
        roofMesh.uv        = uvs;
        roofMesh.RecalculateNormals();
        roofMf.sharedMesh = roofMesh;

        SetLitColor(roofGo, new Color(0.52f, 0.38f, 0.22f));

        // TASK-29: 一時的に半透明表示（BG屋根との差分を視認するため）
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (sh != null)
            {
                var mat = new Material(sh) { name = "Roof_Transparent_Mat" };
                // URP Transparent 設定
                mat.SetFloat("_Surface", 1f);          // 0=Opaque 1=Transparent
                mat.SetFloat("_Blend", 0f);            // Alpha blend
                mat.SetFloat("_AlphaClip", 0f);
                mat.SetFloat("_SrcBlend", 5f);         // SrcAlpha
                mat.SetFloat("_DstBlend", 10f);        // OneMinusSrcAlpha
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = 3000;
                mat.SetOverrideTag("RenderType", "Transparent");
                var c = new Color(0.52f, 0.38f, 0.22f, 0.45f);
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
                if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     c);
                roofMr.sharedMaterial = mat;
            }
        }

        // ── 地面確認ライン（一時表示・ground_y=-5.616）────────────
        // showGroundCalibrationLine=true の間だけ表示。確認後 false に変えて再作成で消える。
        const bool showGroundCalibrationLine = true;
        if (showGroundCalibrationLine)
        {
            var lineGo = new GameObject("GroundCalibLine");
            lineGo.transform.SetParent(envGo.transform, false);
            lineGo.transform.localPosition = new Vector3(0f, -5.616f, 8.0f); // BGより手前に配置

            var lineMf = lineGo.AddComponent<MeshFilter>();
            var lineMr = lineGo.AddComponent<MeshRenderer>();

            // 幅40・高さ0.08 の細い横板
            var lineMesh = new Mesh { name = "GroundCalibLine_Mesh" };
            float hw = 20f, hh = 0.04f;
            lineMesh.vertices = new Vector3[]
            {
                new Vector3(-hw, -hh, 0f), new Vector3(hw, -hh, 0f),
                new Vector3( hw,  hh, 0f), new Vector3(-hw,  hh, 0f),
            };
            lineMesh.triangles = new int[] { 0,1,2, 0,2,3, 0,2,1, 0,3,2 };
            lineMesh.RecalculateNormals();
            lineMf.sharedMesh = lineMesh;

            var lineSh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (lineSh != null)
            {
                var lineMat = new Material(lineSh) { name = "GroundCalibLine_Mat" };
                if (lineMat.HasProperty("_BaseColor")) lineMat.SetColor("_BaseColor", Color.red);
                if (lineMat.HasProperty("_Color"))     lineMat.SetColor("_Color",     Color.red);
                lineMr.sharedMaterial = lineMat;
            }
        }
        var roof = roofGo; // 後続コードとの互換性のためエイリアス

        // ── System ───────────────────────────────────────────
        var sysGo = new GameObject("System");
        var controller = sysGo.AddComponent<InputTapController>();

        // ── RoofCalibrationController（TASK-29: 新BG再キャリブ用・一時追加）──
        // 新BG基準での5点再取得後、S キーで保存 → その値で屋根パラメータを更新する
        var calib = sysGo.AddComponent<RoofCalibrationController>();
        {
            SerializedObject soCalib = new SerializedObject(calib);
            var propCalib = soCalib.FindProperty("calibrationModeActive");
            if (propCalib != null) { propCalib.boolValue = true; soCalib.ApplyModifiedPropertiesWithoutUndo(); }
        }

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
