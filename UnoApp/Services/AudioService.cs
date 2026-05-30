using Microsoft.JSInterop;

namespace UnoApp.Services
{
    public class AudioService
    {
        private readonly IJSRuntime _js;
        private bool _initialized = false;

        public bool Enabled { get; private set; } = true;

        public AudioService(IJSRuntime js)
        {
            _js = js;
        }

        private async Task EnsureInit()
        {
            if (_initialized) return;
            _initialized = true;
            try { await _js.InvokeVoidAsync("UnoAudio.init"); } catch { }
        }

        public async Task PlayAsync(string soundName)
        {
            try
            {
                await EnsureInit();
                await _js.InvokeVoidAsync("UnoAudio.play", soundName);
            }
            catch { }
        }

        public void Play(string soundName)
        {
            _ = PlayAsync(soundName);
        }

        public async Task SetEnabledAsync(bool enabled)
        {
            Enabled = enabled;
            try { await _js.InvokeVoidAsync("UnoAudio.setEnabled", enabled); } catch { }
        }

        public async Task SetVolumeAsync(double volume)
        {
            try { await _js.InvokeVoidAsync("UnoAudio.setVolume", volume); } catch { }
        }
    }
}
