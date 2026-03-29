using UnityEngine;

/// <summary>
/// Safe 基準との比較結果を内部に保持する。外部への影響なし。
/// </summary>
public sealed class SafeStateDiffReader : MonoBehaviour
{
    public enum DiffState { NotChecked, MatchFound, DiffFound }

    private DiffState _state = DiffState.NotChecked;

    public DiffState CurrentState => _state;

    private void Start()
    {
        bool isMatch = (SafeStateSnapshot.SafeStateVersion == "TASK_SNOW_SHAPE_MEASURED");
        _state = isMatch ? DiffState.MatchFound : DiffState.DiffFound;
        Debug.Log("[SafeDiff] " + _state);
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(4, 4, 240, 24), "SafeDiff: " + _state);
    }
}
