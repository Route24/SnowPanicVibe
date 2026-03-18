#!/bin/bash
# send_summary_to_noa.command
# ダブルクリック1回で latest_summary.txt をクリップボードに入れ、ChatGPT を前面に出す。

AUTOMATION_DIR="$(cd "$(dirname "$0")" && pwd)"
SUMMARY_FILE="$AUTOMATION_DIR/latest_summary.txt"

echo ""
echo "╔══════════════════════════════════════╗"
echo "║   SNOW PANIC - Send Summary to Noa   ║"
echo "╚══════════════════════════════════════╝"
echo ""

# --- summary ファイル確認 ---
if [ ! -f "$SUMMARY_FILE" ]; then
    echo "❌ ERROR: latest_summary.txt が見つかりません"
    echo "   Play→Stop を先に実行してください"
    sleep 5; exit 1
fi

# --- クリップボードに入れるテキストを組み立てる ---
SUMMARY=$(cat "$SUMMARY_FILE")
CLIP_TEXT="latest_summary.txt を見て、次の一手を1つだけ決めて。

$SUMMARY"

# --- クリップボードへコピー ---
echo "$CLIP_TEXT" | pbcopy

echo "✅ クリップボードにコピーしました"
echo ""
echo "--- コピーした内容 ---"
echo "$CLIP_TEXT"
echo "----------------------"
echo ""

# --- ChatGPT を前面に出してペースト+Enter送信 ---
osascript <<'APPLESCRIPT'
tell application "ChatGPT"
    activate
end tell
delay 1
tell application "System Events"
    keystroke "v" using command down
    delay 0.5
    key code 36
end tell
APPLESCRIPT

echo "✅ ChatGPT にペーストして送信しました"
echo ""

sleep 2
