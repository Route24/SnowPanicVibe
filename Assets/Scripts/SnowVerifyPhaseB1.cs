using UnityEngine;

/// <summary>
/// Phase B1: 固定雪1個表示のみ。SnowPack/Pool/Collapse/Chain は全て使わない。
/// </summary>
[DefaultExecutionOrder(-300)]
public class SnowVerifyPhaseB1 : MonoBehaviour
{
    const float CamX = 0f;
    const float CamY = 2.2f;
    const float CamZ = -4f;
    const float CamEulerX = 32f;

    static bool _logged;

    void Awake()
    {
        if (!IsPhaseB1()) return;
        ApplyCamera();
    }

    void Start()
    {
        if (!IsPhaseB1()) return;
        Invoke(nameof(LogPhaseB1), 0.8f);
    }

    static bool IsPhaseB1()
    {
        return GameObject.Find("VerifyMarkerPhaseB1") != null;
    }

    static void ApplyCamera()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(CamX, CamY, CamZ);
            cam.transform.rotation = Quaternion.Euler(CamEulerX, 0f, 0f);
        }
    }

    void LogPhaseB1()
    {
        if (_logged) return;
        _logged = true;

        bool phaseB1Started = true;

        var roof = GameObject.Find("RoofPhaseB1");
        bool roofVisible = roof != null && roof.activeInHierarchy;

        var staticSnow = GameObject.Find("StaticSnowPhaseB1");
        bool staticSnowCreated = staticSnow != null;
        string staticSnowPos = "N/A";
        string staticSnowScale = "N/A";
        bool staticSnowVisible = false;

        if (staticSnow != null)
        {
            staticSnowPos = $"({staticSnow.transform.position.x:F2},{staticSnow.transform.position.y:F2},{staticSnow.transform.position.z:F2})";
            var s = staticSnow.transform.localScale;
            staticSnowScale = $"({s.x:F2},{s.y:F2},{s.z:F2})";
            staticSnowVisible = staticSnow.activeInHierarchy;
            var r = staticSnow.GetComponent<Renderer>();
            if (r != null) staticSnowVisible = staticSnowVisible && r.enabled;
        }

        var msg = $"[SNOW_VERIFY_PHASE_B1] phase_b1_started={phaseB1Started.ToString().ToLower()} roof_visible={roofVisible.ToString().ToLower()} static_snow_created={staticSnowCreated.ToString().ToLower()} static_snow_visible={staticSnowVisible.ToString().ToLower()} static_snow_position={staticSnowPos} static_snow_scale={staticSnowScale} pool_used=false collapse_enabled=false chain_enabled=false";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== PHASE_B1 === phase_b1_started={phaseB1Started} roof_visible={roofVisible} static_snow_created={staticSnowCreated} static_snow_visible={staticSnowVisible}");
    }
}
