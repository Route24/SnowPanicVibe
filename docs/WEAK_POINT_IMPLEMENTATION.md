# Weak Point System 実装ドキュメント

屋根の雪に「弱点ポイント」を配置し、戦略性・中毒性を追加。弱点を叩くと巨大雪崩が起きやすくなる。

---

## 実装サマリー

- **SnowCluster.cs**: `isWeakPoint` プロパティ追加
- **AvalanchePhysicsSystem.cs**: Weak Point 生成、効果、Mega 確率、スコアボーナス、可視ヒント、ASSI REPORT

---

## 追加・変更スクリプト

### SnowCluster.cs（変更）

```csharp
// 追加
public bool isWeakPoint;
```

### AvalanchePhysicsSystem.cs（変更）

**追加パラメータ（Inspector）**
```
[Header("Weak Point")]
weakPointDamageMultiplier = 3f      // support_damage 倍率
weakPointNeighborWeakenBonus = 2f   // 隣接への追加 weaken
weakPointMegaChanceBonus = 0.4f     // Mega Avalanche 確率ボーナス
weakPointScoreMultiplier = 2f       // スコア倍率（x2 = +1/ピース）
```

**ロジック**
- `RebuildClusters()`: クラスター構築後、`max(1, N/8)` 個をランダムに Weak Point に設定
- `ApplyWeakPointVisualHint(cluster)`: わずかな色差（明度 92%）を付与
- `OnSnowHit()`: 弱点ヒット時
  - `support_value -= hitDamage * 3`
  - 隣接 weaken に +2 追加
  - Mega 確率 +0.4
  - スコア +toDetach.Count（2倍相当）

**静的プロパティ**
- `WeakPointsTotal`, `WeakPointHits`, `WeakPointMegaTriggered`

---

## コード全文（該当箇所）

### SnowCluster.cs 差分

```csharp
public bool isWeakPoint;
```

### AvalanchePhysicsSystem.cs 主要差分

```csharp
// フィールド
[Header("Weak Point")]
public float weakPointDamageMultiplier = 3f;
public float weakPointNeighborWeakenBonus = 2f;
[Range(0f, 1f)] public float weakPointMegaChanceBonus = 0.4f;
public float weakPointScoreMultiplier = 2f;

readonly HashSet<int> _weakPointHintApplied = new HashSet<int>();
float _weakPointNeighborBonus;
float _weakPointMegaChanceBonus;
bool _weakPointHitThisTap;

public static int WeakPointsTotal { get; private set; }
public static int WeakPointHits { get; private set; }
public static bool WeakPointMegaTriggered { get; private set; }

// RebuildClusters 内: 弱点配置
int weakCount = Mathf.Max(1, _clusters.Count / 8);
// Fisher-Yates でランダムインデックス選択 → isWeakPoint=true, ApplyWeakPointVisualHint

// OnSnowHit 内: 弱点効果
float damage = hitDamage * (c.isWeakPoint ? weakPointDamageMultiplier : 1f);
_weakPointNeighborBonus = anyWeakPointHit ? weakPointNeighborWeakenBonus : 0f;
// DetachCluster(c, 0, c.isWeakPoint)

// DetachCluster 内: neighborWeaken = weakenValue + _weakPointNeighborBonus
// 弱点起点時: SnowPhysicsScoreManager.Instance.Add(toDetach.Count)
// Mega 時: WeakPointMegaTriggered = true
```

---

## テスト方法

1. Unity で Play
2. 屋根の雪を観察: わずかに暗い色のクラスター（弱点ヒント）
3. 弱点を叩く: 通常より大きく雪崩が発生するか確認
4. 通常箇所を叩く: 小さい雪崩と比較
5. 停止 → ASSI Report で `=== WEAK POINT TEST ===` を確認

### PASS

- Weak Point を叩くと、通常より大きい雪崩が起きる

### FAIL

- Weak Point の効果が通常ヒットと変わらない

---

## ASSI REPORT サンプル

```
=== WEAK POINT TEST ===
weak_points_total=5
weak_point_hits=3
weak_point_mega_triggered=true
```

---

## 可視ヒント

- **わずかな色差**: 弱点クラスターのピースを明度 92% に暗くする
- 明確なマーカーは出さない（パーティクル・アイコン等なし）

---

コーディングが終わりました。Play して動作を確認してください。
