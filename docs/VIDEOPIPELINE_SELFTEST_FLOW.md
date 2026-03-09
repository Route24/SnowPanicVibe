# VideoPipeline SelfTest フロー（必須実装チェック）

## 前提
SelfTest 押下で Unity は自動で Play に入る（ユーザー操作不要）。

## 最終形
SelfTest 1ボタンで:
Play開始 → 録画開始 → 10秒 → 録画停止 → mp4確定生成 → 日付アーカイブ（1日1本） → preview生成 → result=OK

---

## 必須実装（録画が止まってファイル確定するまで保証）

| # | 要件 | 実装 | 場所 |
|---|------|------|------|
| 1 | Play開始検知 | EditorApplication.playModeStateChanged(EnteredPlayMode) | OnPlayModeStateChanged |
| 2 | Recorder.Start() | _controller.StartRecording() | StartRecordingThisSession |
| 3 | 10秒待機 | elapsed >= RecordTimeoutSeconds(10) または SetRecordModeToTimeInterval(0,10) | OnUpdate |
| 4 | Recorder.Stop() | _controller.StopRecording() | OnUpdate (Recording分岐) |
| 4b | フラッシュ待ち | RecorderFlushDelaySec(3秒) 後に ExitPlaymode | StopRecording 後 |
| 5 | mp4ポーリング | File.Exists && size>0, timeout=20s, interval=0.5s | FindExpectedOrNewestMp4, WaitMp4 |
| 6 | local_mp4_path / local_mp4_exists | _localPath, _localMp4Exists=true | mp4検出成功時 |

---

## 追加：履歴保存
- daily_archive_path = Recordings/snow_test_YYYYMMDD.mp4
- 同日が既に存在する場合はコピーしない（1日1本）
- daily_archive_created=true/false

## 追加：preview生成（VIDEO PREVIEW FOR NOA）
- mp4→preview.gif（ノア確認用・毎回必ず1つ）
  - 長さ 5秒、解像度 640px、fps 10
  - `ffmpeg -i snow_test_latest.mp4 -vf "fps=10,scale=640:-1:flags=lanczos" -t 5 preview.gif`
- ffmpeg fallback: PNG連番→ImageMagickでGIF→contact_sheet→qlmanage(macOS)→placeholder
- snow_test_latest.mp4 + preview.gif を Drive (gdrive:SnowPanicVideos/) にアップロード
- Slack: preview.gif リンク + mp4リンク
- ASSIレポート === VIDEO PREVIEW FOR NOA ===: preview_type, preview_path, preview_exists, preview_size_bytes, preview_drive_link

---

## ASSI REPORT ゴール
- result=OK
- local_mp4_exists=true
- local_mp4_path=（フルパス）
- local_mp4_size_bytes>0
- daily_archive_path=
- daily_archive_created=true/false
- preview_path=
- preview_created=true/false

---

## VIDEO FOR NOA / Drive 共有

### ノア参照可能にする
- `rclone link` はデフォルトで「アクセスにアカウント不要」の公開リンクを生成する
- direct_view_url / direct_download_url を ASSI レポート最上段の VIDEO FOR NOA に出力
- Drive アップロード成功時: upload_result=DRIVE_READY, final_result=DRIVE_READY
- Slack 投稿失敗は warning 扱い。Drive 成功なら全体を ERROR にしない

### 運用手順（リンクが開けない場合）
1. Google Drive で `SnowPanicVideos` フォルダを開く
2. フォルダ右クリック → 共有 → 「リンクを知っている全員」に変更
3. または `rclone backend copyid` でファイル ID を取得し、手動で共有設定を変更
