using UnityEngine;

/// <summary>
/// 強制デバッグ雪表示。Play時に RoofPanel 上へ白いオブジェクトを確実に生成する。
/// 既存の SnowPackSpawner / RoofSnowSystem に一切依存しない。
/// 確認後は本スクリプトを削除 or 無効化してください。
/// </summary>
[DefaultExecutionOrder(-1000)]
public class DebugSnowVisual : MonoBehaviour
{
    [Header("デバッグ雪 参照 (自動解決可)")]
    public Collider roofCollider;

    [Header("外観")]
    public Color snowColor = new Color(0.95f, 0.97f, 1f, 1f);
    public Vector3 sizeMultiplier = new Vector3(1f, 0.18f, 1f);
    public float heightOffset = 0.12f;

    [Header("制御")]
    public bool destroyOnDisable = false;

    GameObject _debugGo;

    void Start()
    {
        // 参照が無ければ自動解決
        if (roofCollider == null)
        {
            var panel = GameObject.Find("RoofPanel");
            if (panel != null) roofCollider = panel.GetComponent<Collider>();
        }
        if (roofCollider == null)
        {
            // 全 BoxCollider から最大面積のものを選ぶ
            float bestArea = -1f;
            foreach (var c in Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
            {
                var s = c.bounds.size;
                float area = s.x * s.z;
                if (area > bestArea) { bestArea = area; roofCollider = c; }
            }
        }

        SpawnDebugSnow();
    }

    void SpawnDebugSnow()
    {
        if (_debugGo != null) return;

        // --- 位置・サイズ計算 ---
        Vector3 worldPos;
        Quaternion worldRot;
        Vector3 worldScale;

        if (roofCollider != null)
        {
            Bounds b = roofCollider.bounds;
            // 屋根面法線（BoxCollider の transform.up を屋根の上方向とみなす）
            Transform rt = roofCollider.transform;
            Vector3 normal = rt.up.normalized;
            // 法線が下を向いていたら反転
            if (Vector3.Dot(normal, Vector3.up) < 0f) normal = -normal;

            worldPos  = b.center + normal * (b.extents.y + heightOffset);
            worldRot  = Quaternion.FromToRotation(Vector3.up, normal);
            worldScale = new Vector3(
                b.size.x * sizeMultiplier.x,
                Mathf.Max(0.12f, b.size.y * sizeMultiplier.y + heightOffset),
                b.size.z * sizeMultiplier.z);
        }
        else
        {
            // フォールバック: 画面中央あたり
            worldPos   = new Vector3(0f, 2f, 0f);
            worldRot   = Quaternion.identity;
            worldScale = new Vector3(3f, 0.3f, 2f);
            Debug.LogWarning("[DebugSnowVisual] roofCollider not found, placing at default position.");
        }

        // --- GameObject 生成 ---
        _debugGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _debugGo.name = "DEBUG_SnowVisual";

        // 物理無効化（ゲームに影響させない）
        var col = _debugGo.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        _debugGo.transform.SetPositionAndRotation(worldPos, worldRot);
        _debugGo.transform.localScale = worldScale;

        // --- Unlit 白マテリアル（URP / Built-in 両対応） ---
        var mr = _debugGo.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            string[] shaderCandidates = new[]
            {
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Sprites/Default",
                "Standard"
            };
            Shader sh = null;
            foreach (var sname in shaderCandidates)
            {
                sh = Shader.Find(sname);
                if (sh != null) break;
            }
            if (sh != null)
            {
                var mat = new Material(sh);
                mat.name = "DebugSnow_Unlit";
                if (mat.HasProperty("_BaseColor"))   mat.SetColor("_BaseColor", snowColor);
                if (mat.HasProperty("_Color"))       mat.SetColor("_Color", snowColor);
                if (mat.HasProperty("_RendererColor")) mat.SetColor("_RendererColor", snowColor);
                mr.sharedMaterial = mat;
            }
            mr.shadowCastingMode  = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows     = false;
            mr.enabled            = true;

            // Sorting: 最前面
            mr.sortingOrder = 9999;
        }

        // Layer を Default に固定（カリングマスク対策）
        _debugGo.layer = 0;

        Debug.Log($"[DEBUG_SNOW_VISUAL] debug_object_created=YES name={_debugGo.name} " +
                  $"pos=({worldPos.x:F2},{worldPos.y:F2},{worldPos.z:F2}) " +
                  $"scale=({worldScale.x:F2},{worldScale.y:F2},{worldScale.z:F2}) " +
                  $"roof={(roofCollider != null ? roofCollider.gameObject.name : "none")}");
    }

    void OnDisable()
    {
        if (destroyOnDisable && _debugGo != null)
        {
            Destroy(_debugGo);
            _debugGo = null;
        }
    }
}
