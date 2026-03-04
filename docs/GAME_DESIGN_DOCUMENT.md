# Snow Panic! - Game Design Document

Version: 1.0  
Date: 2026-03-05  
Author: Ken & Noah

---

# 1. Game Overview

Snow Panic! is a physics-based snow removal game.

The player removes snow from rooftops to prevent houses from collapsing under the weight.

The core appeal of the game is **the satisfying physics of snow sliding and falling**.

---

# 2. Platform

Primary platform:

Steam (PC)

Possible future ports:

- iOS
- Android

---

# 3. Game Field

Village with **6 houses**

Layout:

Horizontal row of houses.

Camera:

Side scrolling camera (final implementation TBD)

---

# 4. Initial Game State

When the game starts:

- Snow is NOT falling
- Snow is already accumulated on rooftops

The player begins immediately clearing snow.

---

# 5. Snow Physics

When the player clicks snow:

1. The snow is hit with a tool
2. It becomes a small snow chunk
3. It slides along the roof angle
4. It falls off the roof
5. It lands on the ground
6. It disappears after a short time

---

# 6. Ground Snow

Snow that falls from rooftops:

- temporarily accumulates on the ground
- disappears after a short time

---

# 7. Player Controls

Input:

Mouse click.

When snow is clicked:

A tool animation appears (e.g., shovel).

The tool **hits the snow and knocks it loose.**

---

# 8. Player Character Representation

The protagonist:

A boy around **10 years old**.

He does NOT appear as a controllable character.

Instead:

He appears in a **reaction window (wipe)** in the top corner of the screen.

Possible reactions:

- surprised
- happy
- worried
- excited

These reactions respond to gameplay events.

---

# 9. Snowfall System

At the start of the game:

No snowfall.

Snowfall occurs as events during gameplay.

Levels:

- light snow
- heavy snowstorm

---

# 10. Scoring System

Removing snow generates **pocket money**.

Example:

Normal snow removal  
+10 money

Large snow drop (avalanche)  
+combo bonus

---

# 11. Upgrade System

The player can spend money to upgrade snow removal tools.

Tool progression:

1. Bare hands
2. Stick
3. Shovel
4. Large shovel
5. Bomb (blasts large snow areas)

Upgrades increase:

- snow removal power
- efficiency

---

# 12. Failure Condition

If **even one house collapses** due to snow weight:

Game Over.

---

# 13. Game Modes

## Time Attack Mode

Duration:

90 seconds (temporary value)

Goal:

Earn the highest score within the time limit.

---

## Endless Mode

Inspired by Tetris style survival gameplay.

- snow falls continuously
- difficulty increases over time
- houses collapse if not maintained

---

# 14. Title Screen

Visual:

A peaceful snowy village.

Village life scenes:

- children playing
- villagers chatting outside houses
- the protagonist's dog running around

However:

Dark snow clouds gather in the distant sky.

A major snowstorm is coming.

---

# 15. Story

The village is peaceful.

The protagonist is a **10-year-old boy**.

His father goes to town to buy supplies.

The boy is left home alone.

Then a massive snowstorm approaches.

Snow begins to pile up on rooftops.

If the snow gets too heavy, houses will collapse.

The boy must protect the village by clearing snow.

---

# 16. Opening Sequence (10 seconds)

0:00  
Peaceful snowy village

children playing  
villagers chatting  
dog running

0:04  
camera pans to sky

dark snow clouds

0:07  
snow already piled on rooftops

0:09  
boy looks up at the sky

0:10  

Snow Panic!

---

# 17. Development Priority

1. Snow physics feel
2. Sound effects
3. Visual effects
4. Graphics

---

# 18. Development Phases

Phase 1  
Core physics

Phase 2  
Game loop

Phase 3  
6 house system

Phase 4  
Camera / scrolling

Phase 5  
Art pass

Phase 6  
Sound implementation

Phase 7  
Visual effects

Phase 8  
Polish

---

# 19. Pending Decisions

- final scrolling implementation
- UI layout
- difficulty curve
- tool upgrade balancing
