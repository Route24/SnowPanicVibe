# [Snow Panic / 1ファイル依頼] Avalanche_Test_OneHouse を 1ハウス化

## 変更ファイル一覧

---

### file path: Assets/Scripts/Editor/CorniceSceneSetup.cs

**置換1（既存オブジェクト掃除に OneHouseMarker 追加）**
```csharp
// 置換前
        var namesToDelete = new List<string> { "CorniceRoot", "Ground", "HouseBody", "Roof", "RoofBase", "RoofPanel", "EavesDropTrigger", "CorniceSnow", "Person", "Window", "Porch", "SnowParticle", "GroundSnow", "RoofSnow", "RoofSnowPlaceholder", "RidgeSnow", "GroundDecor", "FarmVillageBackdrop", "Houses", "DioramaVolume", "DistantTrees" };

// 置換後
        var namesToDelete = new List<string> { "CorniceRoot", "Ground", "HouseBody", "Roof", "RoofBase", "RoofPanel", "EavesDropTrigger", "CorniceSnow", "Person", "Window", "Porch", "SnowParticle", "GroundSnow", "RoofSnow", "RoofSnowPlaceholder", "RidgeSnow", "GroundDecor", "FarmVillageBackdrop", "Houses", "DioramaVolume", "DistantTrees", "OneHouseMarker" };
```

**置換2（家生成ループを OneHouse 時は 1軒に）**
```csharp
// 置換前
        // 8軒の家を 2行 x 4列 で配置（片流れ屋根、同じ向き）
        var housesRoot = new GameObject("Houses");
        housesRoot.transform.SetParent(root.transform, false);
        housesRoot.transform.localPosition = Vector3.zero;

        float houseW = 2f;
        float houseD = 2f;
        float houseH = 1.2f;
        float spacingX = 3.2f;
        float spacingZ = 3.5f;
        float offsetX = -spacingX * 1.5f;
        float offsetZ = spacingZ * 0.5f;
        var slideDir = new Vector3(0f, -0.42f, -0.9f).normalized;

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 4; col++)
            {
                int idx = row * 4 + col;
                float px = offsetX + col * spacingX;
                float pz = offsetZ - row * spacingZ;

// 置換後
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
```

**置換3（Setup 完了ログ）**
```csharp
// 置換前
        Selection.activeGameObject = root;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Setup complete. Play モードで屋根の雪が表示されます。屋根の雪をクリックで雪落とし。");

// 置換後
        Selection.activeGameObject = root;
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log($"[CORNICE_SETUP] scene={sceneName} house_count={houseCount} spawn_system=CorniceSetup spawn_reason=SetupCorniceScene one_house_forced={oneHouseForced}");
        Debug.Log("Setup complete. Play モードで屋根の雪が表示されます。屋根の雪をクリックで雪落とし。");
```

---

### file path: Assets/Scripts/CorniceRuntimeSnowSetup.cs

**置換（CORNICE_SCENE_CHECK ログに one_house_forced 追加）**
```csharp
// 置換前
        int houseCount = panels.Count;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
        bool isExpected = houseCount >= 1 && houseCount <= 8;
        Debug.Log($"[CORNICE_SCENE_CHECK] scene={scene} house_count={houseCount} spawn_system=CorniceRuntime spawn_reason=SetupCorniceScene is_expected={isExpected}");

// 置換後
        int houseCount = panels.Count;
        string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name ?? "";
        bool oneHouseForced = transform.Find("OneHouseMarker") != null;
        bool isExpected = houseCount >= 1 && houseCount <= 8;
        Debug.Log($"[CORNICE_SCENE_CHECK] scene={scene} house_count={houseCount} spawn_system=CorniceRuntime spawn_reason=SetupCorniceScene one_house_forced={oneHouseForced} is_expected={isExpected}");
```

---

### file path: Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs

**置換（ASSI CORNICE SCENE CHECK に one_house_forced を解析対象に追加）**
```csharp
// 置換前
            if (key == "scene" || key == "house_count" || key == "spawn_system" || key == "spawn_reason" || key == "is_expected")

// 置換後
            if (key == "scene" || key == "house_count" || key == "spawn_system" || key == "spawn_reason" || key == "one_house_forced" || key == "is_expected")
```

---

## 変更理由

- **原因**: `SetupCorniceScene` が常に `2行×4列=8軒` を生成していた
- **対応**: シーン名に `"OneHouse"` を含む場合のみ `house_count=1` に変更
- **運用**: Avalanche_Test_OneHouse を開いた状態で Setup Cornice Scene を実行すると 1軒のみ生成

---

## 期待ログ

**Edit 時（Setup Cornice Scene 実行後）**
```
[CORNICE_SETUP] scene=Avalanche_Test_OneHouse house_count=1 spawn_system=CorniceSetup spawn_reason=SetupCorniceScene one_house_forced=True
```

**Play 時（CorniceRuntimeSnowSetup 起動後）**
```
[CORNICE_SCENE_CHECK] scene=Avalanche_Test_OneHouse house_count=1 spawn_system=CorniceRuntime spawn_reason=SetupCorniceScene one_house_forced=True is_expected=True
```

---

## 再生確認手順

1. Avalanche_Test_OneHouse を開く
2. メニューで **SnowPanicVibe → Setup Cornice Scene** を実行
3. Play して 1軒だけ表示されていることを確認
4. 屋根をクリックし、雪が塊で落ちることを確認
5. 停止 → ASSI レポートに `house_count=1 one_house_forced=True` が出ていることを確認
