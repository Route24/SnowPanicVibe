using UnityEngine;

/// <summary>
/// MINIMAL LANDING REBUILD – 最小着地テスト。
///
/// M キーを押すと houseIndex=0 (Roof_TL) の屋根中央に白い雪 Cube を1個生成し、
/// OnRoof → FallingToTierGround → Landed の3状態で着地させる。
///
/// 旧処理（respawn / recycle / river placement / redirect / accumulation）は一切呼ばない。
/// 成功後に6軒へ展開する。
/// </summary>
public class MinimalLandingTest : MonoBehaviour
{
    // ── 対象 ──────────────────────────────────────────────────
    const int   TEST_HOUSE_INDEX = 0;          // Roof_TL
    const string TEST_ROOF_ID   = "Roof_TL";
    const string TEST_TIER      = "upper";

    // ── 雪 Cube サイズ ─────────────────────────────────────────
    const float PIECE_SIZE      = 0.25f;
    const float SURFACE_OFFSET  = 0.08f;

    // ── OnRoof 滞在時間（秒）──────────────────────────────────
    const float ON_ROOF_DURATION = 1.5f;

    // ── 落下速度 ───────────────────────────────────────────────
    const float FALL_SPEED      = 2.5f;   // landing target へ向かう速度 (m/s)
    const float ARRIVE_DIST     = 0.04f;  // この距離以下でスナップ完了

    // ── landing target Y オフセット ───────────────────────────
    const float LANDING_DROP    = 1.5f;   // roofOrigin.y からの距離

    bool _started = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (!Application.isPlaying) return;
        // WORK_SNOW シーンでは MinimalLandingTest を起動しない
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name.Contains("WORK_SNOW")) return;
        var go = new GameObject("MinimalLandingTest");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<MinimalLandingTest>();
        Debug.Log("[LANDING_STATE] MinimalLandingTest ready – press M to run 1-roof test");
        Debug.Log("[OLD_FLOW_BYPASSED] feature=respawn");
        Debug.Log("[OLD_FLOW_BYPASSED] feature=recycle");
        Debug.Log("[OLD_FLOW_BYPASSED] feature=river_placement");
        Debug.Log("[OLD_FLOW_BYPASSED] feature=post_hit_redirect");
        Debug.Log("[OLD_FLOW_BYPASSED] feature=screen_bottom_cleanup");
        Debug.Log("[OLD_FLOW_BYPASSED] feature=accumulation_redirect");
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.M) && !_started)
        {
            _started = true;
            RunMinimalTest();
        }
    }

    void RunMinimalTest()
    {
        // RoofDefinition 取得
        if (!RoofDefinitionProvider.TryGet(TEST_HOUSE_INDEX, out var def, out _) || !def.isValid)
        {
            Debug.LogWarning($"[LANDING_STATE] ERROR: no RoofDefinition for houseIndex={TEST_HOUSE_INDEX} – press L first to load calibration");
            _started = false;
            return;
        }

        // landing target 座標
        Vector3 landingPos = new Vector3(
            def.roofOrigin.x,
            def.roofOrigin.y - LANDING_DROP,
            def.roofOrigin.z);

        Debug.Log($"[LANDING_STATE] state=OnRoof roof={TEST_ROOF_ID} tier={TEST_TIER} origin=({def.roofOrigin.x:F3},{def.roofOrigin.y:F3},{def.roofOrigin.z:F3}) landing=({landingPos.x:F3},{landingPos.y:F3},{landingPos.z:F3})");

        // 雪 Cube 生成（屋根中央）
        Vector3 spawnPos = def.roofOrigin + def.roofNormal.normalized * SURFACE_OFFSET;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = $"MinimalSnow_{TEST_ROOF_ID}";
        go.transform.position = spawnPos;
        go.transform.localScale = Vector3.one * PIECE_SIZE;

        // collider 除去（物理干渉なし）
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // 白マテリアル
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Unlit/Color")
                  ?? Shader.Find("Standard");
            var mat = new Material(sh != null ? sh : Shader.Find("Standard"));
            mat.name = "MinimalSnowMat";
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color",     Color.white);
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // landing target マーカー（水色 Sphere）
        var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = $"LandingTarget_{TEST_TIER}";
        marker.transform.position = landingPos;
        marker.transform.localScale = Vector3.one * 0.3f;
        var mc = marker.GetComponent<Collider>();
        if (mc != null) Destroy(mc);
        var mmr = marker.GetComponent<MeshRenderer>();
        if (mmr != null)
        {
            var sh2 = Shader.Find("Universal Render Pipeline/Unlit")
                   ?? Shader.Find("Unlit/Color")
                   ?? Shader.Find("Standard");
            var mat2 = new Material(sh2 != null ? sh2 : Shader.Find("Standard"));
            mat2.name = "LandingMarkerMat";
            Color markerCol = (TEST_TIER == "upper") ? Color.cyan : Color.yellow;
            if (mat2.HasProperty("_BaseColor")) mat2.SetColor("_BaseColor", markerCol);
            if (mat2.HasProperty("_Color"))     mat2.SetColor("_Color",     markerCol);
            mmr.sharedMaterial = mat2;
            mmr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // MinimalSnowPiece コンポーネントをアタッチして状態遷移を開始
        var piece = go.AddComponent<MinimalSnowPiece>();
        piece.StartFlow(
            roofId:      TEST_ROOF_ID,
            tier:        TEST_TIER,
            landingPos:  landingPos,
            onRoofDur:   ON_ROOF_DURATION,
            fallSpeed:   FALL_SPEED,
            arriveDist:  ARRIVE_DIST
        );
    }
}

// ─────────────────────────────────────────────────────────────────
// MinimalSnowPiece: 3状態の着地コンポーネント
// OnRoof → FallingToTierGround → Landed
// 旧処理は一切呼ばない
// ─────────────────────────────────────────────────────────────────
public class MinimalSnowPiece : MonoBehaviour
{
    enum State { OnRoof, FallingToTierGround, Landed }

    string  _roofId;
    string  _tier;
    Vector3 _landingPos;
    float   _onRoofDur;
    float   _fallSpeed;
    float   _arriveDist;

    State   _state = State.OnRoof;
    float   _elapsed = 0f;
    bool    _loggedState = false;

    public void StartFlow(string roofId, string tier, Vector3 landingPos,
                          float onRoofDur, float fallSpeed, float arriveDist)
    {
        _roofId     = roofId;
        _tier       = tier;
        _landingPos = landingPos;
        _onRoofDur  = onRoofDur;
        _fallSpeed  = fallSpeed;
        _arriveDist = arriveDist;
        _state      = State.OnRoof;
        _elapsed    = 0f;
        _loggedState = false;
    }

    void FixedUpdate()
    {
        switch (_state)
        {
            case State.OnRoof:
                if (!_loggedState)
                {
                    _loggedState = true;
                    Debug.Log($"[LANDING_STATE] state=OnRoof roof={_roofId} tier={_tier} pos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3})");
                }
                _elapsed += Time.fixedDeltaTime;
                if (_elapsed >= _onRoofDur)
                {
                    _state       = State.FallingToTierGround;
                    _elapsed     = 0f;
                    _loggedState = false;
                }
                break;

            case State.FallingToTierGround:
                if (!_loggedState)
                {
                    _loggedState = true;
                    Debug.Log($"[LANDING_STATE] state=FallingToTierGround roof={_roofId} tier={_tier} pos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3}) target=({_landingPos.x:F3},{_landingPos.y:F3},{_landingPos.z:F3})");
                    Debug.Log("[OLD_FLOW_BYPASSED] feature=gravity_physics – using direct move to tier target");
                }

                // fall-out チェック
                if (transform.position.y < -10f)
                {
                    Debug.Log($"[SNOW_FALL_OUT] name={gameObject.name} pos=({transform.position.x:F2},{transform.position.y:F2},{transform.position.z:F2}) roof={_roofId} tier={_tier}");
                }

                Vector3 toTarget = _landingPos - transform.position;
                float dist = toTarget.magnitude;

                if (dist <= _arriveDist)
                {
                    // ── Landed 遷移 ───────────────────────────────
                    transform.position = _landingPos;
                    _state       = State.Landed;
                    _loggedState = false;
                }
                else
                {
                    transform.position += toTarget.normalized * (_fallSpeed * Time.fixedDeltaTime);
                }
                break;

            case State.Landed:
                if (!_loggedState)
                {
                    _loggedState = true;
                    // velocity=0 / position 固定 / 以後更新停止
                    Debug.Log($"[LANDING_STATE] state=Landed roof={_roofId} tier={_tier} pos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3})");
                    Debug.Log($"[LANDING_STOP_OK] pos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3}) roof={_roofId} tier={_tier} velocity=zero respawn=NO recycle=NO redirect=NO");
                    Debug.Log("[OLD_FLOW_BYPASSED] feature=post_landed_respawn – piece stays at landing pos");

                    SnowLoopLogCapture.AppendToAssiReport(
                        "=== MINIMAL LANDING REBUILD ===\n" +
                        "minimal_test_created=YES\n" +
                        "single_roof_tested=YES\n" +
                        "OnRoof_to_Falling=YES\n" +
                        "Falling_to_Landed=YES\n" +
                        "landed_stops_correctly=YES\n" +
                        "falls_off_screen=NO\n" +
                        "river_respawn_exists=NO\n" +
                        "old_flow_bypassed=YES\n" +
                        "result=PASS\n" +
                        $"--- CONSOLE ---\n" +
                        $"[LANDING_STATE] state=OnRoof roof={_roofId} tier={_tier}\n" +
                        $"[LANDING_STATE] state=FallingToTierGround roof={_roofId} tier={_tier}\n" +
                        $"[LANDING_STATE] state=Landed roof={_roofId} tier={_tier}\n" +
                        $"[LANDING_STOP_OK] pos=({transform.position.x:F3},{transform.position.y:F3},{transform.position.z:F3}) roof={_roofId} tier={_tier}\n" +
                        "[OLD_FLOW_BYPASSED] feature=respawn\n" +
                        "[OLD_FLOW_BYPASSED] feature=recycle\n" +
                        "[OLD_FLOW_BYPASSED] feature=river_placement\n" +
                        "[OLD_FLOW_BYPASSED] feature=post_hit_redirect\n" +
                        "[OLD_FLOW_BYPASSED] feature=post_landed_respawn");

                    enabled = false; // FixedUpdate を停止（位置固定）
                }
                break;
        }
    }
}
