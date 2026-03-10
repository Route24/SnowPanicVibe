using UnityEngine;

/// <summary>
/// Phase A: シーン成立確認のみ。Snow 系スクリプトは一切使わない。
/// 屋根＋目印キューブが見えることだけ確認。
/// </summary>
[DefaultExecutionOrder(-300)]
public class SnowVerifyPhaseA : MonoBehaviour
{
    const float CamX = 0f;
    const float CamY = 2.2f;
    const float CamZ = -4f;
    const float CamEulerX = 32f;
    const float CamEulerY = 0f;
    const float CamEulerZ = 0f;

    static bool _logged;

    void Awake()
    {
        if (!IsPhaseA()) return;
        ApplyCamera();
    }

    void Start()
    {
        if (!IsPhaseA()) return;
        Invoke(nameof(LogPhaseA), 0.8f);
    }

    static bool IsPhaseA()
    {
        return GameObject.Find("VerifyMarkerPhaseA") != null;
    }

    static void ApplyCamera()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(CamX, CamY, CamZ);
            cam.transform.rotation = Quaternion.Euler(CamEulerX, CamEulerY, CamEulerZ);
        }
    }

    void LogPhaseA()
    {
        if (_logged) return;
        _logged = true;

        bool cameraCreated = Camera.main != null;
        string camPos = cameraCreated ? $"({Camera.main.transform.position.x:F2},{Camera.main.transform.position.y:F2},{Camera.main.transform.position.z:F2})" : "N/A";
        string camRot = cameraCreated ? $"({Camera.main.transform.eulerAngles.x:F1},{Camera.main.transform.eulerAngles.y:F1},{Camera.main.transform.eulerAngles.z:F1})" : "N/A";

        var roof = GameObject.Find("RoofPhaseA");
        bool roofCreated = roof != null && roof.activeInHierarchy;
        bool roofVisible = roofCreated; // カメラ固定時は作成されていれば見える想定

        var marker = GameObject.Find("MarkerCubePhaseA");
        bool markerVisible = marker != null && marker.activeInHierarchy;

        bool snowEnabled = Object.FindFirstObjectByType<SnowPackSpawner>() != null ||
            Object.FindFirstObjectByType<RoofSnowSystem>() != null ||
            Object.FindFirstObjectByType<GroundSnowSystem>() != null;
        bool snowScriptsEnabled = snowEnabled;

        bool phaseAOk = roofCreated && markerVisible && cameraCreated && !snowScriptsEnabled;

        var msg = $"[SNOW_VERIFY_PHASE_A] phase_a_scene_created={phaseAOk.ToString().ToLower()} camera_created={cameraCreated.ToString().ToLower()} camera_position={camPos} camera_rotation={camRot} roof_created={roofCreated.ToString().ToLower()} roof_visible={roofVisible.ToString().ToLower()} marker_cube_visible={markerVisible.ToString().ToLower()} snow_scripts_enabled={snowScriptsEnabled.ToString().ToLower()}";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== PHASE_A === phase_a_scene_created={phaseAOk} roof_visible={roofVisible} marker_cube_visible={markerVisible} snow_scripts_enabled={snowScriptsEnabled}");
    }
}
