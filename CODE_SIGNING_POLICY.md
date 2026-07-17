# Code Signing Policy

mouseclicker.app (the [`stevologic/mouse_clicker`](https://github.com/stevologic/mouse_clicker)
project) signs its released `MouseClicker.exe` to protect its integrity and
authenticity and to identify the publisher to Windows.

## Certificate

Release binaries are signed with a certificate provided **free of charge by the
[SignPath Foundation](https://signpath.org/)**, using code signing by
[SignPath.io](https://signpath.io/). We gratefully acknowledge their support of
open-source software.

## Team roles

This project is maintained by **[stevologic](https://github.com/stevologic)**,
who fills all SignPath roles:

- **Author** — proposes and commits the changes to be released.
- **Reviewer** — reviews the source and the built artifact before signing.
- **Approver** — manually approves each individual signing request in SignPath.

All maintainers use **multi-factor authentication** for both their GitHub and
SignPath accounts.

## Build & signing process

- Releases are built from public source in **GitHub Actions on `windows-latest`**
  via [`build.ps1`](build.ps1) — no local or unreproducible steps.
- The unsigned artifact is submitted to SignPath, which signs it only after
  **manual approval of each release**. See
  [`.github/workflows/sign-release.yml`](.github/workflows/sign-release.yml).
- The signed `MouseClicker.exe` carries product-name and version metadata that
  match the project (`AssemblyInfo.cs`).

## Privacy policy

mouseclicker.app collects **no personal data**. It runs entirely on your machine
and makes **no network connections except those you explicitly initiate** — for
example, sending a prompt to an AI provider using *your own* API key, talking to
a local Ollama server you run, or the About tab's optional check of GitHub's
public releases API for a newer version (which sends nothing about you). Settings
and recordings are stored locally under `%APPDATA%\MouseClicker`. There is **no
telemetry and no auto-update** (update *checks* are link-only). The program
**transfers no information to us or any third party without your consent.**

**About the optional keyboard recording.** The Record feature can, *only when you
tick the off-by-default "Also record keystrokes" checkbox*, capture keystrokes so
a macro can reproduce them. This is standard macro-recorder behavior, never
enabled without your action, and the captured input **stays on your machine** —
it is only written to a local recording file you choose to save and is never
transmitted. It is not a keylogger: nothing is captured unless you start a
recording with that option on.

## Uninstalling

mouseclicker.app is a single portable executable — nothing is installed
system-wide, no registry keys, no services. To remove it completely:

1. Delete `MouseClicker.exe`.
2. *(Optional)* Delete the settings folder `%APPDATA%\MouseClicker`.
