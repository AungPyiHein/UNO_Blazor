window.UnoLeaveGuard = (() => {
    function handler(e) {
        e.preventDefault();
        e.returnValue = '';
        return '';
    }
    return {
        enable:  function() { window.addEventListener('beforeunload', handler); },
        disable: function() { window.removeEventListener('beforeunload', handler); }
    };
})();

window.UnoAudio = (() => {
    let ctx = null;
    let masterGainNode = null;
    let _enabled = true;
    let _volume = 0.6;

    function init() {
        if (ctx) return;
        try {
            ctx = new (window.AudioContext || window.webkitAudioContext)();
            masterGainNode = ctx.createGain();
            masterGainNode.gain.value = _volume;
            masterGainNode.connect(ctx.destination);
        } catch(e) {
            console.warn('UnoAudio: Web Audio API not available', e);
        }
    }

    function resume() {
        if (ctx && ctx.state === 'suspended') ctx.resume();
    }

    function tone(freq, type, startTime, duration, vol) {
        if (!ctx || !masterGainNode) return;
        vol = vol || 0.3;
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = type;
        osc.frequency.setValueAtTime(freq, startTime);
        gain.gain.setValueAtTime(vol, startTime);
        gain.gain.exponentialRampToValueAtTime(0.0001, startTime + duration);
        osc.connect(gain);
        gain.connect(masterGainNode);
        osc.start(startTime);
        osc.stop(startTime + duration + 0.02);
    }

    function sweep(freqStart, freqEnd, type, startTime, duration, vol) {
        if (!ctx || !masterGainNode) return;
        vol = vol || 0.3;
        const osc = ctx.createOscillator();
        const gain = ctx.createGain();
        osc.type = type;
        osc.frequency.setValueAtTime(freqStart, startTime);
        osc.frequency.exponentialRampToValueAtTime(Math.max(freqEnd, 20), startTime + duration);
        gain.gain.setValueAtTime(vol, startTime);
        gain.gain.exponentialRampToValueAtTime(0.0001, startTime + duration);
        osc.connect(gain);
        gain.connect(masterGainNode);
        osc.start(startTime);
        osc.stop(startTime + duration + 0.02);
    }

    function noiseBlip(startTime, duration, filterFreq, vol) {
        if (!ctx || !masterGainNode) return;
        filterFreq = filterFreq || 2000;
        vol = vol || 0.15;
        const bufLen = Math.ceil(ctx.sampleRate * duration);
        const buf = ctx.createBuffer(1, bufLen, ctx.sampleRate);
        const data = buf.getChannelData(0);
        for (let i = 0; i < bufLen; i++) data[i] = Math.random() * 2 - 1;

        const src = ctx.createBufferSource();
        src.buffer = buf;

        const filter = ctx.createBiquadFilter();
        filter.type = 'bandpass';
        filter.frequency.value = filterFreq;
        filter.Q.value = 1.0;

        const gain = ctx.createGain();
        gain.gain.setValueAtTime(vol, startTime);
        gain.gain.exponentialRampToValueAtTime(0.0001, startTime + duration);

        src.connect(filter);
        filter.connect(gain);
        gain.connect(masterGainNode);
        src.start(startTime);
        src.stop(startTime + duration + 0.02);
    }

    const sounds = {
        cardPlay() {
            const t = ctx.currentTime;
            sweep(500, 900, 'sine', t, 0.07, 0.38);
            noiseBlip(t + 0.01, 0.07, 3200, 0.14);
        },
        cardDraw() {
            const t = ctx.currentTime;
            sweep(320, 210, 'triangle', t, 0.13, 0.28);
            noiseBlip(t, 0.1, 1300, 0.09);
        },
        skip() {
            const t = ctx.currentTime;
            tone(680, 'square', t, 0.06, 0.28);
            tone(340, 'square', t + 0.07, 0.11, 0.22);
        },
        reverse() {
            const t = ctx.currentTime;
            sweep(850, 180, 'sine', t, 0.32, 0.38);
        },
        draw2() {
            const t = ctx.currentTime;
            [0, 0.14].forEach(function(d) {
                sweep(420, 300, 'triangle', t + d, 0.11, 0.24);
                noiseBlip(t + d, 0.09, 1900, 0.08);
            });
        },
        draw4() {
            const t = ctx.currentTime;
            [0, 0.1, 0.2, 0.3].forEach(function(d) {
                sweep(390, 260, 'triangle', t + d, 0.09, 0.20);
            });
            sweep(130, 55, 'sine', t, 0.48, 0.48);
        },
        wild() {
            const t = ctx.currentTime;
            [523, 659, 784, 1047].forEach(function(f, i) {
                tone(f, 'sine', t + i * 0.075, 0.2, 0.32);
            });
        },
        vortex() {
            const t = ctx.currentTime;
            sweep(90, 22, 'sawtooth', t, 2.2, 0.52);
            noiseBlip(t, 2.1, 260, 0.22);
            sweep(600, 100, 'sine', t + 0.3, 1.5, 0.18);
        },
        unoCall() {
            const t = ctx.currentTime;
            [392, 523, 784].forEach(function(f, i) {
                tone(f, 'square', t + i * 0.1, 0.24, 0.40);
            });
        },
        caught() {
            const t = ctx.currentTime;
            sweep(430, 90, 'sawtooth', t, 0.38, 0.48);
            tone(55, 'sine', t + 0.12, 0.32, 0.42);
        },
        win() {
            const t = ctx.currentTime;
            [523, 659, 784, 1047, 784, 1047, 1319].forEach(function(f, i) {
                tone(f, 'sine', t + i * 0.13, 0.24, 0.42);
            });
            [130, 165, 196, 261].forEach(function(f, i) {
                tone(f, 'triangle', t + i * 0.26, 0.34, 0.32);
            });
        },
        sevenSwap() {
            const t = ctx.currentTime;
            sweep(520, 1100, 'sine', t, 0.14, 0.48);
            noiseBlip(t + 0.06, 0.14, 4200, 0.14);
        },
        zeroRotate() {
            const t = ctx.currentTime;
            sweep(280, 860, 'sine', t, 0.55, 0.40);
            sweep(860, 280, 'sine', t + 0.55, 0.55, 0.32);
        },
        jumpIn() {
            const t = ctx.currentTime;
            tone(1100, 'square', t, 0.07, 0.44);
            tone(1550, 'square', t + 0.07, 0.08, 0.38);
        },
        challenge() {
            const t = ctx.currentTime;
            [180, 270, 400].forEach(function(f, i) {
                tone(f, 'sawtooth', t + i * 0.1, 0.20, 0.48);
            });
        },
        challengeBluffCaught() {
            const t = ctx.currentTime;
            [600, 800, 1000, 1200].forEach(function(f, i) {
                tone(f, 'square', t + i * 0.08, 0.16, 0.42);
            });
        },
        challengeFail() {
            const t = ctx.currentTime;
            sweep(320, 70, 'sawtooth', t, 0.55, 0.52);
            tone(55, 'sine', t + 0.2, 0.4, 0.38);
        },
        shuffle() {
            const t = ctx.currentTime;
            noiseBlip(t, 0.45, 2600, 0.30);
            tone(105, 'sine', t, 0.42, 0.32);
        },
        newRound() {
            const t = ctx.currentTime;
            [261, 329, 392, 523].forEach(function(f, i) {
                tone(f, 'sine', t + i * 0.11, 0.28, 0.42);
            });
        },
        pendingDraw() {
            const t = ctx.currentTime;
            sweep(200, 400, 'sine', t, 0.2, 0.3);
        }
    };

    return {
        init: function() { init(); },
        setEnabled: function(val) { _enabled = !!val; },
        setVolume: function(val) {
            _volume = Math.max(0, Math.min(1, val));
            if (masterGainNode) masterGainNode.gain.value = _volume;
        },
        getEnabled: function() { return _enabled; },
        play: function(soundName) {
            try {
                init();
                resume();
                if (!_enabled) return;
                if (sounds[soundName]) sounds[soundName]();
            } catch(e) {
                console.warn('UnoAudio play error:', soundName, e);
            }
        }
    };
})();
