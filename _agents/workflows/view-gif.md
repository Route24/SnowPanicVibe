---
description: 瞬時にGIFファイル（動画）を参照・解析するプロトコル
---

# GIF動画解析最速プロトコル

Recordingsフォルダに置かれた特定のGIF動画を、AIエージェントが「無駄な探索や迷子にならずに」数秒〜数十秒で最速読み取り・解析するためのプロトコルです。

次回から「Recordingsにある xxx.gif を参照して」と指示された場合は、必ずこのプロトコルに従って動きます。

## 手順

1. 対象となる GIFファイル名 を確認し、その絶対パスが `/Users/kenichinishi/unity/SnowPanicVibe/Recordings/<ファイル名>` であることを特定する。

// turbo-all
2. `browser_subagent` ツールを起動し、以下の厳格な `Task` 指示を与える。

```text
Navigate EXACTLY to the URL `file:///Users/kenichinishi/unity/SnowPanicVibe/Recordings/<GIFファイル名>`.
DO NOT navigate to the root directory. DO NOT click through folders. 
Once the GIF is playing in the viewport, wait 1 second, capture 3 screenshots (using capture_browser_screenshot), and provide a highly detailed analysis of the animation.
CRITICAL RULE: If the URL fails to load or access is blocked, IMMEDIATELY exit and report the error. DO NOT attempt any click-based file system exploration.
```

3. 成功して戻ってきたサブエージェントのスクリーンショットと報告文を確認し、ユーザーにフィードバックする。

## なぜこのプロトコルが必要か？
- 従来はAIがディレクトリを1階層ずつ手作業で登り降りして探していたため、1回の確認に5〜10分もの時間がかかっていました。
- このプロトコルにより URL 直打ち を強制し、もしアクセス権限で弾かれた場合も無駄な自己修復（探索迷子）に入るのを禁止することで、結果を瞬時にユーザーに返すことができます。
