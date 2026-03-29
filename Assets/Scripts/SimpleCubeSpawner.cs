using UnityEngine;

public class SimpleCubeSpawner : MonoBehaviour
{
    const string CubeName = "SIMPLE_TEST_CUBE";
    GameObject _cube;
    bool _done;

    void Start()
    {
        // ── 不要オブジェクトを非表示 ──────────────────────────
        HideIfExists("RedCube_Visibility");
        HideIfExists("SnowQuad_Static");
        HideIfExists("SnowTest");
        // 動的に生成されたデバッグオブジェクトも対象
        HideIfExists("DEBUG_RedCube_CamTest");
        HideIfExists("ForceSnowCube");

        // ── シアンボックスを屋根上に1個生成 ────────────────────
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube.name = CubeName;

        // 屋根（RoofQuad）の上に配置
        var roof = GameObject.Find("RoofQuad");
        if (roof != null)
        {
            _cube.transform.position = roof.transform.position + Vector3.up * 0.3f;
            _cube.transform.rotation = roof.transform.rotation;
        }
        else
        {
            _cube.transform.position = new Vector3(0f, 1.5f, 0f);
        }
        _cube.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.cyan);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color", Color.cyan);
        _cube.GetComponent<Renderer>().sharedMaterial = mat;

        Debug.Log($"[CYAN_BOX] spawned=YES name={CubeName} pos={_cube.transform.position}");
    }

    void Update()
    {
        if (_done) return;
        if (!Input.GetMouseButtonDown(0)) return;

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit)) return;

        Debug.Log($"[CLICK_TARGET] hit={hit.collider.gameObject.name}");

        if (hit.collider.gameObject.name != CubeName) return;

        Destroy(hit.collider.gameObject);
        _cube = null;
        _done = true;
        Debug.Log("[CYAN_BOX] destroyed_on_click=YES");
    }

    static void HideIfExists(string goName)
    {
        var go = GameObject.Find(goName);
        if (go != null)
        {
            go.SetActive(false);
            Debug.Log($"[SCENE_CLEANUP] hidden={goName}");
        }
    }
}
