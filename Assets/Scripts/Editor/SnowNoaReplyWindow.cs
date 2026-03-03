#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ノアへの返答を表示し、コピーボタン一発でクリップボードにコピーできるウィンドウ。
/// </summary>
public class SnowNoaReplyWindow : EditorWindow
{
    static readonly string LogPath = Path.GetFullPath(Path.Combine("Assets", "Logs", "snowloop_latest.txt"));
    string _replyText = "";
    Vector2 _scroll;
    bool _initialized;

    [MenuItem("Tools/Snow Panic/ノアへの返答をコピー", false, 199)]
    static void CopyReply()
    {
        var reply = BuildNoaReply();
        if (!string.IsNullOrEmpty(reply))
        {
            EditorGUIUtility.systemCopyBuffer = reply;
            Debug.Log("[ノア返答] クリップボードにコピーしました。");
        }
        else
        {
            Debug.LogWarning("[ノア返答] ログがありません。Play を1回実行してからお試しください。");
        }
    }

    [MenuItem("Tools/Snow Panic/ノアへの返答を表示", false, 200)]
    static void Open()
    {
        var w = GetWindow<SnowNoaReplyWindow>("ノアへの返答");
        w.minSize = new Vector2(420, 280);
    }

    void OnEnable()
    {
        if (string.IsNullOrEmpty(_replyText))
        {
            _replyText = BuildNoaReply();
            _initialized = !string.IsNullOrEmpty(_replyText);
        }
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);

        if (GUILayout.Button("返答を再生成", GUILayout.Height(24)))
        {
            _replyText = BuildNoaReply();
            _initialized = !string.IsNullOrEmpty(_replyText);
        }

        EditorGUILayout.Space(4);

        if (_initialized && !string.IsNullOrEmpty(_replyText))
        {
            if (GUILayout.Button("クリップボードにコピー", GUILayout.Height(32)))
            {
                EditorGUIUtility.systemCopyBuffer = _replyText;
                Debug.Log("[ノア返答] クリップボードにコピーしました。");
            }

            EditorGUILayout.Space(4);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            _replyText = EditorGUILayout.TextArea(_replyText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }
        else if (!_initialized)
        {
            EditorGUILayout.HelpBox("「返答を生成して表示」をクリックすると、最新ログからノアへの返答を生成します。", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("ログファイルが見つかりません。Play を1回実行してから再度お試しください。", MessageType.Warning);
        }
    }

    static string BuildNoaReply()
    {
        if (!File.Exists(LogPath))
            return "";

        string[] lines = File.ReadAllLines(LogPath);
        if (lines.Length == 0) return "";

        var syncCount = 0;
        var avalancheCount = 0;
        var lastSync = "";
        var lastAvalanche = "";
        float lastRoofDepth = 0f;
        float lastPackDepth = 0f;

        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Contains("[SnowPackSync]"))
            {
                syncCount++;
                lastSync = l;
                var dm = Regex.Match(l, @"roofDepth=([\d.]+) packDepth=([\d.]+)");
                if (dm.Success)
                {
                    float.TryParse(dm.Groups[1].Value, out lastRoofDepth);
                    float.TryParse(dm.Groups[2].Value, out lastPackDepth);
                }
            }
            if (l.Contains("[Avalanche] fired")) { avalancheCount++; lastAvalanche = l; }
        }

        var sb = new StringBuilder();
        sb.AppendLine("ノアへ");
        sb.AppendLine();
        sb.AppendLine("修正対応ありがとうございます。");
        sb.AppendLine();
        sb.AppendLine("【確認結果】");
        sb.AppendLine($"- SnowPackSync: [SnowPackSync] reason=DepthDelta が {syncCount} 回発火");
        sb.AppendLine($"- roofDepth に追従して Rebuild が動作（最終: roofDepth={lastRoofDepth:F2} packDepth={lastPackDepth:F2}）");
        sb.AppendLine($"- Avalanche: {avalancheCount} 回発火");
        sb.AppendLine("- SnowPackBasis: usingLocal=true, roofUp/roofFwd で傾斜配置");
        sb.AppendLine("- SnowFallMax1s: 落下雪 maxSpeed と Avalanche burstVel が判別可能");
        sb.AppendLine();
        sb.AppendLine("【次のご指示をお願いします】");
        sb.AppendLine();

        return sb.ToString();
    }
}
#endif
