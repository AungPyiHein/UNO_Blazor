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
        GameOver
    }

    public class UnoGame
    {
        public event Action? OnStateChanged;
        public Func<string, Task>? OnBoardAnimation;

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
        public UnoCard? PendingCard { get; private set; }
        public Player? PlayerAtRisk { get; private set; }
        public List<string> GameLog { get; private set; } = new();
        public Player? Winner { get; private set; }
        public int WinnerScore { get; private set; }
        public bool IsUnoCalled { get; set; } = false;

        private Random _random = new();
        private System.Threading.SemaphoreSlim _actionLock = new(1, 1);

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

            // Deal 5 cards to each player
            foreach (var player in Players)
            {
                player.Hand.Clear();
                for (int i = 0; i < 5; i++)
                {
                    player.Hand.Add(DrawPile.Pop());
                }
            }

            // Initial discard
            UnoCard firstCard = DrawPile.Pop();
            while (firstCard.Color == CardColor.Wild) // Standard Uno: First card cannot be Wild
            {
                DrawPile.Push(firstCard);
                ShuffleDeck(); // Re-shuffle or just insert at bottom? Let's just re-shuffle for simplicity.
                firstCard = DrawPile.Pop();
            }
            DiscardPile.Add(firstCard);
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
                
                if (Settings.EnableWildShuffleCard)
                {
                    deck.Add(new UnoCard(Guid.NewGuid(), CardColor.Wild, CardValue.WildShuffle));
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

        public bool CanPlayCard(UnoCard card)
        {
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

            if (!currentPlayer.IsHuman)
            {
                await Task.Delay(1500); // Slower CPU thinking
                // Check if it's still this CPU's turn after the delay (in case of human jump-in)
                if (Status != GameStatus.GameOver && CurrentPlayerIndex == Players.IndexOf(currentPlayer))
                {
                    await ExecuteCpuTurn(currentPlayer);
                }
            }
        }

        private async Task ExecuteCpuTurn(Player cpu)
        {
            // 1. Check for Pending Draw (Stacking)
            if (PendingDrawCount > 0)
            {
                // We only reach here if Stacking is ON and CPU has a stackable card (guaranteed by StartTurnAsync)
                var stackable = cpu.Hand.First(c => c.Value == TopCard?.Value);
                LogAction($"{cpu.Name} STACKED a {stackable}!");
                // Always pass a declared color — WildDraw4 is Wild color and would trigger color picker without it
                await PlayCardAsync(cpu, stackable, CardColor.Red);
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
                    await PlayCardAsync(cpu, powerCard, CardColor.Red); 
                    return;
                }
            }

            // 5. Standard Play — exclude 7 when SevenSwap is on (only played via strategy with explicit target)
            var match = cpu.Hand.FirstOrDefault(c => CanPlayCard(c) && !(c.Value == CardValue.Seven && Settings.SevenSwap));
            if (match != null)
            {
                LogAction($"{cpu.Name} played {match}.");
                await PlayCardAsync(cpu, match, CardColor.Red);
                return;
            }

            // 6. Draw Fallback
            cpu.CurrentStatus = "Drawing...";
            LogAction($"{cpu.Name} had no moves and is drawing...");
            var drawn = await DrawCardAsync(cpu);
            LogAction($"{cpu.Name} drew {drawn}.");
            
            if (CanPlayCard(drawn) && !(drawn.Value == CardValue.Seven && Settings.SevenSwap))
            {
                LogAction($"{cpu.Name} played the drawn {drawn}.");
                await PlayCardAsync(cpu, drawn, CardColor.Red);
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

        public async Task<bool> JumpInAsync(Player player, UnoCard card)
        {
            if (!Settings.JumpInRule) return false;
            if (TopCard == null) return false;

            // Prevent jump-in with special-action cards that trigger UI pickers.
            // Cascading "pick swap target" inside a jump-in window is very confusing UX.
            if (card.Value == CardValue.Seven && Settings.SevenSwap) return false;
            if (card.Value == CardValue.Zero && Settings.ZeroRotate) return false;

            // Strict Validation: Exact Color AND Exact Value
            if (card.Color != TopCard.Color || card.Value != TopCard.Value) return false;

            await _actionLock.WaitAsync();
            try
            {
                // Re-validate inside lock in case someone else jumped in
                if (TopCard == null || card.Color != TopCard.Color || card.Value != TopCard.Value) return false;

                LogAction($"{player.Name} JUMPED IN with {card}!");
                
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
            
            // Handle Wild requiring color selection
            if (card.Color == CardColor.Wild && declaredColor == null)
            {
                PendingCard = card;
                Status = GameStatus.WaitingForColorSelection;
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

            if (player.Hand.Contains(card))
            {
                player.Hand.Remove(card);
            }
            else if (card == PendingCard)
            {
                PendingCard = null;
            }
            
            UnoCard playedCard = card;
            if (card.Color == CardColor.Wild && declaredColor.HasValue)
            {
                playedCard = card with { Color = declaredColor.Value };
            }
            else if (card.Color == CardColor.Wild)
            {
                playedCard = card with { Color = CardColor.Red }; // Fallback
            }
            
            DiscardPile.Add(playedCard);
            Status = GameStatus.Playing;

            await HandleSpecialActions(player, playedCard, targetPlayer);

            if (player.Hand.Count == 1)
            {
                PlayerAtRisk = player;
                _ = Task.Run(() => RunUnoTimerAsync(player));
                return; // Turn sequence will resume after UNO QTE
            }

            if (player.Hand.Count == 0)
            {
                Status = GameStatus.GameOver;
                Winner = player;
                player.CurrentStatus = "WINNER!";
                WinnerScore = CalculateWinnerScore();
                LogAction($"{player.Name} WINS with {WinnerScore} points!");
                return;
            }

            MoveToNextTurn();
            
            if (Settings.JumpInRule && IsJumpInPossible())
            {
                _ = Task.Run(() => RunJumpInTimerAsync());
            }
            else
            {
                _ = Task.Run(async () => {
                    await Task.Delay(1500); // Slower visual delay before the next turn starts
                    await StartTurnAsync();
                });
            }
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
            _ = Task.Run(() => TriggerCpuJumpInCheck(TopCard));

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
            _ = Task.Run(() => TriggerCpuUnoCheck());

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
                    for (int i = 0; i < 2; i++) playerAtRisk.Hand.Add(DrawOne());
                    PlayerAtRisk = null;
                    Status = GameStatus.Playing;
                    
                    MoveToNextTurn();
                    
                    if (Settings.JumpInRule && IsJumpInPossible())
                    {
                        _ = Task.Run(() => RunJumpInTimerAsync());
                    }
                    else
                    {
                        _ = StartTurnAsync();
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
                }
                else
                {
                    LogAction($"{caller.Name} CAUGHT {PlayerAtRisk.Name}! {PlayerAtRisk.Name} draws 2.");
                    for (int i = 0; i < 2; i++) PlayerAtRisk.Hand.Add(DrawOne());
                }

                PlayerAtRisk = null;
                Status = GameStatus.Playing;

                MoveToNextTurn();

                if (Settings.JumpInRule && IsJumpInPossible())
                {
                    _ = Task.Run(() => RunJumpInTimerAsync());
                }
                else
                {
                    _ = StartTurnAsync();
                }
            }
            finally
            {
                _actionLock.Release();
            }
            OnStateChanged?.Invoke();
        }

        private async Task TriggerCpuUnoCheck()
        {
            if (Status != GameStatus.WaitingForUnoCall || PlayerAtRisk == null) return;

            // Each CPU starts its own independent reaction timer
            foreach (var cpu in Players.Where(p => !p.IsHuman))
            {
                _ = Task.Run(async () => 
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
                        CardValue.Zero => 0,
                        CardValue.One => 1,
                        CardValue.Two => 2,
                        CardValue.Three => 3,
                        CardValue.Four => 4,
                        CardValue.Five => 5,
                        CardValue.Six => 6,
                        CardValue.Seven => 7,
                        CardValue.Eight => 8,
                        CardValue.Nine => 9,
                        CardValue.Skip => 20,
                        CardValue.Reverse => 20,
                        CardValue.Draw2 => 20,
                        CardValue.Wild => 50,
                        CardValue.WildDraw4 => 50,
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

        private async Task HandleSpecialActions(Player currentPlayer, UnoCard card, Player targetPlayer)
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
                    if (Settings.Stacking) PendingDrawCount += 4;
                    else await ApplyDrawAsync(4);
                    break;
                case CardValue.Seven:
                    if (Settings.SevenSwap && targetPlayer != null)
                    {
                        await PerformSevenSwap(currentPlayer, targetPlayer);
                    }
                    break;
                case CardValue.Zero:
                    if (Settings.ZeroRotate)
                    {
                        await PerformZeroRotate();
                    }
                    break;
                case CardValue.WildShuffle:
                    await PerformWildShuffleAsync();
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

        public async Task<UnoCard> DrawCardAsync(Player player)
        {
            if (Settings.DrawUntilPlayable)
            {
                UnoCard drawn;
                do
                {
                    drawn = DrawOne();
                    player.Hand.Add(drawn);
                    OnStateChanged?.Invoke();
                    await Task.Delay(800); // Even slower visual card draw
                } while (!CanPlayCard(drawn));
                
                return drawn;
            }
            else
            {
                UnoCard drawn = DrawOne();
                player.Hand.Add(drawn);
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

            if (DrawPile.Count == 0) throw new Exception("No cards left in game!");

            return DrawPile.Pop();
        }

        private void ReshuffleDiscardIntoDraw()
        {
            UnoCard currentTop = DiscardPile.Last();
            DiscardPile.RemoveAt(DiscardPile.Count - 1);

            List<UnoCard> newDeck = DiscardPile.Select(c => 
                (c.Value == CardValue.Wild || c.Value == CardValue.WildDraw4) 
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

        private async Task PerformWildShuffleAsync()
        {
            if (OnBoardAnimation != null)
            {
                await OnBoardAnimation.Invoke("vortex-shuffle");
            }

            // Collect all cards
            List<UnoCard> allCards = new();
            foreach (var p in Players)
            {
                allCards.AddRange(p.Hand);
                p.Hand.Clear();
            }

            // Shuffle them
            Shuffle(allCards);

            // Distribute evenly
            int totalCards = allCards.Count;
            int baseCount = totalCards / Players.Count;
            int remainder = totalCards % Players.Count;

            int cardIndex = 0;
            foreach (var p in Players)
            {
                int cardsToGive = baseCount;
                if (remainder > 0)
                {
                    // Give remainder randomly. For simplicity, just give to the first few players after shuffle
                    cardsToGive++;
                    remainder--;
                }

                for (int i = 0; i < cardsToGive; i++)
                {
                    p.Hand.Add(allCards[cardIndex++]);
                }
            }
            
            OnStateChanged?.Invoke();
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
