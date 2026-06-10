using Microsoft.AspNetCore.SignalR.Client;
using System.Text.Json;
using UnoApp.Multiplayer;

namespace UnoApp.Services;

public class LobbyService : IAsyncDisposable
{
    private HubConnection? _hub;

    public event Action<string, List<string>>? PlayersUpdated;
    public event Action<string[], int>? GameStarted;
    public event Action? RoomClosed;
    public event Action<string>? StateUpdated;
    public event Action<string>? MoveReceived;

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

        _hub.On<string[], int>("GameStarted", (players, cpuCount) =>
            GameStarted?.Invoke(players, cpuCount));

        _hub.On("RoomClosed", () =>
            RoomClosed?.Invoke());

        _hub.On<string>("StateUpdated", json =>
            StateUpdated?.Invoke(json));

        _hub.On<string>("MoveReceived", json =>
            MoveReceived?.Invoke(json));

        await _hub.StartAsync();
    }

    public async Task JoinRoomAsync(string roomCode, string playerName)
    {
        if (_hub == null) return;
        MyRoomCode = roomCode.Trim().ToUpperInvariant();
        MyPlayerName = playerName.Trim();
        await _hub.InvokeAsync("JoinRoom", MyRoomCode, MyPlayerName);
    }

    public async Task StartGameAsync(int cpuCount = 0)
    {
        if (_hub == null || MyRoomCode == null) return;
        await _hub.InvokeAsync("StartGame", MyRoomCode, cpuCount);
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
