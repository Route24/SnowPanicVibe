#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;

/// <summary>
/// CLEAN ROOM: SnowVisibilityLab シーンを最小構成で生成する。
/// メニュー: SnowPanic → Create SnowVisibilityLab
/// STEP 1: カメラ前の赤Cube が見えること
/// STEP 2: 傾斜屋根Quad + 白Quad が見えること
/// STEP 3: runtime白Quad 生成確認は CameraVisibilityTest.cs が担当
/// </summary>
public static class SnowVisibilityLabCreator
{
    const string ScenePath = "Assets/Scenes/SnowVisibilityLab.unity";

    // カメラ設定
    const float CamY   =  3.5f;
    const float CamZ   = -6.0f;
    const float CamFOV =  45f;
    const float CamPitchDeg = 25f;  // 下向き角度

    // 屋根設定
    const float RoofY        = 1.2f;
    const float RoofSlopeDeg = 20f;
    const float RoofW        = 4f;
    const float RoofD        = 2.5f;

    [MenuItem("SnowPanic/Create SnowVisibilityLab (CleanRoom)", false, 2)]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── Camera ────────────────────────────────────────────
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags       = CameraClearFlags.SolidColor;
        cam.backgroundColor  = new Color(0.12f, 0.16f, 0.28f, 1f);
        cam.fieldOfView      = CamFOV;
        cam.nearClipPlane    = 0.1f;
        cam.farClipPlane     = 100f;
        cam.cullingMask      = ~0;
        cam.depth            = 0;
        camGo.transform.position = new Vector3(0f, CamY, CamZ);
        camGo.transform.eulerAngles = new Vector3(CamPitchDeg, 0f, 0f);
        camGo.AddComponent<AudioListener>();

        // URP Additional Camera Data
        var urpData = camGo.AddComponent<UniversalAdditionalCameraData>();
        urpData.renderType = CameraRenderType.Base;
        urpData.renderShadows = true;

        // ── Directional Light ──────────────────────────────────
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type      = LightType.Directional;
        light.intensity = 1.2f;
        light.color     = Color.white;
        lightGo.transform.eulerAngles = new Vector3(50f, -30f, 0f);
        lightGo.AddComponent<UniversalAdditionalLightData>();

        // ── Ground Plane ───────────────────────────────────────
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position   = new Vector3(0f, 0f, 0f);
        ground.transform.localScale = new Vector3(2f, 1f, 2f);
        SetURPLitColor(ground, new Color(0.3f, 0.35f, 0.3f));

        // ── Roof Quad (傾斜屋根) ──────────────────────────────
        var roofGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        roofGo.name = "RoofQuad";
        roofGo.transform.position    = new Vector3(0f, RoofY, 0f);
        roofGo.transform.eulerAngles = new Vector3(-RoofSlopeDeg, 0f, 0f);
        roofGo.transform.localScale  = new Vector3(RoofW, RoofD, 1f);
        SetURPLitColor(roofGo, new Color(0.55f, 0.42f, 0.28f)); // 茶色の屋根

        // ── 白 SnowQuad (屋根上・仮雪) ────────────────────────
        var snowQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        snowQuad.name = "SnowQuad_Static";
        // 屋根の子にして屋根面上へ配置
        snowQuad.transform.SetParent(roofGo.transform, false);
        snowQuad.transform.localPosition = new Vector3(0f, 0f, 0.01f); // 屋根面から少し浮かせる
        snowQuad.transform.localScale    = new Vector3(0.98f, 0.7f, 1f);
        snowQuad.transform.localEulerAngles = Vector3.zero;
        SetURPUnlitColor(snowQuad, new Color(0.95f, 0.97f, 1.0f)); // 白

        // ── 赤Cube (カメラ前 STEP1 確認用) ───────────────────
        var redCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        redCube.name = "RedCube_Visibility";
        // カメラ正面 3m（カメラ pos + forward * 3）
        Vector3 camFwd = Quaternion.Euler(CamPitchDeg, 0f, 0f) * Vector3.forward;
        redCube.transform.position   = new Vector3(0f, CamY, CamZ) + camFwd * 3f;
        redCube.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f);
        SetURPUnlitColor(redCube, Color.red);

        // コライダー無効（ゲームに干渉しない）
        var rc = redCube.GetComponent<Collider>();
        if (rc != null) rc.enabled = false;

        // ── CameraVisibilityTest をアタッチ (STEP3 runtime Quad) ─
        camGo.AddComponent<CameraVisibilityTest>();

        // ── シーン保存 ─────────────────────────────────────────
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[SnowVisibilityLab] scene_created=YES path={ScenePath} " +
                  $"cam='{camGo.name}' pos=({camGo.transform.position}) " +
                  $"redCube='{redCube.name}' pos=({redCube.transform.position}) " +
                  $"snowQuad='{snowQuad.name}'");

        EditorUtility.DisplayDialog("SnowVisibilityLab",
            "シーン作成完了！\n\n" +
            "Assets/Scenes/SnowVisibilityLab.unity\n\n" +
            "【STEP 1】Play → 赤Cubeが見えるか確認\n" +
            "【STEP 2】RoofQuad(茶) と SnowQuad_Static(白) が見えるか確認\n" +
            "【STEP 3】Console の [CAM_VISIBILITY] で runtime Quad 生成を確認",
            "OK");
    }

    // URP Lit マテリアル（色付き）
    static void SetURPLitColor(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var sh = Shader.Find("Universal Render Pipeline/Lit")
              ?? Shader.Find("Standard");
        if (sh == null) return;
        var mat = new Material(sh) { name = $"{go.name}_Mat" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        mr.sharedMaterial = mat;
    }

    // URP Unlit マテリアル（ライティング無し、確実に見える）
    static void SetURPUnlitColor(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var sh = Shader.Find("Universal Render Pipeline/Unlit")
              ?? Shader.Find("Unlit/Color")
              ?? Shader.Find("Standard");
        if (sh == null) return;
        var mat = new Material(sh) { name = $"{go.name}_Unlit" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        mr.sharedMaterial = mat;
    }
}
#endif
