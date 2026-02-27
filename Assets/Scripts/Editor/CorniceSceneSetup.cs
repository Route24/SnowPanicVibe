using UnityEditor;
using UnityEngine;

public static class CorniceSceneSetup
{
    [MenuItem("SnowPanicVibe/Setup Cornice Scene")]
    public static void SetupCorniceScene()
    {
        // 既存オブジェクトを掃除
        string[] namesToDelete = { "CorniceRoot", "Ground", "HouseBody", "Roof", "RoofBase", "CorniceSnow", "Person", "Window", "Porch", "SnowParticle", "GroundSnow", "RoofSnow", "GroundDecor" };
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

        // 地面（雪で覆われた感じ：白っぽく）
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.SetParent(root.transform, false);
        ground.transform.position = Vector3.zero;
        ground.transform.localScale = new Vector3(3f, 1f, 3f);
        ground.GetComponent<Renderer>().material.color = new Color(0.9f, 0.92f, 0.95f);
        ground.GetComponent<Collider>().material = new PhysicsMaterial("Ground") { bounciness = 0f };

        // 地面の積雪（約50cm、パーティクルで凸凹を表現）
        var groundSnowObj = new GameObject("GroundSnow");
        groundSnowObj.transform.SetParent(root.transform, false);
        groundSnowObj.transform.position = new Vector3(0f, 0.25f, 0f); // 厚み50cmの中心
        var groundSnowPs = groundSnowObj.AddComponent<ParticleSystem>();
        var gsMain = groundSnowPs.main;
        gsMain.loop = false;
        gsMain.startLifetime = 9999f;
        gsMain.startSpeed = 0f;
        gsMain.startSize = new ParticleSystem.MinMaxCurve(0.0375f, 0.07f); // 4分の1に
        gsMain.startColor = new Color(0.82f, 0.85f, 0.9f, 0.95f); // 地面の雪：グレー寄りで差別化
        gsMain.maxParticles = 30000;
        gsMain.simulationSpace = ParticleSystemSimulationSpace.World;
        gsMain.gravityModifier = 0f;
        gsMain.playOnAwake = true;
        var gsEmission = groundSnowPs.emission;
        gsEmission.enabled = true;
        gsEmission.rateOverTime = 0f;
        gsEmission.SetBursts(new[] { new ParticleSystem.Burst(0f, 25000) }); // 5倍
        var gsShape = groundSnowPs.shape;
        gsShape.shapeType = ParticleSystemShapeType.Box;
        gsShape.scale = new Vector3(8f, 0.5f, 8f); // 厚み50cm、体積内にランダム配置
        var gsRenderer = groundSnowPs.GetComponent<ParticleSystemRenderer>();
        if (gsRenderer != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                gsRenderer.material = new Material(shader) { color = Color.white };
                gsRenderer.renderMode = ParticleSystemRenderMode.Billboard;
                gsRenderer.minParticleSize = 0.01f;
            }
        }
        groundSnowPs.Simulate(0.5f, true, true);

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
            rock.GetComponent<Renderer>().material.color = new Color(0.4f, 0.38f, 0.35f);
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
            grass.GetComponent<Renderer>().material.color = new Color(0.25f, 0.35f, 0.2f);
        }

        // 小さな家本体（木造・茶色っぽい）
        var house = GameObject.CreatePrimitive(PrimitiveType.Cube);
        house.name = "HouseBody";
        house.transform.SetParent(root.transform, false);
        house.transform.position = new Vector3(0f, 0.75f, 0f);
        house.transform.localScale = new Vector3(2f, 1.5f, 2f);
        house.GetComponent<Renderer>().material.color = new Color(0.55f, 0.4f, 0.3f); // 木の茶色

        // 三角屋根（切妻）△型：中央が尖り、左右に下る
        var roof = new GameObject("Roof");
        roof.transform.SetParent(root.transform, false);
        // 屋根の下端（軒）が家の天井（Y=1.5）にぴったり付くように
        roof.transform.position = new Vector3(0f, 1.5f, 0f);

        // 左右の屋根：中央（棟）でしっかり重なるように配置
        var roofLeft = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofLeft.name = "RoofLeft";
        roofLeft.transform.SetParent(roof.transform, false);
        roofLeft.transform.localPosition = new Vector3(-0.5f, 0.3f, 0f);
        roofLeft.transform.localScale = new Vector3(1.35f, 0.05f, 2.02f);
        roofLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
        roofLeft.GetComponent<Renderer>().material.color = new Color(0.35f, 0.32f, 0.3f);
        var roofSlideMat = new PhysicsMaterial("RoofSlide") { dynamicFriction = 0.05f, staticFriction = 0.08f, bounciness = 0f };
        roofLeft.GetComponent<Collider>().material = roofSlideMat;

        var roofRight = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roofRight.name = "RoofRight";
        roofRight.transform.SetParent(roof.transform, false);
        roofRight.transform.localPosition = new Vector3(0.5f, 0.3f, 0f);
        roofRight.transform.localScale = new Vector3(1.35f, 0.05f, 2.02f);
        roofRight.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
        roofRight.GetComponent<Renderer>().material.color = new Color(0.35f, 0.32f, 0.3f);
        roofRight.GetComponent<Collider>().material = roofSlideMat;

        // 屋根の積雪：こんもり厚く積もった雪。クリックで塊が滑り落ちる
        CreateRoofSnow(roofLeft.transform, new Vector3(-0.82f, -0.57f, 0f));
        CreateRoofSnow(roofRight.transform, new Vector3(0.82f, -0.57f, 0f));

        // ポーチ（正面の玄関先）
        var porch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        porch.name = "Porch";
        porch.transform.SetParent(root.transform, false);
        porch.transform.position = new Vector3(0f, 0.08f, -1.15f);
        porch.transform.localScale = new Vector3(2.2f, 0.08f, 0.4f);
        porch.GetComponent<Renderer>().material.color = new Color(0.45f, 0.35f, 0.25f); // 木のポーチ

        // 窓（正面＝カメラ側、暗めで窓らしく）
        var window = GameObject.CreatePrimitive(PrimitiveType.Cube);
        window.name = "Window";
        window.transform.SetParent(house.transform, false);
        window.transform.localPosition = new Vector3(0f, 0.2f, -0.51f);
        window.transform.localScale = new Vector3(0.6f, 0.5f, 0.02f);
        var windowRend = window.GetComponent<Renderer>();
        if (windowRend != null)
            windowRend.material.color = new Color(0.2f, 0.35f, 0.55f); // 曇った窓

        // 降雪パーティクル（舞い上がらず自然に落下）
        var snowObj = new GameObject("SnowParticle");
        snowObj.transform.SetParent(root.transform, false);
        snowObj.transform.position = new Vector3(0f, 6f, 0f);
        var ps = snowObj.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true;
        main.startLifetime = 5f;
        main.startSpeed = 0.4f;
        main.startSize = 0.02f; // 均一サイズで舞い上がり防止
        main.startColor = new Color(1f, 1f, 1f, 0.7f);
        main.maxParticles = 1500;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0.2f; // しっかり下へ
        var emission = ps.emission;
        emission.rateOverTime = 300f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(15f, 0f, 15f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.02f; // ごくわずかな揺れ
        noise.frequency = 0.2f;
        noise.scrollSpeed = 0.05f;
        noise.damping = true;
        var psRenderer = ps.GetComponent<ParticleSystemRenderer>();
        if (psRenderer != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
            {
                var mat = new Material(shader);
                mat.color = Color.white;
                psRenderer.material = mat;
            }
        }

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

        // 斜め上からの俯瞰視点、家を正面から見るビュー
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

    static RoofSnow CreateRoofSnow(Transform roofPanel, Vector3 slideDir)
    {
        float panelW = 1.35f, panelD = 2.02f;
        float panelH = 0.05f;
        float snowThick = 0.14f; // 厚み増：豪雪地帯で家が潰れる緊迫感
        float topY = 0.025f + snowThick * 0.5f;
        float thickScale = snowThick / panelH;

        var go = new GameObject("RoofSnow");
        go.transform.SetParent(roofPanel, false);
        go.transform.localPosition = new Vector3(0f, topY, 0f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.startLifetime = 9999f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.028f, 0.05f);
        main.startColor = new Color(0.96f, 0.98f, 1f, 0.97f); // 屋根が透けないようにほぼ不透明
        main.maxParticles = 28000; // 2倍で豪雪地帯の重厚感
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy; // 親のscale(1.35x2.02)を継承して屋根と同面積に
        main.playOnAwake = true;
        var em = ps.emission;
        em.enabled = true;
        em.rateOverTime = 0f;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 26000) }); // 2倍：密に積もった豪雪
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        // 屋根パネルと同じ 1x1 ローカル範囲（Hierarchyで親scale 1.35x2.02が適用され屋根と同面積に）
        shape.scale = new Vector3(1f, thickScale * 0.6f, 1f);
        var rend = ps.GetComponent<ParticleSystemRenderer>();
        if (rend != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            if (shader != null)
                rend.material = new Material(shader) { color = Color.white };
            rend.renderMode = ParticleSystemRenderMode.Billboard;
        }
        ps.Simulate(0.1f, true, true);

        var col = go.AddComponent<BoxCollider>();
        col.size = new Vector3(1f, thickScale * 1.2f, 1f); // 屋根と同面積
        col.center = Vector3.zero;
        col.isTrigger = false;

        var comp = go.AddComponent<RoofSnow>();
        comp.snowParticles = ps;
        comp.slideDownDirection = slideDir;
        return comp;
    }

    static void Set俯瞰Camera(Camera cam)
    {
        // 家の正面（-Z側）から斜め上に見下ろす俯瞰ビュー
        // 家中心(0,1,0)、正面ファサード中央あたりを注視
        Vector3 lookAt = new Vector3(0f, 1.5f, -0.5f);
        cam.transform.position = new Vector3(0f, 8f, -8f);
        cam.transform.LookAt(lookAt);
    }
}

