# Snow Panic – Game Design Document

**Version:** 5.0  
**Author:** Ken & Noah  
**Date:** 2026-03

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

Steam で 10 万本を目指す構造を持つ。

### English

Snow Panic is a physics-based casual game about clearing snow from rooftops to protect houses.

Players observe the village from a god-like perspective and knock snow off the roofs.

The core satisfaction is the moment when snow slides off the roof edge.

The design targets 100,000 copies sold on Steam.

---

# 2. Core Game Structure（ゲーム構造）

### 日本語

Snow Panic のゲーム構造を以下の式で定義する。

```
爽快な破壊
＋
タイミングのリスク
＋
短時間プレイ
＝
高いリプレイ性
```

つまり

・**雪崩の気持ちよさ**  
・**通行人回避の緊張**  
・**短時間リプレイ**

この 3 つでゲームを成立させる。

### English

Snow Panic's game structure is defined by:

```
Satisfying Destruction
＋
Timing Risk
＋
Short Sessions
＝
High Replayability
```

In other words:

・**Avalanche satisfaction**  
・**Tension from avoiding villagers**  
・**Short-session replay**

These three pillars make the game work.

---

# 3. Core Gameplay Loop（コアゲームループ）

### 日本語

1. 屋根に雪が積もる  
2. プレイヤーが雪を叩く  
3. 雪にヒビが入る  
4. 雪崩が発生する  
5. 雪崩が連鎖する  
6. 通行人を避ける  
7. スコアを獲得する  
8. 次のチャンスが生まれる  

**1 プレイ時間：30 秒〜2 分**

### English

1. Snow accumulates on roofs  
2. Player hits the snow  
3. Snow cracks  
4. Avalanche begins  
5. Chain collapse occurs  
6. Player avoids villagers  
7. Score is gained  
8. A new opportunity appears  

**Typical session length: 30 seconds to 2 minutes**

---

# 4. Avalanche Feel System（雪崩の気持ちよさシステム）

### 日本語

雪崩の快感は以下の 5 段階で設計する。

| 段階 | 名称 | 内容 |
|------|------|------|
| ① | 予感（Anticipation） | 雪にヒビが入る・雪が少しズレる。プレイヤーが「大きい雪崩が来る」と感じる演出 |
| ② | 崩壊（Collapse） | 雪崩開始時：強い効果音・軽いカメラシェイク・短いズーム |
| ③ | 連鎖（Chain Reaction） | 雪は一度に落ちない。ドサ・ドサ・ドサと順番に落ちることで爽快感を作る |
| ④ | 巨大雪崩（Mega Avalanche） | 一定数以上の雪が崩壊すると MEGA AVALANCHE 発生。短いスローモーション・巨大スコア表示 |
| ⑤ | 余韻（Aftermath） | 崩壊後：雪煙・雪の音・スコア表示 |

### English

Avalanche satisfaction is designed in five stages:

| Stage | Name | Content |
|-------|------|---------|
| ① | Anticipation | Crack in snow, slight shift. Player senses "big avalanche coming" |
| ② | Collapse | When avalanche begins: strong impact sound, slight camera shake, quick zoom |
| ③ | Chain Reaction | Snow should not fall all at once. Blocks collapse sequentially (Thud / Thud / Thud) to create satisfaction |
| ④ | Mega Avalanche | Above threshold: MEGA AVALANCHE. Short slow motion, large score display |
| ⑤ | Aftermath | After collapse: snow particles, snow sound, then score display |

---

# 5. Avalanche Size Design（雪崩サイズ設計）

### 日本語

| 規模 | ブロック数 |
|------|------------|
| 小雪崩 | 10〜20 ブロック |
| 中雪崩 | 20〜50 ブロック |
| 大雪崩 | 50〜80 ブロック |
| 巨大雪崩 | 80〜150 ブロック |

### English

| Size | Blocks |
|------|--------|
| Small | 10–20 blocks |
| Medium | 20–50 blocks |
| Large | 50–80 blocks |
| Mega | 80–150 blocks |

---

# 6. Avalanche Puzzle System（雪崩パズルシステム）

### 日本語

Snow Panic のゲーム性を強化するため「雪崩パズル」の要素を導入する。

プレイヤーは単に雪を叩くのではなく

**「どこを崩すと大きな雪崩になるか」**

を考える必要がある。

**雪の構造**

雪は複数のブロックで構成されている。

ブロックは以下の関係を持つ。

・上の雪は下の雪に支えられている。

**崩壊ルール**

特定のブロックを崩すと周囲の雪が連鎖して崩れる。

例：弱い支点を崩す → 上の雪が落ちる → 隣の雪が崩れる → 大きな雪崩になる

**プレイヤーの戦略**

プレイヤーは小さい崩壊を繰り返すのではなく **「巨大雪崩を作る」** ことを狙う。

そのため

・弱い支点を探す  
・崩壊の方向を予測する  
・通行人のタイミングを見る  

という判断が必要になる。

**雪崩パズルの目的**

このシステムにより Snow Panic は単なるクリックゲームではなく **「物理パズルゲーム」** として成立する。

**理想的なプレイヤー体験**

プレイヤーは「ここを崩すと全部落ちるかもしれない」と予測する。

叩く → 雪が崩れる → 連鎖が始まる → 巨大雪崩になる

この瞬間がゲームの最大の快感になる。

### English

To deepen the gameplay of Snow Panic an additional system called **"Avalanche Puzzle System"** is introduced.

Players do not simply hit snow. They must think about

**"where to break the structure to create a large avalanche."**

**Snow Structure**

Snow is composed of multiple blocks. Each block supports other blocks.

Upper snow is supported by lower snow.

**Collapse Rule**

Breaking a critical block can trigger a chain collapse.

Example: Break a weak support → Upper snow falls → Neighboring blocks collapse → A large avalanche occurs

**Player Strategy**

Instead of triggering small collapses repeatedly players aim to create **Mega Avalanches**.

Players must

・find weak support points  
・predict collapse direction  
・watch villager timing  

**Purpose of Avalanche Puzzle**

This system ensures that Snow Panic becomes not just a click game but a **physics puzzle game**.

**Target Player Experience**

Players think "If I break this point, everything may fall."

They hit the snow. The structure collapses. A chain reaction begins. A massive avalanche occurs.

This moment becomes the core satisfaction of the game.

---

# 7. Villager Timing System（通行人タイミングシステム）

### 日本語

家の前の道を NPC が歩く。

**NPC 種類**

| 種類 | 特性 |
|------|------|
| 子供 | 歩行が速い |
| おばあちゃん | 歩行が遅い |
| 犬 | 突然走る |

**ゲームルール**

NPC に落雪が当たると
・スコア減点  
・コンボリセット  

### English

NPCs walk on the path in front of houses.

**NPC Types**

| Type | Behavior |
|------|----------|
| Child | Fast walking |
| Elderly | Slow walking |
| Dog | Sudden movement |

**Game Rules**

If villagers are hit by snow:
・Score Penalty  
・Combo Reset  

---

# 8. Timing Gameplay（タイミングゲームの定義）

### 日本語

Snow Panic は「叩くゲーム」ではなく **「落雪タイミングゲーム」** と定義する。

プレイヤーは
・雪崩を起こす  
・通行人を避ける  

この判断を行う。

安全に落雪すると **SAFE CLEAR ボーナス**。

### English

Snow Panic is defined as **"Snowfall Timing Game"** not "Tap Game".

The player decides:
・When to trigger an avalanche  
・When to avoid villagers  

Safe snowfall clears award **SAFE CLEAR bonus**.

---

# 9. Combo System（コンボシステム）

### 日本語

連続雪崩でコンボ。

**コンボ例**
・Avalanche x3  
・Avalanche x5  
・Avalanche x10  

**巨大雪崩発生時**：スローモーション演出

### English

Consecutive avalanches build combos.

**Examples:** Avalanche x3 / x5 / x10

**Mega Avalanche:** Slow motion effect

---

# 10. Score System（スコアシステム）

### 日本語

**加点**

| イベント | スコア |
|----------|--------|
| 基本落雪 | +10 |
| 中規模雪崩 | +50 |
| 大雪崩 | +100 |
| 巨大雪崩 | +300 |
| 安全クリア | +300 |

**ペナルティ**

| イベント | スコア |
|----------|--------|
| 通行人直撃 | -200 |

### English

**Positive**

| Event | Score |
|-------|-------|
| Basic Drop | +10 |
| Medium Avalanche | +50 |
| Large Avalanche | +100 |
| Mega Avalanche | +300 |
| Safe Clear | +300 |

**Penalty**

| Event | Score |
|-------|-------|
| Villager Hit | -200 |

---

# 11. Difficulty Curve（難易度カーブ）

### 日本語

| フェーズ | 家の数 | 追加要素 |
|----------|--------|----------|
| 序盤 | 1 軒 | — |
| 中盤 | 3 軒 | 通行人増加、雪量増加 |
| 後半 | 6 軒 | 通行人増加、雪量増加 |

### English

| Phase | Houses | Additional |
|-------|--------|------------|
| Early | 1 house | — |
| Mid | 3 houses | More villagers, more snow accumulation |
| Late | 6 houses | More villagers, more snow accumulation |

---

# 12. Viral Design（Steam バズ設計）

### 日本語

Snow Panic は **クリップ映えするゲーム** として設計する。

**バズ要素**
・巨大雪崩  
・ギリギリ回避  
・事故（犬ヒットなど）  
・巨大コンボ  

これらは **Stream Clip / YouTube Shorts / TikTok** 向けの瞬間。

### English

Snow Panic is designed as a **clip-worthy game**.

**Moments that create viral clips:**
・Mega avalanche  
・Near miss  
・Accidental dog hit  
・Huge combos  

Built for **Stream Clips / YouTube Shorts / TikTok**.

---

# 13. Steam Strategy（Steam 戦略）

### 日本語

| 項目 | 内容 |
|------|------|
| プラットフォーム | Steam |
| 価格 | 500 円 |
| 設計 | 衝動買い価格 |

成功した場合スマホ版（iPhone など）へ展開。

### English

| Item | Value |
|------|-------|
| Platform | Steam |
| Price | 500 yen |
| Design | Impulse purchase pricing |

Expand to mobile (iPhone etc.) on success.

---

# 14. Success Metrics（成功指標）

### 日本語

| 指標 | 目標 |
|------|------|
| Wishlist | 10,000 以上 |
| 売上目標 | 100,000 本 |
| レビュー目標 | 好評率 90% 以上 |

### English

| Metric | Target |
|--------|--------|
| Wishlist | 10,000+ |
| Sales | 100,000 copies |
| Reviews | 90% positive |

---

# 15. Core Gameplay（操作・雪表現）

### 日本語

**操作**
・クリックのみ  
・連打ゲームにしない  
・クールタイムを設ける  

**理由**
・連打だと戦略性が消える  
・落雪の爽快感を見る時間を確保  
・次の一手を考えるゲームにする  

**クールダウンシステム（Cooldown System）**

Snow Panic は連打ゲームではない。

各クリック後にウェイトタイマー（クールダウン）が発生する。

**目的**
・連打を防ぐ  
・戦略的な叩き位置を考えさせる  
・雪崩の物理挙動を観察する時間を作る  

ウェイト状態はUIメーターとして表示する。

**ゲームの方向性**
Snow Panic は「リアルシミュレーション」ではなく「爽快パズル」。  
雪挙動はリアルより気持ちよさを優先。

**雪表現の方針**
・見た目は粒っぽく、内部ロジックは軽量  
・屋根上の雪管理は軽い塊/セルベースを優先  
・崩壊時と落下時だけ粒っぽい演出を足す  

### English

**Operation:** Click only. No spam. Cooldown after each click.

**Reason:** Spam removes strategy. Time to enjoy falling snow. Player plans each move.

**Cooldown System**

Snow Panic is not a rapid tapping game.

After each hit, a cooldown timer is triggered.

**Purpose**
・Prevent spam clicking  
・Encourage strategic hits  
・Allow players to observe avalanche physics  

The cooldown is displayed visually as a UI meter.

**Direction:** "Refreshing Puzzle" not "Real Simulation". Feel over realism.

**Snow:** Grainy look, lightweight logic. Roof management: blocks/cell-based. Particle effects only when collapsing and falling.

---

# 16. Player Interaction（操作・入力システム）

### 日本語

プレイヤーは神視点。男の子を直接操作しない。

**入力システム（Input System）**

プレイヤーはキャラクターを操作しない。

**操作方法**
マウスで屋根をクリックする。

クリックされた位置に対して「雪を叩く」アクションが発生する。

この操作により
・雪が崩れる  
・雪の塊が落下する  
・連鎖雪崩が発生する

屋根クリックで選択している道具（スコップ、棒など）を使用する。

### English

God-view control. Boy not directly controlled.

**Input System**

The player does not control a character.

**Interaction method**  
Click directly on the roof using the mouse.

The clicked position becomes the impact point where the player hits the snow.

This action can trigger
・Snow detachment  
・Falling snow chunks  
・Chain avalanche events

Click roof to use selected tool (shovel, stick, etc.).

---

# 17. World & Perspective

### 日本語

視点は「どうぶつの森型の斜め上視点」。村全体を見渡せる。

### English

Animal Crossing–style isometric perspective. View the whole village.

---

# 18. Main Character（主人公・プレイヤー表現）

### 日本語

主人公は留守番している男の子。プレイヤーは男の子を助ける立場。

**プレイヤー表現（Player Representation）**

本ゲームでは、子供キャラクターはゲームフィールド上に存在しない。

代わりに、画面左上に表示される「リアクションウィンドウ（ワイプ）」として登場する。

このウィンドウはゲーム状況に応じて子供の表情が変化し、プレイヤーに感情的フィードバックを提供する。

**表示位置**  
画面左上

**役割**
・ゲーム状況の感情表現  
・危険状態の警告  
・成功時の喜び演出

**表情例**
🙂 安全  
😟 雪が増えている  
😰 危険状態  
😱 崩壊寸前  
😄 雪下ろし成功

### English

Protagonist: boy staying home alone. Player protects the boy.

**Player Representation**

The boy character does not exist in the game world.

Instead, the boy appears in a reaction window (wipe window) located at the top-left corner of the screen.

This window visually expresses the emotional state of the situation and provides feedback to the player through facial expressions.

**Location**  
Top-left corner of the screen

**Purpose**
・Emotional feedback  
・Danger warning  
・Celebration on success

**Example expressions**  
🙂 Safe  
😟 Snow increasing  
😰 Danger  
😱 Collapse imminent  
😄 Success

---

# 19. Snow System

### 日本語

降雪イベントにより屋根に雪が積もる。雪は斜面方向に滑る。

### English

Snow accumulates on roofs during snowfall. Snow slides along slopes.

---

# 20. House Damage System

### 日本語

積雪が増えると屋根にダメージが蓄積。一定量を超えると家が潰れる。

### English

Accumulated snow increases roof stress. House collapses if limit exceeded.

---

# 21. Events

### 日本語

イベント例：吹雪、大量降雪、急激な積雪

### English

Examples: blizzard, heavy snowfall, rapid accumulation

---

# 22. Tools（道具・アップグレードシステム）

### 日本語

使用可能な道具：スコップ、棒、その他の雪下ろし道具

**道具アップグレードシステム（Tool Upgrade System）**

プレイヤーは雪下ろしで得たお小遣い（スコア通貨）を使って道具をアップグレードできる。

**道具例**
・棒  
・スコップ  
・雪かきレーキ  
・プロ用除雪ツール  

**道具によって変化する要素**
・叩き範囲  
・破壊力  
・雪崩発生確率  

### English

Examples: shovel, stick, other snow tools

**Tool Upgrade System**

Players can upgrade tools using allowance money (score currency) earned from snow removal.

**Example tools**
・Stick  
・Shovel  
・Snow rake  
・Professional snow tool  

**Tools affect**
・Hit area  
・Impact power  
・Avalanche probability  

---

# 23. Emotional Design

### 日本語

男の子のワイプを画面に表示。表情が状況を表す。  
安全 🙂 / 雪増加 😟 / 危険 😰 / 崩壊寸前 😱 / 成功 😄

### English

Boy reaction window. Expressions indicate danger.  
Safe 🙂 / Building 😟 / Danger 😰 / Collapse risk 😱 / Success 😄

---

# 24. Visual Direction

### 日本語

村の冬景色。雪が積もる屋根。雪が滑る物理表現。

### English

Snow village. Winter atmosphere. Sliding snow physics.

---

# 25. Audio Direction

### 日本語

雪が滑る音、雪が落ちる音、屋根がきしむ音

### English

Snow sliding. Snow falling. Roof creaking.

---

# 26. Player Representation（プレイヤー表現・リアクションウィンドウ）

### 日本語

本ゲームでは、子供キャラクターはゲームフィールド上に存在しない。

代わりに、画面左上に表示される「リアクションウィンドウ（ワイプ）」として登場する。

このウィンドウはゲーム状況に応じて子供の表情が変化し、プレイヤーに感情的フィードバックを提供する。

**表示位置**  
画面左上

**役割**
・ゲーム状況の感情表現  
・危険状態の警告  
・成功時の喜び演出

**表情例**
| 状態 | 表情 |
|------|------|
| 安全 | 🙂 |
| 雪が増えている | 😟 |
| 危険状態 | 😰 |
| 崩壊寸前 | 😱 |
| 雪下ろし成功 | 😄 |

### English

The boy character does not exist in the game world.

Instead, the boy appears in a reaction window (wipe window) located at the top-left corner of the screen.

This window visually expresses the emotional state of the situation and provides feedback to the player through facial expressions.

**Location**  
Top-left corner of the screen

**Purpose**
・Emotional feedback  
・Danger warning  
・Celebration on success

**Example expressions**
| State | Expression |
|-------|------------|
| Safe | 🙂 |
| Snow increasing | 😟 |
| Danger | 😰 |
| Collapse imminent | 😱 |
| Success | 😄 |

---

# 27. Input System（入力システム）

### 日本語

プレイヤーはキャラクターを操作しない。

**操作方法**

マウスで屋根をクリックする。

クリックされた位置に対して「雪を叩く」アクションが発生する。

この操作により
・雪が崩れる  
・雪の塊が落下する  
・連鎖雪崩が発生する

### English

The player does not control a character.

**Interaction method**

Click directly on the roof using the mouse.

The clicked position becomes the impact point where the player hits the snow.

This action can trigger
・Snow detachment  
・Falling snow chunks  
・Chain avalanche events

---

# 28. Cooldown System（クールダウンシステム）

### 日本語

Snow Panic は連打ゲームではない。

各クリック後にウェイトタイマー（クールダウン）が発生する。

**目的**
・連打を防ぐ  
・戦略的な叩き位置を考えさせる  
・雪崩の物理挙動を観察する時間を作る

ウェイト状態は UI メーターとして表示する。

### English

Snow Panic is not a rapid tapping game.

After each hit, a cooldown timer is triggered.

**Purpose**
・Prevent spam clicking  
・Encourage strategic hits  
・Allow players to observe avalanche physics

The cooldown is displayed visually as a UI meter.

---

# 29. Tool Upgrade System（道具アップグレードシステム）

### 日本語

プレイヤーは雪下ろしで得たお小遣い（スコア通貨）を使って道具をアップグレードできる。

**道具例**
・棒  
・スコップ  
・雪かきレーキ  
・プロ用除雪ツール

**道具によって変化する要素**
・叩き範囲  
・破壊力  
・雪崩発生確率

### English

Players can upgrade tools using allowance money (score currency) earned from snow removal.

**Example tools**
・Stick  
・Shovel  
・Snow rake  
・Professional snow tool

**Tools affect**
・Hit area  
・Impact power  
・Avalanche probability

---

# 30. UI Layout（UI レイアウト）

### 日本語

**画面 UI 構成**

| 位置 | 要素 |
|------|------|
| 左上 | 子供リアクションウィンドウ |
| 中央上 | スコア |
| 右上 | コンボ表示 |
| 画面下 | 道具切り替え UI、ウェイトタイマー |

### English

**UI Layout**

| Position | Element |
|----------|---------|
| Top-left | Boy reaction window |
| Top-center | Score |
| Top-right | Combo indicator |
| Bottom area | Tool selection UI, Cooldown meter |

---

# 31. Snow Accumulation Meter（積雪メーター）

### 日本語

**概念**

積雪メーターは円グラフ（パイチャート）ではない。

雪の結晶（スノーフレーク）型の透明な容器である。

雪の結晶の形をした空の瓶を想像する。屋根に雪が積もるにつれ、その雪が容器の下から徐々に満たされていく。砂時計の下半分に砂が溜まるように、雪が容器内でゆっくり下方向に沈む。雪が一瞬で現れるのではなく、自然に沈みながら溜まる。目的は「雪が積もっている」ことを視覚的に強調すること。

**設計ルール**

| 状態 | 見た目 |
|------|--------|
| 初期 | 雪の結晶のアウトラインのみ表示。内部は空・透明 |
| 積雪増加 | 白い雪が下から上へ満たされる。雪の高さが自然に上昇 |
| 満タン | 雪の結晶容器が満杯 |
| 最大レベル | 赤色に変色し、パルス警告アニメーション開始 |
| 警告5秒継続 | 雪の重さで家が崩壊 |

**目的**

・積雪を直感的に把握  
・雪テーマとの一貫性  
・一般的なバー / 円グラフ風 UI の代わり

### English

**Concept**

The accumulation meter is NOT a pie chart.

It is a transparent container shaped like a snowflake.

Imagine an empty bottle shaped like a snowflake. As snow accumulates on the roof, snow gradually fills this snowflake-shaped container from the bottom upward. Similar to how sand fills the bottom half of an hourglass. Snow should slowly settle downward inside the container rather than appearing instantly. The purpose is to visually emphasize that snow is accumulating.

**Design Rules**

| State | Appearance |
|-------|------------|
| Initial | Only the snowflake outline is visible; inside is empty / transparent |
| Accumulation increasing | White snow fills from bottom to top. Snow level rises naturally |
| Full capacity | Snowflake container becomes full |
| Max level | Turns red and starts a pulsing warning animation |
| Warning 5 seconds | House collapses due to snow weight |

**Purpose**

・Make accumulation visually intuitive  
・Match the snow theme  
・Replace generic bar / pie-chart style UI
