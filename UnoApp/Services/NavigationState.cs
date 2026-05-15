using System;

namespace UnoApp.Services
{
    public class NavigationState
    {
        private bool _isArenaMode;
        public bool IsArenaMode
        {
            get => _isArenaMode;
            set
            {
                if (_isArenaMode != value)
                {
                    _isArenaMode = value;
                    OnStateChanged?.Invoke();
                }
            }
        }

        public event Action? OnStateChanged;
    }
}
