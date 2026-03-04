using UnityEngine;

/// <summary>
/// MVP: Snow pile at ground hit point. Stays briefly, blinks, despawns.
/// </summary>
public class GroundSnowPile : MonoBehaviour
{
    float _lifetimeRemaining;
    float _blinkDuration;
    float _blinkStartTime = -1f;
    Vector3 _initScale;
    bool _blinking;

    public static GroundSnowPile Create(Transform parent, Vector3 position, float amount, Color snowColor, float scalePerAmount, float lifetimeSec, float blinkDurationSec)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "GroundSnowPile";
        go.transform.SetParent(parent);
        go.transform.position = position;
        float s = Mathf.Max(0.03f, Mathf.Min(0.3f, amount * scalePerAmount));
        go.transform.localScale = new Vector3(s, s * 0.6f, s);
        go.transform.rotation = Quaternion.identity;
        var c = go.GetComponent<Collider>();
        if (c != null) c.enabled = false;
        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = sh != null ? new Material(sh) : null;
            if (mat != null) { mat.color = snowColor; r.sharedMaterial = mat; }
        }
        var pile = go.AddComponent<GroundSnowPile>();
        pile._lifetimeRemaining = lifetimeSec;
        pile._blinkDuration = blinkDurationSec;
        pile._initScale = go.transform.localScale;
        return pile;
    }

    void Update()
    {
        if (_blinking)
        {
            float elapsed = Time.time - _blinkStartTime;
            if (elapsed >= _blinkDuration)
            {
                if (gameObject != null) Object.Destroy(gameObject);
                return;
            }
            float t = elapsed / _blinkDuration;
            float scale = Mathf.Lerp(1f, 0.01f, t);
            transform.localScale = new Vector3(_initScale.x * scale, _initScale.y * scale, _initScale.z * scale);
            return;
        }
        _lifetimeRemaining -= Time.deltaTime;
        if (_lifetimeRemaining <= 0f)
        {
            _blinking = true;
            _blinkStartTime = Time.time;
            _initScale = transform.localScale;
        }
    }
}
