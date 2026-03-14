#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>ASSI プロトコル: Play確認後の必須記入。still_looks_like_blocks / still_falls_straight_down / play_confirmed / evidence_path を記入し、レポートに反映。</summary>
public class SnowPanicPlayVerificationWindow : EditorWindow
{
    const string VerificationPath = "Assets/Logs/play_verification_snow_mass.txt";

    string _stillLooksLikeBlocks = "";
    string _stillFallsStraightDown = "";
    string _playConfirmed = "";
    string _evidencePath = "";
    string _slideVisibleBeforeDrop = "";
    string _movesSidewaysOnRoof = "";
    string _contactExistsButSlideNotVisible = "";

    [MenuItem("Snow Panic/Play Verification (Snow Mass + Roof Slide)", false, 150)]
    public static void Open()
    {
        var w = GetWindow<SnowPanicPlayVerificationWindow>("Play Verification");
        w.minSize = new Vector2(420, 220);
    }

    void OnEnable()
    {
        Load();
    }

    void Load()
    {
        try
        {
            var path = GetVerificationPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (!File.Exists(path)) return;
            var lines = File.ReadAllLines(path);
            var d = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            if (d.TryGetValue("still_looks_like_blocks", out var v1)) _stillLooksLikeBlocks = v1;
            if (d.TryGetValue("still_falls_straight_down", out var v2)) _stillFallsStraightDown = v2;
            if (d.TryGetValue("play_confirmed", out var v3)) _playConfirmed = v3;
            if (d.TryGetValue("evidence_path", out var v4)) _evidencePath = v4;
            if (d.TryGetValue("slide_visible_before_drop", out var v5)) _slideVisibleBeforeDrop = v5;
            if (d.TryGetValue("moves_sideways_on_roof", out var v6)) _movesSidewaysOnRoof = v6;
            if (d.TryGetValue("contact_exists_but_slide_not_visible", out var v7)) _contactExistsButSlideNotVisible = v7;
        }
        catch { }
    }

    void Save()
    {
        try
        {
            var path = GetVerificationPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("still_looks_like_blocks=" + (_stillLooksLikeBlocks ?? "").Trim());
            sb.AppendLine("still_falls_straight_down=" + (_stillFallsStraightDown ?? "").Trim());
            sb.AppendLine("play_confirmed=" + (_playConfirmed ?? "").Trim());
            sb.AppendLine("evidence_path=" + (_evidencePath ?? "").Trim());
            sb.AppendLine("slide_visible_before_drop=" + (_slideVisibleBeforeDrop ?? "").Trim());
            sb.AppendLine("moves_sideways_on_roof=" + (_movesSidewaysOnRoof ?? "").Trim());
            sb.AppendLine("contact_exists_but_slide_not_visible=" + (_contactExistsButSlideNotVisible ?? "").Trim());
            File.WriteAllText(path, sb.ToString());
        }
        catch (Exception ex)
        {
            Debug.LogError("[PlayVerification] Save failed: " + ex.Message);
        }
    }

    void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "Play → 2回以上タップ → 雪崩発動 → 以下を記入 → Save\n" +
            "レポートは BuildReport 時にこのファイルを読み込みます。",
            MessageType.Info);
        EditorGUILayout.Space(4);

        _stillLooksLikeBlocks = EditorGUILayout.TextField("still_looks_like_blocks", _stillLooksLikeBlocks);
        EditorGUILayout.LabelField("", "YES または NO（ブロック群に見えるか）");
        EditorGUILayout.Space(2);

        _stillFallsStraightDown = EditorGUILayout.TextField("still_falls_straight_down", _stillFallsStraightDown);
        EditorGUILayout.LabelField("", "YES または NO（真下に即落ちるか）");
        EditorGUILayout.Space(2);

        _playConfirmed = EditorGUILayout.TextField("play_confirmed", _playConfirmed);
        EditorGUILayout.LabelField("", "YES または NO（2回以上タップ確認したか）");
        EditorGUILayout.Space(2);

        _evidencePath = EditorGUILayout.TextField("evidence_path", _evidencePath);
        EditorGUILayout.LabelField("", "gif または debug スクショのフルパス");
        EditorGUILayout.Space(2);

        _slideVisibleBeforeDrop = EditorGUILayout.TextField("slide_visible_before_drop", _slideVisibleBeforeDrop);
        EditorGUILayout.LabelField("", "YES/NO（downhill方向に滑ってから落ちたか）");
        EditorGUILayout.Space(2);
        _movesSidewaysOnRoof = EditorGUILayout.TextField("moves_sideways_on_roof", _movesSidewaysOnRoof);
        EditorGUILayout.LabelField("", "YES/NO（pieceは屋根面上を横移動しているか）");
        EditorGUILayout.Space(2);
        _contactExistsButSlideNotVisible = EditorGUILayout.TextField("contact_exists_but_slide_not_visible", _contactExistsButSlideNotVisible);
        EditorGUILayout.LabelField("", "YES/NO（接触はあるが見た目は落下か）");
        EditorGUILayout.Space(8);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Save", GUILayout.Height(28)))
        {
            Save();
            if (SnowLoopNoaReportAutoCopy.BuildReport())
            {
                Debug.Log("[PlayVerification] Saved. Report rebuilt.");
                AssiReportWindow.OpenAndShowReport();
            }
            else
            {
                Debug.Log("[PlayVerification] Saved. (Report rebuild skipped - Play once then Stop first)");
            }
        }
        if (GUILayout.Button("Load", GUILayout.Height(28)))
            Load();
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>Play Verification ファイルのパス。SnowLoopNoaReportAutoCopy が読み込みに使用。</summary>
    public static string GetVerificationPath() => Path.GetFullPath(Path.Combine(Environment.CurrentDirectory ?? ".", VerificationPath));

    /// <summary>レポート用。Play確認値が空の場合は (Play確認後記入) を返す。</summary>
    public static (string stillLooksLikeBlocks, string stillFallsStraightDown, string playConfirmed, string evidencePath) LoadVerification()
    {
        const string Placeholder = "(Play確認後記入)";
        try
        {
            var path = GetVerificationPath();
            if (!File.Exists(path))
                return (Placeholder, Placeholder, Placeholder, Placeholder);
            var lines = File.ReadAllLines(path);
            var d = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in lines)
            {
                var eq = line.IndexOf('=');
                if (eq > 0)
                    d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            string v(string k) => d.TryGetValue(k, out var x) && !string.IsNullOrWhiteSpace(x) ? x.Trim() : Placeholder;
            return (v("still_looks_like_blocks"), v("still_falls_straight_down"), v("play_confirmed"), v("evidence_path"));
        }
        catch
        {
            return (Placeholder, Placeholder, Placeholder, Placeholder);
        }
    }

    /// <summary>レポート用。指定キーの値を返す。無い場合は空文字。</summary>
    public static string GetVerificationValue(string key)
    {
        try
        {
            var path = GetVerificationPath();
            if (!File.Exists(path)) return "";
            foreach (var line in File.ReadAllLines(path))
            {
                var eq = line.IndexOf('=');
                if (eq > 0 && string.Equals(line.Substring(0, eq).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(eq + 1).Trim();
            }
        }
        catch { }
        return "";
    }
}
#endif
