using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>落下する雪の塊。落ちながら変形し、地面に当たると広がって積もり消える</summary>
public class SnowClump : MonoBehaviour
{
    [HideInInspector] public Vector3 slideDownDirection;
    [HideInInspector] public float initialSlideSpeed = 0.42f;  // ASSI: 屋根沿い滑落 - 初期滑りを強め (0.25->0.42)
    [HideInInspector] public Collider roofColliderToIgnoreWhenStuck;
    [HideInInspector] public Collider roofSurfaceCollider;
    [HideInInspector] public RoofSnow ownerRoofSnow;
    [HideInInspector] public bool debugMode;

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
    float _pressureDetachCooldown;
    float _roofRestTime;
    bool _hasDropped;
    bool _hasDeposited;
    bool _hasBeginSlide;
    float _fallStartTime;
    float _groundedStartTime = -1f;
    float _edgeStickTime;
    float _offDistGraceTimer;
    float _roofSlideStartTime;
    [HideInInspector] public float consumeOnDepositAmount = 0.02f;
    [HideInInspector] public float groundDepositAmount = 0.10f;
    [SerializeField] LayerMask groundMask = ~0;
    enum ClumpState { RoofSliding, Falling, Grounded, Despawning }
    ClumpState _state;
    const float GroundPileWaitSeconds = 4.0f;
    const float GroundPileBlinkDuration = 1.0f;
    const float GroundPileBlinkInterval = 0.1f;
    const float OffDistDropThreshold = 0.28f; // ASSI: 屋根沿い滑落 - 落下判定の猶予を拡大 (0.20->0.28)

    /// <summary>ASSI Fix Pack: 屋根沿い滑落優先。軒先落下時チューニング。</summary>
    public static float maxFallCarrySpeed = 1.5f;   // 滑落方向の初速を強めに持ち越す (1.2->1.5)
    public static float sideDamp = 0.25f;           // 横方向は少し残す (0.2->0.25)
    public static float dropImpulse = 0.35f;        // 真下成分を弱め、屋根傾斜方向を優先 (0.6->0.35)
    public static float airDrag = 1.0f;             // 空気抵抗 (1.2->1.0)
    const float OffDistGraceDuration = 0.4f;       // ASSI: 接触安定の猶予延長 (0.25->0.4)
    const float NearEdgeMargin = 0.02f;
    const float NearEdgeDropDelay = 0.22f; // 0.35 -> 0.22
    const float DebugMinRoofSlideTime = 0.7f;
    const float GroundRayDistance = 0.25f;

    static int s_spawnPerSec;
    static int s_beginSlidePerSec;
    static int s_dropPerSec;
    static int s_depositPerSec;
    static int s_forcedDropPerSec;
    static int s_forcedDepositPerSec;
    static float s_windowStart = -1f;

    public const int MaxActiveSnowPieces = 400;
    public const int MaxActiveDynamicPieces = 250;
    static readonly List<SnowClump> s_allActive = new List<SnowClump>();

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        _spawnTime = Time.time;
        _spawnPos = transform.position;
        _state = ClumpState.RoofSliding;
        if (_rb != null)
        {
            _rb.useGravity = true;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.constraints = RigidbodyConstraints.FreezeRotation;
        }
        _roofSlideStartTime = Time.time;
        StartCoroutine(BeginSlideImpulseNextFrame());
        _propBlock = new MaterialPropertyBlock();
        for (int i = 0; i < transform.childCount; i++)
        {
            var t = transform.GetChild(i);
            _particles.Add(t);
            _baseLocalPos.Add(t.localPosition);
            var r = t.GetComponent<Renderer>();
            _renderers.Add(r);
            _baseColors.Add(r != null ? MaterialColorHelper.GetColorSafe(r.sharedMaterial, Color.white) : Color.white);
        }
        s_allActive.Add(this);
    }

    void LateUpdate()
    {
        if (_landed)
        {
            if (_state == ClumpState.Grounded || _state == ClumpState.Despawning) return;
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

    void FixedUpdate()
    {
        if (_rb == null) return;
        if (_state == ClumpState.Falling)
        {
            return;
        }
        if (_state != ClumpState.RoofSliding) return;
        if (_rb.isKinematic) return;
        if (roofSurfaceCollider == null) return;
        if (_offDistGraceTimer > 0f) _offDistGraceTimer -= Time.fixedDeltaTime;

        Vector3 normal = roofSurfaceCollider.transform.up.normalized;
        Vector3 closest = roofSurfaceCollider.ClosestPoint(_rb.position);
        float offDist = Vector3.Distance(_rb.position, closest);
        bool keepOnRoofByDebug = debugMode && (Time.time - _roofSlideStartTime) < DebugMinRoofSlideTime;

        if (keepOnRoofByDebug)
        {
            _rb.position = closest + normal * 0.02f;
            _rb.linearVelocity = Vector3.ProjectOnPlane(_rb.linearVelocity, normal);
            return;
        }

        // A) 屋根面に近い or 起動直後猶予中は拘束を適用
        bool nearEdge = IsNearColliderEdge(closest, roofSurfaceCollider.bounds, NearEdgeMargin * 3f);
        if (nearEdge && offDist > 0.08f)
        {
            BeginFall((GetRoofSlideDirection() * Mathf.Max(0.5f, initialSlideSpeed) + Vector3.down * 1.1f), "nearEavesEdge", closest, offDist);
            return;
        }
        if (offDist <= OffDistDropThreshold || _offDistGraceTimer > 0f)
        {
            _rb.position = closest + normal * 0.02f;
            _rb.linearVelocity = Vector3.ProjectOnPlane(_rb.linearVelocity, normal);

            // B-2) 縁で停滞したら強制落下
            float edgeSpeed = _rb.linearVelocity.magnitude;
            if (nearEdge && edgeSpeed < 0.15f)
            {
                _edgeStickTime += Time.fixedDeltaTime;
                if (_edgeStickTime > NearEdgeDropDelay)
                {
                    BeginFall((GetRoofSlideDirection() * Mathf.Max(0.5f, initialSlideSpeed) + Vector3.down * 1.1f), "nearEdge", closest, offDist);
                    return;
                }
            }
            else
            {
                _edgeStickTime = 0f;
            }
            return;
        }

        if (debugMode)
        {
            _rb.position = closest + normal * 0.02f;
            _rb.linearVelocity = Vector3.ProjectOnPlane(_rb.linearVelocity, normal);
            return;
        }

        // B-1) 猶予終了後、十分に外れた＋下向き/十分速度の時だけ落下
        float verticalVel = Vector3.Dot(_rb.linearVelocity, normal);
        float speed = _rb.linearVelocity.magnitude;
        bool reallyLeavingRoof = verticalVel < -0.05f || speed > 0.35f;
        if (reallyLeavingRoof)
        {
            BeginFall((GetRoofSlideDirection() * Mathf.Max(0.6f, initialSlideSpeed) + Vector3.down * 1.1f), "offDist", closest, offDist);
            return;
        }

        // 単なる浮きは拘束に戻して滑走継続
        _rb.position = closest + normal * 0.02f;
        _rb.linearVelocity = Vector3.ProjectOnPlane(_rb.linearVelocity, normal);
    }

    /// <summary>軒先トリガー用。屋根を無視して落下させる。速度クランプ＋空気抵抗適用</summary>
    public void ForceDropFromEaves(Collider roofCol)
    {
        if (_landed || _rb == null || _state == ClumpState.Grounded) return;
        if (_hasDropped) return;
        var myCol = GetComponent<Collider>();
        if (myCol != null && roofCol != null)
            Physics.IgnoreCollision(myCol, roofCol);
        RecordForcedDrop();
        Vector3 initVel = (Vector3.down + GetRoofSlideDirection() * 0.45f).normalized * 1.2f;
        BeginFall(initVel, "forcedDrop", transform.position, 0f);
    }

    public bool HasLanded => _landed;

    /// <summary>クリックで即削除（軒先の残雪用）。GroundPile の場合は点滅コルーチンも停止</summary>
    public void RemoveImmediate()
    {
        if (_state == ClumpState.Grounded || _state == ClumpState.Despawning)
            StopAllCoroutines();
        FinalizeDepositAndDestroy(true);
    }

    void OnCollisionEnter(Collision col)
    {
        if (_landed) return;
        if (_state == ClumpState.RoofSliding)
        {
            var other = col.collider.GetComponent<SnowClump>() ?? col.collider.GetComponentInParent<SnowClump>();
            if (other != null && other != this && ownerRoofSnow != null && _pressureDetachCooldown <= 0f)
            {
                // 雪塊同士の接触圧で追加剥離を誘発
                float speed = _rb != null ? _rb.linearVelocity.magnitude : initialSlideSpeed;
                if (!debugMode)
                    ownerRoofSnow.TryDetachByPressure(col.GetContact(0).point, Mathf.Max(0.6f, speed * 4f));
                _pressureDetachCooldown = 0.06f;

                var contact = col.GetContact(0);
                float relSpeed = col.relativeVelocity.magnitude;
                Vector3 roofNormal = roofSurfaceCollider != null ? roofSurfaceCollider.transform.up.normalized : Vector3.up;
                bool roofPlaneContact = Mathf.Abs(Vector3.Dot(contact.normal, roofNormal)) < 0.55f;
                if (relSpeed > 0.2f && roofPlaneContact && other.IsRoofKinematic())
                {
                    Vector3 dir = GetRoofSlideDirection();
                    other.ActivateByImpact(dir, Mathf.Clamp(relSpeed * 0.45f, 0.18f, 0.55f));
                }
            }
        }
        if (_state != ClumpState.Falling)
            return;
        var otherClump = col.collider.GetComponent<SnowClump>() ?? col.collider.GetComponentInParent<SnowClump>();
        if (otherClump != null && otherClump != this && otherClump.IsGrounded())
        {
            LandNow();
            return;
        }
        var n = col.transform.name;
        if (n.Contains("Roof")) return;
        if (n.Contains("HouseBody")) return;
        int groundLayer = LayerMask.NameToLayer("Ground");
        bool isGroundLayer = groundLayer >= 0 && col.gameObject.layer == groundLayer;
        bool isGroundByName = n.Contains("Ground") || n.Contains("Plane") || n.Contains("Porch") ||
            n.Contains("Rock") || n.Contains("Grass") || n.Contains("Terrain");
        bool isLowEnough = transform.position.y < 1.5f;
        if ((isGroundLayer || isGroundByName) && isLowEnough)
        {
            LandNow();
        }
    }

    void Update()
    {
        if (_landed) return;
        if (_rb == null) return;
        if (_pressureDetachCooldown > 0f) _pressureDetachCooldown -= Time.deltaTime;

        if (_state == ClumpState.RoofSliding)
        {
            MaintainRoofSlide();
            return;
        }

        if (_state == ClumpState.Falling && (Time.time - _fallStartTime) > 5f)
        {
            FinalizeDepositAndDestroy(true);
            return;
        }

        float speedSq = _rb.linearVelocity.sqrMagnitude;
        if (transform.position.y < 0.25f && speedSq < 0.0001f)
        {
            if (_groundedStartTime < 0f) _groundedStartTime = Time.time;
            if ((Time.time - _groundedStartTime) > 0.5f)
            {
                LandNow();
                return;
            }
        }
        else
        {
            _groundedStartTime = -1f;
        }
    }

    void LandNow()
    {
        if (_landed) return;
        _landed = true;
        _landTime = Time.time;
        _state = ClumpState.Grounded;

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.Sleep();
            _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        StartCoroutine(WaitThenBlinkThenDespawn());
    }

    IEnumerator WaitThenBlinkThenDespawn()
    {
        yield return new WaitForSeconds(GroundPileWaitSeconds);
        UnityEngine.Debug.Log("[DESPAWN] state=Grounded wait=4 blink=1");
        _state = ClumpState.Despawning;
        float blinkElapsed = 0f;
        bool visible = true;
        while (blinkElapsed < GroundPileBlinkDuration)
        {
            visible = !visible;
            SetRenderersVisible(visible);
            yield return new WaitForSeconds(GroundPileBlinkInterval);
            blinkElapsed += GroundPileBlinkInterval;
        }
        SetRenderersVisible(false);
        int scoreBefore = SnowPhysicsScoreManager.Instance != null ? SnowPhysicsScoreManager.Instance.Score : 0;
        UnityEngine.Debug.Log($"[SNOW_HIT_CHECK] hit_detected=true hit_object={gameObject.name} script=SnowClump.cs time={Time.time:F2} current_score={scoreBefore}");
        SnowPhysicsScoreManager.Instance?.AddScoreOnDespawn();
        FinalizeDepositAndDestroy(false);
    }

    void SetRenderersVisible(bool visible)
    {
        foreach (var r in _renderers)
            if (r != null) r.enabled = visible;
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

    IEnumerator BeginSlideImpulseNextFrame()
    {
        yield return new WaitForFixedUpdate();
        if (_hasBeginSlide) yield break;
        if (_rb == null || _state != ClumpState.RoofSliding) yield break;
        if (!_rb.isKinematic) yield break;
        _hasBeginSlide = true;
        RecordBeginSlide();
        var dir = GetRoofSlideDirection();
        _rb.isKinematic = false;
        _offDistGraceTimer = OffDistGraceDuration;
        _roofSlideStartTime = Time.time;
        _rb.AddForce(dir * initialSlideSpeed, ForceMode.Impulse); // SnowTestCube と同じ起動パターン
    }

    void MaintainRoofSlide()
    {
        if (roofSurfaceCollider == null)
        {
            BeginFall(Vector3.down * 1.1f, "noRoofSurface", transform.position, 1f);
            return;
        }

        Vector3 roofNormal = roofSurfaceCollider.transform.up.normalized;
        Vector3 slideDir = GetRoofSlideDirection();
        if (_rb.isKinematic)
        {
            _roofRestTime += Time.deltaTime;
            if (_roofRestTime > 0.2f)
            {
                _rb.isKinematic = false;
                _offDistGraceTimer = OffDistGraceDuration;
                _roofSlideStartTime = Time.time;
                _rb.AddForce(slideDir * Mathf.Max(0.08f, initialSlideSpeed * 0.35f), ForceMode.Impulse);
                _roofRestTime = 0f;
            }
            return;
        }

        Vector3 v = _rb.linearVelocity;
        Vector3 tangentV = Vector3.ProjectOnPlane(v, roofNormal);
        if (tangentV.sqrMagnitude < 0.0025f)
        {
            _roofRestTime += Time.deltaTime;
            if (_roofRestTime > 0.08f)
            {
                // 屋根上で止まった塊は一旦キネマ化（下流からの衝突で再起動）
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
            }
        }
        else
        {
            _roofRestTime = 0f;
        }

        if (!_rb.isKinematic)
        {
            if (Vector3.Dot(tangentV.normalized, slideDir) < 0.3f)
                tangentV = Vector3.Lerp(tangentV, slideDir * tangentV.magnitude, 0.25f);
            _rb.linearVelocity = tangentV + (-roofNormal * 0.02f);
        }

        Vector3 closest = roofSurfaceCollider.ClosestPoint(transform.position);
        if ((closest - transform.position).sqrMagnitude > 0.25f)
        {
            if (!debugMode)
            {
                BeginFall(slideDir * Mathf.Max(0.6f, tangentV.magnitude) + Vector3.down * 1.1f, "offDist", closest, Vector3.Distance(transform.position, closest));
                return;
            }
            _rb.position = closest + roofNormal * 0.02f;
            _rb.linearVelocity = Vector3.ProjectOnPlane(_rb.linearVelocity, roofNormal);
        }
        TryPressurePropagation();
    }

    void BeginFall(Vector3 initialVelocity, string reason = "unknown", Vector3 closest = default, float offDist = 0f)
    {
        if (_state == ClumpState.Falling || _state == ClumpState.Grounded) return;
        if (_hasDropped) return;
        _hasDropped = true;
        RecordDrop();
        _state = ClumpState.Falling;
        _fallStartTime = Time.time;
        _groundedStartTime = -1f;
        _edgeStickTime = 0f;
        if (_rb == null) return;
        var myCol = GetComponent<Collider>();
        if (myCol != null && roofSurfaceCollider != null)
            Physics.IgnoreCollision(myCol, roofSurfaceCollider);
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.constraints = RigidbodyConstraints.FreezeRotation;

        // ASSI: 屋根沿い滑落 - 真下落下を減らし、屋根傾斜方向の初速を優先
        Vector3 downhill = GetRoofSlideDirection();
        Vector3 vDown = Vector3.Project(initialVelocity, downhill);
        Vector3 vSide = initialVelocity - vDown;
        float downMag = Mathf.Min(vDown.magnitude, maxFallCarrySpeed);
        // 屋根斜面方向を主軸に、真下成分は最小限
        Vector3 v = downhill.normalized * Mathf.Max(downMag, 0.5f) + vSide * sideDamp + downhill.normalized * dropImpulse;
        _rb.linearVelocity = v;

        _rb.linearDamping = airDrag;
        _rb.angularDamping = 2f;
        if (debugMode) Debug.Log($"[SnowClumpDrop] reason={reason} pos={transform.position} vel={v} offDist={offDist:F3}");
    }

    void TryPressurePropagation()
    {
        if (debugMode) return;
        if (ownerRoofSnow == null || _pressureDetachCooldown > 0f) return;
        Vector3 slideDir = GetRoofSlideDirection();
        var hits = Physics.OverlapSphere(transform.position + slideDir * 0.18f, 0.2f, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            var other = hits[i].GetComponent<SnowClump>() ?? hits[i].GetComponentInParent<SnowClump>();
            if (other == null || other == this) continue;
            float speed = _rb != null ? _rb.linearVelocity.magnitude : initialSlideSpeed;
            ownerRoofSnow.TryDetachByPressure(transform.position + slideDir * 0.22f, Mathf.Max(0.7f, speed * 4.5f));
            _pressureDetachCooldown = 0.08f;
            break;
        }
    }

    Vector3 GetRoofSlideDirection()
    {
        if (roofSurfaceCollider != null)
        {
            Vector3 d = Vector3.ProjectOnPlane(Vector3.down, roofSurfaceCollider.transform.up).normalized;
            if (d.sqrMagnitude > 0.001f) return d;
        }
        if (slideDownDirection.sqrMagnitude > 0.001f) return slideDownDirection.normalized;
        return Vector3.down;
    }

    public bool IsRoofKinematic()
    {
        return _state == ClumpState.RoofSliding && _rb != null && _rb.isKinematic;
    }

    public void ActivateByImpact(Vector3 dir, float impulse)
    {
        if (_rb == null || !IsRoofKinematic()) return;
        Vector3 roofUp = roofSurfaceCollider != null ? roofSurfaceCollider.transform.up : Vector3.up;
        Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (slideDir.sqrMagnitude < 0.001f) slideDir = GetRoofSlideDirection();
        _rb.isKinematic = false;
        _offDistGraceTimer = OffDistGraceDuration;
        _roofSlideStartTime = Time.time;
        _rb.AddForce(slideDir * impulse, ForceMode.Impulse);
        _roofRestTime = 0f;
    }

    void OnDestroy()
    {
        s_allActive.Remove(this);
        if (ownerRoofSnow != null)
            ownerRoofSnow.NotifyClumpDestroyed(this);
    }

    public static int GetActiveCount() => s_allActive.Count;

    public static int GetDynamicCount()
    {
        int n = 0;
        foreach (var c in s_allActive)
            if (c != null && (c._state == ClumpState.RoofSliding || c._state == ClumpState.Falling)) n++;
        return n;
    }

    /// <summary>ASSI: グローバル cap 超過時に古い地面雪を強制消去</summary>
    public static void EvictOldestGroundPiecesIfNeeded(int cap = MaxActiveSnowPieces)
    {
        while (s_allActive.Count >= cap)
        {
            SnowClump oldest = null;
            float oldestTime = float.MaxValue;
            foreach (var c in s_allActive)
            {
                if (c == null || !c.IsGrounded()) continue;
                if (c.GroundPileStartTime < oldestTime)
                {
                    oldestTime = c.GroundPileStartTime;
                    oldest = c;
                }
            }
            if (oldest == null) break;
            oldest.ForceEarlyDespawn();
        }
    }

    /// <summary>cap 用。スコア加算なしで即消去</summary>
    public void ForceEarlyDespawn()
    {
        if (_hasDeposited) return;
        StopAllCoroutines();
        _hasDeposited = true;
        _state = ClumpState.Grounded;
        Destroy(gameObject);
    }

    static void MaybeLogCounters()
    {
        if (!Application.isPlaying) return;
        if (s_windowStart < 0f) s_windowStart = Time.time;
        if (Time.time - s_windowStart < 1f) return;

        int active = 0;
        int sliding = 0;
        int falling = 0;
        foreach (var c in FindObjectsByType<SnowClump>(FindObjectsSortMode.None))
        {
            if (c == null) continue;
            active++;
            if (c.IsSlidingOnRoof()) sliding++;
            else if (c.IsFallingState()) falling++;
        }

        float avgRoofSnow = 0f;
        int roofCount = 0;
        foreach (var roof in FindObjectsByType<RoofSnow>(FindObjectsSortMode.None))
        {
            if (roof == null) continue;
            avgRoofSnow += roof.snowAmount;
            roofCount++;
        }
        if (roofCount > 0) avgRoofSnow /= roofCount;

        Debug.Log($"[SnowClumpDiag/1s] activeClumps={active} sliding={sliding} falling={falling} deposited={s_depositPerSec} forcedDeposit={s_forcedDepositPerSec} forcedDrop={s_forcedDropPerSec} roofSnowAmount(avg)={avgRoofSnow:F2} spawn={s_spawnPerSec} beginSlide={s_beginSlidePerSec} drop={s_dropPerSec}");
        if (s_spawnPerSec > 80 || s_dropPerSec > 80)
            Debug.LogWarning("[SnowClumpDiag] high event rate detected. Check repeated detach/drop path.");

        s_spawnPerSec = 0;
        s_beginSlidePerSec = 0;
        s_dropPerSec = 0;
        s_depositPerSec = 0;
        s_forcedDropPerSec = 0;
        s_forcedDepositPerSec = 0;
        s_windowStart = Time.time;
    }

    public static void TickDiagnostics()
    {
        MaybeLogCounters();
    }

    public static void RecordSpawn()
    {
        s_spawnPerSec++;
        MaybeLogCounters();
    }

    static void RecordBeginSlide()
    {
        s_beginSlidePerSec++;
        MaybeLogCounters();
    }

    static void RecordDrop()
    {
        s_dropPerSec++;
        MaybeLogCounters();
    }

    static void RecordForcedDrop()
    {
        s_forcedDropPerSec++;
        MaybeLogCounters();
    }

    void RecordDeposit()
    {
        s_depositPerSec++;
        MaybeLogCounters();
    }

    static void RecordForcedDeposit()
    {
        s_forcedDepositPerSec++;
        MaybeLogCounters();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCounters()
    {
        s_spawnPerSec = 0;
        s_beginSlidePerSec = 0;
        s_dropPerSec = 0;
        s_depositPerSec = 0;
        s_forcedDropPerSec = 0;
        s_forcedDepositPerSec = 0;
        s_windowStart = -1f;
    }

    public bool IsSlidingOnRoof()
    {
        return _state == ClumpState.RoofSliding && _rb != null && !_rb.isKinematic;
    }

    public bool IsFallingState()
    {
        return _state == ClumpState.Falling;
    }

    public bool IsGroundPile()
    {
        return _state == ClumpState.Grounded;
    }

    public bool IsGrounded()
    {
        return _state == ClumpState.Grounded;
    }

    public float GroundPileStartTime => _landTime;

    bool IsNearColliderEdge(Vector3 point, Bounds b, float margin)
    {
        float dx = Mathf.Min(Mathf.Abs(point.x - b.min.x), Mathf.Abs(b.max.x - point.x));
        float dy = Mathf.Min(Mathf.Abs(point.y - b.min.y), Mathf.Abs(b.max.y - point.y));
        float dz = Mathf.Min(Mathf.Abs(point.z - b.min.z), Mathf.Abs(b.max.z - point.z));
        return dx <= margin || dy <= margin || dz <= margin;
    }

    void FinalizeDepositAndDestroy(bool forced)
    {
        if (_hasDeposited) return;
        _hasDeposited = true;
        if (forced) RecordForcedDeposit();
        RecordDeposit();

        if (_rb != null)
        {
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.isKinematic = true;
            _rb.Sleep();
        }

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (GroundSnowAccumulator.Instance != null)
            GroundSnowAccumulator.Instance.AddSnow(transform.position, groundDepositAmount);
        EmitToGroundSnow();
        bool hasOwner = ownerRoofSnow != null;
        if (hasOwner)
            ownerRoofSnow.OnClumpDeposited(consumeOnDepositAmount);
        var state = _state == ClumpState.Despawning ? SnowDespawnLogger.SnowState.Despawning
            : _state == ClumpState.Grounded ? SnowDespawnLogger.SnowState.Grounded
            : SnowDespawnLogger.SnowState.Unknown;
        SnowDespawnLogger.RequestDespawn(forced ? "ForcedDeposit" : "Deposit", state, transform.position, gameObject);
        Debug.Log($"[SnowClumpDeposit] destroyed=true ownerRoofSnowNull={!hasOwner} forced={forced}");
        Destroy(gameObject, 0.5f);
    }
}
