# Snow Panic Master Development Roadmap

AI設計 + AIコーディングで安定して完成させるための開発進行ルールとステップ定義。  
**1ステップずつ**このロードマップに従って実装する。

---

## 開発ルール

| ルール | 内容 |
|--------|------|
| **ルール1** | 1回の依頼で **1ステップのみ** 実装する |
| **ルール2** | 必ず **PASS / FAIL** を判定する |
| **ルール3** | 修正対象を宣言する → `=== MODIFY TARGET ===` `target_system=...` |
| **ルール4** | 対象外システムは **変更禁止** |

---

## Phase 0 開発基盤

### Step 0-1: AI開発パイプライン基盤

**実装**
- TEST RESULT
- DEBUG SCREENSHOT STATUS
- SCENE STATE

**PASS**  
ASSI REPORT に TEST RESULT / DEBUG SCREENSHOT STATUS / SCENE STATE が出る。

---

### Step 0-2: Change Scope Lock

**目的**  
修正対象以外を変更できないようにする。

**PASS**  
`unexpected_changes=0`

---

### Step 0-3: Bug Origin Tracker

**目的**  
バグ発生時に event trace / state snapshot / origin analysis を出す。

**PASS**  
BUG ORIGIN ANALYSIS が REPORT に出る。

---

## Phase 1 安定化

### Step 1-1: Score UI 固定

**仕様**  
左上のみ・黄色・黒アウトライン・サイズ固定。

**PASS**  
Score表示が1つのみ。

---

### Step 1-2: Tapエラー根絶

**対象**  
Particle duration エラー。

**PASS**  
Console error = 0

---

### Step 1-3: 雪と屋根サイズ一致

**目的**  
屋根と雪の width / depth を一致させる。

**PASS**  
初期状態でズレがない。

---

## Phase 2 雪崩コア

### Step 2-1: 斜面滑走

**仕様**  
雪は真下ではなく斜面方向へ滑る。

---

### Step 2-2: 局所剥離

**仕様**  
叩くと小崩れが起きる。

---

### Step 2-3: 連鎖崩壊

**仕様**  
小崩れ → 周囲崩壊

---

### Step 2-4: Mega Avalanche

**条件**  
一定崩壊数で発動。

---

### Step 2-5: 余韻

**仕様**  
雪煙・残雪・転がり。

---

## Phase 3 ゲーム性

### Step 3-1: Avalanche Combo System

連鎖で Combo 増加。

---

### Step 3-2: Weak Point System

屋根に崩壊弱点を配置。

---

### Step 3-3: Run Structure

1 run = 60〜120秒。

---

## Phase 4 商品化

### Step 4-1: Snow Visual

雪の見た目改善。

---

### Step 4-2: Roof Template

屋根タイプ: MonoSlope / Gable / Flat

---

### Step 4-3: 家数拡張

1 → 3 → 6

---

### Step 4-4: NPC危険

子供・高齢者・犬。

---

## ASSI REPORT 追加セクション

```
=== DEVELOPMENT STEP ===
current_step=...
step_result=PASS/FAIL
```

---

## 成功条件

ロードマップ順に **すべての Step が PASS** になること。

---

## テスト方法

1. 該当 Step の PASS 条件を確認する。
2. Unity で Play → 該当条件を満たすか確認。
3. 停止 → ASSI Report の `=== DEVELOPMENT STEP ===` を確認。
4. `step_result=PASS` であればその Step は完了。

---

## ASSI REPORT サンプル

```
=== MODIFY TARGET ===
target_system=AvalanchePhysics
allowed_files=AvalanchePhysicsSystem.cs,SnowCluster.cs
protected_systems=Camera,VideoPipeline,ScoreManager

=== DEVELOPMENT STEP ===
current_step=2-2
step_result=PASS

=== TEST RESULT [TEST_SNOW_HIT] ===
expected: snow pieces detach
result: PASS
value_activePieces=42

=== DEBUG SCREENSHOT STATUS ===
captured=true

=== SCENE STATE ===
scene_name=Avalanche_Test_OneHouse
root_object_count=156
active_snow_pieces=42
score_value=128
```

---

## 変更・追加コード（DEVELOPMENT STEP 出力用）

### DevelopmentStepTracker.cs（追加済）

```csharp
public static class DevelopmentStepTracker
{
    public static string CurrentStep { get; set; } = "0-1";
    public static string StepResult { get; set; } = "PENDING";

    public static void EmitToReport()
    {
        SnowLoopLogCapture.AppendToAssiReport("=== DEVELOPMENT STEP ===");
        SnowLoopLogCapture.AppendToAssiReport($"current_step={CurrentStep}");
        SnowLoopLogCapture.AppendToAssiReport($"step_result={StepResult}");
    }
}
```

### AIPipelineTestCollector.cs（変更済）

EmitFinalReport 末尾に `DevelopmentStepTracker.EmitToReport();` を追加。

---

コーディングが終わりました。各 Step 実装時に `DevelopmentStepTracker.CurrentStep` と `StepResult` を設定してください。
