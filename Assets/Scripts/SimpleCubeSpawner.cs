using UnityEngine;

public class SimpleCubeSpawner : MonoBehaviour
{
    const string CubeName = "SIMPLE_TEST_CUBE";
    GameObject _cube;
    bool _done;

    void Start()
    {
        _cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cube.name = CubeName;
        _cube.transform.position = new Vector3(0f, 2f, 0f);
        _cube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = Color.cyan;
        _cube.GetComponent<Renderer>().sharedMaterial = mat;
        // コライダーはそのまま残す（クリック判定用）— TASK3B v3
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
    }
}
