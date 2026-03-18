using System.IO;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// WORK_SNOW シーン専用。
/// 【モード: ALL_6_ROOFS_ROUGH_FIT】
///
/// RoofGuide_TL〜BR の RectTransform anchorMin/anchorMax を
/// RoofCalibrationData.json の各屋根座標に合わせて更新し、
/// Image 色を白（雪色）にする。
///
/// Canvas は Screen Space Overlay (RenderMode=0)。
/// キャリブレーションデータの座標系: x=左0→右1, y=上0→下1（画像座標）
/// Canvas の anchorMin/anchorMax: x=左0→右1, y=下0→上1（UI座標）
/// 変換: anchorY = 1 - calibY
/// </summary>
[ExecuteAlways]
public class WorkSnowForcer : MonoBehaviour
{
    const string CALIB_PATH = "Assets/Art/RoofCalibrationData.json";

    // キャリブレーション ID → RoofGuide オブジェクト名 の対応
    static readonly (string calibId, string guideId)[] RoofPairs =
    {
        ("Roof_TL", "RoofGuide_TL"),
        ("Roof_TM", "RoofGuide_TM"),
        ("Roof_TR", "RoofGuide_TR"),
        ("Roof_BL", "RoofGuide_BL"),
        ("Roof_BM", "RoofGuide_BM"),
        ("Roof_BR", "RoofGuide_BR"),
    };

    static readonly Color SnowWhite = new Color(0.93f, 0.96f, 1.0f, 0.95f);

    bool _applied = false;

    [System.Serializable] class V2 { public float x, y; }
    [System.Serializable] class RoofEntry
    {
        public string id;
        public V2 topLeft, topRight, bottomRight, bottomLeft;
        public bool confirmed;
    }
    [System.Serializable] class SaveData { public RoofEntry[] roofs; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (!scene.Contains("WORK_SNOW")) return;
        if (Object.FindFirstObjectByType<WorkSnowForcer>() != null) return;

        var bgGo = GameObject.Find("BackgroundImage");
        if (bgGo != null)
        {
            bgGo.AddComponent<WorkSnowForcer>();
            Debug.Log($"[ALL6_SNOW_FIT] Bootstrap attached to BackgroundImage scene={scene}");
        }
        else
        {
            var go = new GameObject("WorkSnowForcer_Root");
            go.AddComponent<WorkSnowForcer>();
            Debug.Log($"[ALL6_SNOW_FIT] Bootstrap created root scene={scene}");
        }
    }

    void OnEnable() { _applied = false; }
    void Start()    { Apply(); }
    void Update()   { if (!_applied) Apply(); }

    void Apply()
    {
        // RoofGuideCanvas をアクティブに保つ
        var canvas = GameObject.Find("RoofGuideCanvas");
        if (canvas != null && !canvas.activeSelf)
        {
            canvas.SetActive(true);
            Debug.Log("[ALL6_SNOW_FIT] RoofGuideCanvas re-activated");
        }

        if (!File.Exists(CALIB_PATH))
        {
            Debug.LogWarning($"[ALL6_SNOW_FIT] calib not found: {CALIB_PATH}");
            return;
        }

        var sd = JsonUtility.FromJson<SaveData>(File.ReadAllText(CALIB_PATH));
        if (sd == null || sd.roofs == null)
        {
            Debug.LogWarning("[ALL6_SNOW_FIT] JSON parse failed");
            return;
        }

        int successCount = 0;
        var sb = new System.Text.StringBuilder();

        foreach (var (calibId, guideId) in RoofPairs)
        {
            // キャリブレーションデータを探す
            RoofEntry entry = null;
            foreach (var r in sd.roofs)
                if (r.id == calibId) { entry = r; break; }

            if (entry == null || !entry.confirmed)
            {
                Debug.LogWarning($"[ALL6_SNOW_FIT] {calibId} not found or not confirmed");
                sb.AppendLine($"  {guideId}: SKIP(no calib)");
                continue;
            }

            // 4点 bbox
            float minX = Mathf.Min(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
            float maxX = Mathf.Max(entry.topLeft.x, entry.topRight.x, entry.bottomRight.x, entry.bottomLeft.x);
            float minY = Mathf.Min(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);
            float maxY = Mathf.Max(entry.topLeft.y, entry.topRight.y, entry.bottomRight.y, entry.bottomLeft.y);

            // Canvas UI 座標に変換（y 軸反転）
            var anchorMin = new Vector2(minX, 1f - maxY);
            var anchorMax = new Vector2(maxX, 1f - minY);

            // RoofGuide オブジェクトを取得
            var guideGo = GameObject.Find(guideId);
            if (guideGo == null)
            {
                Debug.LogWarning($"[ALL6_SNOW_FIT] {guideId} not found");
                sb.AppendLine($"  {guideId}: SKIP(go not found)");
                continue;
            }

            var rt = guideGo.GetComponent<RectTransform>();
            if (rt == null)
            {
                Debug.LogWarning($"[ALL6_SNOW_FIT] {guideId} has no RectTransform");
                sb.AppendLine($"  {guideId}: SKIP(no RectTransform)");
                continue;
            }

            // anchor を屋根座標に更新
            rt.anchorMin        = anchorMin;
            rt.anchorMax        = anchorMax;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta        = Vector2.zero;

            // Image を白色（雪色）に変更
            var img = guideGo.GetComponent<Image>();
            if (img == null) img = guideGo.AddComponent<Image>();
            img.color         = SnowWhite;
            img.raycastTarget = false;

            successCount++;
            sb.AppendLine($"  {guideId}: OK anchor=({anchorMin.x:F3},{anchorMin.y:F3})-({anchorMax.x:F3},{anchorMax.y:F3})");

            Debug.Log($"[ROOF_SNOW_FIT] roof={calibId} guide={guideId}" +
                      $" anchorMin=({anchorMin.x:F4},{anchorMin.y:F4})" +
                      $" anchorMax=({anchorMax.x:F4},{anchorMax.y:F4})" +
                      $" color=snow_white visible=YES rough_fit=YES");
        }

        bool all6 = successCount == 6;
        Debug.Log($"[ALL6_SNOW_FIT] count={successCount}/6 all_6={(all6 ? "YES" : "NO")}" +
                  $" method=canvas_anchor_fit screen_space_overlay=YES" +
                  $" is_playing={Application.isPlaying}");
        Debug.Log($"[ALL6_SNOW_FIT] detail:\n{sb}");

        _applied = all6;
    }
}
