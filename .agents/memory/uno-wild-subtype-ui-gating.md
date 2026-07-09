---
name: Uno wild-card subtype UI gating
description: Client UI must mirror server-side subtype exceptions for wild cards (e.g. Vortex), not just the broad Color=="Wild" check.
---

In this UNO game, `UnoGame.InternalPlayCard` treats `CardValue.Vortex` as color-nullified and skips
color selection (`card.Color == CardColor.Wild && card.Value != CardValue.Vortex` gates the
`WaitingForColorSelection` transition). Client-side code that decides whether to show the color
picker (`GameView.razor`'s `OnCardClickedAsync`) must apply the same `Value != Vortex` exception,
not just check `Color == "Wild"` — otherwise the picker pops up for a card the server will never
ask a color for.

**Why:** Vortex is a Wild-colored card but has its own distinct effect (hand reshuffle) and the
server intentionally bypasses color selection for it.

**How to apply:** whenever adding a new wild-family card subtype (or auditing existing wild-gated UI
code), check both `Color == "Wild"` and `Value` against every place that gates wild-only UI/logic on
the client, cross-referencing server-side exceptions in `UnoGame.InternalPlayCard`.
