using UnityEngine;

public static class AssiProtocol
{
    public const string PROTOCOL_NAME = "SNOWPANIC_ASSI_MASTER";
    public const string PROTOCOL_VERSION = "1.0";

    public static readonly string[] CORE_EDIT_RULES =
    {
        "Edit only minimal lines",
        "Do not rewrite entire files",
        "Do not modify unrelated systems",
        "Preserve architecture",
        "Return minimal patch",
        "Never regenerate working systems",
        "Never revert confirmed behaviour"
    };

    public static readonly string[] PROTECTED_FILES =
    {
        "SnowPackSpawner.cs",
        "RoofSnowSystem.cs",
        "SnowPackFallingPiece.cs",
        "SnowVisual.cs",
        "SnowPhysicsScoreManager.cs"
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        Debug.Log($"[ASSI_PROTOCOL_ACTIVE] {PROTOCOL_NAME} v{PROTOCOL_VERSION}");

        foreach (var rule in CORE_EDIT_RULES)
        {
            Debug.Log("[ASSI_RULE] " + rule);
        }

        Debug.Log("[ASSI_PROTECTED_FILES] " + string.Join(", ", PROTECTED_FILES));
    }
}
