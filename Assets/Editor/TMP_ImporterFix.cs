#if UNITY_EDITOR
using UnityEditor;

/// <summary>
/// TMP ImporterがPlayするたびに表示される問題の抑制。
/// 初回ロード時にTMP Essential Resourcesを非対話でインポートし、再表示を防ぐ。
/// </summary>
[InitializeOnLoad]
public static class TMP_ImporterFix
{
    const string PrefsKey = "SnowPanic_TMP_EssentialsImported";

    static TMP_ImporterFix()
    {
        EditorApplication.delayCall += EnsureTMPImported;
    }

    static void EnsureTMPImported()
    {
        if (EditorApplication.isPlaying) return;
        if (EditorPrefs.GetBool(PrefsKey, false)) return;

        var type = System.Type.GetType("TMPro.TMP_PackageResourceImporter, Unity.TextMeshPro")
            ?? System.Type.GetType("TMPro.TMP_PackageResourceImporter, Unity.TextMeshPro.Editor");
        if (type == null) return;

        var method = type.GetMethod("ImportResources",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            null,
            new[] { typeof(bool), typeof(bool), typeof(bool) },
            null);
        if (method == null) return;

        try
        {
            method.Invoke(null, new object[] { true, false, false }); // essentials, examples, interactive
            EditorPrefs.SetBool(PrefsKey, true);
        }
        catch
        {
            // インポート失敗時は次回に任せる（手動で Window > TextMeshPro > Import TMP Essential Resources を実行）
        }
    }
}
#endif
