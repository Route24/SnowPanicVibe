using UnityEngine;

/// <summary>
/// 2D RoofGuide ワークフロー用データホルダー。
/// 実際の表示は Canvas 配下の RoofGuide_XX Image コンポーネントが担う。
/// このスクリプトは RoofRect データを保持し、外部から参照できるようにする。
///
/// Roof Guide Workflow:
///   1. Editor で RoofGuideCanvas/RoofGuide_XX の RectTransform を調整して屋根に合わせる
///   2. 合わせ完了後 RoofGuideCanvas を非アクティブにして本番確認
///   3. BackgroundImage を village_clean.png に差し替えて最終確認
/// </summary>
public class BillboardRoofMapOverlay : MonoBehaviour
{
    [System.Serializable]
    public struct RoofRect
    {
        public string id;
        [Range(0f,1f)] public float xMin, xMax, yMin, yMax;
    }

    // 6軒の屋根 normalized 座標（参照用・Canvas の RectTransform が実体）
    public RoofRect[] roofs = new RoofRect[]
    {
        new RoofRect { id="Roof_TL", xMin=0.117f, xMax=0.303f, yMin=0.295f, yMax=0.391f },
        new RoofRect { id="Roof_TM", xMin=0.386f, xMax=0.610f, yMin=0.269f, yMax=0.373f },
        new RoofRect { id="Roof_TR", xMin=0.679f, xMax=0.874f, yMin=0.281f, yMax=0.382f },
        new RoofRect { id="Roof_BL", xMin=0.078f, xMax=0.322f, yMin=0.590f, yMax=0.712f },
        new RoofRect { id="Roof_BM", xMin=0.405f, xMax=0.591f, yMin=0.642f, yMax=0.747f },
        new RoofRect { id="Roof_BR", xMin=0.659f, xMax=0.908f, yMin=0.587f, yMax=0.708f },
    };

    void Awake()
    {
        foreach (var r in roofs)
            Debug.Log($"[ROOF_MAP] id={r.id} xMin={r.xMin:F3} xMax={r.xMax:F3} yMin={r.yMin:F3} yMax={r.yMax:F3}");
        Debug.Log($"[ROOF_MAP] mode=2D_canvas count={roofs.Length}");
    }

    /// <summary>指定 id の RoofRect を返す。</summary>
    public bool TryGetRoof(string id, out RoofRect result)
    {
        foreach (var r in roofs)
        {
            if (r.id == id) { result = r; return true; }
        }
        result = default;
        return false;
    }
}
