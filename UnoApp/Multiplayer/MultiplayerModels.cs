namespace UnoApp.Multiplayer;

public class MoveDto
{
    public int PlayerIndex { get; set; }
    public string Type { get; set; } = "";  // "play","draw","color","challenge","accept","uno","catch","swap","play-drawn","keep-drawn"
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
    public double RotationAngle { get; set; }
}

public class GameStateDto
{
    public int CurrentPlayerIndex { get; set; }
    public CardDto? TopCard { get; set; }
    public string? ActiveColor { get; set; }
    public int[] HandCounts { get; set; } = Array.Empty<int>();
    public string[] PlayerNames { get; set; } = Array.Empty<string>();
    public string[] PlayerIds { get; set; } = Array.Empty<string>();
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
    
    public Dictionary<string, string> EndGameVotes { get; set; } = new();
    public string? GameOverDeadline { get; set; }
    public bool HostProposedNextRound { get; set; }

    // ── New unified fields ────────────────────────────────────────────────────
    /// <summary>Last 8 cards on the discard pile (oldest first, newest last).</summary>
    public List<CardDto> DiscardHistory { get; set; } = new();

    /// <summary>Last 60 game log entries.</summary>
    public List<string> GameLog { get; set; } = new();

    /// <summary>Animation event string to trigger on the client (MP broadcast).</summary>
    public string AnimationTrigger { get; set; } = "";

    /// <summary>ID of the card the local player just drew that is playable (for PLAY/KEEP flow).</summary>
    public string? DrawnCardId { get; set; }

    /// <summary>Current round number.</summary>
    public int RoundNumber { get; set; } = 1;

    /// <summary>Index of the player who last played a card (for card-play animation origin).</summary>
    public int LastPlayerIndex { get; set; } = -1;

    /// <summary>True when the local player should press UNO (has 1 card and is at risk).</summary>
    public bool CanCallUno { get; set; }

    /// <summary>True when the local player can catch another player for not calling UNO.</summary>
    public bool CanCatchUno { get; set; }

    /// <summary>Jump-in QTE window in seconds (from game settings).</summary>
    public double JumpInTimerSeconds { get; set; } = 2.5;

    /// <summary>Whether Forced Play is active (hides the KEEP button after drawing).</summary>
    public bool ForcedPlay { get; set; }

    /// <summary>Cards revealed during a WD4 bluff challenge.</summary>
    public List<CardDto> Wd4BluffCards { get; set; } = new();

    /// <summary>Index of the player whose cards are being revealed in a WD4 bluff.</summary>
    public int Wd4BluffPlayerIndex { get; set; } = -1;

    /// <summary>Player index that this DTO was built for (0 = human in SP, MyPlayerIndex in MP).</summary>
    public int MyPlayerIndex { get; set; }

    /// <summary>
    /// Index of the player who was just skipped (by a Skip or 2p Reverse) and therefore cannot
    /// jump in on that same card. -1 when no player is currently skipped.
    /// </summary>
    public int SkippedForJumpInIndex { get; set; } = -1;
}
