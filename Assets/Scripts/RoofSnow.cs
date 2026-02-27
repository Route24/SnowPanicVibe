using UnityEngine;

/// <summary>屋根の雪。こんもり積もった雪。クリックで塊が滑り落ちる</summary>
public class RoofSnow : MonoBehaviour
{
    public ParticleSystem snowParticles;
    [HideInInspector] public Vector3 slideDownDirection;
    float _lastHitTime;

    public void Hit(Vector3 hitPoint)
    {
        if (Time.time - _lastHitTime < 0.4f) return;
        _lastHitTime = Time.time;
        int count;
        float widthScale, heightScale, depthScale;
        SpawnSnowClump(hitPoint, out count, out widthScale, out heightScale, out depthScale);
        RemoveSnowAt(hitPoint, widthScale, depthScale);
    }

    void RemoveSnowAt(Vector3 worldHitPoint, float widthScale, float depthScale)
    {
        if (snowParticles == null) return;
        Vector3 localHit = transform.InverseTransformPoint(worldHitPoint);
        float radius = 0.18f + (widthScale + depthScale) * 0.1f;
        int maxParticles = snowParticles.main.maxParticles;
        var particles = new ParticleSystem.Particle[maxParticles];
        int liveCount = snowParticles.GetParticles(particles);
        int removed = 0;
        for (int i = 0; i < liveCount; i++)
        {
            float dist = Vector3.Distance(particles[i].position, localHit);
            if (dist < radius)
            {
                particles[i].remainingLifetime = 0f;
                removed++;
            }
        }
        snowParticles.SetParticles(particles, liveCount);
    }

    void SpawnSnowClump(Vector3 hitPoint, out int count, out float widthScale, out float heightScale, out float depthScale)
    {
        var go = new GameObject("SnowClump");
        go.transform.position = hitPoint + Vector3.up * 0.25f;

        // 毎回ランダム：量・形・厚み
        count = Random.Range(28, 95);
        int shapeType = Random.Range(0, 3); // 0=平板, 1=塊, 2=細長い
        widthScale = heightScale = depthScale = 1f;
        switch (shapeType)
        {
            case 0: widthScale = Random.Range(0.9f, 1.5f); heightScale = Random.Range(0.4f, 0.75f); depthScale = Random.Range(0.85f, 1.3f); break;
            case 1: widthScale = Random.Range(0.7f, 1.2f); heightScale = Random.Range(0.9f, 1.5f); depthScale = Random.Range(0.7f, 1.2f); break;
            default: widthScale = Random.Range(0.5f, 0.9f); heightScale = Random.Range(0.6f, 1.1f); depthScale = Random.Range(1.2f, 1.8f); break;
        }

        for (int i = 0; i < count; i++)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            p.name = "Particle";
            p.transform.SetParent(go.transform, false);
            float u = Random.Range(-0.5f, 0.5f);
            float v = Random.Range(-0.5f, 0.5f);
            float w = Random.Range(-0.5f, 0.5f);
            p.transform.localPosition = new Vector3(u * 0.28f * widthScale, w * 0.22f * heightScale, v * 0.28f * depthScale);
            float sz = Random.Range(0.025f, 0.055f);
            p.transform.localScale = Vector3.one * sz;
            p.GetComponent<Renderer>().material.color = new Color(1f, 1f, 1f, Random.Range(0.9f, 1f));
            Object.Destroy(p.GetComponent<Collider>());
        }

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.mass = Mathf.Lerp(1f, 3f, (count - 28f) / 67f); // 量に応じて質量
        rb.linearDamping = 0.2f;
        rb.angularDamping = 2f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(0.5f * widthScale, 0.12f * heightScale, 0.4f * depthScale);
        col.center = Vector3.zero;

        var pm = new PhysicsMaterial("SnowSlide") { dynamicFriction = 0.08f, staticFriction = 0.12f, bounciness = 0f };
        col.material = pm;

        var clump = go.AddComponent<SnowClump>();
        clump.slideDownDirection = slideDownDirection;
    }
}
