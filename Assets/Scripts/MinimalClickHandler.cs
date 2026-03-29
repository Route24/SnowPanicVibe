using UnityEngine;

/// <summary>
/// SnowMinimalClickLab 専用。
/// 指定オブジェクトをクリックで削除するだけ。
/// 他の処理は一切しない。
/// </summary>
public class MinimalClickHandler : MonoBehaviour
{
    [Tooltip("クリックで消すオブジェクト名")]
    public string targetName = "CyanBox";

    bool _done;

    void Update()
    {
        if (_done) return;
        if (!Input.GetMouseButtonDown(0)) return;

        var cam = Camera.main;
        if (cam == null) return;

        var ray = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var hit)) return;

        if (hit.collider.gameObject.name != targetName) return;

        Destroy(hit.collider.gameObject);
        _done = true;
        Debug.Log("[MinimalClick] cyan_box_destroyed_on_click=YES");
    }
}
