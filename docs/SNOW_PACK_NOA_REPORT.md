# SnowPack 修正レポート（ノア検証用）

## 1) 変更したファイル一覧

| ファイル | 変更種別 |
|---------|---------|
| `Assets/Scripts/SnowPackSpawner.cs` | 修正（A/B/C 全要件） |
| `Assets/Scripts/RoofSnowSystem.cs` | 修正（IsInAvalancheCooldown 追加） |

---

## 2) 変更行の要約

### SnowPackSpawner.cs

| 関数/箇所 | 変更内容 |
|----------|---------|
| **フィールド追加** | `visualScale`, `_poolReturnQueue`, `_pendingSlideRootToDestroy`, `_pendingRemoveCountFromAvalanche`, `_rootChildrenMin1s`, `_rootChildrenMax1s`, `_visualPackDeltaMax1s` |
| **SpawnPieceLocal** | 親Transformは等倍、子 "Mesh" に MeshFilter/MeshRenderer を配置し localScale=(visualScale,visualScale,visualScale) を適用 |
| **GetOrSpawnPiece** | 再利用時に親 scale=(w,h,w)、Mesh 子に visualScale を適用 |
| **ReturnToPool** | `_inAvalancheSlide` 中は早期 return。親が `_piecesRoot` のとき LogRootMutation |
| **RemoveLayers** | `IsInAvalancheCooldown` 中は早期 return + 証拠ログ `[SnowPackAvalancheGuard]` |
| **AvalancheSlideRoutine** | 一括 ReturnToPool を廃止。`_poolReturnQueue` に積み、`_pendingRemoveCountFromAvalanche` に層数を保持 |
| **Update** | ① `_poolReturnQueue` を毎フレーム最大50個ずつ ReturnToPool ② クールダウン終了後に `RecordLayersRemoved` で removeCount 記録 ③ Sync は `inAvalanche` 時スキップ |
| **LogRootMutation** | 全変更で frame 付き + StackTrace 出力（スパム抑止を解除） |
| **RecordLayersRemoved** | removeCount 加算の唯一の入口。`[SnowPackRemove]` ログで理由付き |
| **ClearSnowPack** | `_poolReturnQueue` と `_pendingSlideRootToDestroy` をクリア。LogRootMutation を追加 |
| **1秒監査** | `rootChildrenDelta1s`, `visualPackDeltaMax1s` を追加。activePieces=0 時に `[SnowPackPASS] activePieces=0 FAIL`。雪崩中かつキューありで `[SnowPackAvalancheGuard]` 証拠ログ |

### RoofSnowSystem.cs

| 関数/箇所 | 変更内容 |
|----------|---------|
| **IsInAvalancheCooldown** | `Time.time < _nextAvalancheTime` を返す public プロパティ |

---

## 3) rootChildren の 1秒最大変動値

**ログタグ**: `[SnowPackAudit1s] rootChildrenDelta1s=<値>`

- 計測: 毎秒 `_rootChildrenMax1s - _rootChildrenMin1s` を出力
- **PASS条件**: rootChildrenDelta1s <= 200
- 分割返却（50個/フレーム）により、1秒あたりの変動を抑制

---

## 4) activePieces が 0 になる瞬間が存在するか

**想定**: **No**（禁止している）

- `RemoveLayers` を雪崩中にブロック
- Pool返却を AvalancheVisual.end 後に限定し、1フレーム50個までに制限
- 発生時は `[SnowPackPASS] activePieces=0 FAIL` でエラーログ出力

**検証**: ログに `activePieces=0 FAIL` が出ないこと

---

## 5) removeCount が発生する条件と呼び出し元

| 条件 | 呼び出し元 | ログ |
|-----|-----------|------|
| **DepthSync**（roofDepth 低下） | `RemoveLayers` → `RecordLayersRemoved` | `reason=DepthSync.RemoveLayers(roofDepth drop)` |
| **AvalancheVisual.end** | クールダウン終了後に `RecordLayersRemoved` | `reason=AvalancheSlideVisual.end(deferred)` |

**統一**: `RecordLayersRemoved` のみが `_removeCount` を増やす。

**Avalanche中**: removeCount は増加しない（記録はクールダウン終了後に遅延実行）。

---

## 6) SnowPackRootMutation ログの抜粋（例）

```
[SnowPackRootMutation] before=1597 after=2 reason=AvalancheSlideRoutine.SetParent(slideRoot) frame=1234
STACK:
  at SnowPackSpawner.AvalancheSlideRoutine (...) 
  at UnityEngine.MonoBehaviour.StartCoroutine (...)
  ...
```

```
[SnowPackRootMutation] before=2 after=1 reason=Destroy(slideRoot) frame=1289
STACK:
  at SnowPackSpawner.Update ()
  ...
```

---

## 7) visualDepth と packDepth の差の最大値

**ログタグ**: `[SnowPackAudit1s] visualPackDeltaMax1s=<値>`

- 毎秒の `|visualDepth - packDepthMeters|` の最大値を 1 秒区間で集計

---

## 8) Avalanche中に Pool返却が行われていない証拠ログ

| ログ | 意味 |
|-----|------|
| `[SnowPackAvalancheGuard] RemoveLayers skipped n=X reason=IsInAvalancheCooldown evidence=NoRemoveDuringAvalanche` | 雪崩中に RemoveLayers がスキップされた |
| `[SnowPackAvalancheGuard] poolReturnDeferred inAvalanche=true queueSize=X evidence=NoPoolReturnDuringAvalanche` | 雪崩中はキューが処理されず、返却が遅延されている |

- `ReturnToPool` 内の `if (_inAvalancheSlide) return` により、スライド中の直接 ReturnToPool は実行されない
- キュー処理は `!_inAvalancheSlide` のときのみ実行

---

## 判定基準（PASS条件）

| 項目 | 条件 |
|-----|------|
| children 1秒変動 | rootChildrenDelta1s <= 200 |
| activePieces | 0 にならない |
| removeCount | Avalanche中に増えない |
| ループ現象 | 視覚的に消える |

---

## 実行時検証手順

1. シーン `Avalanche_Test_OneHouse` を再生
2. 雪崩を発生させる
3. コンソールで以下を確認:
   - `[SnowPackAudit1s]` の `rootChildrenDelta1s`, `activePieces`, `visualPackDeltaMax1s`
   - `[SnowPackAvalancheGuard]` が雪崩中に出力されること
   - `[SnowPackPASS] activePieces=0 FAIL` が出力されないこと
   - `[SnowPackRootMutation]` で `1597→2` や `2→1` の要因が StackTrace で追えること
