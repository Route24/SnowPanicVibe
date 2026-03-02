using UnityEngine;

public class RoofDebugAutoSetup : MonoBehaviour
{
    public Transform targetRoof;
    public bool createFlatRoof = true;
    public bool disableSnowFX = true;
    public bool autoAlignCamera = false;
    public bool alignCameraOnStart = false;
    public float debugWidth = 2.0f;
    public float debugDepth = 2.0f;
    public float debugTiltX = 22f;

    GameObject debugFlat;

    void Start()
    {
        if (targetRoof == null) return;

        if (createFlatRoof)
            CreateFlatRoof();

        if (disableSnowFX)
            DisableSnowFX();

        // Play開始時に勝手に視点を変えない。必要時のみ明示実行。
        if (alignCameraOnStart && autoAlignCamera)
            AlignCamera();
    }

    [ContextMenu("Align Camera Now")]
    void AlignCameraNow()
    {
        AlignCamera();
    }

    void CreateFlatRoof()
    {
        if (targetRoof == null) return;

        if (debugFlat == null)
        {
            var existing = targetRoof.Find("RoofDebugFlat");
            if (existing != null) debugFlat = existing.gameObject;
        }

        if (debugFlat != null)
        {
            debugFlat.transform.localPosition = new Vector3(0, 0.07f, 0);
            debugFlat.transform.localRotation = Quaternion.Inverse(targetRoof.rotation) * Quaternion.Euler(debugTiltX, 0, 0);
            debugFlat.transform.localScale = new Vector3(debugWidth, 0.02f, debugDepth);
            return;
        }

        debugFlat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        debugFlat.name = "RoofDebugFlat";

        debugFlat.transform.SetParent(targetRoof);
        debugFlat.transform.localPosition = new Vector3(0, 0.07f, 0);
        debugFlat.transform.localRotation = Quaternion.Inverse(targetRoof.rotation) * Quaternion.Euler(debugTiltX, 0, 0);
        debugFlat.transform.localScale = new Vector3(debugWidth, 0.02f, debugDepth);

        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.color = new Color(0.2f, 0.25f, 0.3f);
        debugFlat.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    void DisableSnowFX()
    {
        var fx = GameObject.Find("SnowParticle");
        if (fx) fx.SetActive(false);

        var ground = GameObject.Find("GroundSnow");
        if (ground) ground.SetActive(false);
    }

    void AlignCamera()
    {
        var cam = Camera.main;
        if (!cam) return;
        if (targetRoof == null) return;

        Vector3 focus = targetRoof.position + Vector3.up * 0.5f;
        cam.transform.position = targetRoof.position + new Vector3(0, 2.5f, -3.5f);
        cam.transform.LookAt(focus);
    }

    void OnDrawGizmos()
    {
        if (targetRoof == null) return;
        Vector3 origin = targetRoof.position;
        Vector3 up = targetRoof.up;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + up * 1.0f);

        Vector3 slideDir = Vector3.ProjectOnPlane(Vector3.down, up).normalized;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(origin, origin + slideDir * 1.5f);
    }
}

