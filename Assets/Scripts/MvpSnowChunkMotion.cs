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

    public void Activate(Vector3 pos, Vector3 vel, float life, GroundSnowSystem ground, LayerMask mask, float deposit)
    {
        transform.position = pos;
        velocity = vel;
        lifeRemaining = life;
        groundSnow = ground;
        groundMask = mask;
        depositAmount = deposit;
        gameObject.SetActive(true);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 prev = transform.position;
        velocity += Vector3.down * gravity * dt;
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
