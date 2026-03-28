using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

/// <summary>
/// Snow Panic - 屋根雪表示 自動診断 &amp; 自動接続ツール
/// メニュー: SnowPanic → Snow Scene Diagnostic
/// </summary>
public static class SnowSceneDiagnosticTool
{
    [MenuItem("SnowPanic/Snow Scene Diagnostic")]
    public static void RunDiagnostic()
    {
        Debug.Log("[SNOW_DIAG] ====== Snow Scene Diagnostic START ======");

        var rss = Object.FindFirstObjectByType<RoofSnowSystem>();
        var sps = Object.FindFirstObjectByType<SnowPackSpawner>();

        bool anyFix = false;

        // ── 1. RoofSnowSystem 有無 ──────────────────────────
        if (rss == null)
        {
            Debug.LogError("[SNOW_DIAG] RoofSnowSystem NOT FOUND in scene. root_cause=missing_component");
            EditorUtility.DisplayDialog("Snow Diagnostic", "RoofSnowSystem が見つかりません。\nSnowTest GameObject を確認してください。", "OK");
            return;
        }
        Debug.Log($"[SNOW_DIAG] RoofSnowSystem FOUND on '{rss.gameObject.name}' enabled={rss.enabled} activeInHierarchy={rss.gameObject.activeInHierarchy}");

        // ── 2. roofSlideCollider ──────────────────────────────
        if (rss.roofSlideCollider == null)
        {
            var roofGo = GameObject.Find("RoofSlideCollider") ?? GameObject.Find("RoofPanel");
            if (roofGo != null)
            {
                var col = roofGo.GetComponent<Collider>();
                if (col != null)
                {
                    rss.roofSlideCollider = col;
                    EditorUtility.SetDirty(rss);
                    anyFix = true;
                    Debug.Log($"[SNOW_DIAG] roofSlideCollider auto-wired to '{roofGo.name}'");
                }
            }
            else
            {
                Debug.LogError("[SNOW_DIAG] roofSlideCollider NULL & no RoofSlideCollider/RoofPanel found. root_cause=missing_collider_go");
            }
        }
        else
        {
            Debug.Log($"[SNOW_DIAG] roofSlideCollider OK: '{rss.roofSlideCollider.gameObject.name}'");
        }

        // ── 3. snowPackSpawner ────────────────────────────────
        if (rss.snowPackSpawner == null)
        {
            if (sps != null)
            {
                rss.snowPackSpawner = sps;
                EditorUtility.SetDirty(rss);
                anyFix = true;
                Debug.Log($"[SNOW_DIAG] snowPackSpawner auto-wired: RoofSnowSystem -> '{sps.gameObject.name}'");
            }
            else
            {
                Debug.LogWarning("[SNOW_DIAG] snowPackSpawner NULL but FindFirstObjectByType will resolve at runtime.");
            }
        }
        else
        {
            Debug.Log($"[SNOW_DIAG] snowPackSpawner already set: '{rss.snowPackSpawner.gameObject.name}'");
        }

        // ── 4. SnowPackSpawner.roofSnowSystem ────────────────
        if (sps != null)
        {
            var roofSnowSystemField = typeof(SnowPackSpawner).GetField(
                "roofSnowSystem",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (roofSnowSystemField != null)
            {
                var current = roofSnowSystemField.GetValue(sps) as RoofSnowSystem;
                if (current == null)
                {
                    roofSnowSystemField.SetValue(sps, rss);
                    EditorUtility.SetDirty(sps);
                    anyFix = true;
                    Debug.Log("[SNOW_DIAG] SnowPackSpawner.roofSnowSystem auto-wired");
                }
                else
                {
                    Debug.Log($"[SNOW_DIAG] SnowPackSpawner.roofSnowSystem OK: '{current.gameObject.name}'");
                }
            }
        }

        // ── 5. targetSnowRenderer (SnowPackSpawner) ──────────
        if (sps != null)
        {
            var targetRendField = typeof(SnowPackSpawner).GetField(
                "targetSnowRenderer",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (targetRendField != null)
            {
                var currentR = targetRendField.GetValue(sps) as Renderer;
                if (currentR == null)
                {
                    // RoofSnowLayer 子 Renderer を探す
                    Renderer candidate = null;
                    if (rss.roofSlideCollider != null)
                    {
                        var layer = rss.roofSlideCollider.transform.Find("RoofSnowLayer");
                        if (layer != null)
                        {
                            candidate = layer.GetComponent<Renderer>();
                            if (candidate != null)
                            {
                                targetRendField.SetValue(sps, candidate);
                                EditorUtility.SetDirty(sps);
                                anyFix = true;
                                Debug.Log($"[SNOW_DIAG] targetSnowRenderer auto-wired to '{candidate.gameObject.name}' (RoofSnowLayer child)");
                            }
                        }
                    }
                    if (candidate == null)
                    {
                        // RoofSnowLayer はまだ存在しない (Play時に生成される) → 問題なし
                        Debug.Log("[SNOW_DIAG] targetSnowRenderer NULL, but RoofSnowLayer is created at runtime. OK.");
                    }
                }
                else
                {
                    Debug.Log($"[SNOW_DIAG] targetSnowRenderer OK: '{currentR.gameObject.name}'");
                }
            }
        }

        // ── 6. RoofSnowSystem GameObject/Component のアクティブ確認 ──
        if (!rss.gameObject.activeInHierarchy)
        {
            rss.gameObject.SetActive(true);
            EditorUtility.SetDirty(rss.gameObject);
            anyFix = true;
            Debug.Log("[SNOW_DIAG] SnowTest GO was inactive → activated");
        }
        if (!rss.enabled)
        {
            rss.enabled = true;
            EditorUtility.SetDirty(rss);
            anyFix = true;
            Debug.Log("[SNOW_DIAG] RoofSnowSystem component was disabled → enabled");
        }

        // ── 7. roofSnowColor が暗い場合は白に戻す ─────────────
        // Inspector 値が反映されるが ApplySimpleSnowMaterial が上書きするので確認のみ
        Color c = rss.roofSnowColor;
        if (c.r < 0.5f && c.g < 0.5f && c.b < 0.5f)
        {
            rss.roofSnowColor = new Color(0.92f, 0.95f, 1f);
            EditorUtility.SetDirty(rss);
            anyFix = true;
            Debug.Log($"[SNOW_DIAG] roofSnowColor was dark ({c.r:F2},{c.g:F2},{c.b:F2}) → reset to (0.92,0.95,1.0)");
        }

        // ── 8. シーン保存 ────────────────────────────────────
        if (anyFix)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());
            Debug.Log("[SNOW_DIAG] Scene marked dirty. Please save (Ctrl+S) then Play.");
        }
        else
        {
            Debug.Log("[SNOW_DIAG] No broken references found. All wired correctly.");
        }

        // ── 9. 診断サマリー ──────────────────────────────────
        string roofColliderStatus = rss.roofSlideCollider != null ? "OK:" + rss.roofSlideCollider.gameObject.name : "NONE";
        string spsStatus = rss.snowPackSpawner != null ? "OK:" + rss.snowPackSpawner.gameObject.name : "NULL(auto-resolve at runtime)";
        string spsRssStatus = sps != null ? "found" : "MISSING";

        Debug.Log($"[SNOW_DIAG] === SUMMARY ===\n" +
                  $"  rss_go='{rss.gameObject.name}' enabled={rss.enabled}\n" +
                  $"  roofSlideCollider={roofColliderStatus}\n" +
                  $"  snowPackSpawner={spsStatus}\n" +
                  $"  SnowPackSpawner_in_scene={spsRssStatus}\n" +
                  $"  heightmap_mode={rss.heightmap_mode_enabled}\n" +
                  $"  any_fix_applied={anyFix}");

        string msg = anyFix
            ? $"自動修正を適用しました。\nCtrl+S でシーンを保存 → Play で確認してください。"
            : $"参照は正常です。\nそれでも雪が見えない場合は Play してから Console の [SNOW_MESH_ASSIGN] ログを確認してください。";

        EditorUtility.DisplayDialog("Snow Diagnostic", msg, "OK");
        Debug.Log("[SNOW_DIAG] ====== Snow Scene Diagnostic END ======");
    }
}
