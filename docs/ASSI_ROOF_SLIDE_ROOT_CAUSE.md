# ASSI - 屋根沿い滑落 真下落ち原因と対策

## 問題

雪が屋根を滑らず、屋根を通過したように真下へ落ちる。

## 切り分け結果

| 要因 | 状態 | 対策 |
|------|------|------|
| 1. 屋根 collider と雪 piece の接触不足 | 要因の可能性 | contactOffset 0.01→0.02 に拡大。spawn 位置を closest + up*0.03 で屋根面上に配置。 |
| 2. friction 不足 | 主因 | PhysicsMaterial dynamic 0.08→0.18, static 0.12→0.25 に増加。滑る前に少し「乗る」状態に。 |
| 3. 初速/重力が強すぎて接触前提が崩れている | 主因 | BeginFall の dropImpulse 0.6→0.35 に弱化。maxFallCarrySpeed 1.2→1.5 で滑落方向初速を優先。 |
| 4. piece の中心位置や spawn 位置が不適切 | 軽微 | ClosestPoint で屋根面上の最近接点を取得済み。 |
| 5. 屋根法線を無視して detach している | 非該当 | slideDownDirection = Vector3.ProjectOnPlane(Vector3.down, roofUp) で屋根傾斜に沿った方向を使用。 |

## 根本原因（root_cause_of_vertical_drop）

**BeginFall で Vector3.down * dropImpulse (0.6) を強く加算していたため、落下時に世界座標の真下成分が支配的になり、屋根傾斜方向の滑落感が失われていた。** あわせて、OffDistDropThreshold が 0.20 と厳しく、わずかな浮きで即 BeginFall が発火し、屋根面上での滑走時間が短かった。

## 実施した修正

- dropImpulse: 0.6 → 0.35（真下成分を弱化）
- maxFallCarrySpeed: 1.2 → 1.5（滑落方向初速を優先）
- OffDistDropThreshold: 0.20 → 0.28（落下判定の猶予拡大）
- OffDistGraceDuration: 0.25 → 0.4（接触安定の猶予延長）
- PhysicsMaterial: dynamic 0.18, static 0.25（滑る前に乗る）
- contactOffset: 0.02（屋根接触安定化）
- initialSlideSpeed / SlideSpeed: 若干上昇（雪塊・カスケード・雪崩）
- TryDetachByPressure: cooldown 0.08→0.06, pressure 0.45→0.38（塊感強化）

## 参考

- SnowClump.cs: BeginFall, FixedUpdate の屋根拘束
- RoofSnow.cs: SpawnSnowClump の PhysicsMaterial, Rigidbody, Collider 設定
