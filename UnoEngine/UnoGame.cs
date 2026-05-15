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
        GameOver
    }

    public class UnoGame
    {
        public event Action? OnStateChanged;

        public List<Player> Players { get; private set; } = new();
        public Stack<UnoCard> DrawPile { get; private set; } = new();
        public List<UnoCard> DiscardPile { get; private set; } = new();
        public GameSettings Settings { get; private set; }
        public int CurrentPlayerIndex { get; private set; } = 0;
        public int GameDirection { get; private set; } = 1; 
        public int PendingDrawCount { get; private set; } = 0;
        public GameStatus Status { get; private set; } = GameStatus.Playing;
        public UnoCard? TopCard => DiscardPile.LastOrDefault();
        public UnoCard? PendingCard { get; private set; }
        public List<string> GameLog { get; private set; } = new();

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

            // Deal 7 cards to each player
            foreach (var player in Players)
            {
                player.Hand.Clear();
                for (int i = 0; i < 7; i++)
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
            if (!currentPlayer.IsHuman)
            {
                await Task.Delay(1000);
                await ExecuteCpuTurn(currentPlayer);
            }
        }

        private async Task ExecuteCpuTurn(Player cpu)
        {
            // 1. Check for Pending Draw (Stacking)
            if (PendingDrawCount > 0)
            {
                var stackable = cpu.Hand.FirstOrDefault(c => 
                    (TopCard?.Value == CardValue.Draw2 && c.Value == CardValue.Draw2) ||
                    (TopCard?.Value == CardValue.WildDraw4 && c.Value == CardValue.WildDraw4));

                if (stackable != null)
                {
                    LogAction($"{cpu.Name} stacked a {stackable.Value}!");
                    await PlayCardAsync(cpu, stackable);
                    return;
                }
                else
                {
                    LogAction($"{cpu.Name} couldn't stack and drew {PendingDrawCount} cards.");
                    HandlePendingDraw();
                    await StartTurnAsync();
                    return;
                }
            }

            // 2. Strategy: Rule 7 (Swap)
            var card7 = cpu.Hand.FirstOrDefault(c => c.Value == CardValue.Seven && CanPlayCard(c));
            if (card7 != null && Settings.SevenSwap)
            {
                var target = Players.Where(p => p != cpu).OrderBy(p => p.Hand.Count).First();
                LogAction($"{cpu.Name} played a 7 and swapped with {target.Name}!");
                await PlayCardAsync(cpu, card7, null, target);
                return;
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

            // 5. Standard Play
            var match = cpu.Hand.FirstOrDefault(CanPlayCard);
            if (match != null)
            {
                LogAction($"{cpu.Name} played {match}.");
                await PlayCardAsync(cpu, match, CardColor.Red);
                return;
            }

            // 6. Draw Fallback
            LogAction($"{cpu.Name} had no moves and is drawing...");
            var drawn = DrawCard(cpu);
            LogAction($"{cpu.Name} drew {drawn}.");
            
            if (Settings.DrawUntilPlayable)
            {
                if (CanPlayCard(drawn))
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
            else
            {
                // DrawCard already called MoveToNextTurn for non-playable draw
                await StartTurnAsync();
            }
        }

        public async Task PlayCardAsync(Player player, UnoCard card, CardColor? declaredColor = null, Player? targetPlayer = null)
        {
            if (Status == GameStatus.Playing && !CanPlayCard(card)) throw new Exception("Invalid move.");

            await _actionLock.WaitAsync();
            try
            {
                InternalPlayCard(player, card, declaredColor, targetPlayer);
            }
            finally
            {
                _actionLock.Release();
            }

            // Trigger CPU Jump-In check for others
            _ = Task.Run(() => TriggerCpuJumpInCheck(TopCard));

            OnStateChanged?.Invoke();
            await StartTurnAsync();
        }

        public async Task<bool> JumpInAsync(Player player, UnoCard card)
        {
            if (!Settings.JumpInRule) return false;
            if (TopCard == null) return false;

            // Cannot jump in on your own turn
            if (GetCurrentPlayer() == player) return false;

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
                
                InternalPlayCard(player, card);
            }
            finally
            {
                _actionLock.Release();
            }

            // Trigger CPU Jump-In check for others
            _ = Task.Run(() => TriggerCpuJumpInCheck(TopCard));
            
            OnStateChanged?.Invoke();
            await StartTurnAsync();
            return true;
        }

        private void InternalPlayCard(Player player, UnoCard card, CardColor? declaredColor = null, Player? targetPlayer = null)
        {
            if (Status != GameStatus.Playing && Status != GameStatus.WaitingForColorSelection && Status != GameStatus.WaitingForSwapTarget) 
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
                PendingCard = card;
                Status = GameStatus.WaitingForSwapTarget;
                return;
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

            HandleSpecialActions(player, playedCard, targetPlayer);

            if (player.Hand.Count == 0)
            {
                Status = GameStatus.GameOver;
            }

            MoveToNextTurn();
        }

        private async Task TriggerCpuJumpInCheck(UnoCard? lastPlayed)
        {
            if (lastPlayed == null || !Settings.JumpInRule) return;

            // Wait a random delay for human reaction
            await Task.Delay(_random.Next(500, 1500));

            foreach (var cpu in Players.Where(p => !p.IsHuman))
            {
                var match = cpu.Hand.FirstOrDefault(c => c.Color == lastPlayed.Color && c.Value == lastPlayed.Value);
                if (match != null)
                {
                    // Attempt Jump-In
                    await JumpInAsync(cpu, match);
                    break; // Only one CPU jumps in at a time for simplicity
                }
            }
        }

        private void HandleSpecialActions(Player currentPlayer, UnoCard card, Player targetPlayer)
        {
            switch (card.Value)
            {
                case CardValue.Skip:
                    MoveToNextTurn();
                    break;
                case CardValue.Reverse:
                    if (Players.Count == 2)
                    {
                        MoveToNextTurn(); // In 2 player, Reverse acts like Skip
                    }
                    else
                    {
                        GameDirection *= -1;
                    }
                    break;
                case CardValue.Draw2:
                    if (Settings.Stacking) PendingDrawCount += 2;
                    else ApplyDraw(2);
                    break;
                case CardValue.WildDraw4:
                    if (Settings.Stacking) PendingDrawCount += 4;
                    else ApplyDraw(4);
                    break;
                case CardValue.Seven:
                    if (Settings.SevenSwap && targetPlayer != null)
                    {
                        PerformSevenSwap(currentPlayer, targetPlayer);
                    }
                    break;
                case CardValue.Zero:
                    if (Settings.ZeroRotate)
                    {
                        PerformZeroRotate();
                    }
                    break;
            }
        }

        private void ApplyDraw(int count)
        {
            int nextPlayerIndex = GetNextPlayerIndex();
            Player nextPlayer = Players[nextPlayerIndex];
            for (int i = 0; i < count; i++)
            {
                nextPlayer.Hand.Add(DrawOne());
            }
            MoveToNextTurn(); // Skip their turn after drawing
        }

        public void HandlePendingDraw()
        {
            if (PendingDrawCount > 0)
            {
                Player currentPlayer = Players[CurrentPlayerIndex];
                for (int i = 0; i < PendingDrawCount; i++)
                {
                    currentPlayer.Hand.Add(DrawOne());
                }
                PendingDrawCount = 0;
                MoveToNextTurn();
            }
        }

        public UnoCard DrawCard(Player player)
        {
            if (Settings.DrawUntilPlayable)
            {
                UnoCard drawn;
                do
                {
                    drawn = DrawOne();
                    player.Hand.Add(drawn);
                    OnStateChanged?.Invoke();
                } while (!CanPlayCard(drawn));
                
                return drawn;
            }
            else
            {
                UnoCard drawn = DrawOne();
                player.Hand.Add(drawn);
                MoveToNextTurn();
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

        private void PerformSevenSwap(Player p1, Player p2)
        {
            // Ensure reference swap to avoid ghost cards
            var tempHand = p1.Hand;
            p1.Hand = p2.Hand;
            p2.Hand = tempHand;
        }

        private void PerformZeroRotate()
        {
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
        }

        private void MoveToNextTurn()
        {
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
