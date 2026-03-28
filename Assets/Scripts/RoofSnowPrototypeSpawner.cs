using UnityEngine;

/// <summary>
/// SnowVisibilityLab 専用: Play 時に屋根の上に TEMP_SNOW_CUBE を 1 個だけ生成する。
/// Phase 1 最小確認用。既存雪システムには触らない。
/// </summary>
public class RoofSnowPrototypeSpawner : MonoBehaviour
{
    public Transform targetRoof;
    public Vector3 cubeScale = new Vector3(0.6f, 0.3f, 0.6f);
    public float offsetFromRoof = 0.15f;

    bool _spawned;

    void Start()
    {
        if (_spawned) return;
        if (targetRoof == null)
        {
            var rq = GameObject.Find("RoofQuad");
            if (rq != null) targetRoof = rq.transform;
        }
        if (targetRoof == null) { Debug.LogWarning("[TASK1] targetRoof not found"); return; }

        Vector3 spawnPos = targetRoof.position + targetRoof.up * offsetFromRoof;

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "TEMP_SNOW_CUBE";
        cube.transform.position = spawnPos;
        cube.transform.rotation = Quaternion.FromToRotation(Vector3.up, targetRoof.up);
        cube.transform.localScale = cubeScale;

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (mat == null || mat.shader.name == "Hidden/InternalErrorShader")
            mat = new Material(Shader.Find("Unlit/Color"));
        mat.color = new Color(1f, 0.4f, 0.7f, 1f); // ピンク（仮）
        cube.GetComponent<Renderer>().sharedMaterial = mat;

        Destroy(cube.GetComponent<Collider>());

        _spawned = true;
        Debug.Log("[TASK1] spawned TEMP_SNOW_CUBE");
    }
}
