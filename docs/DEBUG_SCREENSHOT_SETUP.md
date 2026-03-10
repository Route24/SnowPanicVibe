# Debug Screenshot 自動保存・ASSI REPORT 出力

## 概要

Play 実行中/Stop 直後に Game View / Scene View / Console / Inspector のスクショを自動保存し、ASSI REPORT にパス・Drive 共有リンク（placeholder）を出力する機能。

---

## 変更ファイルパス

| 種別 | パス |
|------|------|
| 新規 | `Assets/Scripts/DebugScreenshotCapture.cs` |
| 新規 | `Assets/Editor/DebugScreenshotEditor.cs` |
| 変更 | `Assets/Scripts/SnowLoopLogCapture.cs` |
| 変更 | `Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs` |

---

## セットアップ手順

1. **Unity でプロジェクトを開く**
2. **シーンを開く**（例: Avalanche_Test_OneHouse）
3. **特に配置不要**  
   `DebugScreenshotCapture` は `SnowLoopLogCapture.Bootstrap` で自動生成される

---

## 動作フロー

### Game View
- **Play 開始 2 秒後**: `DebugScreenshotCapture` が自動キャプチャ
- **Stop 直前**: `OnApplicationQuit` で再度キャプチャ

### Scene / Console / Inspector
- **Stop 時（ExitingPlayMode）**: `DebugScreenshotEditor` が Editor ウィンドウをキャプチャ

### 保存先
- 固定: `{ProjectRoot}/Recordings/debug/`
- 例: `/Users/kenichinishi/unity/SnowPanicVibe/Recordings/debug/`

### ファイル名
- `gameview_latest.png` / `sceneview_latest.png` / `console_latest.png` / `inspector_latest.png`
- セッション別: `*_vp_play_YYYYMMDD_HHMMSS.png`

---

## ASSI REPORT 出力フォーマット

```
=== DEBUG SCREENSHOT STATUS ===
gameview_local_path=/path/to/Recordings/debug/gameview_latest.png
gameview_exists=true
gameview_drive_link=
gameview_drive_upload_success=false
error=none
gameview_session_path=/path/to/Recordings/debug/gameview_vp_play_20260309_143022.png

=== DEBUG SCREENSHOT [SCENEVIEW] ===
sceneview_local_path=/path/to/Recordings/debug/sceneview_latest.png
sceneview_exists=true
...

=== DEBUG SCREENSHOT [CONSOLE] ===
...

=== DEBUG SCREENSHOT [INSPECTOR] ===
...
```

---

## テスト手順

1. Unity で Play を開始
2. 2 秒待つ（Game View キャプチャ）
3. Stop を押す
4. 以下を確認:
   - `Recordings/debug/` に `gameview_latest.png`, `sceneview_latest.png`, `console_latest.png`, `inspector_latest.png` が存在する
   - ASSI Report ウィンドウに `=== DEBUG SCREENSHOT STATUS ===` セクションがある
   - 各 `*_local_path` に正しいパスが出力されている

---

## Drive 共有（今後の拡張）

- `tryDriveUpload` を有効にすると `drive_link=(not_implemented)` を出力
- 実装時: `DebugScreenshotCapture.EmitReport` および `DebugScreenshotEditor.EmitReport` 内で Drive API を呼び、`share_link` を設定

---

## モジュール化

- **再利用**: 他プロジェクトでも `DebugScreenshotCapture.cs` と `DebugScreenshotEditor.cs` をコピーして利用可能
- **設定**: Inspector で `captureDelayOnPlay`, `saveSessionCopy`, `tryDriveUpload`, `captureOnQuit` を変更可能
