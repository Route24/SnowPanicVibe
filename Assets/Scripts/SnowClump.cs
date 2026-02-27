using UnityEngine;
using System.Collections.Generic;

/// <summary>落下する雪の塊。落ちながら変形し、地面に当たると広がって積もり消える</summary>
public class SnowClump : MonoBehaviour
{
    [HideInInspector] public Vector3 slideDownDirection;

    Rigidbody _rb;
    bool _landed;
    float _landTime;
    List<Transform> _particles = new List<Transform>();
    List<Vector3> _baseLocalPos = new List<Vector3>();
    List<Renderer> _renderers = new List<Renderer>();
    List<Color> _baseColors = new List<Color>();
    float _spawnTime;
    const float SpreadDuration = 0.5f;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        _spawnTime = Time.time;
        if (slideDownDirection.sqrMagnitude > 0.01f)
        {
            _rb.linearVelocity = slideDownDirection.normalized * 0.25f;
        }
        for (int i = 0; i < transform.childCount; i++)
        {
            var t = transform.GetChild(i);
            _particles.Add(t);
            _baseLocalPos.Add(t.localPosition);
            var r = t.GetComponent<Renderer>();
            _renderers.Add(r);
            _baseColors.Add(r != null ? r.material.color : Color.white);
        }
    }

    void LateUpdate()
    {
        if (_particles.Count == 0) return;

        if (_landed)
        {
            float elapsed = Time.time - _landTime;
            float t = Mathf.Clamp01(elapsed / SpreadDuration);
            float spread = Mathf.SmoothStep(0f, 1f, t);
            float squash = Mathf.SmoothStep(0f, 0.7f, t);
            float alpha = Mathf.SmoothStep(1f, 0f, t);

            for (int i = 0; i < _particles.Count; i++)
            {
                Vector3 p = _baseLocalPos[i];
                p.x *= 1f + spread * 1.2f;
                p.z *= 1f + spread * 1.2f;
                p.y *= 1f - squash;
                _particles[i].localPosition = p;

                if (_renderers[i] != null)
                {
                    var c = _baseColors[i];
                    c.a = alpha;
                    _renderers[i].material.color = c;
                }
            }

            if (t >= 1f)
            {
                EmitToGroundSnow();
                Destroy(gameObject);
            }
            return;
        }

        float time = Time.time - _spawnTime;
        Vector3 vel = _rb.linearVelocity;
        float speed = vel.magnitude;
        float deformStrength = 0.03f + speed * 0.08f;
        Vector3 velDir = speed > 0.01f ? vel.normalized : Vector3.down;
        for (int i = 0; i < _particles.Count; i++)
        {
            float n0 = Mathf.PerlinNoise(time * 1.2f + i * 0.1f, 0f);
            float n1 = Mathf.PerlinNoise(0f, time * 1.5f + i * 0.1f);
            float n2 = Mathf.PerlinNoise(time * 0.9f + i * 0.07f, time * 0.7f);
            Vector3 noise = new Vector3((n0 - 0.5f) * 2f, (n1 - 0.5f) * 2f, (n2 - 0.5f) * 2f);
            Vector3 stretch = velDir * (n0 - 0.3f) * deformStrength;
            Vector3 offset = noise * deformStrength + stretch;
            _particles[i].localPosition = _baseLocalPos[i] + offset;
        }
    }

    void OnCollisionEnter(Collision col)
    {
        if (_landed) return;
        var n = col.transform.name;
        if (n.Contains("Roof")) return;
        if (n.Contains("Ground") || n.Contains("Plane") || transform.position.y < 0.6f)
        {
            _landed = true;
            _landTime = Time.time;
            for (int i = 0; i < _particles.Count; i++)
                _baseLocalPos[i] = _particles[i].localPosition;
            _rb.isKinematic = true;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }
    }

    void EmitToGroundSnow()
    {
        var go = GameObject.Find("GroundSnow");
        if (go == null) return;
        var ps = go.GetComponent<ParticleSystem>();
        if (ps == null) return;
        Vector3 pos = transform.position;
        pos.y = 0.15f;
        int count = Mathf.Min(_particles.Count, 60);
        var ep = new ParticleSystem.EmitParams();
        for (int i = 0; i < count; i++)
        {
            ep.position = pos + new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));
            ep.velocity = Vector3.zero;
            ep.startSize = Random.Range(0.03f, 0.06f);
            ep.startLifetime = 9999f;
            ep.startColor = new Color(0.82f, 0.85f, 0.9f, 0.9f);
            ps.Emit(ep, 1);
        }
    }
}
