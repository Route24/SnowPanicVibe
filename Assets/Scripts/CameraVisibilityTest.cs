using UnityEngine;

/// <summary>
/// カメラ前に強制的に巨大赤キューブを生成する visibility 確認スクリプト。
/// Play 開始直後に実行される。既存システムに一切依存しない。
/// 確認後は SnowPanic → Remove Camera Visibility Test で削除可能。
/// v2
/// </summary>
[DefaultExecutionOrder(-2000)]
public class CameraVisibilityTest : MonoBehaviour
{
    void Start()
    {
        // ── 1. 全カメラ列挙 ──────────────────────────────────
        var allCams = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        Debug.Log($"[CAM_VISIBILITY] active_camera_count={allCams.Length}");
        foreach (var c in allCams)
        {
            Debug.Log($"[CAM_VISIBILITY] cam='{c.gameObject.name}' enabled={c.enabled} " +
                      $"depth={c.depth} targetDisplay={c.targetDisplay} " +
                      $"cullingMask={c.cullingMask} tag='{c.tag}' " +
                      $"pos=({c.transform.position.x:F2},{c.transform.position.y:F2},{c.transform.position.z:F2}) " +
                      $"rot=({c.transform.eulerAngles.x:F1},{c.transform.eulerAngles.y:F1},{c.transform.eulerAngles.z:F1}) " +
                      $"fov={c.fieldOfView:F1} ortho={c.orthographic}");
        }

        // ── 2. Main Camera 取得 & hard reset ─────────────────
        Camera cam = Camera.main;
        if (cam == null && allCams.Length > 0) cam = allCams[0];

        if (cam == null)
        {
            Debug.LogError("[CAM_VISIBILITY] camera_found=NO — no Camera in scene!");
            return;
        }

        cam.enabled = true;
        cam.cullingMask = ~0; // Everything
        cam.tag = "MainCamera";
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.15f, 0.25f, 1f);
        Debug.Log($"[CAM_VISIBILITY] camera_hard_reset=YES cam='{cam.gameObject.name}' " +
                  $"pos=({cam.transform.position.x:F2},{cam.transform.position.y:F2},{cam.transform.position.z:F2}) " +
                  $"rot=({cam.transform.eulerAngles.x:F1},{cam.transform.eulerAngles.y:F1},{cam.transform.eulerAngles.z:F1}) " +
                  $"projection={(cam.orthographic ? "orthographic" : "perspective")} " +
                  $"fov={cam.fieldOfView:F1} cullingMask={cam.cullingMask}");

        // ── 3. カメラ前に赤キューブ生成 ──────────────────────
        Vector3 spawnPos = cam.transform.position + cam.transform.forward * 3.0f;

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "DEBUG_RedCube_CamTest";
        cube.transform.position = spawnPos;
        cube.transform.rotation = Quaternion.identity;
        cube.transform.localScale = new Vector3(2f, 2f, 2f);
        cube.layer = 0; // Default

        // 物理無効
        var col = cube.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // 赤マテリアル --- URP / Built-in 両対応
        var mr = cube.GetComponent<MeshRenderer>();
        if (mr != null)
        {
            string[] shaderCandidates = new[]
            {
                "Universal Render Pipeline/Unlit",
                "Unlit/Color",
                "Sprites/Default",
                "Standard"
            };
            Shader sh = null;
            string usedShaderName = "none";
            foreach (var sname in shaderCandidates)
            {
                sh = Shader.Find(sname);
                if (sh != null) { usedShaderName = sname; break; }
            }

            if (sh != null)
            {
                var mat = new Material(sh);
                mat.name = "DEBUG_Red";
                Color red = Color.red;
                if (mat.HasProperty("_BaseColor"))     mat.SetColor("_BaseColor", red);
                if (mat.HasProperty("_BaseMap"))       mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                if (mat.HasProperty("_Color"))         mat.SetColor("_Color", red);
                if (mat.HasProperty("_RendererColor")) mat.SetColor("_RendererColor", red);
                mr.sharedMaterial = mat;
                Debug.Log($"[CAM_VISIBILITY] shader_used={usedShaderName}");
            }
            else
            {
                Debug.LogWarning("[CAM_VISIBILITY] No suitable shader found — cube uses default material");
            }
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = 9999;
            mr.enabled = true;
        }

        Debug.Log($"[CAM_VISIBILITY] red_cube_created=YES " +
                  $"name={cube.name} " +
                  $"pos=({spawnPos.x:F2},{spawnPos.y:F2},{spawnPos.z:F2}) " +
                  $"scale=(2,2,2) layer=Default mat={(mr?.sharedMaterial?.name ?? "none")}");
    }
}
