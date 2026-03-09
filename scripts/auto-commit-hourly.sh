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
