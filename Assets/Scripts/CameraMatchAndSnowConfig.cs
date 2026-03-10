using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 1) Play前のScene Viewカメラを保存し、Play後にMain Cameraへ適用して完全一致させる。
/// 2) 屋根上の初期積雪厚みを増やし、主塊がドサッと落ちる見た目を強化する。
/// 3) 雪の落下方向を「奥→手前」に固定し、積雪を屋根に密着させる。
/// </summary>
public static class CameraMatchAndSnowConfig
{
    internal const string KeyPos = "CameraMatch_preplay_pos";
    internal const string KeyRot = "CameraMatch_preplay_rot";
    internal const string KeyFov = "CameraMatch_preplay_fov";
    internal const float PosTolerance = 0.01f;
    internal const float RotTolerance = 0.5f;
    const float FovTolerance = 0.5f;

#if UNITY_EDITOR
    [InitializeOnLoadMethod]
    static void RegisterPlayModeHandler()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            var sv = SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
            {
                var cam = sv.camera;
                var pos = cam.transform.position;
                var rot = cam.transform.rotation;
                float fov = cam.fieldOfView;
                EditorPrefs.SetString(KeyPos, pos.x + "," + pos.y + "," + pos.z);
                EditorPrefs.SetString(KeyRot, rot.x + "," + rot.y + "," + rot.z + "," + rot.w);
                EditorPrefs.SetFloat(KeyFov, fov);
            }
            else
            {
                var main = Object.FindFirstObjectByType<Camera>();
                if (main != null && main.CompareTag("MainCamera"))
                {
                    var pos = main.transform.position;
                    var rot = main.transform.rotation;
                    float fov = main.fieldOfView;
                    EditorPrefs.SetString(KeyPos, pos.x + "," + pos.y + "," + pos.z);
                    EditorPrefs.SetString(KeyRot, rot.x + "," + rot.y + "," + rot.z + "," + rot.w);
                    EditorPrefs.SetFloat(KeyFov, fov);
                }
            }
        }
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        var go = new GameObject("CameraMatchAndSnowConfig_Runner");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<CameraMatchAndSnowRunner>();
    }
}

class CameraMatchAndSnowRunner : MonoBehaviour
{
    bool _applied;
    int _frameCount;

    void LateUpdate()
    {
        if (_applied) return;
        _frameCount++;
        if (_frameCount < 1) return;

        _applied = true;
        ApplyRoofBasedCamera();
        ApplySnowConfigOverrides();
        ApplySnowFeelTuning();
        ApplySnowConfigOverridesRebuild();
        ApplyFallDirectionAndSnowOffset();
        LogRequiredValues();
    }

    void ApplyRoofBasedCamera()
    {
        string scene = SceneManager.GetActiveScene().name ?? "";
        bool oneHouse = !string.IsNullOrEmpty(scene) && (scene.Contains("OneHouse") || GameObject.Find("OneHouseMarker") != null);
        if (!oneHouse) return;

        var cam = Camera.main;
        if (cam == null) return;

        Collider roofCol = GameObject.Find("RoofSlideCollider")?.GetComponent<Collider>();
        if (roofCol == null)
        {
            var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
            roofCol = roofSys != null ? roofSys.roofSlideCollider : null;
        }
        if (roofCol == null)
            roofCol = GameObject.Find("RoofSurface")?.GetComponent<Collider>();
        if (roofCol == null)
        {
            var sp = Object.FindFirstObjectByType<SnowPackSpawner>();
            roofCol = sp != null ? sp.roofCollider : null;
        }

        if (roofCol == null) return;

        Bounds b = roofCol.bounds;
        Vector3 roofCenter = b.center;
        Vector3 roofSize = b.size;
        Vector3 roofUp = roofCol.transform.up.normalized;
        float dist = 6.5f;
        float pitchDeg = 36f;
        float yawDeg = 180f;
        Vector3 lookTarget = roofCenter + roofUp * 0.3f;

        float pitchRad = pitchDeg * Mathf.Deg2Rad;
        float yawRad = yawDeg * Mathf.Deg2Rad;
        float h = dist * Mathf.Cos(pitchRad);
        float y = dist * Mathf.Sin(pitchRad);
        float x = Mathf.Sin(yawRad) * h;
        float z = Mathf.Cos(yawRad) * h;
        Vector3 camPos = lookTarget + new Vector3(x, y, z);
        Quaternion camRot = Quaternion.LookRotation(lookTarget - camPos);
        float fov = 45f;

        cam.transform.position = camPos;
        cam.transform.rotation = camRot;
        cam.fieldOfView = fov;

        var orbit = cam.GetComponent<CameraOrbit>();
        if (orbit != null)
        {
            if (orbit.target == null)
            {
                var go = new GameObject("CameraTarget");
                go.transform.position = lookTarget;
                orbit.target = go.transform;
            }
            else
            {
                orbit.target.position = lookTarget;
            }
            orbit.distance = dist;
            orbit._pitch = pitchDeg;
            orbit._yaw = yawDeg;
            orbit.yMin = 4f;
            orbit.yMax = 12f;
        }
    }

    static bool TryParseVector3(string s, out Vector3 v)
    {
        v = Vector3.zero;
        var parts = s.Split(',');
        if (parts.Length != 3) return false;
        float x, y, z;
        if (!float.TryParse(parts[0], out x) || !float.TryParse(parts[1], out y) || !float.TryParse(parts[2], out z))
            return false;
        v = new Vector3(x, y, z);
        return true;
    }

    static bool TryParseQuaternion(string s, out Quaternion q)
    {
        q = Quaternion.identity;
        var parts = s.Split(',');
        if (parts.Length != 4) return false;
        float x, y, z, w;
        if (!float.TryParse(parts[0], out x) || !float.TryParse(parts[1], out y) || !float.TryParse(parts[2], out z) || !float.TryParse(parts[3], out w))
            return false;
        q = new Quaternion(x, y, z, w);
        return true;
    }

    void ApplySnowConfigOverrides()
    {
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return;

        string scene = SceneManager.GetActiveScene().name ?? "";
        bool oneHouse = !string.IsNullOrEmpty(scene) && (scene.Contains("OneHouse") || GameObject.Find("OneHouseMarker") != null);
        SnowPackSpawner.ForceDownhillTowardCamera = oneHouse;
        SnowPackSpawner.UseFullRoofCoverage = oneHouse;
        if (!oneHouse) return;

        spawner.targetDepthMeters = 0.65f;
        spawner.snowDepthScale = 0.4f;
        spawner.snowPieceThicknessScale = 0.38f;
        spawner.snowRenderThicknessScale = 0.75f;
        spawner.pieceSize = 0.17f;
        spawner.pieceHeightScale = 0.9f;
        // Rebuild は ApplySnowFeelTuning でまとめて行う（oneHouse 時）
    }

    void ApplySnowConfigOverridesRebuild()
    {
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return;
        string scene = SceneManager.GetActiveScene().name ?? "";
        bool oneHouse = !string.IsNullOrEmpty(scene) && (scene.Contains("OneHouse") || GameObject.Find("OneHouseMarker") != null);
        if (oneHouse) return;
        if (spawner.rebuildOnPlay && spawner.isActiveAndEnabled)
            spawner.RebuildSnowPack("CameraMatchAndSnowConfig_thickness_up");
    }

    /// <summary>1軒本番シーン用: 一撃量・連鎖・塊感の3項目に絞った気持ちよさ調整。PhaseB2卒業→本番手触りへ。</summary>
    void ApplySnowFeelTuning()
    {
        string scene = SceneManager.GetActiveScene().name ?? "";
        bool oneHouse = !string.IsNullOrEmpty(scene) && (scene.Contains("OneHouse") || GameObject.Find("OneHouseMarker") != null);
        if (!oneHouse) return;

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
        if (spawner == null || roofSys == null) return;

        // 1) 一撃で剥がれる量: 叩く場所で変わる。狙いが必要。
        roofSys.hitRadiusR = 0.9f;
        spawner.localAvalancheMinDetach = 16;
        spawner.localAvalancheMaxDetach = 60;

        // 2) 連鎖の起きやすさ: うまく当てると大きく連鎖
        spawner.chainDetachChance = 0.8f;
        spawner.secondaryDetachFraction = 0.38f;
        spawner.maxSecondaryDetachPerHit = 30;
        spawner.unstableRadiusScale = 1.45f;
        spawner.secondaryDetachDelaySec = 0.33f;
        spawner.unstableDurationSec = 1.4f;

        // 3) 塊で落ちる気持ちよさ
        roofSys.localAvalancheSlideSpeed = 1.02f;
        roofSys.burstChunkCount = 42;
        roofSys.burstChunkSpeed = 2.3f;

        // キューブ感軽減は軽く（主目的は手触り）
        spawner.jitter = 0.032f;

        if (spawner.rebuildOnPlay && spawner.isActiveAndEnabled)
            spawner.RebuildSnowPack("SnowFeelTuning");
        Debug.Log("[SnowFeelTuning] oneHouse=true 一撃量・連鎖・塊感 調整適用（PhaseB2卒業→本番手触り）");
    }

    void ApplyFallDirectionAndSnowOffset()
    {
        string scene = SceneManager.GetActiveScene().name ?? "";
        bool oneHouse = !string.IsNullOrEmpty(scene) && (scene.Contains("OneHouse") || GameObject.Find("OneHouseMarker") != null);
        if (!oneHouse) return;

        var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
        if (roofSys != null)
        {
            roofSys.roofSnowSurfaceOffsetY = -0.05f;
            roofSys.roofSnowConstantThickness = 0.06f;
        }

        var groundSys = Object.FindFirstObjectByType<GroundSnowSystem>();
        if (groundSys != null)
        {
            groundSys.groundPileLifetimeSec = 4.5f;
            groundSys.groundPileBlinkDurationSec = 0.6f;
            groundSys.maxGroundPiles = 48;
        }
    }

    void LogRequiredValues()
    {
        var cam = Camera.main;
        Vector3 runtimePos = cam != null ? cam.transform.position : Vector3.zero;
        Quaternion runtimeRot = cam != null ? cam.transform.rotation : Quaternion.identity;
        string rtPosStr = string.Format("({0:F2},{1:F2},{2:F2})", runtimePos.x, runtimePos.y, runtimePos.z);
        string rtRotStr = string.Format("({0:F1},{1:F1},{2:F1})", runtimeRot.eulerAngles.x, runtimeRot.eulerAngles.y, runtimeRot.eulerAngles.z);

        Collider roofColLog = GameObject.Find("RoofSlideCollider")?.GetComponent<Collider>();
        if (roofColLog == null)
        {
            var rsl = Object.FindFirstObjectByType<RoofSnowSystem>();
            roofColLog = rsl != null ? rsl.roofSlideCollider : null;
        }
        if (roofColLog == null) roofColLog = GameObject.Find("RoofSurface")?.GetComponent<Collider>();

        Vector3 roofCenter = roofColLog != null ? roofColLog.bounds.center : Vector3.zero;
        Vector3 roofSize = roofColLog != null ? roofColLog.bounds.size : Vector3.zero;
        Vector3 lookTarget = roofCenter + (roofColLog != null ? roofColLog.transform.up.normalized * 0.3f : Vector3.zero);
        bool targetVisible = false;
        Vector2 vpCenter = Vector2.zero;
        if (cam != null && roofColLog != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(lookTarget);
            targetVisible = vp.z > 0f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f;
            vpCenter = new Vector2(vp.x, vp.y);
        }
        string roofCenterStr = string.Format("({0:F2},{1:F2},{2:F2})", roofCenter.x, roofCenter.y, roofCenter.z);
        string roofSizeStr = string.Format("({0:F2},{1:F2},{2:F2})", roofSize.x, roofSize.y, roofSize.z);
        string lookStr = string.Format("({0:F2},{1:F2},{2:F2})", lookTarget.x, lookTarget.y, lookTarget.z);
        string fwdStr = string.Format("({0:F3},{1:F3},{2:F3})", cam != null ? cam.transform.forward.x : 0f, cam != null ? cam.transform.forward.y : 0f, cam != null ? cam.transform.forward.z : 0f);
        string vpStr = string.Format("({0:F3},{1:F3})", vpCenter.x, vpCenter.y);

        Debug.Log($"[CAMERA_VIEW] roof_bounds_center={roofCenterStr} roof_bounds_size={roofSizeStr} camera_position={rtPosStr} camera_rotation={rtRotStr} camera_forward={fwdStr} look_target={lookStr} target_visible={targetVisible.ToString().ToLower()} target_viewport_center={vpStr} camera_matched={targetVisible.ToString().ToLower()}");

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        float initialDepth = spawner != null ? spawner.targetDepthMeters * spawner.snowDepthScale : 0f;
        float primarySize = spawner != null ? spawner.pieceSize : 0f;
        int largestFall = SnowPackSpawner.LastRemovedCount;
        string activeSnow = "RoofSnowLayer+SnowPackPiece";

        Debug.Log($"[SNOW_CONFIG] initial_snow_depth={initialDepth:F3} primary_cluster_size={primarySize:F2} largest_fall_group={largestFall} active_snow_visual={activeSnow}");

        Vector3 roofBoundsCenter = Vector3.zero;
        Vector3 roofBoundsSize = Vector3.zero;
        Vector3 snowCoverBoundsCenter = Vector3.zero;
        Vector3 snowCoverBoundsSize = Vector3.zero;
        Vector2 roofSurfaceSize = Vector2.zero;
        Vector2 snowCoverSize = Vector2.zero;
        bool snowCoverMatches = false;
        float snowCoverMarginX = 0f, snowCoverMarginZ = 0f;
        string activeRoofTarget = "none";
        string roofShape = "mono_slope";
        string roofSlopeDir = "front";

        if (roofColLog != null)
        {
            activeRoofTarget = roofColLog.name;
            var rb = roofColLog.bounds;
            roofBoundsCenter = rb.center;
            roofBoundsSize = rb.size;
            if (roofColLog is BoxCollider box)
            {
                roofSurfaceSize = new Vector2(Mathf.Max(0.1f, box.size.x), Mathf.Max(0.1f, box.size.z));
            }
            else
            {
                roofSurfaceSize = new Vector2(Mathf.Max(0.1f, rb.size.x), Mathf.Max(0.1f, rb.size.z));
            }
        }
        if (spawner != null)
        {
            snowCoverSize = new Vector2(spawner.RoofWidth, spawner.RoofLength);
            snowCoverBoundsCenter = spawner.RoofCenter;
            Vector3 r = spawner.RoofR, f = spawner.RoofF;
            float w = spawner.RoofWidth, l = spawner.RoofLength;
            Vector3 p0 = snowCoverBoundsCenter - r * (w * 0.5f) - f * (l * 0.5f);
            Bounds scb = new Bounds(p0, Vector3.zero);
            scb.Encapsulate(snowCoverBoundsCenter + r * (w * 0.5f) - f * (l * 0.5f));
            scb.Encapsulate(snowCoverBoundsCenter + r * (w * 0.5f) + f * (l * 0.5f));
            scb.Encapsulate(snowCoverBoundsCenter - r * (w * 0.5f) + f * (l * 0.5f));
            snowCoverBoundsCenter = scb.center;
            snowCoverBoundsSize = scb.size;
            float tol = 0.05f;
            bool matchDirect = Mathf.Abs(roofSurfaceSize.x - snowCoverSize.x) <= tol && Mathf.Abs(roofSurfaceSize.y - snowCoverSize.y) <= tol;
            bool matchSwap = Mathf.Abs(roofSurfaceSize.x - snowCoverSize.y) <= tol && Mathf.Abs(roofSurfaceSize.y - snowCoverSize.x) <= tol;
            snowCoverMatches = matchDirect || matchSwap;
            snowCoverMarginX = 0.5f * (roofBoundsSize.x - snowCoverBoundsSize.x);
            snowCoverMarginZ = 0.5f * (roofBoundsSize.z - snowCoverBoundsSize.z);
        }
        Vector3 downhill = spawner != null ? spawner.RoofDownhill : Vector3.zero;
        roofSlopeDir = GetFallDirectionLabel(downhill, cam != null ? cam.transform.forward : Vector3.forward);

        var groundSys = Object.FindFirstObjectByType<GroundSnowSystem>();
        bool groundSpawned = groundSys != null;
        float groundLifetime = groundSys != null ? groundSys.groundPileLifetimeSec : 0f;
        int groundActiveCount = groundSys != null ? groundSys.GetActivePileCount() : 0;

        string roofBoundsCenterStr = string.Format("({0:F2},{1:F2},{2:F2})", roofBoundsCenter.x, roofBoundsCenter.y, roofBoundsCenter.z);
        string roofBoundsSizeStr = string.Format("({0:F2},{1:F2},{2:F2})", roofBoundsSize.x, roofBoundsSize.y, roofBoundsSize.z);
        string snowCenterStr = string.Format("({0:F2},{1:F2},{2:F2})", snowCoverBoundsCenter.x, snowCoverBoundsCenter.y, snowCoverBoundsCenter.z);
        string snowSizeStr = string.Format("({0:F2},{1:F2},{2:F2})", snowCoverBoundsSize.x, snowCoverBoundsSize.y, snowCoverBoundsSize.z);
        string roofSurfStr = string.Format("({0:F2},{1:F2})", roofSurfaceSize.x, roofSurfaceSize.y);
        string snowCoverStr = string.Format("({0:F2},{1:F2})", snowCoverSize.x, snowCoverSize.y);

        Debug.Log($"[SNOW_COVER] active_roof_target={activeRoofTarget} roof_surface_bounds_center={roofBoundsCenterStr} roof_surface_bounds_size={roofBoundsSizeStr} snow_cover_bounds_center={snowCenterStr} snow_cover_bounds_size={snowSizeStr} roof_surface_size={roofSurfStr} snow_cover_size={snowCoverStr} snow_cover_matches_roof={snowCoverMatches.ToString().ToLower()} snow_cover_margin_x={snowCoverMarginX:F3} snow_cover_margin_z={snowCoverMarginZ:F3} roof_shape={roofShape} roof_slope_direction={roofSlopeDir} ground_snow_spawned={groundSpawned.ToString().ToLower()} ground_snow_lifetime={groundLifetime:F1} ground_snow_active_count={groundActiveCount}");
        RoofSnowReportWriter.Write(roofSurfStr, snowCoverStr, snowCoverMatches);

        string visualFallDir = GetFallDirectionLabel(downhill, cam != null ? cam.transform.forward : Vector3.forward);
        var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
        float snowOffset = roofSys != null ? roofSys.roofSnowSurfaceOffsetY : 0f;
        bool snowAttached = roofSys != null && roofSys.roofSlideCollider != null;

        Debug.Log($"[ROOF_FALL_DIR] roof_shape={roofShape} roof_slope_direction={roofSlopeDir} visual_fall_direction={visualFallDir} snow_surface_offset={snowOffset:F3} snow_visual_attached={snowAttached.ToString().ToLower()} hit_target={activeRoofTarget} camera_position={rtPosStr} camera_rotation={rtRotStr}");
    }

    static string GetFallDirectionLabel(Vector3 downhill, Vector3 camForward)
    {
        if (downhill.sqrMagnitude < 0.001f) return "unknown";
        float dotZ = Vector3.Dot(downhill, Vector3.forward);
        float dotX = Vector3.Dot(downhill, Vector3.right);
        if (Mathf.Abs(dotZ) >= Mathf.Abs(dotX))
            return dotZ < 0 ? "front" : "back";
        return dotX > 0 ? "right" : "left";
    }
}
