using UnityEngine;

/// <summary>
/// 積雪モジュール: 屋根アセットに直接依存せず RoofDefinition のみを参照。
/// 家が1軒でも6軒でも同じモジュールを使用可能。
/// </summary>
public static class SnowModule
{
    public const int MaxHouses = 6;
}

/// <summary>
/// 屋根のサイズ・傾斜・向きが決まれば積雪できる定義データ。
/// アセット変更時はこれを差し替えるだけで済む。
/// </summary>
public struct RoofDefinition
{
    public float width;
    public float depth;
    public float slopeAngle;
    public Vector3 slopeDirection;
    public Vector3 roofOrigin;
    public Vector3 roofNormal;
    public Vector3 roofR;
    public Vector3 roofF;
    public Vector3 roofDownhill;
    public bool isValid;

    public static RoofDefinition Invalid => default;

    public static RoofDefinition Create(float width, float depth, float slopeAngleDeg, Vector3 slopeDirection, Vector3 roofOrigin, Vector3 roofNormal)
    {
        Vector3 n = roofNormal.sqrMagnitude > 0.001f ? roofNormal.normalized : Vector3.up;
        if (Vector3.Dot(n, Vector3.up) < 0f) n = -n;

        Vector3 worldUp = Vector3.up;
        Vector3 r = Vector3.Cross(worldUp, n);
        if (r.sqrMagnitude < 1e-6f) r = Vector3.Cross(Vector3.forward, n);
        r.Normalize();
        Vector3 f = Vector3.Cross(n, r).normalized;
        Vector3 g = Physics.gravity.magnitude > 0.001f ? Physics.gravity.normalized : Vector3.down;
        Vector3 downhill = Vector3.ProjectOnPlane(g, n).normalized;
        if (downhill.sqrMagnitude < 1e-6f) downhill = -f;

        return new RoofDefinition
        {
            width = Mathf.Max(0.1f, width),
            depth = Mathf.Max(0.1f, depth),
            slopeAngle = slopeAngleDeg,
            slopeDirection = slopeDirection.sqrMagnitude > 0.001f ? slopeDirection.normalized : downhill,
            roofOrigin = roofOrigin,
            roofNormal = n,
            roofR = r,
            roofF = f,
            roofDownhill = downhill,
            isValid = true
        };
    }
}

/// <summary>
/// Collider から RoofDefinition を生成するリゾルバ。後方互換用。
/// </summary>
public static class RoofDefinitionResolver
{
    public static bool ResolveFromCollider(Collider roofCollider, Transform roofAngleReference, out RoofDefinition def)
    {
        def = RoofDefinition.Invalid;
        if (roofCollider == null) return false;

        var angleT = roofAngleReference != null ? roofAngleReference : roofCollider.transform;
        Vector3 rawN = angleT.up.normalized;
        if (Vector3.Dot(rawN, Vector3.up) < 0f) rawN = -rawN;

        Vector3 worldUp = Vector3.up;
        Vector3 r = Vector3.Cross(worldUp, rawN);
        if (r.sqrMagnitude < 1e-6f) r = Vector3.Cross(Vector3.forward, rawN);
        r.Normalize();
        Vector3 f = Vector3.Cross(rawN, r).normalized;
        Vector3 g = Physics.gravity.magnitude > 0.001f ? Physics.gravity.normalized : Vector3.down;
        Vector3 downhill = Vector3.ProjectOnPlane(g, rawN).normalized;
        if (downhill.sqrMagnitude < 1e-6f) downhill = -angleT.forward.normalized;

        Bounds b = roofCollider.bounds;
        Vector3 center = b.center;
        float projectedW, projectedL;

        if (roofCollider is BoxCollider box)
        {
            var t = roofCollider.transform;
            Vector3 c = box.center;
            float sx = box.size.x, sy = box.size.y, sz = box.size.z;
            float minR = float.MaxValue, maxR = float.MinValue, minF = float.MaxValue, maxF = float.MinValue;
            for (int ix = 0; ix <= 1; ix++)
            for (int iz = 0; iz <= 1; iz++)
            {
                Vector3 local = c + new Vector3((ix - 0.5f) * sx, sy * 0.5f, (iz - 0.5f) * sz);
                Vector3 world = t.TransformPoint(local);
                float cr = Vector3.Dot(world - center, r);
                float cf = Vector3.Dot(world - center, f);
                if (cr < minR) minR = cr; if (cr > maxR) maxR = cr;
                if (cf < minF) minF = cf; if (cf > maxF) maxF = cf;
            }
            projectedW = Mathf.Max(0.5f, maxR - minR);
            projectedL = Mathf.Max(0.5f, maxF - minF);
        }
        else
        {
            float minR = float.MaxValue, maxR = float.MinValue, minF = float.MaxValue, maxF = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + new Vector3(
                    (i & 1) != 0 ? b.extents.x : -b.extents.x,
                    (i & 2) != 0 ? b.extents.y : -b.extents.y,
                    (i & 4) != 0 ? b.extents.z : -b.extents.z);
                float cr = Vector3.Dot(corner - b.center, r);
                float cf = Vector3.Dot(corner - b.center, f);
                if (cr < minR) minR = cr; if (cr > maxR) maxR = cr;
                if (cf < minF) minF = cf; if (cf > maxF) maxF = cf;
            }
            projectedW = Mathf.Max(0.5f, maxR - minR);
            projectedL = Mathf.Max(0.5f, maxF - minF);
        }

        float slopeAngle = 90f - Vector3.Angle(rawN, Vector3.up);

        def = new RoofDefinition
        {
            width = projectedW,
            depth = projectedL,
            slopeAngle = slopeAngle,
            slopeDirection = downhill,
            roofOrigin = center,
            roofNormal = rawN,
            roofR = r,
            roofF = f,
            roofDownhill = downhill,
            isValid = true
        };
        return true;
    }
}

/// <summary>
/// RoofDefinition のホルダー。複数家対応のインデックス付き。
/// </summary>
public static class RoofDefinitionProvider
{
    static RoofDefinition[] _definitions = new RoofDefinition[SnowModule.MaxHouses];
    static bool[] _fromResolver = new bool[SnowModule.MaxHouses];
    static int _houseCount;

    public static int HouseCount => _houseCount;

    public static void Set(int houseIndex, RoofDefinition def, bool fromResolver)
    {
        if (houseIndex < 0 || houseIndex >= SnowModule.MaxHouses) return;
        _definitions[houseIndex] = def;
        _fromResolver[houseIndex] = fromResolver;
        if (houseIndex >= _houseCount) _houseCount = houseIndex + 1;
    }

    /// <summary>外部から RoofDefinition を注入。アセット非依存で雪を配置する際に使用。snow_module_asset_direct_dependency=false になる。</summary>
    public static void SetFromExternal(int houseIndex, RoofDefinition def)
    {
        Set(houseIndex, def, fromResolver: false);
    }

    public static bool TryGet(int houseIndex, out RoofDefinition def, out bool fromResolver)
    {
        def = RoofDefinition.Invalid;
        fromResolver = false;
        if (houseIndex < 0 || houseIndex >= SnowModule.MaxHouses) return false;
        def = _definitions[houseIndex];
        fromResolver = _fromResolver[houseIndex];
        return def.isValid;
    }

    public static void Clear(int houseIndex)
    {
        if (houseIndex >= 0 && houseIndex < SnowModule.MaxHouses)
        {
            _definitions[houseIndex] = RoofDefinition.Invalid;
            _fromResolver[houseIndex] = false;
        }
    }

    public static void ClearAll()
    {
        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            _definitions[i] = RoofDefinition.Invalid;
            _fromResolver[i] = false;
        }
        _houseCount = 0;
    }
}
