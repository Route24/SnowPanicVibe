using UnityEngine;
using System.Collections;

/// <summary>屋根の雪。こんもり積もった雪。クリックで塊が滑り落ちる</summary>
public class RoofSnow : MonoBehaviour
{
    public ParticleSystem snowParticles;
    [HideInInspector] public Vector3 slideDownDirection;
    [Tooltip("false=軒だけ叩ける（道具未強化）、true=棟も叩ける")]
    public bool canReachRidge = false;
    float _lastHitTime;

    /// <summary>ridgeProximity: 0=軒先(端), 1=棟(高い側)。片流れ屋根も対応</summary>
    float GetRidgeProximity(Vector3 localHit)
    {
        // 滑り方向の主軸で判定。X優勢=切妻の左右、Z優勢=片流れの前後
        if (Mathf.Abs(slideDownDirection.x) > Mathf.Abs(slideDownDirection.z))
        {
            float ridgeAxis = slideDownDirection.x < 0 ? localHit.x : -localHit.x;
            return Mathf.Clamp01((ridgeAxis + 0.5f));
        }
        else
        {
            // 片流れ：軒(-Z側)でridgeProximity=0、棟(+Z側)で=1
            float ridgeAxis = slideDownDirection.z < 0 ? localHit.z : -localHit.z;
            return Mathf.Clamp01((ridgeAxis + 0.5f));
        }
    }

    Coroutine _cascadeCoroutine;
    float _lastEaveCleanup;

    void Update()
    {
        if (snowParticles == null) return;
        if (Time.time - _lastEaveCleanup < 0.15f) return;
        _lastEaveCleanup = Time.time;
        RemoveSnowAtEaves();
    }

    int GetRemainingParticleCount()
    {
        if (snowParticles == null) return 0;
        int max = snowParticles.main.maxParticles;
        var particles = new ParticleSystem.Particle[max];
        int n = snowParticles.GetParticles(particles);
        int alive = 0;
        for (int i = 0; i < n; i++)
            if (particles[i].remainingLifetime > 0.01f) alive++;
        return alive;
    }

    bool HasSnowNear(Vector3 localPoint, float radius = 0.5f)
    {
        if (snowParticles == null) return false;
        int max = snowParticles.main.maxParticles;
        var particles = new ParticleSystem.Particle[max];
        int n = snowParticles.GetParticles(particles);
        for (int i = 0; i < n; i++)
        {
            if (particles[i].remainingLifetime < 0.01f) continue;
            if (Vector3.Distance(particles[i].position, localPoint) < radius)
                return true;
        }
        return false;
    }

    bool HasAnySnow()
    {
        return GetRemainingParticleCount() > 100;
    }

    public void Hit(Vector3 hitPoint, bool fromCascade = false)
    {
        if (!fromCascade && Time.time - _lastHitTime < 0.4f) return;

        Vector3 localHit = transform.InverseTransformPoint(hitPoint);
        if (!HasAnySnow()) return;

        float ridgeProximity = GetRidgeProximity(localHit);
        if (!canReachRidge && ridgeProximity > 0.45f) return;

        if (!fromCascade) _lastHitTime = Time.time;

        bool isCenter = ridgeProximity > 0.5f;
        bool isAvalanche = canReachRidge && isCenter && ridgeProximity > 0.6f && !fromCascade;

        if (isAvalanche)
        {
            if (_cascadeCoroutine != null) { StopCoroutine(_cascadeCoroutine); _cascadeCoroutine = null; }
            _cascadeCoroutine = StartCoroutine(AvalancheCascade(localHit));
            return;
        }

        bool isCascade = isCenter;

        int count;
        float widthScale, heightScale, depthScale;
        if (isCascade)
        {
            count = Random.Range(45, 95);
            widthScale = Random.Range(1f, 1.5f);
            heightScale = Random.Range(0.7f, 1.2f);
            depthScale = Random.Range(1f, 1.4f);
        }
        else
        {
            count = Random.Range(35, 55);
            widthScale = 0.6f;
            heightScale = 2.5f;
            depthScale = 0.75f;
        }

        float slideSpeed = isCascade ? 0.1f : 0.12f; // ゆっくり滑り落ちる感じ
        SpawnSnowClump(hitPoint, count, widthScale, heightScale, depthScale, 0.12f, slideSpeed);
        float radius = isCascade ? 0.35f + (widthScale + depthScale) * 0.12f : 0.45f;
        RemoveSnowAt(hitPoint, radius);
        RemoveSnowAtEaves();
        // 軒先のワールド位置でも明示的に削除
        Vector3 localEave = new Vector3(0f, 0f, slideDownDirection.z < 0 ? -0.4f : 0.4f);
        RemoveSnowAt(transform.TransformPoint(localEave), 0.5f);

        if (isCascade && !fromCascade)
            _cascadeCoroutine = StartCoroutine(CascadeFall(localHit, ridgeProximity));
    }

    /// <summary>雪崩：棟から軒先へ雪塊が連鎖的に流れ落ちる（消さずに全部落とす）</summary>
    IEnumerator AvalancheCascade(Vector3 localHitStart)
    {
        bool useZ = Mathf.Abs(slideDownDirection.z) >= Mathf.Abs(slideDownDirection.x);
        float ridgeA = useZ ? (slideDownDirection.z < 0 ? 0.35f : -0.35f) : (slideDownDirection.x < 0 ? 0.35f : -0.35f);
        float eavesA = useZ ? (slideDownDirection.z < 0 ? -0.35f : 0.35f) : (slideDownDirection.x < 0 ? -0.35f : 0.35f);

        const int Steps = 18;
        const float StepDelay = 0.09f;
        const float SlideSpeed = 0.12f;

        for (int s = 0; s < Steps; s++)
        {
            float t = (s + 0.5f) / Steps;
            float a = Mathf.Lerp(ridgeA, eavesA, t);
            float ortho = Random.Range(-0.35f, 0.35f);
            Vector3 localPos = useZ ? new Vector3(ortho, 0f, a) : new Vector3(a, 0f, ortho);
            Vector3 worldPos = transform.TransformPoint(localPos);

            int count = Random.Range(50, 95);
            float w = Random.Range(1f, 1.5f), h = Random.Range(0.7f, 1.1f), d = Random.Range(1f, 1.4f);
            SpawnSnowClump(worldPos, count, w, h, d, spawnOffsetY: 0.08f, slideSpeed: SlideSpeed);
            RemoveSnowAt(worldPos, 0.5f);

            if (GetRemainingParticleCount() < 30) break;
            yield return new WaitForSeconds(StepDelay);
            if (snowParticles == null || this == null) yield break;
        }
        // わずかな残りだけ削除（ほぼ全て雪塊として流れ落ちた後）
        if (GetRemainingParticleCount() < 150)
            RemoveAllSnow();
    }

    void RemoveAllSnow()
    {
        if (snowParticles == null) return;
        int maxParticles = snowParticles.main.maxParticles;
        var particles = new ParticleSystem.Particle[maxParticles];
        int liveCount = snowParticles.GetParticles(particles);
        for (int i = 0; i < liveCount; i++)
            particles[i].remainingLifetime = 0f;
        snowParticles.SetParticles(particles, liveCount);
    }

    void RemoveSnowAt(Vector3 worldHitPoint, float radius)
    {
        if (snowParticles == null) return;
        Vector3 localHit = transform.InverseTransformPoint(worldHitPoint);
        int maxParticles = snowParticles.main.maxParticles;
        var particles = new ParticleSystem.Particle[maxParticles];
        int liveCount = snowParticles.GetParticles(particles);
        for (int i = 0; i < liveCount; i++)
        {
            float dist = Vector3.Distance(particles[i].position, localHit);
            if (dist < radius)
                particles[i].remainingLifetime = 0f;
        }
        snowParticles.SetParticles(particles, liveCount);
        RemoveSnowAtEaves();
    }

    /// <summary>軒先（滑り落ち方向の端）の残雪を消す</summary>
    void RemoveSnowAtEaves()
    {
        if (snowParticles == null) return;

        int maxParticles = snowParticles.main.maxParticles;
        var particles = new ParticleSystem.Particle[maxParticles];
        int liveCount = snowParticles.GetParticles(particles);

        if (Mathf.Abs(slideDownDirection.z) >= Mathf.Abs(slideDownDirection.x))
        {
            // 片流れ：軒先は Z の端。slideDownDirection.z < 0 なら軒先は -Z 側。広めに消す
            float eaveZ = slideDownDirection.z < 0 ? -0.2f : 0.2f;
            for (int i = 0; i < liveCount; i++)
            {
                if ((slideDownDirection.z < 0 && particles[i].position.z < eaveZ) ||
                    (slideDownDirection.z > 0 && particles[i].position.z > eaveZ))
                    particles[i].remainingLifetime = 0f;
            }
        }
        else
        {
            // 切妻：軒先は X の端
            float eaveX = slideDownDirection.x < 0 ? -0.1f : 0.1f;
            for (int i = 0; i < liveCount; i++)
            {
                if ((slideDownDirection.x < 0 && particles[i].position.x < eaveX) ||
                    (slideDownDirection.x > 0 && particles[i].position.x > eaveX))
                    particles[i].remainingLifetime = 0f;
            }
        }
        snowParticles.SetParticles(particles, liveCount);
    }

    void SpawnSnowClump(Vector3 hitPoint, int count, float widthScale, float heightScale, float depthScale, float spawnOffsetY = 0.15f, float slideSpeed = 0.25f)
    {
        var go = new GameObject("SnowClump");
        Vector3 offset = slideDownDirection.sqrMagnitude > 0.01f
            ? slideDownDirection.normalized * 0.05f + Vector3.up * spawnOffsetY
            : Vector3.up * spawnOffsetY;
        go.transform.position = hitPoint + offset;

        for (int i = 0; i < count; i++)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "Particle";
            p.transform.SetParent(go.transform, false);
            float u = Random.Range(-0.5f, 0.5f);
            float v = Random.Range(-0.5f, 0.5f);
            float w = Random.Range(-0.5f, 0.5f);
            p.transform.localPosition = new Vector3(u * 0.22f * widthScale, w * 0.18f * heightScale, v * 0.22f * depthScale);
            float sz = Random.Range(0.02f, 0.045f);
            p.transform.localScale = new Vector3(sz, sz * 1.1f, sz); // 雪らしく少し縦長
            var pr = p.GetComponent<Renderer>();
            var snowMat = new Material(pr.sharedMaterial);
            snowMat.color = new Color(1f, 1f, 1f, Random.Range(0.9f, 1f));
            pr.sharedMaterial = snowMat;
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
        clump.initialSlideSpeed = slideSpeed;
        var roofPanelCol = transform.parent != null ? transform.parent.GetComponent<Collider>() : null;
        clump.roofColliderToIgnoreWhenStuck = roofPanelCol;

        // 雪塊が屋根雪のコライダーに当たって跳ねないよう無視（屋根パネルとは当たり続けて滑る）
        var roofSnowCol = GetComponent<Collider>();
        var clumpCol = go.GetComponent<Collider>();
        if (roofSnowCol != null && clumpCol != null)
            Physics.IgnoreCollision(clumpCol, roofSnowCol);
    }

    /// <summary>中央を叩いたとき、棟から軒先へ向かって連鎖的に雪を落とす</summary>
    IEnumerator CascadeFall(Vector3 localHitStart, float ridgeProximity)
    {
        const int Steps = 4;
        const float StepDelay = 0.14f;
        const float StepDistance = 0.24f;
        const float SlideSpeed = 0.1f;
        float eavesDir = slideDownDirection.x < 0 ? -1f : 1f;

        for (int s = 1; s <= Steps; s++)
        {
            yield return new WaitForSeconds(StepDelay);
            if (snowParticles == null || this == null) yield break;
            if (GetRemainingParticleCount() < 50) break;

            Vector3 localNext = localHitStart + new Vector3(eavesDir * StepDistance * s, 0f, (s % 2 == 0 ? 0.08f : -0.08f) * s);
            localNext.x = Mathf.Clamp(localNext.x, -0.45f, 0.45f);
            localNext.z = Mathf.Clamp(localNext.z, -0.45f, 0.45f);

            Vector3 worldNext = transform.TransformPoint(localNext);
            int count = Random.Range(35, 75);
            float w = Random.Range(0.9f, 1.4f), h = Random.Range(0.6f, 1f), d = Random.Range(0.9f, 1.3f);
            SpawnSnowClump(worldNext, count, w, h, d, 0.08f, SlideSpeed);
            RemoveSnowAt(worldNext, 0.24f + (w + d) * 0.1f);
        }
    }
}
