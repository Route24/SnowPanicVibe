#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 新規最小検証シーン作成。固定値のみで成立。
/// 屋根1枚＋雪1枚＋カメラ1台。既存 SnowVerify_Minimal に依存しない。
/// </summary>
public static class SnowVerifyFixedSceneCreator
{
    const string ScenePath = "Assets/Scenes/SnowVerify_Fixed.unity";
    const float RoofW = 1.5f;
    const float RoofD = 1.5f;
    const float RoofY = 1f;
    const float RoofSlopeDeg = 20f;
    const float CamX = 0f;
    const float CamY = 2.2f;
    const float CamZ = -4f;
    const float CamEulerX = 32f;
    const float CamEulerY = 0f;
    const float CamEulerZ = 0f;

    [MenuItem("SnowPanic/Create Fixed Verify Scene (New)", false, 19)]
    public static void CreateFixedVerifyScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildFixedScene();
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SnowVerifyFixed] new_minimal_scene_created=true scene_name=SnowVerify_Fixed path={ScenePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SnowVerifyFixed] Create failed: {e.Message}\n{e.StackTrace}");
        }
    }

    static void BuildFixedScene()
    {
        CreateVerifyMarkerFixed();
        var roofCol = CreateFixedRoof();
        var groundCol = CreateGround();
        CreateSnowFixed(roofCol, groundCol);
        SetFixedCamera();
    }

    static void CreateVerifyMarkerFixed()
    {
        var go = new GameObject("VerifyMarkerFixed");
        go.AddComponent<SnowVerifyFixedScene>();
    }

    static void SetFixedCamera()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(CamX, CamY, CamZ);
            cam.transform.rotation = Quaternion.Euler(CamEulerX, CamEulerY, CamEulerZ);
        }
        var light = Object.FindFirstObjectByType<Light>();
        if (light != null)
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }

    static Collider CreateFixedRoof()
    {
        var root = new GameObject("RoofFixed");
        root.transform.position = new Vector3(0f, RoofY, 0f);
        root.transform.rotation = Quaternion.Euler(RoofSlopeDeg, 0f, 0f);

        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "RoofPanel";
        panel.transform.SetParent(root.transform, false);
        panel.transform.localPosition = Vector3.zero;
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = new Vector3(RoofW, 1f, RoofD);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", new Color(0.6f, 0.45f, 0.35f));
        panel.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(panel.GetComponent<Collider>());

        var colGo = new GameObject("RoofSlideCollider");
        colGo.transform.SetParent(root.transform, false);
        colGo.transform.localPosition = Vector3.zero;
        colGo.transform.localRotation = Quaternion.identity;
        colGo.transform.localScale = Vector3.one;
        var box = colGo.AddComponent<BoxCollider>();
        box.center = Vector3.zero;
        box.size = new Vector3(RoofW, 0.02f, RoofD);
        box.isTrigger = false;

        return box;
    }

    static Collider CreateGround()
    {
        var plane = GameObject.Find("Plane");
        if (plane == null)
        {
            plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            plane.name = "Plane";
        }
        plane.transform.position = new Vector3(0f, -0.5f, 0f);
        plane.transform.localScale = new Vector3(2f, 1f, 2f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", new Color(0.88f, 0.9f, 0.93f));
        plane.GetComponent<Renderer>().sharedMaterial = mat;
        return plane.GetComponent<Collider>();
    }

    static void CreateSnowFixed(Collider roofCol, Collider groundCol)
    {
        var go = new GameObject("SnowFixed");
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;

        var ground = go.AddComponent<GroundSnowSystem>();
        ground.groundCollider = groundCol;

        var roof = go.AddComponent<RoofSnowSystem>();
        roof.roofSlideCollider = roofCol;
        roof.groundSnowSystem = ground;
        roof.roofSnowDepthMeters = 0.1f;

        var pack = go.AddComponent<SnowPackSpawner>();
        pack.roofCollider = roofCol;
        pack.roofSnowSystem = roof;
        pack.houseIndex = 0;
        pack.targetDepthMeters = 0.1f;
        pack.packDepthMeters = 0.1f;
        pack.rebuildOnPlay = true;
        pack.enableStateIndicator = false;
        pack.pieceSize = 0.12f;
        roof.snowPackSpawner = pack;

        var fall = go.AddComponent<SnowFallSystem>();
        fall.enabled = false;
        fall.roofSnowSystem = roof;
        fall.groundSnowSystem = ground;
        fall.roofSlideCollider = roofCol;

        pack.EnsureSnowPackVisualHierarchy();
    }
}
#endif
