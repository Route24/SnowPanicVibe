using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Tiny non-rigidbody chunk motion for avalanche visuals.
/// </summary>
public class MvpSnowChunkMotion : MonoBehaviour
{
    public Vector3 velocity;
    public float lifeRemaining;
    public float gravity = 9.81f;
    [Tooltip("Clamp horizontal slide speed on roof (slow, readable).")]
    public float maxSpeedOnRoof = 1.2f;
    [Tooltip("Velocity damp while on roof (creeps, not rockets).")]
    [Range(0f, 1f)] public float roofDrag = 0.6f;
    [Tooltip("Airborne gravity multiplier (higher = shorter arcs).")]
    public float airborneGravityScale = 1.6f;
    [Tooltip("Air drag when falling.")]
    [Range(0f, 0.5f)] public float airborneDrag = 0.2f;
    public LayerMask groundMask = ~0;
    public float depositAmount = 0.02f;
    public GroundSnowSystem groundSnow;
    public Action<MvpSnowChunkMotion> onFinished;
    Vector3 _roofNormal;
    float _roofSlideRemaining;
    float _activatedAt;
    [Tooltip("Min seconds before ground hit is allowed (prevents immediate despawn from roof).")]
    public float minLifetimeBeforeGroundCheck = 0.8f;
    [Header("Roof debris auto-despawn")]
    [Tooltip("Speed below this = stuck on roof (velocity<0.05 が1秒で強制消去).")]
    public float roofStuckSpeedThreshold = 0.05f;
    [Tooltip("Position delta below this = not moving.")]
    public float roofStuckMoveThreshold = 0.002f;
    [Tooltip("Seconds of low speed OR low move on roof before despawn. 止まり雪対策で3秒（再タップ猶予）。")]
    public float roofStuckDespawnSeconds = 3f;
    [Tooltip("強制TTL: 生成時に dieAt=Time.time+この値 をセット。100%消える保険。")]
    public float forceTtlSeconds = 6f;
    [Tooltip("Downhill impulse when tap triggers unstick.")]
    public float unstickImpulseMagnitude = 1.5f;
    float _roofIdleTimer;
    bool _roofIdleLogged;
    bool _despawning;
    Vector3 _prevPos;
    float _dieAt;

    public void Activate(Vector3 pos, Vector3 vel, float life, GroundSnowSystem ground, LayerMask mask, float deposit)
    {
        Activate(pos, vel, life, ground, mask, deposit, Vector3.zero, 0f);
    }

    /// <summary>屋根面に沿って滑る→軒越えで自由落下。roofN!=0かつ roofSlideTime>0 で有効。</summary>
    public void Activate(Vector3 pos, Vector3 vel, float life, GroundSnowSystem ground, LayerMask mask, float deposit, Vector3 roofN, float roofSlideTime)
    {
        _despawning = false;
        transform.position = pos;
        velocity = vel;
        lifeRemaining = life;
        groundSnow = ground;
        groundMask = mask;
        depositAmount = deposit;
        _roofNormal = roofN.sqrMagnitude > 0.001f ? roofN.normalized : Vector3.zero;
        _roofSlideRemaining = Mathf.Max(0f, roofSlideTime);
        _activatedAt = Time.time;
        _dieAt = Time.time + Mathf.Clamp(forceTtlSeconds, 4f, 8f);
        _roofIdleTimer = 0f;
        _roofIdleLogged = false;
        _prevPos = pos;
        // プール再利用時も必ず大きめスケールを適用（雪塊として見えるサイズ）
        float cs = UnityEngine.Random.Range(0.32f, 0.52f);
        transform.localScale = new Vector3(cs, cs * 0.65f, cs);
        var col = GetComponent<Collider>();
        if (col != null) { col.isTrigger = false; col.enabled = true; }
        gameObject.SetActive(true);
        DetachedSnowRegistry.RegisterChunk(this);
        DetachedSnowDiagnostics.LogChunkInfoIfFirst(this);
        LogDetachedSpawn(vel);
    }

    /// <summary>Called when user taps this chunk. Applies downhill impulse to unstick.</summary>
    public void ApplyTapImpulse(Vector3 roofNormal)
    {
        Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, roofNormal).normalized;
        if (downhill.sqrMagnitude < 0.01f) downhill = -roofNormal;
        velocity = downhill * Mathf.Max(unstickImpulseMagnitude, velocity.magnitude + 0.5f);
        _roofIdleTimer = 0f;
        if (_roofSlideRemaining <= 0f) _roofSlideRemaining = 0.2f;
        UnityEngine.Debug.Log($"[ChunkTap] impulse applied pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2})");
    }

    IEnumerator FadeOutThenDespawn()
    {
        _despawning = true;
        float duration = 0.2f;
        Vector3 startScale = transform.localScale;
        for (float t = 0f; t < duration; t += Time.deltaTime)
        {
            float a = 1f - (t / duration);
            transform.localScale = startScale * a;
            yield return null;
        }
        LogDetachedStop("stuck");
        LogDetachedForceDespawn("stopped");
        Finish();
    }

    void Update()
    {
        if (_despawning) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 prev = transform.position;

        if (Time.time > _dieAt)
        {
            LogDetachedForceDespawn("TTL");
            Finish();
            return;
        }

        if (_roofSlideRemaining > 0f && _roofNormal.sqrMagnitude > 0.001f)
        {
            velocity = Vector3.ProjectOnPlane(velocity, _roofNormal);
            float mag = velocity.magnitude;
            if (mag > 0.0001f)
            {
                velocity = velocity.normalized * Mathf.Min(mag, maxSpeedOnRoof);
            }
            velocity *= Mathf.Max(0.7f, 1f - roofDrag * dt * 8f);
            _roofSlideRemaining -= dt;

            float speed = velocity.magnitude;
            float moved = (prev - _prevPos).magnitude;
            bool stuck = speed < roofStuckSpeedThreshold || moved < roofStuckMoveThreshold;
            if (stuck)
            {
                _roofIdleTimer += dt;
                if (!_roofIdleLogged && _roofIdleTimer >= 0.5f)
                {
                    _roofIdleLogged = true;
                    var col = GetComponent<Collider>();
                    UnityEngine.Debug.Log($"[StoppedSnow] class=Chunk pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({velocity.x:F2},{velocity.y:F2},{velocity.z:F2}) speed={speed:F3} moved={moved:F4} layer={UnityEngine.LayerMask.LayerToName(gameObject.layer)} colEnabled={(col != null && col.enabled)} timer={_roofIdleTimer:F2}s");
                }
                if (_roofIdleTimer >= roofStuckDespawnSeconds)
                {
                    StartCoroutine(FadeOutThenDespawn());
                    return;
                }
            }
            else
            {
                _roofIdleTimer = 0f;
                _roofIdleLogged = false;
            }
        }
        else
        {
            _roofIdleTimer = 0f;
            _roofIdleLogged = false;
            velocity += Vector3.down * (gravity * airborneGravityScale) * dt;
            velocity *= Mathf.Max(0.97f, 1f - airborneDrag * dt * 4f);
        }
        Vector3 next = prev + velocity * dt;
        Vector3 dir = next - prev;
        float dist = dir.magnitude;
        bool allowGroundCheck = (Time.time - _activatedAt) >= minLifetimeBeforeGroundCheck;
        if (allowGroundCheck && dist > 0.0001f && Physics.Raycast(prev, dir / dist, out RaycastHit hit, dist, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (groundSnow != null && hit.collider != null) groundSnow.SpawnPileAt(hit.point, depositAmount);
            CoreGameplayManager.Instance?.AddMoneyFromChunkLanding(depositAmount);
            Finish();
            return;
        }

        transform.position = next;
        _prevPos = prev;
        lifeRemaining -= dt;
        if (lifeRemaining <= 0f)
            Finish();
    }

    void Finish()
    {
        DetachedSnowRegistry.UnregisterChunk(this);
        gameObject.SetActive(false);
        onFinished?.Invoke(this);
    }

    void LogDetachedSpawn(Vector3 vel)
    {
        var col = GetComponent<Collider>();
        UnityEngine.Debug.Log($"[DetachedSpawn] class=chunk pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({vel.x:F2},{vel.y:F2},{vel.z:F2}) layer={UnityEngine.LayerMask.LayerToName(gameObject.layer)} col.enabled={(col != null && col.enabled)}");
    }

    void LogDetachedStop(string reason)
    {
        var col = GetComponent<Collider>();
        float downhillDot = _roofNormal.sqrMagnitude > 0.001f
            ? Vector3.Dot(velocity, Vector3.ProjectOnPlane(Vector3.down, _roofNormal).normalized)
            : 0f;
        UnityEngine.Debug.Log($"[DetachedStop] pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) velocity=({velocity.x:F2},{velocity.y:F2},{velocity.z:F2}) isSleeping=N/A layer={UnityEngine.LayerMask.LayerToName(gameObject.layer)} collider.enabled={(col != null && col.enabled)}");
        string state = velocity.magnitude < 0.05f ? "logic_stopped" : "moving";
        UnityEngine.Debug.Log($"[StopReason] velocity=({velocity.x:F2},{velocity.y:F2},{velocity.z:F2}) downhillDot={downhillDot:F3} state={state}");
    }

    void LogDetachedForceDespawn(string reason)
    {
        UnityEngine.Debug.Log($"[DetachedForceDespawn] pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) reason={reason}");
    }

    /// <summary>外部から強制Despawn（packed=0一括掃除用）。</summary>
    public void ForceDespawn()
    {
        if (_despawning) return;
        _despawning = true;
        LogDetachedForceDespawn("Sweep");
        Finish();
    }
}
