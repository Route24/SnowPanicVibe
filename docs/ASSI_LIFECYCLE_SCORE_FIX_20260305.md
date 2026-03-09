# ASSI — Ground Linger + Blink + Score UI Fix (2026-03-05)

## Summary
- **Lifecycle**: Removed TryRaycastGroundDeposit (caused premature/air landing). Ground transition ONLY via OnCollisionEnter + strict Update fallback.
- **States**: RoofSliding → Falling → Grounded → Despawning
- **Score UI**: Bootstrap order fixed, DontDestroyOnLoad, bold 28pt text

## Files Changed

| File | Changes |
|------|---------|
| SnowClump.cs | Removed TryRaycastGroundDeposit; state names (RoofSliding, Grounded, Despawning); stricter OnCollisionEnter (Ground layer, pile-on-pile); collider enabled for pile; Update fallback stricter (y<0.25, 0.5s settle) |
| SnowScoreDisplayUI.cs | EnsureBootstrapIfNeeded before create; DontDestroyOnLoad; font 28pt bold |
| SnowPhysicsScoreManager.cs | EnsureBootstrapIfNeeded() public; DontDestroyOnLoad |

## Lifecycle Flow

1. **RoofSliding** — on roof, sliding toward eaves
2. **Falling** — left roof, gravity on, air drag
3. **Grounded** — OnCollisionEnter with Ground/Plane/grounded pile OR (fallback) y<0.25 + 0.5s settle
   - rb.Sleep(), kinematic, collider enabled
   - Start WaitThenBlinkThenDespawn
4. **Despawning** — after 4s wait, during 1s blink
5. **Destroy** — AddScoreOnDespawn, FinalizeDepositAndDestroy

## Ground Detection (OnCollisionEnter)

- Ground layer (if exists)
- Name: Ground, Plane, Porch, Rock, Grass, Terrain
- Pile-on-pile: collision with grounded SnowClump
- Height: transform.position.y < 1.5f

## Constants (SnowClump.cs)

- GroundPileWaitSeconds = 4.0
- GroundPileBlinkDuration = 1.0
- GroundPileBlinkInterval = 0.1
- Fall timeout = 5s (safety)
