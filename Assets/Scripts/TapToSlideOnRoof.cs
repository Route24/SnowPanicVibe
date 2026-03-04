using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>屋根1枚プロトタイプ用。タップで屋根面に沿って雪を滑らせる。</summary>
[RequireComponent(typeof(Camera))]
public class TapToSlideOnRoof : MonoBehaviour
{
    public float maxDistance = 50f;
    public LayerMask hitMask = ~0;
    public float hitGizmoRadius = 0.15f;
    public float hitGizmoDuration = 1f;

    void Update()
    {
        Vector2 screenPos = Vector2.zero;
        bool pressed = false;
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            screenPos = Mouse.current.position.ReadValue();
            pressed = true;
        }
        else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
        {
            screenPos = Touchscreen.current.primaryTouch.position.ReadValue();
            pressed = true;
        }
        if (!pressed) return;

        var cooldown = Object.FindFirstObjectByType<ToolCooldownManager>();
        if (cooldown != null && !cooldown.CanHit) return;

        var cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
        RaycastHit hit;
        bool hitSomething = Physics.Raycast(ray, out hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore);

        bool isRoof = false;
        Vector3 tapPoint = hitSomething ? hit.point : Vector3.zero;

        if (roofSys != null && roofSys.roofSlideCollider != null)
        {
            if (hitSomething && (hit.collider == roofSys.roofSlideCollider || hit.collider.transform.IsChildOf(roofSys.roofSlideCollider.transform)))
            {
                isRoof = true;
            }
            else if (hitSomething)
            {
                Vector3 closest = roofSys.roofSlideCollider.ClosestPoint(hit.point);
                float distSq = (closest - hit.point).sqrMagnitude;
                Bounds b = roofSys.roofSlideCollider.bounds;
                b.Expand(0.5f);
                if (distSq < 0.25f || b.Contains(hit.point))
                {
                    isRoof = true;
                    tapPoint = closest;
                }
            }
        }

        if (hitSomething && isRoof)
        {
            if (cooldown != null) cooldown.OnHit();
            Vector3 roofLocal = roofSys.roofSlideCollider.transform.InverseTransformPoint(tapPoint);
            string colliderPath = hit.collider != null ? GetTransformPath(hit.collider.transform) : "?";
            float u = 0f, v = 0f;
            var spawner = Object.FindFirstObjectByType<SnowPackSpawner>();
            if (spawner != null) spawner.ComputeTapUV(tapPoint, out u, out v);
            Debug.Log($"[TapHit] rayOrig=({ray.origin.x:F2},{ray.origin.y:F2},{ray.origin.z:F2}) hitCollider={colliderPath} hitPt=({tapPoint.x:F3},{tapPoint.y:F3},{tapPoint.z:F3}) roofLocal=({roofLocal.x:F3},{roofLocal.y:F3},{roofLocal.z:F3}) u={u:F3} v={v:F3} accepted=Yes");
            StartCoroutine(ShowHitGizmo(tapPoint));
            roofSys.RequestTapSlide(tapPoint);
        }
        else if (hitSomething)
        {
            string colliderPath = hit.collider != null ? GetTransformPath(hit.collider.transform) : "?";
            Debug.Log($"[TapMiss] rayOrig=({ray.origin.x:F2},{ray.origin.y:F2},{ray.origin.z:F2}) hitCollider={colliderPath} hitPt=({hit.point.x:F3},{hit.point.y:F3},{hit.point.z:F3}) accepted=No");
        }
        else
        {
            Debug.Log($"[TapMiss] rayOrig=({ray.origin.x:F2},{ray.origin.y:F2},{ray.origin.z:F2}) noHit screen=({screenPos.x:F0},{screenPos.y:F0})");
        }
    }

    static string GetTransformPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new System.Collections.Generic.List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    IEnumerator ShowHitGizmo(Vector3 worldPos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "TapHitGizmo";
        var col = go.GetComponent<Collider>();
        if (col != null) col.enabled = false;
        go.transform.position = worldPos;
        go.transform.localScale = Vector3.one * (hitGizmoRadius * 2f);
        var r = go.GetComponent<Renderer>();
        if (r != null && r.sharedMaterial != null) r.sharedMaterial.color = Color.red;
        yield return new WaitForSeconds(hitGizmoDuration);
        Object.Destroy(go);
    }
}
