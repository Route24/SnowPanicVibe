# ASSI Fix Pack — Implementation Note (2026-03-05)

## Summary
- **Landing distance**: Velocity clamp at eaves + EavesCatchZone + air drag
- **Ground pile**: Already implemented (4s wait, 1s blink, kinematic on land)
- **Shadows**: Directional Light, snow pieces + roof particles cast/receive
- **Performance**: MaxActiveDynamicPieces=250, Discrete, Interpolation=None
- **Score UI**: Canvas + Text top-left "SCORE: N"

## Files Changed

| File | Changes |
|------|---------|
| SnowClump.cs | Velocity clamp (maxFallCarrySpeed, sideDamp, dropImpulse), airDrag when Falling, GetDynamicCount, LandNow kinematic/Sleep |
| RoofSnow.cs | Rigidbody Discrete+None interpolation, particle shadows, dynamic cap 250 |
| EavesCatchZone.cs | **New** — Trigger under eaves, applies drag for 0.3s |
| CorniceRuntimeSnowSetup.cs | EnsureShadowsEnabled, roof particle shadows, EnsureEavesCatchZone |
| CorniceSceneSetup.cs (Editor) | EavesCatchZone creation |
| SnowPhysicsScoreManager.cs | OnScoreChanged event |
| SnowScoreDisplayUI.cs | **New** — Canvas + Text "SCORE: N" top-left |

## Tuning Constants (Script Inspector / Code)

### SnowClump.cs (static, tunable)
| Constant | Value | Purpose |
|----------|-------|---------|
| maxFallCarrySpeed | 1.2f | Max downhill speed carried into fall |
| sideDamp | 0.2f | Sideways velocity multiplier at eaves |
| dropImpulse | 0.6f | Downward impulse at transition |
| airDrag | 1.2f | linearDamping while Falling |
| GroundPileWaitSeconds | 4.0f | Wait before blink |
| GroundPileBlinkDuration | 1.0f | Blink length |
| GroundPileBlinkInterval | 0.1f | Blink toggle interval |
| MaxActiveDynamicPieces | 250 | Cap for OnRoof+Falling |

### EavesCatchZone
| Constant | Value | Purpose |
|----------|-------|---------|
| dragMultiplier | 0.92f | Velocity *= this each frame in zone |
| applyDuration | 0.3f | Seconds to apply |

## Acceptance
- 80–90% of 10 pieces land within pile band under eaves
- Pieces remain, blink, disappear
- Roof + snow depth readable (shadows)
- FPS stable after 30s
- Score visible top-left
