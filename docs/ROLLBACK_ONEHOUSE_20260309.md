# [Snow Panic / 1ファイル依頼] rollback_target=pre_camera_change_good_state 復元

## 変更ファイル一覧

---

### file path: Assets/Scripts/Editor/CorniceSceneSetup.cs

**置換（OneHouse 時は Self Test 中も Setup 実行）**
```csharp
// 置換前
    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode) return;
        if (VideoPipelineSelfTestMode.IsActive) return;
        if (EditorPrefs.GetBool(PrefAutoSetup, true))
            SetupAll();
        FixAllMissingScriptsAndSave();
    }

// 置換後
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
```

---

### file path: Assets/Scripts/CorniceRuntimeSnowSetup.cs

**置換1（CreateRoofSnowSystems 先頭：OneHouse ロールバック）**
- シーン名に OneHouse を含むかつ家が2軒以上なら、House_1〜House_7 を DestroyImmediate
- カメラが (-6,4,-6)(25,45,0) でなければ爽快パズル視点にリセット
- rollback_applied フラグを設定

**置換2（ログ出力に house_count, camera_position, camera_rotation, rollback_applied 追加）**
```
[CORNICE_SCENE_CHECK] scene=X house_count=N camera_position=(x,y,z) camera_rotation=(x,y,z) rollback_applied=true/false ...
[SNOW_ROLLBACK_CHECK] rollback_target=pre_camera_change_good_state house_count=N camera_position=... camera_rotation=... rollback_applied=... ...
```

---

### file path: Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs

**置換（CORNICE SCENE CHECK 抽出キー追加）**
```csharp
// 置換前
            if (key == "scene" || key == "house_count" || key == "spawn_system" || key == "spawn_reason" || key == "one_house_forced" || key == "is_expected")

// 置換後
            if (key == "scene" || key == "house_count" || key == "camera_position" || key == "camera_rotation" || key == "rollback_applied" || key == "spawn_system" || key == "spawn_reason" || key == "one_house_forced" || key == "is_expected")
```

---

## 変更理由

- **現状**: 家8軒・カメラ斜め・雪挙動崩れ
- **目的**: rollback_target=pre_camera_change_good_state を復元
- **方針**: OneHouse シーンでは Self Test 中も Setup 実行、Runtime で 8軒→1軒＋カメラリセットを強制

---

## 想定ログ例

**OneHouse + Self Test 実行後**
```
[CORNICE_SCENE_CHECK] scene=Avalanche_Test_OneHouse house_count=1 camera_position=(-6.00,4.00,-6.00) camera_rotation=(25.0,45.0,0.0) rollback_applied=true spawn_system=CorniceRuntime one_house_forced=True is_expected=True
[CAMERA_LOCK_CHECK] camPos=(-6.00,4.00,-6.00) camEuler=(25.0,45.0,0.0) result=ROLLBACK_APPLIED target=(-6,4,-6)(25,45,0)
[SNOW_ROLLBACK_CHECK] rollback_target=pre_camera_change_good_state house_count=1 camera_position=(-6.00,4.00,-6.00) camera_rotation=(25.0,45.0,0.0) rollback_applied=true RoofSnow_debugMode=false ... result=OK
```

---

## 確認手順

1. Avalanche_Test_OneHouse を開く
2. **SnowPanic → Self Test** を実行
3. 家が1軒だけ表示されることを確認
4. カメラが爽快パズル視点（Position -6,4,-6 Rotation 25,45,0）であることを確認
5. 停止 → ASSI レポートに `house_count=1 rollback_applied=true` が出ることを確認
6. gif 録画がそのまま成功することを確認

---

## 成功条件

- 家が1軒だけ表示される
- カメラが元の試作視点（爽快パズル）
- gif 録画はそのまま成功
