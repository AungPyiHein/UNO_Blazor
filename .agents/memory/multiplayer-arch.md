---
name: Multiplayer Architecture
description: How local multiplayer works — host-authority with per-player state broadcast over SignalR.
---

## Design

Host-authority model: the host browser tab runs the full `UnoGame` engine. Remote players are `Player` slots with `IsRemote = true, IsHuman = false`. When it's a remote player's turn, `StartTurnAsync` skips CPU logic (condition is now `!IsHuman && !IsRemote`). The game simply pauses, waiting for a network move.

## Data flow

1. Host's `_game.OnStateChanged` fires after every action.
2. `BroadcastStateToRemotePlayers()` calls `UnoGame.GetStateSnapshot(playerIndex)` for each remote player, serializes to JSON, sends via `LobbyService.SendStateToPlayerAsync(idx, json)` → hub routes to that player's connection.
3. Remote tab receives `StateUpdated` event → renders `GameStateDto`.
4. Remote player interacts → `LobbyService.SendMoveAsync(MoveDto)` → hub routes to host via `MoveReceived`.
5. Host's `ApplyNetworkMoveAsync` calls the appropriate `UnoGame` method.

## Key files

- `UnoApp/Multiplayer/MultiplayerModels.cs` — MoveDto, CardDto, GameStateDto
- `UnoApp/Services/LobbyService.cs` — singleton SignalR hub client
- `UnoApp.Server/Hubs/GameHub.cs` — JoinRoom, StartGame, SendStateToPlayer, SendMove
- `UnoApp/Pages/Lobby.razor` — room join/start UI
- `UnoApp/Pages/MultiplayerGame.razor` — non-host player game view
- `UnoApp/Pages/Home.razor` — host game; auto-starts via OnAfterRenderAsync if LobbyService.IsGameStarted

## Wild card handling

Remote player picks color LOCALLY before sending the play move: `{ type:"play", cardId:"...", color:"Red" }`. Host calls `PlayCardAsync(player, card, CardColor.Red)` — `declaredColor != null` bypasses the server-side color-selection state entirely.

## 7-swap handling

Remote players have `IsHuman = false`, so the game engine auto-picks a swap target (CPU path). The remote tab doesn't need a target picker.

**Why:** Keeps the engine untouched; all special decisions either happen locally on the remote tab (color pick) or fall back to CPU logic (swap target).

## WD4 Challenge

`GetStateSnapshot` sets `IsMyTurn = true` when `Status == WaitingForWd4Challenge && _wd4ChallengerIndex == forPlayerIndex`. Remote tab shows Challenge/Accept buttons on that condition.
