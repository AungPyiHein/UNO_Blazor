using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;
using UnoApp.Multiplayer;

namespace UnoApp.Services;

public class LobbyService : IAsyncDisposable
{
    private HubConnection? _hub;

    public event Action<string, List<string>>? PlayersUpdated;
    public event Action<int>? CpuCountUpdated;
    public event Action<string[], int>? GameStarted;
    public event Action? RoomClosed;
    public event Action? PlayerKicked;
    public event Action<string, string>? ChatMessageReceived;
    public event Action<string>? StateUpdated;
    public event Action<string>? MoveReceived;
    public event Action<string>? RulesUpdated;
    public event Action? NextRoundRequested;
    public event Action<string, string>? EndGameVoteReceived;
    public event Action<string, bool>? PlayerLeft;
    public event Action<string, bool>? PlayerReadyUpdated;
    public event Action? HubReconnecting;
    public event Action? HubReconnected;
    public event Action? ReturnedToLobby;
    public event Action? HubConnectionClosed;

    public string? CurrentRulesJson { get; private set; }

    public bool IsConnected => _hub?.State == HubConnectionState.Connected;
    public string? MyRoomCode { get; private set; }
    public string? MyPlayerName { get; private set; }
    public int MyPlayerIndex { get; private set; } = -1;
    public bool IsHost => MyPlayerIndex == 0;
    public bool IsGameStarted { get; private set; }
    public string[] AllPlayerNames { get; private set; } = Array.Empty<string>();
    public int CpuCount { get; private set; }

    public async Task ConnectAsync(string baseUri)
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }

        var hubUrl = baseUri.TrimEnd('/') + "/gamehub";

        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        _hub.On<string, List<string>>("PlayersUpdated", (room, players) =>
            PlayersUpdated?.Invoke(room, players));

        _hub.On<int>("CpuCountUpdated", (cpuCount) => {
            CpuCount = cpuCount;
            CpuCountUpdated?.Invoke(cpuCount);
        });

        _hub.On<string[], int>("GameStarted", (players, cpuCount) =>
            GameStarted?.Invoke(players, cpuCount));

        _hub.On("RoomClosed", () =>
            RoomClosed?.Invoke());

        _hub.On("PlayerKicked", () =>
            PlayerKicked?.Invoke());

        _hub.On<string, string>("ChatMessage", (sender, msg) =>
            ChatMessageReceived?.Invoke(sender, msg));

        _hub.On<string>("StateUpdated", json =>
            StateUpdated?.Invoke(json));

        _hub.On<string>("MoveReceived", json =>
            MoveReceived?.Invoke(json));

        _hub.On<string>("RulesUpdated", json => {
            CurrentRulesJson = json;
            RulesUpdated?.Invoke(json);
        });

        _hub.On("NextRoundRequested", () =>
            NextRoundRequested?.Invoke());

        _hub.On<string, string>("EndGameVoteReceived", (playerName, voteType) =>
            EndGameVoteReceived?.Invoke(playerName, voteType));

        _hub.On<string, bool>("PlayerLeft", (name, isHost) => PlayerLeft?.Invoke(name, isHost));
        _hub.On<string, bool>("PlayerReadyUpdated", (name, isReady) => PlayerReadyUpdated?.Invoke(name, isReady));
        _hub.On("ReturnToLobby", () => ReturnedToLobby?.Invoke());

        _hub.Reconnecting += _ => { HubReconnecting?.Invoke(); return Task.CompletedTask; };
        _hub.Reconnected  += _ => { HubReconnected?.Invoke();  return Task.CompletedTask; };
        _hub.Closed       += _ => { HubConnectionClosed?.Invoke(); return Task.CompletedTask; };

        await _hub.StartAsync();
    }

    public async Task SetRoomRulesAsync(string rulesJson)
    {
        if (_hub == null || MyRoomCode == null) return;
        CurrentRulesJson = rulesJson;
        await _hub.InvokeAsync("SetRoomRules", MyRoomCode, rulesJson);
    }

    public async Task KickPlayerAsync(string playerName)
    {
        if (_hub == null || MyRoomCode == null) return;
        await _hub.InvokeAsync("KickPlayer", MyRoomCode, playerName);
    }

    public async Task SendChatMessageAsync(string message)
    {
        if (_hub == null || MyRoomCode == null || MyPlayerName == null) return;
        await _hub.InvokeAsync("SendChatMessage", MyRoomCode, MyPlayerName, message);
    }

    public async Task JoinRoomAsync(string roomCode, string playerName)
    {
        if (_hub == null) return;
        MyRoomCode = roomCode.Trim().ToUpperInvariant();
        MyPlayerName = playerName.Trim();
        await _hub.InvokeAsync("JoinRoom", MyRoomCode, MyPlayerName);
    }

    public async Task SetCpuCountAsync(int cpuCount)
    {
        if (_hub == null || MyRoomCode == null) return;
        await _hub.InvokeAsync("SetCpuCount", MyRoomCode, cpuCount);
    }

    public async Task StartGameAsync(int cpuCount = 0)
    {
        if (_hub == null || MyRoomCode == null) return;
        await _hub.InvokeAsync("StartGame", MyRoomCode, cpuCount);
    }

    public async Task SetPlayerReadyAsync(bool isReady)
    {
        if (_hub == null || MyRoomCode == null) return;
        await _hub.InvokeAsync("SetPlayerReady", MyRoomCode, isReady);
    }

    public async Task SendNextRoundRequestAsync()
    {
        if (_hub == null || MyRoomCode == null) return;
        await _hub.InvokeAsync("RequestNextRound", MyRoomCode);
    }

    public async Task SendEndGameVoteAsync(string voteType)
    {
        if (_hub != null && !string.IsNullOrEmpty(MyRoomCode))
            await _hub.InvokeAsync("SendEndGameVote", MyRoomCode, voteType);
    }

    public async Task SendReturnToLobbyAsync()
    {
        if (_hub != null && !string.IsNullOrEmpty(MyRoomCode))
            await _hub.InvokeAsync("ReturnToLobby", MyRoomCode);
    }

    public async Task SendMoveAsync(MoveDto move)
    {
        if (_hub == null || MyRoomCode == null) return;
        var json = JsonSerializer.Serialize(move);
        await _hub.InvokeAsync("SendMove", MyRoomCode, json);
    }

    public async Task SendStateToPlayerAsync(int playerIndex, string stateJson)
    {
        if (_hub == null || MyRoomCode == null) return;
        await _hub.InvokeAsync("SendStateToPlayer", MyRoomCode, playerIndex, stateJson);
    }

    public void SetPlayerInfo(int myIndex, string[] allPlayerNames, int cpuCount = 0)
    {
        MyPlayerIndex = myIndex;
        AllPlayerNames = allPlayerNames;
        CpuCount = cpuCount;
        IsGameStarted = true;
    }

    public void ResetGame()
    {
        IsGameStarted = false;
        MyPlayerIndex = -1;
        AllPlayerNames = Array.Empty<string>();
        CpuCount = 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub != null)
        {
            await _hub.DisposeAsync();
            _hub = null;
        }
    }
}
