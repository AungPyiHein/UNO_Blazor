---
name: UnoGame HandleSpecialActions parameter name
description: The active player parameter in HandleSpecialActions is named currentPlayer, not player
---

`HandleSpecialActions(Player currentPlayer, UnoCard card, Player? targetPlayer)`

**Why:** Different from `InternalPlayCard` where the local variable is also `player`. Using `player` inside `HandleSpecialActions` causes CS0103.

**How to apply:** Always use `currentPlayer` when referencing the acting player inside `HandleSpecialActions`.
