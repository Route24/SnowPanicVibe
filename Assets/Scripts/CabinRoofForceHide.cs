using UnityEngine;

/// <summary>cabin-roofを初手から絶対に表示しない。茶色屋根一瞬表示の防止。</summary>
[DefaultExecutionOrder(-32767)]
public class CabinRoofForceHide : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnAfterSceneLoad()
    {
        ForceHideAndLog();
        DestroyRoofProxyAndLog();
        ForceHideDebugPanels();
    }

    static void ForceHideDebugPanels()
    {
        try
        {
            var col = GameObject.Find("RoofSlideCollider");
            if (col != null)
            {
                var debug = col.transform.Find("RoofSlideColliderDebug");
                if (debug != null) debug.gameObject.SetActive(false);
            }
            var visual = GameObject.Find("SnowPackVisual");
            if (visual != null)
            {
                var ind = visual.transform.Find("SnowPackStateIndicator");
                if (ind != null) ind.gameObject.SetActive(false);
            }
        }
        catch (System.Exception) { }
    }

    void Awake()
    {
        ForceHideAndLog();
    }

    void LateUpdate()
    {
        ForceHide();
    }

    static void ForceHide()
    {
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t != null && t.gameObject.name == "cabin-roof")
            {
                var mr = t.GetComponent<MeshRenderer>();
                if (mr != null && mr.enabled) mr.enabled = false;
            }
        }
    }

    static void ForceHideAndLog()
    {
        try
        {
            int count = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t != null && t.gameObject.name == "cabin-roof")
                {
                    var mr = t.GetComponent<MeshRenderer>();
                    bool wasEnabled = mr != null && mr.enabled;
                    if (mr != null) mr.enabled = false;
                    count++;
                    SnowLoopLogCapture.AppendToAssiReport($"CABIN_ROOF_HIDE path={GetTransformPath(t)} wasEnabled={wasEnabled}");
                }
            }
            SnowLoopLogCapture.AppendToAssiReport($"=== CABIN_ROOF_FORCE_HIDE count={count} ===");
        }
        catch (System.Exception) { }
    }

    static void DestroyRoofProxyAndLog()
    {
        try
        {
            var existing = GameObject.Find("RoofProxy");
            bool found = existing != null;
            if (existing != null) Object.Destroy(existing);
            SnowLoopLogCapture.AppendToAssiReport("=== ROOF_PROXY_DISABLED ===");
            SnowLoopLogCapture.AppendToAssiReport($"found={found} destroyed={found}");
        }
        catch (System.Exception) { }
    }

    static string GetTransformPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new System.Collections.Generic.List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
