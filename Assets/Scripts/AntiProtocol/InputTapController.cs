using UnityEngine;

public sealed class InputTapController : MonoBehaviour
{
    [SerializeField] private LayerMask snowLayerMask;

    private Camera cachedMainCamera;

    private void Awake()
    {
        cachedMainCamera = Camera.main;
        if (cachedMainCamera == null)
        {
            Debug.LogError("InputTapController: MainCamera が見つかりません。");
        }
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (cachedMainCamera == null)
        {
            return;
        }

        Ray ray = cachedMainCamera.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit, 1000f, snowLayerMask))
        {
            return;
        }

        SnowBlockNode block = hit.collider.GetComponent<SnowBlockNode>();
        if (block == null)
        {
            return;
        }

        Debug.Log("[SnowBlock] click_hit_cyan_box=YES name=" + hit.collider.gameObject.name);
        block.OnHit();
    }
}
