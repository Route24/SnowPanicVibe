# 補助メッシュ非表示 + 雪密着調整 2026-03-09

> コピーボタンで各ブロックをコピーできます。

---

## 実装サマリ

- A) **RoofDebugFlat** の Renderer を無効化（当たり判定は維持）
- B) **RoofSlideColliderDebug** を非表示（SetActive(false)）
- C) **RoofDebugAutoSetup** 作成時も Renderer を無効化
- D) 全シーン中の RoofDebugFlat を一括非表示
- E) RoofSnowLayer に snow_surface_offset (-0.02) で密着
- F) SnowPackPiece の RoofSurfaceOffset を 0.01→0.005 に変更
- G) ログに `[HELPER_MESH_HIDE]` で visible_roof_mesh_count, hidden_helper_meshes, snow_surface_offset, snow_visual_attached, hit_target を出力

---

## 変更ファイル一覧

| ファイル | 変更内容 |
|----------|----------|
| `Assets/Scripts/CorniceRuntimeSnowSetup.cs` | RoofDebugFlat Renderer 無効化、HideHelperMeshesAndLog 追加、ログ出力 |
| `Assets/Scripts/RoofDebugAutoSetup.cs` | CreateFlatRoof で Renderer 無効化 |
| `Assets/Scripts/RoofSnowSystem.cs` | roofSnowSurfaceOffsetY=-0.02、UpdateRoofVisual に適用 |
| `Assets/Scripts/SnowPackSpawner.cs` | RoofSurfaceOffset 0.01→0.005 |

---

## ログ出力例

```
[HELPER_MESH_HIDE] visible_roof_mesh_count=2 hidden_helper_meshes=[RoofRoot/RoofSlideCollider/RoofSlideColliderDebug] active_roof_target=asset_roof roof_shape=mono_slope roof_slope_direction=front snow_surface_offset=-0.02 snow_visual_attached=true hit_target=RoofSlideCollider
```

---

## 非表示対象

| オブジェクト | 処理 |
|--------------|------|
| RoofDebugFlat | MeshRenderer.enabled = false |
| RoofSlideColliderDebug | SetActive(false) |
| cabin-roof（既存） | CabinRoofForceHide で無効化 |
| RoofProxy（既存） | Destroy |

---

## 変更しないもの

- giant_collapse_type の基本方針
- pieceSize=0.17
- maxSecondaryDetachPerHit=28
- chainDetachChance=0.78
- secondaryDetachFraction=0.35
- VideoPipeline、gif、Drive、Slack
- 1軒化、片流れ屋根の向き

---

## 雪密着パラメータ

| パラメータ | 変更前 | 変更後 |
|------------|--------|--------|
| RoofSnowSystem.roofSnowSurfaceOffsetY | (新規) | -0.02 |
| SnowPackSpawner.RoofSurfaceOffset | 0.01 | 0.005 |

---

コーディングが終わりました。Play して確認してください。
