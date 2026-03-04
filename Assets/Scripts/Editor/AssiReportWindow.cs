#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// ASSI REPORT を1ウィンドウにまとめて表示。Play停止時に自動で開き、コピーボタンでノアにペーストして送信できる。
/// </summary>
public class AssiReportWindow : EditorWindow
{
    string _reportText = "";
    Vector2 _scroll;
    bool _hasContent;

    /// <summary>Play停止時に自動呼び出し。ノア用レポートを表示するウィンドウを開く。</summary>
    public static void OpenAndShowReport()
    {
        var w = GetWindow<AssiReportWindow>("ASSI Report");
        w.minSize = new Vector2(360, 300);
        w.LoadReport();
        w.Focus();
    }

    [MenuItem("Tools/ASSI Report (ノア送信用)", false, 300)]
    [MenuItem("SnowPanicVibe/ASSI Report (ノア送信用)", false, 300)]
    static void OpenFromMenu()
    {
        var w = GetWindow<AssiReportWindow>("ASSI Report");
        w.minSize = new Vector2(360, 300);
        w.LoadReport();
    }

    void LoadReport()
    {
        _reportText = SnowLoopNoaReportAutoCopy.GetReportContent();
        _hasContent = !string.IsNullOrEmpty(_reportText);
    }

    void OnEnable()
    {
        LoadReport();
    }

    void OnGUI()
    {
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("レポート専用カラム。タイトルバーをドラッグして Game/Scene 横にドロップ → 並べて表示", EditorStyles.miniLabel);
        EditorGUILayout.Space(2);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("コピー", GUILayout.Width(120), GUILayout.Height(32)))
        {
            if (_hasContent)
            {
                EditorGUIUtility.systemCopyBuffer = _reportText;
                Debug.Log("[ASSI] クリップボードにコピーしました。ノアにペーストして送信してください。");
            }
            else
            {
                Debug.LogWarning("[ASSI] レポートがありません。Play を実行して停止してください。");
            }
        }
        if (GUILayout.Button("再生成", GUILayout.Width(70), GUILayout.Height(24)))
        {
            if (SnowLoopNoaReportAutoCopy.BuildReport())
            {
                LoadReport();
                Debug.Log("[ASSI] 再生成しました。");
            }
            else
                Debug.LogWarning("[ASSI] ログがありません。Play 実行後に停止してください。");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        if (_hasContent)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var style = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextArea(_reportText, style, GUILayout.ExpandHeight(true));
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndScrollView();
        }
        else
        {
            EditorGUILayout.HelpBox(
                "レポートがありません。\nPlay を実行 → 停止 すると、このウィンドウにレポートが表示されます。\n「コピー」ボタンでノアにペーストして送信できます。",
                MessageType.Info);
        }
    }
}
#endif
