using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnoEngine.Models;

namespace UnoEngine
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Uno Engine Simulation Phase 3 (Jump-In) ===");

            var settings = new GameSettings
            {
                DrawUntilPlayable = true,
                SevenSwap = true,
                ZeroRotate = true,
                Stacking = true,
                JumpInRule = true
            };

            var players = new List<Player>
            {
                new Player { Name = "Human (P0)", IsHuman = true },
                new Player { Name = "CPU 1 (P1)", IsHuman = false },
                new Player { Name = "CPU 2 (P2)", IsHuman = false },
                new Player { Name = "CPU 3 (P3)", IsHuman = false }
            };

            var game = new UnoGame(players, settings);
            game.OnStateChanged += () => {
                // Throttle logs for console
            };

            Console.WriteLine($"Game started! First card: {game.TopCard}");
            
            // Background task to print logs
            _ = Task.Run(async () => {
                int lastCount = 0;
                while (game.Status != GameStatus.GameOver)
                {
                    if (game.GameLog.Count > lastCount)
                    {
                        for (int i = lastCount; i < game.GameLog.Count; i++)
                        {
                            Console.WriteLine($"[LOG] {game.GameLog[i]}");
                        }
                        lastCount = game.GameLog.Count;
                    }
                    await Task.Delay(100);
                }
            });

            // Initial turn
            await game.StartTurnAsync();

            // Run until game over
            while (game.Status != GameStatus.GameOver)
            {
                var current = game.GetCurrentPlayer();
                if (current.IsHuman)
                {
                    await SimulateHumanTurn(game);
                }
                
                await Task.Delay(500); 
            }

            Console.WriteLine("\n=== Game Over! ===");
            foreach (var p in game.Players) Console.WriteLine($"{p.Name}: {p.Hand.Count} cards left.");
        }

        static async Task SimulateHumanTurn(UnoGame game)
        {
            var p = game.GetCurrentPlayer();

            if (game.Status == GameStatus.WaitingForColorSelection)
            {
                await game.PlayCardAsync(p, game.PendingCard!, CardColor.Red);
                return;
            }

            if (game.Status == GameStatus.WaitingForSwapTarget)
            {
                var target = game.Players.Where(player => player != p).OrderBy(_ => Guid.NewGuid()).First();
                await game.PlayCardAsync(p, game.PendingCard!, targetPlayer: target);
                return;
            }

            // Check for Jump-In opportunity (Exact match)
            foreach (var player in game.Players)
            {
                var jumpMatch = player.Hand.FirstOrDefault(c => game.TopCard != null && c.Color == game.TopCard.Color && c.Value == game.TopCard.Value);
                if (jumpMatch != null && player.IsHuman) // Simulation: Human jumps in if they have a match
                {
                    bool success = await game.JumpInAsync(player, jumpMatch);
                    if (success) return;
                }
            }

            var card = p.Hand.FirstOrDefault(game.CanPlayCard);
            if (card != null)
            {
                if (card.Color == CardColor.Wild)
                    await game.PlayCardAsync(p, card, CardColor.Blue);
                else if (card.Value == CardValue.Seven && game.Settings.SevenSwap)
                    await game.PlayCardAsync(p, card, null, game.Players.First(x => x != p));
                else
                    await game.PlayCardAsync(p, card);
            }
            else
            {
                game.DrawCard(p);
            }
        }
    }
}
