using UnityEngine;
using System.Collections.Generic;

/// <summary>STEP1: 雪面法線をSnowPackPieceから推定し、RoofRoot/RoofSlideColliderをその法線に合わせる。茶色屋根を非表示、Colliderを可視化。</summary>
[DefaultExecutionOrder(150)]
public class RoofAlignToSnow : MonoBehaviour
{
    public bool alignOnStart = true;
    public int maxSamplePieces = 64;
    [Tooltip("false=RoofProxyを生成しない（白パネル点滅防止）")]
    public bool enableRoofProxy = false;
    [Tooltip("false=RoofSlideColliderDebug(青い板)を表示しない")]
    public bool enableColliderDebugVisible = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttach()
    {
        if (Object.FindFirstObjectByType<RoofAlignToSnow>() != null) return;
        var go = GameObject.Find("DebugTools");
        if (go == null) go = GameObject.Find("RoofRoot");
        if (go != null) go.AddComponent<RoofAlignToSnow>();
    }

    void Start()
    {
        if (alignOnStart)
            Invoke(nameof(RunAlign), 0.6f);
    }

    void RunAlign()
    {
        var roofRoot = GameObject.Find("RoofRoot");
        var roofCol = GameObject.Find("RoofSlideCollider");
        if (roofRoot == null || roofCol == null) return;

        Transform piecesRootT = null;
        var go = GameObject.Find("SnowPackPiecesRoot");
        if (go != null) piecesRootT = go.transform;
        if (piecesRootT == null) piecesRootT = roofCol.transform.Find("SnowPackVisual/SnowPackPiecesRoot");
        if (piecesRootT == null) return;

        var pieceList = new List<Transform>();
        for (int i = 0; i < piecesRootT.childCount && pieceList.Count < maxSamplePieces; i++)
        {
            var c = piecesRootT.GetChild(i);
            if (c != null && c.gameObject.activeSelf && (c.name == "SnowPackPiece" || c.name.StartsWith("SnowPack")))
                pieceList.Add(c);
        }

        if (pieceList.Count == 0) return;

        Vector3 avgRaw = Vector3.zero;
        Vector3 avgFixed = Vector3.zero;
        foreach (var p in pieceList)
        {
            Vector3 up = p.up.normalized;
            avgRaw += up;
            if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;
            avgFixed += up;
        }
        avgRaw /= pieceList.Count;
        avgFixed /= pieceList.Count;
        Vector3 snowNormal = avgFixed.normalized;
        if (snowNormal.sqrMagnitude < 0.001f) snowNormal = Vector3.up;

        var roofRootT = roofRoot.transform;
        var roofColT = roofCol.transform;
        var beforeEuler = roofRootT.rotation.eulerAngles;

        var delta = Quaternion.FromToRotation(roofRootT.up.normalized, snowNormal);
        roofRootT.rotation = delta * roofRootT.rotation;

        bool colUnderRoot = IsUnder(roofColT, roofRootT);
        if (!colUnderRoot)
        {
            var deltaCol = Quaternion.FromToRotation(roofColT.up.normalized, snowNormal);
            roofColT.rotation = deltaCol * roofColT.rotation;
        }

        var afterEuler = roofRootT.rotation.eulerAngles;
        var colEuler = roofColT.rotation.eulerAngles;
        float dotResult = Vector3.Dot(roofRootT.up.normalized, snowNormal);

        SnowLoopLogCapture.AppendToAssiReport("=== ROOF ALIGN TO SNOW ===");
        SnowLoopLogCapture.AppendToAssiReport($"sampledPieces={pieceList.Count}");
        SnowLoopLogCapture.AppendToAssiReport($"avgPieceUpRaw=({avgRaw.x:F3},{avgRaw.y:F3},{avgRaw.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"avgPieceUpFixed=({avgFixed.x:F3},{avgFixed.y:F3},{avgFixed.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"snowNormal=({snowNormal.x:F3},{snowNormal.y:F3},{snowNormal.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"RoofRoot.beforeEuler=({beforeEuler.x:F1},{beforeEuler.y:F1},{beforeEuler.z:F1})");
        SnowLoopLogCapture.AppendToAssiReport($"RoofRoot.afterEuler=({afterEuler.x:F1},{afterEuler.y:F1},{afterEuler.z:F1})");
        SnowLoopLogCapture.AppendToAssiReport($"RoofSlideCollider.afterEuler=({colEuler.x:F1},{colEuler.y:F1},{colEuler.z:F1})");
        SnowLoopLogCapture.AppendToAssiReport($"dot(RoofRoot.up,snowNormal)={dotResult:F4}");

        if (enableRoofProxy)
            CreateRoofProxy(pieceList, snowNormal, roofRootT);
        else
            DestroyRoofProxyAndLog();

        var cabinRoof = GameObject.Find("cabin-roof");
        if (cabinRoof != null)
        {
            var mr = cabinRoof.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }

        EnsureColliderVisible(roofCol);
        SnowLoopLogCapture.EmitRenderVisibilitySnapshot();
    }

    static void DestroyRoofProxyAndLog()
    {
        var existing = GameObject.Find("RoofProxy");
        bool found = existing != null;
        if (existing != null)
        {
            Object.Destroy(existing);
        }
        SnowLoopLogCapture.AppendToAssiReport("=== ROOF_PROXY_DISABLED ===");
        SnowLoopLogCapture.AppendToAssiReport($"found={found} destroyed={found}");
    }

    static bool IsUnder(Transform child, Transform ancestor)
    {
        var t = child;
        while (t != null)
        {
            if (t == ancestor) return true;
            t = t.parent;
        }
        return false;
    }

    static void CreateRoofProxy(List<Transform> pieces, Vector3 snowNormal, Transform roofRootT)
    {
        if (pieces == null || pieces.Count == 0) return;
        Vector3 worldUp = Vector3.up;
        Vector3 r = Vector3.Cross(worldUp, snowNormal);
        if (r.sqrMagnitude < 1e-6f) r = Vector3.Cross(Vector3.forward, snowNormal);
        r.Normalize();
        Vector3 f = Vector3.Cross(snowNormal, r).normalized;

        Vector3 centerSum = Vector3.zero;
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        int count = 0;
        foreach (var p in pieces)
        {
            if (p == null || !p.gameObject.activeSelf) continue;
            Vector3 pos = p.position;
            centerSum += pos;
            count++;
        }
        if (count == 0) return;
        Vector3 center = centerSum / count;
        float forwardOffset = 0.6f;
        var cam = Camera.main;
        if (cam != null)
        {
            Vector3 camForwardProj = Vector3.ProjectOnPlane(cam.transform.forward, snowNormal);
            if (camForwardProj.sqrMagnitude > 1e-6f)
            {
                camForwardProj.Normalize();
                Vector3 centerBefore = center;
                center += (-camForwardProj) * forwardOffset;
                SnowLoopLogCapture.AppendToAssiReport("=== ROOF PROXY FRONT OFFSET ===");
                SnowLoopLogCapture.AppendToAssiReport($"forwardOffset={forwardOffset}");
                SnowLoopLogCapture.AppendToAssiReport($"camForwardProj=({camForwardProj.x:F3},{camForwardProj.y:F3},{camForwardProj.z:F3})");
                SnowLoopLogCapture.AppendToAssiReport($"centerBefore=({centerBefore.x:F3},{centerBefore.y:F3},{centerBefore.z:F3}) centerAfter=({center.x:F3},{center.y:F3},{center.z:F3})");
            }
        }

        foreach (var p in pieces)
        {
            if (p == null || !p.gameObject.activeSelf) continue;
            Vector3 rel = p.position - center;
            float lx = Vector3.Dot(rel, r);
            float lz = Vector3.Dot(rel, f);
            if (lx < minX) minX = lx;
            if (lx > maxX) maxX = lx;
            if (lz < minZ) minZ = lz;
            if (lz > maxZ) maxZ = lz;
        }
        float width = Mathf.Max(0.5f, maxX - minX);
        float length = Mathf.Max(0.5f, maxZ - minZ);

        Vector3 roofForwardProj = Vector3.ProjectOnPlane(roofRootT.forward, snowNormal);
        if (roofForwardProj.sqrMagnitude < 1e-6f) roofForwardProj = f;
        roofForwardProj.Normalize();
        Quaternion rot = Quaternion.LookRotation(roofForwardProj, snowNormal);
        const float thickness = 0.02f;

        var existing = GameObject.Find("RoofProxy");
        if (existing != null) Object.Destroy(existing);

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "RoofProxy";
        go.transform.position = center;
        go.transform.rotation = rot;
        go.transform.localScale = new Vector3(width, thickness, length);
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            var unlit = Shader.Find("Unlit/Color");
            if (unlit != null)
            {
                var m = new Material(unlit);
                MaterialColorHelper.SetColorSafe(m, Color.blue); // テスト用に青
                rend.sharedMaterial = m;
                rend.enabled = true;
            }
            else
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    var m = new Material(shader);
                    MaterialColorHelper.SetColorSafe(m, Color.blue); // テスト用に青
                    rend.sharedMaterial = m;
                }
                rend.enabled = true;
            }
        }
        go.layer = 0;
        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer >= 0) go.layer = defaultLayer;
        bool cullingHit = (Camera.main != null && (Camera.main.cullingMask & (1 << go.layer)) != 0);
        SnowLoopLogCapture.AppendToAssiReport("=== ROOF PROXY VIS FORCE ===");
        SnowLoopLogCapture.AppendToAssiReport($"layer={go.layer} cullingMaskHit={cullingHit}");
        SnowLoopLogCapture.AppendToAssiReport($"mat={(rend != null && rend.sharedMaterial != null ? rend.sharedMaterial.name : "?")}");
        SnowLoopLogCapture.AppendToAssiReport("forcedVisible=true");

        SnowLoopLogCapture.AppendToAssiReport("=== ROOF PROXY ===");
        SnowLoopLogCapture.AppendToAssiReport($"snowNormal=({snowNormal.x:F3},{snowNormal.y:F3},{snowNormal.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"projMin=({minX:F3},{minZ:F3}) projMax=({maxX:F3},{maxZ:F3}) (planeLocal)");
        SnowLoopLogCapture.AppendToAssiReport($"width={width:F3} length={length:F3} center=({center.x:F3},{center.y:F3},{center.z:F3})");
        SnowLoopLogCapture.AppendToAssiReport($"RoofProxy.pos=({center.x:F3},{center.y:F3},{center.z:F3}) euler=({go.transform.eulerAngles.x:F1},{go.transform.eulerAngles.y:F1},{go.transform.eulerAngles.z:F1}) scale=({width:F3},{thickness:F3},{length:F3})");
    }

    void EnsureColliderVisible(GameObject roofColGo)
    {
        if (roofColGo == null) return;
        var debug = roofColGo.transform.Find("RoofSlideColliderDebug");
        if (debug != null)
        {
            debug.gameObject.SetActive(enableColliderDebugVisible);
            return;
        }
        if (!enableColliderDebugVisible) return;

        var box = roofColGo.GetComponent<BoxCollider>();
        if (box == null) return;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "RoofSlideColliderDebug";
        go.transform.SetParent(roofColGo.transform, false);
        var c = go.GetComponent<Collider>();
        if (c != null) c.enabled = false;
        go.transform.localPosition = box.center;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = box.size;
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            MaterialColorHelper.SetColorSafe(mat, new Color(0f, 0.8f, 1f));
            r.sharedMaterial = mat;
        }
    }
}
