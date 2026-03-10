# Run Structure System 実装ドキュメント

1プレイの流れを設計し、短時間ループで再挑戦したくなる構造を実装。

---

## 実装サマリー

- **RunStructureManager.cs**: Run の状態・タイマー・コンボ・結果・Retry 制御
- **RunResultUI.cs**: 結果画面（Score, Combo, Mega, Rank, Best, Retry/Title）
- **RunHUDUI.cs**: Ready/Start カウントダウン、残り時間表示
- **SnowPhysicsScoreManager.cs**: `ResetForNewRun()` 追加
- **AvalanchePhysicsSystem.cs**: `ResetRunCounters()`, `MegaAvalancheCount` 追加
- **AIPipelineTestCollector.cs**: `=== RUN STRUCTURE TEST ===` 出力

---

## フロー

```
開始 → Ready(1s) → Start(1s) → Running(90s) → 結果表示 → Retry/Title
```

---

## 追加・変更スクリプト

### RunStructureManager.cs（新規）

- **状態**: PreCountdown → Countdown → Running → ShowingResult
- **パラメータ**: runTimeLimit=90, countdownReadySec=1, countdownStartSec=1
- **スコア目標**: Bronze 3000, Silver 6000, Gold 10000, Platinum 15000 → Rank D/C/B/A/S
- **コンボ**: スコア増加で伸び、2秒無変化でリセット
- **Best記録**: PlayerPrefs で保存
- **Retry**: 同一シーンを即再ロード
- **GoToTitle**: SampleScene へ

### RunResultUI.cs（新規）

- 結果パネル（半透明黒オーバーレイ）
- 表示: final_score, max_combo, mega_avalanches, villager_hits, rank
- Best Score, Best Combo, Best Rank
- Retry ボタン（1クリックで即再開）
- Title ボタン

### RunHUDUI.cs（新規）

- Countdown 中: 「Ready」→「Start」中央表示
- Running 中: 残り時間（右上、MM:SS）

### SnowPhysicsScoreManager.cs（変更）

```csharp
public static void ResetForNewRun();
```

### AvalanchePhysicsSystem.cs（変更）

```csharp
public static int MegaAvalancheCount { get; }
public static void ResetRunCounters();
```

---

## テスト方法

1. Unity で Play（Avalanche_Test_OneHouse 推奨）
2. Ready → Start のカウントダウンを確認
3. 90秒間プレイ（残り時間が減ることを確認）
4. 時間切れで結果画面が表示されることを確認
5. Retry を押すと即再開されることを確認
6. 停止 → ASSI Report で `=== RUN STRUCTURE TEST ===` を確認

### PASS

- Run が開始できる
- 時間制限で終了する
- 結果画面が出る
- Retry ですぐ再開できる

### FAIL

- Run の終わりがない
- 結果が出ない
- Retry しづらい

---

## ASSI REPORT サンプル

```
=== RUN STRUCTURE TEST ===
run_started=true
run_finished=true
run_time_limit=90
final_score=1234
max_combo=15
villager_hits=0
result_rank=C
retry_available=true
```

---

## 実装制約

- SnowPhysics（雪崩物理コア）、Camera、Video Pipeline は変更していない
- UI は必要最小限（Canvas 子として追加）

---

## Risk Escalation（将来拡張用）

- 残り時間に応じた村人・犬・Elderly・雪量の変化は未実装
- フック用のコメント・拡張ポイントのみ用意

---

コーディングが終わりました。Play して動作を確認してください。
