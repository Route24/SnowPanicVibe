using UnityEngine;
using UnityEngine.InputSystem;

public class CorniceHitter : MonoBehaviour
{
    public Camera mainCamera;
    public float maxDistance = 100f;
    public LayerMask hitMask = ~0;

    void Awake()
    {
        if (mainCamera == null) mainCamera = Camera.main;
    }

    void Update()
    {
        if (mainCamera == null) return;
        if (Mouse.current == null) return;

        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Ray ray = mainCamera.ScreenPointToRay(mousePos);

            var hits = Physics.RaycastAll(ray, maxDistance, hitMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var hit in hits)
            {
                var roofSnow = hit.collider.GetComponent<RoofSnow>() ?? hit.collider.GetComponentInParent<RoofSnow>() ?? hit.collider.GetComponentInChildren<RoofSnow>();
                if (roofSnow != null) { roofSnow.Hit(hit.point); return; }
                var seg = hit.collider.GetComponentInParent<CorniceSnowSegment>();
                if (seg != null) { seg.Hit(1f); return; }
                var snowClump = hit.collider.GetComponent<SnowClump>() ?? hit.collider.GetComponentInParent<SnowClump>();
                if (snowClump != null) { snowClump.RemoveImmediate(); return; }
            }
        }
    }
}

