using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>落下する雪の塊。落ちながら変形し、地面に当たると広がって積もり消える</summary>
public class SnowClump : MonoBehaviour
{
    [HideInInspector] public Vector3 slideDownDirection;
    [HideInInspector] public float initialSlideSpeed = 0.25f;
    [HideInInspector] public Collider roofColliderToIgnoreWhenStuck;

    Rigidbody _rb;
    Vector3 _spawnPos;
    bool _landed;
    float _landTime;
    float _stuckOnRoofTime;
    List<Transform> _particles = new List<Transform>();
    List<Vector3> _baseLocalPos = new List<Vector3>();
    List<Renderer> _renderers = new List<Renderer>();
    List<Color> _baseColors = new List<Color>();
    MaterialPropertyBlock _propBlock;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    float _spawnTime;
    const float SpreadDuration = 0.5f;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        _spawnTime = Time.time;
        _spawnPos = transform.position;
        var myCol = GetComponent<Collider>();
        if (myCol != null)
        {
            foreach (var other in FindObjectsByType<SnowClump>(FindObjectsSortMode.None))
            {
                if (other == this) continue;
                var otherCol = other.GetComponent<Collider>();
                if (otherCol != null) Physics.IgnoreCollision(myCol, otherCol);
            }
        }
        if (slideDownDirection.sqrMagnitude > 0.01f)
        {
            _rb.linearVelocity = slideDownDirection.normalized * initialSlideSpeed;
        }
        StartCoroutine(EnsureFall());
        _propBlock = new MaterialPropertyBlock();
        for (int i = 0; i < transform.childCount; i++)
        {
            var t = transform.GetChild(i);
            _particles.Add(t);
            _baseLocalPos.Add(t.localPosition);
            var r = t.GetComponent<Renderer>();
            _renderers.Add(r);
            _baseColors.Add(r != null ? r.sharedMaterial.color : Color.white);
        }
    }

    void LateUpdate()
    {
        if (_landed)
        {
            if (_particles.Count == 0)
            {
                EmitToGroundSnow();
                Destroy(gameObject);
                return;
            }
            // 着地後は即静止。短いフェードアウト後に消す
            float elapsed = Time.time - _landTime;
            float t = Mathf.Clamp01(elapsed / 0.15f);
            float alpha = Mathf.SmoothStep(1f, 0f, t);

            for (int i = 0; i < _particles.Count; i++)
            {
                _particles[i].localPosition = _baseLocalPos[i]; // 動かさない

                if (_renderers[i] != null)
                {
                    var c = _baseColors[i];
                    c.a = alpha;
                    _propBlock.Clear();
                    _propBlock.SetColor(ColorId, c);
                    _propBlock.SetColor(BaseColorId, c);
                    _renderers[i].SetPropertyBlock(_propBlock);
                }
            }

            if (t >= 1f)
            {
                EmitToGroundSnow();
                Destroy(gameObject);
            }
            return;
        }

        if (_particles.Count == 0) return;

        // 滑っている時だけ変形（落下中のみ）
        float speed = _rb != null ? _rb.linearVelocity.magnitude : 0f;
        if (speed < 0.02f) return; // ほぼ静止していたら動かさない

        float time = Time.time - _spawnTime;
        Vector3 velDir = speed > 0.01f ? _rb.linearVelocity.normalized : Vector3.down;
        float deformStrength = 0.03f + speed * 0.08f;
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

    /// <summary>クリックで即削除（軒先の残雪用）</summary>
    public void RemoveImmediate()
    {
        EmitToGroundSnow();
        Destroy(gameObject);
    }

    IEnumerator EnsureFall()
    {
        yield return new WaitForSeconds(0.25f);
        if (_landed) yield break;
        float dist = Vector3.Distance(transform.position, _spawnPos);
        if (dist < 0.15f && _rb != null && slideDownDirection.sqrMagnitude > 0.01f)
        {
            _rb.linearVelocity = slideDownDirection.normalized * (initialSlideSpeed + 0.15f);
        }
    }

    void OnCollisionEnter(Collision col)
    {
        if (_landed) return;
        var n = col.transform.name;
        if (n.Contains("Roof")) return;
        if (n.Contains("HouseBody")) return; // 家の壁に当たっても落ちるまで待つ
        if (n.Contains("Ground") || n.Contains("Plane") || n.Contains("Porch") ||
            n.Contains("Rock") || n.Contains("Grass") || transform.position.y < 0.5f)
        {
            LandNow();
        }
    }

    void Update()
    {
        if (_landed) return;
        if (_rb == null) return;

        float speedSq = _rb.linearVelocity.sqrMagnitude;

        // 地面付近（軒先より下）で止まった雪だけ着地扱いにする
        if (transform.position.y < 0.5f && speedSq < 0.0025f)
        {
            LandNow();
            return;
        }

        // 屋根・軒先で止まったら即押し出して地面まで落とす
        if (transform.position.y > 0.5f && transform.position.y < 2.8f && slideDownDirection.sqrMagnitude > 0.01f)
        {
            if (speedSq < 0.0025f) // 速度 < 0.05 で止まり気味
                _stuckOnRoofTime += Time.deltaTime;
            else
                _stuckOnRoofTime = 0f;

            if (_stuckOnRoofTime > 0.06f) // 短くして素早く反応
            {
                _rb.AddForce((slideDownDirection.normalized + Vector3.down * 0.8f) * 5f);
                _stuckOnRoofTime = 0f;
                var myCol = GetComponent<Collider>();
                var roofCol = roofColliderToIgnoreWhenStuck;
                if (roofCol == null)
                {
                    var rp = GameObject.Find("RoofPanel");
                    if (rp != null) roofCol = rp.GetComponent<Collider>();
                }
                if (myCol != null && roofCol != null)
                    Physics.IgnoreCollision(myCol, roofCol);
            }
        }
    }

    void LandNow()
    {
        if (_landed) return;
        _landed = true;
        _landTime = Time.time;
        for (int i = 0; i < _particles.Count; i++)
            _baseLocalPos[i] = _particles[i].localPosition;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.isKinematic = true;
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
