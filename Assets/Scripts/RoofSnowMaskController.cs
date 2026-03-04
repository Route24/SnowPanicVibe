using UnityEngine;

/// <summary>
/// Paints cleared patches (holes) on the roof snow mask. Used when LocalAvalanche removes snow.
/// UV mapping matches SnowPackSpawner.ComputeTapUV (roof-local u,v in [-0.5..0.5]).
/// </summary>
public class RoofSnowMaskController : MonoBehaviour
{
    [Tooltip("Mask resolution. Higher = sharper patches.")]
    public int maskResolution = 128;
    [Tooltip("UV radius of erase circle (relative to 0-1).")]
    [Range(0.02f, 0.3f)] public float eraseRadiusUV = 0.12f;
    [Tooltip("Soft edge falloff (0=hard, 1=soft).")]
    [Range(0f, 0.5f)] public float softEdge = 0.08f;

    Texture2D _mask;
    Material _mat;
    SnowPackSpawner _spawner;
    Collider _roofCollider;
    bool _initialized;
    public bool IsInitialized => _initialized;

    /// <summary>Initialize mask and material. Call from RoofSnowSystem when creating RoofSnowLayer.</summary>
    public void Init(Material roofMat, SnowPackSpawner spawner, Collider roofCollider)
    {
        if (_initialized && _mask != null) return;
        _spawner = spawner;
        _roofCollider = roofCollider;
        _mat = roofMat;
        if (_mat == null) return;
        if (_mask != null) Destroy(_mask);
        _mask = new Texture2D(maskResolution, maskResolution, TextureFormat.RGBA32, false);
        _mask.filterMode = FilterMode.Bilinear;
        _mask.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[maskResolution * maskResolution];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(255, 255, 255, 255); // R=1 = snow visible
        _mask.SetPixels32(pixels);
        _mask.Apply();
        _mat.SetTexture("_SnowMask", _mask);
        _initialized = true;
    }

    /// <summary>Paint a cleared circle at world position. Call from LocalAvalanche.</summary>
    public void PaintEraseAt(Vector3 worldPoint, float radiusWorldMeters = -1f)
    {
        if (_spawner == null) return;
        _spawner.ComputeTapUV(worldPoint, out float u, out float v);
        float rUV = radiusWorldMeters > 0f
            ? (radiusWorldMeters / Mathf.Max(0.01f, Mathf.Max(_spawner.RoofWidth, _spawner.RoofLength)))
            : eraseRadiusUV;
        if (!_initialized || _mask == null)
        {
            Debug.Log($"[MaskPaint] NOT_INIT uv=({u:F3},{v:F3}) R={rUV:F2} (mask not ready)");
            return;
        }
        PaintEraseUV(u, v, rUV);
    }

    /// <summary>Paint erase at UV (u,v in roof basis, roughly [-0.5..0.5]).</summary>
    public void PaintEraseUV(float u, float v, float radiusUV)
    {
        if (!_initialized || _mask == null) return;
        float texU = u + 0.5f;
        float texV = v + 0.5f;
        int cx = Mathf.Clamp(Mathf.RoundToInt(texU * maskResolution), 0, maskResolution - 1);
        int cy = Mathf.Clamp(Mathf.RoundToInt(texV * maskResolution), 0, maskResolution - 1);
        float radiusPx = radiusUV * maskResolution;
        int rad = Mathf.CeilToInt(radiusPx + softEdge * maskResolution) + 1;
        int paints = 0;
        for (int dy = -rad; dy <= rad; dy++)
        {
            for (int dx = -rad; dx <= rad; dx++)
            {
                int px = cx + dx, py = cy + dy;
                if (px < 0 || px >= maskResolution || py < 0 || py >= maskResolution) continue;
                float fu = (px + 0.5f) / maskResolution;
                float fv = (py + 0.5f) / maskResolution;
                float du = (fu - texU) * maskResolution;
                float dv = (fv - texV) * maskResolution;
                float dist = Mathf.Sqrt(du * du + dv * dv);
                float t = (dist - radiusPx) / Mathf.Max(1f, softEdge * maskResolution);
                float erase = 1f - Mathf.Clamp01(t); // smoothstep-like
                float prevF = _mask.GetPixel(px, py).r;
                byte prev = (byte)Mathf.Clamp(Mathf.RoundToInt(prevF * 255f), 0, 255);
                byte next = (byte)Mathf.Clamp(Mathf.RoundToInt(prevF * (1f - erase) * 255f), 0, 255);
                if (next < prev) paints++;
                _mask.SetPixel(px, py, new Color32(next, next, next, 255));
            }
        }
        if (paints > 0)
        {
            _mask.Apply();
            int pr = Mathf.RoundToInt(radiusPx);
            Debug.Log($"[MaskPaint] uv=({u:F3},{v:F3}) R={radiusUV:F2} px=({cx},{cy}) pr={pr}");
        }
    }

    void OnDestroy()
    {
        if (_mask != null) { Destroy(_mask); _mask = null; }
    }
}
