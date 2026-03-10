#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Phase A: シーン成立確認のみ。Snow 系スクリプトは一切追加しない。
/// </summary>
public static class SnowVerifyPhaseACreator
{
    const string ScenePath = "Assets/Scenes/SnowVerify_PhaseA.unity";
    const float RoofW = 1.5f;
    const float RoofD = 1.5f;
    const float RoofY = 1f;
    const float RoofSlopeDeg = 20f;
    const float CamX = 0f;
    const float CamY = 2.2f;
    const float CamZ = -4f;
    const float CamEulerX = 32f;

    [MenuItem("SnowPanic/Phase A: Create Scene Verify (No Snow)", false, 15)]
    public static void CreatePhaseAScene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildPhaseAScene();
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SnowVerifyPhaseA] phase_a_scene_created=true path={ScenePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SnowVerifyPhaseA] Create failed: {e.Message}\n{e.StackTrace}");
        }
    }

    static void BuildPhaseAScene()
    {
        var marker = new GameObject("VerifyMarkerPhaseA");
        marker.AddComponent<SnowVerifyPhaseA>();

        CreateRoof();
        CreateGround();
        CreateMarkerCube();
        SetCamera();
    }

    static void CreateRoof()
    {
        var root = new GameObject("RoofPhaseA");
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
    }

    static void CreateGround()
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
    }

    static void CreateMarkerCube()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "MarkerCubePhaseA";
        cube.transform.position = new Vector3(0f, RoofY + 0.15f, 0f);
        cube.transform.rotation = Quaternion.Euler(RoofSlopeDeg, 0f, 0f);
        cube.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", Color.red);
        cube.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(cube.GetComponent<Collider>());
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
