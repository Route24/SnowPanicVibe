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

        var cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
        {
            Debug.Log($"[TapMiss] frame={Time.frameCount} t={Time.time:F2} screen=({screenPos.x:F0},{screenPos.y:F0})");
            return;
        }

        var roofSys = Object.FindFirstObjectByType<RoofSnowSystem>();
        bool isRoof = roofSys != null && roofSys.roofSlideCollider == hit.collider;

        if (isRoof)
        {
            Vector3 roofLocal = hit.collider.transform.InverseTransformPoint(hit.point);
            Debug.Log($"[TapHit] frame={Time.frameCount} t={Time.time:F2} hit=({hit.point.x:F3},{hit.point.y:F3},{hit.point.z:F3}) roofLocal=({roofLocal.x:F3},{roofLocal.y:F3},{roofLocal.z:F3})");
            StartCoroutine(ShowHitGizmo(hit.point));
            roofSys.RequestTapSlide(hit.point);
        }
        else
        {
            Debug.Log($"[TapMiss] frame={Time.frameCount} t={Time.time:F2} screen=({screenPos.x:F0},{screenPos.y:F0}) hitOther={hit.collider.name}");
        }
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
