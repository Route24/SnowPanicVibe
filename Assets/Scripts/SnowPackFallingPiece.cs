using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
    /// <summary>軒先越え直後は屋根上と誤判定されやすいので、この秒数は roof stuck チェックをスキップ。</summary>
    const float RoofStuckGraceSeconds = 0.6f;

    const float GroundedWaitSeconds = 4.0f;
    const float BlinkDuration = 1.0f;
    const float BlinkInterval = 0.1f;

    enum State { RoofSliding, Falling, Grounded, Despawning }
    State _state = State.Falling;

    /// <summary>屋根スライドフェーズ用。滑落後Fallingへ遷移。</summary>
    const float RoofSlidePhaseSeconds = 2.0f;
    Vector3 _roofCenter;
    Vector3 _roofDownhill;
    float _roofEdgeTEnd;
    Collider _roofCollider;
    readonly List<Vector3> _trajectoryPoints = new List<Vector3>();
    float _roofContactStartTime = -1f;
    float _roofContactEndTime = -1f;

    public bool hasLanded => _state == State.Grounded;

    // クラスター追従フォロワー管理
    readonly List<SnowClusterFollower> _followers = new List<SnowClusterFollower>();
    public void AddFollower(SnowClusterFollower f) { if (f != null) _followers.Add(f); }
    void ReleaseFollowers(string reason)
    {
        foreach (var f in _followers)
            if (f != null) f.Release(reason);
        _followers.Clear();
    }

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
        _roofCollider = null;
        _trajectoryPoints.Clear();
        _roofContactStartTime = -1f;
        _roofContactEndTime = -1f;
        _roofStuckTimer = 0f;
        _roofStuckLogged = false;
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.useGravity = true;
        _rb.linearVelocity = initialVelocity;
        _rb.constraints = RigidbodyConstraints.None;
        _renderers = GetComponentsInChildren<Renderer>(true);
        EnableFallingRenderers();
        ScaleUpForFalling();

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

        var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
        _roofDownhill = (roofSys != null && roofSys.roofSlideCollider != null) ? Vector3.ProjectOnPlane(Vector3.down, roofSys.roofSlideCollider.transform.up).normalized : Vector3.forward;

        _fallingTriggeredCount++;
        float velMag = initialVelocity.magnitude;
        float verticalMag = Mathf.Abs(initialVelocity.y);
        float horizontalMag = Mathf.Sqrt(initialVelocity.x * initialVelocity.x + initialVelocity.z * initialVelocity.z);
        bool downhillDominantAtT0 = horizontalMag > verticalMag && velMag > 0.05f;
        bool verticalDominantAtT0 = verticalMag > horizontalMag && velMag > 0.05f;
        UnityEngine.Debug.Log($"[ROOT_CAUSE_ISOLATION] begin_fall_source=SnowPackFallingPiece vel=({initialVelocity.x:F2},{initialVelocity.y:F2},{initialVelocity.z:F2}) roof_ignored=N/A use_gravity=YES");
        UnityEngine.Debug.Log($"[VERTICAL_DROP_ISOLATION] source=SnowPackFallingPiece maintains_roof_contact_after_detach=NO loses_contact_immediately=YES gravity_applied_too_early=YES downhill_velocity_dominant_at_t0={(downhillDominantAtT0 ? "YES" : "NO")} vertical_velocity_dominant_at_t0={(verticalDominantAtT0 ? "YES" : "NO")} detached_at=PAST_ROOF_EDGE");
        StartCoroutine(LogVelocityAt01s());
        DetachedSnowRegistry.RegisterFalling(this);
        DetachedSnowDiagnostics.LogFallingInfoIfFirst(this);
        LogDetachedSpawn();
        SnowLoopLogCapture.AppendToAssiReport($"=== FallingTriggered === count={_fallingTriggeredCount}");
    }

    /// <summary>屋根面上でdetach。useGravity=falseで短いslide phase→軒先越えでFallingへ。</summary>
    public void ActivateRoofSlide(Vector3 initialVelocity, Collider roofCol, Vector3 roofCenter, Vector3 downhill, float roofEdgeTEnd)
    {
        _startTime = Time.time;
        _state = State.RoofSliding;
        _roofCenter = roofCenter;
        _roofDownhill = downhill;
        _roofEdgeTEnd = roofEdgeTEnd;
        _roofCollider = roofCol;
        _trajectoryPoints.Clear();
        _roofContactStartTime = -1f;
        _roofContactEndTime = -1f;
        _roofStuckTimer = 0f;
        _roofStuckLogged = false;
        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        // 初期化時点で即 Kinematic にして物理エンジンの介入を完全排除
        _rb.isKinematic = true;
        _rb.useGravity = false;
        // A: velocity を屋根面上にプロジェクションして Y 混入を除去（参照用に保持）
        Vector3 roofNormal = roofCol != null ? roofCol.transform.up : Vector3.up;
        Vector3 slideSurfaceVelocity = Vector3.ProjectOnPlane(initialVelocity, roofNormal);
        if (slideSurfaceVelocity.sqrMagnitude < 0.001f) slideSurfaceVelocity = downhill * initialVelocity.magnitude;
        _rb.constraints = RigidbodyConstraints.None;
        _renderers = GetComponentsInChildren<Renderer>(true);
        // falling piece は常に表示（showSnowGridDebug に関係なく本番 mesh で見せる）
        EnableFallingRenderers();
        // 落下時は雪片らしいサイズに拡大（屋根上の小さなピースを見えるサイズに）
        ScaleUpForFalling();

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

        // B: 開始位置を屋根面上に補正（closest point + 法線方向に半径分オフセット）
        if (roofCol != null)
        {
            Vector3 closest = roofCol.ClosestPoint(transform.position);
            float halfSize = 0.075f;
            transform.position = closest + roofNormal * halfSize;
        }

        _fallingTriggeredCount++;
        float velMag = slideSurfaceVelocity.magnitude;
        float horizontalMag = Mathf.Sqrt(slideSurfaceVelocity.x * slideSurfaceVelocity.x + slideSurfaceVelocity.z * slideSurfaceVelocity.z);
        bool downhillDominantAtT0 = horizontalMag > Mathf.Abs(slideSurfaceVelocity.y) && velMag > 0.05f;
        UnityEngine.Debug.Log($"[VERTICAL_DROP_ISOLATION] source=SnowPackFallingPiece maintains_roof_contact_after_detach=YES loses_contact_immediately=NO gravity_applied_too_early=NO downhill_velocity_dominant_at_t0={(downhillDominantAtT0 ? "YES" : "NO")} detached_at=ON_ROOF_SURFACE slide_vel=({slideSurfaceVelocity.x:F2},{slideSurfaceVelocity.y:F2},{slideSurfaceVelocity.z:F2})");
        DetachedSnowRegistry.RegisterFalling(this);
        DetachedSnowDiagnostics.LogFallingInfoIfFirst(this);
        LogDetachedSpawn();
        LogRealCollisionDiagnostics(roofCol);
        SnowLoopLogCapture.AppendToAssiReport($"=== RoofSlideTriggered === count={_fallingTriggeredCount}");
    }

    void SwitchToFalling()
    {
        if (_state != State.RoofSliding || _rb == null) return;
        _state = State.Falling;
        // G: Kinematic を解除して通常物理落下に戻す
        _rb.isKinematic = false;
        _rb.useGravity = true;
        // 軒先を越えた時点の downhill 速度を初速として引き継ぐ（Y成分を抑えて真下落下を防ぐ）
        Vector3 downhillFlat = new Vector3(_roofDownhill.x, Mathf.Min(_roofDownhill.y, 0f), _roofDownhill.z).normalized;
        if (downhillFlat.sqrMagnitude < 0.001f) downhillFlat = _roofDownhill.normalized;
        _rb.linearVelocity = downhillFlat * SlideMinSpeed;
        _startTime = Time.time;
        StartCoroutine(LogVelocityAt01s());
        float roofContactDur = _roofContactStartTime >= 0f && _roofContactEndTime >= 0f ? (_roofContactEndTime - _roofContactStartTime) : 0f;
        UnityEngine.Debug.Log($"[SLIDE_VISUALIZATION] roof_contact_duration_seconds={roofContactDur:F2} roof_contact_start={_roofContactStartTime:F2} roof_contact_end={_roofContactEndTime:F2} pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({_rb.linearVelocity.x:F2},{_rb.linearVelocity.y:F2},{_rb.linearVelocity.z:F2})");
        Vector3 vAtTransition = _rb.linearVelocity;
        float vMagT = vAtTransition.magnitude;
        float vVertT = Mathf.Abs(vAtTransition.y);
        float vHorizT = Mathf.Sqrt(vAtTransition.x * vAtTransition.x + vAtTransition.z * vAtTransition.z);
        bool vertDomAtTransition = vMagT > 0.05f && vVertT > vHorizT;
        UnityEngine.Debug.Log($"[VERTICAL_DROP_ISOLATION] source=SnowPackFallingPiece slide_to_falling_transition pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({vAtTransition.x:F2},{vAtTransition.y:F2},{vAtTransition.z:F2}) vertical_velocity_still_dominant={(vertDomAtTransition ? "YES" : "NO")}");
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        foreach (Transform c in go.transform)
            if (c != null) SetLayerRecursively(c.gameObject, layer);
    }

    IEnumerator LogVelocityAt01s()
    {
        yield return new WaitForSeconds(0.1f);
        if (_rb == null || _state != State.Falling) yield break;
        Vector3 v = _rb.linearVelocity;
        float vMag = v.magnitude;
        float vVertical = Mathf.Abs(v.y);
        float vHorizontal = Mathf.Sqrt(v.x * v.x + v.z * v.z);
        bool verticalDominant = vMag > 0.05f && vVertical > vHorizontal;
        UnityEngine.Debug.Log($"[VERTICAL_DROP_ISOLATION] after_0_1s vel=({v.x:F2},{v.y:F2},{v.z:F2}) vertical_velocity_still_dominant={(verticalDominant ? "YES" : "NO")}");
    }

    /// <summary>
    /// 落下時に雪片らしいサイズへ拡大する。
    /// 屋根上のピースは非常に薄く小さいため、落下時に球状メッシュが見えるサイズにする。
    /// </summary>
    void ScaleUpForFalling()
    {
        // 現在のスケールを取得し、雪片として見えるサイズ（最低20cm）に拡大
        Vector3 cur = transform.localScale;
        float targetXZ = Mathf.Max(cur.x, 0.20f) * UnityEngine.Random.Range(1.5f, 2.5f);
        float targetY  = Mathf.Max(cur.y, 0.15f) * UnityEngine.Random.Range(1.5f, 2.5f);
        // 球状に近づける（XYZを均一に）
        float uniform = Mathf.Max(targetXZ, targetY);
        transform.localScale = new Vector3(uniform, uniform * 0.8f, uniform);
        UnityEngine.Debug.Log($"[FallingScale] scaled_up from=({cur.x:F3},{cur.y:F3},{cur.z:F3}) to=({transform.localScale.x:F3},{transform.localScale.y:F3},{transform.localScale.z:F3})");
    }

    void EnableFallingRenderers()
    {
        if (_renderers == null) return;
        bool logged = false;
        foreach (var r in _renderers)
        {
            if (r == null) continue;
            r.enabled = true;
            if (!logged)
            {
                logged = true;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null) { var child = r.transform.Find("Mesh"); if (child != null) mf = child.GetComponent<MeshFilter>(); }
                string meshName = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "null";
                string matName = r.sharedMaterial != null ? r.sharedMaterial.name : "null";
                string meshSrc = meshName.Contains("Rounded") ? "SnowVisual(production)"
                    : meshName.Contains("Cube") ? "BuildCubeMesh(fallback!)"
                    : meshName.Contains("NonSym") ? "BuildNonSymMesh(debug)"
                    : "unknown";
                UnityEngine.Debug.Log($"[FALLING_MESH_ROUTE] mesh_name={meshName} mesh_source={meshSrc} renderer_name={r.name} prefab_name={gameObject.name} script_source=SnowPackFallingPiece.cs assignment_callsite=EnableFallingRenderers material={matName}");
            }
        }
    }

    void LogDetachedSpawn()
    {
        var col = GetComponent<Collider>();
        bool rbSleep = _rb != null && _rb.IsSleeping();
        Vector3 v = _rb != null ? _rb.linearVelocity : Vector3.zero;
        UnityEngine.Debug.Log($"[DetachedSpawn] class=falling pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({v.x:F2},{v.y:F2},{v.z:F2}) layer={LayerMask.LayerToName(gameObject.layer)} col.enabled={(col != null && col.enabled)}");
    }

    void LogRealCollisionDiagnostics(Collider roofCol)
    {
        var myCol = GetComponent<Collider>();
        bool myColEnabled = myCol != null && myCol.enabled;
        bool myColTrigger = myCol != null && myCol.isTrigger;
        bool rbDetect = _rb != null && _rb.detectCollisions;
        bool rbGravity = _rb != null && _rb.useGravity;
        int myLayer = gameObject.layer;

        bool roofIsTrigger = roofCol != null && roofCol.isTrigger;
        int roofLayer = roofCol != null ? roofCol.gameObject.layer : -1;
        string roofLayerName = roofLayer >= 0 ? LayerMask.LayerToName(roofLayer) : "null";
        bool layerCollisionEnabled = roofLayer >= 0 && !Physics.GetIgnoreLayerCollision(myLayer, roofLayer);
        Bounds roofBounds = roofCol != null ? roofCol.bounds : default;

        UnityEngine.Debug.Log($"[ROOF_COLLISION_REAL] DIAGNOSTICS roof_isTrigger={roofIsTrigger} roof_layer={roofLayerName}({roofLayer}) roof_bounds_center=({roofBounds.center.x:F2},{roofBounds.center.y:F2},{roofBounds.center.z:F2}) roof_bounds_size=({roofBounds.size.x:F2},{roofBounds.size.y:F2},{roofBounds.size.z:F2})");
        UnityEngine.Debug.Log($"[ROOF_COLLISION_REAL] DIAGNOSTICS piece_col_enabled={myColEnabled} piece_col_isTrigger={myColTrigger} piece_rb_detectCollisions={rbDetect} piece_rb_useGravity={rbGravity} piece_layer={LayerMask.LayerToName(myLayer)}({myLayer}) layer_collision_enabled={layerCollisionEnabled}");
    }

    /// <summary>slide phase 中の最低 downhill 速度。これを下回ったら補完する。</summary>
    const float SlideMinSpeed = 2.5f;
    /// <summary>slide phase 全期間 roof plane 上を Kinematic 移動させる。</summary>
    const float SlideGraceSeconds = 0.25f; // 後方互換のため残す（未使用）

    void FixedUpdate()
    {
        if ((_state != State.RoofSliding && _state != State.Falling) || _rb == null) return;
        float age = Time.time - _startTime;
        if (age < 0.5f) _trajectoryPoints.Add(transform.position);

        // G: RoofSliding 全期間を Kinematic 強制移動で roof plane 拘束
        if (_state == State.RoofSliding && _roofCollider != null)
        {
            Vector3 rNormal = _roofCollider.transform.up;
            if (!_rb.isKinematic) _rb.isKinematic = true;

            // _roofCenter（Spawner から渡した bounds.center）を基準に roof plane を定義
            // transform.position を roof plane 上に投影（法線成分を除去）
            Vector3 toPos = transform.position - _roofCenter;
            Vector3 onPlane = _roofCenter + Vector3.ProjectOnPlane(toPos, rNormal);

            // downhill 方向に SlideMinSpeed で移動
            Vector3 v = Vector3.ProjectOnPlane(_roofDownhill, rNormal).normalized * SlideMinSpeed;
            Vector3 nextPos = onPlane + v * Time.fixedDeltaTime;
            // 法線方向に 0.075f オフセットして surface 上に乗せる
            transform.position = nextPos + rNormal * 0.075f;

            if (age < Time.fixedDeltaTime * 2f)
                UnityEngine.Debug.Log($"[ROOF_SLIDE_FORCE] kinematic_slide_started pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) v=({v.x:F2},{v.y:F2},{v.z:F2}) downhillSpeed={Vector3.Dot(v, _roofDownhill):F2} minSpeed={SlideMinSpeed}");
        }
    }

    void Update()
    {
        if (_state == State.RoofSliding)
        {
            // edge-based のみで Falling に切り替え（time-based は除去）
            float tVal = Vector3.Dot(transform.position - _roofCenter, _roofDownhill);
            if (tVal > _roofEdgeTEnd)
                SwitchToFalling();
            DrawSlideVisualization();
            return;
        }
        if (_state == State.Falling)
        {
            if ((Time.time - _startTime) >= fallTimeoutSeconds)
            {
                ReturnFromFall("Timeout");
                return;
            }
            float age = Time.time - _startTime;
            if (age < 0.5f) { _roofStuckTimer = 0f; _roofStuckLogged = false; return; }
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
        DrawSlideVisualization();
    }

    const float DrawScale = 0.5f;
    void DrawSlideVisualization()
    {
        if ((_state != State.RoofSliding && _state != State.Falling) || _rb == null) return;
        Vector3 p = transform.position;
        float age = Time.time - _startTime;
        if (age > 1f && _trajectoryPoints.Count == 0) return;

        Vector3 v = _rb.linearVelocity;
        if (v.sqrMagnitude > 0.001f)
            Debug.DrawLine(p, p + v.normalized * DrawScale, Color.red, 0.5f);
        if (_roofDownhill.sqrMagnitude > 0.001f)
            Debug.DrawLine(p, p + _roofDownhill.normalized * DrawScale, Color.green, 0.5f);
        Debug.DrawLine(p, p + Vector3.down * DrawScale * 0.5f, Color.yellow, 0.5f);
        for (int i = 1; i < _trajectoryPoints.Count; i++)
            Debug.DrawLine(_trajectoryPoints[i - 1], _trajectoryPoints[i], Color.cyan, 2f);
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

    int _roofCollisionStayFrames;

    void OnCollisionEnter(Collision collision)
    {
        if (this == null || gameObject == null || collision == null || collision.collider == null) return;
        if (_roofCollider != null && collision.collider == _roofCollider)
        {
            if (_roofContactStartTime < 0f) _roofContactStartTime = Time.time;
            _roofContactEndTime = Time.time;
            _roofCollisionStayFrames = 1;
            UnityEngine.Debug.Log($"[ROOF_COLLISION_REAL] ON_COLLISION_ENTER fired with roofCollider pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({_rb?.linearVelocity.x:F2},{_rb?.linearVelocity.y:F2},{_rb?.linearVelocity.z:F2}) state={_state}");
            return;
        }
        // 屋根と思われる別コライダーとの衝突も記録（layer/name で判定）
        string hitName = collision.collider.name ?? "";
        bool hitRoofLike = hitName.Contains("Roof") || hitName.Contains("roof") || hitName.Contains("Slide");
        if (hitRoofLike)
            UnityEngine.Debug.Log($"[ROOF_COLLISION_REAL] ON_COLLISION_ENTER hit roof-like object={hitName} (not _roofCollider ref) state={_state} pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2})");
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

    void OnCollisionStay(Collision collision)
    {
        if (collision == null || collision.collider == null || _roofCollider == null) return;
        if (collision.collider == _roofCollider)
        {
            _roofContactEndTime = Time.time;
            _roofCollisionStayFrames++;
            if (_roofCollisionStayFrames == 5 || _roofCollisionStayFrames == 15)
                UnityEngine.Debug.Log($"[ROOF_COLLISION_REAL] ON_COLLISION_STAY frames={_roofCollisionStayFrames} pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) vel=({_rb?.linearVelocity.x:F2},{_rb?.linearVelocity.y:F2},{_rb?.linearVelocity.z:F2})");
        }
    }

    void OnCollisionExit(Collision collision)
    {
        if (collision == null || collision.collider == null || _roofCollider == null) return;
        if (collision.collider == _roofCollider && _roofContactStartTime >= 0f)
        {
            float dur = _roofContactEndTime - _roofContactStartTime;
            UnityEngine.Debug.Log($"[SLIDE_VISUALIZATION] roof_contact_lost duration_seconds={dur:F2} pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2})");
        }
    }

    void LandNow()
    {
        if (_state != State.Falling) return;
        _state = State.Grounded;
        ReleaseFollowers("landed");
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
        ReleaseFollowers(reason);
        UnityEngine.Debug.Log($"[SNOW_DESTROY_CALL] reason={reason} state={_state} pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) go={gameObject.name}");
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
