namespace UnoEngine.Models
{
    public class GameSettings
    {
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
        /// Allow stacking Draw 2s (if Player A plays a +2, Player B can play a +2 to make Player C draw 4).
        /// </summary>
        public bool Stacking { get; set; } = false;

        /// <summary>
        /// Allow players to play out of turn if they have an exact match (Color and Value).
        /// </summary>
        public bool JumpInRule { get; set; } = true;

        /// <summary>
        /// The duration of the QTE window for jumping in.
        /// </summary>
        public double JumpInTimerSeconds { get; set; } = 2.5;
    }
}
