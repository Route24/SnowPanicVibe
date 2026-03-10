# 雪の気持ちよさ調整（1軒シーン用）

## 実装サマリ

- **ApplySnowFeelTuning**: Avalanche_Test_OneHouse で自動適用
- 狙い撃ち・連鎖・塊感・雪っぽさを調整
- 6軒には戻さない（1軒のみ）

---

## 変更ファイル

| # | file path |
|---|-----------|
| 1 | `Assets/Scripts/CameraMatchAndSnowConfig.cs` |
| 2 | `docs/SNOW_FEEL_TUNING.md` |
| 3 | `docs/SNOW_FEEL_COPY.html` |

---

## 調整項目と値

| 項目 | 調整後 | 狙い |
|------|--------|------|
| **一撃で剥がれる量** | | |
| hitRadiusR | 0.88 | やや狭め＝狙いが必要 |
| localAvalancheMinDetach | 18 | 最低剥がれ量 |
| localAvalancheMaxDetach | 65 | 1撃上限（連鎖に委ねる） |
| **連鎖の起きやすさ** | | |
| chainDetachChance | 0.82 | 条件で大きく連鎖 |
| secondaryDetachFraction | 0.4 | 二次崩壊多め |
| maxSecondaryDetachPerHit | 32 | 連鎖キャパ拡大 |
| unstableRadiusScale | 1.5 | 不安定ゾーン広め |
| secondaryDetachDelaySec | 0.32 | 連鎖やや速め |
| unstableDurationSec | 1.5 | 連鎖継続時間 |
| **塊で落ちる感じ** | | |
| localAvalancheSlideSpeed | 1.05 | やや速め＝インパクト |
| burstChunkCount | 44 | 塊の数 |
| burstChunkSpeed | 2.4 | 落下速さ |
| **残雪の見え方** | | |
| snowRenderThicknessScale | 0.78 | 厚み感 |
| pieceHeightScale | 0.88 | 高さ |
| **雪っぽさ（キューブ軽減）** | | |
| pieceSize | 0.155 | やや小さめ＝粒感 |
| jitter | 0.038 |  irregular |
| normalInset | 0.014 | 角の丸み |
| snowColor | (0.94,0.97,1) | 柔らかい白 |

---

## 使い方

1. **SnowPanicVibe → Open Avalanche_Test_OneHouse** でシーンを開く
2. **Play** で自動適用
3. 屋根を叩いて感覚を確認
4. 変更したい値は `ApplySnowFeelTuning()` 内を編集

---

## 成功条件チェック

- [ ] どこを叩くか少し考えたくなる
- [ ] うまく当てると大きく崩れて気持ちいい
- [ ] ただの連打より狙い撃ちの方が気持ちいい
- [ ] 見た目が少し雪らしくなった
