using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace UnoApp.Server.Hubs;

public class GameHub : Hub
{
    private static readonly ConcurrentDictionary<string, List<(string Name, string Id)>> _rooms = new();
    private static readonly ConcurrentDictionary<string, int> _roomCpuCount = new();
    private static readonly ConcurrentDictionary<string, (string RoomCode, string PlayerName, string PlayerId)> _connections = new();
    private static readonly ConcurrentDictionary<string, string> _roomRules = new();

    public async Task JoinRoom(string roomCode, string playerName, string playerId)
    {
        roomCode   = roomCode.Trim().ToUpperInvariant();
        playerName = playerName.Trim();
        playerId   = playerId.Trim();

        if (string.IsNullOrWhiteSpace(roomCode) || string.IsNullOrWhiteSpace(playerName))
            return;

        _connections[Context.ConnectionId] = (roomCode, playerName, playerId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomCode);

        var players = _rooms.GetOrAdd(roomCode, _ => new List<(string Name, string Id)>());
        lock (players)
        {
            if (!players.Any(p => p.Name == playerName))
                players.Add((playerName, playerId));
        }

        await BroadcastPlayers(roomCode, players);

        // Send existing rules and CPU count to the newly joined player
        if (_roomRules.TryGetValue(roomCode, out var existingRules))
            await Clients.Caller.SendAsync("RulesUpdated", existingRules);
        
        if (_roomCpuCount.TryGetValue(roomCode, out var cpuCount))
            await Clients.Caller.SendAsync("CpuCountUpdated", cpuCount);
    }

    public async Task SetRoomRules(string roomCode, string rulesJson)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        _roomRules[roomCode] = rulesJson;
        await Clients.Group(roomCode).SendAsync("RulesUpdated", rulesJson);
    }

    public async Task SetCpuCount(string roomCode, int cpuCount)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        
        if (!_rooms.TryGetValue(roomCode, out var players))
            return;

        List<(string Name, string Id)> snapshot;
        lock (players) { snapshot = players.ToList(); }

        // Only host can set CPU count
        if (!_connections.TryGetValue(Context.ConnectionId, out var callerInfo) || snapshot.Count == 0 || snapshot[0].Name != callerInfo.PlayerName)
            return;

        _roomCpuCount[roomCode] = cpuCount;
        await Clients.Group(roomCode).SendAsync("CpuCountUpdated", cpuCount);
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

        List<(string Name, string Id)> snapshot;
        lock (players) { snapshot = players.ToList(); }

        // Only the host (index 0) may kick
        if (!_connections.TryGetValue(Context.ConnectionId, out var callerInfo)) return;
        if (snapshot.Count == 0 || snapshot[0].Name != callerInfo.PlayerName) return;

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

        List<(string Name, string Id)> snapshot;
        lock (players) { snapshot = players.ToList(); }

        var names = snapshot.Select(p => p.Name).ToArray();
        var ids = snapshot.Select(p => p.Id).ToArray();
        await Clients.Group(roomCode).SendAsync("GameStarted", names, ids, cpuCount);
    }

    public async Task SendMove(string roomCode, string moveJson)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(roomCode, out var players)) return;

        List<(string Name, string Id)> snapshot;
        lock (players) { snapshot = players.ToList(); }

        if (snapshot.Count == 0) return;

        var hostConnectionId = _connections
            .FirstOrDefault(kv => kv.Value.RoomCode == roomCode && kv.Value.PlayerName == snapshot[0].Name)
            .Key;

        if (hostConnectionId != null)
            await Clients.Client(hostConnectionId).SendAsync("MoveReceived", moveJson);
    }

    public async Task RequestNextRound(string roomCode)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(roomCode, out var players)) return;

        List<(string Name, string Id)> snapshot;
        lock (players) { snapshot = players.ToList(); }

        if (snapshot.Count == 0) return;

        var hostConnectionId = _connections
            .FirstOrDefault(kv => kv.Value.RoomCode == roomCode && kv.Value.PlayerName == snapshot[0].Name)
            .Key;

        if (hostConnectionId != null)
            await Clients.Client(hostConnectionId).SendAsync("NextRoundRequested");
    }

    public async Task ReturnToLobby(string roomCode)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (_rooms.ContainsKey(roomCode))
            await Clients.Group(roomCode).SendAsync("ReturnToLobby");
    }

    public async Task SendEndGameVote(string roomCode, string voteType)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(roomCode, out var players)) return;
        if (!_connections.TryGetValue(Context.ConnectionId, out var callerInfo)) return;

        List<(string Name, string Id)> snapshot;
        lock (players) { snapshot = players.ToList(); }

        if (snapshot.Count == 0) return;

        var hostConnectionId = _connections
            .FirstOrDefault(kv => kv.Value.RoomCode == roomCode && kv.Value.PlayerName == snapshot[0].Name)
            .Key;

        if (hostConnectionId != null)
            await Clients.Client(hostConnectionId).SendAsync("EndGameVoteReceived", callerInfo.PlayerName, voteType);
    }

    public async Task SetPlayerReady(string roomCode, bool isReady)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (!_connections.TryGetValue(Context.ConnectionId, out var info)) return;
        await Clients.Group(roomCode).SendAsync("PlayerReadyUpdated", info.PlayerName, isReady);
    }

    public async Task SendStateToPlayer(string roomCode, int playerIndex, string stateJson)
    {
        roomCode = roomCode.Trim().ToUpperInvariant();
        if (!_rooms.TryGetValue(roomCode, out var players)) return;

        List<(string Name, string Id)> snapshot;
        lock (players) { snapshot = players.ToList(); }

        if (playerIndex < 0 || playerIndex >= snapshot.Count) return;

        var targetName = snapshot[playerIndex].Name;
        var targetConnectionId = _connections
            .FirstOrDefault(kv => kv.Value.RoomCode == roomCode && kv.Value.PlayerName == targetName)
            .Key;

        if (targetConnectionId != null)
            await Clients.Client(targetConnectionId).SendAsync("StateUpdated", stateJson);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (_connections.TryRemove(Context.ConnectionId, out var info))
            await RemovePlayerFromRoom(info.RoomCode, info.PlayerName);

        await base.OnDisconnectedAsync(exception);
    }

    private async Task RemovePlayerFromRoom(string roomCode, string playerName)
    {
        if (!_rooms.TryGetValue(roomCode, out var players))
            return;

        bool wasHost;
        lock (players)
        {
            wasHost = players.Count > 0 && players[0].Name == playerName;
            players.RemoveAll(p => p.Name == playerName);
        }

        if (players.Count == 0)
        {
            _rooms.TryRemove(roomCode, out _);
            _roomCpuCount.TryRemove(roomCode, out _);
            await Clients.Group(roomCode).SendAsync("RoomClosed");
            return;
        }

        await Clients.Group(roomCode).SendAsync("PlayerLeft", playerName, wasHost);
        await BroadcastPlayers(roomCode, players);
    }

    private async Task BroadcastPlayers(string roomCode, List<(string Name, string Id)> players)
    {
        List<(string Name, string Id)> snapshot;
        lock (players) { snapshot = players.ToList(); }
        
        var names = snapshot.Select(p => p.Name).ToList();
        var ids = snapshot.Select(p => p.Id).ToList();
        
        await Clients.Group(roomCode).SendAsync("PlayersUpdated", roomCode, names, ids);
    }
}
