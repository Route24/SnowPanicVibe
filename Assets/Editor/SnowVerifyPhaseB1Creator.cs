#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Phase B1: 固定雪1個表示。SnowPack/Pool は使わない。
/// </summary>
public static class SnowVerifyPhaseB1Creator
{
    const string ScenePath = "Assets/Scenes/SnowVerify_PhaseB1.unity";
    const float RoofW = 1.5f;
    const float RoofD = 1.5f;
    const float RoofY = 1f;
    const float RoofSlopeDeg = 20f;
    const float CamX = 0f;
    const float CamY = 2.2f;
    const float CamZ = -4f;
    const float CamEulerX = 32f;
    const float StaticSnowScale = 0.15f;

    [MenuItem("SnowPanic/Phase B1: Create Static Snow Verify", false, 17)]
    public static void CreatePhaseB1Scene()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        try
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            BuildPhaseB1Scene();
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
                AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[SnowVerifyPhaseB1] phase_b1_started=true path={ScenePath}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[SnowVerifyPhaseB1] Create failed: {e.Message}\n{e.StackTrace}");
        }
    }

    static void BuildPhaseB1Scene()
    {
        var marker = new GameObject("VerifyMarkerPhaseB1");
        marker.AddComponent<SnowVerifyPhaseB1>();

        CreateRoof();
        CreateGround();
        CreateMarkerCube();
        CreateStaticSnow();
        SetCamera();
    }

    static void CreateRoof()
    {
        var root = new GameObject("RoofPhaseB1");
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
        cube.name = "MarkerCubePhaseB1";
        cube.transform.position = new Vector3(0.4f, RoofY + 0.12f, 0.2f);
        cube.transform.rotation = Quaternion.Euler(RoofSlopeDeg, 0f, 0f);
        cube.transform.localScale = new Vector3(0.12f, 0.12f, 0.12f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", Color.yellow);
        cube.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(cube.GetComponent<Collider>());
    }

    static void CreateStaticSnow()
    {
        var snow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        snow.name = "StaticSnowPhaseB1";
        snow.transform.position = new Vector3(0f, RoofY + 0.08f, 0f);
        snow.transform.rotation = Quaternion.Euler(RoofSlopeDeg, 0f, 0f);
        snow.transform.localScale = new Vector3(StaticSnowScale, StaticSnowScale * 0.6f, StaticSnowScale);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        mat.SetColor("_BaseColor", new Color(0.95f, 0.97f, 1f));
        snow.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(snow.GetComponent<Collider>());
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
