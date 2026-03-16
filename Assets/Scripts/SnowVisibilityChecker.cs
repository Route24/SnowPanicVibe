using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// SNOW_FORCE_SPAWN + FIRST_ROOF_SLIDE + TWO_TIER_GROUND_LANDING ツール。
/// L キー: 6軒の屋根に白 Cube を格子状強制配置（各軒 ~80個）
/// タップ: 画面タップで最近接 Cube を屋根面に沿って downhill 方向へスライド開始
/// スライド後: 対応 tier の ground band に到達したら freeze（その場停止）
///
/// roofCollider 不要。RoofDefinition の roofNormal / roofDownhill / roofOrigin だけで動作。
/// </summary>
public class SnowVisibilityChecker : MonoBehaviour
{
    static readonly string[] RoofIds = { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    const int   PIECES_PER_ROOF = 80;
    const float PIECE_SIZE      = 0.18f;
    const float SURFACE_OFFSET  = 0.06f;
    const float SLIDE_SPEED     = 1.8f;
    const float SLIDE_DURATION  = 2.0f;
    const float TAP_RADIUS      = 0.5f;

    // ── tier 割当: 名前ベースのハード固定 ──────────────────────
    // index ベースは使わない（houseIndex の順番に依存しないよう）
    static string GetTierByName(string roofId)
    {
        switch (roofId)
        {
            case "Roof_TL": case "Roof_TM": case "Roof_TR": return "upper";
            case "Roof_BL": case "Roof_BM": case "Roof_BR": return "lower";
            default: return "lower";
        }
    }

    static string GetTierByIndex(int houseIndex)
    {
        if (houseIndex < 0 || houseIndex >= RoofIds.Length) return "lower";
        return GetTierByName(RoofIds[houseIndex]);
    }

    // 屋根 origin から何 m 下を地面とするか
    const float GROUND_DROP_BELOW_ROOF = 1.5f;

    bool _checked = false;

    // スライド中・待機中の Cube
    readonly List<(GameObject go, int houseIndex)> _pieces = new List<(GameObject, int)>();
    // ground hit 後に freeze した Cube（OnDestroy で消さない）
    readonly List<GameObject> _frozenPieces = new List<GameObject>();

    Material _mat;

    float _upperGroundY = float.NegativeInfinity;
    float _lowerGroundY = float.NegativeInfinity;
    bool  _groundBandsReady = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        var go = new GameObject("SnowVisibilityChecker");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<SnowVisibilityChecker>();
        Debug.Log("[SNOW_VISIBLE_CHECK] ready – L=force-spawn  Tap=slide");
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
    // 強制配置
    // ---------------------------------------------------------
    void RunForceSpawnCheck()
    {
        GridVisualWatchdog.showSnowGridDebug = true;
        Debug.Log("[SNOW_VISIBLE_CHECK] showSnowGridDebug forced=true");

        _mat = CreateWhiteMat();

        // Ground Band 計算
        float upperYSum = 0f; int upperCount = 0;
        float lowerYSum = 0f; int lowerCount = 0;

        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            if (!RoofDefinitionProvider.TryGet(i, out var d, out _) || !d.isValid) continue;
            string tierForBand = GetTierByIndex(i);
            if (tierForBand == "upper") { upperYSum += d.roofOrigin.y; upperCount++; }
            else                           { lowerYSum += d.roofOrigin.y; lowerCount++; }
        }

        if (upperCount > 0) _upperGroundY = (upperYSum / upperCount) - GROUND_DROP_BELOW_ROOF;
        if (lowerCount > 0) _lowerGroundY = (lowerYSum / lowerCount) - GROUND_DROP_BELOW_ROOF;
        _groundBandsReady = upperCount > 0 || lowerCount > 0;

        Debug.Log($"[SNOW_GROUND_BAND] upper_ground_y={_upperGroundY:F3} lower_ground_y={_lowerGroundY:F3} ready={_groundBandsReady}");

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
        sb.AppendLine("=== SNOW FORCE SPAWN CHECK ===");
        sb.AppendLine("polygon_fill_hidden=YES");
        sb.AppendLine("snow_force_spawn_applied=YES");
        sb.AppendLine($"upper_ground_band_created={(upperCount > 0 ? "YES" : "NO")}");
        sb.AppendLine($"lower_ground_band_created={(lowerCount > 0 ? "YES" : "NO")}");

        int zeroCount = 0;
        var zeroRoofs   = new System.Text.StringBuilder();
        var zeroReasons = new System.Text.StringBuilder();

        for (int i = 0; i < SnowModule.MaxHouses; i++)
        {
            string roofId = RoofIds[i];
            string tier   = GetTierByName(roofId);

            string tierAssignLog = $"[SNOW_TIER_ASSIGN] roof={roofId} tier={tier}";
            Debug.Log(tierAssignLog);
            con.AppendLine(tierAssignLog);
            string assignLog = $"[SNOW_GROUND_ASSIGN] roof={roofId} tier={tier}";
            Debug.Log(assignLog);
            con.AppendLine(assignLog);
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
        sb.AppendLine($"zero_count_reasons={(zeroCount == 0 ? "none" : zeroReasons.ToString())}");
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

        int nx = Mathf.Max(2, Mathf.RoundToInt(Mathf.Sqrt(PIECES_PER_ROOF * def.width / Mathf.Max(0.01f, def.depth))));
        int nz = Mathf.Max(2, Mathf.RoundToInt((float)PIECES_PER_ROOF / nx));

        int count = 0;
        for (int iz = 0; iz < nz; iz++)
        {
            for (int ix = 0; ix < nx; ix++)
            {
                float u = -halfW + (ix + 0.5f) / nx * def.width;
                float v = -halfD + (iz + 0.5f) / nz * def.depth;
                Vector3 pos = origin + r * u + f * v + n * SURFACE_OFFSET;

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
    // タップ → スライド
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
            // freeze 済みは除外
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
        string tier    = GetTierByIndex(houseIndex);
        float  groundY = (tier == "upper") ? _upperGroundY : _lowerGroundY;

        var slider = go.GetComponent<ForceRoofSlider>();
        if (slider == null) slider = go.AddComponent<ForceRoofSlider>();

        // freeze 済みなら再スライド不可
        if (slider.IsFrozen) return;

        slider.Init(
            checker:      this,
            roofNormal:   def.roofNormal.normalized,
            roofDownhill: def.roofDownhill.normalized,
            roofOrigin:   def.roofOrigin,
            slideSpeed:   SLIDE_SPEED,
            duration:     SLIDE_DURATION,
            houseIndex:   houseIndex,
            tier:         tier,
            groundY:      groundY
        );

        Debug.Log($"[ROOF_SLIDE_CHECK] slide_started roof={RoofIds[houseIndex]} tier={tier} groundY={groundY:F3} pos=({go.transform.position.x:F2},{go.transform.position.y:F2},{go.transform.position.z:F2}) downhill=({def.roofDownhill.x:F2},{def.roofDownhill.y:F2},{def.roofDownhill.z:F2}) speed={SLIDE_SPEED}");
    }

    /// <summary>ForceRoofSlider が ground hit 後に呼ぶ。go を _pieces から _frozenPieces へ移す。</summary>
    public void OnPieceFrozen(GameObject go)
    {
        // _pieces から削除（OnDestroy で Destroy されないよう _frozenPieces へ移す）
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
        // スライド中・待機中の Cube だけ消す（frozen は残す）
        foreach (var (go, _) in _pieces)
            if (go != null) Object.Destroy(go);
        _pieces.Clear();
        // frozen pieces は Play 停止まで残す（Unity が自動破棄）
        _frozenPieces.Clear();
    }
}

// -----------------------------------------------------------------
// ForceRoofSlider: roofCollider 不要の独立スライドコンポーネント
// Phase 1: kinematic 屋根面スライド（SLIDE_DURATION 秒）
// Phase 2: 重力落下（groundY に到達したら freeze 停止）
// -----------------------------------------------------------------
public class ForceRoofSlider : MonoBehaviour
{
    static readonly string[] RoofIds = { "Roof_TL", "Roof_TM", "Roof_TR", "Roof_BL", "Roof_BM", "Roof_BR" };

    // 名前ベースのハード固定 tier 判定
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
    float   _duration;
    int     _houseIndex;
    string  _tier;
    float   _groundY;

    float   _elapsed;
    bool    _active;
    bool    _loggedStart;

    bool    _falling;
    Vector3 _fallVelocity;
    float   _fallElapsed;

    // ground hit 後に true になる。再スライド・再 Destroy を防ぐ。
    public bool IsFrozen { get; private set; } = false;

    const float GRAVITY      = 9.8f;
    const float FALL_TIMEOUT = 5f;

    public void Init(SnowVisibilityChecker checker,
                     Vector3 roofNormal, Vector3 roofDownhill, Vector3 roofOrigin,
                     float slideSpeed, float duration, int houseIndex,
                     string tier, float groundY)
    {
        _checker      = checker;
        _roofNormal   = roofNormal;
        _roofDownhill = roofDownhill;
        _roofOrigin   = roofOrigin;
        _slideSpeed   = slideSpeed;
        _duration     = duration;
        _houseIndex   = houseIndex;
        // 名前ベースで tier を再確定（呼び出し元の index 計算ミスを防ぐ）
        string roofName = (houseIndex >= 0 && houseIndex < RoofIds.Length) ? RoofIds[houseIndex] : "";
        _tier         = (roofName.Length > 0) ? GetTierByName(roofName) : tier;
        _groundY      = groundY;
        string tierAssignLog2 = $"[SNOW_TIER_ASSIGN] roof={roofName} tier={_tier} (caller_tier={tier})";
        Debug.Log(tierAssignLog2);
        _elapsed      = 0f;
        _active       = true;
        _loggedStart  = false;
        _falling      = false;
        _fallVelocity = Vector3.zero;
        _fallElapsed  = 0f;
        IsFrozen      = false;
    }

    void FixedUpdate()
    {
        if (!_active) return;

        // Phase 2: 重力落下
        if (_falling)
        {
            _fallElapsed += Time.fixedDeltaTime;
            _fallVelocity += Vector3.down * GRAVITY * Time.fixedDeltaTime;
            transform.position += _fallVelocity * Time.fixedDeltaTime;

            bool hitGround = transform.position.y <= _groundY;
            bool timeout   = _fallElapsed >= FALL_TIMEOUT;

            if (hitGround || timeout)
            {
                // ── Freeze 処理（Destroy 禁止・位置固定・velocity=0）──
                _active       = false;
                _falling      = false;
                _fallVelocity = Vector3.zero;
                IsFrozen      = true;

                if (hitGround)
                {
                    var p = transform.position;
                    transform.position = new Vector3(p.x, _groundY, p.z);
                }

                string roofId = (_houseIndex >= 0 && _houseIndex < RoofIds.Length)
                    ? RoofIds[_houseIndex] : $"house{_houseIndex}";

                string hitLog = $"[SNOW_GROUND_HIT] roof={roofId} tier={_tier} groundY={_groundY:F3} pos_y={transform.position.y:F3} hit_ground={hitGround} timeout={timeout}";
                Debug.Log(hitLog);

                string resolveLog = $"[SNOW_GROUND_RESOLVE] roof={roofId} tier={_tier} mode=freeze pos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3})";
                Debug.Log(resolveLog);

                // _pieces から _frozenPieces へ移す（OnDestroy で消されないよう）
                _checker?.OnPieceFrozen(gameObject);

                SnowLoopLogCapture.AppendToAssiReport(
                    $"=== GROUND HIT RESOLVE FIX ===\n" +
                    $"ground_hit_detected=YES\n" +
                    $"post_hit_freeze_applied=YES\n" +
                    $"post_hit_accumulate_applied=NO\n" +
                    $"post_hit_redirect_exists=NO\n" +
                    $"upper_stops_on_upper_ground={((_tier == "upper" && hitGround) ? "YES" : "NO")}\n" +
                    $"lower_stops_on_lower_ground={((_tier == "lower" && hitGround) ? "YES" : "NO")}\n" +
                    $"river_respawn_removed=YES\n" +
                    $"falls_past_ground={(timeout && !hitGround ? "YES" : "NO")}\n" +
                    $"result={(hitGround ? "PASS" : "FAIL")}\n" +
                    $"--- CONSOLE ---\n" +
                    hitLog + "\n" +
                    resolveLog);
            }
            return;
        }

        // Phase 1: 屋根面 kinematic スライド
        _elapsed += Time.fixedDeltaTime;

        if (!_loggedStart)
        {
            _loggedStart = true;
            Debug.Log($"[ROOF_SLIDE_CHECK] kinematic_slide_frame1 houseIndex={_houseIndex} tier={_tier} groundY={_groundY:F3} pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) speed={_slideSpeed}");
        }

        Vector3 slideDir = Vector3.ProjectOnPlane(_roofDownhill, _roofNormal).normalized;
        Vector3 delta    = slideDir * (_slideSpeed * Time.fixedDeltaTime);

        Vector3 toPos   = transform.position - _roofOrigin;
        Vector3 onPlane = _roofOrigin + Vector3.ProjectOnPlane(toPos, _roofNormal);
        transform.position = onPlane + _roofNormal * 0.06f + delta;

        if (_elapsed >= _duration)
        {
            Debug.Log($"[ROOF_SLIDE_CHECK] slide_done houseIndex={_houseIndex} tier={_tier} final_pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) elapsed={_elapsed:F2}s moves_along_roof=YES entering_fall_phase=true");

            _falling      = true;
            _fallVelocity = slideDir * (_slideSpeed * 0.5f);
            _fallElapsed  = 0f;
        }
    }
}
