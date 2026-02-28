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
        main.startColor = new Color(0.82f, 0.85f, 0.9f, 0.95f);
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
        shape.scale = new Vector3(8f, 0.5f, 8f);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
                rend.sharedMaterial = new Material(shader) { color = Color.white };
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
        main.startLifetime = 5f;
        main.startSpeed = 0.4f;
        main.startSize = 0.02f;
        main.startColor = new Color(1f, 1f, 1f, 0.7f);
        main.maxParticles = 1500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.2f;
        var em = ps.emission;
        em.rateOverTime = 300f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(15f, 0f, 15f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.02f;
        noise.frequency = 0.2f;
        noise.scrollSpeed = 0.05f;
        noise.damping = true;

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
                rend.sharedMaterial = new Material(shader) { color = Color.white };
        }
    }

    void CreateRoofSnowSystems()
    {
        var roof = transform.Find("Roof");
        if (roof == null) return;

        foreach (var panel in new[] { roof.Find("RoofPanel") })
        {
            if (panel == null) continue;
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
            main.startColor = new Color(0.96f, 0.98f, 1f, 0.97f);
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
                    rend.sharedMaterial = new Material(shader) { color = Color.white };
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
            comp.canReachRidge = true; // 片流れは全体クリック可能

            Object.Destroy(placeholder.GetComponent<RoofSnowPlaceholder>());
        }
    }
}

/// <summary>Editor 用。RoofSnow の slideDownDirection を保持</summary>
public class RoofSnowPlaceholder : MonoBehaviour
{
    public Vector3 slideDownDirection;
}
