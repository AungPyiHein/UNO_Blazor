using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace UnoApp.Server.Hubs;

public class GameHub : Hub
{
    // roomCode -> list of player names currently in the lobby
    private static readonly ConcurrentDictionary<string, List<string>> _rooms = new();

    // connectionId -> (roomCode, playerName) so we can clean up on disconnect
    private static readonly ConcurrentDictionary<string, (string RoomCode, string PlayerName)> _connections = new();

    /// <summary>
    /// Called by a client when they enter a room code and a display name.
    /// Adds them to the SignalR group and broadcasts the updated player list.
    /// </summary>
    public async Task JoinRoom(string roomCode, string playerName)
    {
        roomCode   = roomCode.Trim().ToUpperInvariant();
        playerName = playerName.Trim();

        if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(playerName))
            return;

        // Track connection → room/name mapping for disconnect cleanup
        _connections[Context.ConnectionId] = (roomCode, playerName);

        // Add to SignalR group so we can broadcast to the whole room
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        // Add player name to the room's list (thread-safe)
        var players = _rooms.GetOrAdd(roomCode, _ => new List<string>());
        lock (players)
        {
            if (!players.Contains(playerName))
                players.Add(playerName);
        }

        // Broadcast updated list to everyone in the room
        await BroadcastPlayers(roomCode, players);
    }

    /// <summary>
    /// Called by the host to remove themselves and close the lobby.
    /// </summary>
    public async Task LeaveRoom()
    {
        if (!_connections.TryRemove(Context.ConnectionId, out var info))
            return;

        await RemovePlayerFromRoom(info.RoomCode, info.PlayerName);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connections.TryRemove(Context.ConnectionId, out var info))
            await RemovePlayerFromRoom(info.RoomCode, info.PlayerName);

        await base.OnDisconnectedAsync(exception);
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private async Task RemovePlayerFromRoom(string roomCode, string playerName)
    {
        if (!_rooms.TryGetValue(roomCode, out var players))
            return;

        lock (players)
            players.Remove(playerName);

        // Clean up empty rooms
        if (players.Count == 0)
        {
            _rooms.TryRemove(roomCode, out _);
            await Clients.Group(roomCode).SendAsync("RoomClosed");
            return;
        }

        await BroadcastPlayers(roomCode, players);
    }

    private async Task BroadcastPlayers(string roomCode, List<string> players)
    {
        List<string> snapshot;
        lock (players) { snapshot = players.ToList(); }

        // "PlayersUpdated" -> (roomCode: string, players: string[])
        await Clients.Group(roomCode).SendAsync("PlayersUpdated", roomCode, snapshot);
    }
}
