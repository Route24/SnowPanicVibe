using UnityEditor;
using UnityEngine;

public static class CorniceSceneSetup
{
    [InitializeOnLoadMethod]
    static void SubscribePlayModeChange()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
            FixCorniceRootMissingScript();
    }

    /// <summary>Edit モードでの material リークを防ぐ。シェーダーから新規作成して sharedMaterial に代入。</summary>
    static void SetRendererColor(Renderer r, Color c)
    {
        if (r == null) return;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) return;
        var mat = new Material(shader);
        mat.color = c;
        r.sharedMaterial = mat;
    }

    [MenuItem("SnowPanicVibe/Fix CorniceRoot Missing Script")]
    public static void FixCorniceRootMissingScript()
    {
        var root = GameObject.Find("CorniceRoot");
        if (root == null) { Debug.LogWarning("CorniceRoot not found."); return; }
        int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
        if (removed > 0) Debug.Log($"Removed {removed} missing script(s) from CorniceRoot.");
        if (root.GetComponent<CorniceRuntimeSnowSetup>() == null)
        {
            root.AddComponent<CorniceRuntimeSnowSetup>();
            Debug.Log("Added CorniceRuntimeSnowSetup to CorniceRoot.");
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
    }

    [MenuItem("SnowPanicVibe/Setup Cornice Scene")]
    public static void SetupCorniceScene()
    {
        // 既存オブジェクトを掃除
        string[] namesToDelete = { "CorniceRoot", "Ground", "HouseBody", "Roof", "RoofBase", "RoofPanel", "CorniceSnow", "Person", "Window", "Porch", "SnowParticle", "GroundSnow", "RoofSnow", "RoofSnowPlaceholder", "RidgeSnow", "GroundDecor" };
        foreach (string name in namesToDelete)
        {
            var obj = GameObject.Find(name);
            if (obj != null) Object.DestroyImmediate(obj);
        }
        foreach (var obj in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (obj.name == "SnowBase" || obj.name == "RoofSnowPlane") Object.DestroyImmediate(obj.gameObject); // 既存シーンの古い白立方体も削除
        }

        // ルート
        var root = new GameObject("CorniceRoot");
        root.AddComponent<CorniceRuntimeSnowSetup>(); // Play モードで ParticleSystem を生成（Unity 6 の material リーク回避）

        // 地面（雪で覆われた感じ：白っぽく）
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root.transform, false);
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        SetRendererColor(ground.GetComponent<Renderer>(), new Color(0.9f, 0.92f, 0.95f));
        ground.GetComponent<Collider>().material = new PhysicsMaterial("Ground") { bounciness = 0f };

        // 地面の積雪（Play モードで ParticleSystem を生成）
        var groundSnowObj = new GameObject("GroundSnow");
        groundSnowObj.transform.SetParent(root.transform, false);
        groundSnowObj.transform.position = new Vector3(0f, 0.25f, 0f);

        // 地面の岩・草（雪に積もっている感じ、地面と屋根の雪を判別しやすく）
        var groundDecor = new GameObject("GroundDecor");
        groundDecor.transform.SetParent(root.transform, false);
        float[] rockX = { -2.5f, 2.2f, -1.8f, 2.8f, -2.8f, 1.5f, -1.2f };
        float[] rockZ = { 1.5f, -0.5f, -2f, 2f, 0.8f, -2.5f, 2.2f };
        for (int i = 0; i < rockX.Length; i++)
        {
            var rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rock_" + i;
            rock.transform.SetParent(groundDecor.transform, false);
            rock.transform.position = new Vector3(rockX[i], 0.25f + i * 0.03f, rockZ[i]);
            float s = 0.6f + (i % 3) * 0.2f;
            rock.transform.localScale = new Vector3(s, s * 0.7f, s * 0.9f);
            SetRendererColor(rock.GetComponent<Renderer>(), new Color(0.4f, 0.38f, 0.35f));
        }
        float[] grassX = { -2f, 2.5f, -0.5f, 2f, -2.2f, 0.8f, -1.5f, 1.8f };
        float[] grassZ = { 2f, 1f, -1.5f, -2.2f, -0.3f, 2.5f, 1.2f, -1f };
        for (int i = 0; i < grassX.Length; i++)
        {
            var grass = GameObject.CreatePrimitive(PrimitiveType.Cube);
            grass.name = "Grass_" + i;
            grass.transform.SetParent(groundDecor.transform, false);
            grass.transform.position = new Vector3(grassX[i], 0.2f, grassZ[i]);
            grass.transform.localScale = new Vector3(0.15f, 0.5f + (i % 2) * 0.25f, 0.1f);
            grass.transform.rotation = Quaternion.Euler(0f, i * 45f, (i % 3) * 8f);
            SetRendererColor(grass.GetComponent<Renderer>(), new Color(0.25f, 0.35f, 0.2f));
        }

        // 小さな家本体（木造・茶色っぽい）
        var house = GameObject.CreatePrimitive(PrimitiveType.Cube);
        house.name = "HouseBody";
        house.transform.SetParent(root.transform, false);
        house.transform.position = new Vector3(0f, 0.75f, 0f);
        house.transform.localScale = new Vector3(2f, 1.5f, 2f);
        SetRendererColor(house.GetComponent<Renderer>(), new Color(0.55f, 0.4f, 0.3f)); // 木の茶色

        // 片流れ屋根（1枚傾斜）：棟なし、雪形状がシンプル
        var roof = new GameObject("Roof");
        roof.transform.SetParent(root.transform, false);
        roof.transform.position = new Vector3(0f, 1.5f, 0f);

        var roofPanel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofPanel.name = "RoofPanel";
        roofPanel.transform.SetParent(roof.transform, false);
        roofPanel.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        roofPanel.transform.localScale = new Vector3(2.02f, 0.05f, 2.2f);
        roofPanel.transform.localRotation = Quaternion.Euler(25f, 0f, 0f); // 手前（-Z）が低い傾斜
        SetRendererColor(roofPanel.GetComponent<Renderer>(), new Color(0.35f, 0.32f, 0.3f));
        var roofSlideMat = new PhysicsMaterial("RoofSlide") { dynamicFriction = 0.05f, staticFriction = 0.08f, bounciness = 0f };
        roofPanel.GetComponent<Collider>().material = roofSlideMat;

        // 屋根の積雪（軒側に滑り落ちる方向）
        CreateRoofSnowPlaceholder(roofPanel.transform, new Vector3(0f, -0.42f, -0.9f).normalized);

        // ポーチ（正面の玄関先）
        var porch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        porch.name = "Porch";
        porch.transform.SetParent(root.transform, false);
        porch.transform.position = new Vector3(0f, 0.08f, -1.15f);
        porch.transform.localScale = new Vector3(2.2f, 0.08f, 0.4f);
        SetRendererColor(porch.GetComponent<Renderer>(), new Color(0.45f, 0.35f, 0.25f)); // 木のポーチ

        // 窓（正面＝カメラ側、暗めで窓らしく）
        var window = GameObject.CreatePrimitive(PrimitiveType.Cube);
        window.name = "Window";
        window.transform.SetParent(house.transform, false);
        window.transform.localPosition = new Vector3(0f, 0.2f, -0.51f);
        window.transform.localScale = new Vector3(0.6f, 0.5f, 0.02f);
        var windowRend = window.GetComponent<Renderer>();
        if (windowRend != null)
            SetRendererColor(windowRend, new Color(0.2f, 0.35f, 0.55f)); // 曇った窓

        // 降雪パーティクル（Play モードで ParticleSystem を生成）
        var snowObj = new GameObject("SnowParticle");
        snowObj.transform.SetParent(root.transform, false);
        snowObj.transform.position = new Vector3(0f, 6f, 0f);

        // 照明（冬の柔らかい光）
        var dirLight = Object.FindFirstObjectByType<UnityEngine.Light>();
        if (dirLight != null)
        {
            dirLight.color = new Color(0.95f, 0.97f, 1f); // わずかに青白い
            dirLight.intensity = 1.2f;
        }

        // カメラ
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.75f, 0.82f, 0.9f);

        if (cam.GetComponent<CorniceHitter>() == null)
            cam.gameObject.AddComponent<CorniceHitter>().mainCamera = cam;

        var orbit = cam.GetComponent<CameraOrbit>();
        if (orbit == null) orbit = cam.gameObject.AddComponent<CameraOrbit>();
        var targetGo = new GameObject("CameraOrbitTarget");
        targetGo.transform.SetParent(root.transform, false);
        targetGo.transform.position = new Vector3(0f, 1.5f, 0f);
        orbit.target = targetGo.transform;
        orbit.distance = 12f;

        Set俯瞰Camera(cam);

        Selection.activeGameObject = root;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Setup complete. Play モードで屋根の雪が表示されます。屋根の雪をクリックで雪落とし。");
    }

    [MenuItem("SnowPanicVibe/Reset Camera to 俯瞰 View")]
    public static void Reset俯瞰Camera()
    {
        var cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("Main Camera not found.");
            return;
        }
        Set俯瞰Camera(cam);
        Debug.Log("Camera reset to 俯瞰 view.");
    }

    static void CreateRoofSnowPlaceholder(Transform roofPanel, Vector3 slideDir)
    {
        float snowThick = 1.7f;
        float topY = 0.025f + snowThick * 0.3f;

        var go = new GameObject("RoofSnowPlaceholder");
        go.transform.SetParent(roofPanel, false);
        go.transform.localPosition = new Vector3(0f, topY, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var ph = go.AddComponent<RoofSnowPlaceholder>();
        ph.slideDownDirection = slideDir;
    }

    static void Set俯瞰Camera(Camera cam)
    {
        Vector3 lookAt = new Vector3(0f, 1.5f, -0.5f);
        cam.transform.position = new Vector3(0f, 8f, -8f);
        cam.transform.LookAt(lookAt);
        var orbit = cam.GetComponent<CameraOrbit>();
        if (orbit != null) { orbit._yaw = 0f; orbit._pitch = 39f; orbit.distance = 12f; }
    }
}

