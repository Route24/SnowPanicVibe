# ASSI Physical Toy Loop — Implementation Note (2026-03-05)

## Target Loop (Completed)
Snowfall → Accumulation → Strike → Sliding → **Roof edge fall** → **Ground pile** → **Blink** → Despawn → **Score +1**

## Files Changed

| File | Changes |
|------|---------|
| `Assets/Scripts/SnowClump.cs` | GroundPile state, Wait→Blink→Despawn coroutine, eaves edge fall, global cap (400), ForceEarlyDespawn |
| `Assets/Scripts/SnowPhysicsScoreManager.cs` | **New** — +1 score per despawn, Debug.Log, bootstrap |
| `Assets/Scripts/RoofSnow.cs` | Global piece cap check, EvictOldestGroundPiecesIfNeeded |
| `Assets/Scripts/AssiDebugUI.cs` | Score display when F1 overlay ON |

## Key Constants (tunable)

| Constant | Location | Value |
|----------|----------|-------|
| GroundPileWaitSeconds | SnowClump.cs | 4.0 |
| GroundPileBlinkDuration | SnowClump.cs | 1.0 |
| GroundPileBlinkInterval | SnowClump.cs | 0.1 |
| MaxActiveSnowPieces | SnowClump.cs | 400 |
| OffDistDropThreshold | SnowClump.cs | 0.20 |
| NearEdgeMargin (×3 for eaves) | SnowClump.cs | 0.06 |

## Roof Edge / Eaves
- **EavesDropTrigger** (CorniceRuntimeSnowSetup, CorniceSceneSetup): BoxCollider trigger at roof eaves; when SnowClump enters → ForceDropFromEaves
- **Additional fall trigger**: When `IsNearColliderEdge` and `offDist > 0.08`, SnowClump transitions to Falling (gravity ON)

## Ground Collision
- Tags/names: "Ground", "Plane", "Porch", "Rock", "Grass" or `position.y < 0.5` with low speed
- On contact: state → GroundPile, freeze RB, disable collider, start Wait→Blink→Despawn

## Camera
- Default: `SetDioramaCamera` / `Set俯瞰Camera` — roof + ground in frame
- **SnowPanic > Reset Camera to 俯瞰 View** to reset

## Sanity Checklist
- [x] Strike 10 times → pieces slide → fall at eaves → land → blink → despawn
- [x] Score increases by 10 (Debug.Log `[SnowPhysicsScore] +1 total=N`)
- [x] Camera shows roof + ground (F1 for overlay + Score)
- [x] Piece cap 400 — oldest ground piles evicted when spawning at cap
