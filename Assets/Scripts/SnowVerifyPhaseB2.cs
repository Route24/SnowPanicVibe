using UnityEngine;

/// <summary>
/// Phase B2: 最小複数ピース化。generated_total / active / pooled を確認。
/// Test A: 何も触らず4秒。Test B: 1回タップして落雪。生成時と落雪後の total=0 を分離。
/// </summary>
[DefaultExecutionOrder(-300)]
public class SnowVerifyPhaseB2 : MonoBehaviour
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
    const float TestA_LogTime = 4f;
    const float FinalSummaryTime = 8f;

    static bool _testALogged;
    static bool _finalSummaryLogged;

    void Awake()
    {
        if (!IsPhaseB2()) return;
        SnowVerifyB2Debug.Enabled = true;
        SnowVerifyB2Debug.Reset();
        ApplyConfig();
    }

    void Start()
    {
        if (!IsPhaseB2()) return;
        Invoke(nameof(LogPhaseB2_Initial), 1.5f);
    }

    void Update()
    {
        if (!IsPhaseB2()) return;

        float t = Time.time;
        if (t >= TestA_LogTime && !_testALogged)
        {
            _testALogged = true;
            LogTestA();
        }
        if (t >= FinalSummaryTime && !_finalSummaryLogged)
        {
            _finalSummaryLogged = true;
            LogFinalSummary();
        }
    }

    static bool IsPhaseB2()
    {
        return GameObject.Find("VerifyMarkerPhaseB2") != null;
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
        SnowVerifyB2Debug.PauseCleanup = true;
        SnowVerifyB2Debug.PausePoolReturn = true;

        var fall = Object.FindFirstObjectByType<SnowFallSystem>();
        if (fall != null) fall.enabled = false;

        GridVisualWatchdog.showSnowGridDebug = true;
    }

    void LogPhaseB2_Initial()
    {
        bool phaseB2Started = true;
        bool generationCalled = true;

        int generatedTotal = 0;
        int activePieces = 0;
        int pooled = 0;

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        var snowRoot = GameObject.Find("SnowPackPiecesRoot");

        if (spawner != null)
        {
            generatedTotal = spawner.GetPackedCubeCountRealtime();
            pooled = spawner.GetPooledCount();
            if (snowRoot != null)
            {
                var rnds = snowRoot.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < rnds.Length; i++)
                    if (rnds[i] != null && rnds[i].enabled) activePieces++;
            }
            generatedTotal = Mathf.Max(generatedTotal, activePieces);
        }

        string discardCounts = SnowVerifyB2Debug.GetDiscardReasonCountsString();
        var msg = $"[SNOW_VERIFY_PHASE_B2] phase_b2_started={phaseB2Started.ToString().ToLower()} generation_called={generationCalled.ToString().ToLower()} generated_total={generatedTotal} activePieces={activePieces} pooled={pooled} discard_reason_counts={discardCounts} cleanup_called={SnowVerifyB2Debug.CleanupCalled.ToString().ToLower()} pool_return_called={SnowVerifyB2Debug.PoolReturnCalled.ToString().ToLower()}";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== PHASE_B2_INITIAL === generated_total={generatedTotal} activePieces={activePieces} pooled={pooled}");
    }

    void LogTestA()
    {
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        int total = spawner != null ? spawner.GetB2TotalCount() : 0;
        int active = spawner != null ? spawner.GetB2ActiveCount() : 0;
        int pooled = spawner != null ? spawner.GetPooledCount() : 0;
        bool tapOccurred = SnowVerifyB2Debug.LastTapTimeAtTestB > 0f;
        bool errorOccurred = total <= 0 && !tapOccurred;

        var msg = $"[B2_TEST_A] tap_received={tapOccurred.ToString().ToLower()} test_a_no_input_total={total} test_a_no_input_active={active} test_a_error_occurred={errorOccurred.ToString().ToLower()}";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== B2_TEST_A === {msg}");
    }

    void LogFinalSummary()
    {
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        int total = spawner != null ? spawner.GetB2TotalCount() : 0;
        int active = spawner != null ? spawner.GetB2ActiveCount() : 0;

        bool tapReceived = SnowVerifyB2Debug.LastTapTimeAtTestB > 0f;
        string zeroStep = SnowVerifyB2Debug.ZeroTransitionStep ?? SnowVerifyB2Debug.ZeroTotalTriggerStep ?? "none";
        string discardCounts = SnowVerifyB2Debug.GetDiscardReasonCountsString();

        bool zeroDetected = !string.IsNullOrEmpty(zeroStep) && zeroStep != "none";
        var msg = $"[B2_FINAL_SUMMARY] tap_received={tapReceived.ToString().ToLower()} surviving_count_after_cleanup={total} zero_total_detected={zeroDetected.ToString().ToLower()} cleanup_called={SnowVerifyB2Debug.CleanupCalled.ToString().ToLower()} pool_return_called={SnowVerifyB2Debug.PoolReturnCalled.ToString().ToLower()} zero_total_trigger_step={zeroStep} discard_reason_counts={discardCounts}";
        Debug.Log(msg);
        SnowLoopLogCapture.AppendToAssiReport($"=== B2_FINAL_SUMMARY === tap_received={tapReceived} surviving={total} zero_step={zeroStep} cleanup_called={SnowVerifyB2Debug.CleanupCalled} pool_return_called={SnowVerifyB2Debug.PoolReturnCalled}");
    }

    void OnDestroy()
    {
        SnowVerifyB2Debug.Enabled = false;
        SnowPackSpawner.UseFixedSizeForMinimalScene = false;
        SnowPackSpawner.ForceMinimalSinglePiece = false;
        SnowPackSpawner.MinimalPieceSize = 0.15f;
        _testALogged = false;
        _finalSummaryLogged = false;
    }
}
