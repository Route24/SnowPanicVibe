# [Snow Panic / 1ファイル依頼] 雪システム混在の解消

## 実装サマリ

- A) OneHouse で RoofSnow（粒雪・SnowClump）を無効化し、SnowPack 塊崩れのみに統一
- B) CorniceHitter に RoofPanel ヒット時のフォールバックを追加（RoofSlideCollider 以外でも雪崩発火）
- C) ログに enabled_snow_systems, disabled_legacy_snow_systems, active_snow_visual, active_snow_break_logic, active_snow_spawn_logic を追加
- D) ASSI レポートに上記キーを反映

---

## 変更ファイル一覧（実施済み）

### file path: `Assets/Scripts/CorniceRuntimeSnowSetup.cs`

**実施内容**

1. OneHouse 時: RoofSnowPlaceholder を RoofSnow に変換せず **DestroyImmediate**（粒雪・SnowClump 系を生成しない）
2. 多軒シーンは従来どおり RoofSnow（ParticleSystem + SnowClump）を作成
3. CORNICE_SCENE_CHECK / SNOW_ROLLBACK_CHECK に以下を追加:
   - `enabled_snow_systems`
   - `disabled_legacy_snow_systems`
   - `active_snow_visual`
   - `active_snow_break_logic`
   - `active_snow_spawn_logic`

---

### file path: `Assets/Scripts/CorniceHitter.cs`

**実施内容**

1. RoofSlideCollider ヒットに加え、**RoofPanel / Roof / RoofSnowSurface** ヒット時も RoofSnowSystem.RequestTapSlide にルーティング
2. `IsCorniceRoofSurface(collider)` で Cornice 屋根面を判定し、ClosestPoint で RoofSlideCollider 上に写像してタップ処理

---

### file path: `Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs`

**実施内容**

- BuildCorniceSceneCheckSection の抽出キーに `enabled_snow_systems`, `disabled_legacy_snow_systems`, `active_snow_visual`, `active_snow_break_logic`, `active_snow_spawn_logic` を追加

---

## ログに出る値（OneHouse / Play 後）

| キー | 例 |
|------|-----|
| scene | Avalanche_Test_OneHouse |
| house_count | 1 |
| active_roof_target | asset_roof |
| roof_shape | mono_slope |
| rollback_applied | true/false |
| hit_target | RoofSlideCollider / RoofPanel |
| enabled_snow_systems | [SnowPackSpawner,RoofSnowSystem,RoofSnowLayer,SnowPackFallingPiece] |
| disabled_legacy_snow_systems | [RoofSnow_particle,SnowClump,RoofSnowPlaceholder] |
| active_snow_visual | RoofSnowLayer+SnowPackPiece |
| active_snow_break_logic | SnowPackSpawner.HandleTap+DetachInRadius |
| active_snow_spawn_logic | SnowPackSpawner.RebuildSnowPack |

---

## 有効/無効の対応表（OneHouse）

| システム | 状態 |
|----------|------|
| SnowPackSpawner | 有効 |
| RoofSnowSystem | 有効 |
| RoofSnowLayer | 有効 |
| SnowPackFallingPiece | 有効 |
| RoofSnow（ParticleSystem） | 無効 |
| SnowClump | 無効 |
| RoofSnowPlaceholder | 削除 |

---

## 変更しないもの

- VideoPipeline / gif / Drive / Slack
- SnowPackSpawner の雪パラメータ（pieceSize=0.13 等）

---

## 成功条件

- 叩いて落ちる雪の見た目が 1 種類（塊崩れ）に統一される
- 過去テストの粒雪や SnowClump が混ざらない
- 最新版の塊崩れだけが動く
- 1 軒・片流れ屋根・gif 録画成功を維持する
