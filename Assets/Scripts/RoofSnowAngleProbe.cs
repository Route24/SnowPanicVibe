using UnityEngine;

/// <summary>雪と屋根の角度を毎秒ログ。原因切り分け・任意で自動補正。DebugTools か RoofRoot にアタッチ。</summary>
[DefaultExecutionOrder(100)]
public class RoofSnowAngleProbe : MonoBehaviour
{
    public bool autoFixOnStart = false;

    [Header("Debug Camera (屋根手前固定、Startで1回のみ)")]
    public bool debugCameraEnable = true;
    public float debugCamYawDeg = -35f;
    public bool debugCamYawInvert = false;
    public float debugCamDistance = 6.0f;
    public float debugCamHeight = 2.0f;
    public float debugCamLookAhead = 1.5f;
    public Transform debugCamTarget;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        if (Object.FindFirstObjectByType<RoofSnowAngleProbe>() != null) return;
        var go = GameObject.Find("DebugTools");
        if (go == null) go = GameObject.Find("RoofRoot");
        if (go != null) go.AddComponent<RoofSnowAngleProbe>();
    }

    Transform _snowPiecesRoot;
    Transform _snowVisual;
    Transform _roofRoot;
    Transform _roofYaw;
    Transform _roofSlideCollider;
    Transform _roofSnowLayer;
    Transform _roofVisualMesh;
    Transform _snowPieceSample;
    Camera _mainCam;
    bool _refsResolved;

    void Start()
    {
        ResolveRefs();

        if (autoFixOnStart)
            RunAutoFixOnce();

        if (debugCameraEnable)
            RunDebugCameraOnce();

        InvokeRepeating(nameof(LogAngles), 0.5f, 1f);
    }

    void OnDestroy()
    {
        CancelInvoke(nameof(LogAngles));
    }

    void ResolveRefs()
    {
        if (_refsResolved) return;
        _refsResolved = true;

        var go = GameObject.Find("SnowPackPiecesRoot");
        if (go != null) _snowPiecesRoot = go.transform;
        go = GameObject.Find("SnowPackVisual");
        if (go != null) _snowVisual = go.transform;

        go = GameObject.Find("RoofRoot");
        if (go != null) _roofRoot = go.transform;

        go = GameObject.Find("RoofYaw");
        if (go != null) _roofYaw = go.transform;

        go = GameObject.Find("RoofSlideCollider");
        if (go != null) _roofSlideCollider = go.transform;

        go = GameObject.Find("RoofSnowLayer");
        if (go != null) _roofSnowLayer = go.transform;

        _roofVisualMesh = ResolveRoofVisualMesh();

        var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null)
        {
            var list = spawner.GetAllPieceRenderers();
            if (list != null && list.Count > 0 && list[0] != null)
            {
                var t = list[0].transform;
                _snowPieceSample = t.name == "SnowPackPiece" ? t : t.parent;
            }
        }

        _mainCam = Camera.main;
        if (_mainCam == null)
            _mainCam = Object.FindFirstObjectByType<Camera>();
    }

    /// <summary>STEP2: RoofRoot配下でbounds最大のMeshRenderer。SnowPack*は除外。</summary>
    static Transform ResolveRoofVisualMesh()
    {
        var roof = GameObject.Find("RoofRoot");
        if (roof == null) return null;

        var exclude = new System.Collections.Generic.HashSet<Transform>();
        var v = GameObject.Find("SnowPackVisual");
        if (v != null) exclude.Add(v.transform);
        var pr = GameObject.Find("SnowPackPiecesRoot");
        if (pr != null)
        {
            exclude.Add(pr.transform);
            for (int i = 0; i < pr.transform.childCount; i++)
            {
                var c = pr.transform.GetChild(i);
                if (c != null && c.name == "SnowPackPiece") exclude.Add(c);
            }
        }

        MeshRenderer best = null;
        float bestSize = 0f;
        foreach (var mr in roof.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null) continue;
            if (exclude.Contains(mr.transform)) continue;
            float s = mr.bounds.size.x * mr.bounds.size.y * mr.bounds.size.z;
            if (s > bestSize) { bestSize = s; best = mr; }
        }
        return best != null ? best.transform : null;
    }

    void RunDebugCameraOnce()
    {
        if (_mainCam == null) return;

        Transform target = debugCamTarget != null ? debugCamTarget : _roofRoot;
        if (target == null) target = (_snowPiecesRoot != null ? _snowPiecesRoot : _snowVisual);
        if (target == null) return;

        float yaw = debugCamYawInvert ? -debugCamYawDeg : debugCamYawDeg;
        Vector3 dir = Vector3.forward;
        Vector3 offset = Quaternion.Euler(0f, yaw, 0f) * dir * debugCamDistance + Vector3.up * debugCamHeight;
        Vector3 camPos = target.position + offset;
        Vector3 lookAt = target.position + target.forward * debugCamLookAhead;
        Quaternion camRot = Quaternion.LookRotation(lookAt - camPos, Vector3.up);

        _mainCam.transform.SetPositionAndRotation(camPos, camRot);

        string targetName = target == _roofRoot ? "RoofRoot" : (target == _snowVisual ? "SnowPackVisual" : target.name);
        var e = camRot.eulerAngles;
        AppendAssiBlock("=== DEBUG CAMERA ===", $"camPos=({camPos.x:F2},{camPos.y:F2},{camPos.z:F2})", $"camEuler=({e.x:F1},{e.y:F1},{e.z:F1})", $"lookAt=({lookAt.x:F2},{lookAt.y:F2},{lookAt.z:F2})", $"target={targetName}", "usingForward=true");
    }

    void RunAutoFixOnce()
    {
        var snowRef = _snowPiecesRoot != null ? _snowPiecesRoot : _snowVisual;
        if (snowRef == null) return;

        var q = snowRef.rotation;
        var snowUp = snowRef.up.normalized;
        string changedTarget = "None";

        if (_roofRoot != null) { _roofRoot.rotation = q; changedTarget = "RoofRoot"; }

        float dotMesh = (_roofVisualMesh != null) ? Vector3.Dot(_roofVisualMesh.up.normalized, snowUp) : -1f;
        if (_roofVisualMesh != null && dotMesh < 0.999f)
        {
            _roofVisualMesh.rotation = q;
            changedTarget = "RoofMesh";
        }

        if (_roofSlideCollider != null)
        {
            _roofSlideCollider.rotation = q;
            if (changedTarget == "None") changedTarget = "Collider";
            else changedTarget += "/Collider";
        }

        var snowE = snowRef.rotation.eulerAngles;
        var rootE = _roofRoot != null ? _roofRoot.rotation.eulerAngles : Vector3.zero;
        var meshE = _roofVisualMesh != null ? _roofVisualMesh.rotation.eulerAngles : Vector3.zero;
        var colE = _roofSlideCollider != null ? _roofSlideCollider.rotation.eulerAngles : Vector3.zero;

        AppendAssiBlock("=== ANGLE FIX RESULT ===", $"snowEuler=({snowE.x:F1},{snowE.y:F1},{snowE.z:F1})", $"roofRootEuler_after=({rootE.x:F1},{rootE.y:F1},{rootE.z:F1})", $"roofMeshEuler_after=({meshE.x:F1},{meshE.y:F1},{meshE.z:F1})", $"colliderEuler_after=({colE.x:F1},{colE.y:F1},{colE.z:F1})", $"changedTarget={changedTarget}");
    }

    void LogAngles()
    {
        ResolveRefs();
        var snowRef = _snowPiecesRoot != null ? _snowPiecesRoot : _snowVisual;
        if (snowRef == null && _roofVisualMesh == null) return;

        var lines = new System.Collections.Generic.List<string> { "=== ANGLE MINI REPORT ===" };

        AppendEulerUp(lines, "RoofRoot", _roofRoot);
        AppendEulerUp(lines, "RoofYaw", _roofYaw);
        AppendEulerUp(lines, "RoofSlideCollider", _roofSlideCollider);
        AppendEulerUp(lines, "RoofSnowLayer", _roofSnowLayer);
        AppendEulerUp(lines, "SnowPackVisual", _snowVisual);
        AppendEulerUp(lines, "SnowPackPiecesRoot", _snowPiecesRoot);
        AppendEulerUp(lines, "SnowPackPiece", _snowPieceSample);
        if (_roofVisualMesh != null)
        {
            var e = _roofVisualMesh.rotation.eulerAngles;
            var u = _roofVisualMesh.up.normalized;
            lines.Add($"roofVisualMesh.name={_roofVisualMesh.name}");
            lines.Add($"roofVisualMesh.worldEuler=({e.x:F1},{e.y:F1},{e.z:F1}) roofVisualMesh.up=({u.x:F3},{u.y:F3},{u.z:F3})");
        }

        if (_roofVisualMesh != null)
        {
            var roofMeshUp = _roofVisualMesh.up.normalized;
            var snowT = _snowVisual != null ? _snowVisual : _snowPiecesRoot;
            if (snowT != null)
                lines.Add($"dot(roofMeshUp,SnowPackVisual.up)={Vector3.Dot(roofMeshUp, snowT.up.normalized):F4}");
            if (_roofSlideCollider != null)
                lines.Add($"dot(roofMeshUp,RoofSlideCollider.up)={Vector3.Dot(roofMeshUp, _roofSlideCollider.up.normalized):F4}");
        }

        AppendAssiBlock(lines.ToArray());
    }

    static void AppendEulerUp(System.Collections.Generic.List<string> lines, string label, Transform t)
    {
        if (t == null) { lines.Add($"{label}=null"); return; }
        var e = t.rotation.eulerAngles;
        var u = t.up.normalized;
        lines.Add($"{label}.worldEuler=({e.x:F1},{e.y:F1},{e.z:F1}) {label}.up=({u.x:F3},{u.y:F3},{u.z:F3})");
    }

    static void AppendAssiBlock(params string[] blockLines)
    {
        foreach (var s in blockLines)
            SnowLoopLogCapture.AppendToAssiReport(s);
    }
}
