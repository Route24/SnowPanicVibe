# [Snow Panic / ロールバック修正] pre_camera_change_good_state 復元

## 変更ファイル一覧

---

### file path: Assets/Scripts/CorniceRuntimeSnowSetup.cs

**置換1（OneHouse 時 CreateGroundSnow/CreateSnowParticle をスキップ）**
```csharp
// OneHouse では地面雪・降雪パーティクルを生成しない（地面雪無し）
if (!VideoPipelineSelfTestMode.IsActive && !isOneHouseScene)
{
    CreateGroundSnow();
    CreateSnowParticle();
}
```

**置換2（カメラを屋根中心視点に変更）**
- 変更前: Position(-6,4,-6) Rotation(25,45,0) 斜め視点
- 変更後: Position(0,2.8,-5.5) Rotation(15,0,0) 屋根にほぼ正対（少しだけ斜め）
- Orbit: _yaw=180, _pitch=15, distance=5.6

**置換3（ログ更新）**
- target=(0,2.8,-5.5)(15,0,0)_roof_center
- ground_snow=disabled

---

### file path: Assets/Scripts/Editor/CorniceSceneSetup.cs

**置換1（OneHouse 時 SetRoofCenterCamera を使用）**
```csharp
if (oneHouseForced)
    SetRoofCenterCamera(cam);
else
    SetDioramaCamera(cam);
```

**置換2（SetRoofCenterCamera 追加）**
```csharp
static void SetRoofCenterCamera(Camera cam)
{
    cam.transform.position = new Vector3(0f, 2.8f, -5.5f);
    cam.transform.rotation = Quaternion.Euler(15f, 0f, 0f);
    cam.fieldOfView = 45f;
    var orbit = cam.GetComponent<CameraOrbit>();
    if (orbit != null) { orbit._yaw = 180f; orbit._pitch = 15f; orbit.distance = 5.6f; orbit.yMin = 2f; orbit.yMax = 6f; }
}
```

---

### file path: Assets/Scripts/GroundSnowAccumulator.cs

**置換（OneHouse では bootstrap スキップ）**
```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
static void EnsureBootstrap()
{
    if (FindFirstObjectByType<GroundSnowAccumulator>() != null) return;
    var scene = SceneManager.GetActiveScene();
    if (!string.IsNullOrEmpty(scene.name) && scene.name.Contains("OneHouse")) return; // pre_camera_change: 地面雪無し
    // ...
}
```

---

## 理想状態

| 項目 | 内容 |
|------|------|
| 家 | 1軒 |
| カメラ | 屋根にほぼ正対（少しだけ斜め）(0,2.8,-5.5)(15,0,0) |
| 雪 | 屋根中心 |
| 地面雪 | 無し |

---

## 確認手順

1. Avalanche_Test_OneHouse を開く
2. SnowPanic → Self Test を実行
3. 家が1軒、カメラが屋根中心、地面に雪粒がないことを確認
4. 停止 → ASSI レポートで house_count=1 rollback_applied=true を確認
5. gif 録画が成功することを確認

---

## 成功条件

- 家が1軒
- 屋根中心カメラ
- 地面雪無し
- gif 録画成功
