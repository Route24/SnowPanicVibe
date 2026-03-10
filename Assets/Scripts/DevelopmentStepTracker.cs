/// <summary>
/// Master Development Roadmap: 現在のStepと結果を宣言。ASSI Report に出力。
/// 1回の依頼で1ステップのみ実装し、PASS/FAILを判定する。
/// </summary>
public static class DevelopmentStepTracker
{
    /// <summary>現在実装中の Step（例: 0-1, 1-2, 2-3）</summary>
    public static string CurrentStep { get; set; } = "0-1";

    /// <summary>Step の結果: PASS / FAIL / PENDING</summary>
    public static string StepResult { get; set; } = "PENDING";

    /// <summary>ASSI Report に === DEVELOPMENT STEP === を出力。</summary>
    public static void EmitToReport()
    {
        try
        {
            SnowLoopLogCapture.AppendToAssiReport("=== DEVELOPMENT STEP ===");
            SnowLoopLogCapture.AppendToAssiReport($"current_step={CurrentStep}");
            SnowLoopLogCapture.AppendToAssiReport($"step_result={StepResult}");
        }
        catch (System.Exception) { /* VideoPipeline SelfTest 等でログ無効時 */ }
    }
}
