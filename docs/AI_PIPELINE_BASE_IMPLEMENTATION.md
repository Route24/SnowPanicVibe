# AI開発パイプライン 基盤実装

## 実装サマリ

- TEST RESULT システム: Play 終了時に TEST_SCENE_LOADED / TEST_CAMERA_LOCK / TEST_SCORE_UPDATE / TEST_SNOW_HIT を出力
- DEBUG EVIDENCE: 既存の gameview / console / inspector スクショ + ASSI REPORT 出力
- TARGET PROTECTION: MODIFY TARGET 宣言（target_system, allowed_files, protected_systems）
- DebugDiagnostics.cs: CaptureGameView / ReportTestResult / ReportSceneInfo / ReportCameraInfo
- ASSI REPORT 拡張: TEST RESULT / DEBUG SCREENSHOT STATUS / SCENE STATE / MODIFY TARGET セクションを追加

---

## 追加ファイル

| パス | 説明 |
|------|------|
| `Assets/Scripts/DebugDiagnostics.cs` | 共通診断モジュール |
| `Assets/Scripts/AIPipelineTestCollector.cs` | TEST RESULT / SCENE STATE 収集 |
| `Assets/Scripts/ModifyTargetDeclaration.cs` | 修正対象宣言 |

---

## 変更ファイル

| パス | 変更内容 |
|------|----------|
| `Assets/Scripts/SnowLoopLogCapture.cs` | AIPipelineTestCollector 追加、ModifyTargetDeclaration.EmitToReport() 呼び出し |
| `Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs` | BuildTestResultSection / BuildSceneStateSection / BuildModifyTargetSection 追加、各レポートにセクション追加 |

---

## セットアップ手順

1. Unity でプロジェクトを開く
2. 特別な設定不要（Bootstrap で自動登録）
3. Play 実行 → Stop → ASSI Report ウィンドウで確認

---

## テスト結果

- Play 1 回実行後、ASSI REPORT に以下が含まれること:
  - `=== TEST RESULT ===`
  - `=== DEBUG SCREENSHOT STATUS ===`
  - `=== SCENE STATE ===`
  - `=== MODIFY TARGET ===`

---

## ASSI REPORT サンプル

```
=== TEST RESULT ===
=== TEST RESULT [TEST_SCENE_LOADED] ===
expected: Avalanche_Test_OneHouse
result: PASS
value_scene_name=Avalanche_Test_OneHouse

=== TEST RESULT [TEST_CAMERA_LOCK] ===
expected: camera position unchanged
result: PASS
value_camPos=(...)
value_camEuler=(...)

=== TEST RESULT [TEST_SCORE_UPDATE] ===
expected: score increases
result: PASS
value_value_scoreBefore=0
value_value_scoreAfter=1

=== TEST RESULT [TEST_SNOW_HIT] ===
expected: snow pieces detach
result: PASS
value_activePieces=42

=== DEBUG SCREENSHOT STATUS ===
gameview_local_path=/Users/kenichinishi/unity/SnowPanicVibe/Recordings/debug/gameview_latest.png
sceneview_local_path=...
console_local_path=...
inspector_local_path=...

gameview_exists=True
sceneview_exists=True
console_exists=True
inspector_exists=True

=== SCENE STATE ===
scene_name=Avalanche_Test_OneHouse
root_object_count=15
active_snow_pieces=42
score_value=1

=== MODIFY TARGET ===
target_system=AIPipelineBase
allowed_files=DebugDiagnostics.cs,AIPipelineTestCollector.cs,ModifyTargetDeclaration.cs
protected_systems=SnowPhysics,SnowSpawner,CameraController,ParticleSystem
```

---

## コード全文

### DebugDiagnostics.cs

```csharp
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DebugDiagnostics
{
    public static void CaptureGameView()
    {
        try { DebugScreenshotCapture.CaptureGameView(true, false); } catch (Exception ex) { Debug.LogWarning($"[DebugDiagnostics] CaptureGameView: {ex.Message}"); }
    }
    public static void CaptureConsole() { }
    public static void CaptureInspector() { }

    public static void ReportTestResult(string testId, string expected, string result, params (string key, string value)[] values)
    {
        try
        {
            SnowLoopLogCapture.AppendToAssiReport($"=== TEST RESULT [{testId}] ===");
            SnowLoopLogCapture.AppendToAssiReport($"expected: {expected}");
            SnowLoopLogCapture.AppendToAssiReport($"result: {result}");
            foreach (var (k, v) in values) SnowLoopLogCapture.AppendToAssiReport($"value_{k}={v}");
        }
        catch (Exception ex) { Debug.LogWarning($"[DebugDiagnostics] ReportTestResult: {ex.Message}"); }
    }

    public static void ReportSceneInfo(string sceneName, int rootObjectCount, int activeSnowPieces, int scoreValue)
    {
        try
        {
            SnowLoopLogCapture.AppendToAssiReport("=== SCENE STATE ===");
            SnowLoopLogCapture.AppendToAssiReport($"scene_name={sceneName}");
            SnowLoopLogCapture.AppendToAssiReport($"root_object_count={rootObjectCount}");
            SnowLoopLogCapture.AppendToAssiReport($"active_snow_pieces={activeSnowPieces}");
            SnowLoopLogCapture.AppendToAssiReport($"score_value={scoreValue}");
        }
        catch (Exception ex) { Debug.LogWarning($"[DebugDiagnostics] ReportSceneInfo: {ex.Message}"); }
    }

    public static void ReportCameraInfo(Vector3 pos, Vector3 euler) { ... }
    public static int GetActiveSnowPiecesCount() { ... }
    public static int GetRootObjectCount() { ... }
}
```

### AIPipelineTestCollector.cs / ModifyTargetDeclaration.cs

- `AIPipelineTestCollector`: Start で初期値保存、OnApplicationQuit で EmitFinalReport → DebugDiagnostics 呼び出し
- `ModifyTargetDeclaration`: target_system, allowed_files, protected_systems を ASSI Report に出力

---

## 禁止事項遵守

- 雪サイズ: 変更なし
- UI: 変更なし
- Particle: 変更なし
- Spawn: 変更なし
