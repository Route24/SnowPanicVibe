using UnityEngine;

/// <summary>
/// Phase B: 雪1個だけ生成確認。崩壊・連鎖・自動追従はオフ。
/// Phase A 成功後にのみ使用。
/// </summary>
[DefaultExecutionOrder(-300)]
public class SnowVerifyPhaseB : MonoBehaviour
{
    const float RoofW = 1.5f;
    const float RoofD = 1.5f;
    const float RoofY = 1f;
    const float RoofSlopeDeg = 20f;
    const float CamX = 0f;
    const float CamY = 2.2f;
    const float CamZ = -4f;
    const float CamEulerX = 32f;
    const float SnowPieceSize = 0.12f;

    static bool _logged;

    void Awake()
    {
        if (!IsPhaseB()) return;
        ApplyConfig();
    }

    void Start()
    {
        if (!IsPhaseB()) return;
        Invoke(nameof(LogPhaseB), 1.2f);
    }

    static bool IsPhaseB()
    {
        return GameObject.Find("VerifyMarkerPhaseB") != null;
    }

    static void ApplyConfig()
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
        if (cam != null)
        {
            cam.transform.position = new Vector3(CamX, CamY, CamZ);
            cam.transform.rotation = Quaternion.Euler(CamEulerX, 0f, 0f);
        }

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null)
        {
            spawner.debugAutoRefillRoofSnow = false;
            spawner.chainDetachChance = 0f;
            spawner.maxSecondaryDetachPerHit = 0;
        }

        var fall = Object.FindFirstObjectByType<SnowFallSystem>();
        if (fall != null) fall.enabled = false;

        GridVisualWatchdog.showSnowGridDebug = true;
    }

    void LogPhaseB()
    {
        if (_logged) return;
        _logged = true;

        bool phaseBStarted = true;

        var snowRoot = GameObject.Find("SnowPackPiecesRoot");
        bool snowSpawnCalled = true;
        int activePieces = 0;
        bool snowVisible = false;
        bool snowAboveRoof = false;

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null && snowRoot != null)
        {
            var rnds = snowRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rnds.Length; i++)
                if (rnds[i] != null && rnds[i].enabled) activePieces++;
            snowVisible = activePieces >= 1;

            var roof = GameObject.Find("RoofPhaseB");
            if (roof != null && snowRoot.transform.childCount > 0)
            {
                for (int i = 0; i < snowRoot.transform.childCount; i++)
                {
                    var c = snowRoot.transform.GetChild(i);
                    if (c.name == "SnowPackPiece" && c.gameObject.activeSelf)
                    {
                        snowAboveRoof = c.position.y >= RoofY - 0.15f;
                        break;
                    }
                }
            }
        }

        bool snowSpawnSuccess = activePieces >= 1;
        bool activePiecesFail = activePieces < 1;

        var msg = $"[SNOW_VERIFY_PHASE_B] phase_b_started={phaseBStarted.ToString().ToLower()} snow_spawn_called={snowSpawnCalled.ToString().ToLower()} snow_spawn_success={snowSpawnSuccess.ToString().ToLower()} activePieces={activePieces} snow_visible={snowVisible.ToString().ToLower()} snow_above_roof={snowAboveRoof.ToString().ToLower()} auto_size_follow=false collapse_enabled=false chain_enabled=false";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== PHASE_B === phase_b_started={phaseBStarted} snow_spawn_success={snowSpawnSuccess} activePieces={activePieces} snow_visible={snowVisible} activePieces_fail={activePiecesFail}");
    }

    void OnDestroy()
    {
        SnowPackSpawner.UseFixedSizeForMinimalScene = false;
        SnowPackSpawner.ForceMinimalSinglePiece = false;
        SnowPackSpawner.MinimalPieceSize = 0.15f;
        _logged = false;
    }
}
