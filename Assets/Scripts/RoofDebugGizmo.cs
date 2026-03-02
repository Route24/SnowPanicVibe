using UnityEngine;

public class RoofDebugGizmo : MonoBehaviour
{
    public float arrowLen = 0.6f;

    void OnDrawGizmos()
    {
        // roof up
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + transform.up * arrowLen);

        // slide direction (down projected onto roof plane)
        Vector3 slide = Vector3.ProjectOnPlane(Vector3.down, transform.up).normalized;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, transform.position + slide * arrowLen);
    }
}

