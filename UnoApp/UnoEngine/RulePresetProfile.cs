using System;

namespace UnoEngine.Models
{
    public class RulePresetProfile
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "Custom Preset";
        public bool IsReadonly { get; set; }
        public GameSettings Settings { get; set; } = new();

        public RulePresetProfile Clone()
        {
            return new RulePresetProfile
            {
                Id = this.Id,
                Name = this.Name,
                IsReadonly = this.IsReadonly,
                Settings = this.Settings.Clone()
            };
        }
    }
}
