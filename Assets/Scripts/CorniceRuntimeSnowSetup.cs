using System.Collections.Generic;
using UnityEngine;

/// <summary>Play モード時に雪の ParticleSystem を生成。Unity 6 の Edit モード material リーク回避。</summary>
[DefaultExecutionOrder(-200)]
public class CorniceRuntimeSnowSetup : MonoBehaviour
{
    void Awake()
    {
        CreateGroundSnow();
        CreateSnowParticle();
        CreateRoofSnowSystems();
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
            if (placeholder == null) continue;
            if (placeholder.GetComponent<ParticleSystem>() != null) continue;

            var slideDir = placeholder.GetComponent<RoofSnowPlaceholder>()?.slideDownDirection ?? Vector3.down;
            var go = placeholder.gameObject;
            go.name = "RoofSnow";

            float panelH = 0.05f;
            float snowThick = 1.7f;
            float thickScale = snowThick / panelH;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.startLifetime = 9999f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.028f, 0.05f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.72f, 0.76f, 0.82f, 0.97f),
                new Color(0.82f, 0.86f, 0.9f, 0.98f));
            main.maxParticles = 28000;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.playOnAwake = true;
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 26000) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.25f, thickScale * 0.6f, 1f);

            var rend = ps.GetComponent<ParticleSystemRenderer>();
            if (rend != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
                if (shader != null)
                    rend.sharedMaterial = new Material(shader) { color = new Color(0.88f, 0.9f, 0.92f) };
                rend.renderMode = ParticleSystemRenderMode.Billboard;
            }
            ps.Simulate(0.1f, true, true);

            var col = go.AddComponent<BoxCollider>();
            col.size = new Vector3(1.25f, thickScale * 0.5f, 1f); // 薄くして水平板のように見えないように
            col.center = Vector3.zero;
            col.isTrigger = false;

            var comp = go.AddComponent<RoofSnow>();
            comp.snowParticles = ps;
            comp.slideDownDirection = slideDir;
            comp.canReachRidge = true;
            comp.roofSurfaceCollider = roofSurfaceCol;

            AddRidgeIndicator(go.transform, slideDir);

            Object.Destroy(placeholder.GetComponent<RoofSnowPlaceholder>());

            // 軒先トリガー（既存シーン用：Setup で作られていない場合）
            CreateEavesDropTrigger(panel.transform, roofSurfaceCol != null ? roofSurfaceCol : panel.GetComponent<Collider>());
        }
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

            bool oldSurfaceColliderDisabled = DisableRoofSurfaceColliders(roof, debugFlat);
            Debug.Log($"[RoofSurfaceSwap] House={houseName} surface=RoofDebugFlat (collider=BoxCollider) oldSurfaceColliderDisabled={oldSurfaceColliderDisabled}");
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
