using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Visual-only roof snow pack generator.
/// Place this on one GameObject and assign roof collider.
/// </summary>
public class SnowPackSpawner : MonoBehaviour
{
    const string SnowVisualLayerName = "SnowVisual";

    [Header("Target")]
    public Collider roofCollider;
    public RoofSnowSystem roofSnowSystem;

    [Header("Sync (Depth view)")]
    public float syncIntervalSeconds = 0.2f;
    public float addThreshold = 0.08f;
    public float removeThreshold = -0.08f;
    public float minSyncInterval = 0.50f;
    public int maxLayerStep = 2;
    [Tooltip("Current displayed depth (updated on Rebuild/Add/Remove)")]
    public float packDepthMeters;

    [Header("Look")]
    [Range(0.1f, 1.5f)] public float targetDepthMeters = 0.5f;
    [Range(0.05f, 0.5f)] public float pieceSize = 0.11f;
    [Range(0.5f, 2f)] public float pieceHeightScale = 0.85f;
    [Range(0f, 0.08f)] public float jitter = 0.03f;
    [Range(0f, 0.06f)] public float normalInset = 0.01f;
    public int maxPieces = 1800;
    public bool rebuildOnPlay = true;

    [Header("Material")]
    public Color snowColor = new Color(0.93f, 0.96f, 1f, 1f);

    Transform _visualRoot;
    Material _snowMat;
    Mesh _pieceMesh;
    bool _generatedThisPlay;
    bool _spawnLogOnce;
    float _nextToggleLogTime;
    float _nextAuditLogTime;
    float _nextSyncCheckTime;
    float _nextSyncAllowedAt;
    const bool UsingLocalPosition = true;

    readonly List<List<Transform>> _layerPieces = new List<List<Transform>>();
    float _cachedLayerStep;
    int _cachedNx, _cachedNz;
    Vector3 _cachedLocalCenter;
    float _cachedHalfX, _cachedHalfZ;
    int _rebuildCount;
    int _addCount;
    int _removeCount;
    bool _inAvalancheSlide;

    void OnEnable()
    {
        if (!Application.isPlaying || _generatedThisPlay) return;
        if (!rebuildOnPlay)
        {
            _generatedThisPlay = true;
            RebuildSnowPack("OnEnable");
        }
    }

    void Start()
    {
        if (!Application.isPlaying || !rebuildOnPlay || _generatedThisPlay) return;
        _generatedThisPlay = true;
        LogSnowPackCall("REBUILD", "Start");
        RebuildSnowPack("RebuildOnPlay");
    }

    [ContextMenu("Rebuild Snow Pack")]
    public void Rebuild()
    {
        RebuildSnowPack("ContextMenu");
    }

    public void RebuildSnowPack(string reason)
    {
        if (roofCollider == null)
            roofCollider = ResolveRoofCollider();
        if (roofCollider == null)
        {
            Debug.LogWarning("[SnowPack] roofCollider is not assigned.");
            return;
        }

        EnsureRoot();
        LogSnowPackCall("REBUILD", reason);
        ClearSnowPack(reason);
        EnsureMaterial();
        EnsurePieceMesh();
        EnsureSnowVisualCollisionSetup();
        AlignVisualRootToRoof();

        Vector3 roofUp = roofCollider.transform.up.normalized;
        Vector3 roofFwd = roofCollider.transform.forward.normalized;
        float roofRotY = roofCollider.transform.rotation.eulerAngles.y;
        float packRotY = _visualRoot != null ? _visualRoot.rotation.eulerAngles.y : 0f;
        Debug.Log($"[SnowPackBasis] usingLocal=true roofUp={roofUp} roofFwd={roofFwd} roofRotY={roofRotY:F1} packRotY={packRotY:F1}");

        CacheGridParams();
        int layers = Mathf.Max(1, Mathf.CeilToInt(targetDepthMeters / Mathf.Max(0.02f, _cachedLayerStep)));
        int spawned = 0;
        for (int y = 0; y < layers; y++)
        {
            var layerList = SpawnLayer(y);
            _layerPieces.Add(layerList);
            spawned += layerList.Count;
            if (spawned >= maxPieces) break;
        }

        _rebuildCount++;
        AuditSnowPackPhysics();
        packDepthMeters = targetDepthMeters;
        Debug.Log($"[SnowPack] generated={spawned} depth={targetDepthMeters:F2} pieceSize={pieceSize:F2} layers={layers}");
    }

    void CacheGridParams()
    {
        float size = Mathf.Max(0.05f, pieceSize);
        GetRoofLocalRect(out _cachedLocalCenter, out _cachedHalfX, out _cachedHalfZ);
        _cachedLayerStep = Mathf.Max(0.02f, size * pieceHeightScale);
        _cachedNx = Mathf.Max(1, Mathf.CeilToInt(_cachedHalfX * 2f / size));
        _cachedNz = Mathf.Max(1, Mathf.CeilToInt(_cachedHalfZ * 2f / size));
    }

    List<Transform> SpawnLayer(int layerIndex)
    {
        var list = new List<Transform>();
        if (roofCollider == null || _visualRoot == null) return list;
        Vector3 roofUp = roofCollider.transform.up.normalized;
        float size = Mathf.Max(0.05f, pieceSize);
        int existing = 0;
        for (int i = 0; i < _layerPieces.Count; i++)
            existing += _layerPieces[i].Count;
        for (int iz = 0; iz < _cachedNz; iz++)
        {
            for (int ix = 0; ix < _cachedNx; ix++)
            {
                if (existing + list.Count >= maxPieces) break;
                float tx = _cachedNx <= 1 ? 0.5f : ix / (float)(_cachedNx - 1);
                float tz = _cachedNz <= 1 ? 0.5f : iz / (float)(_cachedNz - 1);
                Vector3 localP = new Vector3(
                    _cachedLocalCenter.x + Mathf.Lerp(-_cachedHalfX, _cachedHalfX, tx) + Random.Range(-jitter, jitter),
                    _cachedLocalCenter.y + layerIndex * _cachedLayerStep + normalInset,
                    _cachedLocalCenter.z + Mathf.Lerp(-_cachedHalfZ, _cachedHalfZ, tz) + Random.Range(-jitter, jitter));
                Vector3 worldCheck = roofCollider.transform.TransformPoint(localP);
                Vector3 cp = roofCollider.ClosestPoint(worldCheck + roofUp * 0.1f);
                if ((cp - worldCheck).sqrMagnitude > 0.35f) continue;
                var t = SpawnPieceLocal(localP, size);
                list.Add(t);
            }
        }
        return list;
    }

    [ContextMenu("Clear Snow Pack")]
    public void ClearNow()
    {
        ClearSnowPack("ContextMenu");
    }

    public void ManualRebuildButton() => RebuildSnowPack("Manual");
    public void ManualClearButton() => ClearSnowPack("Manual");
    public void RebuildDepthSync() => RebuildSnowPack("DepthSync");

    public void PlayAvalancheSlideVisual(float burstAmount, Vector3 slideOffset, float duration)
    {
        if (duration <= 0f || _visualRoot == null || roofCollider == null) return;
        if (_cachedLayerStep <= 0f) CacheGridParams();
        int layersToRemove = Mathf.Min(
            Mathf.Max(0, Mathf.RoundToInt(burstAmount / _cachedLayerStep)),
            _layerPieces.Count);
        if (layersToRemove <= 0) return;

        var layers = new List<List<Transform>>();
        for (int i = 0; i < layersToRemove && _layerPieces.Count > 0; i++)
        {
            var layer = _layerPieces[_layerPieces.Count - 1];
            _layerPieces.RemoveAt(_layerPieces.Count - 1);
            layers.Add(layer);
        }
        Debug.Log($"[AvalancheVisual] start amount={burstAmount:F3} duration={duration:F2} offset={slideOffset} removedLayers={layersToRemove}");
        _inAvalancheSlide = true;
        StartCoroutine(AvalancheSlideRoutine(layers, slideOffset, duration));
    }

    IEnumerator AvalancheSlideRoutine(List<List<Transform>> layers, Vector3 slideOffset, float duration)
    {
        Vector3 startWorldPos = _visualRoot.position;
        var slideRoot = new GameObject("AvalancheSlideTemp");
        slideRoot.transform.SetParent(_visualRoot.parent, false);
        slideRoot.transform.position = startWorldPos;
        slideRoot.transform.rotation = _visualRoot.rotation;

        int movedPieces = 0;
        for (int i = 0; i < layers.Count; i++)
        {
            for (int j = 0; j < layers[i].Count; j++)
            {
                var t = layers[i][j];
                if (t != null)
                {
                    t.SetParent(slideRoot.transform, true);
                    movedPieces++;
                }
            }
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t01 = Mathf.Clamp01(elapsed / duration);
            slideRoot.transform.position = startWorldPos + slideOffset * t01;
            yield return null;
        }

        Vector3 endWorldPos = startWorldPos + slideOffset;
        float movedMeters = Vector3.Distance(startWorldPos, endWorldPos);

        for (int i = 0; i < layers.Count; i++)
        {
            for (int j = 0; j < layers[i].Count; j++)
            {
                if (layers[i][j] != null)
                    Object.Destroy(layers[i][j].gameObject);
            }
        }
        Object.Destroy(slideRoot);

        packDepthMeters = Mathf.Max(0f, packDepthMeters - layers.Count * _cachedLayerStep);
        _removeCount += layers.Count;
        _inAvalancheSlide = false;
        Debug.Log($"[AvalancheVisual] end movedMeters={movedMeters:F3} start=({startWorldPos.x:F2},{startWorldPos.y:F2},{startWorldPos.z:F2}) end=({endWorldPos.x:F2},{endWorldPos.y:F2},{endWorldPos.z:F2}) durationActual={elapsed:F2} removedLayers={layers.Count} movedPieces={movedPieces}");
    }

    Transform SpawnPieceLocal(Vector3 localPos, float size)
    {
        var go = new GameObject("SnowPackPiece");
        go.transform.SetParent(_visualRoot, false);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        if (_pieceMesh != null) mf.sharedMesh = _pieceMesh;
        if (_snowMat != null) mr.sharedMaterial = _snowMat;

        go.name = "SnowPackPiece";
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.identity;
        float h = Mathf.Max(0.03f, size * pieceHeightScale * Random.Range(0.8f, 1.2f));
        float w = Mathf.Max(0.03f, size * Random.Range(0.8f, 1.15f));
        go.transform.localScale = new Vector3(w, h, w);
        int snowLayer = LayerMask.NameToLayer(SnowVisualLayerName);
        if (snowLayer < 0) snowLayer = LayerMask.NameToLayer("Ignore Raycast");
        if (snowLayer < 0) snowLayer = 2;
        SetLayerRecursively(go, snowLayer);
        LogSpawnOnce(go);
        return go.transform;
    }

    void EnsureRoot()
    {
        if (roofCollider == null) return;
        var roofT = roofCollider.transform;
        var t = roofT.Find("SnowPackVisual");
        if (t == null)
        {
            var go = new GameObject("SnowPackVisual");
            go.transform.SetParent(roofT, false);
            t = go.transform;
        }
        _visualRoot = t;
    }

    void EnsureMaterial()
    {
        if (_snowMat != null) return;
        Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (sh == null) return;
        _snowMat = new Material(sh);
        _snowMat.color = snowColor;
    }

    void EnsurePieceMesh()
    {
        if (_pieceMesh != null) return;
        _pieceMesh = BuildCubeMesh();
    }

    static Mesh BuildCubeMesh()
    {
        var m = new Mesh { name = "SnowPackCubeMesh" };
        var v = new Vector3[]
        {
            new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f),
            new Vector3(0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(-0.5f, -0.5f, 0.5f), new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(0.5f, -0.5f, 0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(0.5f, 0.5f, 0.5f),
            new Vector3(-0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, 0.5f), new Vector3(0.5f, 0.5f, -0.5f), new Vector3(-0.5f, 0.5f, -0.5f),
            new Vector3(-0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, -0.5f), new Vector3(0.5f, -0.5f, 0.5f), new Vector3(-0.5f, -0.5f, 0.5f),
        };
        var t = new int[]
        {
            0,2,1, 0,3,2, 4,6,5, 4,7,6, 8,10,9, 8,11,10,
            12,14,13, 12,15,14, 16,18,17, 16,19,18, 20,22,21, 20,23,22
        };
        m.vertices = v;
        m.triangles = t;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    void AlignVisualRootToRoof()
    {
        if (_visualRoot == null || roofCollider == null) return;
        _visualRoot.SetParent(roofCollider.transform, false);
        _visualRoot.localPosition = Vector3.zero;
        _visualRoot.localRotation = Quaternion.identity;
    }

    void GetRoofLocalRect(out Vector3 localCenter, out float halfX, out float halfZ)
    {
        localCenter = Vector3.zero;
        halfX = 1f;
        halfZ = 1f;
        if (roofCollider == null || _visualRoot == null) return;
        if (roofCollider is BoxCollider box && box.transform == roofCollider.transform)
        {
            localCenter = box.center;
            halfX = Mathf.Max(0.1f, box.size.x * 0.5f);
            halfZ = Mathf.Max(0.1f, box.size.z * 0.5f);
            return;
        }
        Bounds b = roofCollider.bounds;
        localCenter = _visualRoot.InverseTransformPoint(b.center);
        Vector3 ex = _visualRoot.InverseTransformVector(new Vector3(b.extents.x, 0f, 0f));
        Vector3 ez = _visualRoot.InverseTransformVector(new Vector3(0f, 0f, b.extents.z));
        halfX = Mathf.Max(0.1f, Mathf.Abs(ex.x));
        halfZ = Mathf.Max(0.1f, Mathf.Abs(ez.z));
    }

    void EnsureSnowVisualCollisionSetup()
    {
        int snowLayer = EnsureSnowVisualLayerExists();
        if (snowLayer < 0) return;
        for (int i = 0; i < 32; i++)
            Physics.IgnoreLayerCollision(snowLayer, i, true);
    }

    int EnsureSnowVisualLayerExists()
    {
        int idx = LayerMask.NameToLayer(SnowVisualLayerName);
#if UNITY_EDITOR
        if (idx < 0)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets != null && assets.Length > 0)
            {
                var tagManager = new SerializedObject(assets[0]);
                var layersProp = tagManager.FindProperty("layers");
                for (int i = 8; i <= 31; i++)
                {
                    var sp = layersProp.GetArrayElementAtIndex(i);
                    if (string.IsNullOrEmpty(sp.stringValue))
                    {
                        sp.stringValue = SnowVisualLayerName;
                        tagManager.ApplyModifiedProperties();
                        idx = i;
                        break;
                    }
                }
            }
        }
#endif
        return idx;
    }

    Collider ResolveRoofCollider()
    {
        var byName = GameObject.Find("RoofSlideCollider");
        if (byName != null) return byName.GetComponent<Collider>();
        var all = FindObjectsByType<Collider>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            var c = all[i];
            if (c == null) continue;
            if (c.name.Contains("RoofSlideCollider")) return c;
        }
        return null;
    }

    void SetLayerRecursively(GameObject go, int layer)
    {
        if (go == null) return;
        go.layer = layer;
        var trs = go.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < trs.Length; i++)
            trs[i].gameObject.layer = layer;
    }

    void LogSpawnOnce(GameObject go)
    {
        if (!Application.isPlaying || _spawnLogOnce || go == null) return;
        _spawnLogOnce = true;
        bool hasCollider = go.GetComponentInChildren<Collider>(true) != null;
        bool hasRb = go.GetComponentInChildren<Rigidbody>(true) != null;
        string layerName = LayerMask.LayerToName(go.layer);
        string parentName = go.transform.parent != null ? go.transform.parent.name : "None";
        Debug.Log($"[SnowPackSpawn] name={go.name} layer={layerName} hasCollider={hasCollider} hasRigidbody={hasRb} parent={parentName}");
    }

    void AuditSnowPackPhysics()
    {
        if (_visualRoot == null) return;
        int colCount = _visualRoot.GetComponentsInChildren<Collider>(true).Length;
        int rbCount = _visualRoot.GetComponentsInChildren<Rigidbody>(true).Length;
        if (colCount != 0 || rbCount != 0)
            Debug.LogWarning($"[SnowPackAudit] colliders={colCount} rigidbodies={rbCount}");
        else
            Debug.Log("[SnowPackAudit] colliders=0 rigidbodies=0");
    }

    public void ClearSnowPack(string reason)
    {
        EnsureRoot();
        LogSnowPackCall("CLEAR", reason);
        for (int i = 0; i < _layerPieces.Count; i++)
        {
            for (int j = 0; j < _layerPieces[i].Count; j++)
            {
                var t = _layerPieces[i][j];
                if (t != null)
                {
                    if (Application.isPlaying) Object.Destroy(t.gameObject);
                    else Object.DestroyImmediate(t.gameObject);
                }
            }
        }
        _layerPieces.Clear();
        ClearChildren(_visualRoot);
    }

    void AddLayers(int n)
    {
        if (n <= 0 || roofCollider == null || _visualRoot == null) return;
        if (_cachedLayerStep <= 0f) CacheGridParams();
        for (int i = 0; i < n; i++)
        {
            var layer = SpawnLayer(_layerPieces.Count);
            _layerPieces.Add(layer);
            if (layer.Count == 0) break;
        }
        packDepthMeters += n * _cachedLayerStep;
        _addCount += n;
    }

    void RemoveLayers(int n)
    {
        if (n <= 0) return;
        if (_cachedLayerStep <= 0f) CacheGridParams();
        n = Mathf.Min(n, _layerPieces.Count);
        int removed = 0;
        for (int i = 0; i < n; i++)
        {
            if (_layerPieces.Count == 0) break;
            var layer = _layerPieces[_layerPieces.Count - 1];
            _layerPieces.RemoveAt(_layerPieces.Count - 1);
            for (int j = 0; j < layer.Count; j++)
            {
                if (layer[j] != null)
                {
                    if (Application.isPlaying) Object.Destroy(layer[j].gameObject);
                    else Object.DestroyImmediate(layer[j].gameObject);
                }
            }
            removed++;
        }
        packDepthMeters = Mathf.Max(0f, packDepthMeters - removed * _cachedLayerStep);
        _removeCount += removed;
    }

    void LogSnowPackCall(string kind, string reason)
    {
        string scene = SceneManager.GetActiveScene().name;
        int children = _visualRoot != null ? _visualRoot.childCount : 0;
        float t = Application.isPlaying ? Time.time : 0f;
        int frame = Time.frameCount;
        float roofY = roofCollider != null ? roofCollider.transform.rotation.eulerAngles.y : 0f;
        float packY = _visualRoot != null ? _visualRoot.rotation.eulerAngles.y : 0f;
        string localText = UsingLocalPosition ? "true" : "false";
        Debug.Log($"[SnowPack] {kind} reason={reason} frame={frame} t={t:F2} scene={scene} children={children} depth={targetDepthMeters:F2} size={pieceSize:F2} local={localText} roofRotY={roofY:F1} packRotY={packY:F1}");
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        // A) Depth sync (ヒステリシス+クールダウン) - スライド中はスキップ
        if (!_inAvalancheSlide && Time.time >= _nextSyncCheckTime)
        {
            _nextSyncCheckTime = Time.time + Mathf.Max(0.05f, syncIntervalSeconds);
            if (roofSnowSystem == null) roofSnowSystem = FindFirstObjectByType<RoofSnowSystem>();
            if (roofSnowSystem != null)
            {
                float roofDepth = roofSnowSystem.roofSnowDepthMeters;
                float oldPack = packDepthMeters;
                float delta = roofDepth - oldPack;
                string hysteresis = $"(add={addThreshold:F2},remove={removeThreshold:F2})";

                if (Time.time >= _nextSyncAllowedAt)
                {
                    EnsureRoot();
                    if (roofCollider == null) roofCollider = ResolveRoofCollider();
                    if (_cachedLayerStep <= 0f) CacheGridParams();

                    string action = "NoOp";
                    if (delta >= addThreshold)
                    {
                        int layerDelta = Mathf.RoundToInt(delta / _cachedLayerStep);
                        layerDelta = Mathf.Clamp(layerDelta, 1, maxLayerStep);
                        AddLayers(layerDelta);
                        _nextSyncAllowedAt = Time.time + minSyncInterval;
                        action = $"AddLayers({layerDelta})";
                    }
                    else if (delta <= removeThreshold)
                    {
                        int layerDelta = Mathf.RoundToInt(-delta / _cachedLayerStep);
                        layerDelta = Mathf.Clamp(layerDelta, 1, maxLayerStep);
                        RemoveLayers(layerDelta);
                        _nextSyncAllowedAt = Time.time + minSyncInterval;
                        action = $"RemoveLayers({layerDelta})";
                    }

                    if (action != "NoOp")
                        Debug.Log($"[SnowPackSync] roofDepth={roofDepth:F3} packDepth={oldPack:F3} delta={delta:F3} action={action} hysteresis={hysteresis} minSyncInterval={minSyncInterval:F2} nextAllowedAt={_nextSyncAllowedAt:F2}");
                }
            }
        }

        if (_visualRoot == null) return;
        if (Time.time >= _nextAuditLogTime)
        {
            _nextAuditLogTime = Time.time + 1f;
            int children = _visualRoot.childCount;
            int activePieces = 0;
            var renderers = _visualRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null && renderers[i].enabled) activePieces++;
            Debug.Log($"[SnowPackAudit1s] frame={Time.frameCount} t={Time.time:F2} children={children} activePieces={activePieces} packDepthMeters={packDepthMeters:F3} rebuildCount={_rebuildCount} addCount={_addCount} removeCount={_removeCount}");
        }
        if (Time.time < _nextToggleLogTime) return;
        _nextToggleLogTime = Time.time + 1f;
        if (!_visualRoot.gameObject.activeInHierarchy)
            Debug.Log("[SnowPackToggleCheck] visualActive=false -> if ground rise stops now, issue is SnowPackVisual side");
    }

    static void ClearChildren(Transform root)
    {
        if (root == null) return;
        var toDelete = new List<GameObject>();
        for (int i = 0; i < root.childCount; i++)
            toDelete.Add(root.GetChild(i).gameObject);
        for (int i = 0; i < toDelete.Count; i++)
        {
            if (Application.isPlaying) Object.Destroy(toDelete[i]);
            else Object.DestroyImmediate(toDelete[i]);
        }
    }
}
