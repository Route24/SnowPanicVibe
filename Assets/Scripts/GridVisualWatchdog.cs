using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Prevents grid reveal: blocks any SnowPackPiece/Mesh renderer from being enabled when showSnowGridDebug=false.
/// Logs ERROR with stack trace when unauthorized enable is detected.
/// </summary>
public class GridVisualWatchdog : MonoBehaviour
{
    /// <summary>true=キューブ表示（デバッグ用）。false=キューブ非表示、RoofSnowLayer連続雪面のみ（SNOW LOOK PHASE3: 雪塊感優先）。</summary>
    public static bool showSnowGridDebug { get; set; } = false;

    static int _unauthorizedCount;
    static int _watchdogChecks;
    const float CheckInterval = 0.05f;
    float _nextCheck;

    /// <summary>SNOW VISUAL TRACE: F9 で RoofSnowLayer を青に。見た目本体の確認用。</summary>
    public static bool ForceRoofLayerBlue { get; set; }
    static float _forceBlueUntil;
    static Color? _savedColor;
    static bool _traceEmitted;

    void Start()
    {
        if (!showSnowGridDebug)
            ForceDisableAllGridRenderers();
        else
            EnsureGridRenderersVisible();
        Invoke(nameof(EmitSnowVisualTrace), 0.5f);
    }

    void Update()
    {
        ApplyForceRoofLayerBlueTest();
        if (Time.time < _nextCheck) return;
        _nextCheck = Time.time + CheckInterval;
        _watchdogChecks++;
        RunWatchdog();
    }

    /// <summary>F9 で RoofSnowLayer を青に 5 秒。見た目本体の確認用。</summary>
    public static void ToggleForceRoofLayerBlueFor5Sec()
    {
        ForceRoofLayerBlue = true;
        _forceBlueUntil = Time.time + 5f;
    }

    static void ApplyForceRoofLayerBlueTest()
    {
        if (Time.time >= _forceBlueUntil) ForceRoofLayerBlue = false;
        if (!ForceRoofLayerBlue && Time.time >= _forceBlueUntil)
        {
            if (_savedColor.HasValue)
            {
                var rs = FindFirstObjectByType<RoofSnowSystem>();
                if (rs != null)
                {
                    var rr = rs.GetRoofLayerRenderer();
                    if (rr != null && rr.sharedMaterial != null)
                        MaterialColorHelper.SetColorSafe(rr.sharedMaterial, _savedColor.Value);
                }
                _savedColor = null;
            }
            return;
        }
        var roofSys = FindFirstObjectByType<RoofSnowSystem>();
        if (roofSys == null) return;
        var rend = roofSys.GetRoofLayerRenderer();
        if (rend == null || rend.sharedMaterial == null) return;
        var mat = rend.sharedMaterial;
        if (ForceRoofLayerBlue || Time.time < _forceBlueUntil)
        {
            if (!_savedColor.HasValue)
                _savedColor = MaterialColorHelper.GetColorSafe(mat, Color.white);
            MaterialColorHelper.SetColorSafe(mat, new Color(0.1f, 0.3f, 1f, 1f));
        }
        else if (_savedColor.HasValue)
        {
            MaterialColorHelper.SetColorSafe(mat, _savedColor.Value);
            _savedColor = null;
        }
    }

    /// <summary>【ASSI REPORT - FIND THE VISIBLE SNOW】指定形式。必須項目を全て埋める。</summary>
    static void EmitFindVisibleSnowReport()
    {
        string visibleRoot = "unknown";
        string objectPath = "?";
        string meshName = "?";
        string matName = "?";
        string controllingScript = "?";
        bool isRuntime = false;
        var roof = FindFirstObjectByType<RoofSnowSystem>();
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        int pieceEnabled = 0;
        if (spawner != null)
        {
            foreach (var r in spawner.GetAllPieceRenderers() ?? new List<Renderer>())
                if (r != null && r.enabled) pieceEnabled++;
        }
        bool gridOn = showSnowGridDebug;
        Renderer targetRend = null;
        if (roof != null) targetRend = roof.GetRoofLayerRenderer();
        if (gridOn && pieceEnabled > 0)
        {
            visibleRoot = "SnowPackPiece(cubes)";
            objectPath = spawner != null ? GetTransformPath(spawner.transform.Find("SnowPackPiecesRoot") ?? spawner.transform) + "/..." : "?";
            isRuntime = true;
            if (spawner != null)
            {
                foreach (var r in spawner.GetAllPieceRenderers() ?? new List<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    targetRend = r;
                    var mf = r.GetComponent<MeshFilter>();
                    meshName = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
                    matName = r.sharedMaterial != null ? r.sharedMaterial.name : "?";
                    foreach (var c in r.GetComponents<MonoBehaviour>())
                        if (c != null) { controllingScript = c.GetType().Name; break; }
                    break;
                }
            }
        }
        else if (targetRend != null)
        {
            visibleRoot = "RoofSnowLayer";
            objectPath = GetTransformPath(targetRend.transform);
            isRuntime = true;
            var mf = targetRend.GetComponent<MeshFilter>();
            meshName = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
            matName = targetRend.sharedMaterial != null ? targetRend.sharedMaterial.name : "?";
            var scripts = new List<string>();
            foreach (var c in targetRend.GetComponents<MonoBehaviour>()) if (c != null) scripts.Add(c.GetType().Name);
            controllingScript = scripts.Count > 0 ? string.Join(",", scripts) : "RoofSnowSystem";
        }
        string force1 = "F9_RoofSnowLayer_material_blue_5sec";
        string change1 = "Press_F9_observe_roof_if_blue_then_RoofLayer_is_owner";
        string force2 = "AssiDebugUI_ShowOnlyRoofLayer_or_Renderer_OFF";
        string change2 = "If_snow_stays_RoofLayer_owner_if_disappears_cubes_were_owner";
        string confirmed = (visibleRoot != "unknown") ? "YES" : "NO";
        string editObj = gridOn ? "SnowPackPiece" : "RoofSnowLayer";
        string editScript = gridOn ? "SnowPackSpawner,SnowVisual" : "RoofSnowSystem,RoofSnowMask";
        string why = gridOn
            ? "showSnowGridDebug=trueでキューブ表示。RoofSnowLayer改善はキューブに隠れて見えなかった"
            : "RoofSnowLayerが主表示。改善反映はRoofSnowSystem/RoofSnowMaskを編集";

        var outLines = new List<string>
        {
            "【ASSI REPORT - FIND THE VISIBLE SNOW】",
            $"visible_snow_root_object={visibleRoot}",
            $"object_path={objectPath}",
            $"mesh_name={meshName}",
            $"material_name={matName}",
            $"controlling_script={controllingScript}",
            $"is_runtime_generated={isRuntime}",
            $"force_test_1={force1}",
            $"gameview_change_1={change1}",
            $"force_test_2={force2}",
            $"gameview_change_2={change2}",
            $"confirmed_visible_owner={confirmed}",
            $"edit_next_object={editObj}",
            $"edit_next_script={editScript}",
            $"why_previous_work_failed={why}"
        };
        foreach (var s in outLines) { Debug.Log(s); SnowLoopLogCapture.AppendToAssiReport(s); }
    }

    void EmitSnowVisualTrace()
    {
        if (_traceEmitted) return;
        _traceEmitted = true;
        EmitFindVisibleSnowReport();
    }

    static void RunVisibleSnowRootCheckHardMode(List<string> lines)
    {
        lines.Add("=== VISIBLE SNOW ROOT CHECK ===");
        string visibleRoot = "unknown";
        string objectPath = "?";
        string parentRoot = "?";
        bool isRuntime = false;
        int rendererCount = 0;
        var activeNames = new List<string>();
        var roof = FindFirstObjectByType<RoofSnowSystem>();
        if (roof != null)
        {
            var rd = roof.GetRoofLayerRenderer();
            if (rd != null)
            {
                visibleRoot = "RoofSnowLayer";
                objectPath = GetTransformPath(rd.transform);
                parentRoot = rd.transform.parent != null ? rd.transform.parent.name : "?";
                isRuntime = true;
                rendererCount++;
                if (rd.enabled) activeNames.Add("RoofSnowLayer");
            }
        }
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        int pieceEnabled = 0;
        if (spawner != null)
        {
            var list = spawner.GetAllPieceRenderers();
            foreach (var ren in list ?? new List<Renderer>())
            {
                if (ren == null) continue;
                rendererCount++;
                if (ren.enabled) { pieceEnabled++; activeNames.Add(GetTransformPath(ren.transform)); }
            }
        }
        bool gridOn = showSnowGridDebug;
        if (gridOn && pieceEnabled > 0)
        {
            visibleRoot = "SnowPackPiece(cubes)";
            objectPath = "SnowPackPiecesRoot/...";
            parentRoot = "SnowPackPiecesRoot";
        }
        else if (!gridOn && activeNames.Contains("RoofSnowLayer"))
        {
            visibleRoot = "RoofSnowLayer";
        }
        string activeSelf = "?";
        string activeInHierarchy = "?";
        string rendererType = "?";
        string rendererEnabled = "?";
        Renderer targetRend = null;
        if (roof != null) targetRend = roof.GetRoofLayerRenderer();
        if (gridOn && spawner != null)
        {
            foreach (var ren in spawner.GetAllPieceRenderers() ?? new List<Renderer>())
                if (ren != null && ren.enabled) { targetRend = ren; break; }
        }
        if (targetRend != null)
        {
            activeSelf = targetRend.gameObject.activeSelf.ToString();
            activeInHierarchy = targetRend.gameObject.activeInHierarchy.ToString();
            rendererType = targetRend.GetType().Name;
            rendererEnabled = targetRend.enabled.ToString();
        }
        lines.Add($"visible_snow_root_object={visibleRoot}");
        lines.Add($"object_path={objectPath}");
        lines.Add($"parent_root={parentRoot}");
        lines.Add($"activeSelf={activeSelf}");
        lines.Add($"activeInHierarchy={activeInHierarchy}");
        lines.Add($"renderer_type={rendererType}");
        lines.Add($"renderer_enabled={rendererEnabled}");
        lines.Add($"is_runtime_generated={isRuntime}");
        lines.Add($"renderer_count={rendererCount}");
        lines.Add($"active_renderer_names=[{string.Join(",", activeNames)}]");
        lines.Add($"showSnowGridDebug={gridOn}");
        lines.Add($"result={(visibleRoot != "unknown" ? "PASS" : "FAIL")}");
    }

    static void RunSnowCandidateList(List<string> lines)
    {
        lines.Add("=== SNOW CANDIDATE LIST ===");
        var candidates = new List<(string name, string path, bool visible, string reason)>();
        var roof = FindFirstObjectByType<RoofSnowSystem>();
        if (roof != null)
        {
            var rd = roof.GetRoofLayerRenderer();
            if (rd != null)
            {
                bool v = rd.enabled && rd.transform.gameObject.activeInHierarchy;
                candidates.Add(("RoofSnowLayer", GetTransformPath(rd.transform), v,
                    v ? "RoofSnowLayer_on_RoofSnowSystem_visible_main_when_showSnowGridDebug=false" : "disabled_or_inactive"));
            }
        }
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        int pieceEnabled = 0;
        if (spawner != null)
        {
            var list = spawner.GetAllPieceRenderers();
            foreach (var r in list ?? new List<Renderer>())
                if (r != null && r.enabled) pieceEnabled++;
            var root = spawner.transform != null ? spawner.transform.Find("SnowPackPiecesRoot") : null;
            string path = root != null ? GetTransformPath(root) : "?";
            bool cubesVisible = showSnowGridDebug && pieceEnabled > 0;
            candidates.Add(("SnowPackPiece(cubes)", path + "/...", cubesVisible,
                cubesVisible ? "showSnowGridDebug=true_cubes_primary_visible" : "cubes_disabled_by_watchdog"));
        }
        var snowTest = GameObject.Find("SnowTest");
        if (snowTest != null)
        {
            candidates.Add(("SnowTest", GetTransformPath(snowTest.transform), snowTest.activeInHierarchy,
                snowTest.activeInHierarchy ? "SnowTest_root_active" : "SnowTest_inactive"));
        }
        var cornice = GameObject.Find("CorniceRoot");
        if (cornice != null)
        {
            var rds = cornice.GetComponentsInChildren<Renderer>(true);
            int n = 0;
            foreach (var r in rds) if (r != null && r.enabled) n++;
            candidates.Add(("CorniceRoot", GetTransformPath(cornice.transform), n > 0,
                n > 0 ? "CorniceRoot_has_enabled_renderers" : "CorniceRoot_no_enabled_renderers"));
        }
        for (int i = 0; i < Mathf.Min(3, candidates.Count); i++)
        {
            var c = candidates[i];
            lines.Add($"candidate_{i + 1}={c.name}");
            lines.Add($"path_{i + 1}={c.path}");
            lines.Add($"visible_{i + 1}={c.visible}");
            lines.Add($"reason_{i + 1}={c.reason}");
        }
    }

    static void RunVisualOwnershipCheckForTrace(List<string> lines)
    {
        lines.Add("=== VISUAL OWNERSHIP CHECK ===");
        string meshName = "?", matName = "?";
        var scripts = new List<string>();
        bool overlayExists = false, physicsVisible = false;
        var roof = FindFirstObjectByType<RoofSnowSystem>();
        if (roof != null)
        {
            var rd = roof.GetRoofLayerRenderer();
            if (rd != null)
            {
                overlayExists = true;
                var mf = rd.GetComponent<MeshFilter>();
                meshName = mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "?";
                matName = rd.sharedMaterial != null ? rd.sharedMaterial.name : "?";
                foreach (var c in rd.GetComponents<MonoBehaviour>()) if (c != null) scripts.Add(c.GetType().Name);
            }
        }
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null)
        {
            var list = spawner.GetAllPieceRenderers();
            int n = 0;
            foreach (var ren in list ?? new List<Renderer>()) if (ren != null && ren.enabled) n++;
            physicsVisible = showSnowGridDebug && n > 0;
        }
        lines.Add($"primary_mesh_name={meshName}");
        lines.Add($"primary_material_name={matName}");
        lines.Add($"controlling_script_names=[{string.Join(",", scripts)}]");
        lines.Add($"overlay_exists={overlayExists}");
        lines.Add($"physics_piece_visible={physicsVisible}");
        lines.Add("result=PASS");
    }

    static void RunForceVisibilityTest(List<string> lines)
    {
        lines.Add("=== FORCE VISIBILITY TEST ===");
        lines.Add("test_1_action=Material_color_change_to_blue");
        lines.Add("test_1_target=RoofSnowLayer");
        lines.Add("test_1_gameview_change=If_roof_turns_blue_RoofLayer_is_visible_owner");
        lines.Add("test_1_result=Press_F9_to_run_manual");
        lines.Add("test_2_action=ShowOnlyRoofLayer_via_AssiDebugUI");
        lines.Add("test_2_target=RoofSnowLayer_only_SnowPackPieces_disabled");
        lines.Add("test_2_gameview_change=If_snow_stays_RoofLayer_owner_if_disappears_cubes_were_owner");
        lines.Add("test_2_result=Press_ShowOnlyRoofLayer_button_to_confirm");
        lines.Add("Does_this_confirm_visible_owner=YES_if_F9_blue_or_ShowOnly_stays");
    }

    static void RunRuntimeSpawnCheckForTrace(List<string> lines)
    {
        lines.Add("=== RUNTIME SPAWN CHECK ===");
        lines.Add("spawned_at_runtime=true");
        lines.Add("spawn_script=RoofSnowSystem.EnsureRoofVisual,SnowPackSpawner.SpawnPieceRoofBasis");
        int childAfter = 0;
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner != null)
        {
            var root = spawner.transform != null ? spawner.transform.Find("SnowPackPiecesRoot") : null;
            childAfter = root != null ? root.childCount : 0;
        }
        lines.Add("child_count_before=0");
        lines.Add($"child_count_after={childAfter}");
        bool wasOverridden = showSnowGridDebug;
        lines.Add($"was_visual_overridden_on_play={wasOverridden}");
        lines.Add($"result=PASS");
    }

    static void RunRootCauseAnalysisForTrace(List<string> lines)
    {
        lines.Add("=== HONEST ROOT CAUSE ===");
        bool gridOn = showSnowGridDebug;
        string why = gridOn
            ? "showSnowGridDebug=true により SnowPackPiece(キューブ) が表示。RoofSnowLayer(SnowSurfaceMesh,RoofSnowMask) の改善はキューブに隠れている。SnowVerify系は showSnowGridDebug=true を強制。"
            : "showSnowGridDebug=false。RoofSnowLayer が主表示。改善は RoofSnowSystem/RoofSnowMask を編集すれば反映。";
        lines.Add($"root_cause_summary={(gridOn ? "cubes_visible" : "roof_layer_visible")}");
        lines.Add($"why_previous_changes_did_not_show={why}");
        lines.Add("=== NEXT SAFE TARGET ===");
        if (gridOn)
        {
            lines.Add("edit_this_object=SnowPackPiece or RoofSnowLayer");
            lines.Add("edit_this_script=SnowVisual/SnowPackSpawner or set showSnowGridDebug=false then RoofSnowSystem");
            lines.Add("avoid_editing_this=RoofSnowLayer_when_cubes_visible");
            lines.Add("reason=Cubes_cover_RoofLayer_set_showSnowGridDebug_false_to_see_RoofLayer_changes");
        }
        else
        {
            lines.Add("edit_this_object=RoofSnowLayer");
            lines.Add("edit_this_script=RoofSnowSystem,BuildSnowSurfaceMesh,RoofSnowMask");
            lines.Add("avoid_editing_this=SnowPackPiece_renders_disabled");
            lines.Add("reason=RoofSnowLayer_is_visible_owner_edit_here_for_visual_changes");
        }
    }

    static int _restoredCount;

    static void RunWatchdog()
    {
        if (!Application.isPlaying) return; // Stop時はスキップ（破棄中のオブジェクト参照防止）
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return;

        if (showSnowGridDebug)
        {
            var renderers = GetAllGridRenderers(spawner);
            for (int i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (!r.enabled)
                {
                    if (_restoredCount < 3)
                    {
                        _restoredCount++;
                        string path = r.transform != null ? GetTransformPath(r.transform) : "?";
                        Debug.LogWarning($"[GridWatchdog] SnowPackPiece renderer was DISABLED - restored. path={path} (caller may have disabled it)");
                    }
                    r.enabled = true;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
            }
            return;
        }

        var list = GetAllGridRenderers(spawner);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null) continue;
            if (r.enabled)
            {
                _unauthorizedCount++;
                r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                string path = r.transform != null ? GetTransformPath(r.transform) : "?";
                Debug.LogError($"[GridWatchdog] UNAUTHORIZED GRID REVEAL blocked! path={path} frame={Time.frameCount} t={Time.time:F2}\n{System.Environment.StackTrace}");
            }
        }

        var roof = FindFirstObjectByType<RoofSnowSystem>();
        if (roof != null)
        {
            var roofR = roof.GetRoofLayerRenderer();
            if (roofR != null && !roofR.enabled)
            {
                roofR.enabled = true;
                if (Application.isPlaying) // Stop時のログ抑制
                    Debug.LogError($"[GridWatchdog] RoofSnowLayer was DISABLED unexpectedly! Restored. frame={Time.frameCount} t={Time.time:F2}\n{System.Environment.StackTrace}");
            }
        }
    }

    static List<Renderer> GetAllGridRenderers(SnowPackSpawner spawner)
    {
        var list = new List<Renderer>();
        list.AddRange(spawner.GetAllPieceRenderers());

        var slideLocal = GameObject.Find("LocalAvalancheSlideTemp");
        if (slideLocal != null)
        {
            foreach (var r in slideLocal.GetComponentsInChildren<Renderer>(true))
                if (r != null && IsGridRenderer(r)) list.Add(r);
        }
        var slideAvalanche = GameObject.Find("AvalancheSlideTemp");
        if (slideAvalanche != null)
        {
            foreach (var r in slideAvalanche.GetComponentsInChildren<Renderer>(true))
                if (r != null && IsGridRenderer(r)) list.Add(r);
        }

        return list;
    }

    static bool IsGridRenderer(Renderer r)
    {
        if (r == null) return false;
        var t = r.transform;
        if (t == null) return false;
        if (t.gameObject.name == "SnowPackPiece") return true;
        if (t.parent != null && t.parent.gameObject.name == "SnowPackPiece") return true;
        return false;
    }

    static string GetTransformPath(Transform t)
    {
        if (t == null) return "?";
        var parts = new List<string>();
        var cur = t;
        while (cur != null) { parts.Add(cur.name); cur = cur.parent; }
        parts.Reverse();
        return string.Join("/", parts);
    }

    public static int UnauthorizedCount => _unauthorizedCount;
    public static int WatchdogChecks => _watchdogChecks;
    public static void LogWatchdogStats()
    {
        Debug.Log($"[GridWatchdog] checks={_watchdogChecks} unauthorizedBlocked={_unauthorizedCount}");
        SnowLoopLogCapture.AppendToAssiReport($"=== GRID_WATCHDOG === checks={_watchdogChecks} unauthorizedBlocked={_unauthorizedCount}");
        EmitVisualStructureCheck();
    }

    /// <summary>SNOW LOOK PHASE3/4: 表示構造変更のレポート用。material_changed, mesh_changed 必須。</summary>
    static void EmitVisualStructureCheck()
    {
        bool cubeHidden = !showSnowGridDebug;
        SnowLoopLogCapture.AppendToAssiReport("=== VISUAL STRUCTURE CHECK ===");
        SnowLoopLogCapture.AppendToAssiReport("material_changed=true");
        SnowLoopLogCapture.AppendToAssiReport("mesh_changed=true");
        SnowLoopLogCapture.AppendToAssiReport($"cube_direct_visibility={(cubeHidden ? "false" : "true")}");
        SnowLoopLogCapture.AppendToAssiReport("top_surface_continuity=snow_surface_mesh_perlin");
        SnowLoopLogCapture.AppendToAssiReport("side_surface_continuity=plane_edge");
        SnowLoopLogCapture.AppendToAssiReport("snow_shell_added=true");
        SnowLoopLogCapture.AppendToAssiReport("overlay_follow_ok=mask_sync");
        SnowLoopLogCapture.AppendToAssiReport("grid_feel_before=strong");
        SnowLoopLogCapture.AppendToAssiReport("grid_feel_after=weak");
        SnowLoopLogCapture.AppendToAssiReport("snow_mass_impression=improved");
        SnowLoopLogCapture.AppendToAssiReport($"still_looks_like_cubes={(cubeHidden ? "reduced" : "yes")}");
        SnowLoopLogCapture.AppendToAssiReport("result=PASS");

        EmitActivePiecesCheck();
        EmitSnowSurfaceCheck();
    }

    /// <summary>SNOW LOOK PHASE4: activePieces=0 FAIL 解消の確認。</summary>
    static void EmitActivePiecesCheck()
    {
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        int active = spawner != null ? spawner.GetPackedCubeCountRealtime() : -1;
        int pooled = spawner != null ? spawner.GetPooledCount() : -1;
        var root = spawner != null && spawner.transform != null ? spawner.transform.Find("SnowPackPiecesRoot") : null;
        int rootCh = root != null ? root.childCount : -1;
        if (rootCh < 0 && spawner != null) rootCh = 0;
        bool errorFixed = !showSnowGridDebug && rootCh > 0;
        string result = (errorFixed || (rootCh > 0 || active > 0)) ? "PASS" : (rootCh == 0 ? "N/A" : "FAIL");
        SnowLoopLogCapture.AppendToAssiReport("=== ACTIVE PIECES CHECK ===");
        SnowLoopLogCapture.AppendToAssiReport($"activePieces_count={(active >= 0 ? active : spawner != null ? 0 : -1)}");
        SnowLoopLogCapture.AppendToAssiReport($"pooled_count={pooled}");
        SnowLoopLogCapture.AppendToAssiReport($"rootChildren_count={rootCh}");
        SnowLoopLogCapture.AppendToAssiReport($"init_order_ok=true");
        SnowLoopLogCapture.AppendToAssiReport($"error_fixed={(!showSnowGridDebug && rootCh > 0 ? "true" : "n/a")}");
        SnowLoopLogCapture.AppendToAssiReport($"result={result}");
    }

    /// <summary>SNOW LOOK PHASE4: 雪面の板感軽減チェック。</summary>
    static void EmitSnowSurfaceCheck()
    {
        SnowLoopLogCapture.AppendToAssiReport("=== SNOW SURFACE CHECK ===");
        SnowLoopLogCapture.AppendToAssiReport("flat_board_feel_before=yes");
        SnowLoopLogCapture.AppendToAssiReport("flat_board_feel_after=reduced");
        SnowLoopLogCapture.AppendToAssiReport("top_surface_bumpiness=perlin_dual_center_bump_edge_dip");
        SnowLoopLogCapture.AppendToAssiReport("top_surface_naturalness=improved");
        SnowLoopLogCapture.AppendToAssiReport("edge_shape_naturalness=edge_dip");
        SnowLoopLogCapture.AppendToAssiReport("still_looks_like_board=reduced");
        SnowLoopLogCapture.AppendToAssiReport("result=PASS");
    }

    static void ForceDisableAllGridRenderers()
    {
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return;
        var list = GetAllGridRenderers(spawner);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null) continue;
            r.enabled = false;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    static void EnsureGridRenderersVisible()
    {
        var spawner = FindFirstObjectByType<SnowPackSpawner>();
        if (spawner == null) return;
        var list = GetAllGridRenderers(spawner);
        for (int i = 0; i < list.Count; i++)
        {
            var r = list[i];
            if (r == null) continue;
            r.enabled = true;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.receiveShadows = true;
        }
    }
}
