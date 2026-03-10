# 雪の気持ちよさ調整（1軒本番シーン用）

## 実装サマリ

- **ApplySnowFeelTuning**: Avalanche_Test_OneHouse で自動適用
- **3項目のみ**: 一撃量・連鎖・塊感（PhaseB2卒業→本番手触り）
- キューブ軽減は jitter のみ軽く

---

## 変更ファイル

| # | file path |
|---|-----------|
| 1 | `Assets/Scripts/CameraMatchAndSnowConfig.cs` |
| 2 | `docs/SNOW_FEEL_TUNING.md` |
| 3 | `docs/SNOW_FEEL_COPY.html` |

---

## 調整項目と値（3項目+軽い jitter）

| 項目 | 調整後 | 狙い |
|------|--------|------|
| **1) 一撃で剥がれる量** | | |
| hitRadiusR | 0.9 | 叩く場所で変わる・狙いが必要 |
| localAvalancheMinDetach | 16 | 最低剥がれ量 |
| localAvalancheMaxDetach | 60 | 1撃上限（連鎖に委ねる） |
| **2) 連鎖の起きやすさ** | | |
| chainDetachChance | 0.8 | うまく当てると大きく連鎖 |
| secondaryDetachFraction | 0.38 | 二次崩壊 |
| maxSecondaryDetachPerHit | 30 | 連鎖キャパ |
| unstableRadiusScale | 1.45 | 不安定ゾーン |
| secondaryDetachDelaySec | 0.33 | 連鎖タイミング |
| unstableDurationSec | 1.4 | 連鎖継続 |
| **3) 塊で落ちる気持ちよさ** | | |
| localAvalancheSlideSpeed | 1.02 | 塊感・インパクト |
| burstChunkCount | 42 | 塊の数 |
| burstChunkSpeed | 2.3 | 落下速さ |
| **キューブ軽減（軽く）** | | |
| jitter | 0.032 | わずかになめらか |

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
