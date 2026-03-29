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
    const float roofThickness = 2.0f;
    const float roofWidth     = 6f;
    const float roofDepth     = 4f;

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
        var bgQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bgQuad.name = "BackgroundImage";
        bgQuad.transform.SetParent(envGo.transform, false);
        bgQuad.transform.localPosition = new Vector3(0f, -0.3f, 0.6f);
        bgQuad.transform.localEulerAngles = new Vector3(36f, 0f, 0f);
        bgQuad.transform.localScale = new Vector3(15.0f, 8.5f, 1f);
        var bgTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/clean_background.png");
        if (bgTex != null)
        {
            var bgSh = Shader.Find("Unlit/Texture") ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (bgSh != null)
            {
                var bgMat = new Material(bgSh) { name = "BackgroundImage_Mat" };
                bgMat.mainTexture = bgTex;
                bgQuad.GetComponent<MeshRenderer>().sharedMaterial = bgMat;
            }
        }
        var bgCol = bgQuad.GetComponent<Collider>();
        if (bgCol != null) bgCol.enabled = false;

        // ── Roof (Environment の子) ──────────────────────────
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Roof";
        roof.transform.SetParent(envGo.transform, false);
        roof.transform.localPosition = Vector3.zero;
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
