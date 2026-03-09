#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>SnowTest prefab を作成し、シーンに配置。activePieces=0 FAIL を解消。Tap→packed→avalanche が動作する状態にする。</summary>
public static class SnowTestPrefabBuilder
{
    const string PrefabPath = "Assets/Prefabs/SnowTest.prefab";

    [MenuItem("SnowPanic/Create SnowTest Prefab & Setup Scene", false, 45)]
    public static void CreatePrefabAndSetupScene()
    {
        var roofCol = ResolveRoofCollider();
        var groundCol = ResolveGroundCollider();
        if (roofCol == null)
        {
            Debug.LogError("[SnowTestPrefab] RoofSlideCollider が見つかりません。シーンに RoofRoot/RoofSlideCollider を配置してください。");
            return;
        }

        var prefab = CreateOrLoadPrefab();
        if (prefab == null)
        {
            Debug.LogError("[SnowTestPrefab] Prefab の作成に失敗しました。");
            return;
        }

        var old = GameObject.Find("SnowTest");
        if (old != null) Object.DestroyImmediate(old);

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = "SnowTest";

        var roofT = roofCol.transform;
        var roofBounds = roofCol.bounds;
        var roofUp = roofT.up.normalized;
        var spawnPos = roofBounds.center + roofUp * (roofBounds.extents.y + 0.15f);
        instance.transform.position = spawnPos;
        instance.transform.rotation = roofT.rotation;

        WireReferences(instance, roofCol, groundCol);
        EnsureSnowPackHierarchy(instance, roofCol);

        EnsureTapToSlide();

        var setup = Object.FindFirstObjectByType<RoofSlideTestAutoSetup>();
        if (setup != null) setup.targetSnowTest = instance.transform;

        Selection.activeGameObject = instance;
        Undo.RegisterCreatedObjectUndo(instance, "SnowTest Prefab Setup");
        Debug.Log("[SnowTestPrefab] SnowTest prefab をシーンに配置しました。Play してタップで雪崩を確認してください。");
    }

    static GameObject CreateOrLoadPrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "SnowTest";
        go.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.linearDamping = 0.5f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        go.AddComponent<GroundSnowSystem>();
        go.AddComponent<RoofSnowSystem>();
        go.AddComponent<SnowPackSpawner>();
        go.AddComponent<SnowFallSystem>();
        go.AddComponent<RoofAlignToSnow>();
        go.AddComponent<SnowTestSlideAssist>();

        var prefabObj = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
        Object.DestroyImmediate(go);
        return prefabObj;
    }

    static void WireReferences(GameObject instance, Collider roofCol, Collider groundCol)
    {
        var ground = instance.GetComponent<GroundSnowSystem>();
        var roof = instance.GetComponent<RoofSnowSystem>();
        var pack = instance.GetComponent<SnowPackSpawner>();
        var fall = instance.GetComponent<SnowFallSystem>();
        var assist = instance.GetComponent<SnowTestSlideAssist>();
        var rb = instance.GetComponent<Rigidbody>();

        if (ground != null) ground.groundCollider = groundCol;
        if (roof != null)
        {
            roof.roofSlideCollider = roofCol;
            roof.groundSnowSystem = ground;
            roof.snowPackSpawner = pack;
        }
        if (pack != null)
        {
            pack.roofCollider = roofCol;
            pack.roofSnowSystem = roof;
            pack.targetDepthMeters = 0.5f;
            pack.packDepthMeters = 0.5f;
            pack.rebuildOnPlay = true;
        }
        if (fall != null)
        {
            fall.roofSnowSystem = roof;
            fall.groundSnowSystem = ground;
            fall.roofSlideCollider = roofCol;
        }
        if (assist != null)
        {
            assist.roofSlideCollider = roofCol;
            assist.rb = rb;
        }

        EditorUtility.SetDirty(instance);
    }

    static void EnsureSnowPackHierarchy(GameObject instance, Collider roofCol)
    {
        var pack = instance.GetComponent<SnowPackSpawner>();
        if (pack == null || roofCol == null) return;
        pack.EnsureSnowPackVisualHierarchy();
        EditorUtility.SetDirty(roofCol.gameObject);
    }

    static Collider ResolveRoofCollider()
    {
        var byName = GameObject.Find("RoofSlideCollider");
        if (byName != null)
        {
            var c = byName.GetComponent<Collider>();
            if (c != null) return c;
        }
        var root = GameObject.Find("RoofRoot");
        if (root != null)
        {
            var child = root.transform.Find("RoofSlideCollider");
            if (child != null)
            {
                var c = child.GetComponent<Collider>();
                if (c != null) return c;
            }
        }
        var all = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
        foreach (var c in all)
        {
            if (c != null && c.name.Contains("RoofSlideCollider")) return c;
        }
        return null;
    }

    static Collider ResolveGroundCollider()
    {
        var plane = GameObject.Find("Plane");
        if (plane != null)
        {
            var c = plane.GetComponent<Collider>();
            if (c != null) return c;
        }
        var ground = GameObject.Find("Ground");
        if (ground != null)
        {
            var c = ground.GetComponent<Collider>();
            if (c != null) return c;
        }
        return null;
    }

    static void EnsureTapToSlide()
    {
        var cam = Object.FindFirstObjectByType<Camera>();
        if (cam == null) return;
        if (cam.GetComponent<TapToSlideOnRoof>() != null) return;
        cam.gameObject.AddComponent<TapToSlideOnRoof>();
    }
}
#endif
