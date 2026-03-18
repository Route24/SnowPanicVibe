#!/usr/bin/env python3
"""
アシがコーディング完了時に呼ぶスクリプト。
current_status.txt を READY_FOR_UNITY_PLAY に更新する。
"""
import os
import datetime

automation_dir = os.path.dirname(os.path.abspath(__file__))
status_path = os.path.join(automation_dir, "current_status.txt")

def read_field(text, key):
    for line in text.splitlines():
        if line.startswith(key + "="):
            return line[len(key)+1:].strip()
    return ""

now = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")

existing = open(status_path).read() if os.path.exists(status_path) else ""
last_summary  = read_field(existing, "last_summary_at")
last_protocol = read_field(existing, "last_protocol_at")
last_approved = read_field(existing, "last_approved_at")

with open(status_path, "w", encoding="utf-8") as f:
    f.write(f"status=READY_FOR_UNITY_PLAY\n")
    f.write(f"updated_at={now}\n")
    f.write(f"last_summary_at={last_summary}\n")
    f.write(f"last_protocol_at={last_protocol}\n")
    f.write(f"last_approved_at={last_approved}\n")

print(f"[mark_ready] status=READY_FOR_UNITY_PLAY ({now})")
