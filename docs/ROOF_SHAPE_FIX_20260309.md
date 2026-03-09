# [Snow Panic / 1ファイル依頼] 屋根形状の修正

## 実装サマリ

- A) 屋根を手前に傾く素直な片流れに統一（OneHouse: RoofPanel 0.02f、Kenney 屋根非表示）
- B) roof_slope_direction=front をログ・ASSIレポートに追加
- C) RoofSnowSurface を OneHouse で表示してプレイ面を明確化

---

## 変更ファイル一覧（実施済み）

以下が適用済みの変更です。差分参照用。

---

### file path: `Assets/Scripts/Editor/CorniceSceneSetup.cs`

**実施内容**

1. OneHouse 時: RoofPanel 厚さ 0.02f（薄い 1 枚の片流れ）、`CreateRoofEaveEdge` スキップ
2. OneHouse 時: RoofPanel Renderer を有効化（木質屋根を表示）
3. `CreateRoofSnowSurface(roofPanel.transform, houseW, houseD, oneHouseForced)` — OneHouse で RoofSnowSurface 表示
4. `CreateKenneyHolidayHouseVisual(houseRoot, skipRoof)` — OneHouse では `skipRoof=true` で Kenney Roof_A/B/Point を生成しない

---

### file path: `Assets/Scripts/CorniceRuntimeSnowSetup.cs`

**実施内容**

- `roof_slope_direction=front` を `[CORNICE_SCENE_CHECK]` と `[SNOW_ROLLBACK_CHECK]` に追加

---

### file path: `Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs`

**実施内容**

- `BuildCorniceSceneCheckSection` の抽出キーに `roof_slope_direction` を追加（ASSI レポートに含める）

---

## ログに出る値（Play 後）

| キー | 例 |
|------|-----|
| scene | Avalanche_Test_OneHouse |
| house_count | 1 |
| one_house_forced | true |
| active_roof_target | asset_roof |
| test_roof_visible | false |
| roof_shape | mono_slope |
| roof_slope_direction | front |
| camera_position | (0.00,5.20,-5.80) |
| camera_rotation | (36.0,0.0,0.0) |
| hit_target | RoofSnow（TAP_DEBUG） |

---

## 変更しないもの

- VideoPipeline / gif / Drive / Slack
- SnowPackSpawner の雪パラメータ
- 雪挙動・連鎖挙動の調整

---

## 成功条件

- 屋根が見た目に自然な片流れ屋根になっている
- 奥が高く、手前が低い
- 面が途中で折れない
- 雪が手前へ落ちそうな形に見える
- 1軒・屋根プレイ面・gif 録画は維持される
