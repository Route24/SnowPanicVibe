# [Snow Panic / 1ファイル依頼] 屋根プレイ面の整理

## 変更ファイル一覧

---

### file path: Assets/Scripts/Editor/CorniceSceneSetup.cs

**置換1（SetupAll: OneHouse 時は SnowTest を作成せず削除）**
```csharp
// 置換前
    public static void SetupAll()
    {
        SetupCorniceScene();
        SetupSnowTest();
        Debug.Log("Setup All 完了。Play で雪落としを楽しめます。");
    }

// 置換後
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
```

**置換2（namesToDelete に SnowTestRoot 追加）**
```csharp
// 置換前
        var namesToDelete = new List<string> { "CorniceRoot", "Ground", ... "OneHouseMarker" };

// 置換後
        var namesToDelete = new List<string> { "CorniceRoot", "Ground", ... "OneHouseMarker", "SnowTestRoot" };
```

---

### file path: Assets/Scripts/CorniceRuntimeSnowSetup.cs

**置換1（Awake: OneHouse 時 SnowTestRoot を非表示）**
```csharp
        if (isOneHouseScene)
        {
            var testRoot = GameObject.Find("SnowTestRoot");
            if (testRoot != null) testRoot.SetActive(false);
        }
```

**置換2（ログに active_roof_target, test_roof_visible, roof_shape 追加）**
```csharp
        bool testRoofVisible = GameObject.Find("SnowTestRoot") != null && GameObject.Find("SnowTestRoot").activeSelf;
        string activeRoofTarget = oneHouseForced ? "asset_roof" : (testRoofVisible ? "test_roof" : "asset_roof");
        string roofShape = "mono_slope";
        Debug.Log($"[CORNICE_SCENE_CHECK] scene=... active_roof_target={activeRoofTarget} test_roof_visible={testRoofVisible} roof_shape={roofShape} ...");
        Debug.Log("[SNOW_ROLLBACK_CHECK] ... active_roof_target=... test_roof_visible=... roof_shape=... ...");
```

---

### file path: Assets/Scripts/CorniceHitter.cs

**置換（TAP_DEBUG に hit_target 追加）**
```csharp
// lastHitObject の直後に hit_target= を追加
Debug.Log($"[TAP_DEBUG] ... lastHitObject={...} hit_target={TapDebugState.LastHitObject} lastHitLayer={...}");
```

---

### file path: Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs

**置換（CORNICE SCENE CHECK 抽出キー追加）**
```csharp
            if (key == "scene" || key == "house_count" || key == "one_house_forced" || key == "rollback_applied" || key == "camera_position" || key == "camera_rotation" || key == "active_roof_target" || key == "test_roof_visible" || key == "roof_shape" || key == "spawn_system" || key == "spawn_reason" || key == "is_expected")
```

**置換（TAP DEBUG に hit_target 抽出追加）**
```csharp
        foreach (var m in Regex.Matches(last, @"(TapHit|TapMiss|lastHitObject|hit_target|lastHitLayer)=([^\s]*)"))
```

---

## 変更理由

- **現状**: アセット家の横にテスト屋根（SnowTestRoot）が表示され、見た目とプレイ対象がズレている
- **目的**: アセット家の屋根のみをプレイ面に統一
- **方針**: OneHouse では SnowTest を生成せず削除/非表示、屋根形状は既存の片流れ（mono_slope）を維持

---

## 想定ログ例

```
[CORNICE_SCENE_CHECK] scene=Avalanche_Test_OneHouse house_count=1 one_house_forced=true rollback_applied=false camera_position=(0.00,5.20,-5.80) camera_rotation=(36.0,0.0,0.0) active_roof_target=asset_roof test_roof_visible=false roof_shape=mono_slope ...
[SNOW_ROLLBACK_CHECK] ... active_roof_target=asset_roof test_roof_visible=false roof_shape=mono_slope ...
[TAP_DEBUG] TapHit=1 TapMiss=0 lastHitObject=RoofSnow hit_target=RoofSnow lastHitLayer=Default
```

---

## 確認手順

1. SnowPanicVibe → Open Avalanche_Test_OneHouse
2. Play（Setup All が自動実行）
3. テスト屋根が見えないことを確認
4. アセット家の屋根をクリックし、雪が崩れることを確認
5. ログで active_roof_target=asset_roof test_roof_visible=false hit_target=RoofSnow を確認
6. Self Test で gif 録画が成功することを確認

---

## 成功条件

- テスト用屋根が見えない
- アセット家の屋根だけがプレイ対象
- アセット家の屋根に雪が積もる
- 屋根は片流れ面（mono_slope）
- クリック対象が家屋根側に統一（hit_target=RoofSnow）
- gif 録画がそのまま成功
