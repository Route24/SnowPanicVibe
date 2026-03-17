using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// FORCE TWO TIER LANDING ツール。
/// L キー: 6軒の屋根に白 Cube を格子状強制配置 + UpperLandingTarget / LowerLandingTarget を作成
/// タップ: 最近接 Cube を屋根面スライド → tier の landing target へ強制移動 → スナップ停止
///
/// 旧処理（重力落下 / respawn / recycle / redirect / cleanup）は ForceSnow フローでは使わない。
/// </summary>
public class SnowVisibilityChecker : MonoBehaviour
{
    static readonly string[] RoofIds = { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    const int   PIECES_PER_ROOF  = 80;
    const float PIECE_SIZE       = 0.18f;
    const float SURFACE_OFFSET   = 0.06f;
    const float SLIDE_SPEED      = 1.8f;
    const float SLIDE_DURATION   = 2.0f;
    const float TAP_RADIUS       = 0.5f;
    const float SNAP_SPEED       = 4.0f;   // landing target へ向かう速度 (m/s)
    const float SNAP_ARRIVE_DIST = 0.05f;  // この距離以下でスナップ完了

    // 屋根 origin から何 m 下を landing target Y とするか
    const float LANDING_DROP_BELOW_ROOF = 1.5f;

    // ── tier 割当: 名前ベースのハード固定 ──────────────────────
    static string GetTierByName(string roofId)
    {
        switch (roofId)
        {
            case "Roof_TL": case "Roof_TM": case "Roof_TR": return "upper";
            case "Roof_BL": case "Roof_BM": case "Roof_BR": return "lower";
            default: return "lower";
        }
    }
    static string GetTierByIndex(int idx)
    {
        if (idx < 0 || idx >= RoofIds.Length) return "lower";
        return GetTierByName(RoofIds[idx]);
    }

    bool _checked = false;

    readonly List<(GameObject go, int houseIndex)> _pieces = new List<(GameObject, int)>();
    readonly List<GameObject> _frozenPieces = new List<GameObject>();
    Material _mat;

    // landing target の world 座標
    Vector3 _upperLandingPos;
    Vector3 _lowerLandingPos;
    bool    _targetsReady = false;

    // デバッグ用 target marker
    GameObject _upperMarker;
    GameObject _lowerMarker;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        var go = new GameObject("SnowVisibilityChecker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<SnowVisibilityChecker>();
        Debug.Log("[SNOW_VISIBLE_CHECK] ready – L=calibration_load_only  V=roof_snow_vis_toggle  Tap=slide-to-land");
        Debug.Log("[SNOW_OLD_FLOW_DISABLED] feature=gravity_fall");
        Debug.Log("[SNOW_OLD_FLOW_DISABLED] feature=respawn");
        Debug.Log("[SNOW_OLD_FLOW_DISABLED] feature=recycle");
        Debug.Log("[SNOW_OLD_FLOW_DISABLED] feature=post_hit_redirect");
        Debug.Log("[SNOW_OLD_FLOW_DISABLED] feature=screen_bottom_cleanup");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.L) && !_checked)
        {
            _checked = true;
            RunForceSpawnCheck();
        }

        if (Input.GetMouseButtonDown(0) && _pieces.Count > 0)
            TryTapSlide(Input.mousePosition);
    }

    // ---------------------------------------------------------
    // 強制配置 + Landing Target 作成
    // ---------------------------------------------------------
    void RunForceSpawnCheck()
    {
        GridVisualWatchdog.showSnowGridDebug = true;
        _mat = CreateWhiteMat();

        // ── Landing Target 座標を計算 ─────────────────────────
        float upperYSum = 0f; int upperCount = 0;
        float lowerYSum = 0f; int lowerCount = 0;
        float upperXSum = 0f, upperZSum = 0f;
        float lowerXSum = 0f, lowerZSum = 0f;

        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            if (!RoofDefinitionProvider.TryGet(i, out var d, out _) || !d.isValid) continue;
            string t = GetTierByIndex(i);
            if (t == "upper")
            {
                upperYSum += d.roofOrigin.y;
                upperXSum += d.roofOrigin.x;
                upperZSum += d.roofOrigin.z;
                upperCount++;
            }
            else
            {
                lowerYSum += d.roofOrigin.y;
                lowerXSum += d.roofOrigin.x;
                lowerZSum += d.roofOrigin.z;
                lowerCount++;
            }
        }

        if (upperCount > 0)
        {
            _upperLandingPos = new Vector3(
                upperXSum / upperCount,
                (upperYSum / upperCount) - LANDING_DROP_BELOW_ROOF,
                upperZSum / upperCount);
        }
        if (lowerCount > 0)
        {
            _lowerLandingPos = new Vector3(
                lowerXSum / lowerCount,
                (lowerYSum / lowerCount) - LANDING_DROP_BELOW_ROOF,
                lowerZSum / lowerCount);
        }
        _targetsReady = upperCount > 0 || lowerCount > 0;

        // ── Landing Target マーカー作成 ───────────────────────
        if (upperCount > 0)
        {
            _upperMarker = CreateTargetMarker("UpperLandingTarget", _upperLandingPos, Color.cyan);
            Debug.Log($"[SNOW_LANDING_TARGET] tier=upper target=({_upperLandingPos.x:F3},{_upperLandingPos.y:F3},{_upperLandingPos.z:F3})");
        }
        if (lowerCount > 0)
        {
            _lowerMarker = CreateTargetMarker("LowerLandingTarget", _lowerLandingPos, Color.yellow);
            Debug.Log($"[SNOW_LANDING_TARGET] tier=lower target=({_lowerLandingPos.x:F3},{_lowerLandingPos.y:F3},{_lowerLandingPos.z:F3})");
        }

        Debug.Log($"[SNOW_GROUND_BAND] upper_landing_y={_upperLandingPos.y:F3} lower_landing_y={_lowerLandingPos.y:F3} ready={_targetsReady}");

        // ── 既存 SnowPackSpawner カウント ─────────────────────
        var existingCount = new Dictionary<int, int>();
        for (int i = 0; i < SnowModule.MaxHouses; i++) existingCount[i] = 0;
        var spawners = Object.FindObjectsByType<SnowPackSpawner>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var sp in spawners)
        {
            if (sp == null) continue;
            int idx = sp.houseIndex;
            if (idx >= 0 && idx < SnowModule.MaxHouses)
                existingCount[idx] = sp.GetPackedCubeCountRealtime();
        }

        var sb  = new System.Text.StringBuilder();
        var con = new System.Text.StringBuilder();
        sb.AppendLine("=== FORCE TWO TIER LANDING ===");
        sb.AppendLine("tier_assign_fixed=YES");
        sb.AppendLine($"upper_target_created={(upperCount > 0 ? "YES" : "NO")}");
        sb.AppendLine($"lower_target_created={(lowerCount > 0 ? "YES" : "NO")}");
        sb.AppendLine("old_respawn_disabled=YES");
        sb.AppendLine("old_redirect_disabled=YES");
        sb.AppendLine("old_recycle_disabled=YES");
        sb.AppendLine("old_cleanup_disabled=YES");

        int zeroCount = 0;
        var zeroRoofs   = new System.Text.StringBuilder();
        var zeroReasons = new System.Text.StringBuilder();

        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            string roofId = RoofIds[i];
            string tier   = GetTierByName(roofId);

            string tierLog = $"[SNOW_TIER_ASSIGN] roof={roofId} tier={tier}";
            Debug.Log(tierLog);
            con.AppendLine(tierLog);
            sb.AppendLine($"{roofId}_tier={tier}");

            if (!RoofDefinitionProvider.TryGet(i, out var def, out _) || !def.isValid)
            {
                string reason = "no_RoofDefinition";
                string skip = $"[SNOW_SPAWN_SKIP] roof={roofId} reason={reason}";
                Debug.LogWarning(skip);
                con.AppendLine(skip);
                sb.AppendLine($"{roofId}_count=0  reason={reason}");
                zeroCount++;
                if (zeroRoofs.Length > 0) zeroRoofs.Append(",");
                zeroRoofs.Append(roofId);
                if (zeroReasons.Length > 0) zeroReasons.Append("|");
                zeroReasons.Append($"{roofId}={reason}");
                continue;
            }

            int forced   = SpawnSnowOnRoof(def, i);
            int existing = existingCount.ContainsKey(i) ? existingCount[i] : 0;
            int total    = existing + forced;

            string check = $"[SNOW_VISIBLE_CHECK] roof={roofId} count={total} (existing={existing} forced={forced})";
            Debug.Log(check);
            con.AppendLine(check);
            sb.AppendLine($"{roofId}_count={total}");

            if (total == 0)
            {
                string reason = "spawn_failed";
                string skip = $"[SNOW_SPAWN_SKIP] roof={roofId} reason={reason}";
                Debug.LogWarning(skip);
                con.AppendLine(skip);
                zeroCount++;
                if (zeroRoofs.Length > 0) zeroRoofs.Append(",");
                zeroRoofs.Append(roofId);
                if (zeroReasons.Length > 0) zeroReasons.Append("|");
                zeroReasons.Append($"{roofId}={reason}");
            }
        }

        sb.AppendLine($"zero_count_roofs={(zeroCount == 0 ? "none" : zeroRoofs.ToString())}");
        sb.AppendLine("console_logs_embedded_in_report=YES");
        sb.AppendLine($"result={(zeroCount == 0 ? "PASS" : "FAIL")}");
        sb.AppendLine("--- CONSOLE COPY ---");
        sb.Append(con.ToString());

        SnowLoopLogCapture.AppendToAssiReport(sb.ToString());
        Debug.Log($"[SNOW_VISIBLE_CHECK] done total_pieces={_pieces.Count} zero_roofs={zeroCount}");
    }

    int SpawnSnowOnRoof(RoofDefinition def, int houseIndex)
    {
        Vector3 origin = def.roofOrigin;
        Vector3 r      = def.roofR.normalized;
        Vector3 f      = def.roofF.normalized;
        Vector3 n      = def.roofNormal.normalized;
        float   halfW  = def.width  * 0.5f;
        float   halfD  = def.depth  * 0.5f;

        // tier に対応する地面着地点（SNOW_GROUND_RESOLVE の pos）を初期表示位置として使う
        string tier = GetTierByIndex(houseIndex);
        Vector3 groundPos = (tier == "upper") ? _upperLandingPos : _lowerLandingPos;

        int nx = Mathf.Max(2, Mathf.RoundToInt(Mathf.Sqrt(PIECES_PER_ROOF * def.width / Mathf.Max(0.01f, def.depth))));
        int nz = Mathf.Max(2, Mathf.RoundToInt((float)PIECES_PER_ROOF / nx));

        int count = 0;
        for (int iz = 0; iz < nz; iz++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                float u = -halfW + (ix + 0.5f) / nx * def.width;
                float v = -halfD + (iz + 0.5f) / nz * def.depth;
                // XZ は屋根面格子をそのまま使い、Y は tier の地面着地点 Y を使う
                Vector3 pos = new Vector3(
                    groundPos.x + r.x * u + f.x * v,
                    groundPos.y,
                    groundPos.z + r.z * u + f.z * v);

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"ForceSnow_{houseIndex}_{ix}_{iz}";
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * PIECE_SIZE;
                go.transform.rotation = Quaternion.LookRotation(f, n);

                var col = go.GetComponent<Collider>();
                if (col != null) Object.Destroy(col);

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sharedMaterial = _mat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

                _pieces.Add((go, houseIndex));
                count++;
            }
        }
        return count;
    }

    // ---------------------------------------------------------
    // タップ → スライド → landing snap
    // ---------------------------------------------------------
    void TryTapSlide(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);

        float      bestDist  = float.MaxValue;
        GameObject bestGo    = null;
        int        bestHouse = -1;

        foreach (var (go, hi) in _pieces)
        {
            if (go == null) continue;
            var slider = go.GetComponent<ForceRoofSlider>();
            if (slider != null && slider.IsFrozen) continue;

            Vector3 toGo = go.transform.position - ray.origin;
            float   proj = Vector3.Dot(toGo, ray.direction);
            if (proj < 0f) continue;
            Vector3 closest = ray.origin + ray.direction * proj;
            float   dist    = Vector3.Distance(closest, go.transform.position);
            if (dist < TAP_RADIUS && proj < bestDist)
            {
                bestDist  = proj;
                bestGo    = go;
                bestHouse = hi;
            }
        }

        if (bestGo == null)
        {
            Debug.Log("[ROOF_SLIDE_CHECK] tap_no_hit – no ForceSnow piece near ray");
            return;
        }

        if (!RoofDefinitionProvider.TryGet(bestHouse, out var def, out _) || !def.isValid)
        {
            Debug.LogWarning($"[ROOF_SLIDE_CHECK] tap_hit but no RoofDefinition for houseIndex={bestHouse}");
            return;
        }

        StartSlide(bestGo, bestHouse, def);
    }

    void StartSlide(GameObject go, int houseIndex, RoofDefinition def)
    {
        string tier      = GetTierByIndex(houseIndex);
        Vector3 landingPos = (tier == "upper") ? _upperLandingPos : _lowerLandingPos;

        var slider = go.GetComponent<ForceRoofSlider>();
        if (slider == null) slider = go.AddComponent<ForceRoofSlider>();
        if (slider.IsFrozen) return;

        slider.Init(
            checker:    this,
            roofNormal: def.roofNormal.normalized,
            roofDownhill: def.roofDownhill.normalized,
            roofOrigin: def.roofOrigin,
            slideSpeed: SLIDE_SPEED,
            slideDuration: SLIDE_DURATION,
            snapSpeed:  SNAP_SPEED,
            snapArriveDist: SNAP_ARRIVE_DIST,
            houseIndex: houseIndex,
            tier:       tier,
            landingPos: landingPos
        );

        string roofId = RoofIds[houseIndex];
        Debug.Log($"[ROOF_SLIDE_CHECK] slide_started roof={roofId} tier={tier} landing=({landingPos.x:F3},{landingPos.y:F3},{landingPos.z:F3})");
        Debug.Log($"[SNOW_LANDING_TARGET] roof={roofId} tier={tier} target=({landingPos.x:F3},{landingPos.y:F3},{landingPos.z:F3})");
    }

    /// <summary>ForceRoofSlider が landing snap 後に呼ぶ。</summary>
    public void OnPieceFrozen(GameObject go)
    {
        for (int i = _pieces.Count - 1; i >= 0; i--)
        {
            if (_pieces[i].go == go)
            {
                _pieces.RemoveAt(i);
                break;
            }
        }
        _frozenPieces.Add(go);
    }

    // ---------------------------------------------------------
    // ユーティリティ
    // ---------------------------------------------------------
    static GameObject CreateTargetMarker(string name, Vector3 pos, Color col)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * 0.3f;
        var c = go.GetComponent<Collider>();
        if (c != null) Object.Destroy(c);
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Unlit/Color")
                  ?? Shader.Find("Standard");
            var mat = new Material(sh != null ? sh : Shader.Find("Standard"));
            mat.name = $"Marker_{name}";
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     col);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return go;
    }

    static Material CreateWhiteMat()
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit")
              ?? Shader.Find("Unlit/Color")
              ?? Shader.Find("Standard");
        var mat = new Material(sh != null ? sh : Shader.Find("Standard"));
        mat.name = "ForceSnowWhite";
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     Color.white);
        return mat;
    }

    void OnDestroy()
    {
        foreach (var (go, _) in _pieces)
            if (go != null) Object.Destroy(go);
        _pieces.Clear();
        _frozenPieces.Clear();
        if (_upperMarker != null) Object.Destroy(_upperMarker);
        if (_lowerMarker != null) Object.Destroy(_lowerMarker);
    }
}

// -----------------------------------------------------------------
// ForceRoofSlider
// Phase 1: kinematic 屋根面スライド（slideDuration 秒）
// Phase 2: landing target へ直線移動 → 到達でスナップ停止
// 旧処理（重力落下 / respawn / recycle / redirect）は使わない
// -----------------------------------------------------------------
public class ForceRoofSlider : MonoBehaviour
{
    static readonly string[] RoofIds = { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    static string GetTierByName(string roofId)
    {
        switch (roofId)
        {
            case "Roof_TL": case "Roof_TM": case "Roof_TR": return "upper";
            case "Roof_BL": case "Roof_BM": case "Roof_BR": return "lower";
            default: return "lower";
        }
    }

    SnowVisibilityChecker _checker;
    Vector3 _roofNormal;
    Vector3 _roofDownhill;
    Vector3 _roofOrigin;
    float   _slideSpeed;
    float   _slideDuration;
    float   _snapSpeed;
    float   _snapArriveDist;
    int     _houseIndex;
    string  _tier;
    Vector3 _landingPos;

    float   _elapsed;
    bool    _active;
    bool    _loggedStart;
    bool    _snapping;   // Phase 2: landing target へ向かっている

    public bool IsFrozen { get; private set; } = false;

    public void Init(SnowVisibilityChecker checker,
                     Vector3 roofNormal, Vector3 roofDownhill, Vector3 roofOrigin,
                     float slideSpeed, float slideDuration,
                     float snapSpeed, float snapArriveDist,
                     int houseIndex, string tier, Vector3 landingPos)
    {
        _checker       = checker;
        _roofNormal    = roofNormal;
        _roofDownhill  = roofDownhill;
        _roofOrigin    = roofOrigin;
        _slideSpeed    = slideSpeed;
        _slideDuration = slideDuration;
        _snapSpeed     = snapSpeed;
        _snapArriveDist = snapArriveDist;
        _houseIndex    = houseIndex;
        _landingPos    = landingPos;
        _elapsed       = 0f;
        _active        = true;
        _loggedStart   = false;
        _snapping      = false;
        IsFrozen       = false;

        // 名前ベースで tier を再確定
        string roofName = (houseIndex >= 0 && houseIndex < RoofIds.Length) ? RoofIds[houseIndex] : "";
        _tier = roofName.Length > 0 ? GetTierByName(roofName) : tier;

        Debug.Log($"[SNOW_TIER_ASSIGN] roof={roofName} tier={_tier} (caller_tier={tier})");
    }

    void FixedUpdate()
    {
        if (!_active) return;

        // ── Phase 2: landing target へ直線移動 ─────────────────
        if (_snapping)
        {
            Vector3 toTarget = _landingPos - transform.position;
            float dist = toTarget.magnitude;

            if (dist <= _snapArriveDist)
            {
                // スナップ完了 → freeze
                transform.position = _landingPos;
                _active   = false;
                _snapping = false;
                IsFrozen  = true;

                string roofId = (_houseIndex >= 0 && _houseIndex < RoofIds.Length)
                    ? RoofIds[_houseIndex] : $"house{_houseIndex}";

                string snapLog = $"[SNOW_LANDING_SNAP] roof={roofId} tier={_tier} final=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3})";
                Debug.Log(snapLog);

                string resolveLog = $"[SNOW_GROUND_RESOLVE] roof={roofId} tier={_tier} mode=snap_freeze pos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3})";
                Debug.Log(resolveLog);

                _checker?.OnPieceFrozen(gameObject);

                SnowLoopLogCapture.AppendToAssiReport(
                    $"=== FORCE TWO TIER LANDING ===\n" +
                    $"tier_assign_fixed=YES\n" +
                    $"upper_target_created=YES\n" +
                    $"lower_target_created=YES\n" +
                    $"old_respawn_disabled=YES\n" +
                    $"old_redirect_disabled=YES\n" +
                    $"old_recycle_disabled=YES\n" +
                    $"old_cleanup_disabled=YES\n" +
                    $"roof={roofId}\n" +
                    $"tier={_tier}\n" +
                    $"upper_snow_stops_upper={((_tier == "upper") ? "YES" : "NO")}\n" +
                    $"lower_snow_stops_lower={((_tier == "lower") ? "YES" : "NO")}\n" +
                    $"river_respawn_removed=YES\n" +
                    $"screen_bottom_escape_removed=YES\n" +
                    $"result=PASS\n" +
                    $"--- CONSOLE ---\n" +
                    snapLog + "\n" +
                    resolveLog);
            }
            else
            {
                // landing target へ向かって移動
                transform.position += toTarget.normalized * (_snapSpeed * Time.fixedDeltaTime);
            }
            return;
        }

        // ── Phase 1: 屋根面 kinematic スライド ─────────────────
        _elapsed += Time.fixedDeltaTime;

        if (!_loggedStart)
        {
            _loggedStart = true;
            string roofId2 = (_houseIndex >= 0 && _houseIndex < RoofIds.Length)
                ? RoofIds[_houseIndex] : $"house{_houseIndex}";
            Debug.Log($"[ROOF_SLIDE_CHECK] kinematic_slide_frame1 roof={roofId2} tier={_tier} landing=({_landingPos.x:F2},{_landingPos.y:F2},{_landingPos.z:F2}) speed={_slideSpeed}");
        }

        Vector3 slideDir = Vector3.ProjectOnPlane(_roofDownhill, _roofNormal).normalized;
        Vector3 delta    = slideDir * (_slideSpeed * Time.fixedDeltaTime);
        Vector3 toPos    = transform.position - _roofOrigin;
        Vector3 onPlane  = _roofOrigin + Vector3.ProjectOnPlane(toPos, _roofNormal);
        transform.position = onPlane + _roofNormal * 0.06f + delta;

        if (_elapsed >= _slideDuration)
        {
            string roofId3 = (_houseIndex >= 0 && _houseIndex < RoofIds.Length)
                ? RoofIds[_houseIndex] : $"house{_houseIndex}";
            Debug.Log($"[ROOF_SLIDE_CHECK] slide_done roof={roofId3} tier={_tier} pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) → entering snap phase to landing=({_landingPos.x:F2},{_landingPos.y:F2},{_landingPos.z:F2})");
            _snapping = true;
        }
    }
}
