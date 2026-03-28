#if UNITY_EDITOR
/// <summary>
/// ConsoleWindowUtility の互換スタブ。
/// Unity の内部 Console API が取得できない環境向けの最小実装。
/// </summary>
public static class ConsoleWindowUtility
{
    /// <summary>
    /// Unity Console のエラー/警告/ログ件数を取得する。
    /// 内部 API が使えない場合は -1 を返す。
    /// </summary>
    public static void GetConsoleLogCounts(out int errors, out int warnings, out int logs)
    {
        errors   = -1;
        warnings = -1;
        logs     = -1;

#if UNITY_2019_1_OR_NEWER
        try
        {
            // Unity 内部 API でカウント取得を試みる
            var logEntriesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.LogEntries");
            if (logEntriesType != null)
            {
                var getCountMethod = logEntriesType.GetMethod(
                    "GetCountsByType",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (getCountMethod != null)
                {
                    object[] args = new object[] { 0, 0, 0 };
                    getCountMethod.Invoke(null, args);
                    errors   = (int)args[0];
                    warnings = (int)args[1];
                    logs     = (int)args[2];
                }
            }
        }
        catch { /* フォールバック: -1 のまま */ }
#endif
    }
}
#endif
