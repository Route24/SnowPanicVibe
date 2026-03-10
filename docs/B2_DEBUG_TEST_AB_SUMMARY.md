# B2-debug: Test A/B 分離・落雪後のゼロ化ログ

## 実装サマリ

- **Test A**: Play後4秒、何も触らず → `test_a_no_input_total`, `test_a_no_input_active`, `test_a_error_occurred` をログ
- **Test B**: 1回タップして落雪 → `test_b_after_tap`, `test_b_after_detach`, `test_b_after_cleanup` を各段階でログ
- `zero_transition_step` で total=0 になったタイミングを特定
- Phase B2 Creator に TapToSlideOnRoof 追加（Test B 実行可能）

---

## 変更ファイル一覧

| # | file path | 役割 |
|---|-----------|------|
| 1 | `Assets/Scripts/SnowVerifyB2Debug.cs` | ZeroTransitionStep, LastTapTimeAtTestB 追加 |
| 2 | `Assets/Scripts/SnowPackSpawner.cs` | GetB2ActiveCount/GetB2TotalCount, B2_AFTER_TAP/DETACH/CLEANUP ログ |
| 3 | `Assets/Scripts/RoofSnowSystem.cs` | RecordTapForTestB 呼び出し |
| 4 | `Assets/Scripts/SnowVerifyPhaseB2.cs` | Test A/B モニタ、最終サマリ |
| 5 | `Assets/Editor/SnowVerifyPhaseB2Creator.cs` | TapToSlideOnRoof 追加 |

---

## 必須ログ（全出力）

| ログキー | 出力タイミング |
|----------|----------------|
| `test_a_no_input_total` | 4秒経過時（Test A） |
| `test_a_no_input_active` | 4秒経過時 |
| `test_a_error_occurred` | total=0 かつタップなし時 true |
| `test_b_after_tap_total` | タップ直後（B2_AFTER_TAP） |
| `test_b_after_tap_active` | タップ直後 |
| `test_b_after_detach_total` | detach直後（B2_AFTER_DETACH） |
| `test_b_after_detach_active` | detach直後 |
| `test_b_after_cleanup_total` | cleanup直後（B2_AFTER_CLEANUP） |
| `test_b_after_cleanup_active` | cleanup直後 |
| `zero_transition_step` | generation / after_tap / after_detach / after_cleanup / none |
| `discard_reason_counts` | {...} |
| `cleanup_called` | true/false |
| `pool_return_called` | true/false |

---

## 実行手順

1. **SnowPanic → Phase B2: Create Multi-Piece Snow Verify** でシーン作成（TapToSlide 付き）
2. **Test A**: Play → 4秒放置 → Console に `[B2_TEST_A]` が出る
3. **Test B**: Play → 1回タップ（屋根をクリック）→ `[B2_AFTER_TAP]` → `[B2_AFTER_DETACH]` → `[B2_AFTER_CLEANUP]` → 8秒で `[B2_FINAL_SUMMARY]`

---

## 置換・追加箇所（差分）

### 1. SnowVerifyB2Debug.cs

```csharp
// 追加（RecordDiscard の前）
    public static string ZeroTransitionStep;
    public static float LastTapTimeAtTestB;

    public static void RecordZeroTransition(string step)
    {
        if (!Enabled) return;
        if (string.IsNullOrEmpty(ZeroTransitionStep))
            ZeroTransitionStep = step;
    }

    public static void RecordTapForTestB(float tapTime)
    {
        if (!Enabled) return;
        LastTapTimeAtTestB = tapTime;
    }

// Reset() 内に追加
        ZeroTransitionStep = null;
        LastTapTimeAtTestB = -10f;
```

### 2. SnowPackSpawner.cs

```csharp
// GetPooledCount の直後に追加
    public int GetB2ActiveCount()
    {
        return _visualRoot != null ? CountPiecesUnder(_visualRoot) : 0;
    }

    public int GetB2TotalCount()
    {
        return GetB2ActiveCount() + GetPooledCount();
    }

// HandleTap 終了、StartCoroutine の直前に追加
        if (SnowVerifyB2Debug.Enabled)
        {
            int total = GetB2TotalCount();
            int active = GetB2ActiveCount();
            int pooled = GetPooledCount();
            int surv = GetPackedCubeCountRealtime() + toRemove.Count;
            UnityEngine.Debug.Log($"[B2_AFTER_TAP] test_b_after_tap_total={total} ...");
            if (total <= 0) SnowVerifyB2Debug.RecordZeroTransition("after_tap");
        }

// LocalAvalancheSlideRoutine の _poolReturnQueue.Add 後
        if (SnowVerifyB2Debug.Enabled)
        {
            int total = GetB2TotalCount();
            ...
            UnityEngine.Debug.Log($"[B2_AFTER_DETACH] test_b_after_detach_total={total} ...");
            if (total <= 0) SnowVerifyB2Debug.RecordZeroTransition("after_detach");
        }

// pool返却完了・slideRoot破棄直前
        if (SnowVerifyB2Debug.Enabled)
        {
            int total = GetB2TotalCount();
            ...
            UnityEngine.Debug.Log($"[B2_AFTER_CLEANUP] test_b_after_cleanup_total={total} ...");
            if (total <= 0) SnowVerifyB2Debug.RecordZeroTransition("after_cleanup");
        }
```

### 3. RoofSnowSystem.cs

```csharp
// RequestTapSlide の先頭、snowPackSpawner.LogNearestPieceToTap の前
        if (SnowVerifyB2Debug.Enabled) SnowVerifyB2Debug.RecordTapForTestB(Time.time);
```

### 4. SnowVerifyPhaseB2.cs

- 全面書き換え（Test A 4秒、Final Summary 8秒、LogTestA, LogFinalSummary）

### 5. SnowVerifyPhaseB2Creator.cs

```csharp
// SetCamera 内、cam.transform 設定のあと
            if (cam.GetComponent<TapToSlideOnRoof>() == null)
                cam.gameObject.AddComponent<TapToSlideOnRoof>();
```

---

## 成功条件

- total=0 になるタイミングが `zero_transition_step` で特定できる
- 生成時か、タップ後か、cleanup後かが明確になる
- 次の修正対象が1箇所に絞れる
