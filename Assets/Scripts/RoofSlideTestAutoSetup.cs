using UnityEngine;
using System.Collections;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// One-house slide test helper for Avalanche_Test_OneHouse.
/// Attach this to DebugTools and run context menus.
/// </summary>
public class RoofSlideTestAutoSetup : MonoBehaviour
{
    const string VersionStamp = "v2026-03-02-XX";
    [Header("Targets")]
    public Transform targetRoof;
    public Transform targetSnowTest;

    [Header("Roof slide collider")]
    public string slideColliderName = "RoofSlideCollider";
    public float roofPaddingXZ = 0.02f;
    public float roofColliderFixedThickness = 0.2f;
    public float roofColliderCenterY = 0.1f;
    [Range(1f, 10f)] public float roofSlideLengthMultiplier = 5f;
    public float roofSlideMinLengthMeters = 10f;
    public string roofSlideLayerName = "RoofSlide";
    public bool createSideStoppers = false;
    public float stopperThickness = 0.06f;
    public float stopperHeight = 0.45f;
    public float stopperDepthPadding = 0.06f;

    [Header("SnowTest rigidbody")]
    public float linearDamping = 0.05f;
    public float spawnLiftFromRoof = 0.15f;
    public float demoDropHeight = 0.15f;
    public float maxRoofDistanceBeforeRespawn = 30f;
    public bool freezeRotationForSlide = true;
    [Range(0.2f, 5f)] public float slideAssistAccel = 1.0f;
    public bool followSnowTestForDemo = false;
    public bool keepInitialCameraViewOnPlay = true;
    public Vector3 demoCameraOffset = new Vector3(-8f, 6f, -8f);
    public bool restoreCameraAfterDemo = true;
    public bool startOnRoofNoDrop = true;

    [Header("Auto run")]
    public bool autoRunOnPlay = true;
    public bool autoCreateSnowTestIfMissing = true;
    public bool autoCreateDebugToolsIfMissing = true;

    [Header("Natural snow loop")]
    public bool enableNaturalSnowLoop = true;
    public float spawnIntervalSeconds = 0.8f;
    public bool applyLoopConfigFromSetup = false;
    public float addPerLanding = 0.18f;
    public float baseThreshold = 0.30f;
    public float slopeFactor = 0.25f;
    public float burstSpeed = 1.0f;
    public float stickKick = 0.15f;
    public float burstDuration = 0.25f;
    public float loadDropOnBurst = 0.35f;
    public bool forceAvalancheNow = false;

    [Header("Physics material")]
    public string physicsMaterialAssetPath = "Assets/Materials/Physics/SnowSlidePhysic.mat";

    BoxCollider _roofSlideCollider;
    bool _hasAutoRunThisPlay;
    bool _initialized;
    bool _initializedDone;
    bool _setupInProgress;
    bool _enterFromSetupCalled;
    int _runId;
    bool _setupPositionWritten;
    Coroutine _logStateAfterDelayCoroutine;
    Coroutine _naturalSnowLoopCoroutine;
    bool _loggedLoopConfigBypass;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureRuntimeBootstrap()
    {
        var setup = FindFirstObjectByType<RoofSlideTestAutoSetup>();
        if (setup == null)
        {
            var go = GameObject.Find("DebugTools");
            if (go == null) go = new GameObject("DebugTools");
            setup = go.GetComponent<RoofSlideTestAutoSetup>();
            if (setup == null) setup = go.AddComponent<RoofSlideTestAutoSetup>();
            Debug.Log("[RoofSlideAuto] DebugTools/RoofSlideTestAutoSetup が無かったため自動作成しました。");
        }

        if (setup.targetRoof == null)
            setup.TryAutoAssignRoof();
        if (setup.targetSnowTest == null)
            setup.TryAutoAssignSnowTest();
    }

    void Start()
    {
        if (!Application.isPlaying) return;
        if (_initialized) return;
        _initialized = true;
        Debug.Log($"[RoofSlideVersion] RoofSlideTestAutoSetup {VersionStamp} running");
        StartCoroutine(LogFirstFrames10());
        if (!autoRunOnPlay || _hasAutoRunThisPlay) return;
        _hasAutoRunThisPlay = true;
        RunSetupNow();
        StartNaturalSnowLoopIfNeeded();
    }

    [ContextMenu("Setup All (A/B/C)")]
    public void SetupAll()
    {
        CreateOrUpdateRoofSlideCollider();
        ApplySnowSlidePhysicsMaterial();
        SetupSnowTestForSlide();
        ApplySpawnCorrection();
    }

    [ContextMenu("Run Setup + Diagnostics Now")]
    public void RunSetupNow()
    {
        if (_setupInProgress || _initializedDone) return;
        _setupInProgress = true;
        _runId++;
        _setupPositionWritten = false;
        var report = PreflightCheckAndFix();
        if (!report.okToContinue)
        {
            _setupInProgress = false;
            Debug.LogError("[RoofSlidePreflight] targetRoof が未設定のため停止。DebugTools の targetRoof を設定してください。");
            return;
        }

        SetupAll();
        EnsureRuntimeSafetyAndKickDemo(report.snow, report.roofCol);
        if (_logStateAfterDelayCoroutine != null)
            StopCoroutine(_logStateAfterDelayCoroutine);
        _logStateAfterDelayCoroutine = StartCoroutine(LogStateAfterDelayRealtime(3f, report.snow, report.roofCol, _runId));
        _initializedDone = true;
        _setupInProgress = false;
        StartNaturalSnowLoopIfNeeded();
    }

    [ContextMenu("Create/Update Roof Slide Collider")]
    public void CreateOrUpdateRoofSlideCollider()
    {
        if (targetRoof == null) return;

        var existing = targetRoof.Find(slideColliderName);
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(slideColliderName);
            go.transform.SetParent(targetRoof, false);
        }

        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        DebugSnowVisibility.LogRotationOverrideExecuted("RoofSlideTestAutoSetup.cs", 162, go.name);

        var box = go.GetComponent<BoxCollider>();
        if (box == null) box = go.AddComponent<BoxCollider>();
        _roofSlideCollider = box;
        int roofSlideLayer = EnsureRoofSlideLayerAndGetIndex();
        if (roofSlideLayer >= 0)
            go.layer = roofSlideLayer;

        FitBoxColliderToRoofRenderers(targetRoof, box, roofPaddingXZ, roofColliderFixedThickness, roofColliderCenterY, roofSlideLengthMultiplier, roofSlideMinLengthMeters);
        if (createSideStoppers)
            CreateOrUpdateSideStoppers(box);
        else
            RemoveSideStoppers(box.transform);
    }

    [ContextMenu("Apply Snow Slide Physic Material")]
    public void ApplySnowSlidePhysicsMaterial()
    {
        var mat = GetOrCreateSnowSlidePhysicMaterial();
        if (mat == null) return;

        if (_roofSlideCollider == null && targetRoof != null)
        {
            var t = targetRoof.Find(slideColliderName);
            if (t != null) _roofSlideCollider = t.GetComponent<BoxCollider>();
        }
        if (_roofSlideCollider != null)
            _roofSlideCollider.material = mat;

        var snowCol = ResolveSnowTestCollider();
        if (snowCol != null)
            snowCol.material = mat;
    }

    [ContextMenu("Setup SnowTest For Slide")]
    public void SetupSnowTestForSlide()
    {
        var snow = ResolveSnowTestTransform();
        if (snow == null) return;

        var rb = snow.GetComponent<Rigidbody>();
        if (rb == null) rb = snow.gameObject.AddComponent<Rigidbody>();
        rb.constraints = freezeRotationForSlide
            ? (RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ)
            : RigidbodyConstraints.None;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = linearDamping;
        rb.sleepThreshold = 0f;
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.WakeUp();

        var col = snow.GetComponent<Collider>();
        if (col == null) col = snow.gameObject.AddComponent<BoxCollider>();
        ForceSnowVisible(snow);

        if (_roofSlideCollider == null && targetRoof != null)
        {
            var t = targetRoof.Find(slideColliderName);
            if (t != null) _roofSlideCollider = t.GetComponent<BoxCollider>();
        }
        if (_roofSlideCollider == null) return;
        ApplySpawnCorrection();
        ConfigureSlideAssist(snow, _roofSlideCollider);
    }

    void OnDrawGizmosSelected()
    {
        if (_roofSlideCollider == null && targetRoof != null)
        {
            var t = targetRoof.Find(slideColliderName);
            if (t != null) _roofSlideCollider = t.GetComponent<BoxCollider>();
        }

        if (_roofSlideCollider != null)
        {
            Gizmos.color = new Color(0.2f, 1f, 1f, 0.5f);
            Matrix4x4 prev = Gizmos.matrix;
            Gizmos.matrix = _roofSlideCollider.transform.localToWorldMatrix;
            Gizmos.DrawWireCube(_roofSlideCollider.center, _roofSlideCollider.size);
            Gizmos.matrix = prev;

            Vector3 roofUp = _roofSlideCollider.transform.up.normalized;
            Vector3 topCenterLocal = _roofSlideCollider.center + Vector3.up * (_roofSlideCollider.size.y * 0.5f);
            Vector3 topCenterWorld = _roofSlideCollider.transform.TransformPoint(topCenterLocal);
            Vector3 dir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Vector3.ProjectOnPlane(-_roofSlideCollider.transform.forward, roofUp).normalized;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(topCenterWorld, topCenterWorld + dir * 0.8f);
            Gizmos.DrawSphere(topCenterWorld, 0.025f);
        }

        var snow = ResolveSnowTestTransform();
        if (snow != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 origin = snow.position + Vector3.up * 0.02f;
            Vector3 end = origin + Vector3.down * 1.2f;
            Gizmos.DrawLine(origin, end);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 1.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                Gizmos.DrawSphere(hit.point, 0.03f);
            }
        }
    }

    Transform ResolveSnowTestTransform()
    {
        if (targetSnowTest != null) return targetSnowTest;
        var byName = GameObject.Find("SnowTest");
        if (byName != null) return byName.transform;
        var cube = FindFirstObjectByType<SnowTestCube>();
        if (cube != null) return cube.transform;
        return null;
    }

    Collider ResolveSnowTestCollider()
    {
        var t = ResolveSnowTestTransform();
        return t != null ? t.GetComponent<Collider>() : null;
    }

    static void FitBoxColliderToRoofRenderers(Transform roofRoot, BoxCollider box, float paddingXZ, float fixedThickness, float fixedCenterY, float lengthMultiplier, float minLengthMeters)
    {
        var renderers = roofRoot.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            box.transform.localPosition = Vector3.zero;
            box.size = new Vector3(2f, Mathf.Max(0.02f, fixedThickness), Mathf.Max(2f * Mathf.Max(1f, lengthMultiplier), minLengthMeters));
            box.center = new Vector3(0f, fixedCenterY, 0f);
            return;
        }

        bool initialized = false;
        Vector3 localMin = Vector3.zero;
        Vector3 localMax = Vector3.zero;

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled) continue;
            Bounds wb = r.bounds;
            if (wb.size.sqrMagnitude < 0.000001f) continue;

            Vector3 c = wb.center;
            Vector3 e = wb.extents;
            var corners = new Vector3[8]
            {
                new Vector3(c.x-e.x, c.y-e.y, c.z-e.z),
                new Vector3(c.x-e.x, c.y-e.y, c.z+e.z),
                new Vector3(c.x-e.x, c.y+e.y, c.z-e.z),
                new Vector3(c.x-e.x, c.y+e.y, c.z+e.z),
                new Vector3(c.x+e.x, c.y-e.y, c.z-e.z),
                new Vector3(c.x+e.x, c.y-e.y, c.z+e.z),
                new Vector3(c.x+e.x, c.y+e.y, c.z-e.z),
                new Vector3(c.x+e.x, c.y+e.y, c.z+e.z),
            };

            for (int k = 0; k < corners.Length; k++)
            {
                Vector3 local = roofRoot.InverseTransformPoint(corners[k]);
                if (!initialized)
                {
                    localMin = local;
                    localMax = local;
                    initialized = true;
                }
                else
                {
                    localMin = Vector3.Min(localMin, local);
                    localMax = Vector3.Max(localMax, local);
                }
            }
        }

        if (!initialized)
        {
            box.transform.localPosition = Vector3.zero;
            box.size = new Vector3(2f, Mathf.Max(0.02f, fixedThickness), Mathf.Max(2f * Mathf.Max(1f, lengthMultiplier), minLengthMeters));
            box.center = new Vector3(0f, fixedCenterY, 0f);
            return;
        }

        Vector3 size = localMax - localMin;
        size.x = Mathf.Max(0.1f, size.x + paddingXZ * 2f);
        size.z = Mathf.Max(0.1f, size.z + paddingXZ * 2f);
        size.y = Mathf.Max(0.02f, fixedThickness);
        size.z *= Mathf.Max(1f, lengthMultiplier);

        Vector3 boundsCenter = (localMin + localMax) * 0.5f;
        box.transform.localPosition = boundsCenter;
        box.transform.localRotation = Quaternion.identity;
        DebugSnowVisibility.LogRotationOverrideExecuted("RoofSlideTestAutoSetup.cs", 356, box.name);
        box.center = new Vector3(0f, fixedCenterY, 0f);
        box.size = size;
        ExtendColliderDownhill(box, roofRoot, minLengthMeters);
        box.isTrigger = false;
    }

    static void ExtendColliderDownhill(BoxCollider box, Transform roofRoot, float minLengthMeters)
    {
        if (box == null || roofRoot == null) return;
        float requiredLen = Mathf.Max(0.1f, minLengthMeters);

        Vector3 roofUp = roofRoot.up.normalized;
        Vector3 slideDirWorld = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (slideDirWorld.sqrMagnitude < 0.0001f) return;
        Vector3 slideLocal = roofRoot.InverseTransformDirection(slideDirWorld).normalized;

        Vector3 size = box.size;
        Vector3 center = box.center;

        if (Mathf.Abs(slideLocal.z) >= Mathf.Abs(slideLocal.x))
        {
            float oldLen = size.z;
            float newLen = Mathf.Max(oldLen, requiredLen);
            float delta = newLen - oldLen;
            size.z = newLen;
            center.z += Mathf.Sign(slideLocal.z) * (delta * 0.5f);
        }
        else
        {
            float oldLen = size.x;
            float newLen = Mathf.Max(oldLen, requiredLen);
            float delta = newLen - oldLen;
            size.x = newLen;
            center.x += Mathf.Sign(slideLocal.x) * (delta * 0.5f);
        }

        box.size = size;
        box.center = center;
    }

    void ApplySpawnCorrection()
    {
        if (!_setupInProgress && !_initializedDone) return;
        var snow = ResolveSnowTestTransform();
        if (snow == null) return;

        if (_roofSlideCollider == null && targetRoof != null)
        {
            var t = targetRoof.Find(slideColliderName);
            if (t != null) _roofSlideCollider = t.GetComponent<BoxCollider>();
        }
        if (_roofSlideCollider == null) return;

        Vector3 before = snow.position;
        Vector3 spawnPos = ComputeSafeTopSpawnPosition(_roofSlideCollider, out bool hasHit, out RaycastHit hit);
        WriteSetupPositionOnce(snow, "SpawnFix", spawnPos);

        var rb = snow.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }

        bool spawnAboveTop = spawnPos.y >= (_roofSlideCollider.bounds.max.y + spawnLiftFromRoof - 0.0001f);
        string hitName = hasHit && hit.collider != null ? hit.collider.name : "None";
        Vector3 hitNormal = hasHit ? hit.normal : Vector3.zero;
        Debug.Log($"[RoofSlideSpawn] b.min={_roofSlideCollider.bounds.min} b.max={_roofSlideCollider.bounds.max} spawnPos={spawnPos} hit={hitName} hitNormal={hitNormal} spawnAboveTop={spawnAboveTop} before={before}");
    }

    struct PreflightReport
    {
        public bool okToContinue;
        public Transform snow;
        public BoxCollider roofCol;
    }

    PreflightReport PreflightCheckAndFix()
    {
        if (targetRoof == null)
            TryAutoAssignRoof();
        if (targetSnowTest == null)
            TryAutoAssignSnowTest();

        bool hasDebugTools = GameObject.Find("DebugTools") != null;
        bool hasSetup = FindFirstObjectByType<RoofSlideTestAutoSetup>() != null;

        if (targetRoof == null)
        {
            Debug.LogError($"[RoofSlidePreflight] DebugTools={hasDebugTools} RoofSlideTestAutoSetup={hasSetup} targetRoof=NULL");
            return new PreflightReport { okToContinue = false };
        }

        CreateOrUpdateRoofSlideCollider();
        if (_roofSlideCollider == null)
        {
            Debug.LogError("[RoofSlidePreflight] RoofSlideCollider の作成/取得に失敗");
            return new PreflightReport { okToContinue = false };
        }

        var snow = ResolveSnowTestTransform();
        if (snow == null && autoCreateSnowTestIfMissing)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "SnowTest";
            go.transform.localScale = Vector3.one * 0.25f;
            snow = go.transform;
            targetSnowTest = snow;
            Debug.Log("[RoofSlidePreflight] SnowTest が無かったため自動生成しました。");
        }

        if (snow == null)
        {
            Debug.LogError("[RoofSlidePreflight] SnowTest が見つからず、自動生成も無効のため停止");
            return new PreflightReport { okToContinue = false };
        }

        ForceSnowVisible(snow);

        var rb = snow.GetComponent<Rigidbody>();
        if (rb == null) rb = snow.gameObject.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.linearDamping = linearDamping;
        rb.sleepThreshold = 0f;
        rb.constraints = freezeRotationForSlide
            ? (RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ)
            : RigidbodyConstraints.None;
        rb.WakeUp();
        ConfigureSlideAssist(snow, _roofSlideCollider);

        if (Time.timeScale == 0f)
        {
            Time.timeScale = 1f;
            Debug.LogWarning("[RoofSlidePreflight] Time.timeScale=0 を 1 に修正");
        }
        if (Physics.gravity.sqrMagnitude < 0.0001f)
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Debug.LogWarning("[RoofSlidePreflight] Physics.gravity が 0 だったためデフォルトに修正");
        }

        float dist = Vector3.Distance(snow.position, targetRoof.position);
        if (dist > maxRoofDistanceBeforeRespawn)
        {
            Debug.LogWarning($"[RoofSlidePreflight] SnowTest が遠すぎるため再配置 (dist={dist:F2})");
            ApplySpawnCorrection();
        }

        // 屋根面レイキャストで上面へ補正（裏面/外れ防止）
        var roofUp = _roofSlideCollider.transform.up;
        Vector3 rayStart = targetRoof.position + roofUp * 10f;
        if (Physics.Raycast(rayStart, -roofUp, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider == _roofSlideCollider)
            {
                var snapPos = hit.point + roofUp * spawnLiftFromRoof;
                WriteSetupPositionOnce(snow, "SpawnFix", snapPos);
            }
        }
        else
        {
            ApplySpawnCorrection();
        }

        int snowLayer = snow.gameObject.layer;
        int roofLayer = _roofSlideCollider.gameObject.layer;
        if (Physics.GetIgnoreLayerCollision(snowLayer, roofLayer))
        {
            Physics.IgnoreLayerCollision(snowLayer, roofLayer, false);
            Debug.LogWarning($"[RoofSlidePreflight] LayerCollision ignore を解除 ({LayerMask.LayerToName(snowLayer)} x {LayerMask.LayerToName(roofLayer)})");
        }

        Debug.Log($"[RoofSlidePreflight] DebugTools={hasDebugTools} Setup={hasSetup} targetRoof={targetRoof.name} SnowTest={snow.name} rbGravity={rb.useGravity} rbKinematic={rb.isKinematic} constraints={rb.constraints} roofCol={_roofSlideCollider.name}");

        return new PreflightReport
        {
            okToContinue = true,
            snow = snow,
            roofCol = _roofSlideCollider
        };
    }

    void EnsureRuntimeSafetyAndKickDemo(Transform snow, BoxCollider roofCol)
    {
        if (!_setupInProgress && !_initializedDone) return;
        if (snow == null || roofCol == null) return;
        var rb = snow.GetComponent<Rigidbody>();
        if (rb == null) rb = snow.gameObject.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.sleepThreshold = 0f;
        rb.WakeUp();
        ConfigureSlideAssist(snow, roofCol);

        Vector3 spawnPos = ComputeSafeTopSpawnPosition(roofCol, out bool hasHit, out RaycastHit hit);
        spawnPos.y += Mathf.Max(0f, demoDropHeight - spawnLiftFromRoof);
        WriteSetupPositionOnce(snow, "SpawnFix", spawnPos);

        Camera cam = Camera.main;
        if (!keepInitialCameraViewOnPlay && followSnowTestForDemo && cam != null)
            StartCoroutine(FollowCameraForDemo(cam, snow, 3f));

        bool hasRenderer = false;
        foreach (var r in snow.GetComponentsInChildren<Renderer>(true))
        {
            if (r.enabled) { hasRenderer = true; break; }
        }
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        bool inView = IsInView(cam, snow.position);
        string hitName = hasHit && hit.collider != null ? hit.collider.name : "None";
        Vector3 hitNormal = hasHit ? hit.normal : Vector3.zero;
        bool spawnAboveTop = snow.position.y >= (roofCol.bounds.max.y + spawnLiftFromRoof - 0.0001f);
        Debug.Log($"[RoofSlideSpawn] b.min={roofCol.bounds.min} b.max={roofCol.bounds.max} spawnPos={snow.position} hit={hitName} hitNormal={hitNormal} spawnAboveTop={spawnAboveTop}");
        Debug.Log($"[RoofSlideDemo] snowPos={snow.position} camPos={camPos} inView={inView} layer={LayerMask.LayerToName(snow.gameObject.layer)} active={snow.gameObject.activeInHierarchy} renderer={hasRenderer}");
    }

    IEnumerator StartOnRoofNoDropVerification(Transform snow, BoxCollider roofCol, Vector3 fixedPos, float seconds)
    {
        if (snow == null) yield break;
        var rb = snow.GetComponent<Rigidbody>();
        var col = snow.GetComponent<Collider>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.useGravity = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
        // Keep renderer/collider as-is to avoid visible pop/disappear.
        if (col != null) col.enabled = true;

        // Apply initial snap once only (no per-frame teleport).
        var assist = snow.GetComponent<SnowTestSlideAssist>();
        WriteSetupPositionOnce(snow, "SetupLock", fixedPos);
        Debug.Log($"[RoofSlideSpawnFix] appliedOnce=true pos={fixedPos}");
        yield return new WaitForSeconds(seconds);

        if (col != null) col.enabled = true;
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.WakeUp();
        }

        // 解除直後の最初の1フレームで必ず屋根面に吸着
        if (rb != null && roofCol != null)
        {
            Vector3 roofUp = roofCol.transform.up;
            Vector3 closest = roofCol.ClosestPoint(snow.position);
            var snapPos = closest + roofUp * 0.02f;
            WriteSetupPositionOnce(snow, "SetupLock", snapPos);
            rb.isKinematic = true;
        }

        // 0.5秒固定解除直後に、Setup起点でSlideModeへ必ず1回入れる
        if (!_enterFromSetupCalled)
        {
            _enterFromSetupCalled = true;
            var assistForEnter = snow.GetComponent<SnowTestSlideAssist>();
            if (assistForEnter != null)
                assistForEnter.EnterSlideModeFromSetup(roofCol, roofCol.transform.up);
            bool colEnabledNow = col != null && col.enabled;
            bool rbKinNow = rb != null && rb.isKinematic;
            Debug.Log($"[RoofSlideEnterFromSetup] called=true roofCol={(roofCol != null ? roofCol.name : "None")} roofUp={(roofCol != null ? roofCol.transform.up : Vector3.up)} rbKin={rbKinNow} colEnabled={colEnabledNow}");
        }
        StartCoroutine(LogFramesPostUnlock(snow, 10));
    }

    IEnumerator LogFirstFrames10()
    {
        for (int i = 0; i < 10; i++)
        {
            var snow = ResolveSnowTestTransform();
            bool active = snow != null && snow.gameObject.activeInHierarchy;
            bool rend = false;
            bool colEnabled = false;
            bool rbKin = false;
            bool rbGrav = false;
            Vector3 vel = Vector3.zero;
            Vector3 angVel = Vector3.zero;
            Vector3 pos = snow != null ? snow.position : Vector3.zero;
            if (snow != null)
            {
                foreach (var r in snow.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { rend = true; break; }
                var c = snow.GetComponent<Collider>();
                if (c != null) colEnabled = c.enabled;
                var rb = snow.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rbKin = rb.isKinematic;
                    rbGrav = rb.useGravity;
                    vel = rb.linearVelocity;
                    angVel = rb.angularVelocity;
                }
            }
            Debug.Log($"[RoofSlideFrame] f={i} pos={pos} active={active} rend={rend} colEnabled={colEnabled} rbKin={rbKin} rbGrav={rbGrav} vel={vel} angVel={angVel}");
            yield return null;
        }
    }

    Vector3 ComputeSafeTopSpawnPosition(BoxCollider roofCol, out bool hasHit, out RaycastHit hit)
    {
        Bounds b = roofCol.bounds;
        Vector3 topCenter = new Vector3(b.center.x, b.max.y + spawnLiftFromRoof, b.center.z);
        Vector3 origin = topCenter + Vector3.up * 2.0f;
        int mask = 1 << roofCol.gameObject.layer;
        hasHit = Physics.Raycast(origin, Vector3.down, out hit, 10f, mask, QueryTriggerInteraction.Ignore);

        Vector3 spawnPos = topCenter;
        if (hasHit && hit.collider == roofCol)
            spawnPos = hit.point + Vector3.up * spawnLiftFromRoof;

        if (spawnPos.y < b.center.y)
            spawnPos.y = b.max.y + spawnLiftFromRoof;

        return spawnPos;
    }

    IEnumerator LogStateAfterDelayRealtime(float sec, Transform snow, BoxCollider roofCol, int runId)
    {
        yield return new WaitForSecondsRealtime(sec);
        if (runId != _runId) yield break;
        if (snow == null) yield break;

        var rb = snow.GetComponent<Rigidbody>();
        var assist = snow.GetComponent<SnowTestSlideAssist>();
        Vector3 vel = rb != null ? rb.linearVelocity : Vector3.zero;
        bool isKinematic = rb != null && rb.isKinematic;
        bool isSleeping = rb != null && rb.IsSleeping();
        string contactName = assist != null ? assist.LastContactName : "None";
        bool contactOnRoof = assist != null && assist.LastGroundedOnRoof;
        Bounds roofBounds = roofCol != null ? roofCol.bounds : default;
        bool outOfBounds = roofCol == null
            || snow.position.x < roofBounds.min.x || snow.position.x > roofBounds.max.x
            || snow.position.y < roofBounds.min.y || snow.position.y > roofBounds.max.y
            || snow.position.z < roofBounds.min.z || snow.position.z > roofBounds.max.z;
        Vector3 roofUp = (roofCol != null) ? roofCol.transform.up.normalized : Vector3.up;
        float planeSpeed = Vector3.ProjectOnPlane(vel, roofUp).magnitude;
        Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, roofUp).normalized;
        if (slideDir.sqrMagnitude < 0.0001f && roofCol != null)
            slideDir = Vector3.ProjectOnPlane(-roofCol.transform.forward, roofUp).normalized;
        float slideSupport = Mathf.Abs(slideDir.x) * roofBounds.extents.x
                           + Mathf.Abs(slideDir.y) * roofBounds.extents.y
                           + Mathf.Abs(slideDir.z) * roofBounds.extents.z;
        float slidePos = Vector3.Dot(snow.position - roofBounds.center, slideDir);
        bool reachedSlideEdge = slidePos >= (slideSupport - 0.02f);
        bool outOfBoundsAny = outOfBounds || reachedSlideEdge;
        Debug.Log($"[SlideProto3s] runId={runId} pos={snow.position} vel={vel} speed={vel.magnitude:F3} planeSpeed={planeSpeed:F3} isKinematic={isKinematic} isSleeping={isSleeping} contactName={contactName} contactOnRoof={contactOnRoof}");
        Debug.Log($"[SlideRoofBounds] runId={runId} pos={snow.position} bmin={roofBounds.min} bmax={roofBounds.max} outOfBounds={outOfBoundsAny} outByPos={outOfBounds} outBySlideEdge={reachedSlideEdge}");
        _logStateAfterDelayCoroutine = null;
    }

    IEnumerator LogFramesPostUnlock(Transform snow, int frames)
    {
        for (int i = 0; i < frames; i++)
        {
            if (snow == null) yield break;
            var rb = snow.GetComponent<Rigidbody>();
            var col = snow.GetComponent<Collider>();
            var assist = snow.GetComponent<SnowTestSlideAssist>();
            bool rend = false;
            foreach (var r in snow.GetComponentsInChildren<Renderer>(true))
                if (r.enabled) { rend = true; break; }
            bool rbKin = rb != null && rb.isKinematic;
            bool colEnabled = col != null && col.enabled;
            float offDist = assist != null ? assist.LastOffDist : -1f;
            string contactName = assist != null ? assist.LastContactName : "None";
            Debug.Log($"[RoofSlideFramePostUnlock] f={i} rbKin={rbKin} rendererEnabled={rend} colliderEnabled={colEnabled} localScale={snow.localScale} position={snow.position} offDist={offDist:F3} contactName={contactName}");
            yield return null;
        }
    }

    void CreateOrUpdateSideStoppers(BoxCollider roofCol)
    {
        if (roofCol == null) return;
        var root = roofCol.transform;

        var left = root.Find("RoofSlideStopper_L");
        if (left == null)
        {
            var go = new GameObject("RoofSlideStopper_L");
            go.transform.SetParent(root, false);
            left = go.transform;
        }

        var right = root.Find("RoofSlideStopper_R");
        if (right == null)
        {
            var go = new GameObject("RoofSlideStopper_R");
            go.transform.SetParent(root, false);
            right = go.transform;
        }

        SetupStopperCollider(left, roofCol, -1f);
        SetupStopperCollider(right, roofCol, 1f);
    }

    static void RemoveSideStoppers(Transform root)
    {
        if (root == null) return;
        var left = root.Find("RoofSlideStopper_L");
        var right = root.Find("RoofSlideStopper_R");
        if (left != null) Destroy(left.gameObject);
        if (right != null) Destroy(right.gameObject);
    }

    void SetupStopperCollider(Transform stopper, BoxCollider roofCol, float sideSign)
    {
        stopper.localRotation = Quaternion.identity;
        DebugSnowVisibility.LogRotationOverrideExecuted("RoofSlideTestAutoSetup.cs", 775, stopper.name);
        var c = stopper.GetComponent<BoxCollider>();
        if (c == null) c = stopper.gameObject.AddComponent<BoxCollider>();
        c.isTrigger = false;

        Vector3 roofSize = roofCol.size;
        Vector3 roofCenter = roofCol.center;
        float halfX = roofSize.x * 0.5f;
        float halfY = roofSize.y * 0.5f;

        c.size = new Vector3(Mathf.Max(0.02f, stopperThickness), Mathf.Max(roofSize.y + 0.05f, stopperHeight), roofSize.z + stopperDepthPadding);
        stopper.localPosition = new Vector3(
            roofCenter.x + sideSign * (halfX + c.size.x * 0.5f),
            roofCenter.y + halfY + c.size.y * 0.5f - 0.02f,
            roofCenter.z
        );
    }

    void ConfigureSlideAssist(Transform snow, Collider roofCol)
    {
        if (snow == null || roofCol == null) return;
        var rb = snow.GetComponent<Rigidbody>();
        if (rb == null) return;
        var assist = snow.GetComponent<SnowTestSlideAssist>();
        if (assist == null) assist = snow.gameObject.AddComponent<SnowTestSlideAssist>();
        assist.rb = rb;
        assist.roofSlideCollider = roofCol;
        if (applyLoopConfigFromSetup)
        {
            assist.ApplyLoopConfig(
                nameof(RoofSlideTestAutoSetup),
                addPerLanding,
                baseThreshold,
                slopeFactor,
                burstSpeed,
                stickKick,
                burstDuration,
                loadDropOnBurst
            );
        }
        else if (!_loggedLoopConfigBypass)
        {
            _loggedLoopConfigBypass = true;
            Debug.Log("[SnowLoopConfigSource] applyLoopConfigFromSetup=false (SnowTestSlideAssist inspector values are used)");
        }
    }

    void WriteSetupPositionOnce(Transform snow, string writer, Vector3 pos)
    {
        if (snow == null) return;
        if (_setupPositionWritten) return;
        var assist = snow.GetComponent<SnowTestSlideAssist>();
        if (assist != null) assist.RegisterExternalPositionWrite(writer, pos);
        else snow.position = pos;
        _setupPositionWritten = true;
    }

    void StartNaturalSnowLoopIfNeeded()
    {
        if (!Application.isPlaying) return;
        if (!enableNaturalSnowLoop) return;
        if (_naturalSnowLoopCoroutine != null) return;
        _naturalSnowLoopCoroutine = StartCoroutine(NaturalSnowLoop());
    }

    IEnumerator NaturalSnowLoop()
    {
        float interval = Mathf.Clamp(spawnIntervalSeconds, 0.7f, 1.0f);
        var wait = new WaitForSecondsRealtime(interval);
        float nextSpawnAt = Time.unscaledTime + interval;
        while (Application.isPlaying && enableNaturalSnowLoop)
        {
            if (_roofSlideCollider == null && targetRoof != null)
            {
                var t = targetRoof.Find(slideColliderName);
                if (t != null) _roofSlideCollider = t.GetComponent<BoxCollider>();
            }

            var snow = ResolveSnowTestTransform();
            if (snow == null && autoCreateSnowTestIfMissing)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "SnowTest";
                go.transform.localScale = Vector3.one * 0.25f;
                snow = go.transform;
                targetSnowTest = snow;
                SetupSnowTestForSlide();
                ApplySnowSlidePhysicsMaterial();
            }

            if (snow != null && _roofSlideCollider != null)
            {
                var assist = snow.GetComponent<SnowTestSlideAssist>();
                if (assist == null)
                {
                    ConfigureSlideAssist(snow, _roofSlideCollider);
                    assist = snow.GetComponent<SnowTestSlideAssist>();
                }
                if (assist != null)
                    assist.SetNextSpawnInDebug(Mathf.Max(0f, nextSpawnAt - Time.unscaledTime));

                if (assist != null && assist.ReadyForNextDrop)
                {
                    Vector3 spawnPos = ComputeSafeTopSpawnPosition(_roofSlideCollider, out _, out _);
                    spawnPos.y += Mathf.Max(0f, demoDropHeight - spawnLiftFromRoof);
                    assist.BeginDropFromSpawn(spawnPos);
                }
                if (assist != null && forceAvalancheNow)
                {
                    assist.RequestForceAvalancheNow();
                    forceAvalancheNow = false;
                }
            }

            yield return wait;
            nextSpawnAt = Time.unscaledTime + interval;
        }
        _naturalSnowLoopCoroutine = null;
    }

    int EnsureRoofSlideLayerAndGetIndex()
    {
        int idx = LayerMask.NameToLayer(roofSlideLayerName);
#if UNITY_EDITOR
        if (idx < 0)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layersProp = tagManager.FindProperty("layers");
            for (int i = 8; i <= 31; i++)
            {
                var sp = layersProp.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue))
                {
                    sp.stringValue = roofSlideLayerName;
                    tagManager.ApplyModifiedProperties();
                    idx = i;
                    break;
                }
            }
        }
#endif
        return idx;
    }

    void ForceSnowVisible(Transform snow)
    {
        if (snow == null) return;
        if (!snow.gameObject.activeSelf) snow.gameObject.SetActive(true);
        snow.gameObject.layer = 0; // Default
        // Keep original scale (no shape deformation in visibility helper).

        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material fallback = shader != null ? new Material(shader) : null;
        if (fallback != null) MaterialColorHelper.SetColorSafe(fallback, new Color(0.92f, 0.95f, 1f, 1f));

        foreach (var r in snow.GetComponentsInChildren<Renderer>(true))
        {
            r.enabled = true;
            bool needsFallback = r.sharedMaterial == null || r.sharedMaterial.shader == null;
            if (!needsFallback && r.sharedMaterial != null && r.sharedMaterial.shader != null)
                needsFallback = r.sharedMaterial.shader.name == "Hidden/InternalErrorShader";
            if (fallback != null && needsFallback)
                r.sharedMaterial = fallback;
        }
    }

    IEnumerator FollowCameraForDemo(Camera cam, Transform snow, float duration)
    {
        if (cam == null || snow == null) yield break;
        Vector3 originalPos = cam.transform.position;
        Quaternion originalRot = cam.transform.rotation;
        float start = Time.time;
        while (Time.time - start < duration)
        {
            if (cam == null || snow == null) yield break;
            cam.transform.position = snow.position + demoCameraOffset;
            cam.transform.LookAt(snow.position);
            yield return null;
        }
        if (restoreCameraAfterDemo && cam != null)
        {
            cam.transform.position = originalPos;
            cam.transform.rotation = originalRot;
        }
    }

    static bool IsInView(Camera cam, Vector3 worldPos)
    {
        if (cam == null) return false;
        Vector3 vp = cam.WorldToViewportPoint(worldPos);
        return vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
    }

    void TryAutoAssignRoof()
    {
        var roofRoot = GameObject.Find("RoofRoot");
        if (roofRoot != null)
        {
            targetRoof = roofRoot.transform;
            Debug.Log("[RoofSlideAutoAssign] targetRoof=RoofRoot");
            return;
        }

        var cabinRoof = GameObject.Find("cabin-roof");
        if (cabinRoof != null)
        {
            targetRoof = cabinRoof.transform.parent != null ? cabinRoof.transform.parent : cabinRoof.transform;
            Debug.Log($"[RoofSlideAutoAssign] targetRoof={targetRoof.name} (from cabin-roof)");
        }
    }

    void TryAutoAssignSnowTest()
    {
        var t = ResolveSnowTestTransform();
        if (t != null)
        {
            targetSnowTest = t;
            Debug.Log($"[RoofSlideAutoAssign] targetSnowTest={t.name}");
        }
    }

    PhysicsMaterial GetOrCreateSnowSlidePhysicMaterial()
    {
#if UNITY_EDITOR
        // Never save/import assets during play to avoid editor postprocessor exceptions.
        if (Application.isPlaying)
        {
            var playMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsMaterialAssetPath);
            if (playMat != null)
                return playMat;

            var runtimePlayMat = new PhysicsMaterial("SnowSlidePhysic_PlayRuntime");
            runtimePlayMat.dynamicFriction = 0f;
            runtimePlayMat.staticFriction = 0f;
            runtimePlayMat.frictionCombine = PhysicsMaterialCombine.Minimum;
            runtimePlayMat.bounciness = 0f;
            runtimePlayMat.bounceCombine = PhysicsMaterialCombine.Minimum;
            return runtimePlayMat;
        }

        EnsureFolder("Assets/Materials");
        EnsureFolder("Assets/Materials/Physics");

        var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(physicsMaterialAssetPath);
        if (mat == null)
        {
            mat = new PhysicsMaterial("SnowSlidePhysic");
            AssetDatabase.CreateAsset(mat, physicsMaterialAssetPath);
            AssetDatabase.SaveAssets();
        }

        mat.dynamicFriction = 0f;
        mat.staticFriction = 0f;
        mat.frictionCombine = PhysicsMaterialCombine.Minimum;
        mat.bounciness = 0f;
        mat.bounceCombine = PhysicsMaterialCombine.Minimum;
        EditorUtility.SetDirty(mat);
        if (!Application.isPlaying)
            AssetDatabase.SaveAssets();
        return mat;
#else
        var runtimeMat = new PhysicsMaterial("SnowSlidePhysic_Runtime");
        runtimeMat.dynamicFriction = 0f;
        runtimeMat.staticFriction = 0f;
        runtimeMat.frictionCombine = PhysicsMaterialCombine.Minimum;
        runtimeMat.bounciness = 0f;
        runtimeMat.bounceCombine = PhysicsMaterialCombine.Minimum;
        return runtimeMat;
#endif
    }

#if UNITY_EDITOR
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int idx = path.LastIndexOf('/');
        if (idx <= 0) return;
        string parent = path.Substring(0, idx);
        string name = path.Substring(idx + 1);
        if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
#endif
}

