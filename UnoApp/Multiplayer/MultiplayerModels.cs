namespace UnoApp.Multiplayer;

public class MoveDto
{
    public int PlayerIndex { get; set; }
    public string Type { get; set; } = "";  // "play","draw","color","challenge","accept","uno"
    public string? CardId { get; set; }
    public string? Color { get; set; }
    public int? TargetIndex { get; set; }
}

public class CardDto
{
    public string Id { get; set; } = "";
    public string Color { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsPlayable { get; set; }
}

public class GameStateDto
{
    public int CurrentPlayerIndex { get; set; }
    public CardDto? TopCard { get; set; }
    public string? ActiveColor { get; set; }
    public int[] HandCounts { get; set; } = Array.Empty<int>();
    public string[] PlayerNames { get; set; } = Array.Empty<string>();
    public List<CardDto> MyHand { get; set; } = new();
    public bool IsMyTurn { get; set; }
    public string Status { get; set; } = "";
    public int Direction { get; set; } = 1;
    public bool IsGameOver { get; set; }
    public int? WinnerIndex { get; set; }
    public int PendingDraw { get; set; }
    public string NotificationBanner { get; set; } = "";
    public int Wd4ChallengerIndex { get; set; } = -1;
    public string MatchTimestamp { get; set; } = "";
    public int[] TotalScores { get; set; } = Array.Empty<int>();
    public int[] RoundScores { get; set; } = Array.Empty<int>();
    public string[] PlayerStatuses { get; set; } = Array.Empty<string>();
}
