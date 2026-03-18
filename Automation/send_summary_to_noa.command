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
CLIP_TEXT="【Snow Panic - Play/Stop レポート】

$SUMMARY

---
【ノアへの指示】
・上記は最新の自動生成 summary です
・noa_next_check は参考情報です（自動判定）
・以下の形式だけで返してください：

next_action=（次にやること1行）
ok=YES
（自動生成プロトコルで進めてよい場合）

または

next_action=（次にやること1行）
override=（変更点1行のみ）
（プロトコルを変えたい場合のみ）

・長文プロトコルは不要です
・ゲーム挙動の詳細説明も不要です
・判断だけ返してください"

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
