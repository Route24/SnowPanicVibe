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
                var snowClump = hit.collider.GetComponent<SnowClump>() ?? hit.collider.GetComponentInParent<SnowClump>();
                if (snowClump != null) { snowClump.RemoveImmediate(); return; }

                var snowCube = hit.collider.GetComponent<SnowTestCube>();
                if (snowCube != null)
                {
                    var rb = snowCube.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        if (rb.isKinematic) snowCube.Hit(hit.point, hit.normal);
                        else snowCube.PushFromHit(hit.point);
                        return;
                    }
                }

                if (hit.collider.name == "SnowPiece")
                {
                    var prb = hit.rigidbody;
                    if (prb != null) prb.AddForce((hit.point - mainCamera.transform.position).normalized * 2f, ForceMode.Impulse);
                    else Object.Destroy(hit.collider.gameObject);
                    return;
                }

                var roofSnow = hit.collider.GetComponent<RoofSnow>() ?? hit.collider.GetComponentInParent<RoofSnow>() ?? hit.collider.GetComponentInChildren<RoofSnow>();
                if (roofSnow != null && roofSnow.HasAnySnow()) { roofSnow.Hit(hit.point); return; }

                var roofSys = UnityEngine.Object.FindFirstObjectByType<RoofSnowSystem>();
                if (roofSys != null && roofSys.roofSlideCollider == hit.collider)
                {
                    var cooldown = UnityEngine.Object.FindFirstObjectByType<ToolCooldownManager>();
                    if (cooldown != null && !cooldown.CanHit) return;
                    if (cooldown != null) cooldown.OnHit();
                    roofSys.RequestTapSlide(hit.point);
                    return;
                }

                var seg = hit.collider.GetComponentInParent<CorniceSnowSegment>();
                if (seg != null) { seg.Hit(1f); return; }
            }

            if (hits.Length > 0) TryHitNearby(hits[0].point);
            else TryHitNearby(ray.origin + ray.direction * 15f);
        }
    }

    void TryHitNearby(Vector3 center)
    {
        var cols = Physics.OverlapSphere(center, 0.6f);
        float bestDist = float.MaxValue;
        SnowClump bestClump = null;
        SnowTestCube bestCube = null;
        foreach (var c in cols)
        {
            var clump = c.GetComponent<SnowClump>() ?? c.GetComponentInParent<SnowClump>();
            if (clump != null)
            {
                float d = Vector3.SqrMagnitude(c.ClosestPoint(center) - center);
                if (d < bestDist) { bestDist = d; bestClump = clump; bestCube = null; }
            }
            else
            {
                var cube = c.GetComponent<SnowTestCube>();
                if (cube != null)
                {
                    float d = Vector3.SqrMagnitude(c.ClosestPoint(center) - center);
                    if (d < bestDist) { bestDist = d; bestCube = cube; bestClump = null; }
                }
            }
        }
        if (bestClump != null) { bestClump.RemoveImmediate(); return; }
        if (bestCube != null) { bestCube.PushFromHit(center); }
    }
}

