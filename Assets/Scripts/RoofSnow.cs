using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>屋根の雪。こんもり積もった雪。クリックで塊が滑り落ちる</summary>
public class RoofSnow : MonoBehaviour
{
    public ParticleSystem snowParticles;
    public Collider roofSurfaceCollider;
    [Header("Debug")]
    public bool debugMode = true;
    [Header("Safety caps")]
    public int maxActiveClumps = 30;
    [Range(0f, 1f)] public float snowAmount = 1f;
    [Header("Snow consume")]
    public float consumeOnSpawn = 0.01f;
    public float consumeOnDeposit = 0.02f;
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
    float _lastPressureDetachTime;
    float _lastCapLogTime;
    int _initialSnowParticles = -1;
    readonly List<SnowClump> _activeClumps = new List<SnowClump>();

    void Update()
    {
        SnowClump.TickDiagnostics();
        if (snowParticles == null) return;
        if (_initialSnowParticles < 0) _initialSnowParticles = Mathf.Max(1, GetRemainingParticleCount());
        if (GetRemainingParticleCount() < 50) return;
        if (Time.time - _lastEaveCleanup < 0.2f) return;
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

    public bool HasAnySnow()
    {
        return GetRemainingParticleCount() > 0;
    }

    public void Hit(Vector3 hitPoint, bool fromCascade = false)
    {
        if (!fromCascade && Time.time - _lastHitTime < 0.15f) return;
        slideDownDirection = ResolveSlideDirection();

        Vector3 localHit = transform.InverseTransformPoint(hitPoint);
        int remaining = GetRemainingParticleCount();
        if (remaining <= 0) return;

        float ridgeProximity = GetRidgeProximity(localHit);
        if (!canReachRidge && ridgeProximity > 0.45f) return;

        if (!fromCascade) _lastHitTime = Time.time;

        bool isCenter = ridgeProximity > 0.5f;
        bool isCriticalRidge = canReachRidge && ridgeProximity > 0.65f && !fromCascade;
        bool isAvalanche = isCriticalRidge;

        if (isAvalanche)
        {
            AvalancheFeedback.Trigger();
            if (_cascadeCoroutine != null) { StopCoroutine(_cascadeCoroutine); _cascadeCoroutine = null; }
            _cascadeCoroutine = StartCoroutine(AvalancheCascade(localHit));
            return;
        }

        bool isCascade = isCenter;

        int cluster = debugMode ? Random.Range(1, 2) : (isCascade ? Random.Range(3, 7) : Random.Range(3, 5));
        float longAxis = isCascade ? 0.6f : 0.42f;
        float shortAxis = isCascade ? 0.25f : 0.2f;
        SpawnDetachCluster(hitPoint, cluster, shortAxis, longAxis, isCascade ? 0.1f : 0.12f);
        RemoveSnowAtEaves();
        // 軒先のワールド位置でも明示的に削除
        Vector3 localEave = new Vector3(0f, 0f, slideDownDirection.z < 0 ? -0.4f : 0.4f);
        RemoveSnowAt(transform.TransformPoint(localEave), 0.5f);

        if (isCascade && !fromCascade)
            _cascadeCoroutine = StartCoroutine(CascadeFall(localHit, ridgeProximity));

        if (remaining <= 20)
        {
            RemoveAllSnow();
        }
    }

    /// <summary>雪崩：棟から軒先へ雪塊が連鎖的に流れ落ちる（消さずに全部落とす）</summary>
    IEnumerator AvalancheCascade(Vector3 localHitStart)
    {
        bool useZ = Mathf.Abs(slideDownDirection.z) >= Mathf.Abs(slideDownDirection.x);
        float ridgeA = useZ ? (slideDownDirection.z < 0 ? 0.35f : -0.35f) : (slideDownDirection.x < 0 ? 0.35f : -0.35f);
        float eavesA = useZ ? (slideDownDirection.z < 0 ? -0.35f : 0.35f) : (slideDownDirection.x < 0 ? -0.35f : 0.35f);

        const int Steps = 18;
        const float FirstStepDelay = 0.12f;
        const float MinStepDelay = 0.03f;
        const float SlideSpeed = 0.12f;
        float currentDelay = FirstStepDelay;
        Vector3 lastWorldPos = transform.TransformPoint(localHitStart);
        bool hadStep = false;

        for (int s = 0; s < Steps; s++)
        {
            float t = (s + 0.5f) / Steps;
            float a = Mathf.Lerp(ridgeA, eavesA, t);
            float ortho = Random.Range(-0.35f, 0.35f);
            Vector3 localPos = useZ ? new Vector3(ortho, 0f, a) : new Vector3(a, 0f, ortho);
            Vector3 worldPos = transform.TransformPoint(localPos);

            int cluster = Random.Range(3, 6);
            SpawnDetachCluster(worldPos, cluster, 0.24f, 0.6f, SlideSpeed);
            lastWorldPos = worldPos;
            hadStep = true;

            if (GetRemainingParticleCount() < 30) break;
            yield return new WaitForSeconds(currentDelay);
            float delayMul = s <= 2 ? 0.8f : 0.7f; // ギアシフト: 0-2段は緩やか、3段目以降は強く加速
            currentDelay = Mathf.Max(MinStepDelay, currentDelay * delayMul);
            if (snowParticles == null || this == null) yield break;
        }

        if (hadStep)
        {
            SnowfallEventBurst.TriggerEndRewardGlobal(20f, 0.2f);
            SpawnPowderPuff(lastWorldPos);
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

    bool SpawnSnowClump(Vector3 hitPoint, int count, float widthScale, float heightScale, float depthScale, float spawnOffsetY = 0.15f, float slideSpeed = 0.25f, float consumeAmount = 0f)
    {
        if (roofSurfaceCollider == null)
        {
            Debug.LogError($"[RoofSnow] Spawn aborted: roofSurfaceCollider is null on {name}");
            return false;
        }

        CleanupActiveClumps();
        SnowClump.EvictOldestGroundPiecesIfNeeded(SnowClump.MaxActiveSnowPieces);
        if (SnowClump.GetDynamicCount() >= SnowClump.MaxActiveDynamicPieces)
        {
            if (Time.time - _lastCapLogTime > 1f)
            {
                _lastCapLogTime = Time.time;
                Debug.Log($"[RoofSnow] dynamic cap reached ({SnowClump.GetDynamicCount()}/{SnowClump.MaxActiveDynamicPieces}) on {name}");
            }
            return false;
        }
        if (SnowClump.GetActiveCount() >= SnowClump.MaxActiveSnowPieces)
        {
            if (Time.time - _lastCapLogTime > 1f)
            {
                _lastCapLogTime = Time.time;
                Debug.Log($"[RoofSnow] global clump cap reached ({SnowClump.GetActiveCount()}/{SnowClump.MaxActiveSnowPieces}) on {name}");
            }
            return false;
        }
        if (_activeClumps.Count >= Mathf.Max(1, maxActiveClumps))
        {
            if (Time.time - _lastCapLogTime > 1f)
            {
                _lastCapLogTime = Time.time;
                Debug.Log($"[RoofSnow] per-roof clump cap reached ({_activeClumps.Count}/{maxActiveClumps}) on {name}");
            }
            return false;
        }

        var go = new GameObject("SnowClump");
        int snowClumpLayer = LayerMask.NameToLayer("SnowClump");
        int fallingLayer = LayerMask.NameToLayer("FallingSnow");
        if (snowClumpLayer >= 0) go.layer = snowClumpLayer;
        else if (fallingLayer >= 0) go.layer = fallingLayer;
        Vector3 closest = roofSurfaceCollider.ClosestPoint(hitPoint);
        Vector3 spawnPos = closest + roofSurfaceCollider.transform.up * 0.03f;
        go.transform.position = spawnPos;
        Debug.Log($"[RoofSnowSpawn] hitPoint={hitPoint} closestPoint={closest} spawnPosition={spawnPos} roofSurface={roofSurfaceCollider.name}");

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
            MaterialColorHelper.SetColorSafe(snowMat, new Color(1f, 1f, 1f, Random.Range(0.9f, 1f)));
            pr.sharedMaterial = snowMat;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            pr.receiveShadows = true;
            Object.Destroy(p.GetComponent<Collider>());
        }

        var rb = go.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.mass = Mathf.Lerp(2f, 5f, (count - 28f) / 67f);
        rb.linearDamping = 0.2f;
        rb.angularDamping = 2f;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(0.5f * widthScale, 0.12f * heightScale, 0.4f * depthScale);
        col.center = Vector3.zero;

        var pm = new PhysicsMaterial("SnowSlide") { dynamicFriction = 0.08f, staticFriction = 0.12f, bounciness = 0f };
        col.material = pm;

        var clump = go.AddComponent<SnowClump>();
        Vector3 resolvedSlideDir = ResolveSlideDirection();
        clump.slideDownDirection = resolvedSlideDir;
        clump.initialSlideSpeed = slideSpeed;
        var roofPanelCol = roofSurfaceCollider;
        clump.roofColliderToIgnoreWhenStuck = roofPanelCol;
        clump.roofSurfaceCollider = roofPanelCol;
        clump.ownerRoofSnow = this;
        clump.debugMode = debugMode;
        clump.consumeOnDepositAmount = consumeOnDeposit;
        _activeClumps.Add(clump);
        SnowClump.RecordSpawn();
        if (consumeAmount > 0f) ConsumeSnow(consumeAmount);

        // 雪塊が屋根雪のコライダーに当たって跳ねないよう無視（屋根パネルとは当たり続けて滑る）
        var roofSnowCol = GetComponent<Collider>();
        var clumpCol = go.GetComponent<Collider>();
        if (roofSnowCol != null && clumpCol != null)
            Physics.IgnoreCollision(clumpCol, roofSnowCol);
        return true;
    }

    public void TryDetachByPressure(Vector3 worldPoint, float pressure)
    {
        if (debugMode) return;
        if (snowParticles == null) return;
        if (Time.time - _lastPressureDetachTime < 0.08f) return;
        if (GetRemainingParticleCount() < 80) return;
        if (pressure < 0.45f) return;

        _lastPressureDetachTime = Time.time;
        int cluster = Mathf.Clamp(Mathf.RoundToInt(pressure * 0.8f), 1, 3);
        SpawnDetachCluster(worldPoint, cluster, 0.18f, 0.36f, 0.18f);
    }

    /// <summary>中央を叩いたとき、棟から軒先へ向かって連鎖的に雪を落とす</summary>
    IEnumerator CascadeFall(Vector3 localHitStart, float ridgeProximity)
    {
        const int Steps = 4;
        const float FirstStepDelay = 0.12f;
        const float MinStepDelay = 0.03f;
        const float StepDistance = 0.24f;
        const float SlideSpeed = 0.1f;
        float eavesDir = slideDownDirection.x < 0 ? -1f : 1f;
        float currentDelay = FirstStepDelay;

        for (int s = 1; s <= Steps; s++)
        {
            yield return new WaitForSeconds(currentDelay);
            int zeroBasedStep = s - 1;
            float delayMul = zeroBasedStep <= 2 ? 0.8f : 0.7f;
            currentDelay = Mathf.Max(MinStepDelay, currentDelay * delayMul);
            if (snowParticles == null || this == null) yield break;
            if (GetRemainingParticleCount() < 50) break;

            Vector3 localNext = localHitStart + new Vector3(eavesDir * StepDistance * s, 0f, (s % 2 == 0 ? 0.08f : -0.08f) * s);
            localNext.x = Mathf.Clamp(localNext.x, -0.45f, 0.45f);
            localNext.z = Mathf.Clamp(localNext.z, -0.45f, 0.45f);

            Vector3 worldNext = transform.TransformPoint(localNext);
            SpawnDetachCluster(worldNext, Random.Range(2, 4), 0.2f, 0.42f, SlideSpeed);
        }
    }

    void CleanupActiveClumps()
    {
        for (int i = _activeClumps.Count - 1; i >= 0; i--)
            if (_activeClumps[i] == null) _activeClumps.RemoveAt(i);
    }

    public void NotifyClumpDestroyed(SnowClump clump)
    {
        if (clump == null) return;
        _activeClumps.Remove(clump);
    }

    public void ConsumeSnow(float amount)
    {
        if (amount <= 0f) return;
        snowAmount = Mathf.Clamp01(snowAmount - amount);
        ApplySnowAmountToVisual();
    }

    public void OnClumpDeposited(float amount)
    {
        float before = snowAmount;
        ConsumeSnow(amount);
        float after = snowAmount;
        Debug.Log($"[RoofSnowDepositSync] owner={name} snowAmount {before:F3} -> {after:F3} (amount={amount:F3})");
    }

    void ApplySnowAmountToVisual()
    {
        if (snowParticles == null) return;
        if (_initialSnowParticles < 0) _initialSnowParticles = Mathf.Max(1, GetRemainingParticleCount());

        int current = GetRemainingParticleCount();
        int target = Mathf.Clamp(Mathf.RoundToInt(_initialSnowParticles * snowAmount), 0, _initialSnowParticles);
        if (current <= target) return;

        int maxParticles = snowParticles.main.maxParticles;
        var particles = new ParticleSystem.Particle[maxParticles];
        int liveCount = snowParticles.GetParticles(particles);
        int removeNeed = current - target;
        for (int i = 0; i < liveCount && removeNeed > 0; i++)
        {
            if (particles[i].remainingLifetime <= 0.01f) continue;
            if (Random.value < 0.5f)
            {
                particles[i].remainingLifetime = 0f;
                removeNeed--;
            }
        }
        for (int i = 0; i < liveCount && removeNeed > 0; i++)
        {
            if (particles[i].remainingLifetime > 0.01f)
            {
                particles[i].remainingLifetime = 0f;
                removeNeed--;
            }
        }
        snowParticles.SetParticles(particles, liveCount);
    }

    void SpawnDetachCluster(Vector3 centerWorld, int clumpCount, float radiusShort, float radiusLong, float slideSpeed)
    {
        if (clumpCount <= 0) return;
        Vector3 slide = ResolveSlideDirection();
        Vector3 ortho = Vector3.Cross(Vector3.up, slide).normalized;
        if (ortho.sqrMagnitude < 0.001f) ortho = Vector3.right;

        int spawned = 0;
        for (int i = 0; i < clumpCount; i++)
        {
            float u = Random.Range(-1f, 1f);
            float v = Random.Range(-1f, 1f);
            Vector3 p = centerWorld + ortho * (u * radiusShort) + slide * (v * radiusLong * 0.5f);
            int count = Random.Range(30, 62);
            float w = Random.Range(0.8f, 1.25f);
            float h = Random.Range(0.55f, 1.05f);
            float d = Random.Range(0.8f, 1.25f);
            if (SpawnSnowClump(p, count, w, h, d, 0.08f, slideSpeed, consumeOnSpawn))
            {
                RemoveSnowAt(p, 0.18f + (w + d) * 0.08f);
                spawned++;
            }
        }

        if (spawned == 0 && clumpCount > 0)
            Debug.Log($"[RoofSnow] cluster spawn blocked by cap on {name}");
    }

    Vector3 ResolveSlideDirection()
    {
        if (roofSurfaceCollider != null)
        {
            Vector3 roofUp = roofSurfaceCollider.transform.up;
            Vector3 dir = Vector3.ProjectOnPlane(Vector3.down, roofUp);
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.ProjectOnPlane(-roofSurfaceCollider.transform.forward, roofUp);
            if (dir.sqrMagnitude >= 0.0001f)
                return dir.normalized;
        }

        if (slideDownDirection.sqrMagnitude >= 0.0001f)
            return slideDownDirection.normalized;

        return Vector3.forward;
    }

    void SpawnPowderPuff(Vector3 worldPos)
    {
        var go = new GameObject("AvalanchePowderPuff");
        go.transform.position = worldPos + Vector3.up * 0.08f;
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false; // 明示 Play まで待機（duration 再生中変更エラー回避）
        // duration は変更しない（再生中変更で Unity エラー回避）。ワンショットバーストは startLifetime で制御
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.15f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.35f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
        main.startColor = new Color(0.9f, 0.92f, 0.95f, 0.55f);
        main.maxParticles = 32;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var em = ps.emission;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14, 20) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.12f;

        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.12f, 0.12f);
        velocity.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        UnityEngine.Debug.Log("[RoofSnow] particle_error_source=AvalanchePowderPuff particle_velocity_mode_fixed=true");

        var color = ps.colorOverLifetime;
        color.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] {
                new GradientColorKey(new Color(0.92f, 0.94f, 0.97f), 0f),
                new GradientColorKey(new Color(0.92f, 0.94f, 0.97f), 1f)
            },
            new[] {
                new GradientAlphaKey(0.0f, 0f),
                new GradientAlphaKey(0.45f, 0.15f),
                new GradientAlphaKey(0.2f, 0.6f),
                new GradientAlphaKey(0f, 1f)
            });
        color.color = new ParticleSystem.MinMaxGradient(grad);

        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
                rend.sharedMaterial = new Material(shader) { color = new Color(0.9f, 0.92f, 0.95f, 0.5f) };
        }

        ps.Play();
        Destroy(go, 0.6f);
    }
}
