# Snow Panic 1時間ごと自動コミット

## 目的

開発中に挙動がおかしくなったとき、1〜2時間前の状態へ戻しやすくするための自動バックアップ。

## 仕組み

- **1時間ごと**: `scripts/auto-commit-hourly.sh` が実行される
- **変更あり**: `git add` → `git commit` → `git push`
- **変更なし**: スキップ
- **エラー時**: ログのみ残し、次の1時間後に再試行

## セットアップ（Mac / launchd）

### 1. plist のパスを自分の環境に合わせる

`scripts/com.snowpanic.auto-commit-hourly.plist` を開き、以下のパスを自分のプロジェクトパスに変更:

```xml
<string>/Users/kenichinishi/unity/SnowPanicVibe/scripts/auto-commit-hourly.sh</string>
<string>/Users/kenichinishi/unity/SnowPanicVibe</string>
<string>/Users/kenichinishi/unity/SnowPanicVibe/scripts/logs/launchd-stdout.log</string>
<string>/Users/kenichinishi/unity/SnowPanicVibe/scripts/logs/launchd-stderr.log</string>
```

### 2. launchd に登録

```bash
cd /Users/kenichinishi/unity/SnowPanicVibe
ln -sf "$(pwd)/scripts/com.snowpanic.auto-commit-hourly.plist" ~/Library/LaunchAgents/
launchctl load ~/Library/LaunchAgents/com.snowpanic.auto-commit-hourly.plist
```

### 3. 停止する場合

```bash
launchctl unload ~/Library/LaunchAgents/com.snowpanic.auto-commit-hourly.plist
```

### 4. 手動実行（テスト）

```bash
/Users/kenichinishi/unity/SnowPanicVibe/scripts/auto-commit-hourly.sh
```

## ログ

| 出力先 | 内容 |
|--------|------|
| `scripts/logs/auto-commit.log` | 実行結果（変更有無・コミットハッシュ・エラー） |
| `scripts/logs/launchd-stdout.log` | launchd 標準出力 |
| `scripts/logs/launchd-stderr.log` | launchd 標準エラー |

## ログフォーマット

```
run_time=2026-03-09T15:30:00Z repo_path=/path/to/SnowPanicVibe changes_detected=true files_changed_count=5 commit_created=true commit_hash=abc1234 push_success=true error=none
```

## コミットメッセージ例

```
Snow Panic auto backup 2026-03-09 15:30 script snow config
```

## コミット対象外（.gitignore）

- Library, Temp, Obj, Logs, Builds
- Recordings（動画・gif 出力）
- UserSettings
- scripts/logs/（自動コミットログ）
- mono_crash.*（クラッシュダンプ）

## トラブルシュート

| 状況 | 対処 |
|------|------|
| コミットされない | `git status` で変更を確認。`.gitignore` に入っていないか確認 |
| push が失敗 | `git remote -v` で origin を確認。認証（SSH/HTTPS）を確認 |
| launchd が動かない | `launchctl list \| grep snowpanic` で登録確認。plist のパスが正しいか確認 |
