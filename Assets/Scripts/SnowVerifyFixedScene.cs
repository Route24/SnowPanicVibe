using UnityEngine;

/// <summary>
/// 新規最小検証シーン用。固定値だけで成立。屋根1枚＋雪1枚＋カメラ1台。
/// 既存 SnowVerify_Minimal に依存しない。
/// </summary>
[DefaultExecutionOrder(-300)]
public class SnowVerifyFixedScene : MonoBehaviour
{
    const float RoofW = 1.5f;
    const float RoofD = 1.5f;
    const float RoofY = 1f;
    const float RoofSlopeDeg = 20f;
    const float CamX = 0f;
    const float CamY = 2.2f;
    const float CamZ = -4f;
    const float CamEulerX = 32f;
    const float CamEulerY = 0f;
    const float CamEulerZ = 0f;
    const float SnowPieceSize = 0.12f;

    const string SceneName = "SnowVerify_Fixed";
    static bool _createdLogged;

    void Awake()
    {
        if (!IsVerifyFixedScene()) return;
        ApplyFixedConfig();
    }

    void Start()
    {
        if (!IsVerifyFixedScene()) return;
        Invoke(nameof(LogCreationStatus), 1f);
    }

    static bool IsVerifyFixedScene()
    {
        return GameObject.Find("VerifyMarkerFixed") != null;
    }

    static void ApplyFixedConfig()
    {
        RoofDefinitionProvider.ClearAll();
        Vector3 roofOrigin = new Vector3(0f, RoofY, 0f);
        Vector3 roofNormal = Quaternion.Euler(RoofSlopeDeg, 0f, 0f) * Vector3.up;
        var def = RoofDefinition.Create(RoofW, RoofD, RoofSlopeDeg, Vector3.down, roofOrigin, roofNormal);
        RoofDefinitionProvider.SetFromExternal(0, def);

        SnowPackSpawner.UseFixedSizeForMinimalScene = true;
        SnowPackSpawner.FixedRoofWidthForMinimal = RoofW;
        SnowPackSpawner.FixedRoofLengthForMinimal = RoofD;
        SnowPackSpawner.ForceMinimalSinglePiece = true;
        SnowPackSpawner.MinimalPieceSize = SnowPieceSize;
        SnowPackSpawner.UseFullRoofCoverage = false;
        SnowPackSpawner.SnowCoverScaleMultiplierX = 1f;
        SnowPackSpawner.SnowCoverScaleMultiplierZ = 1f;
        SnowPackSpawner.ForceDownhillTowardCamera = false;

        var cam = Camera.main;
        bool cameraCreated = false;
        if (cam != null)
        {
            cam.transform.position = new Vector3(CamX, CamY, CamZ);
            cam.transform.rotation = Quaternion.Euler(CamEulerX, CamEulerY, CamEulerZ);
            cameraCreated = true;
        }

        GridVisualWatchdog.showSnowGridDebug = true;
        var fall = Object.FindFirstObjectByType<SnowFallSystem>();
        if (fall != null) fall.enabled = false;
    }

    void LogCreationStatus()
    {
        if (_createdLogged) return;
        _createdLogged = true;

        bool cameraCreated = Camera.main != null;
        string camPos = cameraCreated ? $"({Camera.main.transform.position.x:F2},{Camera.main.transform.position.y:F2},{Camera.main.transform.position.z:F2})" : "N/A";
        string camRot = cameraCreated ? $"({Camera.main.transform.eulerAngles.x:F1},{Camera.main.transform.eulerAngles.y:F1},{Camera.main.transform.eulerAngles.z:F1})" : "N/A";

        var roof = GameObject.Find("RoofFixed");
        bool roofCreated = roof != null && roof.activeInHierarchy;

        var snowRoot = GameObject.Find("SnowPackPiecesRoot");
        bool snowCreated = snowRoot != null;
        int activePieces = 0;
        string snowSize = "N/A";
        bool snowAboveRoof = false;

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null && snowRoot != null)
        {
            var rnds = snowRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rnds.Length; i++)
                if (rnds[i] != null && rnds[i].enabled) activePieces++;
            snowSize = $"({spawner.RoofWidth:F2},{spawner.RoofLength:F2})";
            var roofT = GameObject.Find("RoofFixed");
            if (roofT != null && snowRoot.transform.childCount > 0)
            {
                var piece = snowRoot.transform.GetChild(0);
                if (piece != null && piece.name == "SnowPackPiece")
                    snowAboveRoof = piece.position.y >= RoofY - 0.1f;
            }
        }

        bool activePiecesFail = activePieces < 1;
        bool newMinimalReady = roofCreated && snowCreated && activePieces >= 1 && !activePiecesFail;

        var msg = $"[SNOW_VERIFY_FIXED] new_minimal_scene_created={newMinimalReady.ToString().ToLower()} scene_name={SceneName} camera_created={cameraCreated.ToString().ToLower()} camera_position={camPos} camera_rotation={camRot} roof_created={roofCreated.ToString().ToLower()} roof_size=({RoofW:F2},{RoofD:F2}) snow_created={snowCreated.ToString().ToLower()} snow_size={snowSize} snow_above_roof={snowAboveRoof.ToString().ToLower()} activePieces={activePieces} activePieces_fail={activePiecesFail.ToString().ToLower()}";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== SNOW_VERIFY_FIXED === new_minimal_scene_created={newMinimalReady} scene_name={SceneName} camera_created={cameraCreated} roof_created={roofCreated} snow_created={snowCreated} activePieces={activePieces} activePieces_fail={activePiecesFail}");
    }

    void OnDestroy()
    {
        SnowPackSpawner.UseFixedSizeForMinimalScene = false;
        SnowPackSpawner.ForceMinimalSinglePiece = false;
        SnowPackSpawner.MinimalPieceSize = 0.15f;
        _createdLogged = false;
    }
}
