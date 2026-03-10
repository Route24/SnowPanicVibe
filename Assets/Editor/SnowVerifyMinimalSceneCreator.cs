#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>検証専用最小シーン: 固定値で成立。屋根1枚・雪1種類・カメラ固定。自動追従なし。</summary>
public static class SnowVerifyMinimalSceneCreator
{
    const string ScenePath = "Assets/Scenes/SnowVerify_Minimal.unity";
    const float RoofW = 1.8f;
    const float RoofD = 0.9f;
    const float CamX = 0f, CamY = 2.2f, CamZ = -3.5f;
    const float CamEulerX = 38f, CamEulerY = 0f, CamEulerZ = 0f;

    [MenuItem("SnowPanic/Create Minimal Verify Scene", false, 20)]
    public static void CreateMinimalVerifyScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildMinimalScene();
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SnowVerify] 固定値最小検証シーンを作成しました: {ScenePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SnowVerify] Create Minimal Verify Scene failed: {e.Message}\n{e.StackTrace}");
        }
    }

    static void BuildMinimalScene()
    {
        CreateVerifyMarker();
        var roofCol = CreateFixedRoof();
        var groundCol = CreateGround();
        CreateSnowTest(roofCol, groundCol);
        SetFixedCamera();
        EnsureTapToSlide();
    }

    static void CreateVerifyMarker()
    {
        var go = new GameObject("VerifyMarker");
        go.AddComponent<SnowVerifyMinimalScene>();
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
        var root = new GameObject("RoofRoot");
        root.transform.position = new Vector3(0f, 1.2f, 0f);
        root.transform.rotation = Quaternion.Euler(18f, 0f, 0f);

        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "RoofPanel";
        panel.transform.SetParent(root.transform, false);
        panel.transform.localPosition = Vector3.zero;
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = new Vector3(RoofW, 1f, RoofD);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", new Color(0.65f, 0.5f, 0.4f));
        panel.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(panel.GetComponent<Collider>());

        var colGo = new GameObject("RoofSlideCollider");
        colGo.transform.SetParent(root.transform, false);
        colGo.transform.localPosition = Vector3.zero;
        colGo.transform.localRotation = Quaternion.identity;
        colGo.transform.localScale = Vector3.one;
        var box = colGo.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0f, 0f);
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
        plane.transform.position = Vector3.zero;
        plane.transform.localScale = new Vector3(2f, 1f, 2f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", new Color(0.9f, 0.92f, 0.95f));
        plane.GetComponent<Renderer>().sharedMaterial = mat;
        return plane.GetComponent<Collider>();
    }

    static void CreateSnowTest(Collider roofCol, Collider groundCol)
    {
        var go = new GameObject("SnowTest");
        go.transform.position = roofCol.bounds.center + roofCol.transform.up * 0.1f;
        go.transform.rotation = roofCol.transform.rotation;

        var ground = go.AddComponent<GroundSnowSystem>();
        ground.groundCollider = groundCol;

        var roof = go.AddComponent<RoofSnowSystem>();
        roof.roofSlideCollider = roofCol;
        roof.groundSnowSystem = ground;
        roof.roofSnowDepthMeters = 0.4f;

        var pack = go.AddComponent<SnowPackSpawner>();
        pack.roofCollider = roofCol;
        pack.roofSnowSystem = roof;
        pack.targetDepthMeters = 0.4f;
        pack.packDepthMeters = 0.4f;
        pack.rebuildOnPlay = true;
        pack.enableStateIndicator = false;
        pack.pieceSize = 0.22f;
        roof.snowPackSpawner = pack;

        var fall = go.AddComponent<SnowFallSystem>();
        fall.enabled = false;
        fall.roofSnowSystem = roof;
        fall.groundSnowSystem = ground;
        fall.roofSlideCollider = roofCol;

        pack.EnsureSnowPackVisualHierarchy();
    }

    static void EnsureTapToSlide()
    {
        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam != null && cam.GetComponent<TapToSlideOnRoof>() == null)
            cam.gameObject.AddComponent<TapToSlideOnRoof>();
    }
}
#endif
