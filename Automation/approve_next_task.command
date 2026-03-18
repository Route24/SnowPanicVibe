#!/bin/bash
# approve_next_task.command
# ダブルクリックするだけで承認＋実行まで完結。追加メッセージ不要。

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
# 「実装対象: - なし」または「今回触るモジュール: - なし」なら確認系
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

# --- 確認系プロトコルはそのまま手順を表示して完了 ---
if [ "$IS_CHECK_ONLY" = true ]; then
    echo "👀 確認系タスクです。以下の手順で確認してください:"
    echo ""
    awk '/^確認手順:/{found=1; next} found && /^$/{exit} found{print "   " $0}' "$PROTOCOL_FILE"
    echo ""
    echo "完了条件:"
    awk '/^完了条件:/{found=1; next} found && /^$/{exit} found{print "   " $0}' "$PROTOCOL_FILE"
    echo ""

    # READY_FOR_UNITY_PLAY に遷移
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
    sleep 3; exit 0
fi

# --- 実装系プロトコルは protocol_runner.py を呼ぶ ---
RUNNER="$AUTOMATION_DIR/protocol_runner.py"
if [ ! -f "$RUNNER" ]; then
    echo "⚠️  実装系タスクですが protocol_runner.py が見つかりません"
    echo "   Cursor のアシに以下を送ってください:"
    echo ""
    echo "   「approved.flag を確認して current_protocol.txt を実行して」"
    echo ""
    sleep 5; exit 1
fi

echo "⚙️  実装系タスクを実行中..."
echo ""
python3 "$RUNNER" "$PROTOCOL_FILE" "$STATUS_FILE"
EXIT_CODE=$?

if [ $EXIT_CODE -eq 0 ]; then
    {
        echo "status=READY_FOR_UNITY_PLAY"
        echo "updated_at=$(date '+%Y-%m-%d %H:%M:%S')"
        echo "last_summary_at=$LAST_SUMMARY"
        echo "last_protocol_at=$LAST_PROTOCOL"
        echo "last_approved_at=$NOW"
    } > "$STATUS_FILE"
    echo ""
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo "  RUNNING → READY_FOR_UNITY_PLAY"
    echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
    echo ""
    echo "🎮 Unity で Play して確認してください"
else
    {
        echo "status=ERROR"
        echo "updated_at=$(date '+%Y-%m-%d %H:%M:%S')"
        echo "last_summary_at=$LAST_SUMMARY"
        echo "last_protocol_at=$LAST_PROTOCOL"
        echo "last_approved_at=$NOW"
    } > "$STATUS_FILE"
    echo "❌ 実行失敗 (exit=$EXIT_CODE)"
    echo "   Cursor のアシに確認を依頼してください"
fi

echo ""
sleep 3
