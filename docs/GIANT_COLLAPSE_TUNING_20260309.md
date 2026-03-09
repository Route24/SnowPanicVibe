# [Snow Panic / 1ファイル依頼] 巨大崩壊型に寄せる

## 実装サマリ

- A) 初回Detach量を増やし主塊を強化（hitRadiusR 0.80→1.05, pieceSize 0.13→0.17）
- B) 二次崩壊・連鎖量を増加（secondaryDetachFraction, maxSecondaryDetachPerHit, chainDetachChance, third wave）
- C) 落下ピースの見た目を大きく（burstChunkCount 48→36 + scale 0.8→1.2）
- D) ログに primary_detach_count, secondary_detach_count, secondary_triggered, largest_fall_group, active_snow_visual, active_snow_break_logic を追加

---

## 変更パラメータ一覧

| パラメータ | 変更前 | 変更後 |
|------------|--------|--------|
| hitRadiusR | 0.80 | 1.05 |
| pieceSize | 0.13 | 0.17 |
| burstChunkCount | 48 | 36 |
| 落下chunk scale | baseScale * 0.8 | baseScale * 1.2 |
| secondaryDetachFraction | 0.22 | 0.35 |
| secondary wave clamp | 6-18 | 10-28 |
| maxSecondaryDetachPerHit | 12 | 28 |
| chainDetachChance | 0.6 | 0.78 |
| third wave 閾値 | removed >= 80 | removed >= 50 |
| thirdWaveDelaySec | 0.85 | 0.65 |
| thirdWaveFraction | 0.12 | 0.18 |
| third wave clamp | 4-12 | 8-24 |

---

## ログに出る値（タップ後）

| キー | 例 |
|------|-----|
| primary_detach_count | 25 |
| primary_cluster_size | 25 |
| secondary_detach_count | 12 |
| secondary_triggered | true |
| largest_fall_group | 25 |
| active_snow_visual | RoofSnowLayer+SnowPackPiece |
| active_snow_break_logic | SnowPackSpawner.HandleTap+DetachInRadius |

`[AVALANCHE_BURST_LOG]` がタップ約1.2秒後に1回出力される。

---

## 変更ファイル

- `Assets/Scripts/RoofSnowSystem.cs`
- `Assets/Scripts/SnowPackSpawner.cs`

---

## 成功条件

- クリック直後に主塊が「ドサッ」と落ちる
- その後に干渉で二次崩壊が起きる
- 見た目がサラサラではなく、重みのある崩壊になる
- 1軒・片流れ屋根・gif録画成功を維持する
