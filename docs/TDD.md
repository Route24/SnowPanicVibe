# Snow Panic! - Technical Design Document

Version: 1.0  
Date: 2026-03-05  
Author: Ken & Noah  
Reference: [GAME_DESIGN_DOCUMENT.md](./GAME_DESIGN_DOCUMENT.md)

---

# 1. Overview

This document defines the Unity implementation plan for Snow Panic!, a physics-based snow removal game. It aligns with the Game Design Document and describes architecture, systems, and implementation phases.

---

# 2. Target Platform

- Primary: Steam (PC) — Unity Standalone
- Future: iOS, Android (Input abstraction required)

---

# 3. Architecture Overview

## 3.1 Core Systems

| System | Responsibility | Key Scripts |
|--------|----------------|-------------|
| Roof Snow | Snow accumulation, sliding, hit response | RoofSnowSystem, RoofSnow, CorniceSnowManager |
| Ground Snow | Temporary accumulation of fallen snow | GroundSnowSystem, GroundSnowAccumulator |
| Snowfall | Event-based snowfall during gameplay | SnowFallSystem |
| Input | Mouse click → tool hit | CorniceHitter, TapToSlideOnRoof |
| Chunk Motion | Physics-based chunk sliding/falling | MvpSnowChunkMotion, SnowPackFallingPiece |
| Spawner | Snow pack generation | SnowPackSpawner |

## 3.2 Scene Structure

```
Scene
├── Village (6 houses)
├── Camera (side-scroll TBD)
├── UI (score, reaction window)
├── Systems (RoofSnowSystem, GroundSnowSystem, SnowFallSystem, ...)
└── Bootstrap (SnowMvpBootstrap, SnowMinReproBootstrap)
```

---

# 4. Implementation Phases

## Phase 1: Core Physics
- [ ] Snow chunk sliding on roof angle
- [ ] Chunk falling from eaves
- [ ] Ground landing and temporary accumulation
- [ ] Chunk disappearance after short time
- [ ] Satisfying feel (tuning mass, friction, forces)

## Phase 2: Game Loop
- [ ] Score / pocket money on snow removal
- [ ] Avalanche combo bonus
- [ ] Time Attack mode (90s)
- [ ] Endless mode skeleton
- [ ] Game Over on house collapse

## Phase 3: 6-House System
- [ ] Multi-house layout
- [ ] Per-house snow state
- [ ] Collapse detection per house

## Phase 4: Camera / Scrolling
- [ ] Side-scroll camera (final implementation TBD)
- [ ] View bounds and house visibility

## Phase 5: Art Pass
- [ ] Visual polish
- [ ] Tool animations (shovel, etc.)
- [ ] Title screen / opening sequence

## Phase 6: Sound
- [ ] Snow hit SFX
- [ ] Avalanche SFX
- [ ] Ambient / music

## Phase 7: Visual Effects
- [ ] Snow particles
- [ ] Impact effects

## Phase 8: Polish
- [ ] Balancing
- [ ] Performance
- [ ] Localization (if needed)

---

# 5. Data Flow

## 5.1 Snow Removal Flow

```
Player Click → CorniceHitter / TapToSlideOnRoof
  → Snow hit / chunk spawn
  → MvpSnowChunkMotion / SnowPackFallingPiece (slide → fall)
  → GroundSnowSystem (landing)
  → Despawn after timeout
  → Score update
```

## 5.2 Snowfall Flow

```
SnowFallSystem (event-driven)
  → SnowfallEventBurst
  → Roof accumulation (RoofSnowSystem / CorniceSnowManager)
  → Level: light snow / heavy snowstorm
```

---

# 6. Key Technical Decisions

| Topic | Decision | Notes |
|-------|----------|-------|
| Physics | Unity Physics (Rigidbody, Collider) | Chunk motion, roof angle, ground |
| Snow representation | Cornice segments / chunks | Per-roof, slide-aware |
| Input | Mouse (Raycast) | Future: touch abstraction |
| Scoring | Event-driven | Normal +10, avalanche combo |
| House collapse | Weight threshold TBD | Single collapse = Game Over |

---

# 7. Pending Implementation Details

- [ ] Final scrolling implementation
- [ ] UI layout (HUD, reaction window)
- [ ] Difficulty curve (Endless mode)
- [ ] Tool upgrade balancing (Bare hands → Stick → Shovel → Large shovel → Bomb)
- [ ] House collapse weight threshold

---

# 8. References

- [GAME_DESIGN_DOCUMENT.md](./GAME_DESIGN_DOCUMENT.md)
- [SNOW_PACK_NOA_REPORT.md](./SNOW_PACK_NOA_REPORT.md)
- [ASSI_REPORT_STACK_BURST_ROOF.md](./ASSI_REPORT_STACK_BURST_ROOF.md)
