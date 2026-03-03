using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual-only roof snow pack generator.
/// Place this on one GameObject and assign roof collider.
/// </summary>
public class SnowPackSpawner : MonoBehaviour
{
    [Header("Target")]
    public Collider roofCollider;

    [Header("Look")]
    [Range(0.1f, 1.5f)] public float targetDepthMeters = 0.5f;
    [Range(0.05f, 0.5f)] public float pieceSize = 0.11f;
    [Range(0.5f, 2f)] public float pieceHeightScale = 0.85f;
    [Range(0f, 0.08f)] public float jitter = 0.03f;
    [Range(0f, 0.06f)] public float normalInset = 0.01f;
    public int maxPieces = 1800;
    public bool rebuildOnPlay = true;

    [Header("Material")]
    public Color snowColor = new Color(0.93f, 0.96f, 1f, 1f);

    Transform _visualRoot;
    Material _snowMat;
    bool _generatedThisPlay;

    void Start()
    {
        if (!Application.isPlaying || !rebuildOnPlay || _generatedThisPlay) return;
        _generatedThisPlay = true;
        Rebuild();
    }

    [ContextMenu("Rebuild Snow Pack")]
    public void Rebuild()
    {
        if (roofCollider == null)
            roofCollider = ResolveRoofCollider();
        if (roofCollider == null)
        {
            Debug.LogWarning("[SnowPack] roofCollider is not assigned.");
            return;
        }

        EnsureRoot();
        ClearChildren(_visualRoot);
        EnsureMaterial();

        Vector3 roofUp = roofCollider.transform.up.normalized;
        Vector3 axisA = Vector3.ProjectOnPlane(roofCollider.transform.right, roofUp).normalized;
        Vector3 axisB = Vector3.ProjectOnPlane(roofCollider.transform.forward, roofUp).normalized;
        if (axisA.sqrMagnitude < 0.001f) axisA = Vector3.Cross(roofUp, Vector3.forward).normalized;
        if (axisB.sqrMagnitude < 0.001f) axisB = Vector3.Cross(axisA, roofUp).normalized;

        Bounds b = roofCollider.bounds;
        float size = Mathf.Max(0.05f, pieceSize);
        int nx = Mathf.Max(1, Mathf.CeilToInt(b.size.x / size));
        int nz = Mathf.Max(1, Mathf.CeilToInt(b.size.z / size));
        int layers = Mathf.Max(1, Mathf.CeilToInt(targetDepthMeters / Mathf.Max(0.02f, size * pieceHeightScale)));

        int spawned = 0;
        Vector3 center = b.center;
        float halfX = b.extents.x;
        float halfZ = b.extents.z;
        for (int y = 0; y < layers; y++)
        {
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    if (spawned >= maxPieces) break;
                    float tx = nx <= 1 ? 0.5f : ix / (float)(nx - 1);
                    float tz = nz <= 1 ? 0.5f : iz / (float)(nz - 1);

                    Vector3 p = center
                        + axisA * Mathf.Lerp(-halfX, halfX, tx)
                        + axisB * Mathf.Lerp(-halfZ, halfZ, tz)
                        + roofUp * (y * size * pieceHeightScale + normalInset);

                    // Keep visuals only on the roof area.
                    Vector3 cp = roofCollider.ClosestPoint(p + roofUp * 0.2f);
                    if ((cp - p).sqrMagnitude > 0.25f) continue;
                    p = cp + roofUp * (y * size * pieceHeightScale + normalInset);

                    p += axisA * Random.Range(-jitter, jitter) + axisB * Random.Range(-jitter, jitter);
                    SpawnPiece(p, roofUp, size);
                    spawned++;
                }
                if (spawned >= maxPieces) break;
            }
            if (spawned >= maxPieces) break;
        }

        Debug.Log($"[SnowPack] generated={spawned} depth={targetDepthMeters:F2} pieceSize={size:F2}");
    }

    [ContextMenu("Clear Snow Pack")]
    public void ClearNow()
    {
        EnsureRoot();
        ClearChildren(_visualRoot);
        Debug.Log("[SnowPack] cleared");
    }

    void SpawnPiece(Vector3 worldPos, Vector3 roofUp, float size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "SnowPackPiece";
        go.transform.SetParent(_visualRoot, true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.forward, roofUp), roofUp);
        float h = Mathf.Max(0.03f, size * pieceHeightScale * Random.Range(0.8f, 1.2f));
        float w = Mathf.Max(0.03f, size * Random.Range(0.8f, 1.15f));
        go.transform.localScale = new Vector3(w, h, w);
        go.layer = LayerMask.NameToLayer("Ignore Raycast") >= 0 ? LayerMask.NameToLayer("Ignore Raycast") : 2;

        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        var r = go.GetComponent<Renderer>();
        if (r != null && _snowMat != null) r.sharedMaterial = _snowMat;
    }

    void EnsureRoot()
    {
        var t = transform.Find("SnowPackVisual");
        if (t == null)
        {
            var go = new GameObject("SnowPackVisual");
            go.transform.SetParent(transform, false);
            t = go.transform;
        }
        _visualRoot = t;
    }

    void EnsureMaterial()
    {
        if (_snowMat != null) return;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (sh == null) return;
        _snowMat = new Material(sh);
        _snowMat.color = snowColor;
    }

    Collider ResolveRoofCollider()
    {
        var byName = GameObject.Find("RoofSlideCollider");
        if (byName != null) return byName.GetComponent<Collider>();
        var all = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (c.name.Contains("RoofSlideCollider")) return c;
        }
        return null;
    }

    static void ClearChildren(Transform root)
    {
        if (root == null) return;
        var toDelete = new List<GameObject>();
        for (int i = 0; i < root.childCount; i++)
            toDelete.Add(root.GetChild(i).gameObject);
        for (int i = 0; i < toDelete.Count; i++)
        {
            if (Application.isPlaying) Object.Destroy(toDelete[i]);
            else Object.DestroyImmediate(toDelete[i]);
        }
    }
}
