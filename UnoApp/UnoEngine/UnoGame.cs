using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnoEngine.Models;
using UnoApp.Multiplayer;

namespace UnoEngine
{
    public enum GameStatus
    {
        Playing,
        WaitingForColorSelection,
        WaitingForSwapTarget,
        WaitingForJumpIn,
        WaitingForUnoCall,
        WaitingForWd4Challenge,
        RevealingWd4Bluff,
        GameOver
    }

    public class UnoGame
    {
        // When true the game loop should pause and avoid progressing turns.
        public bool IsPaused { get; set; } = false;
        public event Action? OnStateChanged;
        public Func<string, Task>? OnBoardAnimation;
        public Func<string, Task>? OnSoundEffect;

        private List<UnoCard>? _wd4HandSnapshotMatchingCards = null;
        public List<UnoCard>? Wd4BluffRevealCards => _wd4HandSnapshotMatchingCards;
        private Player? _wd4Player = null;
        public Player? Wd4BluffPlayer => _wd4Player;
        private int _wd4ChallengerIndex = 0;
        public int Wd4ChallengerIndex => _wd4ChallengerIndex;

        public List<Player> Players { get; private set; } = new();
        public Stack<UnoCard> DrawPile { get; private set; } = new();
        public List<UnoCard> DiscardPile { get; private set; } = new();
        public GameSettings Settings { get; private set; }
        public int CurrentPlayerIndex { get; private set; } = 0;
        public int LastPlayerIndex { get; private set; } = -1;
        public int GameDirection { get; private set; } = 1; 
        public int PendingDrawCount { get; private set; } = 0;
        public GameStatus Status { get; private set; } = GameStatus.Playing;
        public UnoCard? TopCard => DiscardPile.LastOrDefault();
        public List<UnoCard> DiscardHistory => DiscardPile.TakeLast(8).ToList();
        public UnoCard? PendingCard { get; private set; }
        public Player? PendingColorSelector { get; private set; }
        public Player? PlayerAtRisk { get; private set; }
        public List<string> GameLog { get; private set; } = new();
        public Player? Winner { get; private set; }
        public int WinnerScore { get; private set; }
        public int RoundNumber { get; set; } = 1;
        public bool IsUnoCalled { get; set; } = false;
        public bool IsClockwise => GameDirection == 1;
        public bool CanChallengeUno => Status == GameStatus.WaitingForUnoCall && PlayerAtRisk != null && PlayerAtRisk != Players[0];
        public bool CanCallUno => Status == GameStatus.WaitingForUnoCall && PlayerAtRisk == Players[0];
        public int LastUnoViolatorIndex { get; set; } = -1;
        public int LastPlayerAtRiskIndex { get; private set; } = -1;
        // Skip/Reverse(2p) used to call MoveToNextTurn() immediately inside HandleSpecialActions,
        // which advanced the turn (and any visual "skip" indicator) BEFORE the just-played card's
        // hand-count-1 check ran, so the UNO button appeared to pop up only after the next player's
        // turn had already started. Defer that extra advance until after the UNO risk check.
        private bool _pendingExtraTurnAdvance = false;
        public string ActiveNotificationBanner { get; set; } = "";
        public int NotificationBannerTargetIndex { get; set; } = -1; // -1 = broadcast to all
        public CardColor LastValidColor { get; set; }

        public List<int> RemotePlayerIndices { get; set; } = new();
        public Func<int, Task<MoveDto?>>? GetRemoteHumanMove;
        public DateTime? MatchTimestamp { get; private set; }

        private Random _random = new();
        private System.Threading.SemaphoreSlim _actionLock = new(1, 1);
        private readonly Dictionary<int, string> _remoteDrawnCardIds = new();
        public IReadOnlyDictionary<int, string> RemoteDrawnCardIds => _remoteDrawnCardIds;

        public void ConvertPlayerToAi(int playerIndex)
        {
            if (playerIndex < 0 || playerIndex >= Players.Count) return;
            Player player = Players[playerIndex];
            if (!player.IsHuman) return;
            
            player.IsHuman = false;
            player.Name = $"🤖 {player.Name}";
            LogAction($"📶 {player.Name} (now AI) took over!");
            
            // If the player was selecting a wild color, handle that
            if (PendingColorSelector == player && Status == GameStatus.WaitingForColorSelection)
            {
                SafeFireAndForget(() => HandleCpuColorSelectionAsync(player));
            }
            // If the player had a pending swap target, handle that
            else if (PendingCard != null && Status == GameStatus.WaitingForSwapTarget && CurrentPlayerIndex == playerIndex)
            {
                // CPU auto-picks target
                var target = Players.Where(p => p != player).OrderBy(p => p.Hand.Count).First();
                SafeFireAndForget(async () =>
                {
                    await _actionLock.WaitAsync();
                    try
                    {
                        await InternalPlayCard(player, PendingCard, null, target);
                    }
                    finally
                    {
                        _actionLock.Release();
                        OnStateChanged?.Invoke();
                    }
                });
            }
            // If it's currently this player's turn, trigger the AI turn
            else if (CurrentPlayerIndex == playerIndex && Status == GameStatus.Playing)
            {
                SafeFireAndForget(async () => await ExecuteCpuTurn(player));
            }
        }

        private void SafeFireAndForget(Func<Task> taskFactory)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await taskFactory();
                }
                catch (Exception ex)
                {
                    LogAction($"System Error: {ex.Message}");
                }
            });
        }

        public UnoGame(List<Player> players, GameSettings settings)
        {
            Players = players;
            Settings = settings;
            InitializeGame();
        }

        private void InitializeGame()
        {
            List<UnoCard> deck = CreateDeck();
            Shuffle(deck);

            foreach (var card in deck)
            {
                DrawPile.Push(card);
            }

            // Clear hands for animated dealing later
            foreach (var player in Players)
            {
                player.Hand.Clear();
            }

            // Initial discard
            UnoCard firstCard = DrawPile.Pop();
            while (firstCard.Color == CardColor.Wild) // Standard Uno: First card cannot be Wild
            {
                DrawPile.Push(firstCard);
                ShuffleDeck(); // Re-shuffle or just insert at bottom? Let's just re-shuffle for simplicity.
                firstCard = DrawPile.Pop();
            }
            firstCard.RotationAngle = (float)(_random.NextDouble() * 30.0 - 15.0);
            DiscardPile.Add(firstCard);
            LastValidColor = firstCard.Color;

            // Randomly pick the starting player
            CurrentPlayerIndex = _random.Next(0, Players.Count);
            LogAction($"{Players[CurrentPlayerIndex].Name} goes first!");
        }

        public async Task DealStartingCardsAsync()
        {
            int handSize = Settings.StartingHandSize > 0 ? Settings.StartingHandSize : 7;
            for (int i = 0; i < handSize; i++)
            {
                foreach (var player in Players)
                {
                    player.Hand.Add(DrawOne());
                    if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                    OnStateChanged?.Invoke();
                    await Task.Delay(100); // 100ms per card deal speed
                }
            }
            await Task.Delay(500); // pause before starting the turn
        }

        private List<UnoCard> CreateDeck()
        {
            List<UnoCard> deck = new();
            CardColor[] colors = { CardColor.Red, CardColor.Blue, CardColor.Green, CardColor.Yellow };

            foreach (var color in colors)
            {
                // One 0 per color
                deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Zero));

                // Two of 1-9, Skip, Reverse, Draw2 per color
                for (int i = 0; i < 2; i++)
                {
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.One));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Two));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Three));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Four));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Five));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Six));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Seven));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Eight));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Nine));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Skip));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Reverse));
                    deck.Add(new UnoCard(Guid.NewGuid(), color, CardValue.Draw2));
                }
            }

            // Four Wilds and WildDraw4s
            for (int i = 0; i < 4; i++)
            {
                deck.Add(new UnoCard(Guid.NewGuid(), CardColor.Wild, CardValue.Wild));
                deck.Add(new UnoCard(Guid.NewGuid(), CardColor.Wild, CardValue.WildDraw4));
                
                if (Settings.EnableVortex)
                {
                    deck.Add(new UnoCard(Guid.NewGuid(), CardColor.Wild, CardValue.Vortex));
                }
            }

            return deck;
        }

        private void Shuffle(List<UnoCard> cards)
        {
            int n = cards.Count;
            while (n > 1)
            {
                n--;
                int k = _random.Next(n + 1);
                UnoCard value = cards[k];
                cards[k] = cards[n];
                cards[n] = value;
            }
        }

        private void ShuffleDeck()
        {
            var deckList = DrawPile.ToList();
            Shuffle(deckList);
            DrawPile = new Stack<UnoCard>(deckList);
        }

        public bool CanPlayCard(UnoCard? card)
        {
            if (card == null) return false;

            if (Settings.Stacking && PendingDrawCount > 0)
            {
                if (TopCard?.Value == CardValue.Draw2)
                    return card.Value == CardValue.Draw2;
                if (TopCard?.Value == CardValue.WildDraw4)
                    return card.Value == CardValue.WildDraw4;
            }

            if (TopCard == null) return true;
            if (card.Color == CardColor.Wild) return true;
            if (card.Color == TopCard.Color) return true;
            if (card.Value == TopCard.Value) return true;

            return false;
        }

        public void LogAction(string message)
        {
            GameLog.Add(message);
            if (GameLog.Count > 50) GameLog.RemoveAt(0); // Keep last 50
            OnStateChanged?.Invoke();
        }

        public async Task StartTurnAsync()
        {
            if (Status == GameStatus.GameOver) return;
            if (IsPaused) return;

            var currentPlayer = GetCurrentPlayer();
            
            // Auto-process penalty if Stacking is disabled or player has no stackable card
            if (PendingDrawCount > 0)
            {
                var stackCard = currentPlayer.Hand.FirstOrDefault(c => c.Value == TopCard?.Value);
                if (!Settings.Stacking || stackCard == null)
                {
                    currentPlayer.CurrentStatus = $"Taking {PendingDrawCount} Penalty...";
                    OnStateChanged?.Invoke();
                    await Task.Delay(1000);
                    await HandlePendingDrawAsync();
                    return;
                }
            }

            // Safety: if status got stuck in WaitingForSwapTarget on a CPU turn, reset it
            if (Status == GameStatus.WaitingForSwapTarget && !Players[CurrentPlayerIndex].IsHuman)
            {
                Status = GameStatus.Playing;
                PendingCard = null;
            }

            foreach (var p in Players) p.CurrentStatus = p == currentPlayer ? "Thinking..." : "";
            OnStateChanged?.Invoke();

            int capturedIdx = CurrentPlayerIndex;

            // Remote human player (multiplayer guest) — wait for SignalR move
            if (currentPlayer.IsHuman && RemotePlayerIndices.Contains(capturedIdx) && GetRemoteHumanMove != null)
            {
                var moveGetter = GetRemoteHumanMove;
                SafeFireAndForget(async () =>
                {
                    var move = await moveGetter(capturedIdx);
                    if (move != null && CurrentPlayerIndex == capturedIdx && Status != GameStatus.GameOver)
                        await ApplyRemoteMoveAsync(move);
                });
                return;
            }

            if (!currentPlayer.IsHuman)
            {
                int cpuThinkDelay = Settings.CpuDifficulty switch
                {
                    AiDifficulty.Easy => 2200,
                    AiDifficulty.Hard => 900,
                    _ => 1500
                };
                await Task.Delay(cpuThinkDelay);
                if (IsPaused) return;
                // Check if it's still this CPU's turn after the delay (in case of human jump-in)
                if (Status != GameStatus.GameOver && CurrentPlayerIndex == Players.IndexOf(currentPlayer))
                {
                    await ExecuteCpuTurn(currentPlayer);
                }
            }
        }

        public async Task ApplyRemoteMoveAsync(MoveDto move)
        {
            if (move.PlayerIndex < 0 || move.PlayerIndex >= Players.Count) return;
            var player = Players[move.PlayerIndex];

            switch (move.Type)
            {
                case "play":
                    if (Guid.TryParse(move.CardId, out var cardId))
                    {
                        var card = player.Hand.FirstOrDefault(c => c.Id == cardId);
                        if (card != null)
                        {
                            CardColor? color = null;
                            if (move.Color != null && Enum.TryParse<CardColor>(move.Color, out var pc)) color = pc;
                            Player? target = move.TargetIndex.HasValue && move.TargetIndex.Value >= 0 && move.TargetIndex.Value < Players.Count
                                ? Players[move.TargetIndex.Value] : null;
                            await PlayCardAsync(player, card, color, target);
                        }
                    }
                    break;
                case "play-drawn":
                    _remoteDrawnCardIds.Remove(move.PlayerIndex);
                    if (Guid.TryParse(move.CardId, out var drawnCardId))
                    {
                        var drawnCard = player.Hand.FirstOrDefault(c => c.Id == drawnCardId);
                        if (drawnCard != null) await PlayCardAsync(player, drawnCard);
                    }
                    break;
                case "keep-drawn":
                    _remoteDrawnCardIds.Remove(move.PlayerIndex);
                    LogAction($"{player.Name} drew and kept the card.");
                    await PassTurnAfterDraw();
                    break;
                case "draw":
                {
                    // Flags set inside the lock, actions executed outside to avoid re-entrant deadlock.
                    bool drawHadPending = false;
                    bool drawShouldPass = false;
                    await _actionLock.WaitAsync();
                    try
                    {
                        if (PendingDrawCount > 0)
                        {
                            drawHadPending = true;
                        }
                        else
                        {
                            var drawn = await DrawCardAsync(player);
                            if (drawn == null)
                            {
                                // ForcedPlay blocked the draw — nothing to do
                            }
                            else if (!CanPlayCard(drawn))
                            {
                                drawShouldPass = true;
                            }
                            else
                            {
                                // Drawn card is playable — track it so the guest sees PLAY/KEEP choice
                                _remoteDrawnCardIds[move.PlayerIndex] = drawn.Id.ToString();
                                OnStateChanged?.Invoke();
                            }
                        }
                    }
                    finally
                    {
                        _actionLock.Release();
                    }
                    // Execute post-draw turn transitions OUTSIDE the lock so the next turn's
                    // PlayCardAsync / HandleCpuColorSelectionAsync can acquire it without deadlocking.
                    if (drawHadPending) await HandlePendingDrawAsync();
                    else if (drawShouldPass) await PassTurnAfterDraw();
                    break;
                }
                case "afk":
                {
                    // Flags / captured values set inside lock; actions that need the lock run outside.
                    bool afkShouldStartTurn = false;
                    bool afkNeedsChallenge = false;
                    CardColor afkChosenColor = CardColor.Red;
                    bool afkNeedsColor = false;
                    await _actionLock.WaitAsync();
                    try
                    {
                        if (Status == GameStatus.GameOver) return;

                        // The AFK timeout is detected OUTSIDE this lock, so the player may have
                        // already acted in the window between that check and this task acquiring
                        // the lock. Re-validate atomically here, BEFORE any notification/animation,
                        // so a just-in-time move never causes a visible-but-reverted "AFK —
                        // drawing" flash or penalizes the wrong (now-current) player.
                        if (Status == GameStatus.Playing && CurrentPlayerIndex != move.PlayerIndex) return;

                        if (Status == GameStatus.WaitingForColorSelection && PendingColorSelector == player)
                        {
                            // Pick a random color — finalize directly to avoid re-entrant lock
                            // (HandleCpuColorSelectionAsync also acquires _actionLock)
                            var counts = player.Hand.Where(c => c.Color != CardColor.Wild)
                                                    .GroupBy(c => c.Color)
                                                    .OrderByDescending(g => g.Count());
                            afkChosenColor = counts.Any() ? counts.First().Key : (CardColor)_random.Next(0, 4);
                            afkNeedsColor = true;
                        }
                        else if (Status == GameStatus.WaitingForSwapTarget && CurrentPlayerIndex == move.PlayerIndex)
                        {
                            // AFK while choosing swap target: pick player with fewest cards
                            var target = Players.Where(p => p != player).OrderBy(p => p.Hand.Count).First();
                            await InternalPlayCard(player, PendingCard!, null, target);
                        }
                        else if (Status == GameStatus.WaitingForWd4Challenge && Wd4ChallengerIndex == move.PlayerIndex)
                        {
                            // ResolveChallengeAsync also acquires _actionLock — must run outside
                            afkNeedsChallenge = true;
                        }
                        else if (Status == GameStatus.Playing)
                        {
                            // Normal AFK penalty
                            int penaltyCount = Settings.AfkPenaltyCards;
                            int afkIdx = move.PlayerIndex;
                            player.CurrentStatus = $"⏰ AFK — drawing {penaltyCount} card{(penaltyCount != 1 ? "s" : "")}…";
                            OnStateChanged?.Invoke();
                            await Task.Delay(800);
                            if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{afkIdx}");
                            for (int i = 0; i < penaltyCount; i++)
                                await DrawCardAsync(player, isPenaltyDraw: true);
                            player.CurrentStatus = "";
                            LogAction($"⏰ {player.Name} was AFK and drew {penaltyCount} card{(penaltyCount != 1 ? "s" : "")}.");
                            if (Status != GameStatus.GameOver)
                            {
                                MoveToNextTurn();
                                // StartTurnAsync must run OUTSIDE the lock (CPU turn → PlayCardAsync re-acquires it)
                                afkShouldStartTurn = true;
                            }
                        }
                    }
                    finally
                    {
                        _actionLock.Release();
                    }
                    // Post-lock actions
                    if (afkNeedsColor) await InternalFinalizeWildColor(afkChosenColor, player);
                    else if (afkNeedsChallenge) await ResolveChallengeAsync(false);
                    else if (afkShouldStartTurn) await StartTurnAsync();
                    break;
                }
                case "color":
                    if (move.Color != null && Enum.TryParse<CardColor>(move.Color, out var wildColor))
                        await SetWildColorForRemoteAsync(wildColor, player);
                    break;
                case "challenge":
                    await ResolveChallengeAsync(true);
                    break;
                case "accept":
                    await ResolveChallengeAsync(false);
                    break;
                case "uno":
                    await AttemptUnoCallAsync(player);
                    break;
                case "catch":
                    await AttemptUnoCallAsync(player);
                    break;
                case "jump-in":
                    if (Guid.TryParse(move.CardId, out var jumpCardId))
                    {
                        var jumpCard = player.Hand.FirstOrDefault(c => c.Id == jumpCardId);
                        if (jumpCard != null)
                            await JumpInAsync(player, jumpCard);
                    }
                    break;
                case "swap":
                    if (move.TargetIndex.HasValue && PendingCard != null)
                    {
                        var swapTarget = Players[move.TargetIndex.Value];
                        await PlayCardAsync(player, PendingCard, null, swapTarget);
                    }
                    break;
            }
        }

        public async Task SetWildColorForRemoteAsync(CardColor color, Player player)
        {
            await _actionLock.WaitAsync();
            try
            {
                if (Status != GameStatus.WaitingForColorSelection || PendingColorSelector != player) return;
                await InternalFinalizeWildColor(color, player);
            }
            finally
            {
                _actionLock.Release();
            }
            OnStateChanged?.Invoke();
        }

        private async Task ExecuteCpuTurn(Player cpu)
        {
            if (Settings.CpuDifficulty == AiDifficulty.Easy)
            {
                await ExecuteEasyCpuTurnAsync(cpu);
                return;
            }

            // 1. Check for Pending Draw (Stacking)
            if (PendingDrawCount > 0)
            {
                // We only reach here if Stacking is ON and CPU has a stackable card (guaranteed by StartTurnAsync)
                var stackable = cpu.Hand.First(c => c.Value == TopCard?.Value);
                LogAction($"{cpu.Name} STACKED a {stackable}!");
                // Always pass null for declared color so the new Wild logic kicks in
                await PlayCardAsync(cpu, stackable, null);
                return;
            }

            // 2. Strategy: Rule 7 (Swap) - only if target has FEWER cards than us
            var card7 = cpu.Hand.FirstOrDefault(c => c.Value == CardValue.Seven && CanPlayCard(c));
            if (card7 != null && Settings.SevenSwap)
            {
                // Only swap if it clearly benefits the CPU (target has at least 2 fewer cards)
                var target = Players.Where(p => p != cpu && p.Hand.Count < cpu.Hand.Count - 1)
                                    .OrderBy(p => p.Hand.Count).FirstOrDefault();
                if (target != null)
                {
                    LogAction($"{cpu.Name} played a 7 and swapped with {target.Name}!");
                    await PlayCardAsync(cpu, card7, null, target);
                    return;
                }
            }

            // 3. Strategy: Rule 0 (Rotate)
            var card0 = cpu.Hand.FirstOrDefault(c => c.Value == CardValue.Zero && CanPlayCard(c));
            if (card0 != null && Settings.ZeroRotate)
            {
                var nextPlayer = Players[GetNextPlayerIndex()];
                if (nextPlayer.Hand.Count < cpu.Hand.Count)
                {
                    LogAction($"{cpu.Name} played a 0 to rotate hands!");
                    await PlayCardAsync(cpu, card0);
                    return;
                }
            }

            // 4. Strategy: Power Plays (Draw 2/4)
            var powerCard = cpu.Hand.FirstOrDefault(c => 
                (c.Value == CardValue.Draw2 || c.Value == CardValue.WildDraw4) && CanPlayCard(c));
            
            if (powerCard != null)
            {
                var nextPlayer = Players[GetNextPlayerIndex()];
                if (nextPlayer.Hand.Count <= 3) // Threat detected
                {
                    LogAction($"{cpu.Name} played a {powerCard.Value} against {nextPlayer.Name}!");
                    await PlayCardAsync(cpu, powerCard, null); 
                    return;
                }
            }

            // 5. Standard Play — exclude 7 when SevenSwap is on (only played via strategy with explicit target)
            var match = cpu.Hand.FirstOrDefault(c => CanPlayCard(c) && !(c.Value == CardValue.Seven && Settings.SevenSwap));
            if (match != null)
            {
                LogAction($"{cpu.Name} played {match}.");
                await PlayCardAsync(cpu, match, null);
                return;
            }

            // 6. Draw Fallback
            cpu.CurrentStatus = "Drawing...";
            LogAction($"{cpu.Name} had no moves and is drawing...");
            var drawn = await DrawCardAsync(cpu);

            // Guard: if status changed during the draw (e.g. game ended, or a Jump-In fired),
            // or it is no longer this CPU's turn, abandon the continuation silently.
            if (Status != GameStatus.Playing || CurrentPlayerIndex != Players.IndexOf(cpu))
                return;

            if (drawn == null)
            {
                MoveToNextTurn();
                await StartTurnAsync();
                return;
            }
            LogAction($"{cpu.Name} drew {drawn}.");
            
            if (CanPlayCard(drawn) && !(drawn.Value == CardValue.Seven && Settings.SevenSwap))
            {
                LogAction($"{cpu.Name} played the drawn {drawn}.");
                await PlayCardAsync(cpu, drawn, null);
            }
            else
            {
                MoveToNextTurn();
                await StartTurnAsync();
            }
        }

        public async Task PlayCardAsync(Player player, UnoCard card, CardColor? declaredColor = null, Player? targetPlayer = null)
        {
            if (Status == GameStatus.WaitingForJumpIn) return;
            if ((Status == GameStatus.Playing || Status == GameStatus.WaitingForJumpIn) && !CanPlayCard(card)) return;

            await _actionLock.WaitAsync();
            try
            {
                await InternalPlayCard(player, card, declaredColor, targetPlayer);
            }
            finally
            {
                _actionLock.Release();
            }
            
            OnStateChanged?.Invoke();
        }

        public bool IsEligibleForJumpIn(UnoCard card)
        {
            if (!Settings.JumpInRule || TopCard == null) return false;
            if (Status != GameStatus.Playing && Status != GameStatus.WaitingForJumpIn) return false;

            // Block special cards that require UI interaction or complex board sweeps
            if (card.Color == CardColor.Wild) return false;
            if (card.Value == CardValue.Vortex) return false;
            if (card.Value == CardValue.Seven && Settings.SevenSwap) return false;
            if (card.Value == CardValue.Zero && Settings.ZeroRotate) return false;

            return card.Color == TopCard.Color && card.Value == TopCard.Value;
        }

        public async Task<bool> JumpInAsync(Player player, UnoCard card)
        {
            if (!IsEligibleForJumpIn(card)) return false;

            await _actionLock.WaitAsync();
            try
            {
                // Re-validate inside lock in case someone else jumped in
                if (TopCard == null || card.Color != TopCard.Color || card.Value != TopCard.Value) return false;

                LogAction($"{player.Name} JUMPED IN with {card}!");
                if (OnSoundEffect != null) await OnSoundEffect("jumpIn");
                
                // Update turn to this player
                CurrentPlayerIndex = Players.IndexOf(player);
                
                await InternalPlayCard(player, card);
            }
            finally
            {
                _actionLock.Release();
            }

            // Cancel any pending jump-in timer
            _qteCts?.Cancel();
            
            OnStateChanged?.Invoke();
            return true;
        }

        private CancellationTokenSource? _qteCts;

        private async Task InternalPlayCard(Player player, UnoCard card, CardColor? declaredColor = null, Player? targetPlayer = null)
        {
            _qteCts?.Cancel(); // Stop any running QTE immediately

            if (Status != GameStatus.Playing && Status != GameStatus.WaitingForColorSelection && Status != GameStatus.WaitingForSwapTarget && Status != GameStatus.WaitingForJumpIn) 
                return;
            
            if (!player.Hand.Contains(card) && card != PendingCard) throw new Exception("Player doesn't have this card.");
            
            // Only validate match if it's NOT a jump-in (JumpIn validation happens before calling this)
            // But for simplicity, we assume InternalPlayCard is always called with valid intent.

            // Snapshot WD4 player's hand (minus this card) BEFORE it is removed — for challenge adjudication
            if (card.Value == CardValue.WildDraw4 && Settings.EnableWildDraw4Challenge && !Settings.Stacking && declaredColor == null)
            {
                _wd4HandSnapshotMatchingCards = player.Hand
                    .Where(c => !ReferenceEquals(c, card))
                    .Where(c => c.Color == LastValidColor && c.Color != CardColor.Wild)
                    .ToList();
                _wd4Player = player;
            }
            
            // Handle Wild requiring color selection (Vortex is Color Nullified and skips selection)
            if (card.Color == CardColor.Wild && card.Value != CardValue.Vortex)
            {
                if (player.Hand.Contains(card)) player.Hand.Remove(card);
                else if (card == PendingCard) PendingCard = null;

                card.RotationAngle = (float)(_random.NextDouble() * 30.0 - 15.0);
                DiscardPile.Add(card);

                PendingColorSelector = player;
                LastPlayerIndex = Players.IndexOf(player);
                Status = GameStatus.WaitingForColorSelection;
                // Always broadcast WaitingForColorSelection so all players (guests included)
                // see the color-picker overlay before the color is finalized.
                OnStateChanged?.Invoke();

                if (declaredColor != null)
                {
                    // Human already chose a colour (local host or guest colour was bundled with the move).
                    // InternalFinalizeWildColor is called directly here — we're already inside
                    // _actionLock, so we call the internal method rather than the public wrappers.
                    //
                    // In multiplayer, flash the chosen colour slice so other players can see it,
                    // mirroring the CPU highlight animation. Skip in single-player (no remote players)
                    // to avoid a redundant re-flash for the local human who just clicked the button.
                    if (OnBoardAnimation != null && RemotePlayerIndices.Count > 0)
                        await OnBoardAnimation.Invoke($"cpu-color-pick-{declaredColor}");
                    await InternalFinalizeWildColor(declaredColor.Value, player);
                }
                else if (!player.IsHuman)
                {
                    SafeFireAndForget(() => HandleCpuColorSelectionAsync(player));
                }
                else if (RemotePlayerIndices.Contains(Players.IndexOf(player)) && GetRemoteHumanMove != null)
                {
                    // Remote human player, wait for move!
                    int capturedIdx = Players.IndexOf(player);
                    var moveGetter = GetRemoteHumanMove;
                    SafeFireAndForget(async () =>
                    {
                        var move = await moveGetter(capturedIdx);
                        if (move != null && Status == GameStatus.WaitingForColorSelection && PendingColorSelector == player)
                            await ApplyRemoteMoveAsync(move);
                    });
                }
                return;
            }

            // Handle Seven requiring target selection
            if (card.Value == CardValue.Seven && Settings.SevenSwap && targetPlayer == null)
            {
                if (player.IsHuman)
                {
                    // Human must pick: show the UI
                    PendingCard = card;
                    Status = GameStatus.WaitingForSwapTarget;
                    OnStateChanged?.Invoke();
                    // Check if it's a remote human
                    if (RemotePlayerIndices.Contains(Players.IndexOf(player)) && GetRemoteHumanMove != null)
                    {
                        int capturedIdx = Players.IndexOf(player);
                        var moveGetter = GetRemoteHumanMove;
                        SafeFireAndForget(async () =>
                        {
                            var move = await moveGetter(capturedIdx);
                            if (move != null && Status == GameStatus.WaitingForSwapTarget && CurrentPlayerIndex == capturedIdx)
                                await ApplyRemoteMoveAsync(move);
                        });
                    }
                    return;
                }
                else
                {
                    // CPU auto-picks: swap with the player who has the fewest cards
                    targetPlayer = Players.Where(p => p != player).OrderBy(p => p.Hand.Count).First();
                }
            }

            // Proceed with the play
            Status = GameStatus.Playing;
            PendingCard = null;
            LastPlayerIndex = Players.IndexOf(player);
            if (TopCard != null)
            {
                LastValidColor = TopCard.Color;
            }

            if (player.Hand.Contains(card))
            {
                player.Hand.Remove(card);
            }
            else if (card == PendingCard)
            {
                PendingCard = null;
            }
            
            UnoCard playedCard = card;
            if (card.Value == CardValue.Vortex)
            {
                playedCard = card with { Color = LastValidColor };
            }
            else if (card.Color == CardColor.Wild && declaredColor.HasValue)
            {
                playedCard = card with { Color = declaredColor.Value };
            }
            else if (card.Color == CardColor.Wild)
            {
                playedCard = card with { Color = CardColor.Red }; // Fallback
            }
            
            // Assign persistent tilt immediately when played
            playedCard.RotationAngle = (float)(_random.NextDouble() * 30.0 - 15.0); // -15 to 15
            
            DiscardPile.Add(playedCard);
            LastValidColor = playedCard.Color; // Update to the actual played card color before broadcasting
            Status = GameStatus.Playing;
            OnStateChanged?.Invoke(); // Render the card on the discard pile before any overlay fires

            // ── In-game action bonus ───────────────────────────────────────────
            int actionBonus = playedCard.Value switch
            {
                CardValue.Skip      => 5,
                CardValue.Reverse   => 5,
                CardValue.Draw2     => 10,
                CardValue.Wild      => 15,
                CardValue.WildDraw4 => 20,
                CardValue.Vortex    => 20,
                _                   => 0
            };
            if (actionBonus > 0)
            {
                player.Score      += actionBonus;
                player.TotalScore += actionBonus;
            }

            if (OnSoundEffect != null)
            {
                string snd = playedCard.Value switch
                {
                    CardValue.Skip => "skip",
                    CardValue.Reverse => "reverse",
                    CardValue.Draw2 => "draw2",
                    CardValue.WildDraw4 => "draw4",
                    CardValue.Wild => "wild",
                    CardValue.Vortex => "vortex",
                    CardValue.Seven when Settings.SevenSwap => "sevenSwap",
                    CardValue.Zero when Settings.ZeroRotate => "zeroRotate",
                    _ => "cardPlay"
                };
                await OnSoundEffect(snd);
            }

            await HandleSpecialActions(player, playedCard, targetPlayer);

            await EndPlayCardSequence(player);
        }

        private async Task EndPlayCardSequence(Player player)
        {
            // Check all players for empty hand (in case of ZeroRotate swapping hands)
            Player? winner = Players.FirstOrDefault(p => p.Hand.Count == 0);
            if (winner != null)
            {
                Status = GameStatus.GameOver;
                Winner = winner;
                MatchTimestamp = DateTime.UtcNow;
                winner.CurrentStatus = "WINNER!";
                ApplyRoundEndScores();
                WinnerScore = winner.RoundScore;
                LogAction($"{winner.Name} WINS! Round scores applied.");
                if (OnSoundEffect != null) await OnSoundEffect("win");
                return;
            }

            // Only trigger UNO catch if the player who just played now has 1 card
            if (player.Hand.Count == 1)
            {
                if (IsUnoCalled)
                {
                    IsUnoCalled = false; // Player pre-declared UNO, safe
                    PlayerAtRisk = null;
                    LastPlayerAtRiskIndex = -1;
                }
                else
                {
                    // Prevent the same player from being set at risk twice consecutively
                    int playerIndex = Players.IndexOf(player);
                    if (playerIndex != LastPlayerAtRiskIndex)
                    {
                        PlayerAtRisk = player;
                        LastPlayerAtRiskIndex = playerIndex;
                        SafeFireAndForget(() => RunUnoTimerAsync(player));
                        return; // Turn sequence will resume after UNO QTE
                    }
                }
            }

            if (Status == GameStatus.WaitingForWd4Challenge) return;

            AdvanceTurnAfterUnoCheck();
            
            if (Settings.JumpInRule && IsJumpInPossible())
            {
                SafeFireAndForget(() => RunJumpInTimerAsync());
            }
            else
            {
                SafeFireAndForget(async () => {
                    await Task.Delay(1500); // Slower visual delay before the next turn starts
                    await StartTurnAsync();
                });
            }
        }

        public async Task SetWildColorAsync(CardColor color)
        {
            await _actionLock.WaitAsync();
            try
            {
                if (Status != GameStatus.WaitingForColorSelection || PendingColorSelector != Players[0]) return;
                await InternalFinalizeWildColor(color, Players[0]);
            }
            finally
            {
                _actionLock.Release();
            }
            OnStateChanged?.Invoke();
        }

        private async Task HandleCpuColorSelectionAsync(Player cpu)
        {
            await Task.Delay(1500); // UI delay for pizza interaction visual
            await _actionLock.WaitAsync();
            try
            {
                if (Status == GameStatus.WaitingForColorSelection && PendingColorSelector == cpu)
                {
                    var bestColor = CardColor.Red;
                    var colorCounts = cpu.Hand.Where(c => c.Color != CardColor.Wild)
                                              .GroupBy(c => c.Color)
                                              .OrderByDescending(g => g.Count());
                    if (colorCounts.Any()) bestColor = colorCounts.First().Key;
                    else bestColor = (CardColor)_random.Next(0, 4);

                    if (OnBoardAnimation != null)
                    {
                        await OnBoardAnimation.Invoke($"cpu-color-pick-{bestColor}");
                    }
                    await Task.Delay(800); // Brief pause so human sees the chosen slice flash
                    await InternalFinalizeWildColor(bestColor, cpu);
                }
            }
            finally
            {
                _actionLock.Release();
            }
            OnStateChanged?.Invoke();
        }

        private async Task InternalFinalizeWildColor(CardColor color, Player player)
        {
            if (TopCard != null)
            {
                var newCard = TopCard with { Color = color };
                DiscardPile[^1] = newCard;
                LastValidColor = color;
            }
            
            PendingColorSelector = null;
            Status = GameStatus.Playing;
            LogAction($"{player.Name} chose {color}!");
            
            await HandleSpecialActions(player, TopCard!, null);
            if (Status == GameStatus.WaitingForWd4Challenge) return;
            await EndPlayCardSequence(player);
        }

        private bool IsJumpInPossible()
        {
            if (TopCard == null) return false;
            foreach (var p in Players)
            {
                if (p.Hand.Any(c => c.Color == TopCard.Color && c.Value == TopCard.Value))
                    return true;
            }
            return false;
        }

        private async Task RunJumpInTimerAsync()
        {
            _qteCts?.Cancel();
            _qteCts = new CancellationTokenSource();
            var token = _qteCts.Token;

            Status = GameStatus.WaitingForJumpIn;
            OnStateChanged?.Invoke();

            // Notify CPU to evaluate jump in NOW
            SafeFireAndForget(() => TriggerCpuJumpInCheck(TopCard));

            try
            {
                await Task.Delay((int)(Settings.JumpInTimerSeconds * 1000), token);
            }
            catch (TaskCanceledException)
            {
                // Timer was cancelled because someone jumped in! We do not pass the turn.
                return;
            }

            // If timer expired normally, check if we are still waiting
            if (Status == GameStatus.WaitingForJumpIn)
            {
                Status = GameStatus.Playing;
                OnStateChanged?.Invoke();
                await StartTurnAsync();
            }
        }

        private async Task RunUnoTimerAsync(Player playerAtRisk)
        {
            _qteCts?.Cancel();
            _qteCts = new CancellationTokenSource();
            var token = _qteCts.Token;

            Status = GameStatus.WaitingForUnoCall;
            OnStateChanged?.Invoke();

            // Notify CPUs to attempt UNO call or catch
            SafeFireAndForget(() => TriggerCpuUnoCheck());

            try
            {
                // 2.5 seconds to call UNO or get caught
                await Task.Delay(2500, token);
            }
            catch (TaskCanceledException)
            {
                // Timer cancelled because someone clicked the button!
                return;
            }

            // Timer expired and NOBODY clicked it!
            await _actionLock.WaitAsync();
            try
            {
                if (Status == GameStatus.WaitingForUnoCall && PlayerAtRisk == playerAtRisk)
                {
                    LogAction($"Nobody called UNO! {playerAtRisk.Name} drew {Settings.UnoFailurePenaltyCards} penalty cards.");
                    int riskIdx = Players.IndexOf(playerAtRisk);
                    if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"penalty-{riskIdx}-{Settings.UnoFailurePenaltyCards}");
                    for (int i = 0; i < Settings.UnoFailurePenaltyCards; i++)
                    {
                        if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{riskIdx}");
                        playerAtRisk.Hand.Add(DrawOne());
                        if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                        OnStateChanged?.Invoke();
                        await Task.Delay(80);
                    }
                    PlayerAtRisk = null;
                    LastPlayerAtRiskIndex = -1;
                    Status = GameStatus.Playing;
                    
                    AdvanceTurnAfterUnoCheck();
                    
                    if (Settings.JumpInRule && IsJumpInPossible())
                    {
                        SafeFireAndForget(() => RunJumpInTimerAsync());
                    }
                    else
                    {
                        SafeFireAndForget(() => StartTurnAsync());
                    }
                }
            }
            finally
            {
                _actionLock.Release();
            }
            OnStateChanged?.Invoke();
        }

        public async Task AttemptUnoCallAsync(Player caller)
        {
            if (Status != GameStatus.WaitingForUnoCall || PlayerAtRisk == null) return;

            await _actionLock.WaitAsync();
            try
            {
                if (Status != GameStatus.WaitingForUnoCall || PlayerAtRisk == null) return; // double check

                _qteCts?.Cancel(); // Stop the timer

                if (caller == PlayerAtRisk)
                {
                    LogAction($"{caller.Name} successfully called UNO!");
                    ActiveNotificationBanner = $"{caller.Name.ToUpper()} CALLED UNO!";
                    NotificationBannerTargetIndex = -1;
                    if (OnSoundEffect != null) await OnSoundEffect("unoCall");
                }
                else
                {
                    LogAction($"{caller.Name} CAUGHT {PlayerAtRisk.Name}! {PlayerAtRisk.Name} draws 2.");
                    LastUnoViolatorIndex = Players.IndexOf(PlayerAtRisk);
                    ActiveNotificationBanner = $"{caller.Name.ToUpper()} CAUGHT {PlayerAtRisk.Name.ToUpper()}!";
                    NotificationBannerTargetIndex = -1;
                    if (OnSoundEffect != null) await OnSoundEffect("caught");
                    int caughtIdx = Players.IndexOf(PlayerAtRisk);
                    if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"penalty-{caughtIdx}-{Settings.UnoFailurePenaltyCards}");
                    for (int i = 0; i < Settings.UnoFailurePenaltyCards; i++)
                    {
                        if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{caughtIdx}");
                        PlayerAtRisk.Hand.Add(DrawOne());
                        if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                        OnStateChanged?.Invoke();
                        await Task.Delay(80);
                    }
                }

                PlayerAtRisk = null;
                LastPlayerAtRiskIndex = -1;
                Status = GameStatus.Playing;

                AdvanceTurnAfterUnoCheck();

                if (Settings.JumpInRule && IsJumpInPossible())
                {
                    SafeFireAndForget(() => RunJumpInTimerAsync());
                }
                else
                {
                    SafeFireAndForget(() => StartTurnAsync());
                }
            }
            finally
            {
                _actionLock.Release();
            }
            OnStateChanged?.Invoke();
        }

        private Task TriggerCpuUnoCheck()
        {
            if (Status != GameStatus.WaitingForUnoCall || PlayerAtRisk == null) return Task.CompletedTask;

            // Each CPU starts its own independent reaction timer
            foreach (var cpu in Players.Where(p => !p.IsHuman))
            {
                SafeFireAndForget(async () => 
                {
                    // Delay based on reaction time
                    // If CPU is at risk, they try to call it fast (0.5s - 1.5s)
                    // If they are catching someone else, they are slightly slower (1.0s - 2.2s)
                    int delay = (cpu == PlayerAtRisk) ? _random.Next(500, 1500) : _random.Next(1000, 2200);
                    await Task.Delay(delay);
                    
                    // Attempt the call if the QTE is still active
                    if (Status == GameStatus.WaitingForUnoCall && PlayerAtRisk != null)
                    {
                        await AttemptUnoCallAsync(cpu);
                    }
                });
            }
            return Task.CompletedTask;
        }

        private void ApplyRoundEndScores()
        {
            foreach (var p in Players)
            {
                int cards = p.Hand.Count;
                int roundPoints = Math.Max(5, 60 - (cards * 5));
                p.RoundScore  = roundPoints;
                p.TotalScore += roundPoints;
            }
        }

        private async Task TriggerCpuJumpInCheck(UnoCard? lastPlayed)
        {
            if (lastPlayed == null || !Settings.JumpInRule) return;

            int maxDelay = (int)(Settings.JumpInTimerSeconds * 1000);
            await Task.Delay(_random.Next(500, Math.Max(600, maxDelay - 200)));

            if (Status != GameStatus.WaitingForJumpIn) return;

            foreach (var cpu in Players.Where(p => !p.IsHuman))
            {
                var match = cpu.Hand.FirstOrDefault(c => c.Color == lastPlayed.Color && c.Value == lastPlayed.Value);
                if (match != null)
                {
                    await JumpInAsync(cpu, match);
                    break;
                }
            }
        }

        private async Task HandleSpecialActions(Player currentPlayer, UnoCard card, Player? targetPlayer)
        {
            switch (card.Value)
            {
                case CardValue.Skip:
                    if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"skip-{GetNextPlayerIndex()}");
                    _pendingExtraTurnAdvance = true; // Applied after the UNO risk check in EndPlayCardSequence
                    break;
                case CardValue.Reverse:
                    if (Players.Count == 2)
                    {
                        if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"skip-{GetNextPlayerIndex()}");
                        _pendingExtraTurnAdvance = true; // In 2 player, Reverse acts like Skip
                    }
                    else
                    {
                        GameDirection *= -1;
                    }
                    break;
                case CardValue.Draw2:
                    if (Settings.Stacking) PendingDrawCount += 2;
                    else await ApplyDrawAsync(2);
                    break;
                case CardValue.WildDraw4:
                    if (Settings.EnableWildDraw4Challenge && !Settings.Stacking && currentPlayer.Hand.Count >= 0)
                    {
                        _wd4ChallengerIndex = GetNextPlayerIndex();
                        Status = GameStatus.WaitingForWd4Challenge;
                        LogAction($"{Players[_wd4ChallengerIndex].Name} may challenge {currentPlayer.Name}'s Wild Draw 4!");
                        OnStateChanged?.Invoke();
                        if (!Players[_wd4ChallengerIndex].IsHuman)
                        {
                            SafeFireAndForget(() => HandleCpuWd4ChallengeAsync());
                        }
                        else if (RemotePlayerIndices.Contains(_wd4ChallengerIndex) && GetRemoteHumanMove != null)
                        {
                            // Remote human player is challenger
                            int capturedIdx = _wd4ChallengerIndex;
                            var moveGetter = GetRemoteHumanMove;
                            SafeFireAndForget(async () =>
                            {
                                var move = await moveGetter(capturedIdx);
                                if (move != null && Status == GameStatus.WaitingForWd4Challenge && Wd4ChallengerIndex == capturedIdx)
                                    await ApplyRemoteMoveAsync(move);
                            });
                        }
                        return;
                    }
                    if (Settings.Stacking) PendingDrawCount += 4;
                    else await ApplyDrawAsync(4);
                    break;
                case CardValue.Seven:
                    if (Settings.SevenSwap && targetPlayer != null)
                    {
                        if (!currentPlayer.IsHuman && OnBoardAnimation != null)
                        {
                            int actorIdx = Players.IndexOf(currentPlayer);
                            int targetIdx = Players.IndexOf(targetPlayer);
                            await OnBoardAnimation.Invoke($"seven-preview-{actorIdx}-{targetIdx}");
                        }
                        await PerformSevenSwap(currentPlayer, targetPlayer);
                    }
                    break;
                case CardValue.Zero:
                    if (Settings.ZeroRotate && currentPlayer.Hand.Count > 0)
                    {
                        await PerformZeroRotate();
                    }
                    break;
                case CardValue.Vortex:
                    await PerformVortexAsync(currentPlayer);
                    break;
            }
        }

        private async Task ApplyDrawAsync(int count)
        {
            int nextPlayerIndex = GetNextPlayerIndex();
            Player nextPlayer = Players[nextPlayerIndex];
            if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"penalty-{nextPlayerIndex}-{count}");
            for (int i = 0; i < count; i++)
            {
                if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{nextPlayerIndex}");
                nextPlayer.Hand.Add(DrawOne());
                if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                OnStateChanged?.Invoke();
                await Task.Delay(80);
            }
            _pendingExtraTurnAdvance = true; // Skip their turn after drawing, applied after the UNO risk check
        }

        public async Task HandlePendingDrawAsync()
        {
            if (PendingDrawCount > 0)
            {
                Player currentPlayer = Players[CurrentPlayerIndex];
                int currentIdx = CurrentPlayerIndex;
                if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"penalty-{currentIdx}-{PendingDrawCount}");
                for (int i = 0; i < PendingDrawCount; i++)
                {
                    if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{currentIdx}");
                    currentPlayer.Hand.Add(DrawOne());
                    OnStateChanged?.Invoke();
                    await Task.Delay(80); // Visual delay for card-by-card draw
                }
                currentPlayer.CurrentStatus = ""; // Clear stale status
                PendingDrawCount = 0;
                MoveToNextTurn();
                OnStateChanged?.Invoke();
                await StartTurnAsync();
            }
        }

        /// <summary>
        /// Draws exactly one card for the player and fires OnStateChanged.
        /// Returns the drawn card and whether it is currently playable.
        /// Used by the UI to drive animated one-by-one drawing.
        /// </summary>
        public async Task<(UnoCard? card, bool isPlayable)> DrawOneForHumanAsync(Player player)
        {
            if (Status == GameStatus.WaitingForJumpIn)
            {
                return (null, false);
            }
            var drawn = DrawOne();
            player.Hand.Add(drawn);
            OnStateChanged?.Invoke();
            await Task.Delay(200);
            return (drawn, CanPlayCard(drawn));
        }

        /// <summary>Passes the turn after the human drew without playing.</summary>
        public async Task PassTurnAfterDraw()
        {
            MoveToNextTurn();
            OnStateChanged?.Invoke();
            await StartTurnAsync();
        }

        public async Task<UnoCard?> DrawCardAsync(Player player, bool isPenaltyDraw = false)
        {
            if (Status == GameStatus.WaitingForJumpIn)
            {
                return null;
            }
            // Strict Rule Constraint: Under forced-play / no-reneging, block drawing if player holds a playable card.
            // AFK penalty draws bypass this — a player being penalised for inactivity must always receive their cards
            // even if they technically hold a playable card (they just failed to play it).
            if (!isPenaltyDraw && (Settings.ForcedPlay || !Settings.AllowReneging) && PendingDrawCount == 0)
            {
                var hasPlayableCard = player.Hand.Any(card => CanPlayCard(card));
                if (hasPlayableCard)
                {
                    // Only show the banner for the human player — CPU hits this as an internal guard
                    if (player.IsHuman)
                    {
                        ActiveNotificationBanner = $"{player.Name.ToUpper()} MUST PLAY A CARD!";
                        NotificationBannerTargetIndex = Players.IndexOf(player);
                    }
                    LogAction($"{player.Name} tried to draw but has a playable card.");
                    OnStateChanged?.Invoke();
                    return null;
                }
            }

            int playerIdx = Players.IndexOf(player);
            if (Settings.DrawUntilPlayable)
            {
                UnoCard drawn;
                do
                {
                    // Stop if game state changed (e.g. another player jumped in, game ended)
                    if (Status != GameStatus.Playing) return null;
                    if (OnBoardAnimation != null && !player.IsHuman) await OnBoardAnimation.Invoke($"draw-{playerIdx}");
                    drawn = DrawOne();
                    player.Hand.Add(drawn);
                    if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                    OnStateChanged?.Invoke();
                    await Task.Delay(400);
                } while (!CanPlayCard(drawn) && Status == GameStatus.Playing);

                // Loop exited because status changed, not because we found a playable card
                if (Status != GameStatus.Playing) return null;
                return drawn;
            }
            else
            {
                if (OnBoardAnimation != null && !player.IsHuman) await OnBoardAnimation.Invoke($"draw-{playerIdx}");
                UnoCard drawn = DrawOne();
                player.Hand.Add(drawn);
                if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                OnStateChanged?.Invoke();
                return drawn;
            }
        }

        private UnoCard DrawOne()
        {
            if (DrawPile.Count == 0)
            {
                ReshuffleDiscardIntoDraw();
            }

            if (DrawPile.Count == 0)
            {
                // Discard pile also empty — inject a fresh shuffled deck
                LogAction("A fresh deck was added to the draw pile.");
                var fresh = CreateDeck();
                Shuffle(fresh);
                DrawPile = new Stack<UnoCard>(fresh);
            }

            return DrawPile.Pop();
        }

        private void ReshuffleDiscardIntoDraw()
        {
            if (OnSoundEffect != null) _ = OnSoundEffect("shuffle");
            LogAction("Deck reshuffled from discard pile.");
            UnoCard currentTop = DiscardPile.Last();
            DiscardPile.RemoveAt(DiscardPile.Count - 1);

            List<UnoCard> newDeck = DiscardPile.Select(c => 
                (c.Value == CardValue.Wild || c.Value == CardValue.WildDraw4 || c.Value == CardValue.Vortex) 
                ? c with { Color = CardColor.Wild } // Reset wild colors
                : c
            ).ToList();

            Shuffle(newDeck);
            DrawPile = new Stack<UnoCard>(newDeck);
            DiscardPile = new List<UnoCard> { currentTop };
        }

        private async Task PerformSevenSwap(Player p1, Player p2)
        {
            if (OnBoardAnimation != null)
            {
                int p1Index = Players.IndexOf(p1);
                int p2Index = Players.IndexOf(p2);
                await OnBoardAnimation.Invoke($"swap-{p1Index}-{p2Index}");
            }

            // Ensure reference swap to avoid ghost cards
            var tempHand = p1.Hand;
            p1.Hand = p2.Hand;
            p2.Hand = tempHand;
            // State is broadcast by InternalPlayCard after this returns
        }

        private async Task PerformZeroRotate()
        {
            if (OnBoardAnimation != null)
            {
                string anim = GameDirection == 1 ? "rotate-cw" : "rotate-ccw";
                await OnBoardAnimation.Invoke(anim);
            }

            // Rotate hands in current GameDirection
            if (GameDirection == 1) // Clockwise: 0->1, 1->2, 2->3, 3->0
            {
                var lastHand = Players.Last().Hand;
                for (int i = Players.Count - 1; i > 0; i--)
                {
                    Players[i].Hand = Players[i - 1].Hand;
                }
                Players[0].Hand = lastHand;
            }
            else // Counter-Clockwise: 0->3, 3->2, 2->1, 1->0
            {
                var firstHand = Players.First().Hand;
                for (int i = 0; i < Players.Count - 1; i++)
                {
                    Players[i].Hand = Players[i + 1].Hand;
                }
                Players[Players.Count - 1].Hand = firstHand;
            }
            // State is broadcast by InternalPlayCard after this returns
        }

        private async Task PerformVortexAsync(Player activePlayer)
        {
            if (OnBoardAnimation != null)
            {
                await OnBoardAnimation.Invoke("vortex-shuffle");
            }
            else
            {
                ResolveVortex(activePlayer);
            }
        }

        public void ResolveVortex(Player activePlayer)
        {
            List<UnoCard> vortexPool = new();
            foreach (var p in Players)
            {
                vortexPool.AddRange(p.Hand);
                p.Hand.Clear();
            }

            Shuffle(vortexPool);

            int totalPoolCount = vortexPool.Count;
            if (totalPoolCount == 0) return;

            int activePlayerCount = Players.Count;
            int baseCardsPerPlayer = totalPoolCount / activePlayerCount;
            int remainderCards = totalPoolCount % activePlayerCount;

            // Deal base cards
            foreach (var p in Players)
            {
                for (int i = 0; i < baseCardsPerPlayer; i++)
                {
                    p.Hand.Add(vortexPool[vortexPool.Count - 1]);
                    vortexPool.RemoveAt(vortexPool.Count - 1);
                }
            }

            // Deal remainder cards clockwise starting from active player
            if (remainderCards > 0)
            {
                int activeIndex = Players.IndexOf(activePlayer);
                int nextIndex = activeIndex;
                while (remainderCards > 0)
                {
                    Players[nextIndex].Hand.Add(vortexPool[vortexPool.Count - 1]);
                    vortexPool.RemoveAt(vortexPool.Count - 1);
                    nextIndex = (nextIndex + 1) % Players.Count;
                    remainderCards--;
                }
            }

            OnStateChanged?.Invoke();
        }

        public async Task ResolveChallengeAsync(bool doChallenge)
        {
            await _actionLock.WaitAsync();
            try
            {
                if (Status != GameStatus.WaitingForWd4Challenge) return;

                Player challenger = Players[_wd4ChallengerIndex];

                if (doChallenge)
                {
                    if (_wd4Player != null)
                    {
                        ActiveNotificationBanner = $"{challenger.Name} is challenging {_wd4Player.Name}!";
                        NotificationBannerTargetIndex = -1;
                        OnStateChanged?.Invoke();
                    }
                    if (OnSoundEffect != null) await OnSoundEffect("challenge");
                    await Task.Delay(600);

                    if (_wd4HandSnapshotMatchingCards != null && _wd4HandSnapshotMatchingCards.Count > 0 && _wd4Player != null)
                    {
                        // Bluff caught!
                        Status = GameStatus.RevealingWd4Bluff;
                        OnStateChanged?.Invoke();
                        
                        // Wait for reveal animation
                        await Task.Delay(2000);
                        
                        // Now proceed with penalty
                        if (OnSoundEffect != null) await OnSoundEffect("challengeBluffCaught");
                        ActiveNotificationBanner = $"BLUFF CAUGHT! {_wd4Player.Name.ToUpper()} DRAWS 4!";
                        NotificationBannerTargetIndex = -1;
                        LogAction($"{challenger.Name} challenged — {_wd4Player.Name} was bluffing! They draw 4.");
                        int wd4PlayerIdx = Players.IndexOf(_wd4Player);
                        if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"penalty-{wd4PlayerIdx}-4");
                        for (int i = 0; i < 4; i++) { if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{wd4PlayerIdx}"); _wd4Player.Hand.Add(DrawOne()); OnStateChanged?.Invoke(); await Task.Delay(80); }
                        MoveToNextTurn();
                    }
                    else
                    {
                        // Wrong call!
                        Status = GameStatus.RevealingWd4Bluff;
                        _wd4HandSnapshotMatchingCards = null;
                        OnStateChanged?.Invoke();
                        
                        // Wait for reveal animation
                        await Task.Delay(2000);
                        
                        // Now proceed with penalty
                        if (OnSoundEffect != null) await OnSoundEffect("challengeFail");
                        int wrongPenalty = Settings.Wd4WrongChallengePenalty;
                        ActiveNotificationBanner = $"WRONG CALL! {challenger.Name.ToUpper()} DRAWS {wrongPenalty}!";
                        NotificationBannerTargetIndex = -1;
                        LogAction($"{challenger.Name} challenged — bluff NOT confirmed. {challenger.Name} draws {wrongPenalty}.");
                        int challengerIdx = Players.IndexOf(challenger);
                        if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"penalty-{challengerIdx}-{wrongPenalty}");
                        for (int i = 0; i < wrongPenalty; i++) { if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{challengerIdx}"); challenger.Hand.Add(DrawOne()); OnStateChanged?.Invoke(); await Task.Delay(80); }
                        MoveToNextTurn(); // advance to challenger
                        MoveToNextTurn(); // skip challenger — wrong-challenge still forfeits their turn
                    }
                }
                else
                {
                    LogAction($"{challenger.Name} accepts the Wild Draw 4 and draws 4.");
                    int acceptIdx = Players.IndexOf(challenger);
                    if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"penalty-{acceptIdx}-4");
                    for (int i = 0; i < 4; i++) { if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{acceptIdx}"); challenger.Hand.Add(DrawOne()); if (OnSoundEffect != null) await OnSoundEffect("cardDraw"); OnStateChanged?.Invoke(); await Task.Delay(80); }
                    MoveToNextTurn(); // advance to challenger
                    MoveToNextTurn(); // skip challenger — accepting the penalty forfeits their turn
                    Status = GameStatus.Playing;
                    _wd4Player = null;
                    _wd4HandSnapshotMatchingCards = null;
                    OnStateChanged?.Invoke();
                    SafeFireAndForget(() => StartTurnAsync());
                    return;
                }

                Status = GameStatus.Playing;
                _wd4Player = null;
                _wd4HandSnapshotMatchingCards = null;
                OnStateChanged?.Invoke();
                await Task.Delay(1000);
                SafeFireAndForget(() => StartTurnAsync());
            }
            finally
            {
                _actionLock.Release();
            }
        }



        private async Task HandleCpuWd4ChallengeAsync()
        {
            await Task.Delay(_random.Next(900, 2200));
            if (Status != GameStatus.WaitingForWd4Challenge) return;

            Player challenger = Players[_wd4ChallengerIndex];
            bool shouldChallenge = challenger.Hand.Count >= 6 && _random.Next(0, 100) < 35;
            await ResolveChallengeAsync(shouldChallenge);
        }

        private async Task ExecuteEasyCpuTurnAsync(Player cpu)
        {
            if (PendingDrawCount > 0)
            {
                cpu.CurrentStatus = $"Taking {PendingDrawCount} Penalty...";
                OnStateChanged?.Invoke();
                await Task.Delay(1000);
                await HandlePendingDrawAsync();
                return;
            }

            var playable = cpu.Hand.Where(c => CanPlayCard(c)).ToList();
            if (playable.Count > 0)
            {
                var pick = playable[_random.Next(playable.Count)];
                LogAction($"{cpu.Name} played {pick}.");
                await PlayCardAsync(cpu, pick, null);
            }
            else
            {
                cpu.CurrentStatus = "Drawing...";
                LogAction($"{cpu.Name} had no moves and is drawing...");
                var drawn = await DrawCardAsync(cpu);

                // Guard: abandon if status changed or turn shifted during draw loop
                if (Status != GameStatus.Playing || CurrentPlayerIndex != Players.IndexOf(cpu))
                    return;

                if (drawn != null && CanPlayCard(drawn))
                {
                    LogAction($"{cpu.Name} played the drawn {drawn}.");
                    await PlayCardAsync(cpu, drawn, null);
                }
                else
                {
                    MoveToNextTurn();
                    await StartTurnAsync();
                }
            }
        }

        private void MoveToNextTurn()
        {
            IsUnoCalled = false;
            _remoteDrawnCardIds.Clear();
            CurrentPlayerIndex = GetNextPlayerIndex();
        }

        /// <summary>
        /// Advances the turn once the UNO risk check for the card-player has been resolved
        /// (either immediately, or after the UNO QTE completes), then applies any extra
        /// advance that a Skip/Reverse(2p) play deferred so the skipped player isn't passed
        /// over before the UNO button had a chance to appear.
        /// </summary>
        private void AdvanceTurnAfterUnoCheck()
        {
            MoveToNextTurn();
            if (_pendingExtraTurnAdvance)
            {
                _pendingExtraTurnAdvance = false;
                MoveToNextTurn();
            }
        }

        private int GetNextPlayerIndex()
        {
            int index = CurrentPlayerIndex + GameDirection;
            // Use modulo for proper wrapping in both directions
            index = (index % Players.Count + Players.Count) % Players.Count;
            return index;
        }

        public Player GetCurrentPlayer() => Players[CurrentPlayerIndex];

        public async Task ApplyAfkPenaltyAsync(int expectedPlayerIndex, GameStatus expectedStatus)
        {
            // Flags captured inside the lock; the methods they trigger also acquire _actionLock,
            // so they must be called AFTER releasing it (same pattern as ApplyRemoteMoveAsync).
            bool afkShouldStartTurn = false;
            bool afkNeedsColor = false;
            CardColor afkChosenColor = CardColor.Red;
            bool afkNeedsChallenge = false;
            Player? afkPlayer = null;

            await _actionLock.WaitAsync();
            try
            {
                if (Status == GameStatus.GameOver) return;

                // The AFK timeout is detected OUTSIDE this lock (on a background timer), so the
                // player may have already acted (played/drawn/etc.) in the window between that
                // check and this task actually acquiring the lock. Re-validate atomically here,
                // BEFORE any notification/animation is shown, so a just-in-time move never causes
                // a visible-but-reverted "AFK — drawing" flash or penalizes the wrong player.
                if (Status != expectedStatus) return;
                int actualWaitingIndex = expectedStatus switch
                {
                    GameStatus.WaitingForColorSelection => PendingColorSelector != null ? Players.IndexOf(PendingColorSelector) : -1,
                    GameStatus.WaitingForWd4Challenge    => Wd4ChallengerIndex,
                    _                                     => CurrentPlayerIndex,
                };
                if (actualWaitingIndex != expectedPlayerIndex) return;

                var currentPlayer = GetCurrentPlayer();
                if (!currentPlayer.IsHuman) return;

                afkPlayer = currentPlayer;

                if (Status == GameStatus.WaitingForColorSelection && PendingColorSelector == currentPlayer)
                {
                    // Pick best color inside lock (reads hand), finalize outside (acquires lock again)
                    var counts = currentPlayer.Hand.Where(c => c.Color != CardColor.Wild)
                                                    .GroupBy(c => c.Color)
                                                    .OrderByDescending(g => g.Count());
                    afkChosenColor = counts.Any() ? counts.First().Key : (CardColor)_random.Next(0, 4);
                    afkNeedsColor = true;
                }
                else if (Status == GameStatus.WaitingForSwapTarget && CurrentPlayerIndex == Players.IndexOf(currentPlayer))
                {
                    // InternalPlayCard is safe to call inside the lock (it doesn't re-acquire it)
                    var target = Players.Where(p => p != currentPlayer).OrderBy(p => p.Hand.Count).First();
                    await InternalPlayCard(currentPlayer, PendingCard!, null, target);
                }
                else if (Status == GameStatus.WaitingForWd4Challenge && Wd4ChallengerIndex == Players.IndexOf(currentPlayer))
                {
                    // ResolveChallengeAsync acquires _actionLock — must run outside
                    afkNeedsChallenge = true;
                }
                else if (Status == GameStatus.Playing)
                {
                    // Normal AFK penalty
                    int penaltyCount = Settings.AfkPenaltyCards;
                    int afkIdx = CurrentPlayerIndex;
                    currentPlayer.CurrentStatus = $"⏰ AFK — drawing {penaltyCount} card{(penaltyCount != 1 ? "s" : "")}…";
                    OnStateChanged?.Invoke();
                    await Task.Delay(800);
                    if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"draw-{afkIdx}");
                    for (int i = 0; i < penaltyCount; i++)
                        await DrawCardAsync(currentPlayer, isPenaltyDraw: true);
                    currentPlayer.CurrentStatus = "";
                    LogAction($"⏰ {currentPlayer.Name} was AFK and drew {penaltyCount} card{(penaltyCount != 1 ? "s" : "")}.");
                    OnStateChanged?.Invoke();

                    if (Status != GameStatus.GameOver)
                    {
                        MoveToNextTurn();
                        // StartTurnAsync → ExecuteCpuTurn → PlayCardAsync all acquire _actionLock
                        // — must run outside the lock to avoid deadlock
                        afkShouldStartTurn = true;
                    }
                }
            }
            finally
            {
                _actionLock.Release();
            }

            // Post-lock: run actions that re-acquire _actionLock.
            // SetWildColorForRemoteAsync re-validates Status/PendingColorSelector under the lock
            // before finalizing, protecting against a race where another action already resolved color.
            if (afkNeedsColor && afkPlayer != null) await SetWildColorForRemoteAsync(afkChosenColor, afkPlayer);
            else if (afkNeedsChallenge) await ResolveChallengeAsync(false);
            else if (afkShouldStartTurn) await StartTurnAsync();
        }
    }
}
