# ClickForge

**A robust, no-install Windows auto clicker with AI-generated click & movement patterns.**

Tons of options for **how** to click, **where** to move the cursor, and **when** to fire — wrapped in a fully animated interface and packaged as a single ~100 KB portable executable that runs on any Windows 10/11 machine with **no install and no runtime download**. Free and open source under the MIT License.

🌐 **Website:** https://stevologic.github.io/mouse_clicker/ · ⬇ **[Download the latest .exe](https://github.com/stevologic/mouse_clicker/releases/latest/download/ClickForge.exe)**

![ClickForge](docs/img/screenshot-click.png)

---

## Why it's different

- **Truly portable.** A single self-contained `.exe`. It runs on the .NET Framework that already ships with Windows — no installer, no admin rights, no 60 MB runtime bundle.
- **Extremely configurable.** Every click button and type, randomized hold/interval timing, four positioning modes, and three cursor-movement styles including humanized curves.
- **AI patterns, your choice of model.** Describe what you want in plain English and let **Claude, OpenAI, Gemini, or a lightweight local model** build the whole pattern. The local option runs fully offline via [Ollama](https://ollama.com) — no key, no cloud. Works with a built-in heuristic generator too.
- **Live activity HUD.** While a run is active, a sleek floating popup shows a pulsing indicator and a live click counter — always on top and click-through, so it never gets in the way.
- **A living interface.** A hand-built animated UI — a cursor-reactive particle constellation, an aurora backdrop, glassmorphism, and a glowing Start button — rendered entirely in GDI+. It idles to near-zero CPU when the window isn't in the foreground.
- **Tiny & fast.** ~100 KB, native input via the Win32 `SendInput` API, precise sub-millisecond timing.

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

…pick your provider — **Claude (Anthropic), OpenAI, Gemini (Google), or a local model** — and ClickForge translates it into a precise, ready-to-run pattern. The model field is editable, so any current model id works.

- **Local, no key, no cloud:** select **Local (Ollama)** to run a lightweight model right on your machine. Install [Ollama](https://ollama.com), pull a tiny model (e.g. `ollama pull qwen2.5:0.5b`, ~400 MB), and generate patterns fully offline. Leave the key field blank for `http://localhost:11434` or point it at a custom server.
- **Cloud:** enter your own API key for Claude, OpenAI, or Gemini. Keys are stored locally under `%APPDATA%\ClickForge` and sent only to the provider you choose.
- **No key or model at all?** A built-in **offline generator** and one-click **presets** (rapid fire, human idle jiggle, gentle clicks, region spray, double-click spam) have you covered.

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
| AI | Anthropic / OpenAI / Google APIs, or a local model via the Ollama HTTP API (`localhost:11434`), with an offline heuristic fallback |
| Activity HUD | Topmost per-pixel-alpha layered window (`UpdateLayeredWindow`), click-through |
| UI | Hand-built dark-themed WinForms — owner-drawn combos, no designer files |
| Persistence | JSON profiles under `%APPDATA%\ClickForge` |

The whole app is plain C# targeting .NET Framework 4.x, compiled with the in-box `csc.exe`. See [`src/`](src/).

## Requirements

- Windows 10 or 11 (.NET Framework 4.x is preinstalled)
- An API key **only** if you want live cloud AI generation (optional) — or run a local model, or use the offline generator

## Is it safe? (Windows SmartScreen)

When you first run the download, Windows may show **“Windows protected your PC — Microsoft Defender SmartScreen prevented an unrecognized app from starting.”**

**This is not a virus warning.** Microsoft Defender does *not* flag ClickForge as malware — a Defender scan comes back clean. SmartScreen shows this prompt for any executable that is **unsigned** and doesn’t yet have download “reputation.” Code-signing certificates cost money each year, and this is a free, open-source hobby project, so the release build isn’t signed (yet).

You have a few options, from most to least cautious:

1. **Build it yourself.** The source is all here and it compiles with the C# compiler already in Windows — no SDK needed. A locally built exe carries no “mark of the web,” so there’s **no SmartScreen prompt at all**:
   ```powershell
   git clone https://github.com/stevologic/mouse_clicker
   cd mouse_clicker
   powershell -ExecutionPolicy Bypass -File build.ps1 -Run
   ```
2. **Verify the download, then run it.** Every release lists a **SHA-256** checksum. Confirm your download matches before running:
   ```powershell
   Get-FileHash .\ClickForge.exe -Algorithm SHA256
   ```
3. **Run past the prompt.** On the SmartScreen dialog, click **More info → Run anyway**. (Or right-click `ClickForge.exe` → **Properties** → tick **Unblock** → **OK** before launching.)

Because it’s an auto-clicker (it synthesizes mouse input), some **third-party** antivirus tools may heuristically label it a “PUA/auto-clicker.” It only does what you configure — the full source is here to audit, and any detection can be reported to your vendor as a false positive.

> The only way to remove the SmartScreen prompt entirely is to sign the build with a trusted (ideally EV) code-signing certificate. If you’d like to sponsor/provide one, the signing step is easy to add to `build.ps1`.

## Responsible use

ClickForge is a general-purpose desktop automation tool. Use it responsibly and only where automated input is permitted — **many games and online services prohibit automation**. It is not a cheat. Provided as-is under the MIT License, with no warranty.

## License

[MIT](LICENSE) — free to use, modify, and distribute.
