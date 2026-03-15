using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class CorniceSceneSetup
{
    const string PrefAutoSetup = "SnowPanicVibe.AutoSetupOnPlay";

    [InitializeOnLoadMethod]
    static void SubscribePlayModeChange()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        var sceneName = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name ?? "";
        bool isOneHouse = !string.IsNullOrEmpty(sceneName) && sceneName.Contains("OneHouse");
        if (VideoPipelineSelfTestMode.IsActive && !isOneHouse) return;
        if (EditorPrefs.GetBool(PrefAutoSetup, true) || isOneHouse)
            SetupAll();
        FixAllMissingScriptsAndSave();
    }

    const string OneHouseScenePath = "Assets/Scenes/Avalanche_Test_OneHouse.unity";

    [MenuItem("SnowPanicVibe/Open Avalanche_Test_OneHouse", false, 50)]
    public static void OpenAvalancheTestOneHouse()
    {
        if (!UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(OneHouseScenePath))
        {
            Debug.LogWarning("Avalanche_Test_OneHouse.unity が見つかりません。 Assets/Scenes/ を確認してください。");
            return;
        }
        if (UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(OneHouseScenePath);
            FixAllMissingScripts(); // エラー（Missing Script）があれば修復
        }
        Debug.Log("Avalanche_Test_OneHouse を開きました。エラーがあれば SnowPanicVibe → Fix All Missing Scripts を実行してください。");
    }

    [MenuItem("SnowPanicVibe/Setup All (Cornice + Snow Test)")]
    public static void SetupAll()
    {
        var sceneName = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name ?? "";
        bool oneHouse = !string.IsNullOrEmpty(sceneName) && sceneName.Contains("OneHouse");
        SetupCorniceScene();
        if (!oneHouse) SetupSnowTest();
        else
        {
            foreach (var name in new[] { "SnowTestRoot" })
            {
                var obj = GameObject.Find(name);
                if (obj != null) Object.DestroyImmediate(obj);
            }
        }
        Debug.Log(oneHouse ? "Setup All 完了（OneHouse: テスト屋根なし、アセット家屋根のみプレイ面）。" : "Setup All 完了。Play で雪落としを楽しめます。");
    }

    [MenuItem("SnowPanicVibe/Auto Setup on Play %#a", false, 100)]
    public static void ToggleAutoSetup()
    {
        bool v = !EditorPrefs.GetBool(PrefAutoSetup, true);
        EditorPrefs.SetBool(PrefAutoSetup, v);
        Debug.Log(v ? "Play 時に自動で Setup All を実行します。" : "Play 時の自動 Setup を無効にしました。");
    }

    [MenuItem("SnowPanicVibe/Auto Setup on Play %#a", true)]
    static bool ToggleAutoSetupValidate()
    {
        Menu.SetChecked("SnowPanicVibe/Auto Setup on Play", EditorPrefs.GetBool(PrefAutoSetup, true));
        return true;
    }

    static void FixAllMissingScriptsAndSave()
    {
        FixAllMissingScripts();
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        if (scene.isDirty)
            UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes();
    }

    /// <summary>Edit モードでの material リークを防ぐ。シェーダーから新規作成して sharedMaterial に代入。農園系テイスト用にスムースネス付き。</summary>
    static void SetRendererColor(Renderer r, Color c, float smoothness = 0.35f)
    {
        if (r == null) return;
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) return;
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", c);
        mat.SetFloat("_Smoothness", smoothness);
        r.sharedMaterial = mat;
    }

    const string RefTexPath = "Assets/Textures/FarmVillageReference.png";
    const string GroundTexPath = "Assets/Textures/FarmVillageGroundOnly.png";

    /// <summary>参考画像の地面部分（下部）を切り出して FarmVillageGroundOnly.png を生成</summary>
    [MenuItem("SnowPanicVibe/Create Ground Texture from Reference")]
    public static void CreateGroundTextureFromReference()
    {
        var fullPath = Path.Combine(Application.dataPath, "Textures/FarmVillageReference.png");
        if (!File.Exists(fullPath)) { Debug.LogError("FarmVillageReference.png が見つかりません"); return; }

        var bytes = File.ReadAllBytes(fullPath);
        var full = new Texture2D(2, 2);
        if (!full.LoadImage(bytes)) { Debug.LogError("画像の読み込みに失敗"); return; }

        // 下部 40% を切り出し（道や雪が多い部分）
        int w = full.width;
        int h = (int)(full.height * 0.4f);
        int y = 0;
        var pixels = full.GetPixels(0, y, w, h);
        var cropped = new Texture2D(w, h);
        cropped.SetPixels(pixels);
        cropped.Apply();

        var outPath = Path.Combine(Application.dataPath, "Textures/FarmVillageGroundOnly.png");
        File.WriteAllBytes(outPath, cropped.EncodeToPNG());
        AssetDatabase.Refresh();
        Debug.Log("FarmVillageGroundOnly.png を生成しました。");
    }

    const string RoofWoodMatPath = "Assets/Materials/DioramaRoofWood.mat";
    const string RoofSnowMatPath = "Assets/Materials/DioramaRoofSnow.mat";

    /// <summary>屋根マテリアル（ダイオラマ風・木質、雪とのコントラスト）</summary>
    static void SetRoofMaterial(Renderer r)
    {
        if (r == null) return;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RoofWoodMatPath);
        if (mat != null) { r.sharedMaterial = mat; return; }
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) return;
        var fallback = new Material(shader);
        fallback.SetColor("_BaseColor", new Color(0.68f, 0.48f, 0.38f));
        fallback.SetFloat("_Smoothness", 0.25f);
        fallback.SetFloat("_Metallic", 0f);
        r.sharedMaterial = fallback;
    }

    /// <summary>屋根雪サーフェス（メッシュ、ノイズ付き雪マテリアル）</summary>
    static void SetRoofSnowSurfaceMaterial(Renderer r)
    {
        if (r == null) return;
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RoofSnowMatPath);
        if (mat != null) { r.sharedMaterial = mat; return; }
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) return;
        var fallback = new Material(shader);
        fallback.SetColor("_BaseColor", new Color(0.82f, 0.86f, 0.9f));
        fallback.SetFloat("_Smoothness", 0.35f);
        r.sharedMaterial = fallback;
    }

    /// <summary>地面に参考画像の地面部分テクスチャを適用</summary>
    static void SetGroundTexture(Renderer r)
    {
        if (r == null) return;
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(GroundTexPath);
        if (tex == null) tex = AssetDatabase.LoadAssetAtPath<Texture2D>(RefTexPath);
        if (tex == null) { SetRendererColor(r, new Color(0.98f, 0.99f, 1f), 0.3f); return; }
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null) return;
        var mat = new Material(shader);
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.2f);
        r.sharedMaterial = mat;
    }

    [MenuItem("SnowPanicVibe/Fix All Missing Scripts")]
    public static void FixAllMissingScripts()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        int totalRemoved = 0;
        foreach (var root in roots)
        {
            foreach (var go in root.GetComponentsInChildren<Transform>(true))
            {
                int n = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go.gameObject);
                if (n > 0)
                {
                    totalRemoved += n;
                    Debug.Log($"Removed {n} missing script(s) from {go.name}");
                }
            }
        }
        var corniceRoot = GameObject.Find("CorniceRoot");
        var added = false;
        if (corniceRoot != null && corniceRoot.GetComponent<CorniceRuntimeSnowSetup>() == null)
        {
            corniceRoot.AddComponent<CorniceRuntimeSnowSetup>();
            added = true;
            Debug.Log("Added CorniceRuntimeSnowSetup to CorniceRoot.");
        }
        if (totalRemoved > 0 || added)
        {
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            if (totalRemoved > 0) Debug.Log($"Fixed: Removed {totalRemoved} missing script(s).");
        }
    }

    [MenuItem("SnowPanicVibe/Fix CorniceRoot Missing Script")]
    public static void FixCorniceRootMissingScript()
    {
        FixAllMissingScripts();
    }

    [MenuItem("SnowPanicVibe/Setup Cornice Scene")]
    public static void SetupCorniceScene()
    {
        FixAllMissingScripts();
        // 既存オブジェクトを掃除（OneHouse時はテスト屋根も削除）
        var namesToDelete = new List<string> { "CorniceRoot", "Ground", "HouseBody", "Roof", "RoofBase", "RoofPanel", "EavesDropTrigger", "CorniceSnow", "Person", "Window", "Porch", "SnowParticle", "GroundSnow", "RoofSnow", "RoofSnowPlaceholder", "RidgeSnow", "GroundDecor", "FarmVillageBackdrop", "Houses", "DioramaVolume", "DistantTrees", "OneHouseMarker", "SnowTestRoot" };
        for (int i = 0; i < 8; i++) namesToDelete.Add("House_" + i);
        foreach (var name in namesToDelete)
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

        // 地面（雪で覆われたクリーム白）
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root.transform, false);
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(8f, 1f, 6f);
        SetRendererColor(ground.GetComponent<Renderer>(), new Color(0.82f, 0.86f, 0.9f), 0.25f);
        ground.GetComponent<Collider>().material = new PhysicsMaterial("Ground") { bounciness = 0f };

        // 地面の積雪（Play モードで ParticleSystem を生成）
        var groundSnowObj = new GameObject("GroundSnow");
        groundSnowObj.transform.SetParent(root.transform, false);
        groundSnowObj.transform.position = new Vector3(0f, 0.25f, 0f);

        // 地面の装飾（雪景色）
        var groundDecor = new GameObject("GroundDecor");
        groundDecor.transform.SetParent(root.transform, false);

        var sceneName = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene().name ?? "";
        bool oneHouseForced = !string.IsNullOrEmpty(sceneName) && sceneName.Contains("OneHouse");
        int houseCount = oneHouseForced ? 1 : 8;
        int rows = oneHouseForced ? 1 : 2;
        int cols = oneHouseForced ? 1 : 4;

        var housesRoot = new GameObject("Houses");
        housesRoot.transform.SetParent(root.transform, false);
        housesRoot.transform.localPosition = Vector3.zero;

        float houseW = 2f;
        float houseD = 2f;
        float houseH = 1.2f;
        float spacingX = 3.2f;
        float spacingZ = 3.5f;
        float offsetX = oneHouseForced ? 0f : (-spacingX * 1.5f);
        float offsetZ = oneHouseForced ? 0f : (spacingZ * 0.5f);
        var slideDir = new Vector3(0f, -0.42f, -0.9f).normalized;

        if (oneHouseForced)
        {
            var marker = new GameObject("OneHouseMarker");
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = Vector3.zero;
        }

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int idx = row * cols + col;
                float px = offsetX + col * spacingX;
                float pz = offsetZ - row * spacingZ;

                var houseGo = new GameObject("House_" + idx);
                houseGo.transform.SetParent(housesRoot.transform, false);
                houseGo.transform.position = new Vector3(px, 0f, pz);

                var house = GameObject.CreatePrimitive(PrimitiveType.Cube);
                house.name = "HouseBody";
                house.transform.SetParent(houseGo.transform, false);
                house.transform.localPosition = new Vector3(0f, houseH * 0.5f, 0f);
                house.transform.localScale = new Vector3(houseW, houseH, houseD);
                SetRendererColor(house.GetComponent<Renderer>(), new Color(0.82f, 0.7f, 0.55f), 0.4f);
                house.GetComponent<Renderer>().enabled = false; // Kenney 表示優先（当たり判定は別）

                var roof = new GameObject("Roof");
                roof.transform.SetParent(houseGo.transform, false);
                roof.transform.localPosition = new Vector3(0f, houseH, 0f);

                var roofPanel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                roofPanel.name = "RoofPanel";
                roofPanel.transform.SetParent(roof.transform, false);
                roofPanel.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                float roofThick = oneHouseForced ? 0.02f : 0.12f;
                roofPanel.transform.localScale = new Vector3(houseW * 1.02f, roofThick, houseD * 1.12f);
                roofPanel.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
                SetRoofMaterial(roofPanel.GetComponent<Renderer>());
                if (!oneHouseForced) CreateRoofEaveEdge(roof.transform, houseW, houseD);
                CreateRoofSnowSurface(roofPanel.transform, houseW, houseD, oneHouseForced);
                var roofPanelRenderer = roofPanel.GetComponent<Renderer>();
                if (roofPanelRenderer != null) roofPanelRenderer.enabled = oneHouseForced;
                roofPanel.GetComponent<Collider>().material = new PhysicsMaterial("RoofSlide") { dynamicFriction = 0.05f, staticFriction = 0.08f, bounciness = 0f };

                var eavesTrigger = new GameObject("EavesDropTrigger");
                eavesTrigger.transform.SetParent(roof.transform, false);
                eavesTrigger.transform.localPosition = new Vector3(0f, -0.15f, -0.7f);
                eavesTrigger.transform.localRotation = Quaternion.identity;
                eavesTrigger.transform.localScale = Vector3.one;
                var box = eavesTrigger.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(2.2f, 0.8f, 1f);
                box.center = Vector3.zero;
                eavesTrigger.AddComponent<EavesDropTrigger>().roofCollider = roofPanel.GetComponent<Collider>();

                var catchZone = new GameObject("EavesCatchZone");
                catchZone.transform.SetParent(roof.transform, false);
                catchZone.transform.localPosition = new Vector3(0f, -0.35f, -0.85f);
                catchZone.transform.localRotation = Quaternion.identity;
                catchZone.transform.localScale = Vector3.one;
                var catchBox = catchZone.AddComponent<BoxCollider>();
                catchBox.isTrigger = true;
                catchBox.size = new Vector3(2.5f, 0.8f, 1.8f);
                catchBox.center = Vector3.zero;
                var catchScript = catchZone.AddComponent<EavesCatchZone>();
                catchScript.dragMultiplier = 0.92f;
                catchScript.applyDuration = 0.3f;

                CreateRoofSnowPlaceholder(roofPanel.transform, slideDir);

                var window = GameObject.CreatePrimitive(PrimitiveType.Cube);
                window.name = "Window";
                window.transform.SetParent(house.transform, false);
                window.transform.localPosition = new Vector3(0f, 0.15f, -houseD * 0.5f - 0.01f);
                window.transform.localScale = new Vector3(0.5f, 0.4f, 0.02f);
                SetRendererColor(window.GetComponent<Renderer>(), new Color(0.95f, 0.9f, 0.7f), 0.6f);
                window.GetComponent<Renderer>().enabled = false;

                // Holiday Kit の家見た目を追加。OneHouse では屋根をスキップ（片流れ単一面のみ）
                CreateKenneyHolidayHouseVisual(houseGo.transform, oneHouseForced);
            }
        }

        // 降雪パーティクル（Play モードで ParticleSystem を生成）
        var snowObj = new GameObject("SnowParticle");
        snowObj.transform.SetParent(root.transform, false);
        snowObj.transform.position = new Vector3(0f, 6f, 0f);

        // 照明（絵本寄りC: やわらかい昼光）
        var dirLight = Object.FindFirstObjectByType<Light>();
        if (dirLight != null)
        {
            dirLight.color = new Color(1f, 0.97f, 0.9f);
            dirLight.intensity = 1.05f;
            dirLight.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            dirLight.shadows = LightShadows.Soft;
            dirLight.shadowStrength = 0.75f;
            dirLight.shadowBias = 0.05f;
        }
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.5f, 0.54f, 0.62f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.8f, 0.9f, 1f);
        RenderSettings.fogDensity = 0.005f;

        // カメラ（Perspective, 屋根が読める角度）
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera");
            cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }
        cam.orthographic = false;
        cam.fieldOfView = 45f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 50f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.78f, 0.88f, 1f);
        cam.allowHDR = false; // testing readability
        var urpCam = cam.GetComponent<UniversalAdditionalCameraData>();
        if (urpCam != null)
        {
            urpCam.renderPostProcessing = true;
            urpCam.allowHDROutput = false;
        }

        if (cam.GetComponent<CorniceHitter>() == null)
            cam.gameObject.AddComponent<CorniceHitter>().mainCamera = cam;

        var orbit = cam.GetComponent<CameraOrbit>();
        if (orbit == null) orbit = cam.gameObject.AddComponent<CameraOrbit>();
        var targetGo = new GameObject("CameraOrbitTarget");
        targetGo.transform.SetParent(root.transform, false);
        targetGo.transform.position = new Vector3(0f, 2f, 0f);
        orbit.target = targetGo.transform;
        orbit.distance = 18f;

        if (oneHouseForced)
            SetRoofCenterCamera(cam);
        else
            SetDioramaCamera(cam);

        SetupDioramaVolume();
        SoftenKenneySceneMaterials();
        CreateDistantTrees(root.transform);

        if (AssetDatabase.LoadAssetAtPath<Material>(RoofWoodMatPath) == null)
            DioramaRoofSetup.CreateAllAssets();

        Selection.activeGameObject = root;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log($"[CORNICE_SETUP] scene={sceneName} house_count={houseCount} spawn_system=CorniceSetup spawn_reason=SetupCorniceScene one_house_forced={oneHouseForced}");
        Debug.Log("Setup complete. Play モードで屋根の雪が表示されます。屋根の雪をクリックで雪落とし。");
    }

    [MenuItem("SnowPanicVibe/Setup Snow Test")]
    public static void SetupSnowTest()
    {
        FixAllMissingScripts();
        // 既存のテストを削除
        foreach (var name in new[] { "SnowTestRoot" })
        {
            var obj = GameObject.Find(name);
            if (obj != null) Object.DestroyImmediate(obj);
        }

        var root = new GameObject("SnowTestRoot");

        // 横1m x 縦2m の傾斜板（コンパクト化：家の1/4サイズ、X=3 に配置）
        var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plank.name = "SnowTestPlank";
        plank.transform.SetParent(root.transform, false);
        plank.transform.position = new Vector3(3f, 1.5f, 0f);
        plank.transform.localScale = new Vector3(1f, 0.02f, 2f);
        plank.transform.rotation = Quaternion.Euler(-25f, 0f, 0f);
        SetRendererColor(plank.GetComponent<Renderer>(), new Color(0.8f, 0.65f, 0.48f), 0.4f); // 農園風・温かい木の板
        plank.GetComponent<Collider>().material = new PhysicsMaterial("Plank") { dynamicFriction = 0.15f, staticFriction = 0.2f, bounciness = 0f };

        // 雪のキューブ: 10cm、10x20のグリッド、1段
        float cubeSize = 0.1f;
        int gridX = 10, gridZ = 20, layers = 1;
        var snowMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
        snowMat.SetColor("_BaseColor", new Color(0.98f, 0.99f, 1f));
        snowMat.SetFloat("_Smoothness", 0.4f);
        var plankTr = plank.transform;

        for (int layer = 0; layer < layers; layer++)
        {
            for (int ix = 0; ix < gridX; ix++)
            {
                for (int iz = 0; iz < gridZ; iz++)
                {
                    float sz = cubeSize * Random.Range(1.03f, 1.14f);
                    float lx = (ix + 0.5f) / gridX - 0.5f + Random.Range(-0.008f, 0.008f);
                    float lz = (iz + 0.5f) / gridZ - 0.5f + Random.Range(-0.008f, 0.008f);
                    var surfacePos = plankTr.TransformPoint(lx, 0.5f, lz);
                    var up = plankTr.TransformDirection(Vector3.up);
                    var pos = surfacePos + up * (layer * cubeSize * 0.5f + sz * 0.5f);

                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = "SnowCube";
                    cube.transform.SetParent(root.transform, false);
                    cube.transform.position = pos;
                    cube.transform.rotation = plankTr.rotation * Quaternion.Euler(Random.Range(-3f, 3f), Random.Range(-6f, 6f), Random.Range(-3f, 3f));
                    cube.transform.localScale = new Vector3(sz * Random.Range(0.98f, 1.08f), sz * Random.Range(0.98f, 1.05f), sz * Random.Range(0.98f, 1.08f));
                    bool isCritical = (float)iz / gridZ >= 0.55f && Random.value < 0.7f;
                    var mat = new Material(snowMat);
                    mat.SetColor("_BaseColor", new Color(0.98f, 0.99f, 1f)); // isCritical warm tint を停止: 本番snowColorに統一
                    cube.GetComponent<Renderer>().sharedMaterial = mat;
                    cube.GetComponent<Collider>().material = new PhysicsMaterial("Snow") { dynamicFriction = 0.2f, staticFriction = 0.28f, bounciness = 0f };

                    var rb = cube.AddComponent<Rigidbody>();
                    rb.mass = 0.4f;
                    rb.linearDamping = 2f;
                    rb.angularDamping = 4f;
                    rb.isKinematic = true;

                    var script = cube.AddComponent<SnowTestCube>();
                    script.hitForce = 1.2f;
                    script.slideDirection = plankTr.TransformDirection(new Vector3(0f, -0.42f, -0.91f)).normalized;
                    script.canBreak = Random.value < 0.6f;
                    script.spreadRadius = 0.18f;
                    script.avalancheSpreadRadius = 0.55f;
                    script.isCriticalSpot = isCritical;
                    script.breakIntoPieces = 4;
                }
            }
        }

        // カメラに CorniceHitter がなければ追加（クリックで叩けるように）
        var cam = Camera.main;
        if (cam != null && cam.GetComponent<CorniceHitter>() == null)
            cam.gameObject.AddComponent<CorniceHitter>().mainCamera = cam;

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Snow Test 作成完了。雪のキューブをクリックで叩いて落とせます。");
    }

    [MenuItem("SnowPanicVibe/Apply RoofDebugFlat As Physics Surface")]
    public static void ApplyRoofDebugFlatAsPhysicsSurface()
    {
        EnsureSnowClumpLayerAndCollision();
        var housesRoot = GameObject.Find("Houses");
        if (housesRoot == null)
        {
            Debug.LogWarning("Houses が見つかりません。先に Setup Cornice Scene を実行してください。");
            return;
        }

        int changed = 0;
        for (int i = 0; i < housesRoot.transform.childCount; i++)
        {
            var house = housesRoot.transform.GetChild(i);
            var roof = house.Find("Roof");
            if (roof == null) continue;

            var debugFlat = roof.Find("RoofDebugFlat");
            Collider selectedCollider = null;
            bool oldSurfaceColliderDisabled = false;

            if (debugFlat != null)
            {
                if (debugFlat.GetComponent<RoofDebugGizmo>() == null)
                    debugFlat.gameObject.AddComponent<RoofDebugGizmo>();

                // RoofPanel 基準で +15%、Yは0.02固定（累積拡大しない）
                var roofPanel = roof.Find("RoofPanel");
                if (roofPanel != null)
                {
                    var baseScale = roofPanel.localScale;
                    debugFlat.localScale = new Vector3(baseScale.x * 1.15f, 0.02f, baseScale.z * 1.15f);
                }
                else
                {
                    var s = debugFlat.localScale;
                    debugFlat.localScale = new Vector3(s.x * 1.15f, 0.02f, s.z * 1.15f);
                }

                var box = debugFlat.GetComponent<BoxCollider>();
                if (box == null) box = debugFlat.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = false;
                box.enabled = true;
                selectedCollider = box;
                oldSurfaceColliderDisabled = DisableRoofSurfaceCollidersForEditor(roof, debugFlat);
                Debug.Log($"[RoofSurfaceSwap] House={house.name} surface=RoofDebugFlat (collider=BoxCollider) oldSurfaceColliderDisabled={oldSurfaceColliderDisabled}");
            }
            else
            {
                selectedCollider = GetFallbackRoofColliderForEditor(roof);
                string surfaceName = selectedCollider != null ? selectedCollider.name : "None";
                Debug.Log($"[RoofSurfaceSwap] House={house.name} surface={surfaceName} (fallback)");
            }

            if (selectedCollider != null)
            {
                var trigger = roof.Find("EavesDropTrigger")?.GetComponent<EavesDropTrigger>();
                if (trigger != null)
                {
                    trigger.roofCollider = selectedCollider;
                    changed++;
                }

                foreach (var rs in roof.GetComponentsInChildren<RoofSnow>(true))
                {
                    rs.roofSurfaceCollider = selectedCollider;
                    changed++;
                }
            }
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log($"Apply RoofDebugFlat As Physics Surface 完了。更新件数: {changed}");
    }

    [MenuItem("SnowPanicVibe/Setup SnowClump Layer Collision")]
    public static void SetupSnowClumpLayerCollision()
    {
        EnsureSnowClumpLayerAndCollision();
        AssignLayerToExistingSnowClumps();
        Debug.Log("SnowClump レイヤー設定完了（SnowClump x SnowClump 衝突OFF）");
    }

    static bool DisableRoofSurfaceCollidersForEditor(Transform roof, Transform keep)
    {
        if (roof == null) return false;
        bool disabledAny = false;
        string[] candidateNames = { "RoofSnowSurface", "RoofSurface", "RoofPanel" };
        foreach (var name in candidateNames)
        {
            var t = roof.Find(name);
            if (t == null || t == keep) continue;
            foreach (var c in t.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.transform.IsChildOf(keep)) continue;
                if (c.enabled)
                {
                    c.enabled = false;
                    disabledAny = true;
                }
            }
        }
        return disabledAny;
    }

    static Collider GetFallbackRoofColliderForEditor(Transform roof)
    {
        if (roof == null) return null;
        var roofSurface = roof.Find("RoofSurface");
        if (roofSurface != null)
        {
            var c = roofSurface.GetComponent<Collider>();
            if (c != null)
            {
                c.enabled = true;
                return c;
            }
        }

        var roofSnowSurface = roof.Find("RoofSnowSurface");
        if (roofSnowSurface != null)
        {
            var c = roofSnowSurface.GetComponent<Collider>();
            if (c != null)
            {
                c.enabled = true;
                return c;
            }
        }

        var roofPanel = roof.Find("RoofPanel");
        if (roofPanel != null)
        {
            var c = roofPanel.GetComponent<Collider>();
            if (c != null)
            {
                c.enabled = true;
                return c;
            }
        }
        return null;
    }

    static void EnsureSnowClumpLayerAndCollision()
    {
        int idx = LayerMask.NameToLayer("SnowClump");
        if (idx < 0)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layersProp = tagManager.FindProperty("layers");
            for (int i = 8; i <= 31; i++)
            {
                var sp = layersProp.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue))
                {
                    sp.stringValue = "SnowClump";
                    tagManager.ApplyModifiedProperties();
                    idx = i;
                    break;
                }
            }
        }

        if (idx >= 0)
            Physics.IgnoreLayerCollision(idx, idx, true);
    }

    static void AssignLayerToExistingSnowClumps()
    {
        int idx = LayerMask.NameToLayer("SnowClump");
        if (idx < 0) return;
        foreach (var c in Object.FindObjectsByType<SnowClump>(FindObjectsSortMode.None))
        {
            if (c != null) c.gameObject.layer = idx;
        }
    }

    [MenuItem("SnowPanicVibe/Reset Camera to 屋根プレイフィールド (試作基準)")]
    public static void Reset屋根プレイフィールドCamera()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("Main Camera not found."); return; }
        SetRoofCenterCamera(cam);
        Debug.Log("Camera reset to 屋根プレイフィールド view (Position 0,5.2,-5.8 Rotation 36,0,0). 屋根主役・地面ほぼ見えない低い俯瞰。");
    }

    [MenuItem("SnowPanicVibe/Reset Camera to 爽快パズル View")]
    public static void Reset爽快パズルCamera()
    {
        var cam = Camera.main;
        if (cam == null) { Debug.LogWarning("Main Camera not found."); return; }
        SetDioramaCamera(cam);
        Debug.Log("Camera reset to 爽快パズル view (Position -6,4,-6 Rotation 25,45,0).");
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

    static void CreateRoofEaveEdge(Transform roofParent, float houseW, float houseD)
    {
        var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
        edge.name = "RoofEaveEdge";
        edge.transform.SetParent(roofParent, false);
        edge.transform.localPosition = new Vector3(0f, 0.12f, -0.58f);
        edge.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
        edge.transform.localScale = new Vector3(houseW * 1.04f, 0.08f, 0.16f);
        SetRendererColor(edge.GetComponent<Renderer>(), new Color(0.38f, 0.28f, 0.24f), 0.15f);
        edge.GetComponent<Renderer>().enabled = false;
        Object.DestroyImmediate(edge.GetComponent<Collider>());
    }

    /// <summary>屋根上の雪サーフェスメッシュ。OneHouse では表示してプレイ面を明確に。</summary>
    static void CreateRoofSnowSurface(Transform roofPanel, float houseW, float houseD, bool visible = false)
    {
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "RoofSnowSurface";
        quad.transform.SetParent(roofPanel, false);
        quad.transform.localPosition = new Vector3(0f, 0.07f, 0f);
        quad.transform.localRotation = Quaternion.Euler(22f, 0f, 0f);
        quad.transform.localScale = new Vector3(houseW * 0.98f, houseD * 1.05f, 1f);
        SetRoofSnowSurfaceMaterial(quad.GetComponent<Renderer>());
        quad.GetComponent<Renderer>().enabled = visible;
        Object.DestroyImmediate(quad.GetComponent<Collider>());
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

    static void CreateFarmVillageBackdrop()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/FarmVillageReference.png");
        if (tex == null) return;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "FarmVillageBackdrop";
        quad.transform.position = new Vector3(0f, 1.5f, 18f);
        quad.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        quad.transform.localScale = new Vector3(28f, 20f, 1f);

        var col = quad.GetComponent<Collider>();
        if (col != null) Object.DestroyImmediate(col);

        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Texture");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetTexture("_BaseMap", tex);
            quad.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    static void Set俯瞰Camera(Camera cam)
    {
        Vector3 lookAt = new Vector3(0f, 1.5f, -0.5f);
        cam.transform.position = new Vector3(0f, 8f, -8f);
        cam.transform.LookAt(lookAt);
        var orbit = cam.GetComponent<CameraOrbit>();
        if (orbit != null) { orbit._yaw = 180f; orbit._pitch = 39f; orbit.distance = 12f; }
    }

    /// <summary>屋根がプレイフィールド・主役。地面ほぼ見えない低い俯瞰。試作カメラ基準。pre_camera_change_good_state用。</summary>
    static void SetRoofCenterCamera(Camera cam)
    {
        cam.transform.position = new Vector3(0f, 5.2f, -5.8f);
        cam.transform.rotation = Quaternion.Euler(36f, 0f, 0f);
        cam.fieldOfView = 45f;
        var orbit = cam.GetComponent<CameraOrbit>();
        if (orbit != null) { orbit._yaw = 180f; orbit._pitch = 36f; orbit.distance = 6.6f; orbit.yMin = 4f; orbit.yMax = 8f; }
    }

    /// <summary>爽快パズル視点。屋根・落雪・地面衝突が見える。Position(-6,4,-6) Rotation(25,45,0)。</summary>
    static void SetDioramaCamera(Camera cam)
    {
        cam.transform.position = new Vector3(-6f, 4f, -6f);
        cam.transform.rotation = Quaternion.Euler(25f, 45f, 0f);
        cam.fieldOfView = 45f;
        var orbit = cam.GetComponent<CameraOrbit>();
        if (orbit != null) { orbit._yaw = 45f; orbit._pitch = 25f; orbit.distance = 10f; orbit.yMin = 3f; orbit.yMax = 8f; }
    }

    static void SetupDioramaVolume()
    {
        var existing = GameObject.Find("DioramaVolume");
        if (existing != null) Object.DestroyImmediate(existing);

        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/DioramaVolumeProfile.asset")
            ?? AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
        if (profile == null) return;

        var volGo = new GameObject("DioramaVolume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.profile = profile;
        vol.priority = 1;
    }

    static void CreateDistantTrees(Transform root)
    {
        var trees = new GameObject("DistantTrees");
        trees.transform.SetParent(root, false);
        var treeModels = new[]
        {
            "Assets/Art/Kenney/Nature/tree_pineTallA.fbx",
            "Assets/Art/Kenney/Nature/tree_pineTallB.fbx",
            "Assets/Art/Kenney/Nature/tree_pineTallC.fbx",
            "Assets/Art/Kenney/Nature/tree_pineTallD.fbx",
            "Assets/Art/Kenney/Nature/tree_small.fbx",
            "Assets/Art/Kenney/Nature/tree_tall.fbx",
        };
        var rockModels = new[]
        {
            "Assets/Art/Kenney/Nature/rock_largeA.fbx",
            "Assets/Art/Kenney/Nature/rock_largeB.fbx",
            "Assets/Art/Kenney/Nature/rock_tallA.fbx",
            "Assets/Art/Kenney/Nature/rock_tallB.fbx",
        };

        bool placedAny = false;
        float[] treeX = { -18f, -13f, -9f, 9f, 13f, 18f };
        for (int i = 0; i < treeX.Length; i++)
        {
            var model = LoadFirstKenneyModel(treeModels, i);
            if (model == null) continue;
            var obj = InstantiateKenneyModel(model, trees.transform, "KenneyTree_" + i);
            obj.transform.position = new Vector3(treeX[i], 0f, -19f + (i % 2) * 1.2f);
            obj.transform.rotation = Quaternion.Euler(0f, (i % 2) * 12f - 6f, 0f);
            obj.transform.localScale = Vector3.one * 1.6f;
            RemoveAllColliders(obj);
            placedAny = true;
        }

        float[] rockX = { -16f, -10f, 10f, 16f };
        for (int i = 0; i < rockX.Length; i++)
        {
            var model = LoadFirstKenneyModel(rockModels, i);
            if (model == null) continue;
            var obj = InstantiateKenneyModel(model, trees.transform, "KenneyRock_" + i);
            obj.transform.position = new Vector3(rockX[i], 0f, -15f + (i % 2) * 0.8f);
            obj.transform.rotation = Quaternion.Euler(0f, i * 17f, 0f);
            obj.transform.localScale = Vector3.one * 1.35f;
            RemoveAllColliders(obj);
            placedAny = true;
        }

        // Nature Kit が未検出時のみ簡易フォールバック
        if (!placedAny)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.25f, 0.4f, 0.28f));
            float[] xz = { -18f, -12f, 12f, 18f, -15f, 0f, 15f };
            for (int i = 0; i < xz.Length; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = "Tree_" + i;
                quad.transform.SetParent(trees.transform, false);
                quad.transform.position = new Vector3(xz[i], 4f, -18f);
                quad.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                quad.transform.localScale = new Vector3(3f, 5f, 1f);
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }
    }

    static void CreateKenneyHolidayHouseVisual(Transform houseRoot, bool skipRoof = false)
    {
        if (houseRoot == null) return;

        var visual = new GameObject("KenneyHouseVisual");
        visual.transform.SetParent(houseRoot, false);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one;

        var doorway = LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-doorway.fbx")
            ?? LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-doorway-center.fbx");
        var wall = LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-wall.fbx");
        var roof = LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-roof-snow.fbx")
            ?? LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-roof.fbx");
        var roofPoint = LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-roof-snow-point.fbx")
            ?? LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-roof-point.fbx");
        var corner = LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-corner.fbx")
            ?? LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-corner-logs.fbx");
        var fallbackWhole = LoadKenneyModel("Assets/Art/Kenney/Holiday/cabin-wall-roof-center.fbx");

        bool placed = false;
        if (doorway != null) { PlaceKenneyPart(doorway, visual.transform, "Doorway", new Vector3(0f, 0f, -0.5f), Vector3.zero); placed = true; }
        if (wall != null)
        {
            PlaceKenneyPart(wall, visual.transform, "Wall_Back", new Vector3(0f, 0f, 0.5f), new Vector3(0f, 180f, 0f));
            PlaceKenneyPart(wall, visual.transform, "Wall_Left", new Vector3(-0.5f, 0f, 0f), new Vector3(0f, 90f, 0f));
            PlaceKenneyPart(wall, visual.transform, "Wall_Right", new Vector3(0.5f, 0f, 0f), new Vector3(0f, -90f, 0f));
            placed = true;
        }
        if (corner != null)
        {
            PlaceKenneyPart(corner, visual.transform, "Corner_FL", new Vector3(-0.5f, 0f, -0.5f), Vector3.zero);
            PlaceKenneyPart(corner, visual.transform, "Corner_FR", new Vector3(0.5f, 0f, -0.5f), new Vector3(0f, 90f, 0f));
            PlaceKenneyPart(corner, visual.transform, "Corner_BL", new Vector3(-0.5f, 0f, 0.5f), new Vector3(0f, -90f, 0f));
            PlaceKenneyPart(corner, visual.transform, "Corner_BR", new Vector3(0.5f, 0f, 0.5f), new Vector3(0f, 180f, 0f));
            placed = true;
        }
        if (!skipRoof && roof != null)
        {
            PlaceKenneyPart(roof, visual.transform, "Roof_A", new Vector3(0f, 0.72f, 0f), Vector3.zero);
            PlaceKenneyPart(roof, visual.transform, "Roof_B", new Vector3(0f, 0.72f, 0f), new Vector3(0f, 180f, 0f));
            placed = true;
        }
        if (!skipRoof && roofPoint != null) { PlaceKenneyPart(roofPoint, visual.transform, "Roof_Point", new Vector3(0f, 0.9f, 0f), Vector3.zero); placed = true; }

        if (!placed && fallbackWhole != null)
            PlaceKenneyPart(fallbackWhole, visual.transform, "Cabin_Whole", Vector3.zero, Vector3.zero);

        visual.transform.localScale = Vector3.one * 1.25f;
        SnapRootToGround(visual);
    }

    static void PlaceKenneyPart(GameObject model, Transform parent, string name, Vector3 localPos, Vector3 localEuler)
    {
        if (model == null || parent == null) return;
        var obj = InstantiateKenneyModel(model, parent, name);
        obj.transform.localPosition = localPos;
        obj.transform.localRotation = Quaternion.Euler(localEuler);
        obj.transform.localScale = Vector3.one;
        RemoveAllColliders(obj);
    }

    static GameObject InstantiateKenneyModel(GameObject model, Transform parent, string name)
    {
        GameObject obj = PrefabUtility.InstantiatePrefab(model) as GameObject;
        if (obj == null) obj = Object.Instantiate(model);
        obj.name = name;
        obj.transform.SetParent(parent, false);
        return obj;
    }

    static GameObject LoadKenneyModel(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        return AssetDatabase.LoadAssetAtPath<GameObject>(path);
    }

    static GameObject LoadFirstKenneyModel(string[] paths, int seed)
    {
        if (paths == null || paths.Length == 0) return null;
        for (int i = 0; i < paths.Length; i++)
        {
            int idx = (seed + i) % paths.Length;
            var m = LoadKenneyModel(paths[idx]);
            if (m != null) return m;
        }
        return null;
    }

    static void RemoveAllColliders(GameObject root)
    {
        if (root == null) return;
        foreach (var c in root.GetComponentsInChildren<Collider>(true))
            Object.DestroyImmediate(c);
    }

    static void SnapRootToGround(GameObject root)
    {
        if (root == null) return;
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0) return;
        var b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        var p = root.transform.position;
        root.transform.position = new Vector3(p.x, p.y - b.min.y, p.z);
    }

    // Kenney マテリアルのフラット感を抑える軽い色補正（レイアウト/形状は変更しない）
    static void SoftenKenneySceneMaterials()
    {
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (r == null) continue;
            if (r.name.Contains("SnowTest") || r.name.Contains("Plank")) continue;
            var mats = r.sharedMaterials;
            bool changedAny = false;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                if (!src.HasProperty("_BaseColor") && !src.HasProperty("_Color")) continue;
                var key = src.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                var c = src.GetColor(key);
                var adjusted = AdjustStorybookPalette(c);
                var inst = new Material(src);
                inst.SetColor(key, adjusted);
                // Kenney 雪マテリアルは艶を抑えて絵本寄りのマット質感にする
                var matName = src.name.ToLowerInvariant();
                if (matName.Contains("snow"))
                {
                    inst.SetColor(key, new Color(0.91f, 0.92f, 0.94f, c.a));
                    if (inst.HasProperty("_Smoothness")) inst.SetFloat("_Smoothness", 0.08f);
                    if (inst.HasProperty("_Metallic")) inst.SetFloat("_Metallic", 0f);
                }
                if (adjusted == c && !matName.Contains("snow")) continue;
                mats[i] = inst;
                changedAny = true;
            }
            if (changedAny) r.sharedMaterials = mats;
        }
    }

    static Color AdjustStorybookPalette(Color c)
    {
        // Pure white -> off-white
        if (c.r > 0.96f && c.g > 0.96f && c.b > 0.96f)
            return new Color(0.93f, 0.95f, 0.97f, c.a);

        // Brown -> slightly warmer
        if (c.r > c.g && c.g > c.b && c.r > 0.35f)
            return new Color(Mathf.Min(1f, c.r + 0.02f), Mathf.Min(1f, c.g + 0.01f), Mathf.Max(0f, c.b - 0.01f), c.a);

        // Green -> slightly cooler
        if (c.g > c.r && c.g > c.b)
            return new Color(Mathf.Max(0f, c.r - 0.01f), Mathf.Max(0f, c.g - 0.015f), Mathf.Min(1f, c.b + 0.02f), c.a);

        return c;
    }
}

