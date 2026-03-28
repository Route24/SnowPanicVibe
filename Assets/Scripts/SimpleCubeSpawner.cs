using UnityEngine;

public class SimpleCubeSpawner : MonoBehaviour
{
    GameObject _cube;
    bool _destroyed;

    void Start()
    {
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube.name = "SIMPLE_TEST_CUBE";
        _cube.transform.position = new Vector3(0f, 2f, 0f);
        _cube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = Color.cyan;
        _cube.GetComponent<Renderer>().sharedMaterial = mat;
        Debug.Log("[SIMPLE_CUBE] spawned at (0,2,0)");
    }

    void Update()
    {
        // 破壊後: 再生成されたか 1 回だけチェック
        if (_destroyed)
        {
            bool respawned = GameObject.Find("SIMPLE_TEST_CUBE") != null;
            Debug.Log($"[EVENT] respawned={(respawned ? "YES" : "NO")} target=SIMPLE_TEST_CUBE");
            _destroyed = false;
            return;
        }

        if (_cube == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out var hit) && hit.collider.gameObject == _cube)
        {
            Debug.Log("[EVENT] click=YES target=SIMPLE_TEST_CUBE");
            Destroy(_cube);
            _cube = null;
            _destroyed = true;
            Debug.Log("[EVENT] destroyed=YES target=SIMPLE_TEST_CUBE");
        }
    }
}
