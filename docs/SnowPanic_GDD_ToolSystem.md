# Snow Panic – Tool System Design (Draft v1)

作成日: 2026-03-18  
ステータス: Draft

---

## ■ 結論

道具ごとの違いは「個別ロジック」ではなく、  
共通の SnowInteraction システムに対する入力パラメータの違いとして設計する。

---

## ■ 共通システム

### SnowInteraction

すべての道具は以下の入力に変換される：

| パラメータ | 説明 |
|---|---|
| `impact_position` | タップ位置（屋根上の正規化座標） |
| `impact_type` | vibration / cut / pull / explosion |
| `power` | low / medium / high |
| `radius` | 影響半径（small / narrow_line / wide_line / point / wide_circle） |
| `depth` | 剥離深度（shallow / deep / medium_to_deep） |
| `direction` | 力の方向ベクトル（省略時は重力方向） |

この入力に基づき、以下を**共通処理**として実行する：

- **detach**（剥離範囲）: impact_position + radius + depth で決まる
- **slide**（屋根上滑落）: power + direction で決まる
- **land**（軒下停止）: 全道具共通・軒下 eaveGuiY で停止

> ⚠️ 道具ごとの専用 detach / slide / land ロジックは禁止。  
> 違いはパラメータのみで表現する。

---

## ■ 道具一覧

### 🪵 木の棒（Stick）

```
type:   vibration
power:  low
radius: small
depth:  shallow
```

**効果:**
- 表面の雪のみ剥離
- 小規模滑落
- 条件次第で局所雪崩

---

### 🔪 スコップ（Cut Narrow）

```
type:   cut
power:  medium
radius: narrow_line
depth:  deep
```

**効果:**
- 切断ラインより下が滑落
- 屋根まで到達すると一気に崩壊

---

### 🪣 シャベル（Cut Wide）

```
type:   cut
power:  medium
radius: wide_line
depth:  deep
```

**効果:**
- 広範囲切断
- 大量滑落

---

### ⚓ アンカー（Anchor）

```
type:   pull
power:  high
radius: point
depth:  deep
```

**効果:**
- 下層をまとめて引き剥がす
- 自動発動

---

### 💣 爆弾（Explosion）

```
type:   explosion
power:  high
radius: wide_circle
depth:  medium_to_deep
```

**効果:**
- 範囲内を一気に剥離
- 連鎖的雪崩

---

## ■ 設計ルール（重要）

1. `detach` / `slide` / `land` の処理は**全道具共通**にする
2. 道具ごとの専用ロジックは**禁止**
3. 違いは**パラメータのみ**で表現する
4. プレイヤー操作は**統一（タップ）**
5. 新しい道具を追加する場合は、パラメータ定義のみ追加する（処理本体は変更しない）

---

## ■ 開発順序

| 順序 | 道具 | type | 優先理由 |
|---|---|---|---|
| 1 | Stick（木の棒） | vibration | 最もシンプル・基本動作確認 |
| 2 | Cut（スコップ） | cut | 切断ライン実装 |
| 3 | Explosion（爆弾） | explosion | 範囲剥離・連鎖テスト |
| 4 | Pull（アンカー） | pull | 自動発動ロジック |
| 5 | Wide Cut（シャベル） | cut | Cut の radius 拡張 |

---

## ■ 次の Safe 定義

```
safe_name: SAFE_TL_THICK_SNOW_POSITIONAL_FALL
```

**条件:**
- TL で積雪が厚い（視覚的に分かる）
- 叩く位置で落下位置が変わる（positional detach）
- 小規模 / 大規模の差がある（power 差の可視化）
- 軒下停止が維持されている（eave landing 回帰なし）

---

## ■ Snow State Definition

積雪の状態は「状態値（State）」として定義する。個別の家 ID によるハードコードは禁止。

| State | 説明 |
|---|---|
| `NORMAL` | 標準的な積雪。薄板ではなく通常の厚さ |
| `THICK` | 明確に厚い積雪。叩く前から視認できる |

**ルール:**

- 積雪の厚さは `snowState: NORMAL / THICK` で表現する
- Phase1 では TL に `THICK` を適用するが、**システムはどの屋根にも適用可能**にする
- `if (roofId == "Roof_TL")` のような家 ID によるハードコードは禁止
- 厚さパラメータ（例: `thickRatio`）は屋根ごとの設定値として渡す
- 将来的に全6軒を `THICK` にする場合も、コード変更なしでパラメータ変更のみで対応できること

**実装指針:**

```
// NG: ハードコード禁止
if (roofId == "Roof_TL") DrawThickSnow();

// OK: 状態パラメータで制御
if (roof.snowState == SnowState.THICK) DrawThickSnow(roof.thickRatio);
```

---

## ■ 関連ファイル

| ファイル | 役割 |
|---|---|
| `Assets/Scripts/WorkSnowForcer.cs` | 現在の屋根雪表示・タップ検出・落下・軒下停止 |
| `Assets/Art/RoofCalibrationData.json` | 各屋根の4点座標（正規化） |
| `docs/GAME_DESIGN_DOCUMENT.md` | ゲーム全体 GDD |
| `docs/SNOW_REPRESENTATION_DESIGN.md` | 雪表現設計 |
