namespace UnoEngine.Models
{
    public class GameSettings
    {
        /// <summary>
        /// Starting number of cards dealt to each player. Clamped between 3 and 10.
        /// </summary>
        public int StartingHandSize { get; set; } = 7;

        /// <summary>
        /// If a player draws, they keep drawing until they find a match.
        /// </summary>
        public bool DrawUntilPlayable { get; set; } = true;

        /// <summary>
        /// When a '7' is played, the actor MUST swap hands with a chosen target.
        /// </summary>
        public bool SevenSwap { get; set; } = true;

        /// <summary>
        /// When a '0' is played, everyone passes their entire hand to the next player in the current direction.
        /// </summary>
        public bool ZeroRotate { get; set; } = true;

        /// <summary>
        /// Allow stacking Draw 2s and Draw 4s.
        /// </summary>
        public bool Stacking { get; set; } = true;

        /// <summary>
        /// Allow players to play out of turn if they have an exact match (Color and Value).
        /// </summary>
        public bool JumpInRule { get; set; } = true;

        /// <summary>
        /// The duration of the QTE window for jumping in.
        /// </summary>
        public double JumpInTimerSeconds { get; set; } = 2.5;

        /// <summary>
        /// Whether jump-in able cards should glow in the UI to assist the player.
        /// </summary>
        public bool ShowJumpInGlow { get; set; } = true;

        /// <summary>
        /// If enabled, 4 Vortex cards are added to the deck.
        /// </summary>
        public bool EnableVortex { get; set; } = false;

        /// <summary>
        /// If enabled, players MUST play a card if they have a playable card in hand (cannot draw).
        /// </summary>
        public bool ForcedPlay { get; set; } = false;

        /// <summary>
        /// A player can choose to draw a card even if they hold a playable card in hand.
        /// This is the exact opposite of ForcedPlay.
        /// </summary>
        public bool AllowReneging
        {
            get => !ForcedPlay;
            set => ForcedPlay = !value;
        }

        /// <summary>
        /// Next player can challenge if they suspect the user holds a matching color on Wild Draw 4.
        /// </summary>
        public bool EnableWildDraw4Challenge { get; set; } = true;

        /// <summary>
        /// Penalty cards drawn if UNO call fails.
        /// </summary>
        public int UnoFailurePenaltyCards { get; set; } = 2;

        public GameSettings Clone()
        {
            return new GameSettings
            {
                StartingHandSize = this.StartingHandSize,
                DrawUntilPlayable = this.DrawUntilPlayable,
                SevenSwap = this.SevenSwap,
                ZeroRotate = this.ZeroRotate,
                Stacking = this.Stacking,
                JumpInRule = this.JumpInRule,
                JumpInTimerSeconds = this.JumpInTimerSeconds,
                ShowJumpInGlow = this.ShowJumpInGlow,
                EnableVortex = this.EnableVortex,
                ForcedPlay = this.ForcedPlay,
                AllowReneging = this.AllowReneging,
                EnableWildDraw4Challenge = this.EnableWildDraw4Challenge,
                UnoFailurePenaltyCards = this.UnoFailurePenaltyCards
            };
        }
    }
}
