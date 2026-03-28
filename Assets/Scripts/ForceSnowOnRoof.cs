using UnityEngine;

/// <summary>
/// SnowVisibilityLab 専用: Play 開始時に屋根上へ白い雪 Quad を強制配置する。
/// roofSnowConstantThickness に thickness を接続し、BoxCollider も同期する。
/// </summary>
public class ForceSnowOnRoof : MonoBehaviour
{
    [Tooltip("雪を貼り付ける屋根の Transform")]
    public Transform roofTransform;

    [Tooltip("thickness ソース（RoofSnowSystem）")]
    public RoofSnowSystem roofSnowSystem;

    [Tooltip("thickness のフォールバック値")]
    public float snowThickness = 0.18f;

    [Tooltip("雪の色")]
    public Color snowColor = new Color(0.93f, 0.96f, 1.0f, 1.0f);

    GameObject _snowGo;
    BoxCollider _roofCollider;

    void Start()
    {
        // TASK3B: 白い代用オブジェクト生成を一時停止
    }

    void ApplyThickness()
    {
        if (_snowGo == null) return;
        float thickness = roofSnowSystem != null ? roofSnowSystem.roofSnowConstantThickness : snowThickness;

        // ForceSnowCube: localScale.y = thickness（ローカル空間）
        var ls = _snowGo.transform.localScale;
        ls.y = thickness;
        _snowGo.transform.localScale = ls;

        // localPosition.y = thickness * 0.5（底面を屋根面に合わせる）
        var lp = _snowGo.transform.localPosition;
        lp.y = thickness * 0.5f;
        _snowGo.transform.localPosition = lp;

        // BoxCollider の Y サイズを thickness に同期
        if (_roofCollider != null)
        {
            var sz = _roofCollider.size;
            sz.y = thickness;
            _roofCollider.size = sz;
        }
    }

    void SpawnSnow()
    {
        _snowGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _snowGo.name = "ForceSnowCube";
        _snowGo.transform.SetParent(roofTransform, false);

        float rx = 1f / Mathf.Max(0.01f, roofTransform.lossyScale.x);
        float rz = 1f / Mathf.Max(0.01f, roofTransform.lossyScale.z);
        _snowGo.transform.localScale = new Vector3(rx, snowThickness, rz);
        _snowGo.transform.localPosition = new Vector3(0f, snowThickness * 0.5f, 0f);

        var rend = _snowGo.GetComponent<Renderer>();
        if (rend != null)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = snowColor;
            rend.sharedMaterial = mat;
        }
        var col = _snowGo.GetComponent<Collider>();
        if (col != null) Destroy(col);
    }
}
