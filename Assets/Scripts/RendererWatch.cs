using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>青い板・ブリンクパネルの発生源特定用。t=0.1/1/2/3/5でRendererWatch、enabled切替でRendererBlink、TopBlue/TopTransparent出力。</summary>
public class RendererWatch : MonoBehaviour
{
    static readonly Dictionary<int, bool> _prevEnabled = new Dictionary<int, bool>();
    static readonly Dictionary<int, float> _blinkCooldown = new Dictionary<int, float>();
    static float[] _watchTimes = { 0.1f, 1f, 2f, 3f, 5f };
    static int _nextWatchIdx;

    void LateUpdate()
    {
        try
        {
            float t = Time.time;
            if (_nextWatchIdx < _watchTimes.Length && t >= _watchTimes[_nextWatchIdx])
            {
                EmitRendererWatch(t);
                EmitTopCandidates();
                _nextWatchIdx++;
            }
            CheckBlink();
        }
        catch (Exception ex)
        {
            UnityEngine.Debug.LogWarning($"[RendererWatch] exception: {ex.Message}");
        }
    }

    static void EmitRendererWatch(float t)
    {
        try { SnowLoopLogCapture.AppendToAssiReport($"[RendererWatch] t={t:F2} === START ==="); }
        catch (Exception) { return; }
#pragma warning disable 0618
        var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
#pragma warning restore 0618
        int count = 0;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            try
            {
                if (r.sharedMaterial == null) continue;
                string path = GetPath(r.transform);
                string layer = r.gameObject != null && r.gameObject.layer >= 0 ? LayerMask.LayerToName(r.gameObject.layer) : "?";
                string matName = r.sharedMaterial != null ? r.sharedMaterial.name : "null";
                string shaderName = r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "?";
                Color c = Color.white;
                try
                {
                    if (r.sharedMaterial.HasProperty("_Color")) c = r.sharedMaterial.GetColor("_Color");
                    else if (r.sharedMaterial.HasProperty("_BaseColor")) c = r.sharedMaterial.GetColor("_BaseColor");
                }
                catch { }
                int queue = r.sharedMaterial != null ? r.sharedMaterial.renderQueue : 0;
                var b = r.bounds;
                SnowLoopLogCapture.AppendToAssiReport($"[RendererWatch] name={r.name} path={path} enabled={r.enabled} active={(r.gameObject != null && r.gameObject.activeInHierarchy)} layer={layer} mat={matName} shader={shaderName} color=({c.r:F2},{c.g:F2},{c.b:F2}) alpha={c.a:F2} queue={queue} boundsCenter=({b.center.x:F2},{b.center.y:F2},{b.center.z:F2}) boundsSize=({b.size.x:F2},{b.size.y:F2},{b.size.z:F2})");
                count++;
            }
            catch (Exception) { }
        }
        SnowLoopLogCapture.AppendToAssiReport($"[RendererWatch] t={t:F2} count={count} === END ===");
    }

    static void CheckBlink()
    {
        try
        {
    #pragma warning disable 0618
        var renderers = UnityEngine.Object.FindObjectsOfType<Renderer>();
#pragma warning restore 0618
            foreach (var r in renderers)
            {
                if (r == null) continue;
                try
                {
                    int id = r.GetInstanceID();
                    bool cur = r.enabled;
                    if (_prevEnabled.TryGetValue(id, out bool prev) && prev != cur)
                    {
                        float t = Time.time;
                        if (!_blinkCooldown.TryGetValue(id, out float last) || t - last > 0.5f)
                        {
                            _blinkCooldown[id] = t;
                            string path = GetPath(r.transform);
                            string st = UnityEngine.StackTraceUtility.ExtractStackTrace();
                            SnowLoopLogCapture.AppendToAssiReport($"[RendererBlink] name={r.name} path={path} enabled={prev}->{cur}");
                            SnowLoopLogCapture.AppendToAssiReport($"[RendererBlink] stacktrace:\n{st}");
                        }
                    }
                    _prevEnabled[id] = cur;
                }
                catch (Exception) { }
            }
        }
        catch (Exception) { }
    }

    static void EmitTopCandidates()
    {
        try
        {
            var blueList = new List<KeyValuePair<Renderer, float>>();
            var transList = new List<KeyValuePair<Renderer, float>>();
            UnityEngine.Renderer[] renderers2;
#pragma warning disable 0618
            renderers2 = UnityEngine.Object.FindObjectsOfType<Renderer>();
#pragma warning restore 0618
            foreach (var r in renderers2)
            {
                if (r == null || r.sharedMaterial == null) continue;
                try
                {
                    Color c = Color.white;
                    try
                    {
                        if (r.sharedMaterial.HasProperty("_Color")) c = r.sharedMaterial.GetColor("_Color");
                        else if (r.sharedMaterial.HasProperty("_BaseColor")) c = r.sharedMaterial.GetColor("_BaseColor");
                    }
                    catch { }
                    string shaderName = r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "";
                    float blueScore = 0f;
                    if (c.b > c.r && c.b > c.g) blueScore = c.b + (1f - c.r) * 0.5f;
                    if (shaderName.IndexOf("Unlit", StringComparison.OrdinalIgnoreCase) >= 0) blueScore += 0.5f;
                    if (shaderName.IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0) blueScore += 0.3f;
                    if (blueScore > 0f) blueList.Add(new KeyValuePair<Renderer, float>(r, blueScore));

                    float transScore = 0f;
                    if (c.a < 0.5f) transScore = 1f - c.a;
                    if (shaderName.IndexOf("Transparent", StringComparison.OrdinalIgnoreCase) >= 0) transScore += 1f;
                    if (shaderName.IndexOf("Sprites", StringComparison.OrdinalIgnoreCase) >= 0 && c.a < 0.5f) transScore += 0.5f;
                    if (transScore > 0f) transList.Add(new KeyValuePair<Renderer, float>(r, transScore));
                }
                catch { }
            }
        blueList.Sort((a, b) => b.Value.CompareTo(a.Value));
        transList.Sort((a, b) => b.Value.CompareTo(a.Value));
        SnowLoopLogCapture.AppendToAssiReport("[TopBlueCandidates]:");
            for (int i = 0; i < Mathf.Min(5, blueList.Count); i++)
            {
                var kv = blueList[i];
                if (kv.Key == null) continue;
                string path = GetPath(kv.Key.transform);
                string matName = kv.Key.sharedMaterial != null ? kv.Key.sharedMaterial.name : "?";
                SnowLoopLogCapture.AppendToAssiReport($"  [{i}] {kv.Key.name} path={path} mat={matName} score={kv.Value:F2}");
            }
            SnowLoopLogCapture.AppendToAssiReport("[TopTransparentCandidates]:");
            for (int i = 0; i < Mathf.Min(5, transList.Count); i++)
            {
                var kv = transList[i];
                if (kv.Key == null) continue;
                string path = GetPath(kv.Key.transform);
                string matName = kv.Key.sharedMaterial != null ? kv.Key.sharedMaterial.name : "?";
                SnowLoopLogCapture.AppendToAssiReport($"  [{i}] {kv.Key.name} path={path} mat={matName} score={kv.Value:F2}");
            }
        }
        catch (Exception) { }
    }

    static string GetPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }
}
