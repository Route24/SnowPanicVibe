# Snow Panic – Game Design Document

Version: 2.0  
Author: Ken & Noah  
Date: 2026-03

---

## 運用ルール（Update Rule）

**GDD を更新する際は、日本語版・英語版の両方を同時に更新すること。**

どちらか片方だけ更新することは禁止。

---

# 1. Game Overview

### 日本語

Snow Panic は、屋根に積もる雪を落として家を守る物理ベースのカジュアルゲーム。

プレイヤーは神視点で村を見守りながら屋根の雪を叩いて落とす。

ゲームの主な爽快感は「屋根の端から雪がドサッと落ちる瞬間」。

### English

Snow Panic is a physics-based casual game about clearing snow from rooftops to protect houses.

Players observe the village from a god-like perspective and knock snow off the roofs.

The core satisfaction is the moment when snow slides off the roof edge.

---

# 2. Core Gameplay

### 日本語

**操作**
・プレイヤー操作はクリックのみ
・クリック連打ゲームにはしない
・クリック後はクールタイムを設ける

**理由**
・連打だと戦略性が消える
・落雪の爽快感を見る時間を確保する
・プレイヤーが次の一手を考えるゲームにする

**ゲームの方向性**
Snow Panic は
「リアルシミュレーション」ではなく
「爽快パズル」

雪挙動はリアルより気持ちよさを優先する。

**落雪タイプ**
・小粒
・中塊
・大塊
のバリエーションを持たせる。

**雪表現の方針**
・**見た目は粒っぽく、内部ロジックは軽量**
・フル粒子シミュレーションは不要
・屋根上の雪管理は軽い塊/セルベースを優先
・**崩壊時と落下時だけ**粒っぽい演出を足す

---

**フロー**

屋根クリック
↓
道具表示
↓
屋根を叩く
↓
雪が崩れる
↓
雪が斜面を滑る
↓
屋根の端から落下

### English

**Operation**
・Player input is click only
・Not a click-spam game
・Cooldown after each click

**Reason**
・Rapid clicks remove strategy
・Time to enjoy falling snow
・Player plans each move

**Game Direction**
Snow Panic is
"Refreshing Puzzle"
not "Real Simulation"

Snow behavior prioritizes feel over realism.

**Fall Types**
・Small particles
・Medium chunks
・Large blocks
Provide variety.

**Snow Representation Guidelines**
・**Grainy look, lightweight logic inside**
・No full particle simulation
・Roof snow management: lightweight blocks/cell-based first
・**Add particle-like effects only when collapsing and falling**

---

**Flow**

Click roof
↓
Tool appears
↓
Hit the roof
↓
Snow breaks
↓
Snow slides
↓
Snow falls from the edge

---

# 3. Player Interaction

### 日本語

プレイヤーは神視点。

男の子を直接操作しない。

屋根クリックで選択している道具を使用。

例
・スコップ
・棒

### English

The player controls the game from a god-like perspective.

The boy character is not directly controlled.

Clicking a roof uses the selected tool.

Examples
• shovel
• stick

---

# 4. World & Perspective

### 日本語

視点は「どうぶつの森型の斜め上視点」。

村全体を見渡せる。

### English

The game uses an Animal Crossing–style isometric perspective.

Players can view the whole village.

---

# 5. Main Character

### 日本語

主人公は留守番している男の子。

男の子は家の前をうろうろ歩く。

プレイヤーは男の子を助ける立場。

### English

The protagonist is a boy who is staying home alone.

The boy walks around in front of the house.

The player protects the boy.

---

# 6. Snow System

### 日本語

降雪イベントにより屋根に雪が積もる。

雪は斜面方向に滑る。

### English

Snow accumulates on roofs during snowfall events.

Snow slides along roof slopes.

---

# 7. House Damage System

### 日本語

積雪が増えると屋根にダメージが蓄積。

一定量を超えると家が潰れる。

### English

Accumulated snow increases roof stress.

If snow weight exceeds a limit, the house collapses.

---

# 8. Game Loop

### 日本語

降雪
↓
積雪増加
↓
雪下ろし
↓
家を守る
↓
また降雪

### English

Snowfall
↓
Snow accumulation
↓
Snow clearing
↓
House saved
↓
More snowfall

---

# 9. Progression

### 日本語

序盤
自分の家1軒

中盤
隣の家

進行すると村6軒の雪下ろしを任される。

### English

Early game
one house

Mid game
neighbor houses

Later
player is responsible for six houses in the village.

---

# 10. Events

### 日本語

イベント例

・吹雪
・大量降雪
・急激な積雪

### English

Examples

• blizzard
• heavy snowfall
• rapid accumulation

---

# 11. Tools

### 日本語

使用可能な道具

・スコップ
・棒
・その他の雪下ろし道具

### English

Examples

• shovel
• stick
• other snow tools

---

# 12. Emotional Design

### 日本語

男の子のワイプを画面に表示。

表情が状況を表す。

安全 🙂  
雪増加 😟  
危険 😰  
崩壊寸前 😱  
成功 😄  

### English

A boy reaction window appears on screen.

Expressions indicate danger level.

Safe 🙂  
Snow building 😟  
Danger 😰  
Collapse risk 😱  
Success 😄  

---

# 13. Visual Direction

### 日本語

村の冬景色。

雪が積もる屋根。

雪が滑る物理表現。

### English

Snow village.

Winter atmosphere.

Sliding snow physics.

---

# 14. Audio Direction

### 日本語

雪が滑る音
雪が落ちる音
屋根がきしむ音

### English

Snow sliding.

Snow falling.

Roof creaking.

---

# 15. Platform Strategy

### 日本語

最初は Steam でリリース。

成功した場合スマホ版（iPhoneなど）へ展開。

### English

Initial release on Steam.

Later expansion to mobile platforms (iPhone etc).
