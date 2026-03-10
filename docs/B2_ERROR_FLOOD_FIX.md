# SnowPackPoolError 連打停止パッチ

## 実装サマリ

- total=0 検出後、エラーを **1回のみ** 出力（2回目以降は抑制）
- 初回は LogError → **LogWarning** に変更（1回のみ、以降は抑制）
- 監視ループ（Update 内の total==0 チェック）が毎フレーム同じエラーを吐いていた問題を解消
- 必須ログを B2_ZERO_MONITOR に集約

---

## 変更ファイル

| # | file path |
|---|-----------|
| 1 | `Assets/Scripts/SnowPackSpawner.cs` |
| 2 | `Assets/Scripts/SnowVerifyPhaseB2.cs` |
| 3 | `docs/B2_ERROR_FLOOD_FIX.md` |
| 4 | `docs/B2_ERROR_FLOOD_COPY.html` |

---

## 必須ログ（B2_ZERO_MONITOR）

| キー | 説明 |
|------|------|
| zero_total_detected | true/false |
| zero_total_first_frame | 初回検出フレーム |
| zero_total_repeat_count | 検出回数（抑制前の累積） |
| monitor_loop_active | true（Update 内監視） |
| cleanup_loop_active | CleanupCalled の有無 |
| pool_monitor_active | _poolReturnQueue.Count > 0 |
| error_emitted_once | 初回のみ true |
| error_suppressed_after_first | 2回目以降 true |
| zero_total_recovery_attempted | debugAutoRefillRoofSnow && _autoRebuildFired |
| zero_total_recovery_condition | "debugAutoRefillRoofSnow_active_zero" or "none" |

---

## 動作

1. **初回 total==0**: B2_ZERO_MONITOR ログ + LogWarning（1回のみ）
2. **2回目以降**: 2回目で「error_suppressed_after_first=true」を1回ログ、以降はログなし
3. **total > 0 に戻った場合**: フラグリセット。再度 total==0 なら初回扱い
4. **停止**: 毎フレーム return するので、以降の Update 処理は実行されない

---

## 主要差分

### SnowPackSpawner.cs

```csharp
// フィールド追加
bool _zeroTotalErrorEmittedOnce;
int _zeroTotalFirstFrame = -1;
int _zeroTotalRepeatCount;
bool _zeroTotalSuppressLogged;

// total > 0 時にリセット
if (total > 0) {
  _zeroTotalErrorEmittedOnce = false;
  _zeroTotalFirstFrame = -1;
  _zeroTotalRepeatCount = 0;
  _zeroTotalSuppressLogged = false;
}

// total == 0 時
_zeroTotalRepeatCount++;
if (!_zeroTotalErrorEmittedOnce) {
  // 初回: B2_ZERO_MONITOR + LogWarning
  _zeroTotalErrorEmittedOnce = true;
  _zeroTotalFirstFrame = Time.frameCount;
} else {
  // 2回目: 抑制ログ1回のみ
  if (!_zeroTotalSuppressLogged) {
    _zeroTotalSuppressLogged = true;
    // ログ
  }
}
return;  // 以降の Update 処理をスキップ
```

### SnowVerifyPhaseB2.cs

- B2_FINAL_SUMMARY に zero_total_detected を追加
