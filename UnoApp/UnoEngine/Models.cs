using System;
using System.Collections.Generic;

namespace UnoEngine.Models
{
    public enum CardColor
    {
        Red,
        Blue,
        Green,
        Yellow,
        Wild
    }

    public enum CardValue
    {
        Zero, One, Two, Three, Four, Five, Six, Seven, Eight, Nine,
        Skip, Reverse, Draw2,
        Wild, WildDraw4, Vortex
    }

    public record UnoCard(Guid Id, CardColor Color, CardValue Value)
    {
        public float RotationAngle { get; set; } = 0f;
        public override string ToString() => $"{Color} {Value}";
    }

    public class Player
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public List<UnoCard> Hand { get; set; } = new();
        public bool IsHuman { get; set; }
        public int Score { get; set; } = 0;
        public int TotalScore { get; set; } = 0;
        public int RoundScore { get; set; } = 0;
        public string CurrentStatus { get; set; } = string.Empty;

        public override string ToString() => $"{Name} ({Hand.Count} cards)";
    }
}
