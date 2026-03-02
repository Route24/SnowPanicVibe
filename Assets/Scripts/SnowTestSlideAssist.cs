using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Freeze + creep + natural auto avalanche loop.
/// </summary>
public class SnowTestSlideAssist : MonoBehaviour
{
    public Rigidbody rb;
    public Collider roofSlideCollider;
    public bool parentOnFreeze = false;
    public bool enableSnowCreep = true;
    public float creepDelaySeconds = 0.5f;
    public float creepDistance = 0.35f;
    public float creepSpeed = 0.35f;
    public float creepSnapOffset = 0.02f;
    public bool enableDebugVisuals = true;

    [Header("Snow Load / Auto Burst")]
    public float addPerLanding = 0.08f;
    public float baseThreshold = 0.30f;
    public float slopeFactor = 0.25f;
    public float burstSpeed = 1.4f;
    public float stickKick = 0.15f;
    public float burstDuration = 0.25f;
    public float avalancheCooldownSeconds = 1.0f;
    public float loadDropOnBurst = 0.35f;
    public bool forceAvalancheNow = false;
    [Header("Ground deposit")]
    public float groundDepositAmount = 0.12f;

    public string LastContactName { get; private set; } = "None";
    public string LastContactColliderName { get; private set; } = "None";
    public float LastOffDist { get; private set; } = 999f;
    public float LastDeltaPosThisFixed { get; private set; } = 0f;
    public float MovedDistanceLast1s { get; private set; } = 0f;
    public int LastMovedFrame { get; private set; } = -1;
    public bool LastGroundedOnRoof { get; private set; } = false;
    public string SlideMode => _freezeApplied ? "Frozen" : (_inAvalancheCooldown ? "Avalanche" : (_inBurst ? "Burst" : "Dynamic"));
    public bool ReadyForNextDrop => _freezeApplied && !_isCreeping && !_inBurst;
    public int LandingCount => _landingCount;

    static readonly Dictionary<int, float> RoofLoad = new Dictionary<int, float>();

    bool _freezeApplied;
    bool _creepStarted;
    bool _isCreeping;
    bool _inBurst;
    bool _inAvalancheCooldown;
    bool _landingCounted;
    bool _groundDeposited;
    int _landingCount;
    Vector3 _lastContactNormal = Vector3.up;
    Vector3 _prevPos;
    bool _prevPosValid;
    float[] _distanceRing;
    int _distanceRingCapacity;
    int _distanceRingIndex;
    float _distanceRingSum;
    LineRenderer _debugLine;
    TextMesh _debugText;
    float _nextSnowLoopLogTime;
    float _nextSpawnInDebug;
    bool _configLogged;
    bool _hasLockedConfig;
    float _lockedBaseThreshold;
    float _lockedSlopeFactor;
    float _lockedAddPerLanding;
    float _lockedBurstSpeed;
    float _lockedStickKick;
    float _lockedBurstDuration;
    float _lockedLoadDropOnBurst;

    void Awake()
    {
        if (rb == null) rb = GetComponent<Rigidbody>();
        _prevPos = rb != null ? rb.position : transform.position;
        _prevPosValid = true;
        EnsureDistanceRingCapacity();
        EnsureDebugVisuals();
    }

    public void RegisterExternalPositionWrite(string writer, Vector3 pos)
    {
        transform.position = pos;
        if (rb != null) rb.position = pos;
    }

    public void EnterSlideModeFromSetup(Collider roofCol, Vector3 roofUpHint)
    {
        roofSlideCollider = roofCol;
    }

    public void ApplyLoopConfig(
        string source,
        float newAddPerLanding,
        float newBaseThreshold,
        float newSlopeFactor,
        float newBurstSpeed,
        float newStickKick,
        float newBurstDuration,
        float newLoadDropOnBurst)
    {
        MaybeLogOverride(source, "addPerLanding", addPerLanding, newAddPerLanding);
        MaybeLogOverride(source, "baseThreshold", baseThreshold, newBaseThreshold);
        MaybeLogOverride(source, "slopeFactor", slopeFactor, newSlopeFactor);
        MaybeLogOverride(source, "burstSpeed", burstSpeed, newBurstSpeed);
        MaybeLogOverride(source, "stickKick", stickKick, newStickKick);
        MaybeLogOverride(source, "burstDuration", burstDuration, newBurstDuration);
        MaybeLogOverride(source, "loadDropOnBurst", loadDropOnBurst, newLoadDropOnBurst);

        addPerLanding = newAddPerLanding;
        baseThreshold = newBaseThreshold;
        slopeFactor = newSlopeFactor;
        burstSpeed = newBurstSpeed;
        stickKick = newStickKick;
        burstDuration = newBurstDuration;
        loadDropOnBurst = newLoadDropOnBurst;

        _lockedAddPerLanding = addPerLanding;
        _lockedBaseThreshold = baseThreshold;
        _lockedSlopeFactor = slopeFactor;
        _lockedBurstSpeed = burstSpeed;
        _lockedStickKick = stickKick;
        _lockedBurstDuration = burstDuration;
        _lockedLoadDropOnBurst = loadDropOnBurst;
        _hasLockedConfig = true;
    }

    public void RequestForceAvalancheNow()
    {
        forceAvalancheNow = true;
    }

    public void SetNextSpawnInDebug(float seconds)
    {
        _nextSpawnInDebug = Mathf.Max(0f, seconds);
    }

    public void BeginDropFromSpawn(Vector3 worldPos)
    {
        if (rb == null) return;
        if (parentOnFreeze && transform.parent == roofSlideCollider?.transform)
            transform.SetParent(null, true);
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.WakeUp();
        transform.position = worldPos;
        rb.position = worldPos;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        _freezeApplied = false;
        _creepStarted = false;
        _isCreeping = false;
        _inBurst = false;
        _landingCounted = false;
        _groundDeposited = false;
        LastGroundedOnRoof = false;
        LastContactName = "None";
        LastContactColliderName = "None";
    }

    void OnCollisionEnter(Collision col)
    {
        if (roofSlideCollider != null && col.collider == roofSlideCollider)
        {
            if (col.contactCount > 0)
                _lastContactNormal = col.GetContact(0).normal.normalized;
            if (_inAvalancheCooldown) return;
            ApplyFreeze();
            return;
        }

        if (IsGroundCollision(col.collider))
        {
            Vector3 p = col.contactCount > 0 ? col.GetContact(0).point : transform.position;
            ConvertToGroundDeposit(p);
        }
    }

    void OnCollisionStay(Collision col)
    {
        if (roofSlideCollider == null || col.collider != roofSlideCollider) return;
        LastGroundedOnRoof = true;
        LastContactName = roofSlideCollider.name;
        LastContactColliderName = roofSlideCollider.name;
        if (col.contactCount > 0)
            _lastContactNormal = col.GetContact(0).normal.normalized;
        LogContactState("stay", _lastContactNormal);
    }

    void OnCollisionExit(Collision col)
    {
        if (roofSlideCollider == null || col.collider != roofSlideCollider) return;
        if (!_freezeApplied)
        {
            LastGroundedOnRoof = false;
            LastContactName = "None";
            LastContactColliderName = "None";
        }
        LogContactState("exit", _lastContactNormal);
    }

    void ApplyFreeze()
    {
        if (rb == null || roofSlideCollider == null || _freezeApplied || _inAvalancheCooldown) return;

        Vector3 roofUp = _lastContactNormal.sqrMagnitude > 0.0001f
            ? _lastContactNormal
            : roofSlideCollider.transform.up.normalized;
        Vector3 snapPos = roofSlideCollider.ClosestPoint(transform.position) + roofUp * 0.02f;

        if (!rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.position = snapPos;
        transform.position = snapPos;

        if (parentOnFreeze)
            transform.SetParent(roofSlideCollider.transform, true);

        _freezeApplied = true;
        LastGroundedOnRoof = true;
        LastContactName = roofSlideCollider.name;
        LastContactColliderName = roofSlideCollider.name;
        if (!_landingCounted)
        {
            _landingCounted = true;
            _landingCount++;
            AddRoofLoad(addPerLanding);
        }

        string parentName = transform.parent != null ? transform.parent.name : "None";
        Debug.Log($"[SlideFreeze] applied=true pos={transform.position} rbKin={rb.isKinematic} grav={rb.useGravity} vel={rb.linearVelocity} ang={rb.angularVelocity} parent={parentName}");
        LogContactState("enter", roofUp);

        if (enableSnowCreep && !_creepStarted)
        {
            _creepStarted = true;
            StartCoroutine(CreepThenFreeze());
        }
    }

    IEnumerator CreepThenFreeze()
    {
        if (roofSlideCollider == null || rb == null) yield break;
        _isCreeping = true;

        Vector3 roofUpStart = _lastContactNormal.sqrMagnitude > 0.0001f
            ? _lastContactNormal
            : roofSlideCollider.transform.up.normalized;
        Vector3 slopeDirStart = Vector3.ProjectOnPlane(Vector3.down, roofUpStart).normalized;
        if (slopeDirStart.sqrMagnitude < 0.0001f)
            slopeDirStart = Vector3.ProjectOnPlane(-roofSlideCollider.transform.forward, roofUpStart).normalized;
        Debug.Log($"[SlideCreep] start pos={transform.position} slopeDir={slopeDirStart} dist={creepDistance:F3} speed={creepSpeed:F3}");

        yield return new WaitForSecondsRealtime(creepDelaySeconds);

        float moved = 0f;
        while (moved < creepDistance)
        {
            if (roofSlideCollider == null || rb == null) break;

            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f)
            {
                yield return null;
                continue;
            }

            Vector3 roofUp = _lastContactNormal.sqrMagnitude > 0.0001f
                ? _lastContactNormal
                : roofSlideCollider.transform.up.normalized;
            Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
            if (slopeDir.sqrMagnitude < 0.0001f)
                slopeDir = Vector3.ProjectOnPlane(-roofSlideCollider.transform.forward, roofUp).normalized;

            float step = Mathf.Min(creepSpeed * dt, creepDistance - moved);
            Vector3 nextPos = transform.position + slopeDir * step;
            Vector3 snap = roofSlideCollider.ClosestPoint(nextPos) + roofUp * creepSnapOffset;
            transform.position = snap;
            rb.position = snap;
            moved += step;
            yield return null;
        }

        rb.useGravity = false;
        rb.isKinematic = true;
        LastGroundedOnRoof = true;
        LastContactName = roofSlideCollider != null ? roofSlideCollider.name : "None";
        LastContactColliderName = LastContactName;
        Debug.Log($"[SlideCreep] end moved={moved:F3} pos={transform.position}");
        _isCreeping = false;
    }

    void FixedUpdate()
    {
        if (rb == null) return;

        if (roofSlideCollider != null)
        {
            Vector3 cp = roofSlideCollider.ClosestPoint(transform.position);
            LastOffDist = Vector3.Distance(transform.position, cp);
        }

        if (_freezeApplied)
        {
            LastGroundedOnRoof = true;
            if (roofSlideCollider != null)
            {
                LastContactName = roofSlideCollider.name;
                LastContactColliderName = roofSlideCollider.name;
                EnforceLockedConfigIfNeeded();
                LogSnowLoopConfigOnce();
                TryAutoAvalancheBurst();
            }
        }

        EnsureDistanceRingCapacity();
        Vector3 curPos = rb.position;
        float delta = _prevPosValid ? (curPos - _prevPos).magnitude : 0f;
        LastDeltaPosThisFixed = delta;
        if (delta > 0.00001f) LastMovedFrame = Time.frameCount;
        if (_distanceRingCapacity > 0)
        {
            float old = _distanceRing[_distanceRingIndex];
            _distanceRing[_distanceRingIndex] = delta;
            _distanceRingIndex = (_distanceRingIndex + 1) % _distanceRingCapacity;
            _distanceRingSum += delta - old;
            MovedDistanceLast1s = _distanceRingSum;
        }
        _prevPos = curPos;
        _prevPosValid = true;

        UpdateDebugVisuals();
    }

    void TryAutoAvalancheBurst()
    {
        if (_inBurst || _isCreeping || !_freezeApplied || roofSlideCollider == null || rb == null) return;
        int key = roofSlideCollider.GetInstanceID();
        float load = RoofLoad.TryGetValue(key, out float v) ? v : 0f;
        Vector3 roofUp = _lastContactNormal.sqrMagnitude > 0.0001f
            ? _lastContactNormal
            : roofSlideCollider.transform.up.normalized;
        float slope = 1f - Mathf.Clamp01(Vector3.Dot(roofUp, Vector3.up));
        float angleDeg = Vector3.Angle(roofUp, Vector3.up);
        float threshold = Mathf.Clamp01(baseThreshold - slopeFactor * slope);
        if (Time.time >= _nextSnowLoopLogTime)
        {
            _nextSnowLoopLogTime = Time.time + 0.25f;
            Debug.Log($"[SnowLoop] load={load:F2} threshold={threshold:F2} slope={slope:F2} angleDeg={angleDeg:F1} landingCount={_landingCount} addPerLanding={addPerLanding:F2} nextSpawnIn={_nextSpawnInDebug:F2} state=Freeze");
        }
        bool firedByForce = forceAvalancheNow;
        bool shouldFire = firedByForce || (load >= threshold);
        if (!shouldFire) return;

        float before = load;
        float after = Mathf.Clamp01(load - loadDropOnBurst);
        RoofLoad[key] = after;
        Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (slopeDir.sqrMagnitude < 0.0001f)
            slopeDir = Vector3.ProjectOnPlane(-roofSlideCollider.transform.forward, roofUp).normalized;

        _inBurst = true;
        _freezeApplied = false;
        _isCreeping = false;
        _creepStarted = false;
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.linearVelocity = slopeDir * burstSpeed;
        rb.angularVelocity = Vector3.zero;
        rb.WakeUp();
        Bounds b = roofSlideCollider.bounds;
        Vector3 p = transform.position;
        bool outOfBounds = p.x < b.min.x || p.x > b.max.x || p.y < b.min.y || p.y > b.max.y || p.z < b.min.z || p.z > b.max.z;
        bool contactOnRoof = LastGroundedOnRoof;
        bool sleeping = rb.IsSleeping();
        forceAvalancheNow = false;
        Debug.Log($"[AvalancheAuto] fired forced={firedByForce} loadBefore={before:F2} loadAfter={after:F2} burstSpeed={burstSpeed:F2} cooldown={avalancheCooldownSeconds:F2} burstVel={rb.linearVelocity} rbKin={rb.isKinematic} grav={rb.useGravity} sleeping={sleeping} contactOnRoof={contactOnRoof} outOfBounds={outOfBounds}");
        StartCoroutine(AvalancheCooldownRoutine());
    }

    IEnumerator AvalancheCooldownRoutine()
    {
        _inAvalancheCooldown = true;
        Debug.Log($"[AvalancheState] enter cooldown={avalancheCooldownSeconds:F2}");
        yield return new WaitForSecondsRealtime(Mathf.Max(0.01f, avalancheCooldownSeconds));
        _inAvalancheCooldown = false;
        Debug.Log("[AvalancheState] exit");

        if (rb == null) yield break;
        if (roofSlideCollider == null)
        {
            _inBurst = false;
            yield break;
        }

        Vector3 cp = roofSlideCollider.ClosestPoint(transform.position);
        float offDist = Vector3.Distance(transform.position, cp);
        if (offDist <= 0.25f)
        {
            _inBurst = false;
            ApplyFreeze();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void AddRoofLoad(float delta)
    {
        if (roofSlideCollider == null) return;
        int key = roofSlideCollider.GetInstanceID();
        float load = RoofLoad.TryGetValue(key, out float v) ? v : 0f;
        RoofLoad[key] = Mathf.Clamp01(load + delta);
    }

    void LogSnowLoopConfigOnce()
    {
        if (_configLogged || roofSlideCollider == null) return;
        Vector3 roofUp = _lastContactNormal.sqrMagnitude > 0.0001f
            ? _lastContactNormal
            : roofSlideCollider.transform.up.normalized;
        float slope = 1f - Mathf.Clamp01(Vector3.Dot(roofUp, Vector3.up));
        float angleDeg = Vector3.Angle(roofUp, Vector3.up);
        float computedThreshold = Mathf.Clamp01(baseThreshold - slopeFactor * slope);
        Debug.Log($"[SnowLoopConfig] baseThreshold={baseThreshold:F2} slopeFactor={slopeFactor:F2} angleDeg={angleDeg:F1} computedThreshold={computedThreshold:F2}");
        _configLogged = true;
    }

    void EnforceLockedConfigIfNeeded()
    {
        if (!_hasLockedConfig) return;
        if (Mathf.Approximately(addPerLanding, _lockedAddPerLanding)
            && Mathf.Approximately(baseThreshold, _lockedBaseThreshold)
            && Mathf.Approximately(slopeFactor, _lockedSlopeFactor)
            && Mathf.Approximately(burstSpeed, _lockedBurstSpeed)
            && Mathf.Approximately(stickKick, _lockedStickKick)
            && Mathf.Approximately(burstDuration, _lockedBurstDuration)
            && Mathf.Approximately(loadDropOnBurst, _lockedLoadDropOnBurst))
            return;

        MaybeLogOverride("RuntimeExternal", "addPerLanding", addPerLanding, _lockedAddPerLanding);
        MaybeLogOverride("RuntimeExternal", "baseThreshold", baseThreshold, _lockedBaseThreshold);
        MaybeLogOverride("RuntimeExternal", "slopeFactor", slopeFactor, _lockedSlopeFactor);
        MaybeLogOverride("RuntimeExternal", "burstSpeed", burstSpeed, _lockedBurstSpeed);
        MaybeLogOverride("RuntimeExternal", "stickKick", stickKick, _lockedStickKick);
        MaybeLogOverride("RuntimeExternal", "burstDuration", burstDuration, _lockedBurstDuration);
        MaybeLogOverride("RuntimeExternal", "loadDropOnBurst", loadDropOnBurst, _lockedLoadDropOnBurst);

        addPerLanding = _lockedAddPerLanding;
        baseThreshold = _lockedBaseThreshold;
        slopeFactor = _lockedSlopeFactor;
        burstSpeed = _lockedBurstSpeed;
        stickKick = _lockedStickKick;
        burstDuration = _lockedBurstDuration;
        loadDropOnBurst = _lockedLoadDropOnBurst;
    }

    void MaybeLogOverride(string source, string field, float oldValue, float newValue)
    {
        if (Mathf.Approximately(oldValue, newValue)) return;
        string writerName = gameObject != null ? gameObject.name : "None";
        int writerId = gameObject != null ? gameObject.GetInstanceID() : 0;
        string scriptName = GetType().Name;
        string caller = GetExternalCallerInfo();
        Debug.Log($"[SnowLoopOverride] source={source} writer={writerName} writerId={writerId} script={scriptName} field={field} old={oldValue:F2} new={newValue:F2} caller={caller}");
    }

    string GetExternalCallerInfo()
    {
        var st = new System.Diagnostics.StackTrace(true);
        for (int i = 0; i < st.FrameCount; i++)
        {
            var frame = st.GetFrame(i);
            if (frame == null) continue;
            var method = frame.GetMethod();
            if (method == null) continue;
            var type = method.DeclaringType;
            if (type == typeof(SnowTestSlideAssist)) continue;
            string typeName = type != null ? type.Name : "UnknownType";
            string methodName = method.Name;
            int line = frame.GetFileLineNumber();
            if (line > 0)
                return $"{typeName}.{methodName}:{line}";
            return $"{typeName}.{methodName}";
        }
        return "UnknownCaller";
    }

    void EnsureDistanceRingCapacity()
    {
        float dt = Mathf.Max(0.0001f, Time.fixedDeltaTime);
        int need = Mathf.Max(1, Mathf.CeilToInt(1f / dt));
        if (_distanceRing != null && _distanceRingCapacity == need) return;
        _distanceRingCapacity = need;
        _distanceRing = new float[_distanceRingCapacity];
        _distanceRingIndex = 0;
        _distanceRingSum = 0f;
        MovedDistanceLast1s = 0f;
    }

    void EnsureDebugVisuals()
    {
        if (!enableDebugVisuals || !Application.isPlaying) return;
        if (_debugLine == null)
        {
            _debugLine = GetComponent<LineRenderer>();
            if (_debugLine == null) _debugLine = gameObject.AddComponent<LineRenderer>();
            _debugLine.positionCount = 2;
            _debugLine.widthMultiplier = 0.03f;
            _debugLine.useWorldSpace = true;
            var shader = Shader.Find("Sprites/Default");
            if (shader != null) _debugLine.material = new Material(shader) { color = Color.cyan };
        }
        if (_debugText == null)
        {
            var t = transform.Find("RoofSlideDebugText");
            if (t == null)
            {
                var go = new GameObject("RoofSlideDebugText");
                go.transform.SetParent(transform, false);
                t = go.transform;
            }
            _debugText = t.GetComponent<TextMesh>();
            if (_debugText == null) _debugText = t.gameObject.AddComponent<TextMesh>();
            _debugText.fontSize = 48;
            _debugText.characterSize = 0.03f;
            _debugText.anchor = TextAnchor.MiddleCenter;
            _debugText.alignment = TextAlignment.Center;
            _debugText.color = Color.white;
        }
    }

    void UpdateDebugVisuals()
    {
        if (!enableDebugVisuals || !Application.isPlaying || roofSlideCollider == null) return;
        EnsureDebugVisuals();
        Vector3 roofUp = roofSlideCollider.transform.up.normalized;
        Vector3 slopeDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (_debugLine != null)
        {
            Vector3 p0 = transform.position + Vector3.up * 0.08f;
            _debugLine.SetPosition(0, p0);
            _debugLine.SetPosition(1, p0 + slopeDir);
        }
        if (_debugText != null)
        {
            _debugText.transform.position = transform.position + Vector3.up * 0.35f;
            _debugText.text = $"{LastContactColliderName}\n{SlideMode}";
            if (Camera.main != null)
                _debugText.transform.rotation = Quaternion.LookRotation(_debugText.transform.position - Camera.main.transform.position);
        }
    }

    void LogContactState(string phase, Vector3 roofUp)
    {
        if (rb == null) return;
        Vector3 v = rb.linearVelocity;
        float normalSpeed = Vector3.Dot(v, roofUp);
        float planeSpeed = Vector3.ProjectOnPlane(v, roofUp).magnitude;
        Debug.Log($"[SlideProtoContact] phase={phase} contactName={LastContactName} contactNormal={roofUp} normalSpeed={normalSpeed:F3} planeSpeed={planeSpeed:F3}");
    }

    bool IsGroundCollision(Collider c)
    {
        if (c == null) return false;
        if (roofSlideCollider != null && c == roofSlideCollider) return false;
        string n = c.name;
        return n.Contains("Ground") || n.Contains("Plane") || n.Contains("Porch") || transform.position.y < 0.8f;
    }

    void ConvertToGroundDeposit(Vector3 worldPos)
    {
        if (_groundDeposited) return;
        _groundDeposited = true;
        if (GroundSnowAccumulator.Instance != null)
            GroundSnowAccumulator.Instance.AddSnow(worldPos, groundDepositAmount);
        if (rb != null)
        {
            if (!rb.isKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            rb.useGravity = false;
            rb.isKinematic = true;
        }
        var c = GetComponent<Collider>();
        if (c != null) c.enabled = false;
        Destroy(gameObject, 0.02f);
    }
}

