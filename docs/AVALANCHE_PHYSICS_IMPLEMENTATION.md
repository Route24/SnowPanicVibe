# Avalanche Physics System 実装ドキュメント

気持ちいい崩壊体験向け雪崩物理システム。5段階（溜め→崩れ始め→連鎖→巨大雪崩→余韻）を再現。

---

## 実装サマリー

- **SnowCluster.cs**: 物理単位 cluster（3〜8個のピース群）の状態管理
- **AvalanchePhysicsSystem.cs**: ヒット→Support低下→連鎖→Mega Avalanche の統合
- **SnowPackSpawner.cs**: `CollectPackedPiecesWithGrid`, `DetachPiecesDirect` 追加
- **RoofSnowSystem.cs**: AvalanchePhysicsSystem 連携、自動配置
- **AvalancheFeedback.cs**: `TriggerMicroShakeIfExists` 追加
- **AIPipelineTestCollector.cs**: `=== AVALANCHE TEST ===` セクション出力

---

## 追加スクリプト

### SnowCluster.cs

```csharp
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Snow Panic 雪崩物理: 物理単位 cluster（3〜8個のピース群）。
/// </summary>
public class SnowCluster
{
    public int cluster_id;
    public readonly List<Transform> piece_list = new List<Transform>();
    public float support_value;
    public ClusterState weak_state = ClusterState.Stable;

    public enum ClusterState { Stable, Weak, Critical }

    public Vector3 Center { get; /* ピース重心 */ }
    public int ActivePieceCount { get; }
    public void UpdateState(float thresholdStable, float thresholdWeak);
}
```

### AvalanchePhysicsSystem.cs

- `OnSnowHit(worldPoint)`: タップ時に Support 低下、Critical で Detach
- `RebuildClusters()`: グリッドから cluster 構築
- `DetachCluster(cluster, depth)`: 局所剥離、隣接 Weak 化、連鎖キュー投入
- `ProcessChainQueue()`: 遅延連鎖処理
- 静的: `ClustersTotal`, `ClustersDetached`, `MaxChainDepth`, `SlideDistanceAvg`, `MegaAvalancheTriggered`, `LastTapRemovedTotal`
- `EmitAvalancheTestToReport()`: ASSI Report 出力

---

## 変更スクリプト（差分）

### SnowPackSpawner.cs

```csharp
// 追加: CollectPackedPiecesWithGrid
public void CollectPackedPiecesWithGrid(List<(Transform t, int ix, int iz)> outList);

// 追加: DetachPiecesDirect
public void DetachPiecesDirect(List<Transform> pieces, Vector3 slideDir, float slideSpeed);
```

### RoofSnowSystem.cs

```csharp
// RequestTapSlide 内
var avalanchePhys = FindFirstObjectByType<AvalanchePhysicsSystem>();
if (avalanchePhys != null && avalanchePhys.useAvalanchePhysics)
{
    avalanchePhys.OnSnowHit(tapWorldPoint);
    SnowPackSpawner.LastRemovedCount = AvalanchePhysicsSystem.LastTapRemovedTotal;
}
else
    snowPackSpawner.PlayLocalAvalancheAt(tapWorldPoint, hitRadiusR, localAvalancheSlideSpeed);

// Start 内
EnsureAvalanchePhysicsSystem();

// 追加メソッド
void EnsureAvalanchePhysicsSystem(); // 無ければ SnowPackSpawner に AvalanchePhysicsSystem を AddComponent
```

### AvalancheFeedback.cs

```csharp
public static void TriggerMicroShakeIfExists(); // 局所崩壊用
```

### AIPipelineTestCollector.cs

```csharp
// EmitFinalReport 末尾
AvalanchePhysicsSystem.EmitAvalancheTestToReport();
```

---

## テスト方法

### Play で確認

1. Unity で Play
2. 屋根の雪をタップ
3. 以下を確認:
   - **叩くとヒビ感** … 小崩れ（Weak → Critical）
   - **小崩れ** … 1 cluster のみ剥離
   - **連鎖** … 隣接 cluster が遅れて崩壊
   - **斜面を滑る** … 剥離後、斜面方向に滑走
   - **巨大雪崩** … 連続タップ or 連鎖で Mega 発動

### パラメータ（Inspector）

- `AvalanchePhysicsSystem.useAvalanchePhysics`: 有効/無効
- `thresholdStable` / `thresholdWeak`: Support 閾値
- `hitDamage`: 1タップあたりの support 減少
- `weakenValue`, `detachChance`, `chainDelaySec`: 連鎖
- `megaConsecutiveThreshold`, `megaChainDepthThreshold`: Mega 条件

---

## ASSI REPORT サンプル

```
=== AVALANCHE TEST ===
clusters_total=42
clusters_detached=8
max_chain_depth=2
slide_distance_avg=0.960
mega_avalanche_triggered=true
```

---

## PASS / FAIL チェック

| PASS | FAIL |
|------|------|
| 叩くとヒビ感がある | 全部同時崩壊 |
| 小崩れが起きる | パラパラ落ち |
| 連鎖が起きる | 真下落下のみ |
| 斜面を滑る | 連鎖なし |
| 巨大雪崩が発生する | |

---

コーディングが終わりました。Play → 停止 → レポートをノアに送ってください。
