# activePieces 判定ズレ診断パッチ

## 実装サマリ

- **LogActivePiecesDiagnostic**: activePieces=0 時、各 child がなぜ数えられないかを診断
- **onlyAnchorLeft 緩和**: rootChildren=1 かつ唯一の子が SnowPackAnchor の場合は FAIL しない（積雪ゼロの正常状態）
- FAIL 前に B2_ACTIVE_DIAG を必ず出力

---

## 変更ファイル

| # | file path |
|---|-----------|
| 1 | `Assets/Scripts/SnowPackSpawner.cs` |
| 2 | `docs/B2_ACTIVE_PIECES_DIAG.md` |
| 3 | `docs/B2_ACTIVE_PIECES_COPY.html` |

---

## 必須ログ（B2_ACTIVE_DIAG）

| キー | 説明 |
|------|------|
| rootChildren | 直下の子数 |
| activePieces | 有効 Renderer 数 |
| nonCountedChildren | 数えられなかった子数 |
| child_N_reason_not_counted | 各子の非カウント理由 |
| pooled_flag_count | プール済み数 |
| inactive_hierarchy_count | activeInHierarchy=false 数 |
| renderer_disabled_count | Renderer 無効数 |
| no_renderer_count | Renderer なし数 |
| anchor_count | SnowPackAnchor 数 |
| detached_but_should_survive_count |  detached 数 |
| landed_but_should_survive_count | landed ピース数 |
| count_rule_relaxed_test | 緩和で SnowPackPiece が1個以上か |
| activePieces_after_relax | 緩和時のカウント（SnowPackPiece の数） |

---

## 判定緩和

- **onlyAnchorLeft**: rootChildren==1 かつ GetChild(0).name=="SnowPackAnchor" → allCleared=true、FAIL しない
- 理由: SnowPackAnchor は積雪ではない。残りがアンカーのみ = 積雪ゼロの正常状態

---

## 各 child のログ項目

- name, activeSelf, activeInHierarchy
- renderer_enabled, collider_enabled
- pooled, detached, landed
- pos, scale
