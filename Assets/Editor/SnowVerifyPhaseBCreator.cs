#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Phase B: 雪1個だけ生成確認。崩壊・連鎖・自動追従オフ。
/// Phase A 成功後に実行すること。
/// </summary>
public static class SnowVerifyPhaseBCreator
{
    const string ScenePath = "Assets/Scenes/SnowVerify_PhaseB.unity";
    const float RoofW = 1.5f;
    const float RoofD = 1.5f;
    const float RoofY = 1f;
    const float RoofSlopeDeg = 20f;
    const float CamX = 0f;
    const float CamY = 2.2f;
    const float CamZ = -4f;
    const float CamEulerX = 32f;

    [MenuItem("SnowPanic/Phase B: Create Snow Verify (1 Piece Only)", false, 16)]
    public static void CreatePhaseBScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildPhaseBScene();
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SnowVerifyPhaseB] phase_b_started=true path={ScenePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SnowVerifyPhaseB] Create failed: {e.Message}\n{e.StackTrace}");
        }
    }

    static void BuildPhaseBScene()
    {
        var marker = new GameObject("VerifyMarkerPhaseB");
        marker.AddComponent<SnowVerifyPhaseB>();

        var roofCol = CreateRoof();
        var groundCol = CreateGround();
        CreateMarkerCube();
        CreateSnowPhaseB(roofCol, groundCol);
        SetCamera();
    }

    static Collider CreateRoof()
    {
        var root = new GameObject("RoofPhaseB");
        root.transform.position = new Vector3(0f, RoofY, 0f);
        root.transform.rotation = Quaternion.Euler(RoofSlopeDeg, 0f, 0f);

        var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
        panel.name = "RoofPanel";
        panel.transform.SetParent(root.transform, false);
        panel.transform.localPosition = Vector3.zero;
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = new Vector3(RoofW, 1f, RoofD);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", new Color(0.55f, 0.4f, 0.3f));
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
            plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "Plane";
        plane.transform.position = new Vector3(0f, -0.5f, 0f);
        plane.transform.localScale = new Vector3(2f, 1f, 2f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", new Color(0.85f, 0.88f, 0.9f));
        plane.GetComponent<Renderer>().sharedMaterial = mat;
        return plane.GetComponent<Collider>();
    }

    static void CreateMarkerCube()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "MarkerCubePhaseB";
        cube.transform.position = new Vector3(0.4f, RoofY + 0.12f, 0.2f);
        cube.transform.rotation = Quaternion.Euler(RoofSlopeDeg, 0f, 0f);
        cube.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", Color.yellow);
        cube.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(cube.GetComponent<Collider>());
    }

    static void CreateSnowPhaseB(Collider roofCol, Collider groundCol)
    {
        var go = new GameObject("SnowPhaseB");
        go.transform.position = Vector3.zero;
        go.transform.rotation = Quaternion.identity;

        var ground = go.AddComponent<GroundSnowSystem>();
        ground.groundCollider = groundCol;

        var roof = go.AddComponent<RoofSnowSystem>();
        roof.roofSlideCollider = roofCol;
        roof.groundSnowSystem = ground;
        roof.roofSnowDepthMeters = 0.08f;

        var pack = go.AddComponent<SnowPackSpawner>();
        pack.roofCollider = roofCol;
        pack.roofSnowSystem = roof;
        pack.houseIndex = 0;
        pack.targetDepthMeters = 0.08f;
        pack.packDepthMeters = 0.08f;
        pack.rebuildOnPlay = true;
        pack.enableStateIndicator = false;
        pack.pieceSize = 0.12f;
        pack.debugAutoRefillRoofSnow = false;
        pack.chainDetachChance = 0f;
        pack.maxSecondaryDetachPerHit = 0;
        roof.snowPackSpawner = pack;

        var fall = go.AddComponent<SnowFallSystem>();
        fall.enabled = false;

        pack.EnsureSnowPackVisualHierarchy();
    }

    static void SetCamera()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(CamX, CamY, CamZ);
            cam.transform.rotation = Quaternion.Euler(CamEulerX, 0f, 0f);
        }
        var light = Object.FindFirstObjectByType<Light>();
        if (light != null)
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
    }
}
#endif
