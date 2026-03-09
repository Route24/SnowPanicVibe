# SelfTest スクリプト特定・構成

## STEP1&2: SelfTestボタンを生成しているスクリプト

| 項目 | 内容 |
|------|------|
| **スクリプト** | `Assets/Editor/SnowPanicVideoPipelineSelfTest.cs` |
| **クラス** | `SnowPanicVideoPipelineSelfTest`（static） |
| **種類** | Editor スクリプト（[InitializeOnLoad]） |
| **UI** | MenuItem + AssiReportWindow ボタン |

### 呼び出し経路

1. **メニュー**: Unityメニュー → **SnowPanic** → **VideoPipeline** → **SelfTest**
2. **ASSI Report ウィンドウ**: Tools → ASSI Report の **SelfTest** ボタン

### ハンドラ

```csharp
[MenuItem("SnowPanic/VideoPipeline/SelfTest", false, 150)]
public static void RunSelfTest()
```

---

## STEP3–7: VideoPipeline 処理（既に組み込み済み）

`RunSelfTest()` 実行時のフロー:

1. Recordings フォルダ作成
2. Unity Recorder 開始
3. Play 開始（EditorApplication.EnterPlaymode）
4. 10秒録画
5. 録画停止
6. mp4 検出（snow_test.mp4 優先、20秒・500ms ポーリング）
7. 履歴保存（snow_test_YYYYMMDD.mp4、当日1本）
8. プレビュー生成（preview.gif or preview_frames/）
9. rclone アップロード
10. ASSI REPORT 更新・表示

---

## 関連ファイル

| ファイル | 役割 |
|----------|------|
| `Assets/Editor/SnowPanicVideoPipelineSelfTest.cs` | VideoPipeline 本体・SelfTest 処理 |
| `Assets/Scripts/Editor/AssiReportWindow.cs` | SelfTest ボタン配置・レポート表示 |
| `Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs` | レポート生成（VIDEO PIPELINE LOGS 含む） |
| `Assets/Scripts/VideoPipelineSelfTestMode.cs` | SelfTest 実行中フラグ |
| `Assets/Scripts/VideoPipelineSelfTestOverlay.cs` | 録画中のオーバーレイ表示 |

---

## ゴール達成

SelfTest ボタン1回で:

- Play → 録画 → mp4生成 → 履歴保存 → プレビュー生成 → ノア確認可能

が一括で実行される。
