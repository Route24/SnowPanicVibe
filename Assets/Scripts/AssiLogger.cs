// AssiLogger — Console ログを集約管理する静的ヘルパー
//
// 使い方:
//   AssiLogger.Info("[TAG] メッセージ");       // Console に出る
//   AssiLogger.Verbose("[TAG] 詳細ログ");      // verbose=true の時だけ Console に出る
//   AssiLogger.Warn("[TAG] 警告");
//   AssiLogger.Error("[TAG] エラー");
//
// verbose は Inspector の SnowStrip2D.verboseLog か、
// AssiLogger.VerboseEnabled = true; で実行時に切り替え可能。
using UnityEngine;

public static class AssiLogger
{
    // verbose フラグ: デフォルト OFF
    // Inspector から変更したい場合は SnowStrip2D.verboseLog を使う
    public static bool VerboseEnabled = false;

    /// <summary>重要イベント: 常にConsoleに出す</summary>
    public static void Info(string msg)
    {
        Debug.Log(msg);
    }

    /// <summary>詳細ログ: VerboseEnabled=true の時だけConsoleに出す</summary>
    public static void Verbose(string msg)
    {
        if (VerboseEnabled) Debug.Log(msg);
    }

    /// <summary>警告: 常にConsoleに出す</summary>
    public static void Warn(string msg)
    {
        Debug.LogWarning(msg);
    }

    /// <summary>エラー: 常にConsoleに出す</summary>
    public static void Error(string msg)
    {
        Debug.LogError(msg);
    }
}
