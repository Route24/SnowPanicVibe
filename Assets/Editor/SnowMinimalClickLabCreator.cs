#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 未指示処理がゼロの最小検証シーン SnowMinimalClickLab を新規作成する。
/// メニュー: SnowPanic → Create SnowMinimalClickLab
/// 存在するもの: Camera / Light / Ground / Roof / CyanBox のみ。
/// </summary>
public static class SnowMinimalClickLabCreator
{
    const string ScenePath = "Assets/Scenes/SnowMinimalClickLab.unity";

    [MenuItem("SnowPanic/Create SnowMinimalClickLab (CleanRoom)", false, 10)]
    public static void CreateScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ── Main Camera ───────────────────────────────────────
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags      = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.12f, 0.20f, 1f);
        cam.fieldOfView     = 50f;
        cam.nearClipPlane   = 0.1f;
        cam.farClipPlane    = 100f;
        cam.cullingMask     = ~0;
        camGo.transform.position    = new Vector3(0f, 3.0f, -6.0f);
        camGo.transform.eulerAngles = new Vector3(20f, 0f, 0f);
        camGo.AddComponent<AudioListener>();

        // URP Camera Data
        var urp = camGo.AddComponent<UniversalAdditionalCameraData>();
        urp.renderType    = CameraRenderType.Base;
        urp.renderShadows = true;

        // ── Directional Light ──────────────────────────────────
        var lightGo = new GameObject("Directional Light");
        var lt = lightGo.AddComponent<Light>();
        lt.type      = LightType.Directional;
        lt.intensity = 1.2f;
        lt.color     = Color.white;
        lightGo.transform.eulerAngles = new Vector3(50f, -30f, 0f);
        lightGo.AddComponent<UniversalAdditionalLightData>();

        // ── Ground ────────────────────────────────────────────
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position   = Vector3.zero;
        ground.transform.localScale = new Vector3(2f, 1f, 2f);
        SetLitColor(ground, new Color(0.28f, 0.32f, 0.25f));
        // 物理コライダーは残す（Raycast 不要なので無効化）
        var gc = ground.GetComponent<Collider>();
        if (gc != null) gc.enabled = false;

        // ── Roof ──────────────────────────────────────────────
        var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Roof";
        roof.transform.position    = new Vector3(0f, 1.2f, 0.5f);
        roof.transform.eulerAngles = new Vector3(-20f, 0f, 0f);
        roof.transform.localScale  = new Vector3(4f, 0.1f, 2.5f);
        SetLitColor(roof, new Color(0.52f, 0.38f, 0.22f));
        var rc = roof.GetComponent<Collider>();
        if (rc != null) rc.enabled = false;

        // ── CyanBox ───────────────────────────────────────────
        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "CyanBox";
        // Roof の上面に乗せる（Roof の localPos + 傾きを考慮して少し上）
        box.transform.position    = new Vector3(0f, 1.55f, 0.3f);
        box.transform.eulerAngles = new Vector3(-20f, 0f, 0f);
        box.transform.localScale  = new Vector3(0.4f, 0.4f, 0.4f);
        SetUnlitColor(box, Color.cyan);
        // コライダーは有効（クリック Raycast 用）

        // ── MinimalClickHandler を Camera にアタッチ ─────────
        var handler = camGo.AddComponent<MinimalClickHandler>();
        handler.targetName = "CyanBox";

        // ── シーン保存 ────────────────────────────────────────
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            AssetDatabase.CreateFolder("Assets", "Scenes");

        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.Refresh();

        Debug.Log($"[SnowMinimalClickLab] clean_scene_created=YES path={ScenePath}");
        EditorUtility.DisplayDialog("SnowMinimalClickLab",
            "シーン作成完了！\n" +
            ScenePath + "\n\n" +
            "【確認手順】\n" +
            "1. Play → 屋根(茶)とシアンのボックス1個だけ見えるか確認\n" +
            "2. シアンボックスをクリック → 消えるか確認\n" +
            "3. 5秒待って何も出ないか確認\n" +
            "4. Stop → ASSI Report でレポート確認",
            "OK");
    }

    static void SetLitColor(GameObject go, Color color)
    {
        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) return;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (sh == null) return;
        var mat = new Material(sh) { name = go.name + "_Mat" };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
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
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
        mr.sharedMaterial = mat;
    }
}
#endif
