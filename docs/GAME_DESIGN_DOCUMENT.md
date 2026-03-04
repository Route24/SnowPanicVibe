# Snow Panic! – Game Design Document

Version: 1.2  
Author: Ken & Noah  
Date: 2026-03

---

# 1. Game Overview

Snow Panic! is a physics-based snow-clearing game.

The player must prevent village houses from collapsing under heavy snow.

Snow accumulates on rooftops, and the player clears it using various tools.

The core gameplay focuses on satisfying snow physics and avalanche mechanics.

Primary Platform:
Steam (PC)

Future Platform:
Mobile (tap / flick controls)

Development Priority:
Playable gameplay first.
Visual polish, sound, and effects will be implemented later.

---

# 2. Core Gameplay Loop

1. Snow accumulates on roofs
2. Player clears snow
3. Snow falls off roofs
4. Player earns money
5. Player buys tools
6. Snowstorms increase difficulty
7. Player prevents houses from collapsing

Game continues until a house collapses.

---

# 3. Player Interaction

Mouse click:

Click snow to strike it with the equipped tool.

The tool animation appears where the player clicks.

Snow detaches and slides down the roof.

Mobile version:

Tap or flick.

---

# 4. Gameplay Tempo Philosophy

Snow Panic! is not a rapid tapping game.

The intended gameplay tempo is thoughtful and observational.

Players should take a few seconds to think about:

- Which part of the roof to strike
- Which tool to use

Tools have a short cooldown, so players cannot continuously tap.

During this cooldown period, snow slowly slides down the roof.

Snow pieces may interact with each other and trigger chain reactions.

Small collapses can gradually grow into larger avalanches.

A key pleasure of the game is watching these slow chain reactions develop.

The player strikes the snow, then watches the situation evolve.

This creates a rhythm:

Think → Strike → Watch → Avalanche

The avalanche buildup is an essential part of the game's emotional payoff.

---

Implementation Notes (developer reference):

- Tools have cooldown timers.
- Snow sliding speed should be moderate (not too fast).
- Snow pieces can enter an unstable state after a hit.
- Unstable snow may detach later due to interaction with other pieces.
- Avalanche growth can happen during player cooldown.

---

# 5. Player Character

The player character is a boy around 10 years old.

The boy does not appear inside the gameplay world.

Instead he appears in a UI reaction window (wipe).

The boy reacts emotionally to gameplay events.

Examples:

- Surprise
- Happiness
- Panic
- Excitement

---

# 6. Village Setting

A peaceful snowy village.

Houses are arranged in a horizontal row.

Initial prototype:
Single house.

Final structure:
6 houses in a horizontal layout.

Background elements:

- Dog running around
- Villagers talking
- Children playing

---

# 7. Failure Condition

If the snow weight on any roof exceeds the threshold:

House collapses → Game Over.

---

# 8. Game Modes

Time Attack

Duration:
90 seconds

Goal:
Earn as much money as possible.

---

Endless Mode

Snowfall gradually increases.

Difficulty escalates over time.

Game continues until a house collapses.

---

# 9. Economy

Removing snow earns money.

Large avalanches give bonus rewards.

Example:

Avalanche Combo Bonus.

Money is used to purchase tools.

---

# 10. Tool System

Tools are divided into two categories.

Permanent Tools
Consumable Tools

---

Permanent Tools

Unlocked permanently after purchase.

Can be switched during gameplay.

Tools:

Hands
Stick
Shovel
Big Shovel
Blower

---

Blower

Blows snow using wind.

Instead of removing snow, it pushes snow across the roof.

Best used to trigger avalanches.

---

Consumable Tools

Single-use tools.

Must be repurchased after use.

Example:

Bomb

Bombs remove large amounts of snow instantly.

---

# 11. Snow Types

Different snow types affect gameplay.

Powder Snow
Light and easy to move.
Best tool: Blower.

Wet Snow
Heavy and sticky.
Best tool: Shovel.

Packed Snow
Hard compressed snow.
Best tool: Big Shovel.

Ice Snow
Rare large chunks.
May cause large avalanches.

---

# 12. Weather Events

Weather changes during gameplay.

Examples:

Light Snowfall
Heavy Snowfall
Blizzard
Clear Weather

Weather affects snow accumulation speed.

---

# 13. Avalanche System

Large snow movements trigger avalanche bonuses.

Conditions:

Large number of snow pieces falling simultaneously.

Effects:

Camera shake
Snow particle burst
Score multiplier
Boy reaction animation

---

# 14. Mission System

Small optional objectives appear during gameplay.

Examples:

Protect three houses.

Trigger a large avalanche.

Remove 500 units of snow.

Use blower to move snow.

Mission completion grants bonus money.

---

# 15. Camera

Horizontal scrolling camera.

Village houses arranged in a row.

Camera moves left and right across houses.

---

# 16. UI

HUD Elements:

Money counter
Timer
Tool selection
Combo indicator
Mission display

Reaction window (boy character).

---

# 17. Development Priority

Development focus order:

1 Core snow physics
2 Snow removal gameplay
3 House collapse system
4 Economy
5 Tools
6 Village layout
7 Camera
8 UI
9 Missions
10 Weather events

Art, sound, and visual polish will be implemented later.

---

# 18. Development Roadmap

Phase 1
Snow physics prototype

Phase 2
Core gameplay loop

Phase 3
Village layout

Phase 4
Camera

Phase 5
Tools

Phase 6
Missions

Phase 7
Weather system

Phase 8
Polish and balancing

---

# 19. Current Status

Core snow physics
In progress

Gameplay loop
Not started

Village layout
Not started

Tools
Not started

Weather system
Not started

UI
Not started
