using UnityEngine;
using System.Collections;

/// <summary>屋根端を越えた雪ブロック用。Rigidbodyで重力落下、地面接触でGrounded→4s待機→1s点滅→Despawn。</summary>
public class SnowPackFallingPiece : MonoBehaviour
{
    public SnowPackSpawner spawner;
    public LayerMask groundMask = ~0;
    [Tooltip("Safety timeout when never lands (e.g. fell off map). No despawn while Falling except this.")]
    public float fallTimeoutSeconds = 30f;
    [Header("Roof stuck auto-despawn")]
    [Tooltip("屋根上で velocity < この値が続いた秒数でDespawn。")]
    public float roofStuckSpeedThreshold = 0.05f;
    [Tooltip("屋根上でほぼ停止がこの秒数続いたらDespawn。止まり雪対策で3秒（再タップ猶予あり）。")]
    public float roofStuckDespawnSeconds = 3f;

    const float GroundedWaitSeconds = 4.0f;
    const float BlinkDuration = 1.0f;
    const float BlinkInterval = 0.1f;

    enum State { Falling, Grounded, Despawning }
    State _state = State.Falling;

    public bool hasLanded => _state == State.Grounded;
    Rigidbody _rb;
    float _startTime;
    Renderer[] _renderers;
    float _roofStuckTimer;
    bool _roofStuckLogged;
    static int _groundHitCount;
    static int _fallingTriggeredCount;

    /// <summary>落下開始時に呼ぶ。Rigidbodyを設定し、初速を適用。Collider追加・Default層でタップ/衝突可能に。</summary>
    public void ActivateFalling(Vector3 initialVelocity)
    {
        _startTime = Time.time;
        _state = State.Falling;
        _roofStuckTimer = 0f;
        _roofStuckLogged = false;
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearVelocity = initialVelocity;
        _rb.constraints = RigidbodyConstraints.None;
        _renderers = GetComponentsInChildren<Renderer>(true);

        var col = GetComponent<Collider>();
        if (col == null)
        {
            col = gameObject.AddComponent<BoxCollider>();
            var b = (BoxCollider)col;
            b.size = Vector3.one * 0.15f;
            b.center = Vector3.zero;
        }
        if (col != null) { col.enabled = true; col.isTrigger = false; }
        gameObject.layer = 0;
        SetLayerRecursively(gameObject, 0);

        _fallingTriggeredCount++;
        DetachedSnowRegistry.RegisterFalling(this);
        DetachedSnowDiagnostics.LogFallingInfoIfFirst(this);
        LogDetachedSpawn();
        SnowLoopLogCapture.AppendToAssiReport($"=== FallingTriggered === count={_fallingTriggeredCount}");
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        foreach (Transform c in go.transform)
            if (c != null) SetLayerRecursively(c.gameObject, layer);
    }

    void LogDetachedSpawn()
    {
        var col = GetComponent<Collider>();
        bool rbSleep = _rb != null && _rb.IsSleeping();
        Vector3 v = _rb != null ? _rb.linearVelocity : Vector3.zero;
        UnityEngine.Debug.Log($"[DetachedSpawn] class=falling pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({v.x:F2},{v.y:F2},{v.z:F2}) layer={LayerMask.LayerToName(gameObject.layer)} col.enabled={(col != null && col.enabled)}");
    }

    void Update()
    {
        if (_state == State.Falling)
        {
            if ((Time.time - _startTime) >= fallTimeoutSeconds)
            {
                ReturnFromFall("Timeout");
                return;
            }
            if (IsOnRoof() && _rb != null)
            {
                float speed = _rb.linearVelocity.magnitude;
                bool sleeping = _rb.IsSleeping();
                if (speed < roofStuckSpeedThreshold || sleeping)
                {
                    _roofStuckTimer += Time.deltaTime;
                    if (!_roofStuckLogged && _roofStuckTimer >= 0.5f)
                    {
                        _roofStuckLogged = true;
                        var col = GetComponent<Collider>();
                        UnityEngine.Debug.Log($"[StoppedSnow] class=FallingPiece pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({_rb.linearVelocity.x:F2},{_rb.linearVelocity.y:F2},{_rb.linearVelocity.z:F2}) speed={speed:F3} sleeping={sleeping} layer={LayerMask.LayerToName(gameObject.layer)} colliderEnabled={(col != null && col.enabled)} isKinematic={_rb.isKinematic} timer={_roofStuckTimer:F2}s");
                    }
                    if (_roofStuckTimer >= roofStuckDespawnSeconds)
                    {
                        LogDetachedStop("roofStuck");
                        LogDetachedForceDespawn("stopped");
                        StartCoroutine(FadeOutThenDespawn("RoofStuck"));
                        return;
                    }
                }
                else
                {
                    _roofStuckTimer = 0f;
                    _roofStuckLogged = false;
                }
            }
            else
            {
                _roofStuckTimer = 0f;
                _roofStuckLogged = false;
            }
        }
    }

    bool IsOnRoof()
    {
        var roof = Object.FindFirstObjectByType<RoofSnowSystem>();
        if (roof == null || roof.roofSlideCollider == null) return false;
        var b = roof.roofSlideCollider.bounds;
        b.Expand(0.5f); // 止まり雪対策: 端付近も「屋根上」と判定（CheckDetachedRoofStuck と同様）
        return b.Contains(transform.position);
    }

    void LogDetachedStop(string reason)
    {
        var col = GetComponent<Collider>();
        bool rbSleep = _rb != null && _rb.IsSleeping();
        Vector3 v = _rb != null ? _rb.linearVelocity : Vector3.zero;
        UnityEngine.Debug.Log($"[DetachedStop] pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) velocity=({v.x:F2},{v.y:F2},{v.z:F2}) isSleeping={rbSleep} layer={LayerMask.LayerToName(gameObject.layer)} collider.enabled={(col != null && col.enabled)}");
        float downhillDot = 0f;
        var roof = Object.FindFirstObjectByType<RoofSnowSystem>();
        if (roof != null && roof.roofSlideCollider != null)
        {
            Vector3 roofN = roof.roofSlideCollider.transform.up.normalized;
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, roofN).normalized;
            downhillDot = downhill.sqrMagnitude > 0.01f ? Vector3.Dot(v, downhill) : 0f;
        }
        string state = rbSleep || v.magnitude < 0.05f ? "physics_stopped" : "moving";
        UnityEngine.Debug.Log($"[StopReason] velocity=({v.x:F2},{v.y:F2},{v.z:F2}) downhillDot={downhillDot:F3} state={state}");
    }

    void LogDetachedForceDespawn(string reason)
    {
        UnityEngine.Debug.Log($"[DetachedForceDespawn] pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) reason={reason}");
    }

    IEnumerator FadeOutThenDespawn(string reason = "RoofStuckAuto")
    {
        _state = State.Despawning;
        float dur = 0.3f;
        Vector3 s0 = transform.localScale;
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            transform.localScale = s0 * (1f - t / dur);
            yield return null;
        }
        ReturnFromFall(reason);
    }

    /// <summary>中央管理からの屋根停止強制消去。</summary>
    public void ForceDespawnFromCentralRoofStuck()
    {
        if (_state == State.Despawning) return;
        LogDetachedStop("centralRoofStuck");
        LogDetachedForceDespawn("stopped");
        StartCoroutine(FadeOutThenDespawn("CentralRoofStuck"));
    }

    void OnCollisionEnter(Collision collision)
    {
        if (this == null || gameObject == null || collision == null || collision.collider == null) return;
        if (_state != State.Falling) return;
        bool isGround = false;
        int layer = collision.gameObject.layer;
        if (((1 << layer) & groundMask.value) != 0) isGround = true;
        string name = collision.collider.name ?? "";
        if (name.Contains("Ground") || name.Contains("Plane") || name.Contains("Porch") || name.Contains("Rock") || name.Contains("Grass") || name.Contains("Roof"))
            isGround = true;
        if (!isGround) return;

        _groundHitCount++;
        SnowLoopLogCapture.AppendToAssiReport($"=== GroundHit === count={_groundHitCount}");
        LandNow();
    }

    void LandNow()
    {
        if (_state != State.Falling) return;
        _state = State.Grounded;
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
            _rb.Sleep();
        }
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;
        LogDetachedStop("LandNow");
        StartCoroutine(WaitThenBlinkThenDespawn());
    }

    IEnumerator WaitThenBlinkThenDespawn()
    {
        yield return new WaitForSeconds(GroundedWaitSeconds);
        UnityEngine.Debug.Log("[DESPAWN] state=Grounded wait=4 blink=1");
        _state = State.Despawning;
        float blinkElapsed = 0f;
        bool visible = true;
        while (blinkElapsed < BlinkDuration)
        {
            visible = !visible;
            SetRenderersVisible(visible);
            yield return new WaitForSeconds(BlinkInterval);
            blinkElapsed += BlinkInterval;
        }
        SetRenderersVisible(false);
        int scoreBefore = SnowPhysicsScoreManager.Instance != null ? SnowPhysicsScoreManager.Instance.Score : 0;
        UnityEngine.Debug.Log($"[SNOW_HIT_CHECK] hit_detected=true hit_object={gameObject.name} script=SnowPackFallingPiece.cs time={Time.time:F2} current_score={scoreBefore}");
        SnowPhysicsScoreManager.Instance?.AddScoreOnDespawn();
        CoreGameplayManager.Instance?.AddMoneyFromFallingPiece();
        ReturnFromFall("Despawn");
    }

    void SetRenderersVisible(bool v)
    {
        if (_renderers == null) return;
        foreach (var r in _renderers)
            if (r != null) r.enabled = v;
    }

    void ReturnFromFall(string reason)
    {
        if (this == null || gameObject == null) return;
        DetachedSnowRegistry.UnregisterFalling(this);
        var state = _state == State.Falling ? SnowDespawnLogger.SnowState.Falling
            : _state == State.Grounded ? SnowDespawnLogger.SnowState.Grounded
            : SnowDespawnLogger.SnowState.Despawning;
        SnowDespawnLogger.RequestDespawn(reason, state, transform.position, gameObject);
        var rb = GetComponent<Rigidbody>();
        if (rb != null) Destroy(rb);
        var comp = GetComponent<SnowPackFallingPiece>();
        if (comp != null) Destroy(comp);
        if (spawner != null && spawner.gameObject != null)
            spawner.ReturnToPoolFromFalling(transform, reason);
    }

    public static int GroundHitCount => _groundHitCount;
    public static int FallingTriggeredCount => _fallingTriggeredCount;

    /// <summary>packed=0一括掃除用。屋根上に残った落下ピースを強制Despawn。</summary>
    public void ForceDespawnFromSweep()
    {
        LogDetachedForceDespawn("RoofCleanup");
        ReturnFromFall("PackedZeroSweep");
    }
}
