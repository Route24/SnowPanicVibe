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

        // ── 地面確認ライン（確認完了・非表示）────────────────────
        // ground_y=-4.8 で確定済み。再確認時は showGroundCalibrationLine=true に戻す。
        const bool showGroundCalibrationLine = false;
        if (showGroundCalibrationLine)
        {
            var lineGo = new GameObject("GroundCalibLine");
            lineGo.transform.SetParent(envGo.transform, false);
            lineGo.transform.localPosition = new Vector3(0f, -4.8f, 8.0f); // ground_y 微下げ

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

        // ── TypeB 積雪（TASK-30）─────────────────────────────────
        // 屋根4頂点（ローカル）を基準に、積雪面を面集合として生成する。
        // SnowTypeBRoot（roofGo の子）配下に各パネルを生成。
        // 将来: 分割数変更・形状差し替え・削れ表現はパネル単位で行う。
        BuildTypeBSnow(roofGo);

        // ── System ───────────────────────────────────────────
        var sysGo = new GameObject("System");
        var controller = sysGo.AddComponent<InputTapController>();

        // ── RoofCalibrationController（キャリブ完了・非アクティブ）──
        // 再キャリブ時は calibrationModeActive=true に戻す
        var calib = sysGo.AddComponent<RoofCalibrationController>();
        {
            SerializedObject soCalib = new SerializedObject(calib);
            var propCalib = soCalib.FindProperty("calibrationModeActive");
            if (propCalib != null) { propCalib.boolValue = false; soCalib.ApplyModifiedPropertiesWithoutUndo(); }
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

    // ── TypeB 積雪ビルダー（TASK-30）────────────────────────────────
    // 屋根面に沿う積雪を複数パネル（面の集合）で生成する。
    // 【厚み方向の変更】
    //   roofNormal 方向だけでは手前から見たときに「板」に見える。
    //   → 上面 = 屋根面オフセット + Y方向に積む（WorldUp）
    //   → 前面・側面が「高さ」として見えるため、厚みが正面から明確に分かる。
    // SnowTypeBRoot 配下に：
    //   - メインパネル (4×2 分割、各列で高さ段差・端不定形)
    //   - 雪庇パーツ  (手前下端 4分割、前方 + 斜め下方向に張り出し)
    const int   TypeB_PanelCountX       = 4;     // 横分割数
    const int   TypeB_PanelCountY       = 2;     // 縦分割数
    const float TypeB_SnowOffset        = 0.12f; // 屋根面からの法線オフセット（突き抜け防止）
    const float TypeB_HeightBase        = 1.10f; // 雪の高さ（Y方向、手前から見えるサイズ）
    const float TypeB_CorniceOverhang   = 0.70f; // 雪庇の前方張り出し量
    const float TypeB_CorniceHeight     = 0.60f; // 雪庇の高さ（Y方向）

    // 雪の色: 純白（Unlit で常に明確に見える）
    static readonly Color TypeB_SnowColor = new Color(1.00f, 1.00f, 1.00f, 1f);

    // 列ごとの高さバリエーション（塊の凸凹感）
    static readonly float[] TypeB_HeightVar = { 0.20f, -0.15f, 0.25f, -0.10f };

    // 端・手前のジッター（u方向の不定形化）
    static readonly float[] TypeB_EdgeJitter = { -0.035f, 0.025f, -0.020f, 0.030f };

    static void BuildTypeBSnow(GameObject roofGo)
    {
        // 屋根ローカル座標（TASK-29 固定値・変更禁止）
        // 0=bl, 1=br, 2=tr, 3=tl
        var bl = new Vector3(-10.0452f, -1.7223f,  0.6272f);
        var br = new Vector3( 10.0837f, -1.6743f,  0.6092f);
        var tr = new Vector3(  6.5000f,  2.3348f, -0.6318f);
        var tl = new Vector3( -6.5000f,  2.2618f, -0.6048f);

        // 屋根面の法線（下面への突き抜け防止オフセット用）
        var edge1 = br - bl;
        var edge2 = tl - bl;
        var roofNormal = Vector3.Cross(edge1, edge2).normalized;
        if (roofNormal.z > 0f) roofNormal = -roofNormal;

        // 屋根の前方向（下辺中心 → 上辺中心の逆方向 = 手前へ）
        var frontDir = ((bl + br) * 0.5f - (tl + tr) * 0.5f).normalized;

        // 厚み方向 = 純粋に WorldUp（Y+）
        // これにより手前から見たときに「前面の高さ」として厚みが見える
        var upDir = Vector3.up;

        var root = new GameObject("SnowTypeBRoot");
        root.transform.SetParent(roofGo.transform, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale    = Vector3.one;

        int totalParts = 0;

        // ── メインパネル群（4×2、各列で高さ段差・端不定形）────────────
        for (int py = 0; py < TypeB_PanelCountY; py++)
        {
            float v0 = (float)py       / TypeB_PanelCountY;
            float v1 = (float)(py + 1) / TypeB_PanelCountY;

            for (int px = 0; px < TypeB_PanelCountX; px++)
            {
                float u0 = (float)px       / TypeB_PanelCountX;
                float u1 = (float)(px + 1) / TypeB_PanelCountX;

                // 端不定形：左右端にジッターを加える
                float ju0 = (px == 0)                     ? TypeB_EdgeJitter[0] : 0f;
                float ju1 = (px == TypeB_PanelCountX - 1) ? TypeB_EdgeJitter[3] : 0f;
                float jv0L = TypeB_EdgeJitter[px % 4] * 0.5f;
                float jv0R = TypeB_EdgeJitter[(px + 1) % 4] * 0.5f;

                var p00 = BilinearRoof(bl, br, tl, tr, u0 + ju0 + jv0L, v0);
                var p10 = BilinearRoof(bl, br, tl, tr, u1 + ju1 + jv0R, v0);
                var p11 = BilinearRoof(bl, br, tl, tr, u1 + ju1, v1);
                var p01 = BilinearRoof(bl, br, tl, tr, u0 + ju0, v1);

                // 下面 = 屋根面 + 法線方向のわずかなオフセット（突き抜け防止）
                var normalOff = roofNormal * TypeB_SnowOffset;
                var b00 = p00 + normalOff;
                var b10 = p10 + normalOff;
                var b11 = p11 + normalOff;
                var b01 = p01 + normalOff;

                // 上面 = 下面 + Y方向の高さ（列ごとに段差・手前列は高め）
                float h = TypeB_HeightBase + TypeB_HeightVar[px % TypeB_HeightVar.Length];
                if (py == 0) h += 0.20f; // 手前列は高さを追加（重量感）
                var heightVec = upDir * h;

                var t00 = b00 + heightVec;
                var t10 = b10 + heightVec;
                var t11 = b11 + heightVec;
                var t01 = b01 + heightVec;

                AddSnowBox(root, $"SnowPanel_{px}_{py}", b00, b10, b11, b01, t00, t10, t11, t01);
                totalParts++;
            }
        }

        // ── 雪庇パーツ（手前端を前方に張り出す）───────────────────────
        // v=0（手前端）を4分割し、frontDir方向に押し出す。
        // 先端上面を少し下げて「せり出して垂れている」感を出す。
        const int corniceDiv = 4;
        for (int cx = 0; cx < corniceDiv; cx++)
        {
            float u0 = (float)cx       / corniceDiv;
            float u1 = (float)(cx + 1) / corniceDiv;

            float jL = TypeB_EdgeJitter[cx % 4] * 0.8f;
            float jR = TypeB_EdgeJitter[(cx + 1) % 4] * 0.8f;

            var rootL = BilinearRoof(bl, br, tl, tr, u0 + jL, 0f);
            var rootR = BilinearRoof(bl, br, tl, tr, u1 + jR, 0f);

            var normalOff = roofNormal * TypeB_SnowOffset;

            // 根元（= メインパネル手前端と同じ高さ）
            var rb0 = rootL + normalOff;
            var rb1 = rootR + normalOff;
            var rt0 = rb0 + upDir * TypeB_CorniceHeight;
            var rt1 = rb1 + upDir * TypeB_CorniceHeight;

            // 先端：前方に張り出し＋少し下方（先端垂れ）
            float overhang = TypeB_CorniceOverhang + TypeB_EdgeJitter[cx % 4] * 1.2f;
            var tip = frontDir * overhang + upDir * (-0.20f); // 先端を少し下げる
            var cb0 = rb0 + tip;
            var cb1 = rb1 + tip;
            // 先端上面 = 先端下面 + 高さの半分（先端が薄くなる垂れ形状）
            var ct0 = cb0 + upDir * (TypeB_CorniceHeight * 0.55f);
            var ct1 = cb1 + upDir * (TypeB_CorniceHeight * 0.55f);

            AddSnowBox(root, $"SnowCornice_{cx}", rb0, rb1, cb1, cb0, rt0, rt1, ct1, ct0);
            totalParts++;
        }

        Debug.Log($"[AntiProtocol][TypeB] typeb_snow_created=YES total_parts={totalParts}"
                + $" heightBase={TypeB_HeightBase} heightDir=WorldUp"
                + $" corniceOverhang={TypeB_CorniceOverhang} corniceDiv={corniceDiv}"
                + $" roofNormal={roofNormal} frontDir={frontDir}");
    }

    // 6面ボックス（下面4点 b00/b10/b11/b01, 上面4点 t00/t10/t11/t01）を生成して root の子にする
    // b00=左手前, b10=右手前, b11=右奥, b01=左奥  (時計回り from above for top face)
    static void AddSnowBox(GameObject root, string name,
        Vector3 b00, Vector3 b10, Vector3 b11, Vector3 b01,
        Vector3 t00, Vector3 t10, Vector3 t11, Vector3 t01)
    {
        var verts = new Vector3[]
        {
            // 上面 (0-3): 外向き法線 = 法線方向上
            t00, t10, t11, t01,
            // 前面 (4-7): b00/b10/t10/t00
            b00, b10, t10, t00,
            // 後面 (8-11): b11/b01/t01/t11
            b11, b01, t01, t11,
            // 左面 (12-15): b01/b00/t00/t01
            b01, b00, t00, t01,
            // 右面 (16-19): b10/b11/t11/t10
            b10, b11, t11, t10,
            // 下面 (20-23): 内向き（見えなくてよい）
            b00, b01, b11, b10,
        };
        var tris = new int[]
        {
            // 上面
             0, 1, 2,  0, 2, 3,
            // 前面
             4, 5, 6,  4, 6, 7,
            // 後面
             8, 9,10,  8,10,11,
            // 左面
            12,13,14, 12,14,15,
            // 右面
            16,17,18, 16,18,19,
            // 下面
            20,21,22, 20,22,23,
        };

        var mesh = new Mesh { name = name + "_Mesh" };
        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();

        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale    = Vector3.one;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>();
        SetUnlitColor(go, TypeB_SnowColor);
    }

    // 屋根面の双線形補間（u=左右0→1, v=下→上0→1）
    static Vector3 BilinearRoof(Vector3 bl, Vector3 br, Vector3 tl, Vector3 tr,
                                 float u, float v)
    {
        var bottom = Vector3.Lerp(bl, br, u);
        var top    = Vector3.Lerp(tl, tr, u);
        return Vector3.Lerp(bottom, top, v);
    }
}
#endif
