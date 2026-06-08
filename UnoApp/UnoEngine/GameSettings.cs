namespace UnoEngine.Models
{
    public enum AiDifficulty
    {
        Easy,
        Normal,
        Hard
    }

    public class GameSettings
    {
        /// <summary>
        /// Starting number of cards dealt to each player. Clamped between 3 and 10.
        /// </summary>
        public int StartingHandSize { get; set; } = 7;

        /// <summary>
        /// If a player draws, they keep drawing until they find a match.
        /// </summary>
        public bool DrawUntilPlayable { get; set; } = false;

        /// <summary>
        /// When a '7' is played, the actor MUST swap hands with a chosen target.
        /// </summary>
        public bool SevenSwap { get; set; } = false;

        /// <summary>
        /// When a '0' is played, everyone passes their entire hand to the next player in the current direction.
        /// </summary>
        public bool ZeroRotate { get; set; } = false;

        /// <summary>
        /// Allow stacking Draw 2s and Draw 4s.
        /// </summary>
        public bool Stacking { get; set; } = false;

        /// <summary>
        /// Allow players to play out of turn if they have an exact match (Color and Value).
        /// </summary>
        public bool JumpInRule { get; set; } = false;

        /// <summary>
        /// The duration of the QTE window for jumping in.
        /// </summary>
        public double JumpInTimerSeconds { get; set; } = 2.5;

        /// <summary>
        /// Whether jump-in able cards should glow in the UI to assist the player.
        /// </summary>
        public bool ShowJumpInGlow { get; set; } = false;

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
        /// Next player can challenge if they suspect the WD4 player holds a matching color.
        /// </summary>
        public bool EnableWildDraw4Challenge { get; set; } = false;

        /// <summary>
        /// Penalty cards drawn if UNO call fails.
        /// </summary>
        public int UnoFailurePenaltyCards { get; set; } = 2;

        /// <summary>
        /// CPU opponent difficulty level.
        /// </summary>
        public AiDifficulty CpuDifficulty { get; set; } = AiDifficulty.Normal;

        /// <summary>
        /// Cards the challenger draws when a WD4 challenge is wrong (no bluff detected). Default 6.
        /// </summary>
        public int Wd4WrongChallengePenalty { get; set; } = 6;

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
                EnableWildDraw4Challenge = this.EnableWildDraw4Challenge,
                UnoFailurePenaltyCards = this.UnoFailurePenaltyCards,
                CpuDifficulty = this.CpuDifficulty,
                Wd4WrongChallengePenalty = this.Wd4WrongChallengePenalty
            };
        }
    }
}
