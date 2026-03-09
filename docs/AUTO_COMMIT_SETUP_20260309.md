# [Snow Panic / 1ファイル依頼] 1時間ごとの自動GitHubコミット運用

## 実装サマリ

- A) `.gitignore` に scripts/logs/、mono_crash.* を追加
- B) `scripts/auto-commit-hourly.sh` 自動コミットスクリプト作成
- C) `scripts/com.snowpanic.auto-commit-hourly.plist` launchd 設定
- D) `docs/AUTO_COMMIT_HOURLY.md` 使い方ドキュメント

---

## 作成ファイル一覧

| file path | 内容 |
|-----------|------|
| `scripts/auto-commit-hourly.sh` | 変更があれば add / commit / push。変更がなければスキップ。 |
| `scripts/com.snowpanic.auto-commit-hourly.plist` | 1時間ごと（3600秒）実行する launchd 設定 |
| `docs/AUTO_COMMIT_HOURLY.md` | セットアップ・運用手順 |
| `docs/AUTO_COMMIT_SETUP_20260309.md` | 本ドキュメント（変更サマリ） |

---

## .gitignore 追加内容

```
# Crash dumps
mono_crash.*

# Auto-commit logs (local only)
scripts/logs/
```

---

## セットアップ手順（コピー用）

```bash
# 1. plist のパスを自分の環境に合わせて編集
# scripts/com.snowpanic.auto-commit-hourly.plist 内の
# /Users/kenichinishi/unity/SnowPanicVibe を実際のパスに変更

# 2. launchd に登録
cd /Users/kenichinishi/unity/SnowPanicVibe
ln -sf "$(pwd)/scripts/com.snowpanic.auto-commit-hourly.plist" ~/Library/LaunchAgents/
launchctl load ~/Library/LaunchAgents/com.snowpanic.auto-commit-hourly.plist

# 3. 手動テスト（任意）
./scripts/auto-commit-hourly.sh
```

---

## ログ出力例

```
run_time=2026-03-09T15:30:00Z repo_path=/Users/kenichinishi/unity/SnowPanicVibe changes_detected=true files_changed_count=5 commit_created=true commit_hash=abc1234 push_success=true error=none
```

---

## コミットメッセージ例

```
Snow Panic auto backup 2026-03-09 15:30 script snow config
```

---

## 停止したい場合

```bash
launchctl unload ~/Library/LaunchAgents/com.snowpanic.auto-commit-hourly.plist
```
