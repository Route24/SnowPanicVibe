using System.IO;
using UnityEngine;

/// <summary>屋根全面積雪レポート用。Runtime で値を作成し、Editor の WriteSessionData が参照。</summary>
public static class RoofSnowReportWriter
{
    const string FileName = "roof_snow_report.txt";

    public static void Write(string roofSurfaceSize, string snowCoverSize, bool snowCoverMatchesRoof)
    {
        try
        {
            var dir = GetRecordingsDir();
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, FileName);
            var lines = new[]
            {
                "roof_surface_size=" + (roofSurfaceSize ?? "(pending)"),
                "snow_cover_size=" + (snowCoverSize ?? "(pending)"),
                "snow_cover_matches_roof=" + snowCoverMatchesRoof.ToString().ToLower()
            };
            File.WriteAllText(path, string.Join("\n", lines));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[RoofSnowReportWriter] write failed: " + ex.Message);
        }
    }

    static string GetRecordingsDir()
    {
        var dataPath = Application.dataPath;
        if (string.IsNullOrEmpty(dataPath)) return null;
        return Path.GetFullPath(Path.Combine(dataPath, "..", "Recordings"));
    }
}
