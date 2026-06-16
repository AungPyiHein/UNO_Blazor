using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace UnoApp.Server.Hubs;

public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, List<string>> _rooms = new();
    private static readonly ConcurrentDictionary<string, int> _roomCpuCount = new();
    private static readonly ConcurrentDictionary<string, (string RoomCode, string PlayerName)> _connections = new();
    private static readonly ConcurrentDictionary<string, string> _roomRules = new();

    // Spectator tracking
    private static readonly ConcurrentDictionary<string, (string RoomCode, string Name)> _spectatorConnections = new();
    private static readonly ConcurrentDictionary<string, List<string>> _spectators = new();

    public async Task JoinRoom(string roomCode, string playerName)
    {
        roomCode   = roomCode.Trim().ToUpperInvariant();
        playerName = playerName.Trim();

        if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(playerName))
            return;

        _connections[Context.ConnectionId] = (roomCode, playerName);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        var players = _rooms.GetOrAdd(roomCode, _ => new List<string>());
        lock (players)
        {
            if (!players.Contains(playerName))
                players.Add(playerName);
        }

        await BroadcastPlayers(roomCode, players);

        // Send existing rules to the newly joined player
        if (_roomRules.TryGetValue(roomCode, out var existingRules))
            await Clients.Caller.SendAsync("RulesUpdated", existingRules);
    }

    public async Task JoinAsSpectator(string roomCode, string spectatorName)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        spectatorName = spectatorName.Trim();

        if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(spectatorName)) return;

        _spectatorConnections[Context.ConnectionId] = (roomCode, spectatorName);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        var specs = _spectators.GetOrAdd(roomCode, _ => new List<string>());
        int count;
        lock (specs)
        {
            if (!specs.Contains(spectatorName))
                specs.Add(spectatorName);
            count = specs.Count;
        }

        await Clients.Group(roomCode).SendAsync("SpectatorJoined", spectatorName, count);

        if (_roomRules.TryGetValue(roomCode, out var existingRules))
            await Clients.Caller.SendAsync("RulesUpdated", existingRules);
    }

    public async Task SetRoomRules(string roomCode, string rulesJson)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        _roomRules[roomCode] = rulesJson;
        await Clients.Group(roomCode).SendAsync("RulesUpdated", rulesJson);
    }

    public async Task LeaveRoom()
    {
        if (!_connections.TryRemove(Context.ConnectionId, out var info))
            return;

        await RemovePlayerFromRoom(info.RoomCode, info.PlayerName);
    }

    public async Task SendChatMessage(string roomCode, string playerName, string message)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        message  = message.Trim();
        if (string.IsNullOrEmpty(message) || message.Length > 200) return;
        await Clients.Group(roomCode).SendAsync("ChatMessage", playerName, message);
    }

    public async Task KickPlayer(string roomCode, string playerName)
    {
        roomCode   = roomCode.Trim().ToUpperInvariant();
        playerName = playerName.Trim();

        if (!_rooms.TryGetValue(roomCode, out var players)) return;

        List<string> snapshot;
        lock (players) { snapshot = players.ToList(); }

        // Only the host (index 0) may kick
        if (!_connections.TryGetValue(Context.ConnectionId, out var callerInfo)) return;
        if (snapshot.Count == 0 || snapshot[0] != callerInfo.PlayerName) return;

        // Find the kicked player's connection
        var kickedConnId = _connections
            .FirstOrDefault(kv => kv.Value.RoomCode == roomCode && kv.Value.PlayerName == playerName)
            .Key;

        if (kickedConnId == null) return;

        await Clients.Client(kickedConnId).SendAsync("PlayerKicked");
        await Groups.RemoveFromGroupAsync(kickedConnId, roomCode);
        _connections.TryRemove(kickedConnId, out _);
        await RemovePlayerFromRoom(roomCode, playerName);
    }

    public async Task StartGame(string roomCode, int cpuCount)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();

        if (!_rooms.TryGetValue(roomCode, out var players))
            return;

        _roomCpuCount[roomCode] = cpuCount;

        List<string> snapshot;
        lock (players) { snapshot = players.ToList(); }

        await Clients.Group(roomCode).SendAsync("GameStarted", snapshot.ToArray(), cpuCount);
    }

    public async Task SendMove(string roomCode, string moveJson)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(roomCode, out var players)) return;

        List<string> snapshot;
        lock (players) { snapshot = players.ToList(); }

        if (snapshot.Count == 0) return;

        var hostConnectionId = _connections
            .FirstOrDefault(kv => kv.Value.RoomCode == roomCode && kv.Value.PlayerName == snapshot[0])
            .Key;

        if (hostConnectionId != null)
            await Clients.Client(hostConnectionId).SendAsync("MoveReceived", moveJson);
    }

    public async Task RequestNextRound(string roomCode)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(roomCode, out var players)) return;

        List<string> snapshot;
        lock (players) { snapshot = players.ToList(); }

        if (snapshot.Count == 0) return;

        var hostConnectionId = _connections
            .FirstOrDefault(kv => kv.Value.RoomCode == roomCode && kv.Value.PlayerName == snapshot[0])
            .Key;

        if (hostConnectionId != null)
            await Clients.Client(hostConnectionId).SendAsync("NextRoundRequested");
    }

    public async Task SendStateToPlayer(string roomCode, int playerIndex, string stateJson)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(roomCode, out var players)) return;

        List<string> snapshot;
        lock (players) { snapshot = players.ToList(); }

        if (playerIndex < 0 || playerIndex >= snapshot.Count) return;

        var targetName = snapshot[playerIndex];
        var targetConnectionId = _connections
            .FirstOrDefault(kv => kv.Value.RoomCode == roomCode && kv.Value.PlayerName == targetName)
            .Key;

        if (targetConnectionId != null)
            await Clients.Client(targetConnectionId).SendAsync("StateUpdated", stateJson);
    }

    public async Task SendStateToSpectators(string roomCode, string stateJson)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        foreach (var kv in _spectatorConnections)
        {
            if (kv.Value.RoomCode == roomCode)
                await Clients.Client(kv.Key).SendAsync("StateUpdated", stateJson);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // Handle spectator disconnection
        if (_spectatorConnections.TryRemove(Context.ConnectionId, out var specInfo))
        {
            if (_spectators.TryGetValue(specInfo.RoomCode, out var specs))
            {
                int remaining;
                lock (specs)
                {
                    specs.Remove(specInfo.Name);
                    remaining = specs.Count;
                    if (remaining == 0) _spectators.TryRemove(specInfo.RoomCode, out _);
                }
            }
        }

        if (_connections.TryRemove(Context.ConnectionId, out var info))
            await RemovePlayerFromRoom(info.RoomCode, info.PlayerName);

        await base.OnDisconnectedAsync(exception);
    }

    private async Task RemovePlayerFromRoom(string roomCode, string playerName)
    {
        if (!_rooms.TryGetValue(roomCode, out var players))
            return;

        lock (players)
            players.Remove(playerName);

        if (players.Count == 0)
        {
            _rooms.TryRemove(roomCode, out _);
            _roomCpuCount.TryRemove(roomCode, out _);
            await Clients.Group(roomCode).SendAsync("RoomClosed");
            return;
        }

        await BroadcastPlayers(roomCode, players);
    }

    private async Task BroadcastPlayers(string roomCode, List<string> players)
    {
        List<string> snapshot;
        lock (players) { snapshot = players.ToList(); }
        await Clients.Group(roomCode).SendAsync("PlayersUpdated", roomCode, snapshot);
    }
}
