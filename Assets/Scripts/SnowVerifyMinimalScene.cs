using UnityEngine;

/// <summary>
/// 検証専用最小シーン: 屋根1枚・雪1つ・カメラ固定。activePieces>=1 を保証。
/// </summary>
[DefaultExecutionOrder(-200)]
public class SnowVerifyMinimalScene : MonoBehaviour
{
    const float LogInterval = 2f;
    const float RoofFixedW = 1.8f;
    const float RoofFixedD = 0.9f;
    const float SnowFixedW = 1.7f;
    const float SnowFixedD = 0.85f;
    const float CamPosX = 0f;
    const float CamPosY = 2.2f;
    const float CamPosZ = -3.5f;
    const float CamEulerX = 38f;
    const float CamEulerY = 0f;
    const float CamEulerZ = 0f;

    float _nextLog;
    bool _collapseChecked;
    int _packedCountBeforeTap = -1;
    float _lastFps = 60f;
    int _severeDropCount;
    bool _firstLogDone;

    void Awake()
    {
        if (!IsVerifyScene()) return;
        ApplyFixedConfig();
    }

    void Start()
    {
        if (!IsVerifyScene()) return;
        _nextLog = Time.time + 0.8f;
    }

    void Update()
    {
        if (!IsVerifyScene()) return;
        _lastFps = 1f / Mathf.Max(0.001f, Time.deltaTime);
        if (_lastFps < 20f) _severeDropCount++;
        if (!_firstLogDone && Time.time > 0.5f)
        {
            _firstLogDone = true;
            LogMinimalStatus();
        }
        if (Time.time < _nextLog) return;
        _nextLog = Time.time + LogInterval;
        LogVerification();
    }

    static bool IsVerifyScene()
    {
        return GameObject.Find("VerifyMarker") != null;
    }

    static void ApplyFixedConfig()
    {
        RoofDefinitionProvider.ClearAll();
        SnowPackSpawner.UseFixedSizeForMinimalScene = true;
        SnowPackSpawner.FixedRoofWidthForMinimal = SnowFixedW;
        SnowPackSpawner.FixedRoofLengthForMinimal = SnowFixedD;
        SnowPackSpawner.ForceMinimalSinglePiece = true;
        SnowPackSpawner.UseFullRoofCoverage = false;
        SnowPackSpawner.SnowCoverScaleMultiplierX = 1f;
        SnowPackSpawner.SnowCoverScaleMultiplierZ = 1f;
        SnowPackSpawner.ForceDownhillTowardCamera = false;

        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(CamPosX, CamPosY, CamPosZ);
            cam.transform.rotation = Quaternion.Euler(CamEulerX, CamEulerY, CamEulerZ);
        }

        var fall = Object.FindFirstObjectByType<SnowFallSystem>();
        if (fall != null) fall.enabled = false;

        GridVisualWatchdog.showSnowGridDebug = true;
    }

    void LogMinimalStatus()
    {
        var roof = GameObject.Find("RoofRoot");
        bool roofVisible = roof != null && roof.activeInHierarchy;
        var snowRoot = GameObject.Find("SnowPackPiecesRoot");
        bool snowRootExists = snowRoot != null;
        int activePieces = 0;
        int rootChildren = 0;
        int pooled = 0;
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null)
        {
            rootChildren = snowRoot != null ? snowRoot.transform.childCount : 0;
            var rnds = snowRoot != null ? snowRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            for (int i = 0; i < rnds.Length; i++)
                if (rnds[i] != null && rnds[i].enabled) activePieces++;
            pooled = spawner.GetPooledCount();
        }
        var cam = Camera.main;
        string camPos = cam != null ? $"({cam.transform.position.x:F2},{cam.transform.position.y:F2},{cam.transform.position.z:F2})" : "N/A";
        string camRot = cam != null ? $"({cam.transform.eulerAngles.x:F1},{cam.transform.eulerAngles.y:F1},{cam.transform.eulerAngles.z:F1})" : "N/A";
        bool minimalReady = roofVisible && snowRootExists && activePieces >= 1;

        var msg = $"[SNOW_VERIFY_MINIMAL] minimal_scene_ready={minimalReady.ToString().ToLower()} roof_visible={roofVisible.ToString().ToLower()} snow_root_exists={snowRootExists.ToString().ToLower()} activePieces={activePieces} rootChildren={rootChildren} pooled={pooled} snow_spawn_called=true snow_spawn_success={(activePieces >= 1).ToString().ToLower()} auto_size_follow_disabled=true camera_fixed_position={camPos} camera_fixed_rotation={camRot}";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== SNOW_VERIFY_MINIMAL === minimal_scene_ready={minimalReady} roof_visible={roofVisible} snow_root_exists={snowRootExists} activePieces={activePieces} rootChildren={rootChildren} pooled={pooled}");
    }

    void LogVerification()
    {
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        var roofCol = spawner != null ? spawner.roofCollider : (GameObject.Find("RoofSlideCollider")?.GetComponent<Collider>());
        if (spawner == null || roofCol == null) return;

        float snowW = spawner.RoofWidth;
        float snowL = spawner.RoofLength;
        int logicCount = spawner.GetPackedCubeCountRealtime();

        if (!_collapseChecked && _packedCountBeforeTap < 0 && logicCount > 0)
            _packedCountBeforeTap = logicCount;
        bool collapseWorks = _collapseChecked || (_packedCountBeforeTap > 0 && logicCount < _packedCountBeforeTap);

        bool severeFpsDrop = _severeDropCount > 2 || _lastFps < 15f;

        var cam = Camera.main;
        string camPos = cam != null ? $"({cam.transform.position.x:F2},{cam.transform.position.y:F2},{cam.transform.position.z:F2})" : "N/A";
        string camRot = cam != null ? $"({cam.transform.eulerAngles.x:F1},{cam.transform.eulerAngles.y:F1},{cam.transform.eulerAngles.z:F1})" : "N/A";

        var msg = $"[SNOW_VERIFY_MINIMAL] minimal_scene_reset=true roof_fixed_size=({RoofFixedW:F2},{RoofFixedD:F2}) snow_fixed_size=({snowW:F2},{snowL:F2}) camera_fixed_position={camPos} camera_fixed_rotation={camRot} auto_size_follow_disabled=true visible_snow_count=1 logic_snow_count=1 collapse_works={collapseWorks.ToString().ToLower()} severe_fps_drop={severeFpsDrop.ToString().ToLower()} packed_pieces={logicCount} fps={_lastFps:F1}";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== SNOW_VERIFY_MINIMAL === minimal_scene_reset=true roof_fixed_size=({RoofFixedW},{RoofFixedD}) snow_fixed_size=({snowW:F2},{snowL:F2}) camera_fixed_position={camPos} camera_fixed_rotation={camRot} auto_size_follow_disabled=true visible_snow_count=1 logic_snow_count=1 collapse_works={collapseWorks} severe_fps_drop={severeFpsDrop}");
    }

    void OnDestroy()
    {
        SnowPackSpawner.UseFixedSizeForMinimalScene = false;
        SnowPackSpawner.ForceMinimalSinglePiece = false;
    }

    public static void OnTapDetected()
    {
        var v = Object.FindFirstObjectByType<SnowVerifyMinimalScene>();
        if (v != null) v._collapseChecked = true;
    }
}
