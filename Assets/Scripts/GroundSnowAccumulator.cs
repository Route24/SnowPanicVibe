using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight ground snow accumulation on a 2D grid.
/// Converts impacts into persistent visual piles.
/// </summary>
public class GroundSnowAccumulator : MonoBehaviour
{
    public static GroundSnowAccumulator Instance { get; private set; }

    [Header("Grid")]
    public float cellSize = 0.5f;
    public Vector2 areaSize = new Vector2(12f, 12f);
    public float baseY = 0.0f;

    [Header("Deposit")]
    [Range(1, 2)] public int spreadRadiusCells = 1;
    public float spreadSigma = 0.8f;
    public float maxHeightPerCell = 1.5f;

    [Header("Visual")]
    public GameObject snowMoundPrefab;
    public float moundBaseScale = 0.45f;
    public float moundHeightScale = 1.0f;

    readonly Dictionary<Vector2Int, float> _heightByCell = new Dictionary<Vector2Int, float>();
    readonly Dictionary<Vector2Int, Transform> _visualByCell = new Dictionary<Vector2Int, Transform>();
    Material _fallbackMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureBootstrap()
    {
        if (FindFirstObjectByType<GroundSnowAccumulator>() != null) return;
        var go = new GameObject("GroundSnowAccumulator");
        go.transform.position = Vector3.zero;
        go.AddComponent<GroundSnowAccumulator>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void AddSnow(Vector3 worldPos, float amount)
    {
        if (amount <= 0f) return;
        if (!TryWorldToCell(worldPos, out Vector2Int centerCell)) return;

        int radius = Mathf.Max(0, spreadRadiusCells);
        float sigma = Mathf.Max(0.01f, spreadSigma);
        float sigma2 = 2f * sigma * sigma;

        for (int dz = -radius; dz <= radius; dz++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                Vector2Int c = new Vector2Int(centerCell.x + dx, centerCell.y + dz);
                if (!IsInsideCellBounds(c)) continue;

                float d2 = dx * dx + dz * dz;
                float w = Mathf.Exp(-d2 / sigma2);
                float add = amount * w;
                if (add <= 0.0001f) continue;

                float oldH = _heightByCell.TryGetValue(c, out float h) ? h : 0f;
                float newH = Mathf.Clamp(oldH + add, 0f, maxHeightPerCell);
                _heightByCell[c] = newH;
                UpdateCellVisual(c, newH);

                Debug.Log($"[GroundSnow] add pos={worldPos} cell=({c.x},{c.y}) amount={add:F3} newHeight={newH:F3}");
            }
        }
    }

    bool TryWorldToCell(Vector3 worldPos, out Vector2Int cell)
    {
        Vector3 local = worldPos - transform.position;
        float halfX = areaSize.x * 0.5f;
        float halfZ = areaSize.y * 0.5f;
        if (local.x < -halfX || local.x > halfX || local.z < -halfZ || local.z > halfZ)
        {
            cell = default;
            return false;
        }

        int cx = Mathf.FloorToInt((local.x + halfX) / Mathf.Max(0.01f, cellSize));
        int cz = Mathf.FloorToInt((local.z + halfZ) / Mathf.Max(0.01f, cellSize));
        cell = new Vector2Int(cx, cz);
        return true;
    }

    bool IsInsideCellBounds(Vector2Int cell)
    {
        int nx = Mathf.Max(1, Mathf.CeilToInt(areaSize.x / Mathf.Max(0.01f, cellSize)));
        int nz = Mathf.Max(1, Mathf.CeilToInt(areaSize.y / Mathf.Max(0.01f, cellSize)));
        return cell.x >= 0 && cell.x < nx && cell.y >= 0 && cell.y < nz;
    }

    Vector3 CellCenterWorld(Vector2Int cell)
    {
        float halfX = areaSize.x * 0.5f;
        float halfZ = areaSize.y * 0.5f;
        float x = -halfX + (cell.x + 0.5f) * cellSize;
        float z = -halfZ + (cell.y + 0.5f) * cellSize;
        return transform.position + new Vector3(x, baseY, z);
    }

    void UpdateCellVisual(Vector2Int cell, float height)
    {
        if (!_visualByCell.TryGetValue(cell, out Transform t) || t == null)
        {
            t = CreateMoundVisual(cell);
            _visualByCell[cell] = t;
        }

        Vector3 center = CellCenterWorld(cell);
        float yScale = Mathf.Max(0.03f, height * moundHeightScale);
        float xz = Mathf.Max(0.05f, cellSize * moundBaseScale);
        t.position = center + new Vector3(0f, yScale * 0.5f, 0f);
        t.localScale = new Vector3(xz, yScale, xz);
    }

    Transform CreateMoundVisual(Vector2Int cell)
    {
        GameObject go;
        if (snowMoundPrefab != null)
        {
            go = Instantiate(snowMoundPrefab, transform);
            go.name = $"SnowMound_{cell.x}_{cell.y}";
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"SnowMound_{cell.x}_{cell.y}";
            go.transform.SetParent(transform, false);
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (_fallbackMat == null)
                {
                    Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                    _fallbackMat = sh != null ? new Material(sh) : null;
                    if (_fallbackMat != null) _fallbackMat.color = new Color(0.92f, 0.95f, 1f, 1f);
                }
                if (_fallbackMat != null) r.sharedMaterial = _fallbackMat;
            }
        }
        return go.transform;
    }
}
