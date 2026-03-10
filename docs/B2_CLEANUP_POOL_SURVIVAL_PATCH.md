# PhaseB2: cleanup / survival / pool 切り分けパッチ

## 実装サマリ

- **PauseCleanup**: ClearSnowPack の破棄処理をスキップ（屋根残雪を消さない）
- **PausePoolReturn**: ReturnToPool をスキップ（slideRoot 内にピースを残す）
- PhaseB2 で両方 true に設定し、落雪後も surviving >= 1 を確認
- total==0 エラー前に surviving_count を必ずログ
- 4段階ログ: tap直後 / detach後 / cleanup後 / pool return後

---

## 変更ファイル一覧

| # | file path |
|---|-----------|
| 1 | `Assets/Scripts/SnowVerifyB2Debug.cs` |
| 2 | `Assets/Scripts/SnowPackSpawner.cs` |
| 3 | `Assets/Scripts/SnowVerifyPhaseB2.cs` |

---

## 必須ログ（全出力）

| ログキー | 出力タイミング |
|----------|----------------|
| `tap_received` | B2_BEFORE_DETACH, B2_TEST_A, B2_FINAL_SUMMARY |
| `before_detach_total` | B2_BEFORE_DETACH（タップ受付時） |
| `after_detach_total` | B2_AFTER_TAP, B2_AFTER_DETACH |
| `after_detach_active` | B2_AFTER_TAP, B2_AFTER_DETACH |
| `surviving_count_before_cleanup` | B2_POOL_BEFORE, B2_CLEANUP_SKIP |
| `cleanup_called` | B2_AFTER_CLEANUP, B2_CLEANUP_SKIP, B2_CLEANUP_DONE |
| `surviving_count_after_cleanup` | B2_AFTER_CLEANUP, B2_CLEANUP_SKIP, B2_CLEANUP_DONE |
| `pool_return_called` | B2_POOL_AFTER, B2_POOL_SKIP, B2_AFTER_CLEANUP |
| `surviving_count_after_pool` | B2_POOL_AFTER, B2_POOL_SKIP |
| `zero_total_guard_triggered` | B2_ZERO_GUARD（total==0 エラー直前） |
| `zero_total_trigger_step` | B2_ZERO_GUARD, B2_FINAL_SUMMARY |

---

## 仮ルール（PhaseB2）

- `PauseCleanup = true` … ClearSnowPack をスキップ
- `PausePoolReturn = true` … ReturnToPool をスキップ、slideRoot にピースを残す
- 落ちた雪の処理は雑でよい
- まず「屋根上残雪が1個以上残る」ことを確認

---

## 実行手順

1. **SnowPanic → Phase B2: Create Multi-Piece Snow Verify** でシーン作成
2. **Play** → 屋根を1回タップ
3. Console で次を確認:
   - `[B2_BEFORE_DETACH]` tap_received, before_detach_total
   - `[B2_AFTER_TAP]` after_detach_total, after_detach_active
   - `[B2_AFTER_DETACH]` surviving_count
   - `[B2_POOL_SKIP]` PausePoolReturn 時（queue にピース残す）
   - `[B2_AFTER_CLEANUP]` surviving_count_before/after_cleanup
   - `[B2_ZERO_GUARD]` エラー前に surviving_count, zero_total_trigger_step

---

## 主要差分

### SnowVerifyB2Debug.cs
```csharp
// 追加
public static string ZeroTotalTriggerStep;
public static bool PauseCleanup;
public static bool PausePoolReturn;
// Reset() に ZeroTotalTriggerStep, PauseCleanup, PausePoolReturn の初期化
```

### SnowVerifyPhaseB2.cs ApplyConfig()
```csharp
SnowVerifyB2Debug.PauseCleanup = true;
SnowVerifyB2Debug.PausePoolReturn = true;
```

### SnowPackSpawner.cs
- ClearSnowPack: PauseCleanup 時は early return、surviving_count をログ
- Pool 処理: PausePoolReturn 時は for ループをスキップ（queue 維持）
- total==0 エラー直前に B2_ZERO_GUARD で surviving_count, zero_total_trigger_step をログ
- B2_BEFORE_DETACH, B2_AFTER_TAP, B2_AFTER_DETACH, B2_AFTER_CLEANUP のログ形式を更新
