using UnityEngine;

/// <summary>古い屋根雪オブジェクト（巨大立方体など）をPlay開始時に削除</summary>
[DefaultExecutionOrder(-100)]
public class RoofSnowCleanup : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        var go = new GameObject("RoofSnowCleanup");
        go.AddComponent<RoofSnowCleanup>();
    }

    void Start()
    {
        foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (t != null && (t.name == "SnowBase" || t.name == "RoofSnowPlane"))
                Destroy(t.gameObject);
        }
    }
}
