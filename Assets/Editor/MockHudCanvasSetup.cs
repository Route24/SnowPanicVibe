#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

/// <summary>
/// 助成金企画書用 UI モックを現在のシーンに追加・削除するメニュー。
/// SnowPanic → UI Mock → Add Mock HUD Canvas
/// </summary>
public static class MockHudCanvasSetup
{
    const string GoName = "MockHudCanvas";

    // ── 追加 ──────────────────────────────────────────
    [MenuItem("SnowPanic/UI Mock/Add Mock HUD Canvas", false, 200)]
    public static void AddMockHud()
    {
        var existing = GameObject.Find(GoName);
        if (existing != null)
        {
            Debug.Log($"[MockHudCanvasSetup] already_exists=YES – GameObject '{GoName}' は既に存在します。");
            Selection.activeGameObject = existing;
            return;
        }

        var go = new GameObject(GoName);
        var mock = go.AddComponent<MockHudCanvas>();

        // Undo に登録（Ctrl+Z で戻せる）
        Undo.RegisterCreatedObjectUndo(go, "Add Mock HUD Canvas");
        Selection.activeGameObject = go;

        // シーンを dirty にして Game View に即反映
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        Debug.Log($"[MockHudCanvasSetup] canvas_created=YES name={GoName}");
        EditorUtility.DisplayDialog(
            "Mock HUD Canvas",
            $"'{GoName}' をシーンに追加しました。\nGame View で確認してください（Play 不要）。",
            "OK");
    }

    // ── 削除 ──────────────────────────────────────────
    [MenuItem("SnowPanic/UI Mock/Remove Mock HUD Canvas", false, 201)]
    public static void RemoveMockHud()
    {
        var existing = GameObject.Find(GoName);
        if (existing == null)
        {
            Debug.Log($"[MockHudCanvasSetup] not_found=YES – '{GoName}' は見つかりません。");
            return;
        }
        Undo.DestroyObjectImmediate(existing);
        Debug.Log($"[MockHudCanvasSetup] removed=YES name={GoName}");
    }

    // ── バリデーション（シーンが開いていないと無効化） ──
    [MenuItem("SnowPanic/UI Mock/Add Mock HUD Canvas", true)]
    static bool ValidateAdd()
    {
        return UnityEngine.SceneManagement.SceneManager.GetActiveScene().isLoaded;
    }

    [MenuItem("SnowPanic/UI Mock/Remove Mock HUD Canvas", true)]
    static bool ValidateRemove()
    {
        return GameObject.Find(GoName) != null;
    }
}
#endif
