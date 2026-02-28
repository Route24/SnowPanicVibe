using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>右ドラッグでカメラを家の周りに回転。奥の軒先も叩ける</summary>
public class CameraOrbit : MonoBehaviour
{
    public Transform target = null;
    public float distance = 12f;
    public float yMin = 4f;
    public float yMax = 12f;
    public float sensitivity = 2f;

    [HideInInspector] public float _yaw = 180f;
    [HideInInspector] public float _pitch = 39f;

    void Start()
    {
        if (target == null)
        {
            var go = new GameObject("CameraTarget");
            go.transform.position = new Vector3(0f, 1.5f, 0f);
            target = go.transform;
        }
        ApplyOrbit();
    }

    void Update()
    {
        if (Mouse.current == null) return;
        if (Mouse.current.rightButton.isPressed)
        {
            var delta = Mouse.current.delta.ReadValue();
            _yaw += delta.x * sensitivity * 0.5f;
            _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity * 0.3f, 15f, 70f);
            ApplyOrbit();
        }
    }

    void ApplyOrbit()
    {
        if (target == null) return;
        float y = Mathf.Clamp(distance * Mathf.Sin(_pitch * Mathf.Deg2Rad), yMin, yMax);
        float h = distance * Mathf.Cos(_pitch * Mathf.Deg2Rad);
        float x = Mathf.Sin(_yaw * Mathf.Deg2Rad) * h;
        float z = Mathf.Cos(_yaw * Mathf.Deg2Rad) * h;
        transform.position = target.position + new Vector3(x, y, z);
        transform.LookAt(target.position + Vector3.up * 0.5f);
    }
}
