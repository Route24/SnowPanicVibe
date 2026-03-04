# アシ依頼レポート: StackTrace + Burstサイズ + 屋根傾斜表示

## Console全文レポート（FULL MODE）

Play 停止時に **ASSI Report** ウィンドウが開き、以下が含まれる:
- **SUMMARY**: ACTIVE=0, PoolReturn件数, Destroy回数, caller/file:line, scale, roof値
- **ACTIVE ZERO CONTEXT**: activePieces=0 前後30行
- **FULL CONSOLE DUMP**: 直近300行（全文、フィルタなし）
- **ANALYSIS**: 既存の検証結果

`Application.SetStackTraceLogType` で Error/Exception を Full に設定済み。

---

## 実施内容

### 1) StackTrace（犯人が分かる形）

- **A.** `Debug.LogError(new System.Exception(...))` 形式に変更
  - `SnowPackSpawner.cs`: `ReturnToPool`, `LogRootMutation`, `SnowPackDestroy`（slideRoot破棄時）
- **B.** Unity Console: Stack Trace Logging を **Error: Full** に設定すること（⋮→Stack Trace Logging）
- **C.** 出力内容: 呼び出し元メソッド（caller）と file:line が含まれる想定

### 2) BurstSnowサイズ完全統一

- Falling / Packed / Burst の `localScale` を `pieceSize`（0.11）で統一
- 生成時・Pool復帰時に scale を再適用
- `[SnowPieceScale] kind=Falling/Packed/Burst scale=(x,y,z)` ログを追加（初回のみ）

### 3) 屋根傾斜の可視化

- `RoofSnowSystem.Update` で `Debug.DrawRay` を毎フレーム実行（1秒表示）:
  - **red**: roofNormal（屋根の法線 = transform.up）
  - **green**: roofForward（屋根面上の前方向 = transform.forward）
  - **blue**: worldUp（Vector3.up）

---

## レポート（Play実行後に記入）

| 項目 | Yes/No | 備考 |
|------|--------|------|
| Exception付きStackTraceで **caller** と **file:line** が出たか | | Consoleで Error を開き、スタックにファイル名と行番号が含まれるか確認 |
| Burstのscaleが Falling/Packed と一致したか | | 雪崩時・降雪時の見た目で判断。ログ `[SnowPieceScale]` で数値確認可 |
| roofNormal/roofUp/worldUp の3本rayが見えたか | | Scene ビューで屋根中央付近に赤・緑・青の線が1秒間表示されるか |

---

## 補足

- **Console Stack Trace**: メニュー ⋮ → Stack Trace Logging → Error を **Full** にすること
- **Debug.DrawRay**: Scene ビューで表示。Game ビューのみだと見えない場合あり（Gizmos を有効化）
- **Burst scale**: `snowPackSpawner.pieceSize`（デフォルト 0.11）を基準に Falling/Packed/Burst を統一
