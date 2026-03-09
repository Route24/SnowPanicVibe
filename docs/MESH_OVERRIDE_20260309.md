# 補助メッシュ正体特定と完全非表示 2026-03-09

> コピーボタンで各ブロックをコピーできます。

---

## 実装サマリ

- A) **cabin-roof** を全件非表示（FindObjectsByType で複数あっても全て無効化）
- B) **RoofSnowSurface** を非表示（RoofSnowLayer と重複する白い Quad）
- C) **RoofDebugFlat / RoofSlideColliderDebug / RoofProxy** を継続して非表示
- D) ログ `[MESH_OVERRIDE]` で visible_mesh_objects / hidden_mesh_objects を名前で出力

---

## 非表示対象（名前で特定）

| GameObject 名 | 処理 |
|---------------|------|
| cabin-roof | MeshRenderer.enabled = false（全件） |
| RoofDebugFlat | MeshRenderer.enabled = false |
| RoofSlideColliderDebug | SetActive(false) |
| RoofSnowSurface | MeshRenderer.enabled = false |
| RoofProxy | Destroy |

---

## ログ出力例

```
[MESH_OVERRIDE] visible_mesh_objects=[CorniceRoot/Houses/House_0/Roof/RoofPanel,CorniceRoot/.../RoofSnowLayer,...] hidden_mesh_objects=[RoofRoot/RoofYaw/cabin-roof,CorniceRoot/Houses/House_0/Roof/RoofSnowSurface,...] active_roof_target=asset_roof hit_target=RoofSlideCollider roof_shape=mono_slope roof_slope_direction=front snow_surface_offset=-0.02 snow_visual_attached=true
```

---

## 変更ファイル

| ファイル | 変更内容 |
|----------|----------|
| `CabinRoofForceHide.cs` | cabin-roof を FindObjectsByType で全件取得し無効化 |
| `CorniceRuntimeSnowSetup.cs` | RoofSnowSurface を非表示対象に追加、visible_mesh_objects/hidden_mesh_objects ログ |

---

## 表示を残すもの

- RoofPanel（屋根の木質面）
- RoofSnowLayer（雪マスク表示）
- SnowPackPiece（積雪ピース）
- Ground 等

---

コーディングが終わりました。Play して確認してください。
