using UnityEngine;

public sealed class SnowBlockNode : MonoBehaviour
{
    private bool isConsumed;

    public void OnHit()
    {
        if (isConsumed)
        {
            return;
        }

        isConsumed = true;
        Debug.Log("[SnowBlock] cyan_box_destroyed=YES name=" + gameObject.name);

        // AntiProtocolVisibilityReporter に破棄を通知（unexpected_respawn 判定用）
        var reporter = FindObjectOfType<AntiProtocolVisibilityReporter>();
        if (reporter != null) reporter.NotifyCyanDestroyed();

        Destroy(gameObject);
    }
}
