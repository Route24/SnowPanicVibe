using UnityEngine;

/// <summary>
/// GROUND PIPE TIER HELPER
/// SnowFallSystem の ground hit 時に tier 判定と ground Y 計算を提供する。
///
/// tier 判定: hit.point の Y 座標を upper/lower 屋根 roofOrigin.y の中間値で分類。
/// ground Y : hit.collider.bounds.max.y を直接使用（LANDING_DROP 固定値に依存しない）。
///            コライダーが取れない場合のみ roofOrigin.y 平均 - LANDING_DROP にフォールバック。
/// </summary>
public static class GroundPipeTier
{
    static readonly string[] RoofIds = { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };
    const float LANDING_DROP = 1.5f;

    static string GetTierByName(string roofId)
    {
        switch (roofId)
        {
            case "Roof_TL": case "Roof_TM": case "Roof_TR": return "upper";
            default: return "lower";
        }
    }

    // hit.point の Y 座標で upper/lower を判定する
    // upper roofOrigin.y 平均 と lower roofOrigin.y 平均の中間値で分類
    public static string GetTierByPosition(Vector3 hitPoint)
    {
        float upperYSum = 0f; int upperCount = 0;
        float lowerYSum = 0f; int lowerCount = 0;

        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            if (!RoofDefinitionProvider.TryGet(i, out var d, out _) || !d.isValid) continue;
            string t = GetTierByName(RoofIds[i]);
            if (t == "upper") { upperYSum += d.roofOrigin.y; upperCount++; }
            else              { lowerYSum += d.roofOrigin.y; lowerCount++; }
        }

        if (upperCount == 0 && lowerCount == 0) return "lower";
        if (upperCount == 0) return "lower";
        if (lowerCount == 0) return "upper";

        float midY = ((upperYSum / upperCount) + (lowerYSum / lowerCount)) * 0.5f;
        return (hitPoint.y >= midY) ? "upper" : "lower";
    }

    /// <summary>
    /// tier に対応する地面 Y 座標を返す。
    /// hit した Collider の bounds.max.y を直接使用する。
    /// hitCollider が null の場合は roofOrigin.y 平均 - LANDING_DROP にフォールバック。
    /// </summary>
    public static float GetGroundY(string tier, Collider hitCollider = null)
    {
        // ── 優先: hit したコライダーの上面 Y をそのまま使う ──
        if (hitCollider != null)
        {
            float surfaceY = hitCollider.bounds.max.y;
            Debug.Log($"[SNOW_GROUND_RESOLVE] tier={tier} method=collider_bounds_max"
                + $" collider={hitCollider.name} ground_y={surfaceY:F3}"
                + " offscreen_respawn=NO river_respawn=NO");
            return surfaceY;
        }

        // ── フォールバック: roofOrigin.y 平均 - LANDING_DROP ──
        float ySum = 0f; int count = 0;
        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            if (!RoofDefinitionProvider.TryGet(i, out var d, out _) || !d.isValid) continue;
            if (GetTierByName(RoofIds[i]) == tier) { ySum += d.roofOrigin.y; count++; }
        }
        if (count == 0) return float.NegativeInfinity;
        float fallbackY = (ySum / count) - LANDING_DROP;
        Debug.Log($"[SNOW_GROUND_RESOLVE] tier={tier} method=fallback_landing_drop"
            + $" ground_y={fallbackY:F3}"
            + " offscreen_respawn=NO river_respawn=NO");
        return fallbackY;
    }
}
