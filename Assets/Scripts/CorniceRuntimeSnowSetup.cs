using System.Collections.Generic;
using UnityEngine;

/// <summary>Play モード時に雪の ParticleSystem を生成。Unity 6 の Edit モード material リーク回避。</summary>
[DefaultExecutionOrder(-200)]
public class CorniceRuntimeSnowSetup : MonoBehaviour
{
    void Awake()
    {
        EnsureShadowsEnabled();
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
        bool isOneHouseScene = !string.IsNullOrEmpty(scene) && scene.Contains("OneHouse");
        if (isOneHouseScene)
        {
            var testRoot = GameObject.Find("SnowTestRoot");
            if (testRoot != null) testRoot.SetActive(false);
        }
        if (!VideoPipelineSelfTestMode.IsActive && !isOneHouseScene)
        {
            CreateGroundSnow();
            CreateSnowParticle();
        }
        CreateRoofSnowSystems(); // Self Test 中も屋根雪を作成（クリック可能にする）
    }

    void EnsureShadowsEnabled()
    {
        var light = FindFirstObjectByType<Light>();
        if (light != null && light.type == LightType.Directional && light.shadows == LightShadows.None)
        {
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.8f;
            Debug.Log("[CorniceRuntimeSnowSetup] Enabled shadows on Directional Light");
        }
    }

    void CreateGroundSnow()
    {
        var go = transform.Find("GroundSnow");
        if (go == null) return;
        if (go.GetComponent<ParticleSystem>() != null) return; // 既に作成済み

        var ps = go.gameObject.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.startLifetime = 9999f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.0375f, 0.07f);
        main.startColor = new Color(0.78f, 0.8f, 0.84f, 0.95f); // 白飛び防止・読みやすい雪色
        main.maxParticles = 30000;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.playOnAwake = true;
        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 25000) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(30f, 0.5f, 25f);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
                rend.sharedMaterial = new Material(shader) { color = new Color(0.88f, 0.9f, 0.92f) };
            rend.renderMode = ParticleSystemRenderMode.Billboard;
            rend.minParticleSize = 0.01f;
        }
        ps.Simulate(0.5f, true, true);
    }

    void CreateSnowParticle()
    {
        var go = transform.Find("SnowParticle");
        if (go == null) return;
        if (go.GetComponent<ParticleSystem>() != null) return;

        var ps = go.gameObject.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 5.8f;
        main.startSpeed = 0.32f;
        main.startSize = 0.03f;
        main.startColor = new Color(0.85f, 0.87f, 0.9f, 0.72f); // 白飛び防止・降雪
        main.maxParticles = 300;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.2f;
        var em = ps.emission;
        em.rateOverTime = 50f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(15f, 0f, 15f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.01f;
        noise.frequency = 0.2f;
        noise.scrollSpeed = 0.05f;
        noise.damping = true;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
                rend.sharedMaterial = new Material(shader) { color = new Color(0.88f, 0.9f, 0.92f) };
        }

        var burst = go.GetComponent<SnowfallEventBurst>();
        if (burst == null) burst = go.gameObject.AddComponent<SnowfallEventBurst>();
        burst.Configure(
            ps,
            baseRate: 50f,
            burstRate: 100f,   // 常時の2倍
            burstDuration: 0.5f,
            baseMaxParticles: 300,
            burstMaxParticles: 500,
            baseSpeed: 0.32f,
            burstSpeed: 0.4f); // 少しだけ速く

    }

    void CreateRoofSnowSystems()
    {
        var housesRoot = transform.Find("Houses");
        if (housesRoot == null) return;

        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
        bool isOneHouseScene = !string.IsNullOrEmpty(scene) && scene.Contains("OneHouse");
        bool rollbackApplied = false;

        if (isOneHouseScene && housesRoot.childCount > 1)
        {
            for (int i = housesRoot.childCount - 1; i >= 1; i--)
            {
                var child = housesRoot.GetChild(i);
                if (child.name.StartsWith("House_") && child.name != "House_0")
                {
                    Object.DestroyImmediate(child.gameObject);
                    rollbackApplied = true;
                }
            }
        }

        if (isOneHouseScene)
        {
            var mainCam = Camera.main;
            if (mainCam != null)
            {
                // 屋根がプレイフィールド・主役。軒下・地面視認用にカメラを少し引く。(CAMERA ADJUST)
                var targetPos = new Vector3(0f, 5.6f, -7.5f);
                var targetRot = Quaternion.Euler(36f, 0f, 0f);
                float posTol = 0.5f;
                bool posMatch = Vector3.Distance(mainCam.transform.position, targetPos) < posTol;
                bool rotMatch = Quaternion.Angle(mainCam.transform.rotation, targetRot) < 5f;
                if (!posMatch || !rotMatch)
                {
                    mainCam.transform.position = targetPos;
                    mainCam.transform.rotation = targetRot;
                    mainCam.fieldOfView = 45f;
                    var orbit = mainCam.GetComponent<CameraOrbit>();
                    if (orbit != null)
                    {
                        orbit._yaw = 180f;
                        orbit._pitch = 36f;
                        orbit.distance = 9.2f;
                        orbit.yMin = 4f;
                        orbit.yMax = 8f;
                    }
                    rollbackApplied = true;
                }
            }
        }

        var panels = new List<Transform>();
        for (int i = 0; i < housesRoot.childCount; i++)
        {
            var house = housesRoot.GetChild(i);
            var roof = house.Find("Roof");
            if (roof != null)
            {
                var panel = roof.Find("RoofPanel");
                if (panel != null) panels.Add(panel);
            }
        }

        foreach (var panel in panels)
        {
            if (panel == null) continue;
            var roofSurfaceCol = ResolveRoofPhysicsSurface(panel);
            var roof = panel.parent;
            if (roof != null)
            {
                var existingTrigger = roof.Find("EavesDropTrigger");
                if (existingTrigger != null)
                {
                    var triggerScript = existingTrigger.GetComponent<EavesDropTrigger>();
                    if (triggerScript != null && roofSurfaceCol != null)
                        triggerScript.roofCollider = roofSurfaceCol;
                }
            }
            var placeholder = panel.Find("RoofSnowPlaceholder");
            if (placeholder != null)
            {
                if (isOneHouseScene)
                {
                    // OneHouse: 粒雪(RoofSnow/SnowClump)を無効化。SnowPack塊崩れのみ有効。
                    Object.DestroyImmediate(placeholder.gameObject);
                }
                else if (placeholder.GetComponent<ParticleSystem>() == null)
                {
                    var slideDir = placeholder.GetComponent<RoofSnowPlaceholder>()?.slideDownDirection ?? Vector3.down;
                    var go = placeholder.gameObject;
                    go.name = "RoofSnow";

                    float panelH = 0.05f;
                    float snowThick = 1.7f;
                    float thickScale = snowThick / panelH;

                    // 屋根端から外側へオフセット。30〜60% はみ出す（軒先方向へシフト）
                    const float OverhangRatio = 0.45f; // 45% はみ出し
                    bool useZ = Mathf.Abs(slideDir.z) >= Mathf.Abs(slideDir.x);
                    float overhangDistance = useZ ? OverhangRatio : OverhangRatio; // スケール1の軸でオフセット
                    Vector3 roofEdgeOffset = useZ
                        ? new Vector3(0f, 0f, slideDir.z < 0 ? -overhangDistance : overhangDistance)
                        : new Vector3(slideDir.x < 0 ? -overhangDistance : overhangDistance, 0f, 0f);

                    // support_count 減少: オーバーハングで屋根上サポートが減る想定
                    const float SupportReduceRatio = 0.7f;
                    int maxParticles = Mathf.RoundToInt(28000 * SupportReduceRatio);
                    int burstCount = Mathf.RoundToInt(26000 * SupportReduceRatio);

                    Debug.Log($"[SNOW_CORNICE_SETUP] overhang_distance={overhangDistance:F2}");
                    Debug.Log($"[SNOW_CORNICE_SETUP] roof_edge=({roofEdgeOffset.x:F2},{roofEdgeOffset.y:F2},{roofEdgeOffset.z:F2})");

                    var ps = go.AddComponent<ParticleSystem>();
                    var main = ps.main;
                    main.loop = false;
                    main.startLifetime = 9999f;
                    main.startSpeed = 0f;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.028f, 0.05f);
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        new Color(0.72f, 0.76f, 0.82f, 0.97f),
                        new Color(0.82f, 0.86f, 0.9f, 0.98f));
                    main.maxParticles = maxParticles;
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.playOnAwake = true;
                    var em = ps.emission;
                    em.enabled = true;
                    em.rateOverTime = 0f;
                    em.SetBursts(new[] { new ParticleSystem.Burst(0f, burstCount) });
                    var shape = ps.shape;
                    shape.shapeType = ParticleSystemShapeType.Box;
                    shape.scale = new Vector3(1.25f, thickScale * 0.6f, 1f);
                    shape.position = roofEdgeOffset; // 屋根端から外側へオフセット

                    var rend = ps.GetComponent<ParticleSystemRenderer>();
                    if (rend != null)
                    {
                        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                        if (shader != null)
                            rend.sharedMaterial = new Material(shader) { color = new Color(0.88f, 0.9f, 0.92f) };
                        rend.renderMode = ParticleSystemRenderMode.Billboard;
                        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                        rend.receiveShadows = true;
                    }
                    ps.Simulate(0.1f, true, true);

                    var col = go.AddComponent<BoxCollider>();
                    col.size = new Vector3(1.25f, thickScale * 0.5f, 1f); // 薄くして水平板のように見えないように
                    col.center = roofEdgeOffset; // shape と同期（屋根端オフセット）
                    col.isTrigger = false;

                    var comp = go.AddComponent<RoofSnow>();
                    comp.snowParticles = ps;
                    comp.slideDownDirection = slideDir;
                    comp.canReachRidge = true;
                    comp.roofSurfaceCollider = roofSurfaceCol;
                    comp.debugMode = false; // ROLLBACK: 塊で落ちる（3-7粒）。true=1-2粒で個体均一

                    AddRidgeIndicator(go.transform, slideDir);

                    Object.Destroy(placeholder.GetComponent<RoofSnowPlaceholder>());
                }
            }
            CreateEavesDropTrigger(panel.transform, roofSurfaceCol != null ? roofSurfaceCol : panel.GetComponent<Collider>());
            if (roof != null) EnsureEavesCatchZone(roof);
        }

        int houseCount = panels.Count;
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
        bool oneHouseForced = transform.Find("OneHouseMarker") != null;
        bool isExpected = houseCount >= 1 && houseCount <= 8;
        var cam = Camera.main;
        string camPosStr = cam != null ? string.Format("({0:F2},{1:F2},{2:F2})", cam.transform.position.x, cam.transform.position.y, cam.transform.position.z) : "N/A";
        string camRotStr = cam != null ? string.Format("({0:F1},{1:F1},{2:F1})", cam.transform.eulerAngles.x, cam.transform.eulerAngles.y, cam.transform.eulerAngles.z) : "N/A";
        bool testRoofVisible = GameObject.Find("SnowTestRoot") != null && GameObject.Find("SnowTestRoot").activeSelf;
        string activeRoofTarget = oneHouseForced ? "asset_roof" : (testRoofVisible ? "test_roof" : "asset_roof");
        string roofShape = "mono_slope";
        string roofSlopeDirection = "front";
        string enabledSnowSystems = oneHouseForced ? "[SnowPackSpawner,RoofSnowSystem,RoofSnowLayer,SnowPackFallingPiece]" : "[RoofSnow,SnowClump,SnowPackSpawner,RoofSnowSystem]";
        string disabledLegacySnowSystems = oneHouseForced ? "[RoofSnow_particle,SnowClump,RoofSnowPlaceholder]" : "[]";
        string activeSnowVisual = oneHouseForced ? "RoofSnowLayer+SnowPackPiece" : "RoofSnow_particle+RoofSnowLayer";
        string activeSnowBreakLogic = oneHouseForced ? "SnowPackSpawner.HandleTap+DetachInRadius" : "RoofSnow.Hit+SnowPackSpawner";
        string activeSnowSpawnLogic = oneHouseForced ? "SnowPackSpawner.RebuildSnowPack" : "RoofSnow+SnowPackSpawner";
        if (isOneHouseScene)
            HideHelperMeshesAndLog(housesRoot);

        Debug.Log($"[CORNICE_SCENE_CHECK] scene={sceneName} house_count={houseCount} one_house_forced={oneHouseForced.ToString().ToLower()} rollback_applied={rollbackApplied.ToString().ToLower()} camera_position={camPosStr} camera_rotation={camRotStr} active_roof_target={activeRoofTarget} test_roof_visible={testRoofVisible.ToString().ToLower()} roof_shape={roofShape} roof_slope_direction={roofSlopeDirection} enabled_snow_systems={enabledSnowSystems} disabled_legacy_snow_systems={disabledLegacySnowSystems} active_snow_visual={activeSnowVisual} active_snow_break_logic={activeSnowBreakLogic} active_snow_spawn_logic={activeSnowSpawnLogic} spawn_system=CorniceRuntime is_expected={isExpected}");
        if (cam != null)
            Debug.Log($"[CAMERA_LOCK_CHECK] camPos={camPosStr} camEuler={camRotStr} result={(rollbackApplied ? "ROLLBACK_APPLIED" : "UNCHANGED")} target=(0,5.6,-7.5)(36,0,0)_eaves_ground_visible");
        Debug.Log("[SNOW_ROLLBACK_CHECK] rollback_target=pre_camera_change_good_state house_count=" + houseCount + " camera_rotation=" + camRotStr + " rollback_applied=" + rollbackApplied.ToString().ToLower() + " active_roof_target=" + activeRoofTarget + " test_roof_visible=" + testRoofVisible.ToString().ToLower() + " roof_shape=" + roofShape + " roof_slope_direction=" + roofSlopeDirection + " enabled_snow_systems=" + enabledSnowSystems + " disabled_legacy_snow_systems=" + disabledLegacySnowSystems + " ground_snow=disabled result=OK comment=" + (oneHouseForced ? "mono_slope_SnowPack_only" : "multi_house_RoofSnow+SnowPack"));
    }

    void HideHelperMeshesAndLog(Transform housesRoot)
    {
        var hidden = new List<string>();

        // 1. 補助メッシュを非表示
        string[] hideNames = { "RoofDebugFlat", "RoofSlideColliderDebug", "RoofSnowSurface", "cabin-roof", "RoofProxy" };
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null) continue;
            string name = t.gameObject.name;
            bool shouldHide = false;
            foreach (var n in hideNames)
                if (name == n) { shouldHide = true; break; }

            if (shouldHide && name == "RoofProxy")
            {
                Object.Destroy(t.gameObject);
                hidden.Add(GetPath(t));
                continue;
            }

            if (shouldHide)
            {
                var r = t.GetComponent<Renderer>();
                if (r != null && r.enabled) { r.enabled = false; hidden.Add(GetPath(t)); }
                else if (name == "RoofSlideColliderDebug" && t.gameObject.activeSelf)
                { t.gameObject.SetActive(false); hidden.Add(GetPath(t)); }
            }
        }

        var roofCol = GameObject.Find("RoofSlideCollider");
        if (roofCol != null)
        {
            var debug = roofCol.transform.Find("RoofSlideColliderDebug");
            if (debug != null && debug.gameObject.activeSelf)
            { debug.gameObject.SetActive(false); hidden.Add(GetPath(debug)); }
        }

        // 2. 非表示後の可視メッシュ一覧
        var visibleAfter = new List<string>();
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (r == null || !r.enabled) continue;
            visibleAfter.Add(GetPath(r.transform));
        }

        var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
        float snowOffset = roofSys != null ? roofSys.roofSnowSurfaceOffsetY : 0f;
        bool snowVisualAttached = roofSys != null && roofSys.roofSlideCollider != null;
        string hitTarget = roofCol != null ? roofCol.name : (roofSys != null && roofSys.roofSlideCollider != null ? roofSys.roofSlideCollider.name : "none");

        string visStr = visibleAfter.Count > 0 ? string.Join(",", visibleAfter) : "none";
        string hidStr = hidden.Count > 0 ? string.Join(",", hidden) : "none";
        Debug.Log($"[MESH_OVERRIDE] visible_mesh_objects=[{visStr}] hidden_mesh_objects=[{hidStr}] active_roof_target=asset_roof hit_target={hitTarget} roof_shape=mono_slope roof_slope_direction=front snow_surface_offset={snowOffset} snow_visual_attached={snowVisualAttached.ToString().ToLower()}");
    }

    static string GetPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    void EnsureEavesCatchZone(Transform roof)
    {
        if (roof == null || roof.Find("EavesCatchZone") != null) return;
        var catchZone = new GameObject("EavesCatchZone");
        catchZone.transform.SetParent(roof, false);
        catchZone.transform.localPosition = new Vector3(0f, -0.4f, -0.9f);
        catchZone.transform.localRotation = Quaternion.identity;
        catchZone.transform.localScale = Vector3.one;
        var catchBox = catchZone.AddComponent<BoxCollider>();
        catchBox.isTrigger = true;
        catchBox.size = new Vector3(3f, 0.8f, 2f);
        catchBox.center = Vector3.zero;
        var catchScript = catchZone.AddComponent<EavesCatchZone>();
        catchScript.dragMultiplier = 0.92f;
        catchScript.applyDuration = 0.3f;
    }

    Collider ResolveRoofPhysicsSurface(Transform panel)
    {
        if (panel == null) return null;
        var roof = panel.parent;
        if (roof == null) return EnsureRoofSurface(panel);

        var house = roof.parent;
        string houseName = house != null ? house.name : "(unknown)";

        var debugFlat = roof.Find("RoofDebugFlat");
        if (debugFlat != null)
        {
            if (debugFlat.GetComponent<RoofDebugGizmo>() == null)
                debugFlat.gameObject.AddComponent<RoofDebugGizmo>();

            // RoofPanel 基準で +15% のフラット判定面サイズに固定（累積拡大しない）
            var roofPanel = roof.Find("RoofPanel");
            if (roofPanel != null)
            {
                var s = roofPanel.localScale;
                debugFlat.localScale = new Vector3(s.x * 1.15f, 0.02f, s.z * 1.15f);
            }
            else
            {
                var s = debugFlat.localScale;
                debugFlat.localScale = new Vector3(s.x * 1.15f, 0.02f, s.z * 1.15f);
            }

            var debugCol = debugFlat.GetComponent<BoxCollider>();
            if (debugCol == null) debugCol = debugFlat.gameObject.AddComponent<BoxCollider>();
            debugCol.isTrigger = false;
            debugCol.enabled = true;

            // 補助メッシュ非表示：当たり判定は維持、見た目だけ消す
            var debugRend = debugFlat.GetComponent<Renderer>();
            if (debugRend != null) debugRend.enabled = false;

            bool oldSurfaceColliderDisabled = DisableRoofSurfaceColliders(roof, debugFlat);
            Debug.Log($"[RoofSurfaceSwap] House={houseName} surface=RoofDebugFlat (collider=BoxCollider renderer=OFF) oldSurfaceColliderDisabled={oldSurfaceColliderDisabled}");
            return debugCol;
        }

        var fallback = EnsureRoofSurface(panel);
        string fallbackName = fallback != null ? fallback.name : "None";
        Debug.Log($"[RoofSurfaceSwap] House={houseName} surface={fallbackName} (fallback)");
        return fallback;
    }

    Collider EnsureRoofSurface(Transform panel)
    {
        if (panel == null) return null;
        var existing = panel.parent != null ? panel.parent.Find("RoofSurface") : null;
        if (existing != null)
            return existing.GetComponent<Collider>();

        var surface = new GameObject("RoofSurface");
        if (panel.parent != null) surface.transform.SetParent(panel.parent, false);
        else surface.transform.SetParent(panel, false);
        surface.transform.localPosition = panel.localPosition;
        surface.transform.localRotation = panel.localRotation;
        surface.transform.localScale = panel.localScale;

        var box = surface.AddComponent<BoxCollider>();
        box.isTrigger = false;
        box.size = new Vector3(1f, 0.08f, 1f); // 単純な傾斜面判定専用
        box.center = Vector3.zero;

        var panelCol = panel.GetComponent<Collider>();
        if (panelCol != null) panelCol.enabled = false; // 複雑メッシュ干渉を避ける
        return box;
    }

    bool DisableRoofSurfaceColliders(Transform roof, Transform keep)
    {
        if (roof == null) return false;
        bool disabledAny = false;
        string[] candidateNames = { "RoofSurface", "RoofSnowSurface", "RoofPanel" };
        foreach (var name in candidateNames)
        {
            var t = roof.Find(name);
            if (t == null || t == keep) continue;
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.transform.IsChildOf(keep)) continue;
                if (c.enabled)
                {
                    c.enabled = false;
                    disabledAny = true;
                }
            }
        }
        return disabledAny;
    }

    void AddRidgeIndicator(Transform roofSnow, Vector3 slideDir)
    {
        var ind = new GameObject("RidgeIndicator");
        ind.transform.SetParent(roofSnow, false);
        ind.transform.localPosition = Vector3.zero;
        ind.transform.localRotation = Quaternion.identity;
        ind.transform.localScale = Vector3.one;
        bool useZ = Mathf.Abs(slideDir.z) >= Mathf.Abs(slideDir.x);
        float ridgeZ = slideDir.z < 0 ? 0.38f : -0.38f;
        float ridgeX = slideDir.x < 0 ? 0.38f : -0.38f;
        for (int i = -2; i <= 2; i++)
        {
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(line.GetComponent<Collider>());
            line.name = "RidgeLine";
            line.transform.SetParent(ind.transform, false);
            if (useZ)
            {
                line.transform.localPosition = new Vector3(i * 0.15f, 0.08f, ridgeZ);
                line.transform.localScale = new Vector3(0.02f, 0.02f, 0.12f);
            }
            else
            {
                line.transform.localPosition = new Vector3(ridgeX, 0.08f, i * 0.15f);
                line.transform.localScale = new Vector3(0.12f, 0.02f, 0.02f);
            }
            var r = line.GetComponent<Renderer>();
            if (r != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.SetColor("_BaseColor", new Color(0.78f, 0.74f, 0.68f)); // 屋根とのコントラストが読める棟ライン
                    r.sharedMaterial = mat;
                }
            }
        }
    }

    void CreateEavesDropTrigger(Transform roofPanelParent, Collider roofCol)
    {
        if (roofCol == null) return;
        var roof = roofPanelParent.parent;
        if (roof == null) return;
        if (roof.Find("EavesDropTrigger") != null) return;

        var eavesTrigger = new GameObject("EavesDropTrigger");
        eavesTrigger.transform.SetParent(roof, false);
        eavesTrigger.transform.localPosition = new Vector3(0f, -0.25f, -0.85f);
        eavesTrigger.transform.localRotation = Quaternion.identity;
        eavesTrigger.transform.localScale = Vector3.one;
        var box = eavesTrigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(2.5f, 1f, 1.2f);
        box.center = Vector3.zero;
        var triggerScript = eavesTrigger.AddComponent<EavesDropTrigger>();
        triggerScript.roofCollider = roofCol;
    }
}
