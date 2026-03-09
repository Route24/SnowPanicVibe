using UnityEngine;

/// <summary>
/// Boots the new controlled MVP snow architecture at runtime.
/// </summary>
public class SnowMvpBootstrap : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var snowTest = GameObject.Find("SnowTest");
        if (snowTest != null && snowTest.GetComponent<SnowPackSpawner>() != null)
        {
            snowTest.SetActive(true);
            EnsureTapToSlideStatic();
            Debug.Log("[SnowMVP] SnowTest に SnowPackSpawner あり。既存セットアップを使用。");
            return;
        }

        var existing = FindFirstObjectByType<SnowMvpBootstrap>();
        if (existing != null) return;

        var root = new GameObject("SnowMVP");
        var boot = root.AddComponent<SnowMvpBootstrap>();
        boot.Setup();
    }

    static void EnsureTapToSlideStatic()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (cam.GetComponent<TapToSlideOnRoof>() != null) return;
        cam.gameObject.AddComponent<TapToSlideOnRoof>();
    }

    void Setup()
    {
        var roofCol = ResolveRoofCollider();
        var groundCol = ResolveGroundCollider();

        var ground = GetOrCreate<GroundSnowSystem>(gameObject);
        ground.groundCollider = groundCol;

        var roof = GetOrCreate<RoofSnowSystem>(gameObject);
        roof.roofSlideCollider = roofCol;
        roof.groundSnowSystem = ground;
        roof.roofSnowDepthMeters = 0.5f;

        var pack = GetOrCreate<SnowPackSpawner>(gameObject);
        roof.snowPackSpawner = pack;
        pack.roofCollider = roofCol;
        pack.roofSnowSystem = roof;
        pack.targetDepthMeters = 0.5f;
        pack.packDepthMeters = 0.5f;
        pack.rebuildOnPlay = true;
        pack.EnsureSnowPackVisualHierarchy();

        var fall = GetOrCreate<SnowFallSystem>(gameObject);
        fall.roofSnowSystem = roof;
        fall.groundSnowSystem = ground;
        fall.roofSlideCollider = roofCol;
        fall.spawnIntervalSeconds = 0.06f;
        fall.spawnPerTick = 2;
        fall.addPerLandingMeters = 0.01f;
        fall.addPerGroundHit = 0.01f;

        var core = GetOrCreate<CoreGameplayManager>(gameObject);
        core.collapseThresholdMeters = 0.95f;

        var cooldown = GetOrCreate<ToolCooldownManager>(gameObject);
        cooldown.cooldownSec = 1.2f;

        EnsureTapToSlide();
        DisableLegacyPrototype();
        Debug.Log("[SnowMVP] bootstrap complete (RoofSnowSystem/SnowFallSystem/GroundSnowSystem)");
    }

    static T GetOrCreate<T>(GameObject host) where T : Component
    {
        var c = host.GetComponent<T>();
        if (c == null) c = host.AddComponent<T>();
        return c;
    }

    Collider ResolveRoofCollider()
    {
        var byName = GameObject.Find("RoofSlideCollider");
        if (byName != null)
        {
            var c = byName.GetComponent<Collider>();
            if (c != null) return c;
        }

        var all = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (c.name.Contains("RoofSlideCollider")) return c;
        }
        return null;
    }

    Collider ResolveGroundCollider()
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

    void EnsureTapToSlide()
    {
        var cam = Camera.main;
        if (cam == null) return;
        if (cam.GetComponent<CorniceHitter>() != null) return;
        if (cam.GetComponent<TapToSlideOnRoof>() != null) return;
        cam.gameObject.AddComponent<TapToSlideOnRoof>();
        Debug.Log("[SnowMVP] TapToSlideOnRoof added to main camera");
    }

    void DisableLegacyPrototype()
    {
        var snowTest = GameObject.Find("SnowTest");
        if (snowTest != null && snowTest.GetComponent<SnowPackSpawner>() != null)
            return;

        var setup = FindFirstObjectByType<RoofSlideTestAutoSetup>();
        if (setup != null)
        {
            setup.autoRunOnPlay = false;
            setup.enableNaturalSnowLoop = false;
            setup.enabled = false;
            Debug.Log("[SnowMVP] disabled legacy RoofSlideTestAutoSetup");
        }

        if (snowTest != null)
        {
            snowTest.SetActive(false);
            var assist = snowTest.GetComponent<SnowTestSlideAssist>();
            if (assist != null) assist.enableDebugVisuals = false;
            Debug.Log("[SnowMVP] SnowTest disabled (no grid/lattice in GameView)");
        }
    }
}
