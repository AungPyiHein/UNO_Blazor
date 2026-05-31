---
name: UNO engine sound pattern
description: How OnSoundEffect is wired between UnoGame and GameBoard
---

`UnoGame.OnSoundEffect` is a `Func<string, Task>?` public field — NOT a C# event.

**Why:** Multiple subscribers would double-play sounds. A single assigned delegate is cleaner for this use case.

**How to apply:**
- GameBoard assigns it in `OnParametersSet`: `Game.OnSoundEffect = (s) => AudioService.PlayAsync(s);`
- GameBoard clears it in `Dispose`: `Game.OnSoundEffect = null;`
- Do NOT use `+=` / `-=` on it.
- Sound names map to `UnoAudio.play(name)` in `wwwroot/js/audio.js`.
