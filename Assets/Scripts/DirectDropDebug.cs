using UnityEngine;

/// <summary>
/// KEY DRIVEN DIRECT DROP TEST
/// U キー: upper tier debug snow を1個生成 → upper landing target へ直線移動 → 停止 → GroundPipeFix 呼出
/// J キー: lower tier debug snow を1個生成 → lower landing target へ直線移動 → 停止 → GroundPipeFix 呼出
/// L キー後に U/J で即テスト可能。既存フローは一切通さない。
/// </summary>
[DefaultExecutionOrder(-31998)]
public class DirectDropDebug : MonoBehaviour
{
    const float PIECE_SIZE   = 0.28f;
    const float MOVE_SPEED   = 3.0f;
    const float ARRIVE_DIST  = 0.05f;
    const float LANDING_DROP = 1.5f;

    static readonly string[] RoofIds = { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    static string GetTier(string roofId)
    {
        switch (roofId)
        {
            case "Roof_TL": case "Roof_TM": case "Roof_TR": return "upper";
            default: return "lower";
        }
    }

    Vector3    _upperLandingPos;
    Vector3    _lowerLandingPos;
    bool       _targetsReady = false;
    GameObject _upperMarker;
    GameObject _lowerMarker;
    Material   _whiteMat;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        // WORK_SNOW シーンでは DirectDropDebug を起動しない
        // （3D Cube/Sphere マーカーが画面中央に白いブロックとして表示される原因）
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (scene.Contains("WORK_SNOW")) return;
        var go = new GameObject("DirectDropDebug");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<DirectDropDebug>();
        Debug.Log("[DIRECT_DROP_BYPASS] old_flow_skipped=true mode=KEY_DRIVEN_DIRECT_DROP active");
        Debug.Log("[DIRECT_DROP_BYPASS] keys: L=calibration_load  U=upper_drop  J=lower_drop");
    }

    void Awake()
    {
        _whiteMat = MakeWhiteMat();
    }

    void Update()
    {
        if (!_targetsReady)
            TryBuildTargets();

        if (Input.GetKeyDown(KeyCode.U))
        {
            if (!_targetsReady) { Debug.LogWarning("[DIRECT_DROP_BYPASS] U pressed but targets not ready – press L first"); return; }
            SpawnAndDrop("upper", _upperLandingPos);
        }

        if (Input.GetKeyDown(KeyCode.J))
        {
            if (!_targetsReady) { Debug.LogWarning("[DIRECT_DROP_BYPASS] J pressed but targets not ready – press L first"); return; }
            SpawnAndDrop("lower", _lowerLandingPos);
        }
    }

    void TryBuildTargets()
    {
        float upperYSum = 0f, upperXSum = 0f, upperZSum = 0f; int upperCount = 0;
        float lowerYSum = 0f, lowerXSum = 0f, lowerZSum = 0f; int lowerCount = 0;

        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            if (!RoofDefinitionProvider.TryGet(i, out var d, out _) || !d.isValid) continue;
            string tier = GetTier(RoofIds[i]);
            if (tier == "upper") { upperYSum += d.roofOrigin.y; upperXSum += d.roofOrigin.x; upperZSum += d.roofOrigin.z; upperCount++; }
            else                 { lowerYSum += d.roofOrigin.y; lowerXSum += d.roofOrigin.x; lowerZSum += d.roofOrigin.z; lowerCount++; }
        }

        if (upperCount == 0 || lowerCount == 0) return;

        _upperLandingPos = new Vector3(upperXSum / upperCount, (upperYSum / upperCount) - LANDING_DROP, upperZSum / upperCount);
        _lowerLandingPos = new Vector3(lowerXSum / lowerCount, (lowerYSum / lowerCount) - LANDING_DROP, lowerZSum / lowerCount);
        _targetsReady = true;

        Debug.Log("[DIRECT_DROP_BYPASS] targets_ready upper=(" + _upperLandingPos.x.ToString("F2") + "," + _upperLandingPos.y.ToString("F2") + "," + _upperLandingPos.z.ToString("F2") + ") lower=(" + _lowerLandingPos.x.ToString("F2") + "," + _lowerLandingPos.y.ToString("F2") + "," + _lowerLandingPos.z.ToString("F2") + ")");

        if (_upperMarker == null) _upperMarker = MakeMarker("DirectDrop_UpperTarget", _upperLandingPos, Color.cyan);
        else _upperMarker.transform.position = _upperLandingPos;

        if (_lowerMarker == null) _lowerMarker = MakeMarker("DirectDrop_LowerTarget", _lowerLandingPos, Color.yellow);
        else _lowerMarker.transform.position = _lowerLandingPos;
    }

    void SpawnAndDrop(string tier, Vector3 landingPos)
    {
        Vector3 spawnPos = new Vector3(landingPos.x, landingPos.y + 2.5f, landingPos.z);
        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            if (!RoofDefinitionProvider.TryGet(i, out var d, out _) || !d.isValid) continue;
            if (GetTier(RoofIds[i]) == tier) { spawnPos = d.roofOrigin + d.roofNormal.normalized * 0.1f; break; }
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "DirectDropSnow_" + tier;
        go.transform.position = spawnPos;
        go.transform.localScale = Vector3.one * PIECE_SIZE;

        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) { mr.sharedMaterial = _whiteMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; }

        Debug.Log("[DIRECT_DROP_START] tier=" + tier + " spawn=(" + spawnPos.x.ToString("F2") + "," + spawnPos.y.ToString("F2") + "," + spawnPos.z.ToString("F2") + ") landing=(" + landingPos.x.ToString("F2") + "," + landingPos.y.ToString("F2") + "," + landingPos.z.ToString("F2") + ")");
        Debug.Log("[DIRECT_DROP_BYPASS] old_flow_skipped=true tier=" + tier);

        var mover = go.AddComponent<DirectDropMover>();
        mover.StartMove(tier, landingPos, MOVE_SPEED, ARRIVE_DIST);
    }

    static Material MakeWhiteMat()
    {
        var sh  = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
        var mat = new Material(sh != null ? sh : Shader.Find("Standard"));
        mat.name = "DirectDropWhite";
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     Color.white);
        return mat;
    }

    static GameObject MakeMarker(string name, Vector3 pos, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.35f;
        var c = go.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var sh  = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            var mat = new Material(sh != null ? sh : Shader.Find("Standard"));
            mat.name = "Marker_" + name;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     col);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return go;
    }

    void OnDestroy()
    {
        if (_upperMarker != null) Destroy(_upperMarker);
        if (_lowerMarker != null) Destroy(_lowerMarker);
    }
}

// -----------------------------------------------------------------
// DirectDropMover: 物理なし直線移動 → 到達でスナップ停止 → GroundPipeFix 呼出
// -----------------------------------------------------------------
public class DirectDropMover : MonoBehaviour
{
    string  _tier;
    Vector3 _target;
    float   _speed;
    float   _arriveDist;
    bool    _done;
    bool    _loggedMove;

    public void StartMove(string tier, Vector3 target, float speed, float arriveDist)
    {
        _tier       = tier;
        _target     = target;
        _speed      = speed;
        _arriveDist = arriveDist;
        _done       = false;
        _loggedMove = false;
    }

    void FixedUpdate()
    {
        if (_done) return;

        if (!_loggedMove)
        {
            _loggedMove = true;
            Debug.Log("[DIRECT_DROP_MOVE] tier=" + _tier + " pos=(" + transform.position.x.ToString("F2") + "," + transform.position.y.ToString("F2") + "," + transform.position.z.ToString("F2") + ") target=(" + _target.x.ToString("F2") + "," + _target.y.ToString("F2") + "," + _target.z.ToString("F2") + ")");
        }

        if (transform.position.y < -10f)
            Debug.Log("[SNOW_FALL_OUT] name=" + gameObject.name + " tier=" + _tier + " pos=(" + transform.position.x.ToString("F2") + "," + transform.position.y.ToString("F2") + "," + transform.position.z.ToString("F2") + ")");

        Vector3 toTarget = _target - transform.position;
        float dist = toTarget.magnitude;

        if (dist <= _arriveDist)
        {
            transform.position = _target;
            _done   = true;
            enabled = false;

            Debug.Log("[DIRECT_DROP_END] tier=" + _tier + " final=(" + transform.position.x.ToString("F3") + "," + transform.position.y.ToString("F3") + "," + transform.position.z.ToString("F3") + ") velocity=zero respawn=NO recycle=NO");

            // GroundPipeFix 経由で tier 別地面に pile を生成
            GroundPipeFix.OnSnapFreeze(_tier, transform.position);
        }
        else
        {
            transform.position += toTarget.normalized * (_speed * Time.fixedDeltaTime);
        }
    }
}
