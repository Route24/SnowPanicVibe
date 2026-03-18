#!/bin/bash
# approve_next_task.command
# ダブルクリックするだけで承認＋Cursorへ自動送信まで完結。追加メッセージ不要。

AUTOMATION_DIR="$(cd "$(dirname "$0")" && pwd)"
STATUS_FILE="$AUTOMATION_DIR/current_status.txt"
PROTOCOL_FILE="$AUTOMATION_DIR/current_protocol.txt"
FLAG_FILE="$AUTOMATION_DIR/approved.flag"
NOW=$(date "+%Y-%m-%d %H:%M:%S")

echo ""
echo "╔══════════════════════════════════════╗"
echo "║   SNOW PANIC - Approve Next Task     ║"
echo "╚══════════════════════════════════════╝"
echo ""

# --- ステータス確認 ---
if [ ! -f "$STATUS_FILE" ]; then
    echo "❌ ERROR: current_status.txt が見つかりません"
    echo "   Play→Stop を先に実行してください"
    sleep 5; exit 1
fi

STATUS=$(grep "^status=" "$STATUS_FILE" | cut -d= -f2)

if [ "$STATUS" != "WAITING_APPROVAL" ]; then
    echo "⚠️  承認待ち状態ではありません"
    echo "   現在のステータス: $STATUS"
    echo ""
    [ "$STATUS" = "IDLE" ] || [ "$STATUS" = "RUNNING" ] && \
        echo "   → Play→Stop 後に再度ダブルクリックしてください"
    sleep 5; exit 1
fi

if [ ! -f "$PROTOCOL_FILE" ]; then
    echo "❌ ERROR: current_protocol.txt が見つかりません"
    sleep 5; exit 1
fi

# --- プロトコルの目的・モジュールを表示 ---
echo "📋 次のタスク:"
echo ""
awk '/^目的:/{found=1; next} found && /^$/{exit} found{print "   " $0}' "$PROTOCOL_FILE"
echo ""
MODULE=$(awk '/^今回触るモジュール:/{found=1; next} found && /^$/{exit} found{print $0}' "$PROTOCOL_FILE" | head -3)
if [ -n "$MODULE" ]; then
    echo "🔧 触るモジュール:"
    echo "$MODULE" | while IFS= read -r line; do echo "   $line"; done
    echo ""
fi

# --- プロトコル種別を判定 ---
IMPL_TARGET=$(awk '/^実装対象:/{found=1; next} found && /^$/{exit} found{print $0}' "$PROTOCOL_FILE")
MODULE_CONTENT=$(awk '/^今回触るモジュール:/{found=1; next} found && /^$/{exit} found{print $0}' "$PROTOCOL_FILE")

IS_CHECK_ONLY=false
if echo "$IMPL_TARGET" | grep -q "なし" && echo "$MODULE_CONTENT" | grep -q "なし"; then
    IS_CHECK_ONLY=true
fi

# --- approved.flag 作成 ---
{
    echo "approved_at=$NOW"
    echo "protocol_file=$PROTOCOL_FILE"
    echo "is_check_only=$IS_CHECK_ONLY"
} > "$FLAG_FILE"

# --- RUNNING に遷移 ---
LAST_SUMMARY=$(grep "^last_summary_at=" "$STATUS_FILE" 2>/dev/null | cut -d= -f2-)
LAST_PROTOCOL=$(grep "^last_protocol_at=" "$STATUS_FILE" 2>/dev/null | cut -d= -f2-)
{
    echo "status=RUNNING"
    echo "updated_at=$NOW"
    echo "last_summary_at=$LAST_SUMMARY"
    echo "last_protocol_at=$LAST_PROTOCOL"
    echo "last_approved_at=$NOW"
} > "$STATUS_FILE"

echo "✅ 承認完了 ($NOW)"
echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  WAITING_APPROVAL → RUNNING"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo ""

# --- 確認系：手順を表示して READY_FOR_UNITY_PLAY へ ---
if [ "$IS_CHECK_ONLY" = true ]; then
    echo "👀 確認系タスクです:"
    echo ""
    awk '/^確認手順:/{found=1; next} found && /^$/{exit} found{print "   " $0}' "$PROTOCOL_FILE"
    echo ""
    awk '/^完了条件:/{found=1; next} found && /^$/{exit} found{print "   " $0}' "$PROTOCOL_FILE"
    echo ""
    {
        echo "status=READY_FOR_UNITY_PLAY"
        echo "updated_at=$NOW"
        echo "last_summary_at=$LAST_SUMMARY"
        echo "last_protocol_at=$LAST_PROTOCOL"
        echo "last_approved_at=$NOW"
    } > "$STATUS_FILE"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "  RUNNING → READY_FOR_UNITY_PLAY"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "🎮 Unity で Play してください"
    echo ""
    sleep 2; exit 0
fi

# --- 実装系：Cursor に自動送信 ---
echo "⚙️  実装系タスクです。Cursor のアシに自動送信します..."
echo ""

# Cursor を前面に出してチャットに送信
osascript <<APPLESCRIPT
tell application "Cursor"
    activate
end tell
delay 1
tell application "System Events"
    tell process "Cursor"
        keystroke "Automation/current_protocol.txt を読んで実行して"
        delay 0.3
        key code 36
    end tell
end tell
APPLESCRIPT

OSASCRIPT_EXIT=$?
if [ $OSASCRIPT_EXIT -eq 0 ]; then
    echo "✅ Cursor のアシに送信しました"
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "  アシが current_protocol.txt を処理開始"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "完了後に READY_FOR_UNITY_PLAY になります"
else
    echo "⚠️  Cursor への自動送信に失敗しました"
    echo "   Cursor のアシに手動で以下を送ってください:"
    echo ""
    echo "   「Automation/current_protocol.txt を読んで実行して」"
fi

echo ""
sleep 3
