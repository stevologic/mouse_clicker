# ClickForge

**A robust, no-install Windows auto clicker with AI-generated click & movement patterns.**

Tons of options for **how** to click, **where** to move the cursor, and **when** to fire — packaged as a single ~80 KB portable executable that runs on any Windows 10/11 machine with **no install and no runtime download**. Free and open source under the MIT License.

🌐 **Website:** https://stevologic.github.io/mouse_clicker/ · ⬇ **[Download the latest .exe](https://github.com/stevologic/mouse_clicker/releases/latest/download/ClickForge.exe)**

![ClickForge](docs/img/screenshot-click.png)

---

## Why it's different

- **Truly portable.** A single self-contained `.exe`. It runs on the .NET Framework that already ships with Windows — no installer, no admin rights, no 60 MB runtime bundle.
- **Extremely configurable.** Every click button and type, randomized hold/interval timing, four positioning modes, and three cursor-movement styles including humanized curves.
- **AI patterns.** Describe what you want in plain English and let Claude build the whole pattern. Works offline too.
- **Tiny & fast.** ~80 KB, native input via the Win32 `SendInput` API, precise sub-millisecond timing.

## Features

### How to click
- **Buttons:** left, right, middle, and side buttons (X1/X2)
- **Actions:** single, double, triple, N-click bursts, scroll up/down, or press-and-hold / release
- **Human-like hold:** randomized press-to-release time range

### When to click
- Fixed or randomized interval between events (with a live CPS estimate and quick 5–100 CPS presets)
- Run **until stopped**, for a **fixed number of clicks**, or for a **duration**
- Configurable start countdown

### Where to click
- **Current cursor** — click wherever the pointer is
- **Fixed point** — with an on-screen coordinate picker
- **Random in a region** — spray clicks inside a rectangle
- **Point sequence** — walk a looping list of captured points
- **Movement:** teleport, linear glide, or **humanized** curved Bézier travel with easing and micro-jitter
- Optional target jitter and return-to-origin

### Control
- System-wide **global hotkeys** (default `F6` = start/stop, `F8` = emergency stop) that work even in the background
- Save and load named **profiles**
- Multi-monitor aware, DPI-aware (true physical-pixel coordinates)

### AI pattern generator
Type a description like:

> *"Click like a human every 1–3 seconds near the center of the screen, with slight random movement, for 5 minutes."*

…and ClickForge asks **Claude** (via your own Anthropic API key) to translate it into a precise, ready-to-run pattern. No key? A built-in **offline generator** and one-click **presets** (rapid fire, human idle jiggle, gentle clicks, region spray, double-click spam) have you covered. Your key is stored locally under `%APPDATA%\ClickForge` and sent only to `api.anthropic.com`.

## Get started

### Download & run
1. Download [`ClickForge.exe`](https://github.com/stevologic/mouse_clicker/releases/latest/download/ClickForge.exe).
2. Double-click it. That's it — no install.
3. Configure your options (or open the **AI** tab and describe what you want), then press **F6** to start/stop from anywhere.

### Build from source
You only need Windows — the C# compiler ships with the .NET Framework, so there's no SDK to install.

```powershell
git clone https://github.com/stevologic/mouse_clicker
cd mouse_clicker
powershell -ExecutionPolicy Bypass -File build.ps1 -Run
```

`build.ps1` locates the Framework `csc.exe`, generates the app icon, and compiles everything in `src/` into `ClickForge.exe`. Or just run [`run.bat`](run.bat).

## How it works

| Area | Implementation |
| --- | --- |
| Clicking | Win32 `SendInput` for synthetic mouse buttons and wheel |
| Movement | `SetCursorPos` stepped along cubic Bézier paths with smootherstep easing |
| Timing | `Stopwatch`-based precision sleep blended with coarse sleep |
| Hotkeys | `RegisterHotKey` + `WM_HOTKEY`, handled in the form's `WndProc` |
| AI | Anthropic Messages API (`claude-opus-4-8` by default) over HTTPS, with an offline heuristic fallback |
| UI | Hand-built dark-themed WinForms — owner-drawn combos, no designer files |
| Persistence | JSON profiles under `%APPDATA%\ClickForge` |

The whole app is plain C# targeting .NET Framework 4.x, compiled with the in-box `csc.exe`. See [`src/`](src/).

## Requirements

- Windows 10 or 11 (.NET Framework 4.x is preinstalled)
- An Anthropic API key **only** if you want live AI generation (optional)

## Responsible use

ClickForge is a general-purpose desktop automation tool. Use it responsibly and only where automated input is permitted — **many games and online services prohibit automation**. It is not a cheat. Provided as-is under the MIT License, with no warranty.

## License

[MIT](LICENSE) — free to use, modify, and distribute.
