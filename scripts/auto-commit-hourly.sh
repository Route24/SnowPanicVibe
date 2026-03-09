#!/usr/bin/env bash
#
# Snow Panic 自動コミット（1時間ごと想定）
# 変更がある場合のみ add / commit / push
# launchd から呼ぶか、cron で定期実行する

set -e
REPO_PATH="${1:-$(cd "$(dirname "$0")/.." && pwd)}"
LOG_DIR="${REPO_PATH}/scripts/logs"
LOG_FILE="${LOG_DIR}/auto-commit.log"
RUN_TIME=$(date -u "+%Y-%m-%dT%H:%M:%SZ")

mkdir -p "$LOG_DIR"

log() { echo "[$(date '+%Y-%m-%d %H:%M:%S')] $*" | tee -a "$LOG_FILE"; }

# エラー時もログだけ残して終了（set -e を無効化）
trap 'last=$?; log "error=script_exit_code_$last"; exit 0' ERR

cd "$REPO_PATH" || { log "run_time=$RUN_TIME repo_path=$REPO_PATH error=cd_failed"; exit 0; }

# 変更チェック（.gitignore を尊重）
CHANGES=$(git status --porcelain 2>/dev/null || true)
if [ -z "$CHANGES" ]; then
  log "run_time=$RUN_TIME repo_path=$REPO_PATH changes_detected=false files_changed_count=0 commit_created=false push_success=n/a error=none"
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

FILES_SUMMARY=$(echo "$CHANGES" | awk '{print $2}' | head -5 | tr '\n' ' ')
CATEGORY=$(get_category "$CHANGES")
COMMIT_MSG="Snow Panic auto backup $(date '+%Y-%m-%d %H:%M') ${CATEGORY}"

git add -A 2>/dev/null || true
# 再度確認（add 後に空になる場合がある）
STATUS=$(git status --porcelain 2>/dev/null || true)
if [ -z "$STATUS" ]; then
  log "run_time=$RUN_TIME repo_path=$REPO_PATH changes_detected=true files_changed_count=$FILES_CHANGED_COUNT commit_created=false push_success=n/a error=add_no_diff"
  exit 0
fi

COMMIT_HASH=""
PUSH_SUCCESS="false"

if git commit -m "$COMMIT_MSG" 2>/dev/null; then
  COMMIT_HASH=$(git rev-parse --short HEAD 2>/dev/null || echo "unknown")
  if git push origin HEAD 2>/dev/null; then
    PUSH_SUCCESS="true"
  else
    log "run_time=$RUN_TIME repo_path=$REPO_PATH changes_detected=true files_changed_count=$FILES_CHANGED_COUNT commit_created=true commit_hash=$COMMIT_HASH push_success=false error=push_failed"
    exit 0
  fi
else
  log "run_time=$RUN_TIME repo_path=$REPO_PATH changes_detected=true files_changed_count=$FILES_CHANGED_COUNT commit_created=false push_success=n/a error=commit_failed"
  exit 0
fi

log "run_time=$RUN_TIME repo_path=$REPO_PATH changes_detected=true files_changed_count=$FILES_CHANGED_COUNT commit_created=true commit_hash=$COMMIT_HASH push_success=$PUSH_SUCCESS error=none"
