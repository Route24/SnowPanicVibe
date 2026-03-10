# 自動スクショ基盤 動作確認レポート

## 1 of 1 完了 実装サマリ

- size_bytes を各スクショ種別ごとに REPORT に追加
- ASSI REPORT の DEBUG SCREENSHOT STATUS を必須フォーマットに統一
- drive_upload_success / error をセクション末尾に出力

---

## 変更ファイルパス

| 種別 | パス |
|------|------|
| 変更 | `Assets/Scripts/DebugScreenshotCapture.cs` |
| 変更 | `Assets/Editor/DebugScreenshotEditor.cs` |
| 変更 | `Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs` |

---

## 追加・変更コード（差分）

### DebugScreenshotCapture.cs
- `EmitReport` に `sizeBytes` パラメータを追加
- `{kind}_size_bytes={sizeBytes}` を REPORT に出力
- 保存成功時は `png.Length`、fallback 時は `FileInfo(path).Length` で取得

### DebugScreenshotEditor.cs
- `EmitReport` に `sizeBytes` パラメータを追加
- `{kind}_size_bytes={sizeBytes}` を REPORT に出力

### SnowLoopNoaReportAutoCopy.cs
- `BuildDebugScreenshotStatusSection` を全面改修
- ログから key=value を収集し、必須順序で再構成
- gameview/sceneview/console/inspector の local_path → exists → size_bytes → drive_link → drive_upload_success → error を出力

---

## テスト手順

1. Unity でシーンを開く
2. **Play** を実行
3. 2 秒待つ（Game View キャプチャ）
4. **Stop** を押す
5. ASSI Report ウィンドウが開く
6. 以下を確認:
   - `Recordings/debug/` に `gameview_latest.png`, `sceneview_latest.png`, `console_latest.png`, `inspector_latest.png` が存在する
   - 各ファイルのサイズが 0 より大きい
   - ASSI REPORT に `=== DEBUG SCREENSHOT STATUS ===` セクションがある

---

## ASSI REPORT サンプル（成功時）

```
=== DEBUG SCREENSHOT STATUS ===
gameview_local_path=/Users/kenichinishi/unity/SnowPanicVibe/Recordings/debug/gameview_latest.png
sceneview_local_path=/Users/kenichinishi/unity/SnowPanicVibe/Recordings/debug/sceneview_latest.png
console_local_path=/Users/kenichinishi/unity/SnowPanicVibe/Recordings/debug/console_latest.png
inspector_local_path=/Users/kenichinishi/unity/SnowPanicVibe/Recordings/debug/inspector_latest.png

gameview_exists=True
sceneview_exists=True
console_exists=True
inspector_exists=True

gameview_size_bytes=152340
sceneview_size_bytes=89210
console_size_bytes=67320
inspector_size_bytes=45120

gameview_drive_link=
sceneview_drive_link=
console_drive_link=
inspector_drive_link=

drive_upload_success=False
error=none
```

---

## 成功条件チェック

| 項目 | 最低成功 | 理想成功 |
|------|----------|----------|
| Game View 保存 | ✓ | ✓ |
| Scene View 保存 | - | ✓ |
| Console 保存 | ✓ | ✓ |
| Inspector 保存 | - | ✓ |
| REPORT に local_path | ✓ | ✓ |
| REPORT に size_bytes | ✓ | ✓ |
| Drive link | - | (未実装) |

---

## 禁止事項遵守

- 雪サイズ修正: なし
- UI修正: なし
- Particle修正: なし
