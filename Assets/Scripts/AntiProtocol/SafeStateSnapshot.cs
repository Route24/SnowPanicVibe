using UnityEngine;

/// <summary>
/// Safe 基準の識別子を静かに保持するだけ。判定・復元・ログなし。
/// </summary>
public sealed class SafeStateSnapshot : MonoBehaviour
{
    internal const string SafeStateVersion = "TASK_SNOW_SHAPE_MEASURED";

    // ── TASK_SNOW_SHAPE_MEASURED 基準値（TASK-31B 実測値で正式固定）──────
    // Play実測値をそのまま保存。推定値は使用しない。
    // snow_bounds_size_y  = 5.896  許容: ±10% (5.306〜6.486)
    // snow_overhang_above = 1.666  許容: ±20% (1.333〜1.999)
    // snow_overhang_below = 0.173  許容: ±20% (0.000〜0.208)
    // snow_overhang_front = 0.000  許容: 0〜0.15
    // snow_child_count    = 12
    internal const float SafeSnowSizeY          = 5.896f;
    internal const float SafeSnowOverhangAbove  = 1.666f;
    internal const float SafeSnowOverhangBelow  = 0.173f;
    internal const float SafeSnowOverhangFront  = 0.000f;
    internal const int   SafeSnowChildCount     = 12;
}
