using UnityEngine;

public class CorniceSnowManager : MonoBehaviour
{
    [Header("Cornice layout")]
    // 横方向（屋根の幅方向）の分割数
    [Min(1)] public int segmentCount = 30;
    // 奥行き方向の段数
    [Min(1)] public int rows = 4;
    public float roofWidth = 4f;
    public float roofDepth = 3f;
    public float overhang = 0.4f;
    public Vector3 segmentSize = new Vector3(0.2f, 0.15f, 0.2f);
    public float gap = 0.02f;

    [Header("Collapse behavior")]
    [Min(0.1f)] public float fallImpulse = 3f;

    CorniceSnowSegment[] _segments;

    void Start()
    {
        BuildSegments();
    }

    void BuildSegments()
    {
        // Clear existing children (if any)
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }

        int total = segmentCount * rows;
        _segments = new CorniceSnowSegment[total];

        var parent = new GameObject("CorniceSnow");
        parent.transform.SetParent(transform, false);

        float usableWidth = roofWidth - gap;
        float stepX = usableWidth / segmentCount;

        float usableDepth = roofDepth - gap;
        float stepZ = usableDepth / rows;

        // 屋根厚み(おおよそ)と雪ブロック高さから、屋根表面上に接するようなオフセットを計算
        float roofThickness = 0.2f;
        float surfaceOffset = (roofThickness * 0.5f) + (segmentSize.y * 0.5f);

        int index = 0;
        for (int row = 0; row < rows; row++)
        {
            float zLocal = -usableDepth * 0.5f + stepZ * (row + 0.5f);

            for (int i = 0; i < segmentCount; i++)
            {
                float t = (i + 0.5f) / segmentCount;
                float xLocal = -usableWidth * 0.5f + stepX * (i + 0.5f);

                // 屋根ローカルY方向にオフセットして、屋根面のすぐ上に積もらせる
                Vector3 localPos = new Vector3(xLocal, surfaceOffset, zLocal);
                Vector3 worldPos = transform.TransformPoint(localPos);

                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = $"Cornice_{row + 1:00}_{i + 1:00}";
                cube.transform.SetParent(parent.transform, true);
                cube.transform.position = worldPos;
                cube.transform.rotation = transform.rotation;
                cube.transform.localScale = segmentSize;

                var rb = cube.AddComponent<Rigidbody>();
                // 最初は屋根に「くっついて」見えるように重力オフ。崩落時にオンにする。
                rb.useGravity = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;

                var seg = cube.AddComponent<CorniceSnowSegment>();
                seg.index = index;
                seg.normalizedPosition = t;
                seg.manager = this;

                _segments[index] = seg;
                index++;
            }
        }
    }

    public void OnSegmentHit(CorniceSnowSegment segment, float power)
    {
        if (_segments == null || _segments.Length == 0) return;

        int idx = Mathf.Clamp(segment.index, 0, _segments.Length - 1);
        float t = segment.normalizedPosition;

        // 叩いた位置で挙動を変える:
        // 中央付近なら屋根全体が一気に崩落、それ以外は一部分だけ落ちる。
        if (t > 0.3f && t < 0.7f)
        {
            // フル崩落
            for (int i = 0; i < _segments.Length; i++)
            {
                if (_segments[i] != null)
                    _segments[i].Collapse();
            }
        }
        else
        {
            // 局所的な崩落（周辺数ブロックだけ）
            int radius = 1;
            int from = Mathf.Max(0, idx - radius);
            int to = Mathf.Min(_segments.Length - 1, idx + radius);

            for (int i = from; i <= to; i++)
            {
                if (_segments[i] != null)
                    _segments[i].Collapse();
            }
        }
    }

    public Vector3 GetOutwardDirection()
    {
        // 屋根の法線から「外側」をざっくり計算（前方に倒れるイメージ）
        return transform.forward;
    }
}

