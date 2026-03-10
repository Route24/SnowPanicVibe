# Bug Origin Tracker System

## 実装サマリ

- **EVENT TRACE**: SnowHit, SnowDetach, SnowAvalanche, ScoreUpdate, ObjectSpawn, ObjectDestroy, SceneLoad を時系列記録
- **STATE SNAPSHOT**: エラー発生時に scene, active_snow_pieces, snow_root_children, score, cameraPos を保存
- **ERROR TRIGGER**: snow_piece_count=0, null_reference_exception で Bug Tracker 発動
- **ORIGIN ANALYSIS**: 直前イベントと possible_origin_script を出力
- **OBJECT TRACKING**: snow_root_children, snow_pieces_active, snow_pieces_destroyed を出力

---

## 追加ファイル

| パス |
|------|
| `Assets/Scripts/BugOriginTracker.cs` |

---

## 変更ファイル

| パス | 内容 |
|------|------|
| `Assets/Scripts/SnowPackSpawner.cs` | RecordEvent(SnowDetach/ObjectDestroy), OnSnowPiecesZero, ObjectSpawn |
| `Assets/Scripts/SnowPhysicsScoreManager.cs` | RecordScoreUpdate |
| `Assets/Scripts/RoofSnowSystem.cs` | RecordEvent(SnowAvalanche) |
| `Assets/Scripts/SnowLoopLogCapture.cs` | OnException, RecordSceneLoad |
| `Assets/Scripts/AIPipelineTestCollector.cs` | EmitEventTraceToReport, EmitObjectTrackingToReport |
| `Assets/Scripts/Editor/SnowLoopNoaReportAutoCopy.cs` | BuildEventTraceSection, BuildStateSnapshotSection, BuildBugOriginAnalysisSection, BuildObjectTrackingSection |

---

## テスト方法

1. **通常フロー**: Play → タップで雪崩 → Stop → ASSI Report に EVENT TRACE / OBJECT TRACKING を確認
2. **active=0 発動**: 雪を全て落として active_pieces=0 にする → BUG ORIGIN ANALYSIS / STATE SNAPSHOT を確認
3. **例外発動**: 意図的に NullReference を発生 → BUG ORIGIN ANALYSIS に last_events / possible_origin_script を確認

---

## ASSI REPORT サンプル

### EVENT TRACE

```
=== EVENT TRACE ===
time=12.44
event=SnowHit
object=SnowPiece_224
script=SnowPhysics.cs
position=(1.23,3.12,0.21)

time=12.46
event=SnowDetach
object=SnowPackPiece
script=SnowPackSpawner.cs
position=(1.20,3.10,0.20)
```

### BUG ORIGIN ANALYSIS（active=0 時）

```
=== BUG ORIGIN ANALYSIS ===
detected_error=SnowPiecesMissing
detail=snow_piece_count=0
last_events=
SnowDetach
SnowDetach
ObjectDestroy
possible_origin_script=
SnowPackSpawner.cs
RoofSnowSystem.cs
```

### STATE SNAPSHOT

```
=== STATE SNAPSHOT ===
scene=Avalanche_Test_OneHouse
active_snow_pieces=0
snow_root_children=1
score=125
cameraPos=(0,5.2,-5.8)
snow_pieces_destroyed=742
```

### OBJECT TRACKING

```
=== OBJECT TRACKING ===
snow_root_children=742
snow_pieces_active=742
snow_pieces_destroyed=0
```
