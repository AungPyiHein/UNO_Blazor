using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnoEngine.Models;

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
        public bool CanCallUno => (CurrentPlayerIndex == 0 && Players[0].Hand.Count == 2 && !IsUnoCalled && Status == GameStatus.Playing) || (Status == GameStatus.WaitingForUnoCall && PlayerAtRisk == Players[0]);
        public int LastUnoViolatorIndex { get; set; } = -1;
        public string ActiveNotificationBanner { get; set; } = "";
        public CardColor LastValidColor { get; set; }

        private Random _random = new();
        private System.Threading.SemaphoreSlim _actionLock = new(1, 1);

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

            // Safety: if status got stuck in WaitingForSwapTarget on a non-human turn, reset it
            if (Status == GameStatus.WaitingForSwapTarget && CurrentPlayerIndex != 0)
            {
                Status = GameStatus.Playing;
                PendingCard = null;
            }

            foreach (var p in Players) p.CurrentStatus = p == currentPlayer ? "Thinking..." : "";
            OnStateChanged?.Invoke();

            if (!currentPlayer.IsHuman)
            {
                int cpuThinkDelay = Settings.CpuDifficulty switch
                {
                    AiDifficulty.Easy => 2200,
                    AiDifficulty.Hard => 900,
                    _ => 1500
                };
                await Task.Delay(cpuThinkDelay);
                // Check if it's still this CPU's turn after the delay (in case of human jump-in)
                if (Status != GameStatus.GameOver && CurrentPlayerIndex == Players.IndexOf(currentPlayer))
                {
                    await ExecuteCpuTurn(currentPlayer);
                }
            }
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
            if (card.Color == CardColor.Wild && card.Value != CardValue.Vortex && declaredColor == null)
            {
                if (player.Hand.Contains(card)) player.Hand.Remove(card);
                else if (card == PendingCard) PendingCard = null;

                card.RotationAngle = (float)(_random.NextDouble() * 30.0 - 15.0);
                DiscardPile.Add(card);

                PendingColorSelector = player;
                LastPlayerIndex = Players.IndexOf(player);
                Status = GameStatus.WaitingForColorSelection;
                OnStateChanged?.Invoke();

                if (!player.IsHuman)
                {
                    SafeFireAndForget(() => HandleCpuColorSelectionAsync(player));
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
            Status = GameStatus.Playing;

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
            if (player.Hand.Count == 1)
            {
                if (IsUnoCalled)
                {
                    IsUnoCalled = false; // Reset
                    PlayerAtRisk = null;
                }
                else
                {
                    PlayerAtRisk = player;
                    SafeFireAndForget(() => RunUnoTimerAsync(player));
                    return; // Turn sequence will resume after UNO QTE
                }
            }

            if (player.Hand.Count == 0)
            {
                Status = GameStatus.GameOver;
                Winner = player;
                player.CurrentStatus = "WINNER!";
                WinnerScore = CalculateWinnerScore();
                player.TotalScore += WinnerScore;
                LogAction($"{player.Name} WINS with {WinnerScore} points!");
                if (OnSoundEffect != null) await OnSoundEffect("win");
                return;
            }

            if (Status == GameStatus.WaitingForWd4Challenge) return;

            MoveToNextTurn();
            
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
                    LogAction($"Nobody called UNO! {playerAtRisk.Name} drew 2 penalty cards.");
                    for (int i = 0; i < 2; i++)
                    {
                        playerAtRisk.Hand.Add(DrawOne());
                        if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                        OnStateChanged?.Invoke();
                        await Task.Delay(300);
                    }
                    PlayerAtRisk = null;
                    Status = GameStatus.Playing;
                    
                    MoveToNextTurn();
                    
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
                    if (OnSoundEffect != null) await OnSoundEffect("unoCall");
                }
                else
                {
                    LogAction($"{caller.Name} CAUGHT {PlayerAtRisk.Name}! {PlayerAtRisk.Name} draws 2.");
                    LastUnoViolatorIndex = Players.IndexOf(PlayerAtRisk);
                    ActiveNotificationBanner = $"{caller.Name.ToUpper()} CAUGHT {PlayerAtRisk.Name.ToUpper()}!";
                    if (OnSoundEffect != null) await OnSoundEffect("caught");
                    for (int i = 0; i < 2; i++)
                    {
                        PlayerAtRisk.Hand.Add(DrawOne());
                        if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                        OnStateChanged?.Invoke();
                        await Task.Delay(300);
                    }
                }

                PlayerAtRisk = null;
                Status = GameStatus.Playing;

                MoveToNextTurn();

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

        private int CalculateWinnerScore()
        {
            int total = 0;
            foreach (var player in Players)
            {
                foreach (var card in player.Hand)
                {
                    total += card.Value switch
                    {
                        _ when (int)card.Value <= 9 => (int)card.Value,
                        CardValue.Skip => 20,
                        CardValue.Reverse => 20,
                        CardValue.Draw2 => 20,
                        CardValue.Wild => 50,
                        CardValue.WildDraw4 => 50,
                        CardValue.Vortex => 50,
                        _ => 0
                    };
                }
            }
            return total;
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
                    MoveToNextTurn();
                    break;
                case CardValue.Reverse:
                    if (Players.Count == 2)
                    {
                        if (OnBoardAnimation != null) await OnBoardAnimation.Invoke($"skip-{GetNextPlayerIndex()}");
                        MoveToNextTurn(); // In 2 player, Reverse acts like Skip
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
                    if (Settings.ZeroRotate)
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
            for (int i = 0; i < count; i++)
            {
                nextPlayer.Hand.Add(DrawOne());
                if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                OnStateChanged?.Invoke();
                await Task.Delay(300);
            }
            MoveToNextTurn(); // Skip their turn after drawing
        }

        public async Task HandlePendingDrawAsync()
        {
            if (PendingDrawCount > 0)
            {
                Player currentPlayer = Players[CurrentPlayerIndex];
                for (int i = 0; i < PendingDrawCount; i++)
                {
                    currentPlayer.Hand.Add(DrawOne());
                    OnStateChanged?.Invoke();
                    await Task.Delay(300); // Visual delay for card-by-card draw
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
        public async Task<(UnoCard card, bool isPlayable)> DrawOneForHumanAsync(Player player)
        {
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

        public async Task<UnoCard?> DrawCardAsync(Player player)
        {
            // Strict Rule Constraint: Under forced-play / no-reneging, block drawing if player holds a playable card
            if ((Settings.ForcedPlay || !Settings.AllowReneging) && PendingDrawCount == 0)
            {
                var hasPlayableCard = player.Hand.Any(card => CanPlayCard(card));
                if (hasPlayableCard)
                {
                    ActiveNotificationBanner = $"{player.Name.ToUpper()} MUST PLAY A CARD!";
                    LogAction($"{player.Name} tried to draw but has a playable card.");
                    OnStateChanged?.Invoke();
                    return null;
                }
            }

            if (Settings.DrawUntilPlayable)
            {
                UnoCard drawn;
                do
                {
                    drawn = DrawOne();
                    player.Hand.Add(drawn);
                    if (OnSoundEffect != null) await OnSoundEffect("cardDraw");
                    OnStateChanged?.Invoke();
                    await Task.Delay(800); // Even slower visual card draw
                } while (!CanPlayCard(drawn));
                
                return drawn;
            }
            else
            {
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
                        LogAction($"{challenger.Name} challenged — {_wd4Player.Name} was bluffing! They draw 4.");
                        for (int i = 0; i < 4; i++) { _wd4Player.Hand.Add(DrawOne()); OnStateChanged?.Invoke(); await Task.Delay(250); }
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
                        ActiveNotificationBanner = $"WRONG CALL! {challenger.Name.ToUpper()} DRAWS 6!";
                        LogAction($"{challenger.Name} challenged — bluff NOT confirmed. {challenger.Name} draws 6.");
                        for (int i = 0; i < 6; i++) { challenger.Hand.Add(DrawOne()); OnStateChanged?.Invoke(); await Task.Delay(250); }
                        MoveToNextTurn();
                    }
                }
                else
                {
                    LogAction($"{challenger.Name} accepts the Wild Draw 4 and draws 4.");
                    for (int i = 0; i < 4; i++) { challenger.Hand.Add(DrawOne()); if (OnSoundEffect != null) await OnSoundEffect("cardDraw"); OnStateChanged?.Invoke(); await Task.Delay(250); }
                    MoveToNextTurn();
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
            CurrentPlayerIndex = GetNextPlayerIndex();
        }

        private int GetNextPlayerIndex()
        {
            int index = CurrentPlayerIndex + GameDirection;
            if (index >= Players.Count) index = 0;
            if (index < 0) index = Players.Count - 1;
            return index;
        }

        public Player GetCurrentPlayer() => Players[CurrentPlayerIndex];
    }
}
