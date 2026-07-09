---
name: Blazor animation re-entrancy via dual trigger paths
description: A Blazor component method invoked both directly (via @ref) and indirectly (via a changed DTO parameter) can run twice concurrently for the same logical event.
---

If a parent both (a) calls a child component's public animation/trigger method directly via `@ref`,
and (b) also pushes a state DTO to that same child whose fields the child watches in
`OnParametersSetAsync` to fire the *same* trigger, the two paths can race: the direct call runs
first, and while it's mid-flight (awaiting some intermediate step), the parent rebuilds/reassigns
its state object using a "pending trigger" field that hasn't been cleared yet — causing the child's
parameter-watch guard to see a "new" trigger value and fire a second, overlapping invocation.

**Why:** the child's own `_lastSeenTrigger`-style guard only catches repeats coming through the
DTO path; it has no visibility into the direct `@ref` call, so the direct call's first invocation
is invisible to the guard that's supposed to prevent duplicates.

**How to apply:** don't rely on a "last seen value" comparison alone to dedupe a trigger. Add an
explicit "currently in flight" guard inside the invoked method itself (e.g. an
`_activeAnimType`/`_isRunning` field set at entry and cleared in a `finally`), so *any* call path
triggering the same logical event while one is already running is a no-op, regardless of how it was
invoked.
