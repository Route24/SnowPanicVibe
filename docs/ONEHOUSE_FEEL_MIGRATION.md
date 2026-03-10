# PhaseB2 卒業 → 1軒本番シーンへ

## 実装サマリ

- **SnowVerify_PhaseB2 は卒業**。基盤確認は完了。
- **Avalanche_Test_OneHouse** で気持ちよさ調整に戻る。
- 調整は **3項目のみ**: 一撃量・連鎖・塊感。
- キューブ感軽減は軽め（jitter のみ）。主目的は手触り。

---

## 変更ファイル

| # | file path |
|---|-----------|
| 1 | `Assets/Scripts/CameraMatchAndSnowConfig.cs` |
| 2 | `docs/ONEHOUSE_FEEL_MIGRATION.md` |
| 3 | `docs/ONEHOUSE_FEEL_COPY.html` |

---

## ワークフロー

1. **SnowPanicVibe → Open Avalanche_Test_OneHouse**
2. **Play** → 雪崩の気持ちよさを確認
3. 調整は `ApplySnowFeelTuning()` 内の3項目のみ

※ SnowVerify_PhaseB2 は掘り下げ停止。必要時のみ参照。

---

## 3項目の調整値（1軒のみ）

| 項目 | 値 | 狙い |
|------|-----|------|
| **一撃で剥がれる量** | hitRadiusR=0.9, min=16, max=60 | 叩く場所で変わる・狙いが必要 |
| **連鎖の起きやすさ** | chainChance=0.8, secondaryFrac=0.38, maxChain=30 | うまく当てると大きく連鎖 |
| **塊で落ちる気持ちよさ** | slideSpeed=1.02, burstChunk=42, burstSpeed=2.3 | 塊感・インパクト |
| **軽いキューブ軽減** | jitter=0.032 | わずかになめらか |

---

## 成功条件

- [ ] 1軒シーンに戻っている
- [ ] 叩く場所で崩れ方が少し変わる
- [ ] うまく当てると大きく崩れて気持ちいい
- [ ] 狙いがある方が楽しい

---

## PhaseB2 で得た安定要素（維持）

- total=0 エラー連打の抑制
- onlyAnchorLeft による activePieces=0 FAIL の緩和
- B2 診断ログ（必要時のみ PhaseB2 シーンで参照）
