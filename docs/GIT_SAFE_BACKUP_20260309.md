# Snow Panic Git 安全構成 + 1時間ごと自動バックアップ

> **コピーボタン**: 各コードブロック右上のコピーアイコンで全文コピーできます。

---

## 実装サマリ

- A) main は保護ブランチ、自動バックアップは **auto-backup** ブランチのみに push
- B) 1時間ごとに変更有無を確認し、変更がある場合のみ auto-backup へコミット・push
- C) 変更がない場合は何もしない
- D) main にいるときは stash → auto-backup でコミット → main に戻す
- E) main 以外のブランチでは現ブランチでコミットし、その内容を auto-backup へ push

---

## 初回セットアップ（まとめ）

```bash
# 1. auto-backup ブランチ作成・push
cd /Users/kenichinishi/unity/SnowPanicVibe
git checkout -b auto-backup
git push -u origin auto-backup
git checkout -

# 2. launchd 登録（1時間ごと）
ln -sf "$(pwd)/scripts/com.snowpanic.auto-commit-hourly.plist" ~/Library/LaunchAgents/
launchctl load ~/Library/LaunchAgents/com.snowpanic.auto-commit-hourly.plist

# 3. 動作確認
./scripts/auto-commit-hourly.sh
tail -5 scripts/logs/auto-commit.log
```

---

## 日常運用

| 操作 | コマンド |
|------|----------|
| 手動バックアップ | `./scripts/auto-commit-hourly.sh` |
| 自動バックアップ停止 | `launchctl unload ~/Library/LaunchAgents/com.snowpanic.auto-commit-hourly.plist` |
| 自動バックアップ再開 | `launchctl load ~/Library/LaunchAgents/com.snowpanic.auto-commit-hourly.plist` |

---

## 復旧手順（過去のバックアップへ戻す）

### 1. バックアップ履歴を確認

```bash
cd /Users/kenichinishi/unity/SnowPanicVibe
git fetch origin auto-backup
git log origin/auto-backup --oneline -20
```

### 2. 特定のコミット時点へ戻す（hard reset）

```bash
# 例: 1時間前のコミット abc1234 へ戻す
git checkout auto-backup
git reset --hard abc1234
git checkout -b recovery-from-backup   # 作業続行用
```

### 3. 特定のコミットを現在のブランチに取り込む（cherry-pick）

```bash
git cherry-pick abc1234
```

### 4. auto-backup の最新をマージして戻す

```bash
git checkout main   # または feature/xxx
git merge origin/auto-backup
```

---

## ファイル一覧（パスと内容）

### 1. scripts/auto-commit-hourly.sh

```bash
#!/usr/bin/env bash
#
# Snow Panic 自動バックアップ（1時間ごと想定）
# 変更がある場合のみ auto-backup ブランチに add / commit / push
# main には直接 push しない

set -e
REPO_PATH="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
LOG_DIR="${REPO_PATH}/scripts/logs"
LOG_FILE="${LOG_DIR}/auto-commit.log"
RUN_TIME=$(date -u "+%Y-%m-%dT%H:%M:%SZ")
TARGET_BACKUP_BRANCH="auto-backup"

mkdir -p "$LOG_DIR"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG_FILE"; }

trap 'last=$?; log "error=script_exit_code_$last"; exit 0' ERR

cd "$REPO_PATH" || { log "run_time=$RUN_TIME repo_path=$REPO_PATH error=cd_failed"; exit 0; }

CURRENT_BRANCH=$(git branch --show-current 2>/dev/null || echo "unknown")

# 変更チェック
CHANGES=$(git status --porcelain 2>/dev/null || true)
if [ -z "$CHANGES" ]; then
  log "run_time=$RUN_TIME repo_path=$REPO_PATH current_branch=$CURRENT_BRANCH target_backup_branch=$TARGET_BACKUP_BRANCH changes_detected=false files_changed_count=0 commit_created=false push_success=n/a error=none"
  exit 0
fi

FILES_CHANGED_COUNT=$(echo "$CHANGES" | wc -l | tr -d ' ')

# 変更ファイル名からカテゴリを推定
get_category() {
  local files="$1"
  local cat=""
  echo "$files" | grep -qE "\.(cs|shader)$" && cat="${cat}script "
  echo "$files" | grep -qE "\.unity$" && cat="${cat}scene "
  echo "$files" | grep -qE "Roof|Cornice|Snow|Ground" && cat="${cat}snow "
  echo "$files" | grep -qE "Camera|Main Camera" && cat="${cat}camera "
  echo "$files" | grep -qE "Video|Pipeline|Record|gif" && cat="${cat}pipeline "
  echo "$files" | grep -qE "ProjectSettings|Packages|manifest" && cat="${cat}config "
  echo "$files" | grep -qE "\.md$|docs/" && cat="${cat}docs "
  [ -z "$cat" ] && cat="misc"
  echo "$cat" | sed 's/ *$//'
}

CATEGORY=$(get_category "$CHANGES")
COMMIT_MSG="Snow Panic auto backup $(date '+%Y-%m-%d %H:%M') ${CATEGORY} (${FILES_CHANGED_COUNT} files)"

# main の場合は auto-backup に切り替えてコミット（main を汚さない）
if [ "$CURRENT_BRANCH" = "main" ]; then
  git stash push -m "auto-backup-temp-$(date +%s)" -u 2>/dev/null || true
  git checkout -b "$TARGET_BACKUP_BRANCH" 2>/dev/null || git checkout "$TARGET_BACKUP_BRANCH" 2>/dev/null
  git stash pop 2>/dev/null || true
fi

git add -A 2>/dev/null || true
STATUS=$(git status --porcelain 2>/dev/null || true)
if [ -z "$STATUS" ]; then
  [ "$CURRENT_BRANCH" = "main" ] && git checkout main 2>/dev/null || true
  log "run_time=$RUN_TIME repo_path=$REPO_PATH current_branch=$CURRENT_BRANCH target_backup_branch=$TARGET_BACKUP_BRANCH changes_detected=true files_changed_count=$FILES_CHANGED_COUNT commit_created=false push_success=n/a error=add_no_diff"
  exit 0
fi

COMMIT_HASH=""
PUSH_SUCCESS="false"

if git commit -m "$COMMIT_MSG" 2>/dev/null; then
  COMMIT_HASH=$(git rev-parse --short HEAD 2>/dev/null || echo "unknown")
  if git push origin "HEAD:${TARGET_BACKUP_BRANCH}" 2>/dev/null; then
    PUSH_SUCCESS="true"
  else
    log "run_time=$RUN_TIME repo_path=$REPO_PATH current_branch=$(git branch --show-current 2>/dev/null) target_backup_branch=$TARGET_BACKUP_BRANCH changes_detected=true files_changed_count=$FILES_CHANGED_COUNT commit_created=true commit_hash=$COMMIT_HASH push_success=false error=push_failed"
    [ "$CURRENT_BRANCH" = "main" ] && git checkout main 2>/dev/null || true
    exit 0
  fi
else
  log "run_time=$RUN_TIME repo_path=$REPO_PATH current_branch=$CURRENT_BRANCH target_backup_branch=$TARGET_BACKUP_BRANCH changes_detected=true files_changed_count=$FILES_CHANGED_COUNT commit_created=false push_success=n/a error=commit_failed"
  [ "$CURRENT_BRANCH" = "main" ] && git checkout main 2>/dev/null || true
  exit 0
fi

# main から切り替えていたら戻す
[ "$CURRENT_BRANCH" = "main" ] && git checkout main 2>/dev/null || true

log "run_time=$RUN_TIME repo_path=$REPO_PATH current_branch=$CURRENT_BRANCH target_backup_branch=$TARGET_BACKUP_BRANCH changes_detected=true files_changed_count=$FILES_CHANGED_COUNT commit_created=true commit_hash=$COMMIT_HASH push_success=$PUSH_SUCCESS error=none"
```

### 2. scripts/com.snowpanic.auto-commit-hourly.plist

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>com.snowpanic.auto-commit-hourly</string>
  <key>ProgramArguments</key>
  <array>
    <string>/bin/bash</string>
    <string>/Users/kenichinishi/unity/SnowPanicVibe/scripts/auto-commit-hourly.sh</string>
  </array>
  <key>StartInterval</key>
  <integer>3600</integer>
  <key>RunAtLoad</key>
  <false/>
  <key>WorkingDirectory</key>
  <string>/Users/kenichinishi/unity/SnowPanicVibe</string>
  <key>StandardOutPath</key>
  <string>/Users/kenichinishi/unity/SnowPanicVibe/scripts/logs/launchd-stdout.log</string>
  <key>StandardErrorPath</key>
  <string>/Users/kenichinishi/unity/SnowPanicVibe/scripts/logs/launchd-stderr.log</string>
</dict>
</plist>
```

※ 他環境ではパスを自分のプロジェクトに変更してください。

### 3. .gitignore 主要項目（既存で問題なし）

- `[Ll]ibrary/` `[Tt]emp/` `[Oo]bj/` `[Ll]ogs/`
- `[Rr]ecordings/`
- `scripts/logs/`
- `mono_crash.*`

---

## ブランチ運用

| ブランチ | 用途 |
|----------|------|
| **main** | 安定版。直接 push しない。保護ブランチ。 |
| **auto-backup** | 自動バックアップ専用。1時間ごとの自動 push 先。 |
| **feature/xxx** | 手動作業・実験用。 |

---

## ログフォーマット

```
run_time=2026-03-09T16:00:00Z repo_path=/Users/kenichinishi/unity/SnowPanicVibe current_branch=feature/avalanche target_backup_branch=auto-backup changes_detected=true files_changed_count=5 commit_created=true commit_hash=abc1234 push_success=true error=none
```

---

## GitHub 保護設定（推奨）

- main: 保護ブランチ
- PR 必須
- approval 1
- force push 禁止

---

## トラブルシュート

| 状況 | 対処 |
|------|------|
| コミットされない | `git status` で変更を確認。`.gitignore` に入っていないか確認 |
| push が失敗 | `git remote -v` で origin を確認。認証（SSH/HTTPS）を確認 |
| launchd が動かない | `launchctl list | grep snowpanic` で登録確認。plist のパスが正しいか確認 |
