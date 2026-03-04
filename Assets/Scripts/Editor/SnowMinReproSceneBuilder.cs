#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>ミニマル再現シーン Avalanche_MinRepro_01 を新規作成する</summary>
public static class SnowMinReproSceneBuilder
{
    const int FixedRandomSeed = 12345;
    const float RoofAngleDeg = 15f;

    [MenuItem("Tools/Snow Panic/Create MinRepro Scene (Avalanche_MinRepro_01)", false, 50)]
    public static void CreateMinReproScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // Ground Plane
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0, 0, 0);
        ground.transform.localScale = new Vector3(2, 1, 2);

        // Roof Plane: 15° tilt so roofNormal ~ (0, 0.966, 0.259)
        var roof = GameObject.CreatePrimitive(PrimitiveType.Plane);
        roof.name = "RoofPlane";
        roof.transform.position = new Vector3(0, 2f, 0);
        roof.transform.rotation = Quaternion.Euler(RoofAngleDeg, 0, 0);
        roof.transform.localScale = new Vector3(1.5f, 1, 1.5f);
        var roofCollider = roof.GetComponent<Collider>();
        if (roofCollider == null) roof.AddComponent<BoxCollider>();

        // Root for snow systems
        var snowRoot = new GameObject("SnowMinRepro");
        snowRoot.transform.position = Vector3.zero;

        var groundSnow = snowRoot.AddComponent<GroundSnowSystem>();
        var roofSnow = snowRoot.AddComponent<RoofSnowSystem>();
        var snowPack = snowRoot.AddComponent<SnowPackSpawner>();
        var snowFall = snowRoot.AddComponent<SnowFallSystem>();

        roofSnow.roofSlideCollider = roofCollider;
        roofSnow.groundSnowSystem = groundSnow;
        roofSnow.snowPackSpawner = snowPack;

        snowPack.roofCollider = roofCollider;
        snowPack.roofSnowSystem = roofSnow;
        snowPack.snowFallSystem = snowFall;

        snowFall.roofSnowSystem = roofSnow;
        snowFall.groundSnowSystem = groundSnow;
        snowFall.snowPackSpawner = snowPack;
        snowFall.roofSlideCollider = roofCollider;
        snowFall.spawnIntervalSeconds = 0.08f;
        snowFall.spawnPerTick = 2;
        snowFall.addPerLandingMeters = 0.012f;

        snowRoot.AddComponent<SnowMinReproBootstrap>();

        // Camera position
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(0, 3, -6);
            cam.transform.LookAt(new Vector3(0, 1.5f, 0));
        }

        var dir = "Assets/Scenes";
        if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
        EditorSceneManager.SaveScene(scene, dir + "/Avalanche_MinRepro_01.unity");
        Debug.Log("[MinRepro] Created Avalanche_MinRepro_01. Use InitState(12345) for fixed random.");
    }
}
#endif
