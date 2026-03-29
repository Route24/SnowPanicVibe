using UnityEngine;

/// <summary>
/// カメラ前に強制的に巨大赤キューブを生成する visibility 確認スクリプト。
/// Play 開始直後に実行される。既存システムに一切依存しない。
/// 確認後は SnowPanic → Remove Camera Visibility Test で削除可能。
/// v2 — 赤キューブ生成を停止（SnowVisibilityLab 最小化対応）
/// </summary>
[DefaultExecutionOrder(-2000)]
public class CameraVisibilityTest : MonoBehaviour
{
    void Start()
    {
        // 赤キューブ生成を停止済み（SnowVisibilityLab 最小化対応）
        Debug.Log("[CAM_VISIBILITY] red_cube_generation=DISABLED");
    }
}
