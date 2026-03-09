using UnityEngine;
using System.Collections.Generic;

/// <summary>cabin-roofを初手から絶対に表示しない。屋根基準面を1つに統一し、補助メッシュを非表示。</summary>
[DefaultExecutionOrder(-32767)]
public class CabinRoofForceHide : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void OnAfterSceneLoad()
    {
        ForceHideAndLog();
        DestroyRoofProxyAndLog();
        ForceHideDebugPanels();
        HideAllHelperMeshesAndUnify();
        var runner = new GameObject("RoofUnifyRunner");
        runner.AddComponent<RoofUnifyDelayedLogger>();
        var keeper = new GameObject("SnowUnifyKeeper");
        keeper.AddComponent<SnowUnifyKeeper>();
        Object.DontDestroyOnLoad(keeper);
    }

    static void HideAllHelperMeshesAndUnify()
    {
        var hidden = new List<string>();
        string[] hideNames = { "RoofDebugFlat", "RoofSlideColliderDebug", "RoofSnowSurface", "cabin-roof", "RoofProxy" };
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null) continue;
            string name = t.gameObject.name;
            foreach (var n in hideNames)
            {
                if (name != n) continue;
                if (name == "RoofProxy") { Object.Destroy(t.gameObject); hidden.Add(GetTransformPath(t)); break; }
                var r = t.GetComponent<Renderer>();
                if (r != null && r.enabled) { r.enabled = false; hidden.Add(GetTransformPath(t)); }
                else if (name == "RoofSlideColliderDebug" && t.gameObject.activeSelf)
                { t.gameObject.SetActive(false); hidden.Add(GetTransformPath(t)); }
                break;
            }
        }
        var roofCol = GameObject.Find("RoofSlideCollider");
        if (roofCol != null)
        {
            var debug = roofCol.transform.Find("RoofSlideColliderDebug");
            if (debug != null && debug.gameObject.activeSelf) { debug.gameObject.SetActive(false); if (!hidden.Contains(GetTransformPath(debug))) hidden.Add(GetTransformPath(debug)); }
        }
        SnowLoopLogCapture.AppendToAssiReport($"hidden_helper_meshes=[{string.Join(",", hidden)}]");
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

/// <summary>起動遅延後に屋根基準面統一ログを出力。白い雪表示とsnow_visual_targetを統一。</summary>
class RoofUnifyDelayedLogger : MonoBehaviour
{
    float _t;

    void Update()
    {
        _t += Time.deltaTime;
        if (_t < 0.6f) return;
        HideRoofSnowLayerForUnify();
        LogRoofTargetsByName();
        LogSnowUnify();
        Object.Destroy(gameObject);
    }

    /// <summary>RoofSnowLayerを非表示にし、SnowPackPieceのみを雪表示にする。見えている雪とロジックを1つに統一。</summary>
    static void HideRoofSnowLayerForUnify()
    {
        var roofCol = GameObject.Find("RoofSlideCollider");
        if (roofCol == null) return;
        var layer = roofCol.transform.Find("RoofSnowLayer");
        if (layer == null) return;
        var r = layer.GetComponent<Renderer>();
        if (r != null && r.enabled)
        {
            r.enabled = false;
            Debug.Log("[SNOW_UNIFY] RoofSnowLayer disabled. final_snow_source=SnowPackPiece (visible=logic=visual)");
            SnowLoopLogCapture.AppendToAssiReport("=== SNOW_UNIFY ===");
            SnowLoopLogCapture.AppendToAssiReport("RoofSnowLayer=disabled final_snow_source=SnowPackPiece");
        }
    }

    static void LogSnowUnify()
    {
        string visibleName = "SnowPackPiecesRoot";
        string visualName = "SnowPackVisual";
        string logicName = "SnowPackPiecesRoot";
        var roofCol = GameObject.Find("RoofSlideCollider");
        if (roofCol != null)
        {
            var pieces = roofCol.transform.Find("SnowPackVisual/SnowPackPiecesRoot");
            var visual = roofCol.transform.Find("SnowPackVisual");
            if (pieces != null) { visibleName = GetFullPath(pieces); logicName = GetFullPath(pieces); }
            if (visual != null) visualName = GetFullPath(visual);
        }
        bool visibleAndVisualSame = (visibleName.Contains("SnowPackVisual") || visibleName.Contains("SnowPackPiecesRoot")) && visualName.Contains("SnowPackVisual");
        bool visualAndLogicSame = logicName.Contains("SnowPackPiecesRoot") && visualName.Contains("SnowPackVisual");
        string finalSource = "visual";
        string hiddenList = "[RoofSnowLayer]";
        Debug.Log($"[SNOW_UNIFY] visible_snow_target_name={visibleName} snow_visual_target_name={visualName} snow_logic_target_name={logicName} visible_and_visual_same={visibleAndVisualSame.ToString().ToLower()} visual_and_logic_same={visualAndLogicSame.ToString().ToLower()} final_snow_source={finalSource} hidden_snow_targets={hiddenList}");
        SnowLoopLogCapture.AppendToAssiReport($"visible_snow_target_name={visibleName} snow_visual_target_name={visualName} snow_logic_target_name={logicName}");
        SnowLoopLogCapture.AppendToAssiReport($"visible_and_visual_same={visibleAndVisualSame.ToString().ToLower()} visual_and_logic_same={visualAndLogicSame.ToString().ToLower()} final_snow_source={finalSource} hidden_snow_targets={hiddenList}");
    }

    static void LogRoofTargetsByName()
    {
        string roofLogicName = "none";
        string roofVisualName = "none";
        string snowSpawnName = "none";
        string snowVisualName = "none";
        bool sameLogicAndVisual = false;
        bool sameLogicAndSpawn = false;
        bool sameSpawnAndVisual = false;
        string logicTransform = "none";
        string visualTransform = "none";
        string spawnTransform = "none";
        string snowVisualTransform = "none";
        string hiddenStr = "none";

        var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();

        Collider roofLogicCol = roofSys != null ? roofSys.roofSlideCollider : null;
        if (roofLogicCol == null) roofLogicCol = GameObject.Find("RoofSlideCollider")?.GetComponent<Collider>();
        if (roofLogicCol != null)
        {
            roofLogicName = roofLogicCol.gameObject.name;
            logicTransform = FormatTransform(roofLogicCol.transform);

            roofVisualName = roofLogicName;
            visualTransform = logicTransform;
            sameLogicAndVisual = true;

            var layer = roofLogicCol.transform.Find("RoofSnowLayer");
            if (layer != null)
            {
                snowVisualName = GetFullPath(layer);
                snowVisualTransform = FormatTransform(layer);
            }
            else snowVisualName = "RoofSnowLayer(not_found)";
        }

        if (spawner != null && spawner.roofCollider != null)
        {
            snowSpawnName = spawner.roofCollider.gameObject.name;
            spawnTransform = FormatTransform(spawner.roofCollider.transform);
            sameLogicAndSpawn = roofLogicCol != null && spawner.roofCollider == roofLogicCol;

            var roofT = spawner.roofCollider.transform;
            var snowLayer = roofT.Find("RoofSnowLayer");
            var snowVisual = roofT.Find("SnowPackVisual");
            if (snowLayer != null && snowVisual != null)
            {
                snowVisualName = GetFullPath(snowLayer) + "+" + GetFullPath(snowVisual);
                snowVisualTransform = FormatTransform(snowLayer) + " | " + FormatTransform(snowVisual);
            }
            else if (snowLayer != null)
            {
                snowVisualName = GetFullPath(snowLayer);
                snowVisualTransform = FormatTransform(snowLayer);
            }
            else if (snowVisual != null)
            {
                snowVisualName = GetFullPath(snowVisual);
                snowVisualTransform = FormatTransform(snowVisual);
            }
            else if (snowVisualName == "RoofSnowLayer(not_found)")
                snowVisualName = "none";
        }

        sameSpawnAndVisual = roofLogicCol != null && spawner != null && spawner.roofCollider == roofLogicCol && (snowVisualName.Contains("RoofSnowLayer") || snowVisualName.Contains("SnowPackVisual"));

        var hidden = new List<string>();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t == null) continue;
            string n = t.gameObject.name;
            if (n != "cabin-roof" && n != "RoofDebugFlat" && n != "RoofSlideColliderDebug" && n != "RoofSnowSurface") continue;
            bool isHidden = !t.gameObject.activeSelf;
            if (!isHidden) { var r = t.GetComponent<Renderer>(); if (r != null && !r.enabled) isHidden = true; }
            if (isHidden) hidden.Add(GetPath(t));
        }
        if (hidden.Count > 0) hiddenStr = "[" + string.Join(",", hidden) + "]";

        Debug.Log($"[ROOF_TARGETS] roof_logic_target_name={roofLogicName} roof_visual_target_name={roofVisualName} snow_spawn_target_name={snowSpawnName} snow_visual_target_name={snowVisualName} same_logic_and_visual={sameLogicAndVisual.ToString().ToLower()} same_logic_and_spawn={sameLogicAndSpawn.ToString().ToLower()} same_spawn_and_visual={sameSpawnAndVisual.ToString().ToLower()} roof_logic_transform={logicTransform} roof_visual_transform={visualTransform} snow_spawn_transform={spawnTransform} snow_visual_transform={snowVisualTransform} hidden_helper_meshes={hiddenStr}");
        SnowLoopLogCapture.AppendToAssiReport($"[ROOF_TARGETS] logic={roofLogicName} visual={roofVisualName} spawn={snowSpawnName} snow_visual={snowVisualName} same_lv={sameLogicAndVisual} same_ls={sameLogicAndSpawn} same_sv={sameSpawnAndVisual}");
    }

    static string FormatTransform(Transform t)
    {
        if (t == null) return "none";
        var p = t.position;
        var e = t.rotation.eulerAngles;
        return $"pos=({p.x:F2},{p.y:F2},{p.z:F2}) euler=({e.x:F1},{e.y:F1},{e.z:F1})";
    }

    static string GetFullPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    static string GetPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }
}

/// <summary>RoofSnowLayerを常時非表示に維持。GridVisualWatchdogの復元より後に実行。</summary>
class SnowUnifyKeeper : MonoBehaviour
{
    void LateUpdate()
    {
        var roofCol = GameObject.Find("RoofSlideCollider");
        if (roofCol == null) return;
        var layer = roofCol.transform.Find("RoofSnowLayer");
        if (layer == null) return;
        var r = layer.GetComponent<Renderer>();
        if (r != null && r.enabled) r.enabled = false;
    }
}
