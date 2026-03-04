using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>ミニマル再現シーン用: 乱数seed固定・15秒で停止</summary>
public class SnowMinReproBootstrap : MonoBehaviour
{
    public const int FixedSeed = 12345;
    public const float TestDurationSeconds = 15f;

    void Awake()
    {
        if (SceneManager.GetActiveScene().name.Contains("MinRepro"))
        {
            Random.InitState(FixedSeed);
            UnityEngine.Debug.Log($"[MinRepro] Random.InitState({FixedSeed})");
        }
    }

    void Update()
    {
        if (!SceneManager.GetActiveScene().name.Contains("MinRepro")) return;
        if (Time.time >= TestDurationSeconds)
        {
            UnityEngine.Debug.Log($"[MinRepro] Test duration {TestDurationSeconds}s reached. Stopping.");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            enabled = false;
#endif
        }
    }
}
