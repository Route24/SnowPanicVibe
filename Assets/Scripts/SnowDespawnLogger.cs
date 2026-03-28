using UnityEngine;
using System.Diagnostics;

/// <summary>ASSI: despawn 直前のログ。caller を StackTrace で特定。</summary>
public static class DespawnTrace
{
    public static void Log(string reason, string caller, string state, Vector3 pos)
    {
        string c = string.IsNullOrEmpty(caller) ? GetCallerFromTrace() : caller;
        UnityEngine.Debug.Log($"[DESPAWN] reason={reason} caller={c} state={state} t={Time.time:F2}");
    }

    static string GetCallerFromTrace()
    {
        var st = new StackTrace(3, true);
        for (int i = 0; i < st.FrameCount; i++)
        {
            var frame = st.GetFrame(i);
            if (frame == null) continue;
            var method = frame.GetMethod();
            var decl = method?.DeclaringType;
            if (decl != null && (decl.Name.Contains("DespawnTrace") || decl.Name.Contains("SnowDespawn"))) continue;
            var path = frame.GetFileName();
            int line = frame.GetFileLineNumber();
            string name = path != null ? System.IO.Path.GetFileName(path) : (decl?.Name ?? "?");
            return line > 0 ? $"{name}:{line}" : name;
        }
        return "unknown";
    }
}

/// <summary>
/// ASSI: 雪オブジェクトの despawn/pool 直前に必ず呼ぶ。呼び出し元を StackTrace で特定。
/// </summary>
public static class SnowDespawnLogger
{
    public enum SnowState { Roof, Sliding, Falling, Grounded, Despawning, Unknown }

    /// <summary>despawn/pool 前に呼ぶ。DespawnTrace.Log に委譲。</summary>
    public static void RequestDespawn(string reason, SnowState state, Vector3 pos, UnityEngine.Object target = null)
    {
        string stateStr = state.ToString();
        DespawnTrace.Log(reason, null, stateStr, pos);
    }
}
