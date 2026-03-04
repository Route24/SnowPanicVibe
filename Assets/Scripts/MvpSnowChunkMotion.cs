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
    public LayerMask groundMask = ~0;
    public float depositAmount = 0.02f;
    public GroundSnowSystem groundSnow;
    public Action<MvpSnowChunkMotion> onFinished;
    Vector3 _roofNormal;
    float _roofSlideRemaining;

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
            if (mag > 0.0001f) velocity = velocity.normalized * mag;
            _roofSlideRemaining -= dt;
        }
        else
        {
            velocity += Vector3.down * gravity * dt;
        }
        Vector3 next = prev + velocity * dt;
        Vector3 dir = next - prev;
        float dist = dir.magnitude;
        if (dist > 0.0001f && Physics.Raycast(prev, dir / dist, out RaycastHit hit, dist, groundMask, QueryTriggerInteraction.Ignore))
        {
            if (groundSnow != null) groundSnow.AddSnow(depositAmount);
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
