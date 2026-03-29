using UnityEngine;

/// <summary>
/// 外部観測責務：
///   Start() で SafeStateDiffReader から CurrentState を一度だけ取得し、
///   _cachedState に保持する。OnGUI() は _cachedState のみを表示する。
///   この責務以外は追加しない。
/// </summary>
public sealed class SafeStateExternalObserver : MonoBehaviour
{
    private string _cachedState = "NotChecked";

    private void Start()
    {
        var reader = FindObjectOfType<SafeStateDiffReader>();
        if (reader == null)
        {
            Debug.Log("[SafeExternal] ReaderNotFound");
            return;
        }
        _cachedState = reader.CurrentState.ToString();
        Debug.Log("[SafeExternal] " + _cachedState);
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(4, 28, 240, 24), "SafeExternal: " + _cachedState);
    }
}
