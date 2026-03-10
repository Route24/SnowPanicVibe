# Change Scope Lock System

## 実装サマリ

- MODIFY TARGET 宣言（target_system, allowed_files, protected_systems）
- FILE CHANGE CHECK（git status で変更検出、protected 抵触で result=FAIL）
- CODE DIFF（git diff で追加行を出力）
- BUILD GUARD（unexpected_changes 時に PROTECTED SYSTEM MODIFIED 警告）

---

## 追加ファイル

| パス |
|------|
| `Assets/Editor/FileChangeChecker.cs` |

---

## 変更ファイル

| パス | 内容 |
|------|------|
| `Assets/Scripts/ModifyTargetDeclaration.cs` | SnowAvalanche を protected_systems に追加 |
| `Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs` | FILE CHANGE CHECK / CODE DIFF セクション追加 |

---

## セットアップ方法

1. **修正前に宣言**
   - 修正対象を決めたら、`ModifyTargetDeclaration` を設定:
   ```csharp
   ModifyTargetDeclaration.TargetSystem = "ScoreUI";
   ModifyTargetDeclaration.AllowedFiles = "ScoreManager.cs,ScoreUI.cs";
   ```

2. **Play → Stop**
   - レポート生成時に `git status` / `git diff` で変更ファイルを検出

3. **ASSI Report 確認**
   - FILE CHANGE CHECK で `result=PASS` / `FAIL` を確認
   - FAIL の場合は `unexpected_changes` を確認

---

## protected_systems → ファイル対応

| システム | 対象ファイル |
|----------|--------------|
| SnowPhysics | SnowPackFallingPiece, SnowClump, SnowPieceAutoSettle, SnowPhysicsScoreManager, GroundSnow*, MvpSnowChunkMotion, SnowFallSystem, SnowfallEventBurst, SnowDespawnLogger |
| SnowSpawner | SnowPackSpawner |
| SnowAvalanche | RoofSnowSystem, TapToSlideOnRoof, RoofSnow, AvalancheFeedback, RoofSnowCleanup, RoofSnowMaskController, RoofAlignToSnow |
| CameraController | CameraOrbit, CameraMatchAndSnowConfig |
| ParticleSystem | SnowVisual, RoofSnow |

---

## ASSI REPORT サンプル

### PASS 時

```
=== MODIFY TARGET ===
target_system=ScoreUI
allowed_files=ScoreManager.cs,ScoreUI.cs
protected_systems=SnowPhysics,SnowSpawner,SnowAvalanche,CameraController,ParticleSystem

=== FILE CHANGE CHECK ===
changed_files=ScoreUI.cs,ScoreManager.cs
unexpected_changes=
result=PASS

=== CODE DIFF ===
file=ScoreUI.cs
+ outlineWidth=2
+ outlineColor=black

file=ScoreManager.cs
+ _score++;
```

### FAIL 時

```
=== FILE CHANGE CHECK ===
changed_files=ScoreUI.cs,SnowPackSpawner.cs
unexpected_changes=SnowPackSpawner.cs
result=FAIL
```

Console に `[FileChangeChecker] PROTECTED SYSTEM MODIFIED: SnowPackSpawner.cs` が出力される。
