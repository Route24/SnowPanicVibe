using System;
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

    public void Activate(Vector3 pos, Vector3 vel, float life, GroundSnowSystem ground, LayerMask mask, float deposit)
    {
        Activate(pos, vel, life, ground, mask, deposit, Vector3.zero, 0f);
    }

    /// <summary>屋根面に沿って滑る→軒越えで自由落下。roofN!=0かつ roofSlideTime>0 で有効。</summary>
    public void Activate(Vector3 pos, Vector3 vel, float life, GroundSnowSystem ground, LayerMask mask, float deposit, Vector3 roofN, float roofSlideTime)
    {
        transform.position = pos;
        velocity = vel;
        lifeRemaining = life;
        groundSnow = ground;
        groundMask = mask;
        depositAmount = deposit;
        _roofNormal = roofN.sqrMagnitude > 0.001f ? roofN.normalized : Vector3.zero;
        _roofSlideRemaining = Mathf.Max(0f, roofSlideTime);
        _activatedAt = Time.time;
        gameObject.SetActive(true);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 prev = transform.position;
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
        }
        else
        {
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
        lifeRemaining -= dt;
        if (lifeRemaining <= 0f)
            Finish();
    }

    void Finish()
    {
        gameObject.SetActive(false);
        onFinished?.Invoke(this);
    }
}
