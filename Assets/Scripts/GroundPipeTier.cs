using UnityEngine;

/// <summary>
/// GROUND PIPE TIER HELPER
/// SnowFallSystem の ground hit 時に tier 判定と ground Y 計算を提供する。
/// hit.point の X 座標を upper/lower 各 roof の roofOrigin.x 平均と比較して tier を決定する。
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

    // hit.point の Y 座標を使って tier を判定する
    // upper roofs の roofOrigin.y 平均 vs lower roofs の roofOrigin.y 平均の中間値で分類
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

        float upperAvgY = upperYSum / upperCount;
        float lowerAvgY = lowerYSum / lowerCount;
        float midY = (upperAvgY + lowerAvgY) * 0.5f;

        return (hitPoint.y >= midY) ? "upper" : "lower";
    }

    // tier 別の ground Y を計算する（roofOrigin.y 平均 - LANDING_DROP）
    public static float GetGroundY(string tier)
    {
        float ySum = 0f; int count = 0;
        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            if (!RoofDefinitionProvider.TryGet(i, out var d, out _) || !d.isValid) continue;
            if (GetTierByName(RoofIds[i]) == tier)
            {
                ySum += d.roofOrigin.y;
                count++;
            }
        }
        if (count == 0) return float.NegativeInfinity;
        return (ySum / count) - LANDING_DROP;
    }
}
