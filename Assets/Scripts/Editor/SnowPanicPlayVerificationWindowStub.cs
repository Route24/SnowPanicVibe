#if UNITY_EDITOR
using System.Collections.Generic;

/// <summary>
/// SnowPanicPlayVerificationWindow 削除後の互換スタブ。
/// SnowLoopNoaReportAutoCopy.cs からの参照を壊さないために最小実装を提供する。
/// </summary>
public static class SnowPanicPlayVerificationWindow
{
    public class VerificationData
    {
        public string playConfirmed          = string.Empty;
        public string evidencePath           = string.Empty;
        public string stillFallsStraightDown = string.Empty;
        public string stillLooksLikeBlocks   = string.Empty;
    }

    public static VerificationData LoadVerification() => new VerificationData();

    public static string GetVerificationValue(string key) => string.Empty;
}
#endif
