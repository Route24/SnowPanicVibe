#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>SnowTest を SnowPack 系コンポーネント付きで再生成。Tap→packed→avalanche→slide が動作する状態にする。</summary>
public static class SnowTestSnowPackSetup
{
    [MenuItem("SnowPanic/Setup SnowTest With SnowPack", false, 50)]
    public static void Setup()
    {
        var roofCol = ResolveRoofCollider();
        var groundCol = ResolveGroundCollider();
        if (roofCol == null)
        {
            Debug.LogError("[SnowTestSetup] RoofSlideCollider が見つかりません。シーンに RoofRoot/RoofSlideCollider を配置してください。");
            return;
        }

        var old = GameObject.Find("SnowTest");
        if (old != null) Object.DestroyImmediate(old);

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "SnowTest";
        go.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

        var roofT = roofCol.transform;
        var roofBounds = roofCol.bounds;
        var roofUp = roofT.up.normalized;
        var spawnPos = roofBounds.center + roofUp * (roofBounds.extents.y + 0.15f);
        go.transform.position = spawnPos;
        go.transform.rotation = roofT.rotation;

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.linearDamping = 0.5f;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        var ground = GetOrCreate<GroundSnowSystem>(go);
        ground.groundCollider = groundCol;

        var roof = GetOrCreate<RoofSnowSystem>(go);
        roof.roofSlideCollider = roofCol;
        roof.groundSnowSystem = ground;

        var pack = GetOrCreate<SnowPackSpawner>(go);
        pack.roofCollider = roofCol;
        pack.roofSnowSystem = roof;
        pack.targetDepthMeters = 0.5f;
        pack.packDepthMeters = 0.5f;
        pack.rebuildOnPlay = true;
        roof.snowPackSpawner = pack;
        pack.EnsureSnowPackVisualHierarchy();

        var fall = GetOrCreate<SnowFallSystem>(go);
        fall.roofSnowSystem = roof;
        fall.groundSnowSystem = ground;
        fall.roofSlideCollider = roofCol;

        var align = GetOrCreate<RoofAlignToSnow>(go);
        align.alignOnStart = true;

        var assist = GetOrCreate<SnowTestSlideAssist>(go);
        assist.roofSlideCollider = roofCol;
        assist.rb = rb;

        EnsureTapToSlide();

        var setup = Object.FindFirstObjectByType<RoofSlideTestAutoSetup>();
        if (setup != null)
        {
            setup.targetSnowTest = go.transform;
            Debug.Log("[SnowTestSetup] RoofSlideTestAutoSetup.targetSnowTest を設定しました。");
        }

        Selection.activeGameObject = go;
        Debug.Log("[SnowTestSetup] SnowTest を再生成しました。SnowPackSpawner, RoofSnowSystem, RoofAlignToSnow, SnowPackVisual/SnowPackPiecesRoot を追加済み。Play してタップで雪崩を確認してください。");
    }

    static T GetOrCreate<T>(GameObject host) where T : Component
    {
        var c = host.GetComponent<T>();
        if (c == null) c = host.AddComponent<T>();
        return c;
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
        Debug.Log("[SnowTestSetup] TapToSlideOnRoof を Main Camera に追加しました。");
    }
}
#endif
