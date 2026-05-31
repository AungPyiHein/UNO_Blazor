---
name: Wild Draw 4 Challenge flow
description: How WD4 challenge state is managed across InternalPlayCard, HandleSpecialActions, EndPlayCardSequence, and ResolveChallengeAsync
---

**Flow:**
1. `InternalPlayCard` — snapshots `_wd4HandSnapshotHadMatchingColor` and `_wd4Player` BEFORE the card is removed from the player's hand.
2. `HandleSpecialActions` (WildDraw4 case) — sets `_wd4ChallengerIndex = GetNextPlayerIndex()`, sets `Status = WaitingForWd4Challenge`, fires CPU auto-challenge if needed, then **returns** (skips normal draw application).
3. `InternalFinalizeWildColor` — early-returns if status is WaitingForWd4Challenge.
4. `EndPlayCardSequence` — early-returns if status is WaitingForWd4Challenge (skips MoveToNextTurn).
5. `ResolveChallengeAsync(bool doChallenge)` — public method called by GameBoard (human) or HandleCpuWd4ChallengeAsync (CPU). Resolves bluff or non-bluff, applies draws, advances turn.

**Why:** The challenge must pause the entire turn pipeline; any path that would advance the turn must check for the pending challenge state.

**How to apply:** If adding new code paths after card play, always check `if (Status == GameStatus.WaitingForWd4Challenge) return;` before advancing turn.
