using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// 検証用: 最小構成の正解サンプル。
/// 既存実装に依存せず、単純な板屋根＋雪で「屋根全面積雪」「崩壊後も屋根外にはみ出さない」を実現。
/// 使い方: 空のGameObjectに本スクリプトをアタッチ → Play。
/// 本実装のみで試す場合: SnowTest を無効化するか、新規シーンで実行。
/// </summary>
public class SnowPanicMinimalSample : MonoBehaviour
{
    [Header("Roof (板1枚)")]
    public float roofWidth = 4f;
    public float roofLength = 6f;
    public float roofHeight = 2f;

    [Header("Snow")]
    public float pieceSize = 0.25f;
    public int layers = 3;
    public float tapRadius = 0.8f;
    public int tapRaycastLayer = -1;

    [Header("Ground Snow")]
    public float groundSnowLifetime = 3f;
    public float groundSnowHeight = 0.15f;
    public float groundY = 0f;

    [Header("Debug")]
    public bool logEvery3Sec = true;

    GameObject _roof;
    Transform _snowRoot;
    List<Transform> _roofPieces = new List<Transform>();
    List<FallingEntry> _falling = new List<FallingEntry>();
    List<GroundSnowEntry> _groundSnow = new List<GroundSnowEntry>();
    Bounds _roofWorldBounds;
    Bounds _initialSnowBounds;
    float _nextLogTime = -10f;
    float _nextClipTime = -10f;
    bool _groundSnowEverSpawned;

    struct FallingEntry { public Transform t; public float detachTime; }
    struct GroundSnowEntry { public GameObject go; public float spawnTime; }

    void Start()
    {
        CreateGround();
        CreateRoof();
        SpawnRoofSnow();
        LogState("initial");
    }

    void CreateGround()
    {
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "MinimalSample_Ground";
        ground.transform.SetParent(transform);
        ground.transform.localPosition = new Vector3(0, groundY, 0);
        ground.transform.localScale = new Vector3(2f, 1f, 2f);
        ground.GetComponent<Renderer>().sharedMaterial.color = new Color(0.3f, 0.3f, 0.35f);
    }

    void CreateRoof()
    {
        _roof = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _roof.name = "MinimalSample_Roof";
        _roof.transform.SetParent(transform);
        _roof.transform.localPosition = new Vector3(0, roofHeight, 0);
        _roof.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        _roof.transform.localScale = new Vector3(roofWidth, roofLength, 1f);
        _roof.GetComponent<Renderer>().sharedMaterial.color = new Color(0.5f, 0.35f, 0.2f);
        _roofWorldBounds = _roof.GetComponent<Renderer>().bounds;
    }

    void SpawnRoofSnow()
    {
        var root = new GameObject("MinimalSample_SnowRoot");
        root.transform.SetParent(transform);
        root.transform.position = _roof.transform.position;
        root.transform.rotation = _roof.transform.rotation;
        _snowRoot = root.transform;

        int nx = Mathf.Max(1, Mathf.FloorToInt(roofWidth / pieceSize));
        int nz = Mathf.Max(1, Mathf.FloorToInt(roofLength / pieceSize));
        float stepX = roofWidth / nx;
        float stepZ = roofLength / nz;
        float halfW = roofWidth * 0.5f;
        float halfL = roofLength * 0.5f;
        Vector3 roofCenter = _roof.transform.position;
        Vector3 right = _roof.transform.right;
        Vector3 forward = _roof.transform.forward;

        for (int layer = 0; layer < layers; layer++)
        {
            float yOff = layer * pieceSize * 0.9f;
            for (int ix = 0; ix < nx; ix++)
            {
                for (int iz = 0; iz < nz; iz++)
                {
                    float u = (ix + 0.5f) / nx - 0.5f;
                    float v = (iz + 0.5f) / nz - 0.5f;
                    Vector3 offset = right * (u * roofWidth) + forward * (v * roofLength);
                    Vector3 pos = roofCenter + offset + Vector3.up * yOff;

                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "snow";
                    cube.transform.localScale = Vector3.one * pieceSize * 0.95f;
                    cube.transform.position = pos;
                    cube.transform.SetParent(_snowRoot);
                    cube.GetComponent<Renderer>().sharedMaterial.color = new Color(0.95f, 0.97f, 1f);
                    _roofPieces.Add(cube.transform);
                }
            }
        }

        _roofWorldBounds = ComputeRoofAABB();
        _initialSnowBounds = ComputeSnowAABB();
        UnityEngine.Debug.Log($"[MINIMAL_SAMPLE] roof created wx={roofWidth:F2} lx={roofLength:F2} pieces={_roofPieces.Count} test_roof_bounds_size=({_roofWorldBounds.size.x:F3},{_roofWorldBounds.size.y:F3},{_roofWorldBounds.size.z:F3}) ground_snow_lifetime={groundSnowLifetime:F1}");
    }

    Bounds ComputeRoofAABB()
    {
        var r = _roof.GetComponent<Renderer>();
        return r != null ? r.bounds : new Bounds(_roof.transform.position, new Vector3(roofWidth, 0.02f, roofLength));
    }

    Bounds ComputeSnowAABB()
    {
        Bounds b = new Bounds();
        bool first = true;
        for (int i = 0; i < _roofPieces.Count; i++)
        {
            var t = _roofPieces[i];
            if (t == null || !t.gameObject.activeSelf) continue;
            if (first) { b = new Bounds(t.position, Vector3.one * 0.01f); first = false; }
            else b.Encapsulate(t.position);
        }
        return first ? new Bounds(_roof.transform.position, Vector3.zero) : b;
    }

    bool SnowExceedsRoof(Bounds snowBounds)
    {
        var r = _roofWorldBounds;
        float tol = 0.02f;
        return snowBounds.min.x < r.min.x - tol || snowBounds.max.x > r.max.x + tol ||
               snowBounds.min.z < r.min.z - tol || snowBounds.max.z > r.max.z + tol;
    }

    void ClipRemainingToRoof()
    {
        var r = _roofWorldBounds;
        Vector3 roofCenter = _roof.transform.position;
        Vector3 right = _roof.transform.right;
        Vector3 forward = _roof.transform.forward;
        float halfW = roofWidth * 0.5f;
        float halfL = roofLength * 0.5f;
        float margin = 0.02f;
        int clipped = 0;
        for (int i = _roofPieces.Count - 1; i >= 0; i--)
        {
            var t = _roofPieces[i];
            if (t == null || !t.gameObject.activeSelf) continue;
            Vector3 d = t.position - roofCenter;
            float u = Vector3.Dot(d, right);
            float v = Vector3.Dot(d, forward);
            if (Mathf.Abs(u) > halfW + margin || Mathf.Abs(v) > halfL + margin)
            {
                t.gameObject.SetActive(false);
                clipped++;
            }
        }
        if (clipped > 0)
            UnityEngine.Debug.Log($"[MINIMAL_SAMPLE] clip_to_roof count={clipped}");
    }

    void LogState(string stage)
    {
        var roofB = ComputeRoofAABB();
        var snowB = ComputeSnowAABB();
        bool matches = SnowBoundsMatchRoof(snowB, roofB);
        bool exceeds = _roofPieces.Count > 0 && SnowExceedsRoof(snowB);
        int activeCount = 0;
        for (int i = 0; i < _roofPieces.Count; i++)
            if (_roofPieces[i] != null && _roofPieces[i].gameObject.activeSelf) activeCount++;

        var initB = _initialSnowBounds.size;
        var remB = snowB.size;
        UnityEngine.Debug.Log($"[MINIMAL_SAMPLE] stage={stage} test_roof_bounds_size=({roofB.size.x:F3},{roofB.size.y:F3},{roofB.size.z:F3}) test_snow_initial_bounds_size=({initB.x:F3},{initB.y:F3},{initB.z:F3}) test_snow_remaining_bounds_size=({remB.x:F3},{remB.y:F3},{remB.z:F3}) test_snow_matches_roof={matches.ToString().ToLower()} remaining_snow_exceeds_roof={exceeds.ToString().ToLower()} ground_snow_spawned={_groundSnowEverSpawned.ToString().ToLower()} ground_snow_lifetime={groundSnowLifetime:F1}");
    }

    bool SnowBoundsMatchRoof(Bounds snowB, Bounds roofB)
    {
        float tol = 0.1f;
        return Mathf.Abs(snowB.size.x - roofB.size.x) < tol && Mathf.Abs(snowB.size.z - roofB.size.z) < tol;
    }

    void Update()
    {
        if (_roof == null || _snowRoot == null) return;

        _roofWorldBounds = ComputeRoofAABB();

        if (Time.time >= _nextClipTime)
        {
            _nextClipTime = Time.time + 0.2f;
            ClipRemainingToRoof();
        }

        if (logEvery3Sec && Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + 3f;
            LogState("periodic");
        }

        HandleTap();
        UpdateFalling();
        UpdateGroundSnow();
    }

    void HandleTap()
    {
        if (!Input.GetMouseButtonDown(0)) return;
        var cam = Camera.main;
        if (cam == null) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Ray r = cam.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(r, out RaycastHit hit, 50f)) return;

        Vector3 tapPoint = hit.point;
        int detached = 0;
        for (int i = _roofPieces.Count - 1; i >= 0; i--)
        {
            var t = _roofPieces[i];
            if (t == null || !t.gameObject.activeSelf) continue;
            float dist = Vector3.Distance(t.position, tapPoint);
            if (dist > tapRadius) continue;

            t.SetParent(null);
            var rb = t.gameObject.AddComponent<Rigidbody>();
            rb.useGravity = true;
            rb.mass = 0.1f;
            _falling.Add(new FallingEntry { t = t, detachTime = Time.time });
            detached++;
        }
        if (detached > 0)
            UnityEngine.Debug.Log($"[MINIMAL_SAMPLE] tap detached={detached} pos=({tapPoint.x:F2},{tapPoint.y:F2},{tapPoint.z:F2})");
    }

    void UpdateFalling()
    {
        for (int i = _falling.Count - 1; i >= 0; i--)
        {
            var e = _falling[i];
            if (e.t == null)
            {
                _falling.RemoveAt(i);
                continue;
            }
            if (e.t.position.y < groundY + 0.3f)
            {
                SpawnGroundSnow(e.t.position);
                _groundSnowEverSpawned = true;
                UnityEngine.Object.Destroy(e.t.gameObject);
                _falling.RemoveAt(i);
                UnityEngine.Debug.Log($"[MINIMAL_SAMPLE] ground_snow_spawned=true pos=({e.t.position.x:F2},{groundY:F2},{e.t.position.z:F2})");
            }
        }
    }

    void SpawnGroundSnow(Vector3 hitPos)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "ground_snow";
        cube.transform.position = new Vector3(hitPos.x, groundY + groundSnowHeight * 0.5f, hitPos.z);
        cube.transform.localScale = new Vector3(pieceSize * 1.2f, groundSnowHeight, pieceSize * 1.2f);
        cube.GetComponent<Renderer>().sharedMaterial.color = new Color(0.9f, 0.93f, 1f);
        cube.transform.SetParent(transform);
        _groundSnow.Add(new GroundSnowEntry { go = cube, spawnTime = Time.time });
    }

    void UpdateGroundSnow()
    {
        float now = Time.time;
        for (int i = _groundSnow.Count - 1; i >= 0; i--)
        {
            var e = _groundSnow[i];
            if (e.go == null)
            {
                _groundSnow.RemoveAt(i);
                continue;
            }
            if (now - e.spawnTime >= groundSnowLifetime)
            {
                UnityEngine.Object.Destroy(e.go);
                _groundSnow.RemoveAt(i);
            }
        }
    }
}
