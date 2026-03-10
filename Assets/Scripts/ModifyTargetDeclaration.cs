using System;

/// <summary>
/// AI修正時の対象保護。修正対象を宣言し、protected_systems は変更禁止とする。
/// ASSI Report に出力してノアと共有。
/// </summary>
public static class ModifyTargetDeclaration
{
    /// <summary>今回の修正対象システム</summary>
    public static string TargetSystem { get; set; } = "AIPipelineBase";

    /// <summary>変更許可ファイル（カンマ区切り）</summary>
    public static string AllowedFiles { get; set; } = "DebugDiagnostics.cs,AIPipelineTestCollector.cs,ModifyTargetDeclaration.cs";

    /// <summary>保護対象（変更禁止）</summary>
    public static string ProtectedSystems { get; set; } = "SnowPhysics,SnowSpawner,SnowAvalanche,CameraController,ParticleSystem";

    /// <summary>ASSI Report に MODIFY TARGET を出力。</summary>
    public static void EmitToReport()
    {
        try
        {
            SnowLoopLogCapture.AppendToAssiReport("=== MODIFY TARGET ===");
            SnowLoopLogCapture.AppendToAssiReport($"target_system={TargetSystem}");
            SnowLoopLogCapture.AppendToAssiReport($"allowed_files={AllowedFiles}");
            SnowLoopLogCapture.AppendToAssiReport($"protected_systems={ProtectedSystems}");
        }
        catch (Exception ex) { UnityEngine.Debug.LogWarning($"[ModifyTargetDeclaration] EmitToReport: {ex.Message}"); }
    }
}
