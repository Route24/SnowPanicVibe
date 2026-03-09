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
    [Tooltip("Raycastが当たらない時、このピクセル以内の屋根デブリを画面距離で拾う。止まり雪対策で拡大。")]
    public float tapFallbackPixelRadius = 80f;

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

        var chunk = hitSomething ? hit.collider.GetComponent<MvpSnowChunkMotion>() : null;
        var fallingPiece = hitSomething ? hit.collider.GetComponentInParent<SnowPackFallingPiece>() : null;
        string hitType = chunk != null ? "Detached(Chunk)" : (fallingPiece != null ? "Detached(Falling)" : (hitSomething && isRoof ? "Packed" : (hitSomething ? "Other" : "None")));
        string hitObject = hitSomething && hit.collider != null ? GetTransformPath(hit.collider.transform) : "none";
        DetachedSnowDiagnostics.LogTapRaycast(hitMask, hitObject, hitType);
        if (chunk != null && chunk.gameObject.activeSelf && roofSys != null)
        {
            if (cooldown != null) cooldown.OnHit();
            Vector3 roofN = roofSys.roofSlideCollider != null ? roofSys.roofSlideCollider.transform.up.normalized : Vector3.up;
            chunk.ApplyTapImpulse(roofN);
            tapPoint = hit.point;
            StartCoroutine(ShowHitGizmo(tapPoint));
            roofSys.RequestTapSlide(tapPoint);
        }
        else if (fallingPiece != null && roofSys != null)
        {
            if (cooldown != null) cooldown.OnHit();
            Vector3 roofN = roofSys.roofSlideCollider != null ? roofSys.roofSlideCollider.transform.up.normalized : Vector3.up;
            Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, roofN).normalized;
            var rb = fallingPiece.GetComponent<Rigidbody>();
            if (rb != null) rb.AddForce(downhill * 3f + roofN * 1f, ForceMode.Impulse);
            tapPoint = hit.point;
            StartCoroutine(ShowHitGizmo(tapPoint));
            roofSys.RequestTapSlide(tapPoint);
        }
        else if (roofSys != null && roofSys.TryGetClosestDebrisToScreen(cam, screenPos, tapFallbackPixelRadius, out var fallbackChunk, out var fallbackFalling))
        {
            if (cooldown != null) cooldown.OnHit();
            Vector3 roofN = roofSys.roofSlideCollider != null ? roofSys.roofSlideCollider.transform.up.normalized : Vector3.up;
            tapPoint = fallbackChunk != null ? fallbackChunk.transform.position : fallbackFalling.transform.position;
            if (fallbackChunk != null)
            {
                fallbackChunk.ApplyTapImpulse(roofN);
                Debug.Log($"[TapHitType] fallbackUsed=Detached(Chunk) [TapFallback] pos=({tapPoint.x:F2},{tapPoint.y:F2},{tapPoint.z:F2})");
            }
            else
            {
                Vector3 downhill = Vector3.ProjectOnPlane(Vector3.down, roofN).normalized;
                var rb = fallbackFalling.GetComponent<Rigidbody>();
                if (rb != null) rb.AddForce(downhill * 3f + roofN * 1f, ForceMode.Impulse);
                Debug.Log($"[TapHitType] fallbackUsed=Detached(Falling) [TapFallback] pos=({tapPoint.x:F2},{tapPoint.y:F2},{tapPoint.z:F2})");
            }
            StartCoroutine(ShowHitGizmo(tapPoint));
            roofSys.RequestTapSlide(tapPoint);
        }
        else if (hitSomething && isRoof)
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
        if (r != null && r.sharedMaterial != null) MaterialColorHelper.SetColorSafe(r.sharedMaterial, Color.red);
        yield return new WaitForSeconds(hitGizmoDuration);
        Object.Destroy(go);
    }
}
