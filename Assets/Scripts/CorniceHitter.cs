using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>ASSI TAP DEBUG 用。TapHit/TapMiss/lastHitObject/lastHitLayer を蓄積。</summary>
public static class TapDebugState
{
    public static int TapHit;
    public static int TapMiss;
    public static string LastHitObject = "";
    public static string LastHitLayer = "";
}

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

            string hitObj = hits.Length > 0 ? (hits[0].collider != null ? hits[0].collider.gameObject.name : "null") : "none";
            int hitLayer = hits.Length > 0 && hits[0].collider != null ? hits[0].collider.gameObject.layer : -1;
            string layerName = hitLayer >= 0 ? LayerMask.LayerToName(hitLayer) : "N/A";
            Debug.Log($"[TAP RAY] mouse=({mousePos.x:F0},{mousePos.y:F0}) ray=({ray.origin.x:F2},{ray.origin.y:F2},{ray.origin.z:F2}) hit={hits.Length > 0} object={hitObj} layer={hitLayer}({layerName})");

            bool didHit = false;
            foreach (var hit in hits)
            {
                var snowClump = hit.collider.GetComponent<SnowClump>() ?? hit.collider.GetComponentInParent<SnowClump>();
                if (snowClump != null) { RecordTapHit(hit); snowClump.RemoveImmediate(); return; }

                var snowCube = hit.collider.GetComponent<SnowTestCube>();
                if (snowCube != null)
                {
                    var rb = snowCube.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        RecordTapHit(hit);
                        if (rb.isKinematic) snowCube.Hit(hit.point, hit.normal);
                        else snowCube.PushFromHit(hit.point);
                        return;
                    }
                }

                if (hit.collider.name == "SnowPiece")
                {
                    RecordTapHit(hit);
                    var prb = hit.rigidbody;
                    if (prb != null) prb.AddForce((hit.point - mainCamera.transform.position).normalized * 2f, ForceMode.Impulse);
                    else Object.Destroy(hit.collider.gameObject);
                    return;
                }

                var roofSnow = hit.collider.GetComponent<RoofSnow>() ?? hit.collider.GetComponentInParent<RoofSnow>() ?? hit.collider.GetComponentInChildren<RoofSnow>();
                if (roofSnow != null && roofSnow.HasAnySnow()) { RecordTapHit(hit); roofSnow.Hit(hit.point); return; }

                var roofSys = UnityEngine.Object.FindFirstObjectByType<RoofSnowSystem>();
                if (roofSys != null)
                {
                    if (roofSys.roofSlideCollider == hit.collider)
                    {
                        var cooldown = UnityEngine.Object.FindFirstObjectByType<ToolCooldownManager>();
                        if (cooldown != null && !cooldown.CanHit) return;
                        RecordTapHit(hit);
                        if (cooldown != null) cooldown.OnHit();
                        roofSys.RequestTapSlide(hit.point);
                        return;
                    }
                    if (roofSys.roofSlideCollider != null && IsCorniceRoofSurface(hit.collider))
                    {
                        var cooldown = UnityEngine.Object.FindFirstObjectByType<ToolCooldownManager>();
                        if (cooldown != null && !cooldown.CanHit) return;
                        RecordTapHit(hit);
                        if (cooldown != null) cooldown.OnHit();
                        roofSys.RequestTapSlide(roofSys.roofSlideCollider.ClosestPoint(hit.point));
                        return;
                    }
                }

                var seg = hit.collider.GetComponentInParent<CorniceSnowSegment>();
                if (seg != null) { RecordTapHit(hit); seg.Hit(1f); return; }
            }

            if (hits.Length > 0) didHit = TryHitNearby(hits[0].point);
            else didHit = TryHitNearby(ray.origin + ray.direction * 15f);
            if (!didHit)
            {
                TapDebugState.TapMiss++;
                Debug.Log($"[TAP_DEBUG] TapHit={TapDebugState.TapHit} TapMiss={TapDebugState.TapMiss} lastHitObject={TapDebugState.LastHitObject} hit_target={TapDebugState.LastHitObject} lastHitLayer={TapDebugState.LastHitLayer}");
            }
        }
    }

    static void RecordTapHit(RaycastHit hit)
    {
        TapDebugState.TapHit++;
        TapDebugState.LastHitObject = hit.collider != null ? hit.collider.gameObject.name : "";
        TapDebugState.LastHitLayer = hit.collider != null ? LayerMask.LayerToName(hit.collider.gameObject.layer) : "";
        Debug.Log($"[TAP_DEBUG] TapHit={TapDebugState.TapHit} TapMiss={TapDebugState.TapMiss} lastHitObject={TapDebugState.LastHitObject} hit_target={TapDebugState.LastHitObject} lastHitLayer={TapDebugState.LastHitLayer}");
    }

    bool TryHitNearby(Vector3 center)
    {
        var cols = Physics.OverlapSphere(center, 0.6f);
        float bestDist = float.MaxValue;
        SnowClump bestClump = null;
        SnowTestCube bestCube = null;
        Collider bestCol = null;
        foreach (var c in cols)
        {
            var clump = c.GetComponent<SnowClump>() ?? c.GetComponentInParent<SnowClump>();
            if (clump != null)
            {
                float d = Vector3.SqrMagnitude(c.ClosestPoint(center) - center);
                if (d < bestDist) { bestDist = d; bestClump = clump; bestCube = null; bestCol = c; }
            }
            else
            {
                var cube = c.GetComponent<SnowTestCube>();
                if (cube != null)
                {
                    float d = Vector3.SqrMagnitude(c.ClosestPoint(center) - center);
                    if (d < bestDist) { bestDist = d; bestCube = cube; bestClump = null; bestCol = c; }
                }
            }
        }
        if (bestClump != null) { RecordTapHitNearby(bestCol); bestClump.RemoveImmediate(); return true; }
        if (bestCube != null) { RecordTapHitNearby(bestCol); bestCube.PushFromHit(center); return true; }
        return false;
    }

    static void RecordTapHitNearby(Collider c)
    {
        TapDebugState.TapHit++;
        TapDebugState.LastHitObject = c != null ? c.gameObject.name : "";
        TapDebugState.LastHitLayer = c != null ? LayerMask.LayerToName(c.gameObject.layer) : "";
        Debug.Log($"[TAP_DEBUG] TapHit={TapDebugState.TapHit} TapMiss={TapDebugState.TapMiss} lastHitObject={TapDebugState.LastHitObject} hit_target={TapDebugState.LastHitObject} lastHitLayer={TapDebugState.LastHitLayer}");
    }

    static bool IsCorniceRoofSurface(Collider c)
    {
        if (c == null) return false;
        var t = c.transform;
        while (t != null)
        {
            if (t.name == "RoofPanel" || t.name == "Roof" || t.name == "RoofSnowSurface") return true;
            t = t.parent;
        }
        return false;
    }
}

